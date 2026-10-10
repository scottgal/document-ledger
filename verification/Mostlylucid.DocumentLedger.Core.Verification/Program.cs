using System.Text.Json;
using Mostlylucid.DocumentLedger.Core;

var checks = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    checks.Add(name);
}
void Reject(Action action, string name)
{
    try { action(); } catch (ArgumentException) { checks.Add(name); return; }
    catch (InvalidOperationException) { checks.Add(name); return; }
    throw new InvalidOperationException("FAIL: expected rejection: " + name);
}
var scope = new FieldScope("property-a", "property.address", "2026-10");
AddressObservation O(string id, string value, FieldScope? context = null, AddressParts? parts = null) => new()
{
    Id = id, Scope = context ?? scope, Label = "Property address", RawValue = value,
    Parts = parts ?? AddressNormalizer.Parse(value), SourceChecked = true,
    Evidence = [new() { Id = "e-" + id, SourceHash = "source-" + id, Representation = "pdf-" + id,
        Text = value, Page = 0, Region = new(.1, .2, .5, .1) }],
    LineageRoot = "origin-" + id, IndependentOriginConfirmed = true
};
AddressCoalescer Ledger(params AddressObservation[] observations)
{
    var ledger = new AddressCoalescer(); foreach (var o in observations) ledger.Add(o); return ledger;
}
var first = O("a", "42 Example St, Edinburgh, EH11AA");
var second = O("b", "42 Example Street, Edinburgh, EH1 1AA");
Check(FieldRoleAlignment.AddressRole("Property address") == "property.address", "property alias");
Check(FieldRoleAlignment.AddressRole("Subject property") == "property.address", "valuation alias");
Check(FieldRoleAlignment.AddressRole("Address", "Security property") == "property.address", "scoped generic alias");
Check(FieldRoleAlignment.AddressRole("Address", "Correspondence") == "correspondence.address", "competing section alias");
Check(FieldRoleAlignment.AddressRole("Address") == "", "unscoped generic address remains unknown");
Check(FieldRoleAlignment.AddressRole("Requested amount") == "", "non-address not coerced");
Check(AddressNormalizer.Normalize(first.Parts) == AddressNormalizer.Normalize(second.Parts), "suffix and postcode normalisation");
Check(AddressNormalizer.Normalize(new() { Street = "St Andrews Rd" }).Street == "ST ANDREWS ROAD", "Saint prefix retained");
Check(AddressNormalizer.Normalize(new() { Building = "1/2" }).Building == "1/2", "building separator retained");
Check(AddressNormalizer.Normalize(new() { Country = "United Kingdom" }).Country == "GB", "country alias");
Check(AddressNormalizer.Parse("Flat 2, 42 Example Street, Edinburgh, EH1 1AA").Unit == "2", "unit parsed");
Check(AddressNormalizer.Parse("42 Example Street", "street").Building == "42", "street box splits building number");
Check(AddressNormalizer.Parse("Unrecognised house name").Unparsed.Length > 0, "unparsed content preserved");
Check(AddressCoalescer.Compare(first, second).Kind == MatchKind.Equivalent, "equivalent cross-document addresses");
var ledger = Ledger(first);
var initial = ledger.Resolve().Fields.Single();
ledger.Add(second);
var result = ledger.Resolve([new(scope, true, 1)]);
Check(result.Fields.Count == 1, "equivalent observations coalesced");
Check(result.CardinalityIssues.Count == 0, "cardinality after coalescing");
Check(result.Fields[0].Observations.Count == 2, "both observations retained");
Check(result.Fields[0].EvidenceIds.Count == 2, "both source regions retained");
Check(result.Fields[0].ComponentEvidence["postcode"].Count == 2, "component provenance retained");
Check(result.Fields[0].ConfirmedIndependentOrigins == 2, "two confirmed source origins");
Check(result.Fields[0].InstanceId == initial.InstanceId, "instance identity survives additional evidence");
Check(result.Fields[0].PreviousResolution == initial.ResolutionId, "resolution revision links predecessor");
Check(!ledger.Add(first), "same observation import is idempotent");
Check(ledger.Resolve().Fields[0].ResolutionId == result.Fields[0].ResolutionId, "stable resolution on rerun");
Check(ledger.Snapshot().History.Count == 2, "rerun does not inflate history");
Reject(() => ledger.Add(first with { RawValue = "different" }), "ID cannot silently overwrite evidence");
var reverse = Ledger(second, first).Resolve();
Check(reverse.Fields[0].Value == result.Fields[0].Value, "batch order does not affect canonical value");
var fragmentSource = first.Evidence[0] with { SourceHash = "form", Representation = "form-pdf" };
AddressObservation Fragment(string id, string value, AddressParts parts, string component) => O(id, value, parts: parts) with
{
    IsFragment = true, AssemblyGroup = "property-box-1", LineageRoot = "form-origin",
    Evidence = [fragmentSource with { Id = "span-" + id, Text = value, Component = component }]
};
var street = Fragment("street", "42 Example Street", new() { Building = "42", Street = "Example Street" }, "");
var town = Fragment("town", "Edinburgh", new() { Locality = "Edinburgh" }, "locality");
var postal = Fragment("postal", "EH1 1AA", new() { Postcode = "EH1 1AA" }, "postcode");
var assembled = Ledger(street, town, postal).Resolve([new(scope, true)]);
Check(assembled.Fields.Count == 1 && assembled.Fields[0].Value.Postcode == "EH11AA", "three boxes assemble");
Check(assembled.Fields[0].ComponentEvidence["postcode"].SequenceEqual(["span-postal"]), "postcode cites only postcode box");
Check(assembled.Fields[0].ComponentEvidence["street"].SequenceEqual(["span-street"]), "street cites only street box");
Check(assembled.Fields[0].ConfirmedIndependentOrigins == 1, "three boxes are one source origin");
Check(AddressCoalescer.Compare(town, postal with { AssemblyGroup = "another-box" }).Kind == MatchKind.Unresolved, "unrelated fragments remain unresolved");
Check(AddressCoalescer.Compare(town, postal with { Evidence = [postal.Evidence[0] with { Page = 1 }] }).Kind == MatchKind.Unresolved, "assembly cannot cross page by group name alone");
var correspondence = second with { Id = "correspondence", Scope = scope with { Role = "correspondence.address" } };
Check(Ledger(first, correspondence).Resolve().Fields.Count == 2, "identical text in different roles stays separate");
Check(AddressCoalescer.Compare(first, correspondence).Kind == MatchKind.Distinct, "role difference is explicit");
Reject(() => Ledger(first, correspondence).Merge([first.Id, correspondence.Id], "reviewer", "same text"), "manual merge cannot erase roles");
var differentEntity = second with { Id = "entity-b", Scope = scope with { Entity = "property-b" } };
Check(Ledger(first, differentEntity).Resolve().Fields.Count == 2, "different entities stay separate");
var historical = second with { Id = "old", Scope = scope with { Period = "2025-01" } };
Check(Ledger(first, historical).Resolve().Fields.Count == 2, "historical address retained");
Reject(() => Ledger(first, historical).Merge([first.Id, historical.Id], "reviewer", "same text"), "manual merge cannot erase period");
var flat2 = O("flat2", "Flat 2, 42 Example Street, Edinburgh, EH1 1AA");
var flat3 = O("flat3", "Flat 3, 42 Example Street, Edinburgh, EH1 1AA");
Check(AddressCoalescer.Compare(flat2, flat3).Kind == MatchKind.Conflicting, "different units conflict");
var unitResult = Ledger(flat2, flat3).Resolve([new(scope, true)]);
Check(unitResult.Fields.Count == 2 && unitResult.Fields.All(f => f.NeedsReview), "unit conflict remains visible");
Check(unitResult.CardinalityIssues.Count == 1, "unresolved unit conflict triggers cardinality");
Reject(() => Ledger(flat2, flat3).Merge([flat2.Id, flat3.Id], "reviewer", "merge"), "manual merge cannot erase component conflict");
Check(AddressCoalescer.Compare(first, flat2).Kind == MatchKind.Unresolved, "missing unit is not equality");
Check(Ledger(first, flat2, flat3).Resolve().Fields.Count == 3, "missing-unit bridge does not merge distinct flats");
var known = first with { Id = "no-postcode", Parts = first.Parts with { Postcode = "" } };
Check(AddressCoalescer.Compare(known, second).Kind == MatchKind.Complementary, "compatible complete address supplements missing postcode");
Check(Ledger(known, second).Resolve().Fields[0].Value.Postcode == "EH11AA", "compatible source fills component");
Check(Ledger(known, correspondence).Resolve().Fields.Single(f => f.Scope.Role == "property.address").Value.Postcode == "", "competing role never fills missing component");
var unscoped = second with { Id = "unknown", Scope = scope with { Entity = "", Period = "" } };
Check(Ledger(first, unscoped).Resolve().Fields.Count == 2, "unknown scope not guessed");
var reviewed = Ledger(first, unscoped);
var merge = reviewed.Merge([first.Id, unscoped.Id], "tester", "Reviewed entity and period", scope);
Check(reviewed.Resolve().Fields.Count == 1, "review can supply unresolved scope");
Check(reviewed.Snapshot().Observations.Single(o => o.Id == unscoped.Id).Scope.Entity == "", "review preserves original mapping");
reviewed.Undo(merge.Id, "tester", "Reopen identity decision");
Check(reviewed.Resolve().Fields.Count == 2, "undo restores separate unresolved instances");
Check(reviewed.Snapshot().Decisions.Count == 2, "undo retained in history");
Reject(() => reviewed.Undo(merge.Id, "tester", "again"), "double undo rejected");
var keep = ledger.KeepSeparate([first.Id, second.Id], "tester", "Retain distinct cases");
Check(ledger.Resolve().Fields.Count == 2, "keep-separate overrides automatic equivalence");
Reject(() => ledger.Merge([first.Id, second.Id], "tester", "merge"), "keep-separate decision must be undone");
ledger.Undo(keep.Id, "tester", "Cases confirmed same");
Check(ledger.Resolve().Fields.Count == 1, "undo separation restores equivalence");
Check(Ledger(first, second).Resolve(automatic: false).Fields.Count == 2, "automatic layer can be disabled");
var manual = Ledger(first, second);
var explicitMerge = manual.Merge([first.Id, second.Id], "tester", "Reviewed identical address");
Check(manual.Resolve(automatic: false).Fields.Count == 1, "reviewed merge works without inference");
Reject(() => manual.KeepSeparate([first.Id, second.Id], "tester", "separate"), "explicit merge undone before separation");
manual.Undo(explicitMerge.Id, "tester", "Unmerge example");
Check(manual.Resolve(automatic: false).Fields.Count == 2, "explicit unmerge with automatic layer disabled");
var copy = second with { LineageRoot = first.LineageRoot };
Check(Ledger(first, copy).Resolve().Fields[0].ConfirmedIndependentOrigins == 1, "known copied origins counted once");
Check(Ledger(first with { IndependentOriginConfirmed = false }).Resolve().Fields[0].HasUnknownLineage, "unknown lineage explicit");
var sameRegion = first with { Id = "second-model", Producer = "another-model" };
Check(Ledger(first, sameRegion).Resolve().Fields[0].EvidenceIds.Count == 1, "two producers do not duplicate region evidence");
Check(Ledger(first, sameRegion).Resolve().Fields[0].ConfirmedIndependentOrigins == 1, "two producers do not inflate origin support");
Reject(() => Ledger(first).Add(sameRegion with { LineageRoot = "another-origin" }), "source lineage contradiction rejected");
Reject(() => Ledger(first).Add(second with { Evidence = [first.Evidence[0] with { Text = "different span" }] }), "evidence identity cannot be reassigned");
Reject(() => Ledger(first with { Evidence = [first.Evidence[0] with { Region = new(double.NaN, .2, .1, .1) }] }), "invalid coordinate rejected");
Reject(() => Ledger(first with { Evidence = [first.Evidence[0] with { Component = "street" }] }), "unsupported component evidence rejected");
Reject(() => Ledger(first with { Evidence = [] }), "missing evidence rejected");
Check(Ledger(first with { SourceChecked = false }, second).Resolve().Fields.Count == 2, "unchecked source does not auto merge");
Reject(() => Ledger(first with { SourceChecked = false }, second).Merge([first.Id, second.Id], "tester", "merge"), "unchecked source blocks review merge");
var snapshot = JsonSerializer.Deserialize<CoalescingSnapshot>(JsonSerializer.Serialize(ledger.Snapshot()))!;
var restored = AddressCoalescer.Restore(snapshot);
Check(JsonSerializer.Serialize(restored.Resolve()) == JsonSerializer.Serialize(ledger.Resolve()), "snapshot roundtrip replays exact resolution");
Check(restored.Snapshot().History.Count == ledger.Snapshot().History.Count, "restoration preserves history");
Reject(() => AddressCoalescer.Restore(snapshot with { Version = 99 }), "unknown snapshot version rejected");
Reject(() => AddressCoalescer.Restore(snapshot with { Decisions = [new() { Id = "bad", Action = ReviewAction.Undo,
    Actor = "tester", Reason = "bad", At = DateTimeOffset.UtcNow, Observations = ["a"], TargetDecision = "missing" }] }), "invalid review log rejected");
Check(new AddressCoalescer().Resolve([new(scope, true)]).CardinalityIssues.Single().Actual == 0, "missing required field reported");
var detached = ledger.Snapshot();
((IList<AddressObservation>)detached.Observations)[0] = first with { RawValue = "tampered" };
Check(ledger.Snapshot().Observations[0].RawValue == first.RawValue, "snapshot cannot mutate ledger");
Check(Ledger(first, second, street, town, postal).Resolve().Fields.Count == 1, "assembled boxes coalesce with full address");
var unitBox = Fragment("unit-box", "2", new() { Unit = "2" }, "unit");
Check(Ledger(first, street, town, postal, unitBox).Resolve().Fields.Count == 2, "late unit component cannot vanish into unitless full address");
var bridge = first with { Id = "bridge", Parts = first.Parts with { Postcode = "" } };
var otherPostal = second with { Id = "other-postal", Parts = second.Parts with { Postcode = "EH2 2BB" } };
Check(Ledger(first, bridge, otherPostal).Resolve().Fields.Count == 2, "complementary bridge cannot transitively merge conflicting postcodes");
var rejecting = Ledger(first, second);
var rejection = rejecting.Reject([second.Id], "tester", "Incorrect mapping");
Check(rejecting.Resolve().Fields.Single().Observations.SequenceEqual([first.Id]), "rejected observation excluded from canonical result");
Check(rejecting.Snapshot().Observations.Count == 2, "rejection retains original evidence");
Reject(() => rejecting.Merge([first.Id, second.Id], "tester", "merge"), "rejected observation cannot be merged");
var rejectedSnapshot = AddressCoalescer.Restore(JsonSerializer.Deserialize<CoalescingSnapshot>(JsonSerializer.Serialize(rejecting.Snapshot()))!);
Check(rejectedSnapshot.Resolve().Fields.Single().Observations.Count == 1, "rejection survives snapshot roundtrip");
rejecting.Undo(rejection.Id, "tester", "Restore mapping");
Check(rejecting.Resolve().Fields.Single().Observations.Count == 2, "undo rejection restores support");
Check(!Ledger(first).Add(first with { Evidence = [first.Evidence[0] with { SourcePath = "/moved/source.pdf", RepresentationPath = "/moved/render.pdf" }] }), "storage relocation is an idempotent import");
Reject(() => Ledger(first).Resolve([new(scope, true, -1)]), "negative cardinality rejected");
Reject(() => Ledger(first with { LocalField = null! }), "null local field metadata rejected");
Reject(() => Ledger(first with { Evidence = [first.Evidence[0] with { RepresentationPath = null! }] }), "null representation path rejected");
Reject(() => AddressCoalescer.Restore(snapshot with { LastFields = [snapshot.LastFields[0] with { InstanceId = null! }] }), "null resolution identity rejected");
Reject(() => AddressCoalescer.Restore(snapshot with { LastFields = [snapshot.LastFields[0] with { EvidenceIds = ["unknown-span"] }] }), "unknown resolution evidence rejected");
Reject(() => AddressCoalescer.Restore(snapshot with { History = snapshot.History.Concat([snapshot.History[0]]).ToArray() }), "duplicate resolution revisions rejected");
var allRejected = Ledger(first, second);
allRejected.Merge([first.Id, second.Id], "tester", "Reviewed same field");
_ = allRejected.Resolve(automatic: false).Fields.Single();
var rejectAll = allRejected.Reject([first.Id, second.Id], "tester", "Both mappings invalid");
Check(allRejected.Resolve().Fields.Count == 0, "rejecting all members of explicit merge leaves no empty group");
allRejected.Undo(rejectAll.Id, "tester", "Restore reviewed field");
Check(allRejected.Resolve(automatic: false).Fields.Count == 1, "undo all-member rejection restores reviewed merge");
var splitLedger = Ledger(first, second);
var splitMerge = splitLedger.Merge([first.Id, second.Id], "tester", "Reviewed merge");
var beforeSplit = splitLedger.Resolve(automatic: false).Fields.Single();
splitLedger.Undo(splitMerge.Id, "tester", "Separate again");
Check(splitLedger.Resolve(automatic: false).Fields.All(f => f.PreviousResolution == beforeSplit.ResolutionId), "both split branches retain predecessor resolution");
var contradictoryUnits = AddressNormalizer.Parse("Flat 2, Flat 3, 42 Example Street, Edinburgh, EH1 1AA");
Check(contradictoryUnits.Unit == "2" && contradictoryUnits.Unparsed.Contains("FLAT 3"), "contradictory units retained as unparsed evidence");
Check(Ledger(O("contradictory", "Flat 2, Flat 3, 42 Example Street, Edinburgh, EH1 1AA"), flat2).Resolve().Fields.Count == 2, "internal unit contradiction blocks automatic merge");
var report = new { passed = true, count = checks.Count, checks };
var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/core-verification.json");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{checks.Count} semantic coalescing checks passed. {output}");
