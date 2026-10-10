namespace Mostlylucid.DocumentLedger.Core;

public enum MatchKind { Equivalent, Complementary, Distinct, Conflicting, Unresolved }
public enum ReviewAction { Merge, KeepSeparate, Reject, Undo }

public sealed record FieldScope(string Entity, string Role, string Period)
{
    public FieldScope() : this("", "", "") { }
    public bool IsKnown() => !string.IsNullOrWhiteSpace(Entity) && !string.IsNullOrWhiteSpace(Role)
        && !string.IsNullOrWhiteSpace(Period);
}

public sealed record EvidenceRegion(double X, double Y, double Width, double Height)
{
    public EvidenceRegion() : this(0, 0, 0, 0) { }
}

public sealed record SourceEvidence
{
    public required string Id { get; init; }
    public required string SourceHash { get; init; }
    public required string Representation { get; init; }
    public required string Text { get; init; }
    public string SourcePath { get; init; } = "";
    public string RepresentationPath { get; init; } = "";
    public int? Page { get; init; }
    public EvidenceRegion? Region { get; init; }
    // Empty means the region supports the whole observation. Otherwise a component name.
    public string Component { get; init; } = "";
}

public sealed record AddressParts
{
    public string Unit { get; init; } = "";
    public string Building { get; init; } = "";
    public string Street { get; init; } = "";
    public string Locality { get; init; } = "";
    public string Postcode { get; init; } = "";
    public string Country { get; init; } = "";
    public string Unparsed { get; init; } = "";

    public IReadOnlyDictionary<string, string> Values() => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["unit"] = Unit, ["building"] = Building, ["street"] = Street,
        ["locality"] = Locality, ["postcode"] = Postcode, ["country"] = Country,
        ["unparsed"] = Unparsed
    };

    public static AddressParts From(IReadOnlyDictionary<string, string> values) => new()
    {
        Unit = values.GetValueOrDefault("unit", ""), Building = values.GetValueOrDefault("building", ""),
        Street = values.GetValueOrDefault("street", ""), Locality = values.GetValueOrDefault("locality", ""),
        Postcode = values.GetValueOrDefault("postcode", ""), Country = values.GetValueOrDefault("country", ""),
        Unparsed = values.GetValueOrDefault("unparsed", "")
    };

    public string Display() => string.Join(", ", new[]
    {
        string.IsNullOrEmpty(Unit) ? "" : "UNIT " + Unit,
        string.Join(" ", new[] { Building, Street }.Where(s => s.Length > 0)),
        Locality, Postcode, Country, Unparsed
    }.Where(s => s.Length > 0));
}

public sealed record AddressObservation
{
    public required string Id { get; init; }
    public required FieldScope Scope { get; init; }
    public required string Label { get; init; }
    public string Section { get; init; } = "";
    public required string RawValue { get; init; }
    public required AddressParts Parts { get; init; }
    public required IReadOnlyList<SourceEvidence> Evidence { get; init; }
    public bool SourceChecked { get; init; }
    public bool IsFragment { get; init; }
    // A caller-reviewed grouping of components in one source block, not a guessed page proximity.
    public string AssemblyGroup { get; init; } = "";
    public string Producer { get; init; } = "manual";
    public string LocalField { get; init; } = "";
    public string Variant { get; init; } = "";
    public string LineageRoot { get; init; } = "";
    public bool IndependentOriginConfirmed { get; init; }
}

public sealed record PairComparison(string Left, string Right, MatchKind Kind, string Reason);

public sealed record ReviewDecision
{
    public required string Id { get; init; }
    public required ReviewAction Action { get; init; }
    public required IReadOnlyList<string> Observations { get; init; }
    public required string Actor { get; init; }
    public required string Reason { get; init; }
    public required DateTimeOffset At { get; init; }
    public FieldScope? ReviewedScope { get; init; }
    public string TargetDecision { get; init; } = "";
}

public sealed record ResolvedField
{
    public required string InstanceId { get; init; }
    public required string ResolutionId { get; init; }
    public required FieldScope Scope { get; init; }
    public required AddressParts Value { get; init; }
    public required IReadOnlyList<string> Observations { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> ComponentEvidence { get; init; }
    public required IReadOnlyList<string> EvidenceIds { get; init; }
    public required IReadOnlyList<string> Decisions { get; init; }
    public int ConfirmedIndependentOrigins { get; init; }
    public bool HasUnknownLineage { get; init; }
    public bool NeedsReview { get; init; }
    public string Rationale { get; init; } = "";
    public string PreviousResolution { get; init; } = "";
    public string RuleVersion { get; init; } = AddressCoalescer.RuleVersion;
    public string NormalizerVersion { get; init; } = AddressNormalizer.Version;
}

public sealed record FieldConstraint(FieldScope Scope, bool Required = false, int? Maximum = 1);
public sealed record CardinalityIssue(FieldScope Scope, int Actual, string Reason);
public sealed record CoalescingResult(
    IReadOnlyList<ResolvedField> Fields, IReadOnlyList<PairComparison> Comparisons,
    IReadOnlyList<CardinalityIssue> CardinalityIssues);

public sealed record CoalescingSnapshot
{
    public int Version { get; init; } = 1;
    public IReadOnlyList<AddressObservation> Observations { get; init; } = [];
    public IReadOnlyList<ReviewDecision> Decisions { get; init; } = [];
    public IReadOnlyList<ResolvedField> History { get; init; } = [];
    public IReadOnlyList<ResolvedField> LastFields { get; init; } = [];
}

// Optional inference can propose a pair outcome. It cannot bypass the core's scope/component guards.
public interface ICoalescingAdvisor
{
    Task<PairComparison> CompareAsync(AddressObservation left, AddressObservation right,
        CancellationToken cancellationToken = default);
}
