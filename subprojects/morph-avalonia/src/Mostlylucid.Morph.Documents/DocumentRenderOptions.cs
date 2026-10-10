namespace Mostlylucid.Morph.Documents;

/// <summary>Font settings for Word and HTML conversion. Existing PDFs retain their embedded fonts.</summary>
public sealed record DocumentRenderOptions
{
    public static string BundledFontDirectory => Path.Combine(AppContext.BaseDirectory, "MorphFonts");
    public string FontDirectory { get; init; } = BundledFontDirectory;
    public string FallbackFont { get; init; } = "Noto Sans";
    public double FontWidthScale { get; init; } = 1;
}
