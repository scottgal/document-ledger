using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Morph;
using Morph.Avalonia.Demo;
using Morph.PDFium;
using Mostlylucid.Morph.Avalonia;
using Mostlylucid.Morph.Documents;

if (args.Length >= 2 && args[0] == "--suggest")
{
    async Task DiagnoseAsync()
    {
        await using var document = await DocumentSession.OpenAsync(args[1]);
        var text = await document.GetPageTextAsync(0);
        var fields = await LocalTemplateStarter.SuggestAsync("http://localhost:11434", args.Length >= 3 ? args[2] : "qwen3.5:0.8b", text);
        foreach (var field in fields)
        {
            var values = await document.SearchAsync(field.Value);
            var anchors = await document.SearchAsync(field.Anchor);
            Console.WriteLine($"{field.Label}: anchor=[{field.Anchor}] ({anchors.Count}), value=[{field.Value}] ({values.Count}), section=[{field.Section}]");
        }
        Console.WriteLine($"{fields.Count} proposed fields.");
    }
    DiagnoseAsync().GetAwaiter().GetResult();
    return;
}

if (args.Length >= 2 && args[0] == "--dataset")
{
    DatasetInspection.RunAsync(args[1], Path.GetFullPath(args.Length >= 3 ? args[2] : "artifacts/dataset")).GetAwaiter().GetResult();
    return;
}

var directory = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/verification");
Directory.CreateDirectory(directory);
var wordPath = SampleDocument.Create(directory);
var htmlPath = SampleDocument.CreateHtml(directory);
var pdfPath = Path.Combine(directory, "sample-application.pdf");
new WordDocument(wordPath).ExportToPdf(pdfPath, new PdfExportOptions
{
    FontDirectory = DocumentRenderOptions.BundledFontDirectory,
    DefaultFont = "Noto Sans",
    FontFallback = _ => "Noto Sans"
});
var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException($"FAILED: {description}");
    checks++;
    Console.WriteLine($"PASS {description}");
}

PageRegion Union(IReadOnlyList<PageRegion> regions)
{
    var left = regions.Min(r => r.X);
    var top = regions.Min(r => r.Y);
    return new(left, top, regions.Max(r => r.X + r.Width) - left,
        regions.Max(r => r.Y + r.Height) - top);
}

bool HasTextInk(byte[] png, PageRegion region)
{
    using var bitmap = SkiaSharp.SKBitmap.Decode(png);
    var left = Math.Clamp((int)(region.X * bitmap.Width), 0, bitmap.Width - 1);
    var top = Math.Clamp((int)(region.Y * bitmap.Height), 0, bitmap.Height - 1);
    var right = Math.Clamp((int)Math.Ceiling((region.X + region.Width) * bitmap.Width), left + 1, bitmap.Width);
    var bottom = Math.Clamp((int)Math.Ceiling((region.Y + region.Height) * bitmap.Height), top + 1, bitmap.Height);
    var count = 0;
    for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.Red < 100 && pixel.Green < 100 && pixel.Blue < 100) count++;
        }
    return count >= 5;
}

async Task VerifyDocumentsAsync()
{
    foreach (var path in new[] { wordPath, pdfPath, htmlPath })
    {
        await using var document = await DocumentSession.OpenAsync(path);
        Check(document.PageCount >= (path == htmlPath ? 1 : 2), $"{Path.GetExtension(path)} pagination");
        var png = await document.RenderPageAsync(0);
        Check(png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), $"{Path.GetExtension(path)} native PDFium PNG rendering");
        File.WriteAllBytes(Path.Combine(directory, Path.GetFileName(path) + ".png"), png);
        Check((await document.GetPageTextAsync(0)).Contains("Alex Morgan"), $"{Path.GetExtension(path)} text extraction");
        var matches = await document.SearchAsync("Alex Morgan");
        Check(matches.Count >= 1 && matches[0].Regions.Count > 0, $"{Path.GetExtension(path)} searchable text geometry");
        var match = matches[0];
        var text = await document.GetTextInRegionAsync(match.PageIndex, Union(match.Regions));
        if (!text.Contains("Alex")) Console.WriteLine($"Geometry debug: {match.Regions[0]} => '{text}'");
        Check(text.Contains("Alex"), $"{Path.GetExtension(path)} geometry maps back to source text");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await document.RenderPageAsync(0, cancellationToken: cancelled.Token); Check(false, "cancelled render"); }
        catch (OperationCanceledException) { Check(true, "cancelled render"); }
    }

    foreach (var cropped in new[] { false, true })
    foreach (var rotation in new[] { PageRotation.None, PageRotation.Clockwise90, PageRotation.Clockwise180, PageRotation.Clockwise270 })
    {
        var path = Path.Combine(directory, $"rotation-{rotation}-crop-{cropped}.pdf");
        using (var pdf = PdfSharp.Pdf.IO.PdfReader.Open(pdfPath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify))
        {
            pdf.Pages[0].Rotate = (int)rotation * 90;
            if (cropped) pdf.Pages[0].CropBox = new PdfSharp.Pdf.PdfRectangle(
                new PdfSharp.Drawing.XPoint(40, 40), new PdfSharp.Drawing.XPoint(555, 800));
            pdf.Save(path);
        }
        await using var document = await DocumentSession.OpenAsync(path);
        var match = (await document.SearchAsync("Alex Morgan"))[0];
        var text = await document.GetTextInRegionAsync(0, Union(match.Regions));
        Check(text.Contains("Alex"), $"rotation {rotation}, crop {cropped} evidence coordinate roundtrip");
        Check(HasTextInk(await document.RenderPageAsync(0), Union(match.Regions)),
            $"rotation {rotation}, crop {cropped} evidence intersects rendered text");
    }

    var badPath = Path.Combine(directory, "invalid.pdf");
    File.WriteAllText(badPath, "Not a PDF");
    try { await using var invalid = await DocumentSession.OpenAsync(badPath); Check(false, "invalid PDF rejected"); }
    catch (PdfiumException) { Check(true, "invalid PDF rejected"); }

    var disposed = await DocumentSession.OpenAsync(pdfPath);
    await disposed.DisposeAsync();
    try { await disposed.GetPageTextAsync(0); Check(false, "disposed session rejected"); }
    catch (ObjectDisposedException) { Check(true, "disposed session rejected"); }
    try { _ = new PageRegion(-0.1, 0, 0.5, 0.5); Check(false, "invalid evidence region rejected"); }
    catch (ArgumentOutOfRangeException) { Check(true, "invalid evidence region rejected"); }
}

VerifyDocumentsAsync().GetAwaiter().GetResult();
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();

void Pump(Task task)
{
    var deadline = DateTime.UtcNow.AddSeconds(60);
    while (!task.IsCompleted)
    {
        if (DateTime.UtcNow > deadline) throw new TimeoutException("UI verification exceeded 60 seconds.");
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(5);
    }
    Dispatcher.UIThread.RunJobs();
    task.GetAwaiter().GetResult();
}

var viewer = new MorphDocumentViewer();
var window = new Window { Width = 1100, Height = 850, Content = viewer };
window.Show();
Pump(viewer.OpenAsync(wordPath));
Check(viewer.PageCount >= 2, "Avalonia viewer opens Word");
Pump(viewer.GoToPageAsync(1));
Check(viewer.CurrentPageIndex == 1, "Avalonia page navigation");
viewer.SetZoom(1.5);
Check(viewer.Zoom == 1.5, "Avalonia zoom");
Pump(viewer.FindAsync("Alex Morgan"));
Check(viewer.CurrentPageIndex == 0, "Avalonia search navigates to first match");
Pump(viewer.NextMatchAsync());
Check(viewer.CurrentPageIndex == 1, "Avalonia search navigates across pages");
viewer.SetHighlights([new("ledger-1", 0, new PageRegion(0.12, 0.2, 0.35, 0.04), "Applicant")]);
Pump(viewer.ShowHighlightAsync("ledger-1"));
Check(viewer.CurrentPageIndex == 0, "host evidence navigates to its page");

var failed = false;
viewer.OperationFailed += (_, _) => failed = true;
try { Pump(viewer.OpenAsync(Path.Combine(directory, "invalid.pdf"))); }
catch (PdfiumException) { }
Check(failed && viewer.SourcePath == wordPath, "failed replacement preserves the current document and raises an event");

using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try { Pump(viewer.OpenAsync(htmlPath, cancelled.Token)); }
    catch (OperationCanceledException) { }
    Check(viewer.SourcePath == wordPath, "cancelled replacement preserves the current document");
}

var superseded = viewer.OpenAsync(wordPath);
var replacement = viewer.OpenAsync(htmlPath);
try { Pump(superseded); } catch (OperationCanceledException) { }
Pump(replacement);
Check(viewer.SourcePath == htmlPath, "newer document supersedes an older load");
Pump(viewer.OpenAsync(wordPath));
viewer.SetHighlights([new("ledger-1", 0, new PageRegion(0.12, 0.2, 0.35, 0.04), "Applicant")]);
Dispatcher.UIThread.RunJobs();
window.UpdateLayout();
AvaloniaHeadlessPlatform.ForceRenderTimerTick();
using (var image = new RenderTargetBitmap(new PixelSize(1100, 850)))
{
    image.Render(window);
    image.Save(Path.Combine(directory, "viewer.png"), PngBitmapEncoderOptions.Default);
    Check(image.PixelSize.Width == 1100, "Avalonia page and evidence screenshot");
}
Pump(viewer.DisposeAsync().AsTask());
Check(viewer.PageCount == 0, "Avalonia disposal clears native document state");
window.Close();
Console.WriteLine($"{checks} checks passed. Artifacts: {directory}");
