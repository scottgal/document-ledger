using Mostlylucid.Morph.Documents;

namespace Morph.Avalonia.Demo;

// Deliberately small, inspectable state for the concept demo.
public sealed class DemoLedger
{
    public int Version { get; set; } = 1;
    public string Source { get; set; } = "";
    public string TemplateState { get; set; } = "draft";
    public string SourceHash { get; set; } = "";
    public string Producer { get; set; } = "manual";
    public List<ReviewedNeighborhood> ReviewedNeighborhoods { get; set; } = [];
    public List<DemoFeedback> Feedback { get; set; } = [];
    public List<DemoField> Fields { get; set; } = [];
}

public sealed class DemoField
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public string Status { get; set; } = "proposed";
    public string Anchor { get; set; } = "";
    public string Section { get; set; } = "";
    public InitialCentroid Centroid { get; set; } = new();
    public string Selector { get; set; } = "label-relative";
    public int? Page { get; set; }
    public List<DemoRegion> Regions { get; set; } = [];
}

public sealed class DemoRegion
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public PageRegion ToRegion() => new(X, Y, Width, Height);
    public static DemoRegion From(PageRegion r) => new() { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height };
}

public sealed class InitialCentroid
{
    public string State { get; set; } = "initial";
    public string Scope { get; set; } = "field-neighborhood";
    public int IndependentExamples { get; set; } = 1;
    public bool Reviewed { get; set; }
    public string Label { get; set; } = "";
    public List<string> Neighbors { get; set; } = [];
    public List<DemoRegion> AnchorPositions { get; set; } = [];
    public List<DemoRegion> ValuePositions { get; set; } = [];
    public string Relation { get; set; } = "unresolved";
    public EmbeddingSeed Embedding { get; set; } = new();
}

public sealed class EmbeddingSeed
{
    public string Model { get; set; } = "google/embeddinggemma-2";
    public int Dimensions { get; set; } = 768;
    public string State { get; set; } = "pending-model";
    public string LabelInput { get; set; } = "";
    public string NeighborhoodInput { get; set; } = "";
    public string ImageInput { get; set; } = "source-page-and-neighborhood-region";
}

public sealed class DemoFeedback
{
    public string At { get; set; } = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
    public string Field { get; set; } = "";
    public string Action { get; set; } = "";
    public string PreviousValue { get; set; } = "";
    public string Value { get; set; } = "";
    public string Source { get; set; } = "";
}

public sealed class ReviewedNeighborhood
{
    public string Field { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public string Anchor { get; set; } = "";
    public string Section { get; set; } = "";
    public InitialCentroid Features { get; set; } = new();
}
