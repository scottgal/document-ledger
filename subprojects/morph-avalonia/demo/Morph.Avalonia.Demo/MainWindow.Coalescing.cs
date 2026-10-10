using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Mostlylucid.DocumentLedger.Core;
using Mostlylucid.Morph.Documents;

namespace Morph.Avalonia.Demo;

public partial class MainWindow
{
    private static readonly JsonSerializerOptions WorkspaceJson = new() { WriteIndented = true };
    private AddressCoalescer _coalescer = new();
    private readonly Dictionary<string, string> _fieldObservations = new();
    private readonly HashSet<string> _checkedObservations = new(StringComparer.Ordinal);
    private string _currentDocumentPath = "";
    private bool _previewingEvidence;
    private bool _capturingAddresses;
    private bool _suggesting;

    private void InitializeCoalescing()
    {
        AddressSampleButton.Click += async (_, _) => await OpenDocumentAsync(SampleDocument.CreateAddresses(_sampleDirectory));
        AutomaticCoalescingBox.IsCheckedChanged += (_, _) => RefreshLedger();
        ApplyAddressScopeButton.Click += async (_, _) =>
        {
            if (_selected is null || _previewingEvidence || _capturingAddresses) return;
            var field = _selected;
            field.CanonicalRole = CanonicalRoleBox.Text?.Trim() ?? "";
            field.Entity = EntityScopeBox.Text?.Trim() ?? "";
            field.Period = PeriodScopeBox.Text?.Trim() ?? "";
            field.AddressComponent = AddressComponentBox.Text?.Trim().ToLowerInvariant() ?? "";
            field.AssemblyGroup = AssemblyGroupBox.Text?.Trim() ?? "";
            try
            {
                await CaptureAddressesAsync([field]);
                RefreshLedger();
                DemoStatus.Text = "Address observation recorded. Unknown scope and conflicts remain reviewable.";
            }
            catch (Exception exception) { DemoStatus.Text = exception.Message; }
        };
        MergeObservationsButton.Click += (_, _) => ReviewAddresses(ReviewAction.Merge);
        SeparateObservationsButton.Click += (_, _) => ReviewAddresses(ReviewAction.KeepSeparate);
        RejectObservationsButton.Click += (_, _) => ReviewAddresses(ReviewAction.Reject);
        UndoCoalescingButton.Click += (_, _) =>
        {
            if (_capturingAddresses) return;
            try
            {
                var decisions = _coalescer.Snapshot().Decisions;
                var last = decisions.LastOrDefault(d => d.Action != ReviewAction.Undo
                    && !decisions.Any(u => u.Action == ReviewAction.Undo && u.TargetDecision == d.Id));
                if (last is null) { DemoStatus.Text = "No active review decision to undo."; return; }
                _coalescer.Undo(last.Id, "local-user", MergeReasonBox.Text ?? "Undo review");
                RefreshLedger(); DemoStatus.Text = "Review undone; original observations and audit history retained.";
            }
            catch (Exception exception) { DemoStatus.Text = exception.Message; }
        };
        SaveCoalescingButton.Click += async (_, _) =>
        {
            try
            {
                var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "Save address resolutions",
                    SuggestedFileName = "address-ledger.json", DefaultExtension = "json" });
                if (file?.TryGetLocalPath() is { } path)
                {
                    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(_coalescer.Snapshot(), WorkspaceJson));
                    DemoStatus.Text = "Address observations, resolutions and review history saved.";
                }
            }
            catch (Exception exception) { DemoStatus.Text = "Save failed: " + exception.Message; }
        };
        LoadCoalescingButton.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Load address resolutions", AllowMultiple = false });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
            try
            {
                var snapshot = JsonSerializer.Deserialize<CoalescingSnapshot>(await File.ReadAllTextAsync(path))
                    ?? throw new InvalidDataException("Empty address ledger.");
                _coalescer = AddressCoalescer.Restore(snapshot);
                _fieldObservations.Clear(); _checkedObservations.Clear();
                var undone = snapshot.Decisions.Where(d => d.Action == ReviewAction.Undo).Select(d => d.TargetDecision).ToHashSet();
                var rejected = snapshot.Decisions.Where(d => d.Action == ReviewAction.Reject && !undone.Contains(d.Id))
                    .SelectMany(d => d.Observations).ToHashSet();
                foreach (var group in snapshot.Observations.Where(o => !rejected.Contains(o.Id)
                    && o.Evidence.Any(e => e.SourceHash == _ledger.SourceHash)
                    && _ledger.Fields.Any(f => f.Id == o.LocalField)).GroupBy(o => o.LocalField))
                    if (group.Count() == 1) _fieldObservations[group.Key] = group.Single().Id;
                RefreshLedger(); DemoStatus.Text = "Address ledger restored with its review history.";
            }
            catch (Exception exception) { DemoStatus.Text = "Address ledger rejected: " + exception.Message; }
        };
        ReturnToImportButton.Click += async (_, _) =>
        {
            try
            {
                await Viewer.OpenAsync(_currentDocumentPath);
                _selection = null; SelectionText.Text = "Use Select region, then drag over the current document.";
                _previewingEvidence = false; ReturnToImportButton.IsVisible = false;
                SetAddressEditingState(); RefreshLedger();
                if (_selected is not null) await SelectFieldAsync(_selected);
            }
            catch (Exception exception) { DemoStatus.Text = exception.Message; }
        };
    }

    private static string EvidenceText(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private async Task CaptureAddressesAsync(IEnumerable<DemoField> fields, bool reconcileRemoved = false)
    {
        if (_previewingEvidence || _capturingAddresses) return;
        _capturingAddresses = true; SetAddressEditingState();
        var version = _loadVersion;
        var source = _ledger.SourceHash;
        var representation = Viewer.RepresentationHash ?? "";
        var path = _currentDocumentPath;
        var representationPath = Path.Combine(_sampleDirectory, representation + ".pdf");
        var candidate = AddressCoalescer.Restore(_coalescer.Snapshot());
        var mapping = new Dictionary<string, string>(_fieldObservations);
        try
        {
            Directory.CreateDirectory(_sampleDirectory);
            if (!File.Exists(representationPath)) await Viewer.SaveRepresentationAsync(representationPath);
            if (version != _loadVersion) return;
            var currentFields = fields.ToArray();
            if (reconcileRemoved)
                foreach (var missing in mapping.Keys.Except(currentFields.Select(f => f.Id)).ToArray())
                {
                    candidate.Reject([mapping[missing]], "local-user", "Field removed by YAML review; source evidence retained.");
                    mapping.Remove(missing);
                }
            foreach (var field in currentFields)
            {
                var role = field.CanonicalRole.Length > 0 ? field.CanonicalRole : FieldRoleAlignment.AddressRole(field.Label, field.Section);
                if ((role.Length == 0 && !field.Label.Contains("address", StringComparison.OrdinalIgnoreCase))
                    || field.Value.Length == 0 || field.Page is not int page || field.Regions.Count == 0)
                {
                    if (mapping.Remove(field.Id, out var obsolete))
                        candidate.Reject([obsolete], "local-user", "Field no longer supplies an address observation; evidence retained.");
                    continue;
                }
                var evidence = new List<SourceEvidence>();
                foreach (var region in field.Regions)
                {
                    var text = await Viewer.GetEvidenceTextAsync(page, region.ToRegion());
                    if (version != _loadVersion) return;
                    var bounds = new EvidenceRegion(region.X, region.Y, region.Width, region.Height);
                    evidence.Add(new() { Id = AddressCoalescer.StableId("evidence", source, representation, page.ToString(), JsonSerializer.Serialize(bounds)),
                        SourceHash = source, Representation = representation, SourcePath = path, RepresentationPath = representationPath, Page = page,
                        Region = bounds, Text = text });
                }
                var scope = new FieldScope(field.Entity, role, field.Period);
                var parts = AddressNormalizer.Parse(field.Value, field.AddressComponent);
                var id = AddressCoalescer.StableId("observation", source, representation, field.Id, field.Value,
                    JsonSerializer.Serialize(scope), JsonSerializer.Serialize(parts), field.AssemblyGroup,
                    JsonSerializer.Serialize(evidence.Select(e => e.Id).ToArray()));
                var observation = new AddressObservation
                {
                    Id = id, Label = field.Label, Section = field.Section, Scope = scope, RawValue = field.Value, Parts = parts,
                    Evidence = evidence, SourceChecked = EvidenceText(string.Join(" ", evidence.Select(e => e.Text)))
                        .Contains(EvidenceText(field.Value), StringComparison.OrdinalIgnoreCase),
                    IsFragment = field.AddressComponent.Length > 0, AssemblyGroup = field.AssemblyGroup,
                    LocalField = field.Id, Variant = field.Selector, Producer = "document-viewer:" + field.Selector,
                    LineageRoot = source, IndependentOriginConfirmed = Path.GetFileName(path) == "sample-addresses.html"
                };
                if (mapping.TryGetValue(field.Id, out var previous) && previous != id)
                    candidate.Reject([previous], "local-user", "Field correction superseded this mapping; original evidence retained.");
                candidate.Add(observation);
                // Reverting a correction reactivates its retained observation through explicit undo events.
                var reviews = candidate.Snapshot().Decisions;
                foreach (var rejection in reviews.Where(d => d.Action == ReviewAction.Reject && d.Observations.Contains(id)
                    && !reviews.Any(u => u.Action == ReviewAction.Undo && u.TargetDecision == d.Id)))
                    candidate.Undo(rejection.Id, "local-user", "Field review restored this observation.");
                mapping[field.Id] = id;
            }
            if (version != _loadVersion) return;
            _coalescer = candidate;
            _fieldObservations.Clear(); foreach (var item in mapping) _fieldObservations.Add(item.Key, item.Value);
        }
        finally { _capturingAddresses = false; SetAddressEditingState(); }
    }

    private void SetAddressEditingState()
    {
        var editable = !_previewingEvidence && !_capturingAddresses;
        AddFieldButton.IsEnabled = editable; NewTemplateButton.IsEnabled = editable;
        SuggestButton.IsEnabled = editable && !_suggesting;
        ConfirmButton.IsEnabled = editable && _selected is not null;
        UseSelectionButton.IsEnabled = editable && _selected is not null && !string.IsNullOrWhiteSpace(_selection?.Text);
        ApplyLedgerButton.IsEnabled = editable; ApplyAddressScopeButton.IsEnabled = editable;
        MergeObservationsButton.IsEnabled = !_capturingAddresses;
        SeparateObservationsButton.IsEnabled = !_capturingAddresses;
        RejectObservationsButton.IsEnabled = !_capturingAddresses;
        UndoCoalescingButton.IsEnabled = !_capturingAddresses;
        LoadCoalescingButton.IsEnabled = !_capturingAddresses;
    }

    private void ReviewAddresses(ReviewAction action)
    {
        if (_capturingAddresses) return;
        try
        {
            var ids = _checkedObservations.ToArray();
            var reason = MergeReasonBox.Text ?? "";
            if (action == ReviewAction.Merge) _coalescer.Merge(ids, "local-user", reason,
                new(EntityScopeBox.Text?.Trim() ?? "", CanonicalRoleBox.Text?.Trim() ?? "", PeriodScopeBox.Text?.Trim() ?? ""));
            else if (action == ReviewAction.Reject) _coalescer.Reject(ids, "local-user", reason);
            else _coalescer.KeepSeparate(ids, "local-user", reason);
            _checkedObservations.Clear(); RefreshLedger(); DemoStatus.Text = $"Recorded {action} review. Source observations retained.";
        }
        catch (Exception exception) { DemoStatus.Text = "Review rejected: " + exception.Message; }
    }

    private void RefreshCoalescing()
    {
        var snapshot = _coalescer.Snapshot();
        var constraints = snapshot.Observations.Where(o => o.Scope.IsKnown()).Select(o => o.Scope).Distinct().Select(s => new FieldConstraint(s));
        var result = _coalescer.Resolve(constraints, AutomaticCoalescingBox.IsChecked == true);
        _ledger.Coalescing = _coalescer.Snapshot();
        var rejected = snapshot.Decisions.Where(d => d.Action == ReviewAction.Reject
            && !snapshot.Decisions.Any(u => u.Action == ReviewAction.Undo && u.TargetDecision == d.Id)).SelectMany(d => d.Observations).ToHashSet();
        CoalescingSummary.Text = $"{snapshot.Observations.Count - rejected.Count} active observations → {result.Fields.Count} resolved fields";
        CoalescingConflicts.Text = string.Join("\n", result.Comparisons.Where(p => p.Kind is MatchKind.Conflicting or MatchKind.Unresolved)
            .Select(p => p.Reason).Concat(result.CardinalityIssues.Select(i => i.Scope.Role + ": " + i.Reason)).Distinct());
        CoalescingHistory.Text = $"{snapshot.Decisions.Count} review events · {_ledger.Coalescing.History.Count} resolution revisions";
        ResolvedFieldsPanel.Children.Clear(); ObservationPanel.Children.Clear();
        foreach (var field in result.Fields.OrderBy(f => f.Value.Unit.Length > 0).ThenBy(f => f.Scope.Role == "property.address" ? 0 : 1))
        {
            var panel = new StackPanel { Spacing = 5, Margin = new Thickness(8) };
            panel.Children.Add(new TextBlock { Text = $"{field.Scope.Role} · {field.Scope.Entity} · {field.Scope.Period}", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });
            panel.Children.Add(new TextBlock { Text = field.Value.Display(), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = $"{field.Observations.Count} observations · {field.EvidenceIds.Count} regions · {field.ConfirmedIndependentOrigins} confirmed origin(s)"
                + (field.HasUnknownLineage ? " · lineage partly unknown" : "") + (field.NeedsReview ? " · needs review" : ""), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            panel.Children.Add(new TextBlock { Text = field.Rationale, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            var evidencePanel = new WrapPanel();
            foreach (var evidence in snapshot.Observations.Where(o => field.Observations.Contains(o.Id)).SelectMany(o => o.Evidence).DistinctBy(e => e.Id))
            {
                var owner = snapshot.Observations.First(o => field.Observations.Contains(o.Id) && o.Evidence.Any(e => e.Id == evidence.Id));
                var button = new Button { Name = "Source_" + (owner.LocalField.Length > 0 ? owner.LocalField : owner.Id), Content = $"{owner.Label} · page {evidence.Page + 1}", FontSize = 11, Padding = new Thickness(5, 3), Margin = new Thickness(0, 0, 4, 4) };
                button.Click += async (_, _) => await ShowAddressEvidenceAsync(evidence);
                evidencePanel.Children.Add(button);
            }
            panel.Children.Add(evidencePanel);
            ResolvedFieldsPanel.Children.Add(new Border { Child = panel, Background = Brushes.White, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1) });
        }
        foreach (var observation in snapshot.Observations.Where(o => !rejected.Contains(o.Id)))
        {
            var name = _fieldObservations.GetValueOrDefault(observation.LocalField) == observation.Id
                ? "Observation_" + observation.LocalField : "Observation_" + observation.Id;
            var checkbox = new CheckBox { Name = name, IsChecked = _checkedObservations.Contains(observation.Id),
                Content = new TextBlock { Text = $"{observation.Label}: {observation.RawValue}", TextWrapping = TextWrapping.Wrap } };
            checkbox.IsCheckedChanged += (_, _) => { if (checkbox.IsChecked == true) _checkedObservations.Add(observation.Id); else _checkedObservations.Remove(observation.Id); };
            ObservationPanel.Children.Add(checkbox);
        }
    }

    private async Task ShowAddressEvidenceAsync(SourceEvidence evidence)
    {
        if (_capturingAddresses) return;
        try
        {
            if (Viewer.RepresentationHash != evidence.Representation)
            {
                if (!File.Exists(evidence.RepresentationPath)) throw new FileNotFoundException("Recorded PDF is unavailable; the ledger retains its evidence identity.");
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(evidence.RepresentationPath)));
                if (hash != evidence.Representation) throw new InvalidDataException("Recorded PDF bytes changed.");
                await Viewer.OpenAsync(evidence.RepresentationPath);
            }
            _selection = null;
            _previewingEvidence = Viewer.SourcePath != _currentDocumentPath;
            ReturnToImportButton.IsVisible = _previewingEvidence; SetAddressEditingState();
            if (Viewer.RepresentationHash != evidence.Representation)
                throw new InvalidDataException("Rendered representation changed; stored coordinates need review.");
            if (evidence.Page is int page && evidence.Region is { } region)
            {
                Viewer.SetHighlights([new("address-evidence", page, new PageRegion(region.X, region.Y, region.Width, region.Height), "Address source")]);
                await Viewer.ShowHighlightAsync("address-evidence");
                DemoStatus.Text = "Source evidence: " + evidence.Text;
            }
        }
        catch (Exception exception) { DemoStatus.Text = exception.Message; }
    }
}
