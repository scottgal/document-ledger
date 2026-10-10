using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mostlylucid.DocumentLedger.Core;

/// <summary>Append-only observations/reviews with a conservative, deterministic address resolver.</summary>
public sealed class AddressCoalescer
{
    public const string RuleVersion = "scoped-address-coalescing-v1";
    private readonly Dictionary<string, AddressObservation> _observations = new(StringComparer.Ordinal);
    private readonly List<ReviewDecision> _decisions = [];
    private readonly List<ResolvedField> _history = [];
    private List<ResolvedField> _lastFields = [];

    public static string StableId(string kind, params string[] values) => kind + "-" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values)))).ToLowerInvariant();
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    public bool Add(AddressObservation observation)
    {
        Validate(observation);
        if (_observations.TryGetValue(observation.Id, out var existing))
        {
            if (LogicalObservation(existing) != LogicalObservation(observation))
                throw new InvalidOperationException("An observation ID already identifies different evidence. Add a new observation instead.");
            return false;
        }
        foreach (var existingObservation in _observations.Values)
        {
            foreach (var evidence in observation.Evidence)
                if (existingObservation.Evidence.FirstOrDefault(e => e.Id == evidence.Id) is { } previous
                    && previous with { SourcePath = evidence.SourcePath, RepresentationPath = evidence.RepresentationPath } != evidence)
                    throw new ArgumentException("An evidence ID already identifies a different source span.");
            if (observation.IndependentOriginConfirmed && existingObservation.IndependentOriginConfirmed
                && observation.LineageRoot != existingObservation.LineageRoot
                && observation.Evidence.Any(e => existingObservation.Evidence.Any(old => old.SourceHash == e.SourceHash)))
                throw new ArgumentException("The same source cannot have conflicting confirmed lineage roots.");
        }
        _observations.Add(observation.Id, Copy(observation));
        return true;
    }

    private static string LogicalObservation(AddressObservation observation) => JsonSerializer.Serialize(observation with
    {
        Evidence = observation.Evidence.Select(e => e with { SourcePath = "", RepresentationPath = "" }).ToArray()
    });

    public ReviewDecision Merge(IEnumerable<string> observations, string actor, string reason, FieldScope? reviewedScope = null) =>
        Review(ReviewAction.Merge, observations, actor, reason, reviewedScope);

    public ReviewDecision KeepSeparate(IEnumerable<string> observations, string actor, string reason) =>
        Review(ReviewAction.KeepSeparate, observations, actor, reason);

    public ReviewDecision Reject(IEnumerable<string> observations, string actor, string reason) =>
        Review(ReviewAction.Reject, observations, actor, reason);

    public ReviewDecision Undo(string decisionId, string actor, string reason)
    {
        var target = _decisions.Find(d => d.Id == decisionId && d.Action != ReviewAction.Undo)
            ?? throw new ArgumentException("Unknown merge or separation decision.", nameof(decisionId));
        if (!ActiveDecisions().Any(d => d.Id == target.Id)) throw new InvalidOperationException("Decision is already undone.");
        return Append(new() { Id = StableId("review", DecisionId(), "undo", target.Id, actor, reason), Action = ReviewAction.Undo, Observations = target.Observations.ToArray(),
            Actor = actor, Reason = reason, At = DateTimeOffset.UtcNow, TargetDecision = target.Id });
    }

    private string DecisionId() => StableId("review", (_decisions.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _decisions.LastOrDefault()?.Id ?? "");

    private ReviewDecision Review(ReviewAction action, IEnumerable<string> ids, string actor, string reason, FieldScope? scope = null)
    {
        var members = ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (members.Length < (action == ReviewAction.Reject ? 1 : 2)) throw new ArgumentException("Select observations for review.");
        return Append(new() { Id = StableId("review", DecisionId(), action.ToString(), JsonSerializer.Serialize(members), actor, reason, JsonSerializer.Serialize(scope)), Action = action, Observations = members,
            Actor = actor, Reason = reason, At = DateTimeOffset.UtcNow, ReviewedScope = scope });
    }

    private IEnumerable<ReviewDecision> ActiveDecisions() => _decisions.Where(d => d.Action != ReviewAction.Undo
        && !_decisions.Any(u => u.Action == ReviewAction.Undo && u.TargetDecision == d.Id));

    private ReviewDecision Append(ReviewDecision decision)
    {
        if (!Enum.IsDefined(decision.Action) || string.IsNullOrWhiteSpace(decision.Id) || string.IsNullOrWhiteSpace(decision.Actor)
            || string.IsNullOrWhiteSpace(decision.Reason) || decision.Observations is null
            || decision.Observations.Any(string.IsNullOrWhiteSpace) || decision.TargetDecision is null
            || (decision.ReviewedScope is { } reviewed && (reviewed.Entity is null || reviewed.Role is null || reviewed.Period is null)))
            throw new ArgumentException("Review requires identity, actor, reason and observation references.");
        if (_decisions.Any(d => d.Id == decision.Id)) throw new ArgumentException("Duplicate review ID.");
        if (decision.Observations.Any(id => !_observations.ContainsKey(id))) throw new ArgumentException("Unknown observation.");
        if (decision.Action == ReviewAction.Undo)
        {
            var target = ActiveDecisions().FirstOrDefault(d => d.Id == decision.TargetDecision);
            if (target is null || !target.Observations.SequenceEqual(decision.Observations))
                throw new ArgumentException("Undo must reference an active decision and its original members.");
        }
        else if (decision.Observations.Distinct().Count() < (decision.Action == ReviewAction.Reject ? 1 : 2))
            throw new ArgumentException("Review has insufficient observation references.");
        if (decision.Action == ReviewAction.Merge)
        {
            if (ActiveDecisions().Any(d => d.Action == ReviewAction.Reject && d.Observations.Intersect(decision.Observations).Any()))
                throw new InvalidOperationException("Undo rejection before merging a rejected observation.");
            var members = decision.Observations.Select(id => _observations[id]).ToArray();
            var scope = decision.ReviewedScope ?? members.FirstOrDefault(o => o.Scope.IsKnown())?.Scope;
            if (scope is null || !scope.IsKnown()) throw new ArgumentException("A merge needs an explicit entity, role and period.");
            foreach (var member in members)
            {
                if (!member.SourceChecked) throw new InvalidOperationException("Review the source evidence before merging.");
                if (ScopeContradiction(member.Scope, scope) is not null)
                    throw new InvalidOperationException("A merge cannot overwrite a known entity, semantic role or period.");
            }
            var merged = ActiveDecisions().Where(d => d.Action == ReviewAction.Merge).ToArray();
            var closure = decision.Observations.ToHashSet(StringComparer.Ordinal);
            bool changed;
            do
            {
                changed = false;
                foreach (var previous in merged.Where(d => d.Observations.Any(closure.Contains)))
                {
                    if (previous.ReviewedScope is { } priorScope && ScopeContradiction(priorScope, scope) is not null)
                        throw new InvalidOperationException("An existing reviewed merge has a different scope.");
                    foreach (var id in previous.Observations) changed |= closure.Add(id);
                }
            } while (changed);
            foreach (var pair in Pairs(closure.Order(StringComparer.Ordinal).Select(id => _observations[id]).ToArray()))
            {
                if (ComponentConflict(pair.Left, pair.Right) is { } conflict) throw new InvalidOperationException(conflict);
                if (IsSeparated(pair.Left.Id, pair.Right.Id)) throw new InvalidOperationException("Undo the keep-separate decision before merging.");
            }
            decision = decision with { ReviewedScope = scope };
        }
        if (decision.Action == ReviewAction.KeepSeparate)
        {
            // Separating an existing explicit merge requires undo first, keeping history unambiguous.
            var linked = ActiveDecisions().Where(d => d.Action == ReviewAction.Merge)
                .Select(d => d.Observations.ToHashSet()).ToList();
            for (var i = 0; i < linked.Count; i++)
                for (var j = i + 1; j < linked.Count; j++)
                    if (linked[i].Overlaps(linked[j])) { linked[i].UnionWith(linked[j]); linked.RemoveAt(j--); i = -1; break; }
            if (linked.Any(g => decision.Observations.Count(g.Contains) > 1))
                throw new InvalidOperationException("Undo the explicit merge before separating its members.");
        }
        _decisions.Add(Copy(decision));
        return Copy(decision);
    }

    private bool IsSeparated(string left, string right) => ActiveDecisions().Any(d => d.Action == ReviewAction.KeepSeparate
        && d.Observations.Contains(left) && d.Observations.Contains(right));

    public static PairComparison Compare(AddressObservation left, AddressObservation right)
    {
        if (ScopeContradiction(left.Scope, right.Scope) is { } scopeReason)
            return new(left.Id, right.Id, MatchKind.Distinct, scopeReason);
        if (ComponentConflict(left, right) is { } conflict)
            return new(left.Id, right.Id, MatchKind.Conflicting, conflict);
        if (!left.Scope.IsKnown() || !right.Scope.IsKnown())
            return new(left.Id, right.Id, MatchKind.Unresolved, "Entity, semantic role or effective period is unresolved.");
        if (!left.SourceChecked || !right.SourceChecked)
            return new(left.Id, right.Id, MatchKind.Unresolved, "Source evidence has not been checked.");
        var a = AddressNormalizer.Normalize(left.Parts);
        var b = AddressNormalizer.Normalize(right.Parts);
        var assembly = left.IsFragment && right.IsFragment && left.AssemblyGroup.Length > 0
            && left.AssemblyGroup == right.AssemblyGroup
            && left.Evidence.Select(e => (e.SourceHash, e.Representation, e.Page)).Distinct()
                .Intersect(right.Evidence.Select(e => (e.SourceHash, e.Representation, e.Page)).Distinct()).Any();
        if (!assembly && (left.IsFragment || right.IsFragment))
            return new(left.Id, right.Id, MatchKind.Unresolved, "Fragments need a shared reviewed source block.");
        if (!assembly && (a.Unit.Length == 0) != (b.Unit.Length == 0))
            return new(left.Id, right.Id, MatchKind.Unresolved, "A missing unit is unknown, not an equivalent unit.");
        if (a.Unparsed.Length > 0 || b.Unparsed.Length > 0)
            return new(left.Id, right.Id, MatchKind.Unresolved, "Unparsed address content requires review.");
        var identity = a.Building.Length > 0 && a.Street.Length > 0 && a.Building == b.Building && a.Street == b.Street;
        if (!assembly && !identity)
            return new(left.Id, right.Id, MatchKind.Unresolved, "No sufficient shared building/street identity.");
        var same = a.Values().All(p => p.Value == b.Values()[p.Key]);
        return new(left.Id, right.Id, same ? MatchKind.Equivalent : MatchKind.Complementary,
            assembly ? "Compatible components in a reviewed source block." : "Compatible components within the same entity, role and period.");
    }

    private static string? ScopeContradiction(FieldScope a, FieldScope b)
    {
        if (a.Entity.Length > 0 && b.Entity.Length > 0 && a.Entity != b.Entity) return "Different entities.";
        if (a.Role.Length > 0 && b.Role.Length > 0 && a.Role != b.Role) return "Different semantic roles.";
        if (a.Period.Length > 0 && b.Period.Length > 0 && a.Period != b.Period) return "Different effective periods.";
        return null;
    }

    private static string? ComponentConflict(AddressObservation left, AddressObservation right)
    {
        var a = AddressNormalizer.Normalize(left.Parts).Values();
        var b = AddressNormalizer.Normalize(right.Parts).Values();
        foreach (var (component, value) in a)
            if (value.Length > 0 && b[component].Length > 0 && value != b[component])
                return "Conflicting " + component + " components.";
        return null;
    }

    private static IEnumerable<(AddressObservation Left, AddressObservation Right)> Pairs(IReadOnlyList<AddressObservation> members)
    {
        for (var i = 0; i < members.Count; i++)
            for (var j = i + 1; j < members.Count; j++) yield return (members[i], members[j]);
    }

    public CoalescingResult Resolve(IEnumerable<FieldConstraint>? constraints = null, bool automatic = true)
    {
        var requirements = (constraints ?? []).ToArray();
        if (requirements.Any(c => c is null || c.Scope is null || c.Maximum < 0))
            throw new ArgumentException("Constraints require a scope and nonnegative maximum cardinality.");
        var rejected = ActiveDecisions().Where(d => d.Action == ReviewAction.Reject).SelectMany(d => d.Observations).ToHashSet();
        var observations = _observations.Values.Where(o => !rejected.Contains(o.Id)).OrderBy(o => o.Id, StringComparer.Ordinal).ToArray();
        var groups = observations.Select(o => new List<AddressObservation> { o }).ToList();
        var active = ActiveDecisions().ToArray();
        foreach (var review in active.Where(d => d.Action == ReviewAction.Merge))
        {
            var matched = groups.Where(g => g.Any(o => review.Observations.Contains(o.Id))).ToArray();
            if (matched.Length == 0) continue;
            var members = matched.SelectMany(g => g).Select(o => o with { Scope = review.ReviewedScope! }).OrderBy(o => o.Id, StringComparer.Ordinal).ToList();
            foreach (var old in matched) groups.Remove(old);
            groups.Add(members);
        }
        if (automatic)
        {
            // Finish reviewed component blocks before comparing their assembled values with full addresses.
            // This prevents a late unit component from being hidden by an early full-address merge.
            MergeGroups(groups, (left, right) => left.All(a => right.All(b => a.IsFragment && b.IsFragment
                && !IsSeparated(a.Id, b.Id) && Compare(a, b).Kind is MatchKind.Equivalent or MatchKind.Complementary)));
            MergeGroups(groups, (left, right) => left.All(a => right.All(b => !IsSeparated(a.Id, b.Id)
                && ScopeContradiction(a.Scope, b.Scope) is null && ComponentConflict(a, b) is null))
                && Compare(Aggregate(left), Aggregate(right)).Kind is MatchKind.Equivalent or MatchKind.Complementary);
        }
        var comparisons = Pairs(observations).Select(pair => IsSeparated(pair.Left.Id, pair.Right.Id)
            ? new PairComparison(pair.Left.Id, pair.Right.Id, MatchKind.Distinct, "Reviewed keep-separate decision.")
            : Compare(pair.Left, pair.Right)).ToArray();
        var fields = new List<ResolvedField>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups.OrderBy(g => g.Min(o => o.Id), StringComparer.Ordinal))
        {
            var members = group.OrderBy(o => o.Id, StringComparer.Ordinal).ToArray();
            var ids = members.Select(o => o.Id).ToArray();
            var prior = _lastFields.Where(f => !claimed.Contains(f.InstanceId) && ids.Contains(f.Observations[0]))
                .OrderByDescending(f => f.Observations.Count(ids.Contains)).ThenBy(f => f.InstanceId, StringComparer.Ordinal).FirstOrDefault();
            var predecessor = prior ?? _lastFields.Where(f => f.Observations.Any(ids.Contains))
                .OrderByDescending(f => f.Observations.Count(ids.Contains)).ThenBy(f => f.InstanceId, StringComparer.Ordinal).FirstOrDefault();
            var instance = prior?.InstanceId ?? StableId("field", ids[0]);
            claimed.Add(instance);
            var parts = members.Select(o => AddressNormalizer.Normalize(o.Parts)).ToArray();
            var value = AddressParts.From(parts.SelectMany(p => p.Values()).Where(p => p.Value.Length > 0)
                .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First().Value));
            var componentEvidence = value.Values().Where(p => p.Value.Length > 0).ToDictionary(p => p.Key,
                p => (IReadOnlyList<string>)members.Where(o => AddressNormalizer.Normalize(o.Parts).Values()[p.Key].Length > 0)
                    .SelectMany(o => o.Evidence.Where(e => e.Component.Length == 0 || e.Component == p.Key))
                    .Select(e => e.Id).Distinct().Order(StringComparer.Ordinal).ToArray());
            var affecting = active.Where(d => d.Observations.Any(ids.Contains)).Select(d => d.Id).Order(StringComparer.Ordinal).ToArray();
            var resolutionId = StableId("resolution", instance, JsonSerializer.Serialize(members[0].Scope),
                JsonSerializer.Serialize(ids), JsonSerializer.Serialize(value), RuleVersion, AddressNormalizer.Version,
                _decisions.LastOrDefault()?.Id ?? "", automatic.ToString());
            var field = new ResolvedField
            {
                InstanceId = instance, ResolutionId = resolutionId, Scope = members[0].Scope, Value = value,
                Observations = ids, ComponentEvidence = componentEvidence,
                EvidenceIds = members.SelectMany(o => o.Evidence).Select(e => e.Id).Distinct().Order(StringComparer.Ordinal).ToArray(),
                Decisions = affecting, PreviousResolution = predecessor?.ResolutionId == resolutionId
                    ? predecessor.PreviousResolution : predecessor?.ResolutionId ?? "",
                ConfirmedIndependentOrigins = members.Where(o => o.IndependentOriginConfirmed && o.LineageRoot.Length > 0)
                    .Select(o => o.LineageRoot).Distinct().Count(),
                HasUnknownLineage = members.Any(o => !o.IndependentOriginConfirmed || o.LineageRoot.Length == 0),
                Rationale = active.Any(d => d.Action == ReviewAction.Merge && d.Observations.Any(ids.Contains))
                    ? "Explicit reviewed merge; original mappings retained."
                    : members.Length == 1 ? "Single source observation."
                    : members.Any(o => o.IsFragment) ? "Compatible address values after reviewed component assembly."
                    : "Equivalent or complementary addresses within one entity, role and period.",
                NeedsReview = !members[0].Scope.IsKnown() || members.Any(o => !o.SourceChecked || o.Parts.Unparsed.Length > 0)
                    || comparisons.Any(p => (ids.Contains(p.Left) ^ ids.Contains(p.Right)) && p.Kind is MatchKind.Conflicting or MatchKind.Unresolved)
            };
            fields.Add(field);
            if (!_history.Any(h => h.ResolutionId == field.ResolutionId)) _history.Add(field);
        }
        _lastFields = fields;
        var issues = requirements.SelectMany(c =>
        {
            if (c.Maximum < 0) throw new ArgumentException("Maximum cardinality cannot be negative.");
            var count = fields.Count(f => f.Scope == c.Scope);
            return c.Required && count == 0 ? new[] { new CardinalityIssue(c.Scope, count, "Required field is missing.") }
                : c.Maximum is int maximum && count > maximum ? [new CardinalityIssue(c.Scope, count, "Resolved field count exceeds maximum.")] : [];
        }).ToArray();
        return Copy(new CoalescingResult(fields, comparisons, issues));
    }

    private static AddressObservation Aggregate(List<AddressObservation> members)
    {
        var parts = AddressParts.From(members.SelectMany(o => AddressNormalizer.Normalize(o.Parts).Values())
            .Where(p => p.Value.Length > 0).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First().Value));
        return members[0] with { Parts = parts, SourceChecked = members.All(o => o.SourceChecked),
            IsFragment = members.Any(o => o.IsFragment) && (parts.Building.Length == 0 || parts.Street.Length == 0),
            Evidence = members.SelectMany(o => o.Evidence).ToArray() };
    }

    private static void MergeGroups(List<List<AddressObservation>> groups, Func<List<AddressObservation>, List<AddressObservation>, bool> compatible)
    {
            bool merged;
            do
            {
                merged = false;
                groups.Sort((a, b) => StringComparer.Ordinal.Compare(a.Min(o => o.Id), b.Min(o => o.Id)));
                for (var i = 0; i < groups.Count && !merged; i++)
                    for (var j = i + 1; j < groups.Count; j++)
                        if (compatible(groups[i], groups[j]))
                        {
                            groups[i].AddRange(groups[j]); groups.RemoveAt(j); merged = true; break;
                        }
            } while (merged);
    }

    public CoalescingSnapshot Snapshot() => Copy(new CoalescingSnapshot
    {
        Observations = _observations.Values.OrderBy(o => o.Id, StringComparer.Ordinal).ToArray(),
        Decisions = _decisions.ToArray(), History = _history.ToArray(), LastFields = _lastFields.ToArray()
    });

    public static AddressCoalescer Restore(CoalescingSnapshot snapshot)
    {
        if (snapshot.Version != 1 || snapshot.Observations is null || snapshot.Decisions is null
            || snapshot.History is null || snapshot.LastFields is null) throw new ArgumentException("Invalid coalescing snapshot.");
        var ledger = new AddressCoalescer();
        foreach (var observation in snapshot.Observations) ledger.Add(observation);
        foreach (var decision in snapshot.Decisions) ledger.Append(decision);
        foreach (var field in snapshot.History.Concat(snapshot.LastFields))
            if (field is null || string.IsNullOrWhiteSpace(field.InstanceId) || string.IsNullOrWhiteSpace(field.ResolutionId)
                || field.Scope is null || field.Scope.Entity is null || field.Scope.Role is null || field.Scope.Period is null
                || field.Value is null || field.Value.Values().Any(p => p.Value is null)
                || field.Observations is null || field.Observations.Count == 0 || field.Observations.Any(id => id is null || !ledger._observations.ContainsKey(id))
                || field.ComponentEvidence is null || field.ComponentEvidence.Any(p => p.Value is null)
                || field.EvidenceIds is null || field.Decisions is null || field.PreviousResolution is null
                || field.Rationale is null || string.IsNullOrWhiteSpace(field.RuleVersion) || string.IsNullOrWhiteSpace(field.NormalizerVersion)
                || field.ConfirmedIndependentOrigins < 0
                || field.Decisions.Any(id => !ledger._decisions.Any(d => d.Id == id))
                || field.EvidenceIds.Any(id => !field.Observations.SelectMany(o => ledger._observations[o].Evidence).Any(e => e.Id == id))
                || field.ComponentEvidence.Any(p => !field.Value.Values().ContainsKey(p.Key) || p.Value.Any(id => !field.EvidenceIds.Contains(id))))
                throw new ArgumentException("Invalid resolution or provenance references.");
        if (snapshot.History.Select(f => f.ResolutionId).Distinct().Count() != snapshot.History.Count
            || snapshot.LastFields.Select(f => f.InstanceId).Distinct().Count() != snapshot.LastFields.Count
            || snapshot.LastFields.Any(f => !snapshot.History.Any(h => h.ResolutionId == f.ResolutionId)))
            throw new ArgumentException("Duplicate or missing resolution history.");
        ledger._history.AddRange(Copy(snapshot.History));
        ledger._lastFields = Copy(snapshot.LastFields).ToList();
        return ledger;
    }

    private static void Validate(AddressObservation o)
    {
        if (o is null || string.IsNullOrWhiteSpace(o.Id) || o.Scope is null || o.Scope.Entity is null
            || o.Scope.Role is null || o.Scope.Period is null || o.Label is null || o.Section is null
            || o.RawValue is null || o.Parts is null || o.Evidence is null || o.Evidence.Count == 0
            || o.AssemblyGroup is null || o.Producer is null || o.LineageRoot is null || o.LocalField is null || o.Variant is null || o.Parts.Values().Any(p => p.Value is null))
            throw new ArgumentException("Observation requires identity, components and source evidence.");
        foreach (var e in o.Evidence)
        {
            if (e is null || string.IsNullOrWhiteSpace(e.Id) || string.IsNullOrWhiteSpace(e.SourceHash)
                || string.IsNullOrWhiteSpace(e.Representation) || e.Text is null || e.Component is null || e.SourcePath is null || e.RepresentationPath is null
                || e.Page is < 0 || (e.Component.Length > 0 && !o.Parts.Values().ContainsKey(e.Component)))
                throw new ArgumentException("Invalid source evidence.");
            if (e.Region is { } r && (e.Page is null || !double.IsFinite(r.X) || !double.IsFinite(r.Y)
                || !double.IsFinite(r.Width) || !double.IsFinite(r.Height) || r.X < 0 || r.Y < 0
                || r.Width <= 0 || r.Height <= 0 || r.X + r.Width > 1.000001 || r.Y + r.Height > 1.000001))
                throw new ArgumentException("Invalid normalised evidence region.");
        }
        foreach (var (component, value) in o.Parts.Values().Where(p => !string.IsNullOrWhiteSpace(p.Value)))
            if (!o.Evidence.Any(e => e.Component.Length == 0 || e.Component == component))
                throw new ArgumentException("Every component needs supporting evidence.");
    }
}
