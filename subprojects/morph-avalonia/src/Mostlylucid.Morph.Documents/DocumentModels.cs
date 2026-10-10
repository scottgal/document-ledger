namespace Mostlylucid.Morph.Documents;

/// <summary>A page-local rectangle in normalized, top-left coordinates.</summary>
public readonly record struct PageRegion
{
    public PageRegion(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) ||
            !double.IsFinite(height) || x < 0 || y < 0 || width < 0 || height < 0 ||
            x + width > 1.000001 || y + height > 1.000001)
            throw new ArgumentOutOfRangeException(nameof(x), "Regions must fit within the normalized page.");
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
}

public sealed record DocumentPage(int Index, double WidthPoints, double HeightPoints);

public sealed record DocumentMatch(int PageIndex, int CharacterIndex, int CharacterCount,
    string Text, IReadOnlyList<PageRegion> Regions);

/// <summary>Stable host ID and page-local evidence region. Page indexes are zero-based.</summary>
public sealed record DocumentHighlight(string Id, int PageIndex, PageRegion Region, string? Label = null);

public sealed record DocumentSelection(int PageIndex, PageRegion Region, string Text);
