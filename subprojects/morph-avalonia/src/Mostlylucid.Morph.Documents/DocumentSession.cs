using Morph;
using Morph.PDFium;

namespace Mostlylucid.Morph.Documents;

/// <summary>
/// Owns a read-only PDF rendering session. Word and HTML become an in-memory PDF through Morph;
/// page images and text geometry always refer to that same representation.
/// </summary>
public sealed class DocumentSession : IAsyncDisposable
{
    private readonly PdfiumDocument _document;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PageTransform[] _transforms;
    private bool _disposed;

    private DocumentSession(string sourcePath, byte[] pdfBytes, IReadOnlyList<string> fontSubstitutions)
    {
        SourcePath = sourcePath;
        FontSubstitutions = fontSubstitutions;
        _document = PdfiumDocument.Load(pdfBytes);
        try
        {
            var pages = new List<DocumentPage>();
            _transforms = new PageTransform[_document.PageCount];
            for (var i = 0; i < _document.PageCount; i++)
            {
                using var page = _document.LoadPage(i);
                pages.Add(new(i, page.Size.Width, page.Size.Height));
                _transforms[i] = new(page.GetBoundingBox(), page.Rotation);
            }
            Pages = pages.AsReadOnly();
        }
        catch
        {
            _document.Dispose();
            throw;
        }
    }

    public string SourcePath { get; }
    public IReadOnlyList<string> FontSubstitutions { get; }
    public IReadOnlyList<DocumentPage> Pages { get; }
    public int PageCount => Pages.Count;

    public static Task<DocumentSession> OpenAsync(string path, CancellationToken cancellationToken = default) =>
        OpenAsync(path, new DocumentRenderOptions(), cancellationToken);

    public static Task<DocumentSession> OpenAsync(string path, DocumentRenderOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(path);
            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            if (extension is not (".pdf" or ".docx" or ".html" or ".htm"))
                throw new NotSupportedException("Open a PDF, DOCX, HTML or HTM file.");
            var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
            var substitutions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var exportOptions = new PdfExportOptions
            {
                FontDirectory = options.FontDirectory,
                DefaultFont = options.FallbackFont,
                FontWidthScale = options.FontWidthScale,
                FontFallback = family => { substitutions.Add(family); return options.FallbackFont; }
            };
            byte[] pdf;
            if (extension == ".pdf")
                pdf = bytes;
            else if (extension == ".docx")
            {
                using var source = new MemoryStream(bytes, writable: false);
                pdf = new WordDocument(source, options.FallbackFont).ExportToPdf(exportOptions);
            }
            else
            {
                // The renderer parses content; it does not execute scripts or host a browser.
                using var source = new StreamReader(new MemoryStream(bytes, writable: false));
                var html = await source.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                var document = await HtmlDocument.LoadAsync(html, cancellationToken).ConfigureAwait(false);
                pdf = document.ExportToPdf(exportOptions);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var session = new DocumentSession(fullPath, pdf, substitutions.Order(StringComparer.OrdinalIgnoreCase).ToArray());
            if (cancellationToken.IsCancellationRequested)
            {
                await session.DisposeAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            return session;
        }, cancellationToken);
    }

    public Task<byte[]> RenderPageAsync(int pageIndex, int dpi = 120, CancellationToken cancellationToken = default)
    {
        ValidatePage(pageIndex);
        if (dpi is < 36 or > 240)
            throw new ArgumentOutOfRangeException(nameof(dpi), "Render DPI must be between 36 and 240.");
        // Limit a bitmap to roughly 64 MB. A huge page must not exhaust the desktop process.
        var page = Pages[pageIndex];
        var safeDpi = (int)Math.Min(dpi, Math.Floor(72 * Math.Sqrt(16_000_000 / (page.WidthPoints * page.HeightPoints))));
        if (safeDpi < 1)
            throw new InvalidDataException("The page dimensions exceed the render budget.");
        return RunAsync(() => _document.RenderPage(pageIndex, dpi: safeDpi), cancellationToken);
    }

    public Task<string> GetPageTextAsync(int pageIndex, CancellationToken cancellationToken = default)
    {
        ValidatePage(pageIndex);
        return RunAsync(() => WithTextPage(pageIndex, page => page.GetText() ?? ""), cancellationToken);
    }

    public Task<string> GetTextInRegionAsync(int pageIndex, PageRegion region, CancellationToken cancellationToken = default)
    {
        ValidatePage(pageIndex);
        return RunAsync(() => WithTextPage(pageIndex, page =>
        {
            var rectangle = _transforms[pageIndex].ToPdf(region);
            // PDFium's bounded-text boundary test can drop an edge glyph after a
            // normalized-coordinate roundtrip. A sub-point tolerance preserves it.
            const double tolerance = 0.25;
            return page.GetTextInRectangle(new(rectangle.Left - tolerance, rectangle.Bottom - tolerance,
                rectangle.Right + tolerance, rectangle.Top + tolerance)) ?? "";
        }), cancellationToken);
    }

    public Task<IReadOnlyList<DocumentMatch>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return RunAsync<IReadOnlyList<DocumentMatch>>(() =>
        {
            var results = new List<DocumentMatch>();
            for (var index = 0; index < PageCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WithTextPage(index, page =>
                {
                    foreach (var match in page.Search(query))
                    {
                        var regions = page.GetTextRectangles(match.CharIndex, match.CharCount)
                            .Select(_transforms[index].FromPdf).ToArray();
                        results.Add(new(index, match.CharIndex, match.CharCount,
                            page.GetText(match.CharIndex, match.CharCount) ?? query, regions));
                        if (results.Count >= 2_000) break;
                    }
                    return 0;
                });
                if (results.Count >= 2_000) return results;
            }
            return results;
        }, cancellationToken);
    }

    private T WithTextPage<T>(int index, Func<PdfPage, T> action)
    {
        using var page = _document.LoadPage(index);
        var rotation = page.Rotation;
        // PDFium's text ordering and merged rectangles can change at 180/270 degrees.
        // Extract in unrotated page space, then apply our explicit display transform.
        // The session gate keeps renders from observing this temporary rotation.
        page.Rotation = PageRotation.None;
        try { return action(page); }
        finally { page.Rotation = rotation; }
    }

    private void ValidatePage(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (index < 0 || index >= PageCount)
            throw new ArgumentOutOfRangeException(nameof(index));
    }

    private async Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var result = await Task.Run(action, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            _document.Dispose();
        }
        finally { _gate.Release(); }
    }

    private sealed record PageTransform(PdfRectangle Bounds, PageRotation Rotation)
    {
        private (double X, double Y) Forward(double x, double y)
        {
            var u = (x - Bounds.Left) / Bounds.Width;
            var v = (Bounds.Top - y) / Bounds.Height;
            return Rotation switch
            {
                PageRotation.Clockwise90 => (1 - v, u),
                PageRotation.Clockwise180 => (1 - u, 1 - v),
                PageRotation.Clockwise270 => (v, 1 - u),
                _ => (u, v)
            };
        }

        private (double X, double Y) Inverse(double x, double y)
        {
            var (u, v) = Rotation switch
            {
                PageRotation.Clockwise90 => (y, 1 - x),
                PageRotation.Clockwise180 => (1 - x, 1 - y),
                PageRotation.Clockwise270 => (1 - y, x),
                _ => (x, y)
            };
            return (Bounds.Left + u * Bounds.Width, Bounds.Top - v * Bounds.Height);
        }

        public PageRegion FromPdf(PdfRectangle rectangle)
        {
            var corners = new[] { Forward(rectangle.Left, rectangle.Top), Forward(rectangle.Right, rectangle.Top),
                Forward(rectangle.Left, rectangle.Bottom), Forward(rectangle.Right, rectangle.Bottom) };
            var left = Math.Clamp(corners.Min(p => p.X), 0, 1);
            var top = Math.Clamp(corners.Min(p => p.Y), 0, 1);
            var right = Math.Clamp(corners.Max(p => p.X), left, 1);
            var bottom = Math.Clamp(corners.Max(p => p.Y), top, 1);
            return new(left, top, right - left, bottom - top);
        }

        public PdfRectangle ToPdf(PageRegion rectangle)
        {
            var corners = new[] { Inverse(rectangle.X, rectangle.Y), Inverse(rectangle.X + rectangle.Width, rectangle.Y),
                Inverse(rectangle.X, rectangle.Y + rectangle.Height), Inverse(rectangle.X + rectangle.Width, rectangle.Y + rectangle.Height) };
            return new(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Max(p => p.X), corners.Max(p => p.Y));
        }
    }
}
