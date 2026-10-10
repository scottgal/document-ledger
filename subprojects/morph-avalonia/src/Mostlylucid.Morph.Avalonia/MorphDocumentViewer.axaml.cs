using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Shapes;
using global::Avalonia.Input;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Threading;
using Mostlylucid.Morph.Documents;
using Path = System.IO.Path;

namespace Mostlylucid.Morph.Avalonia;

/// <summary>Native page viewer with search and host-provided evidence overlays.</summary>
public partial class MorphDocumentViewer : UserControl, IAsyncDisposable
{
    private DocumentSession? _session;
    private Bitmap? _bitmap;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _pageCancellation;
    private CancellationTokenSource? _searchCancellation;
    private IReadOnlyList<DocumentHighlight> _highlights = [];
    private IReadOnlyList<DocumentMatch> _matches = [];
    private int _matchIndex = -1;
    private string? _activeHighlightId;
    private int _generation;
    private double _zoom = 1;
    private bool _fitWidth = true;
    private bool _disposed;
    private Point? _dragStart;
    private Rectangle? _dragRectangle;

    public MorphDocumentViewer()
    {
        InitializeComponent();
        PreviousButton.Click += async (_, _) => await UiOperationAsync(() => GoToPageAsync(CurrentPageIndex - 1));
        NextButton.Click += async (_, _) => await UiOperationAsync(() => GoToPageAsync(CurrentPageIndex + 1));
        ZoomOutButton.Click += (_, _) => SetZoom(_zoom / 1.25);
        ZoomInButton.Click += (_, _) => SetZoom(_zoom * 1.25);
        FitButton.Click += (_, _) => { _fitWidth = true; ResizePage(); };
        SearchButton.Click += async (_, _) => await UiOperationAsync(() => FindAsync(SearchBox.Text ?? ""));
        NextMatchButton.Click += async (_, _) => await UiOperationAsync(NextMatchAsync);
        SearchBox.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await UiOperationAsync(() => FindAsync(SearchBox.Text ?? ""));
        };
        PageScroll.SizeChanged += (_, _) => { if (_fitWidth) ResizePage(); };
        Overlay.PointerPressed += OnPointerPressed;
        Overlay.PointerMoved += OnPointerMoved;
        Overlay.PointerReleased += OnPointerReleased;
        Overlay.PointerCaptureLost += (_, _) => CancelDrag();
        UpdateToolbar();
    }

    public int CurrentPageIndex { get; private set; }
    public int PageCount => _session?.PageCount ?? 0;
    public string? SourcePath => _session?.SourcePath;
    public string? RepresentationHash => _session?.RepresentationHash;
    public double Zoom => _zoom;
    public DocumentRenderOptions RenderOptions { get; set; } = new();
    public event EventHandler? DocumentOpened;
    public event EventHandler<Exception>? OperationFailed;
    public event EventHandler<DocumentHighlight>? HighlightSelected;
    public event EventHandler<DocumentSelection>? RegionSelected;

    /// <summary>Retrieve evidence without changing the viewer's search or page state.</summary>
    public Task<IReadOnlyList<DocumentMatch>> SearchEvidenceAsync(string query, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        return _session?.SearchAsync(query, cancellationToken)
            ?? Task.FromResult<IReadOnlyList<DocumentMatch>>([]);
    }

    public Task<string> GetPageTextAsync(int pageIndex, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        return _session?.GetPageTextAsync(pageIndex, cancellationToken) ?? Task.FromResult("");
    }

    public Task<string> GetEvidenceTextAsync(int pageIndex, PageRegion region, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        return _session?.GetTextInRegionAsync(pageIndex, region, cancellationToken) ?? Task.FromResult("");
    }

    public Task SaveRepresentationAsync(string path, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        return _session?.SaveRepresentationAsync(path, cancellationToken) ?? throw new InvalidOperationException("No document is open.");
    }

    /// <summary>Call on the UI thread. A newer load cancels and supersedes this request.</summary>
    public async Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        Cancel(ref _loadCancellation);
        Cancel(ref _pageCancellation);
        Cancel(ref _searchCancellation);
        var generation = ++_generation;
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _loadCancellation.Token;
        StatusLabel.Text = $"Opening {Path.GetFileName(path)}…";
        DocumentSession? opened = null;
        try
        {
            opened = await DocumentSession.OpenAsync(path, RenderOptions, token);
            token.ThrowIfCancellationRequested();
            if (generation != _generation) return;
            // Load and decode the first page before replacing the currently visible document.
            var image = await opened.RenderPageAsync(0, cancellationToken: token);
            token.ThrowIfCancellationRequested();
            using var stream = new MemoryStream(image);
            var bitmap = new Bitmap(stream);
            var previous = _session;
            _session = opened;
            opened = null;
            _matches = [];
            _highlights = [];
            _activeHighlightId = null;
            _matchIndex = -1;
            CurrentPageIndex = 0;
            ReplaceBitmap(bitmap);
            ResizePage();
            UpdateToolbar();
            StatusLabel.Text = $"{Path.GetFileName(path)} · {PageCount} pages" +
                (_session.FontSubstitutions.Count == 0 ? "" : $" · substituted fonts: {string.Join(", ", _session.FontSubstitutions)}");
            DocumentOpened?.Invoke(this, EventArgs.Empty);
            if (previous is not null) await previous.DisposeAsync();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            if (generation == _generation) ReportFailure(exception);
            throw;
        }
        finally
        {
            if (opened is not null) await opened.DisposeAsync();
            if (generation == _generation && token.IsCancellationRequested)
                StatusLabel.Text = "Opening cancelled.";
        }
    }

    public async Task GoToPageAsync(int index, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        var session = _session;
        if (session is null || index < 0 || index >= session.PageCount) return;
        Cancel(ref _pageCancellation);
        _pageCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _pageCancellation.Token;
        var generation = _generation;
        var png = await session.RenderPageAsync(index, cancellationToken: token);
        token.ThrowIfCancellationRequested();
        if (generation != _generation || session != _session) return;
        using var stream = new MemoryStream(png);
        var bitmap = new Bitmap(stream);
        CurrentPageIndex = index;
        CancelDrag();
        ReplaceBitmap(bitmap);
        ResizePage();
        PageScroll.Offset = default;
        UpdateToolbar();
    }

    public void SetZoom(double zoom)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!double.IsFinite(zoom)) throw new ArgumentOutOfRangeException(nameof(zoom));
        _fitWidth = false;
        _zoom = Math.Clamp(zoom, 0.25, 3);
        ResizePage();
    }

    public void SetHighlights(IEnumerable<DocumentHighlight> highlights)
    {
        Dispatcher.UIThread.VerifyAccess();
        _highlights = highlights.ToArray();
        DrawOverlays();
    }

    public async Task ShowHighlightAsync(string id, CancellationToken cancellationToken = default)
    {
        var highlight = _highlights.FirstOrDefault(h => h.Id == id);
        if (highlight is null) return;
        _activeHighlightId = id;
        await GoToPageAsync(highlight.PageIndex, cancellationToken);
        ScrollTo(highlight.Region);
    }

    public async Task FindAsync(string query, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        Cancel(ref _searchCancellation);
        _searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _searchCancellation.Token;
        var session = _session;
        if (session is null) return;
        if (string.IsNullOrWhiteSpace(query))
        {
            _matches = [];
            _matchIndex = -1;
            DrawOverlays();
            StatusLabel.Text = "Search cleared.";
            return;
        }
        StatusLabel.Text = "Searching…";
        var matches = await session.SearchAsync(query, token);
        token.ThrowIfCancellationRequested();
        if (session != _session) return;
        _matches = matches;
        _matchIndex = -1;
        if (matches.Count == 0)
        {
            StatusLabel.Text = "No matches. Scanned pages may need OCR.";
            DrawOverlays();
        }
        else await NextMatchAsync();
    }

    public async Task NextMatchAsync()
    {
        if (_matches.Count == 0) return;
        _matchIndex = (_matchIndex + 1) % _matches.Count;
        var match = _matches[_matchIndex];
        await GoToPageAsync(match.PageIndex);
        if (match.Regions.Count > 0) ScrollTo(match.Regions[0]);
        StatusLabel.Text = $"Match {_matchIndex + 1} of {_matches.Count}{(_matches.Count == 2_000 ? " (search limit)" : "")}";
    }

    private void ReplaceBitmap(Bitmap bitmap)
    {
        var previous = _bitmap;
        _bitmap = bitmap;
        PageImage.Source = bitmap;
        PageSurface.IsVisible = true;
        previous?.Dispose();
    }

    private void ResizePage()
    {
        if (_session is null || _bitmap is null) return;
        var page = _session.Pages[CurrentPageIndex];
        var naturalWidth = page.WidthPoints * 96 / 72;
        if (_fitWidth && PageScroll.Bounds.Width > 80)
            _zoom = Math.Clamp((PageScroll.Bounds.Width - 64) / naturalWidth, 0.25, 3);
        PageSurface.Width = naturalWidth * _zoom;
        PageSurface.Height = page.HeightPoints * 96 / 72 * _zoom;
        ZoomLabel.Text = $"{_zoom:P0}";
        DrawOverlays();
    }

    private void DrawOverlays()
    {
        CancelDrag();
        Overlay.Children.Clear();
        foreach (var match in _matches.Where(m => m.PageIndex == CurrentPageIndex))
            foreach (var region in match.Regions)
                AddRectangle(region, Color.Parse("#70FBBF24"), Colors.DarkGoldenrod);
        foreach (var highlight in _highlights.Where(h => h.PageIndex == CurrentPageIndex))
        {
            var active = highlight.Id == _activeHighlightId;
            var rectangle = AddRectangle(highlight.Region,
                Color.Parse(active ? "#8034D399" : "#3042B8FF"),
                Color.Parse(active ? "#00875A" : "#248AD0"));
            rectangle.StrokeThickness = active ? 2 : 1;
            ToolTip.SetTip(rectangle, highlight.Label ?? highlight.Id);
            rectangle.PointerPressed += (_, e) =>
            {
                if (SelectButton.IsChecked == true) return;
                HighlightSelected?.Invoke(this, highlight);
                e.Handled = true;
            };
        }
    }

    private Rectangle AddRectangle(PageRegion region, Color fill, Color stroke)
    {
        var rectangle = new Rectangle { Fill = new SolidColorBrush(fill), Stroke = new SolidColorBrush(stroke),
            StrokeThickness = 1.5, Width = region.Width * PageSurface.Width, Height = region.Height * PageSurface.Height };
        Canvas.SetLeft(rectangle, region.X * PageSurface.Width);
        Canvas.SetTop(rectangle, region.Y * PageSurface.Height);
        Overlay.Children.Add(rectangle);
        return rectangle;
    }

    private Point Constrain(Point point) => new(Math.Clamp(point.X, 0, PageSurface.Width), Math.Clamp(point.Y, 0, PageSurface.Height));

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_session is null || SelectButton.IsChecked != true || !e.GetCurrentPoint(Overlay).Properties.IsLeftButtonPressed) return;
        _dragStart = Constrain(e.GetPosition(Overlay));
        _dragRectangle = AddRectangle(new(0, 0, 0, 0), Color.Parse("#6034D399"), Colors.SeaGreen);
        e.Pointer.Capture(Overlay);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start || _dragRectangle is null) return;
        var current = Constrain(e.GetPosition(Overlay));
        Canvas.SetLeft(_dragRectangle, Math.Min(start.X, current.X));
        Canvas.SetTop(_dragRectangle, Math.Min(start.Y, current.Y));
        _dragRectangle.Width = Math.Abs(current.X - start.X);
        _dragRectangle.Height = Math.Abs(current.Y - start.Y);
    }

    private async void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragStart is not { } start || _session is not { } session) return;
        var end = Constrain(e.GetPosition(Overlay));
        var pageIndex = CurrentPageIndex;
        var region = new PageRegion(Math.Min(start.X, end.X) / PageSurface.Width, Math.Min(start.Y, end.Y) / PageSurface.Height,
            Math.Abs(end.X - start.X) / PageSurface.Width, Math.Abs(end.Y - start.Y) / PageSurface.Height);
        CancelDrag();
        e.Pointer.Capture(null);
        if (region.Width * PageSurface.Width < 3 || region.Height * PageSurface.Height < 3) return;
        await UiOperationAsync(async () =>
        {
            var text = await session.GetTextInRegionAsync(pageIndex, region);
            if (session != _session) return;
            StatusLabel.Text = string.IsNullOrWhiteSpace(text) ? "Selected region contains no embedded text." : $"Selected: {text.Trim()}";
            RegionSelected?.Invoke(this, new(pageIndex, region, text));
        });
    }

    private void CancelDrag()
    {
        _dragStart = null;
        if (_dragRectangle is not null) Overlay.Children.Remove(_dragRectangle);
        _dragRectangle = null;
    }

    private void ScrollTo(PageRegion region) => PageScroll.Offset = new(0, Math.Max(0, region.Y * PageSurface.Height - 100));

    private void UpdateToolbar()
    {
        PageLabel.Text = PageCount == 0 ? "No document" : $"Page {CurrentPageIndex + 1} of {PageCount}";
        PreviousButton.IsEnabled = PageCount > 0 && CurrentPageIndex > 0;
        NextButton.IsEnabled = CurrentPageIndex + 1 < PageCount;
        foreach (var control in new Control[] { ZoomInButton, ZoomOutButton, FitButton, SearchBox, SearchButton, NextMatchButton, SelectButton })
            control.IsEnabled = PageCount > 0;
    }

    private async Task UiOperationAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { ReportFailure(exception); }
    }

    private void ReportFailure(Exception exception)
    {
        StatusLabel.Text = exception.Message;
        OperationFailed?.Invoke(this, exception);
    }

    private static void Cancel(ref CancellationTokenSource? source)
    {
        source?.Cancel();
        source?.Dispose();
        source = null;
    }

    public async ValueTask DisposeAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        ++_generation;
        Cancel(ref _loadCancellation);
        Cancel(ref _pageCancellation);
        Cancel(ref _searchCancellation);
        var session = _session;
        _session = null;
        PageImage.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        PageSurface.IsVisible = false;
        _matches = [];
        _highlights = [];
        DrawOverlays();
        UpdateToolbar();
        if (session is not null) await session.DisposeAsync();
    }
}
