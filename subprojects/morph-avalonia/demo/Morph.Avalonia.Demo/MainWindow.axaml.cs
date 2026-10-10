using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Mostlylucid.Morph.Documents;
using Mostlylucid.DocumentLedger.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Morph.Avalonia.Demo;

public partial class MainWindow : Window
{
    private readonly string _sampleDirectory = Path.Combine(Path.GetTempPath(), "morph-avalonia", Guid.NewGuid().ToString("N"));
    private readonly ISerializer _serializer = new SerializerBuilder().WithTypeConverter(new YamlDotNet.Serialization.Converters.DateTimeOffsetConverter()).WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
    private readonly IDeserializer _deserializer = new DeserializerBuilder().WithTypeConverter(new YamlDotNet.Serialization.Converters.DateTimeOffsetConverter()).WithNamingConvention(UnderscoredNamingConvention.Instance)
        .WithTypeMapping<IReadOnlyList<AddressObservation>, List<AddressObservation>>()
        .WithTypeMapping<IReadOnlyList<SourceEvidence>, List<SourceEvidence>>()
        .WithTypeMapping<IReadOnlyList<ReviewDecision>, List<ReviewDecision>>()
        .WithTypeMapping<IReadOnlyList<ResolvedField>, List<ResolvedField>>()
        .WithTypeMapping<IReadOnlyList<string>, List<string>>()
        .WithTypeMapping<IReadOnlyDictionary<string, IReadOnlyList<string>>, Dictionary<string, IReadOnlyList<string>>>()
        .Build();
    private DemoLedger _ledger = new();
    private DemoField? _selected;
    private DocumentSelection? _selection;
    private int _loadVersion;
    private string[] _documentQueue = [];
    private int _queueIndex;
    private CancellationTokenSource? _suggestionCancellation;

    public MainWindow()
    {
        InitializeComponent();
        InitializeCoalescing();
        OpenButton.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new()
            {
                Title = "Open document",
                FileTypeFilter = [new("Documents") { Patterns = ["*.pdf", "*.docx", "*.html", "*.htm"] }]
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) await OpenDocumentAsync(path);
        };
        NextDocumentButton.Click += async (_, _) =>
        {
            if (_documentQueue.Length == 0) return;
            _queueIndex = (_queueIndex + 1) % _documentQueue.Length;
            await OpenDocumentAsync(_documentQueue[_queueIndex]);
        };
        WordSampleButton.Click += async (_, _) => await OpenDocumentAsync(SampleDocument.Create(_sampleDirectory));
        HtmlSampleButton.Click += async (_, _) => await OpenDocumentAsync(SampleDocument.CreateHtml(_sampleDirectory));
        Viewer.RegionSelected += (_, selection) =>
        {
            _selection = selection;
            SelectionText.Text = selection.Text;
            SelectionLocation.Text = $"Page {selection.PageIndex + 1} · x {selection.Region.X:F3} · y {selection.Region.Y:F3}";
            SetAddressEditingState();
        };
        Viewer.HighlightSelected += async (_, highlight) =>
        {
            var field = _ledger.Fields.Find(f => f.Id == highlight.Id);
            if (field is not null) await SelectFieldAsync(field);
        };
        NewTemplateButton.Click += (_, _) =>
        {
            _suggestionCancellation?.Cancel();
            _ledger.Fields.Clear();
            _coalescer = new(); _fieldObservations.Clear(); _checkedObservations.Clear();
            _ledger.Producer = "manual";
            _selected = null;
            ConfirmButton.IsEnabled = false;
            UseSelectionButton.IsEnabled = false;
            SelectedFieldLabel.Text = "Select a field";
            FieldValueBox.Text = "";
            RefreshLedger();
            DemoStatus.Text = "Blank template. Add a field from a region, or suggest fields with your local LLM.";
        };
        AddFieldButton.Click += async (_, _) => await AddFieldAsync();
        SuggestButton.Click += async (_, _) => await SuggestFieldsAsync();
        ConfirmButton.Click += async (_, _) =>
        {
            if (_selected is null || _previewingEvidence || _capturingAddresses) return;
            RecordFeedback(_selected, "confirm-correction", FieldValueBox.Text ?? "");
            _selected.Value = FieldValueBox.Text ?? "";
            var field = _selected;
            var version = _loadVersion;
            field.Anchor = AnchorBox.Text?.Trim() ?? "";
            field.Section = SectionBox.Text?.Trim() ?? "";
            field.Status = "confirmed";
            try
            {
                await SeedCentroidAsync(field);
                if (version != _loadVersion) return;
                field.Centroid.Reviewed = true;
                _ledger.ReviewedNeighborhoods.RemoveAll(n => n.Field == field.Id && n.SourceHash == _ledger.SourceHash);
                _ledger.ReviewedNeighborhoods.Add(new() { Field = field.Id, SourceHash = _ledger.SourceHash,
                    Anchor = field.Anchor, Section = field.Section, Features = field.Centroid });
                field.Centroid.IndependentExamples = _ledger.ReviewedNeighborhoods.Where(n => n.Field == field.Id)
                    .Select(n => n.SourceHash).Distinct().Count();
                await CaptureAddressesAsync([field]);
                if (version != _loadVersion) return;
                RefreshLedger();
                DemoStatus.Text = $"Confirmed {field.Label}. Draft template retains anchor, section and page neighborhood.";
            }
            catch (Exception exception) { DemoStatus.Text = exception.Message; }
        };
        UseSelectionButton.Click += (_, _) =>
        {
            if (_selected is null || _selection is null || _previewingEvidence || _capturingAddresses) return;
            RecordFeedback(_selected, "replace-evidence", _selection.Text.Trim());
            _selected.Value = _selection.Text.Trim();
            _selected.Page = _selection.PageIndex;
            _selected.Regions = [DemoRegion.From(_selection.Region)];
            _selected.Selector = "reviewed-region";
            _selected.Status = "proposed";
            _selected.Centroid.ValuePositions = _selected.Regions;
            _selected.Centroid.Reviewed = false;
            _selected.Centroid.Relation = "reviewed-region";
            FieldValueBox.Text = _selected.Value;
            RefreshLedger();
            DemoStatus.Text = "Source region attached. Confirm the value to record your correction.";
        };
        ApplyLedgerButton.Click += async (_, _) => await ApplyYamlAsync();
        Closed += async (_, _) => { ++_loadVersion; _suggestionCancellation?.Cancel(); await Viewer.DisposeAsync(); };
        Opened += async (_, _) =>
        {
            var documentIndex = Array.IndexOf(Program.Arguments, "--document");
            var path = documentIndex >= 0 && documentIndex + 1 < Program.Arguments.Length
                ? Program.Arguments[documentIndex + 1] : SampleDocument.Create(_sampleDirectory);
            await OpenDocumentAsync(path);
            if (Program.Arguments.Contains("--addresses")) await OpenDocumentAsync(SampleDocument.CreateAddresses(_sampleDirectory));
            var captureIndex = Array.IndexOf(Program.Arguments, "--capture");
            if (captureIndex >= 0 && captureIndex + 1 < Program.Arguments.Length)
            {
                await Task.Delay(350);
                Dispatcher.UIThread.RunJobs();
                using var image = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height));
                image.Render(this);
                image.Save(Program.Arguments[captureIndex + 1], PngBitmapEncoderOptions.Default);
                if (Program.Arguments.Contains("--capture-only")) Close();
            }
        };
    }

    private async Task OpenDocumentAsync(string path)
    {
        var version = ++_loadVersion;
        _suggestionCancellation?.Cancel();
        var reusable = ReuseTemplateBox.IsChecked == true ? _ledger.Fields.ToArray() : [];
        try
        {
            DemoStatus.Text = "Rendering and extracting label neighborhoods…";
            await Viewer.OpenAsync(path);
            if (version != _loadVersion) return;
            _currentDocumentPath = Path.GetFullPath(path); _previewingEvidence = false;
            ReturnToImportButton.IsVisible = false;
            if (reusable.Length == 0) _coalescer = new();
            _fieldObservations.Clear(); _checkedObservations.Clear();
            var ledger = new DemoLedger { Source = Path.GetFileName(path),
                SourceHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))),
                Producer = reusable.Length > 0 ? "draft-template-replay" : "manual",
                Feedback = reusable.Length > 0 ? _ledger.Feedback.ToList() : [],
                ReviewedNeighborhoods = reusable.Length > 0 ? _ledger.ReviewedNeighborhoods.ToList() : [] };
            var definitions = reusable.Length > 0
                ? reusable.Select(f => (f.Id, f.Label, f.Anchor, f.Section)).ToArray()
                : Path.GetFileName(path) == "sample-addresses.html"
                    ? new[] {
                        ("property_address", "Property address", "Property address:", "Property details"),
                        ("subject_property", "Subject property", "Subject property:", "Property details"),
                        ("street", "Street", "Street:", "Property details"),
                        ("town", "Town", "Town:", "Property details"),
                        ("postcode", "Postcode", "Postcode:", "Property details"),
                        ("correspondence_address", "Correspondence address", "Correspondence address:", "Correspondence"),
                        ("unit_two", "Property unit two", "Property unit two:", "Property details"),
                        ("unit_three", "Property unit three", "Property unit three:", "Property details") }
                : Path.GetFileName(path).StartsWith("sample-", StringComparison.Ordinal)
                    ? new[] {
                        ("applicant", "Applicant", "Applicant:", "APPLICATION RECORD"),
                        ("reference", "Reference", "Reference:", "APPLICATION RECORD"),
                        ("date_of_birth", "Date of birth", "Date of birth:", "APPLICATION RECORD"),
                        ("requested_amount", "Requested amount", "Requested amount:", "APPLICATION RECORD"),
                        ("property_address", "Property address", "Property:", "APPLICATION RECORD") }
                    : [];
            // Deterministic demo selector: label + same-line value. No hardcoded values.
            foreach (var definition in definitions)
            {
                var anchors = await Viewer.SearchEvidenceAsync(definition.Item3);
                if (version != _loadVersion) return;
                var field = new DemoField { Id = definition.Item1, Label = definition.Item2,
                    Anchor = definition.Item3, Section = definition.Item4, Status = "missing" };
                if (reusable.FirstOrDefault(f => f.Id == field.Id) is { } pattern)
                {
                    field.CanonicalRole = pattern.CanonicalRole;
                    field.AddressComponent = pattern.AddressComponent;
                    // Entity, period and reviewed assembly membership are source assertions, not reusable selectors.
                }
                if (Path.GetFileName(path) == "sample-addresses.html")
                {
                    field.CanonicalRole = field.Id == "correspondence_address" ? "correspondence.address" : "property.address";
                    field.Entity = field.Id == "correspondence_address" ? "applicant-a" : "property-a";
                    field.Period = "2026-10";
                    field.AddressComponent = field.Id switch { "street" => "street", "town" => "locality", "postcode" => "postcode", _ => "" };
                    field.AssemblyGroup = field.AddressComponent.Length > 0 ? "property-boxes" : "";
                }
                if (anchors.Count > 0)
                {
                    var anchor = anchors[0];
                    var pageText = await Viewer.GetPageTextAsync(anchor.PageIndex);
                    var line = Regex.Match(pageText, Regex.Escape(field.Anchor) + @"\s*([^\r\n]+)");
                    if (line.Success)
                    {
                        field.Value = line.Groups[1].Value.Trim();
                        var values = await Viewer.SearchEvidenceAsync(field.Value);
                        var evidence = values.Where(m => m.PageIndex == anchor.PageIndex)
                            .OrderBy(m => Math.Abs(m.Regions[0].Y - anchor.Regions[0].Y) * 10
                                + Math.Abs(m.Regions[0].X - anchor.Regions[0].X)).FirstOrDefault();
                        field.Page = anchor.PageIndex;
                        field.Regions = (evidence?.Regions ?? anchor.Regions).Select(DemoRegion.From).ToList();
                        field.Status = "proposed";
                    }
                }
                await SeedCentroidAsync(field);
                ledger.Fields.Add(field);
            }
            if (version != _loadVersion) return;
            _ledger = ledger;
            await CaptureAddressesAsync(_ledger.Fields);
            if (version != _loadVersion) return;
            if (!Path.GetFileName(path).StartsWith("sample-", StringComparison.Ordinal))
            {
                _documentQueue = Directory.GetFiles(Path.GetDirectoryName(path)!, "*" + Path.GetExtension(path)).Order(StringComparer.Ordinal).ToArray();
                _queueIndex = Array.IndexOf(_documentQueue, path);
            }
            else _documentQueue = [];
            NextDocumentButton.IsVisible = _documentQueue.Length > 1;
            _selected = null;
            _selection = null;
            UseSelectionButton.IsEnabled = false;
            RefreshLedger();
            if (_ledger.Fields.Count > 0) await SelectFieldAsync(_ledger.Fields[0]);
            else
            {
                ConfirmButton.IsEnabled = false;
                SelectedFieldLabel.Text = "Build the initial template";
                FieldValueBox.Text = "";
            }
            DemoStatus.Text = _ledger.Fields.Count == 0
                ? "New document. Select a region and add a field, or use Suggest with local LLM."
                : $"{_ledger.Fields.Count(f => f.Status == "proposed")} proposed fields · deterministic label selectors · session-only concept demo";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (version == _loadVersion) DemoStatus.Text = exception.Message; }
    }

    private async Task SelectFieldAsync(DemoField field)
    {
        if (_previewingEvidence) return;
        _selected = field;
        SelectedFieldLabel.Text = field.Label;
        FieldValueBox.Text = field.Value;
        AnchorBox.Text = field.Anchor;
        SectionBox.Text = field.Section;
        CanonicalRoleBox.Text = field.CanonicalRole.Length > 0 ? field.CanonicalRole : Mostlylucid.DocumentLedger.Core.FieldRoleAlignment.AddressRole(field.Label, field.Section);
        EntityScopeBox.Text = field.Entity;
        PeriodScopeBox.Text = field.Period;
        AddressComponentBox.Text = field.AddressComponent;
        AssemblyGroupBox.Text = field.AssemblyGroup;
        ConfirmButton.IsEnabled = true;
        SelectionLocation.Text = field.Page is int p ? $"Page {p + 1} · {field.Anchor} · {field.Status}" : "No matching source yet. Draw a region to supply evidence.";
        UseSelectionButton.IsEnabled = _selection is not null && !string.IsNullOrWhiteSpace(_selection.Text);
        SetAddressEditingState();
        try { await Viewer.ShowHighlightAsync(field.Id); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { DemoStatus.Text = exception.Message; }
    }

    private void RefreshLedger()
    {
        EvidencePanel.Children.Clear();
        var highlights = new List<DocumentHighlight>();
        foreach (var field in _ledger.Fields)
        {
            var content = new StackPanel { Spacing = 3 };
            content.Children.Add(new TextBlock { Text = $"{field.Label} · {field.Status}", FontSize = 12 });
            content.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(field.Value) ? "—" : field.Value,
                TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, FontSize = 15, FontWeight = global::Avalonia.Media.FontWeight.SemiBold });
            var button = new Button { Name = "Field_" + field.Id, Content = content, Padding = new Thickness(12,8),
                Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.Parse("#D7E1E9")), BorderThickness = new Thickness(1),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Left };
            button.Click += async (_, _) => await SelectFieldAsync(field);
            EvidencePanel.Children.Add(button);
            if (field.Page is int page)
                highlights.AddRange(field.Regions.Select(r => new DocumentHighlight(field.Id, page, r.ToRegion(), field.Label)));
        }
        if (!_previewingEvidence) Viewer.SetHighlights(highlights);
        RefreshCoalescing();
        LedgerEditor.Text = _serializer.Serialize(_ledger);
    }

    private void RecordFeedback(DemoField field, string action, string value) => _ledger.Feedback.Add(new()
    {
        Field = field.Id, Action = action, PreviousValue = field.Value, Value = value, Source = _ledger.SourceHash
    });

    private string NewFieldId(string label)
    {
        var stem = Regex.Replace(label.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
        if (stem.Length == 0) stem = "field";
        var id = stem;
        for (var i = 2; _ledger.Fields.Any(f => f.Id == id); i++) id = $"{stem}_{i}";
        return id;
    }

    private async Task AddFieldAsync()
    {
        if (_previewingEvidence || _capturingAddresses) return;
        var label = NewFieldName.Text?.Trim();
        if (string.IsNullOrEmpty(label)) { DemoStatus.Text = "Enter a field name first."; return; }
        var field = new DemoField { Id = NewFieldId(label), Label = label, Anchor = label,
            Value = _selection?.Text.Trim() ?? "", Page = _selection?.PageIndex,
            Regions = _selection is null ? [] : [DemoRegion.From(_selection.Region)],
            Selector = _selection is null ? "label-relative" : "reviewed-region", Status = _selection is null ? "missing" : "proposed" };
        try
        {
            var version = _loadVersion;
            await SeedCentroidAsync(field);
            if (version != _loadVersion) return;
            _ledger.Fields.Add(field);
            await CaptureAddressesAsync([field]);
            if (version != _loadVersion) return;
            RecordFeedback(field, "add-field", field.Value);
            RefreshLedger();
            await SelectFieldAsync(field);
            NewFieldName.Text = "";
            DemoStatus.Text = $"Added {label}. Select its evidence and confirm; the initial neighborhood is in YAML.";
        }
        catch (Exception exception) { DemoStatus.Text = exception.Message; }
    }

    private async Task SeedCentroidAsync(DemoField field)
    {
        var anchors = await Viewer.SearchEvidenceAsync(field.Anchor);
        var anchor = anchors.FirstOrDefault(m => field.Page is null || m.PageIndex == field.Page);
        if (field.Regions.Count == 0 && !string.IsNullOrWhiteSpace(field.Value))
        {
            var values = await Viewer.SearchEvidenceAsync(field.Value);
            var evidence = values.FirstOrDefault(v => anchor is null || v.PageIndex == anchor.PageIndex);
            if (evidence is not null)
            {
                field.Page = evidence.PageIndex;
                field.Regions = evidence.Regions.Select(DemoRegion.From).ToList();
            }
        }
        var pageIndex = field.Page ?? anchor?.PageIndex;
        var neighbors = new List<string>();
        if (pageIndex is int page)
        {
            var text = await Viewer.GetPageTextAsync(page);
            var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var index = Array.FindIndex(lines, line => line.Contains(field.Anchor, StringComparison.OrdinalIgnoreCase));
            if (index < 0 && field.Value.Length > 0) index = Array.FindIndex(lines, line => line.Contains(field.Value, StringComparison.Ordinal));
            if (index >= 0) neighbors.AddRange(lines.Skip(Math.Max(0, index - 2)).Take(5));
        }
        field.Centroid = new()
        {
            Label = field.Label, Neighbors = neighbors, IndependentExamples = field.Page is null ? 0 : 1,
            AnchorPositions = anchor?.Regions.Select(DemoRegion.From).ToList() ?? [],
            ValuePositions = field.Regions.ToList(), Relation = field.Selector == "reviewed-region" ? "selected-region" : "same-line-after-label",
            Embedding = new() { LabelInput = field.Label + " | " + field.Anchor,
                NeighborhoodInput = string.Join(" | ", new[] { field.Section, field.Label }.Concat(neighbors)) }
        };
    }

    private async Task SuggestFieldsAsync()
    {
        if (Viewer.PageCount == 0 || _previewingEvidence || _capturingAddresses) return;
        _suggestionCancellation?.Cancel();
        _suggestionCancellation?.Dispose();
        _suggestionCancellation = new CancellationTokenSource();
        var token = _suggestionCancellation.Token;
        var version = _loadVersion;
        _suggesting = true;
        SuggestButton.IsEnabled = false;
        try
        {
            DemoStatus.Text = "Local model is proposing an initial template…";
            var text = new System.Text.StringBuilder();
            for (var page = 0; page < Math.Min(3, Viewer.PageCount); page++)
                text.AppendLine(await Viewer.GetPageTextAsync(page, token));
            var input = text.ToString();
            if (input.Length > 16000) input = input[..16000];
            var reviewed = _ledger.Fields.Where(f => f.Status == "confirmed")
                .Select(f => $"{f.Label}: anchor={f.Anchor}; section={f.Section}; value={f.Value}").ToArray();
            if (reviewed.Length > 0)
                input += "\nReviewed user corrections (retain these semantic roles and anchors):\n" + string.Join("\n", reviewed);
            var proposed = await LocalTemplateStarter.SuggestAsync(EndpointBox.Text ?? "", ModelBox.Text ?? "", input, token);
            token.ThrowIfCancellationRequested();
            if (version != _loadVersion) return;
            var added = 0;
            foreach (var suggestion in proposed)
            {
                var value = suggestion.Value.Trim();
                var values = await Viewer.SearchEvidenceAsync(value, token);
                if (values.Count == 0)
                {
                    value = value.Trim('"', '\'', '`');
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    values = await Viewer.SearchEvidenceAsync(value, token);
                }
                var suggestedAnchor = suggestion.Anchor.Trim().Trim('"', '\'', '`');
                if (suggestedAnchor.Length == 0) continue;
                var anchors = await Viewer.SearchEvidenceAsync(suggestedAnchor, token);
                var evidence = values.FirstOrDefault(v => anchors.Any(a => a.PageIndex == v.PageIndex));
                if (evidence is null || evidence.Regions.Count == 0) continue;
                if (_ledger.Fields.Any(f => f.Label.Equals(suggestion.Label, StringComparison.OrdinalIgnoreCase))) continue;
                var anchorText = suggestedAnchor;
                var pageText = await Viewer.GetPageTextAsync(evidence.PageIndex, token);
                var sourceLine = pageText.Split(['\r','\n'], StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(line => line.Contains(value, StringComparison.Ordinal));
                if (sourceLine is not null)
                {
                    var colon = sourceLine.IndexOf(':');
                    var valueStart = sourceLine.IndexOf(value, StringComparison.Ordinal);
                    if (colon >= 0 && colon < valueStart) anchorText = sourceLine[..(colon + 1)].Trim();
                }
                var field = new DemoField { Id = NewFieldId(suggestion.Label), Label = suggestion.Label,
                    Value = value, Anchor = anchorText, Section = suggestion.Section ?? "",
                    Page = evidence.PageIndex, Regions = evidence.Regions.Select(DemoRegion.From).ToList() };
                await SeedCentroidAsync(field);
                token.ThrowIfCancellationRequested();
                if (version != _loadVersion) return;
                _ledger.Fields.Add(field);
                added++;
            }
            _ledger.Producer = "ollama:" + ModelBox.Text;
            await CaptureAddressesAsync(_ledger.Fields);
            if (version != _loadVersion) return;
            RefreshLedger();
            if (_ledger.Fields.Count > 0) await SelectFieldAsync(_ledger.Fields[0]);
            DemoStatus.Text = $"Local LLM added {added} source-checked draft fields. Review or override them; corrections feed the template.";
        }
        catch (OperationCanceledException) { if (version == _loadVersion) DemoStatus.Text = "Template suggestion cancelled."; }
        catch (Exception exception) { if (version == _loadVersion) DemoStatus.Text = $"Local LLM unavailable: {exception.Message}. Add fields manually."; }
        finally { _suggesting = false; SetAddressEditingState(); }
    }

    private async Task ApplyYamlAsync()
    {
        if (_previewingEvidence || _capturingAddresses) return;
        try
        {
            var parsed = _deserializer.Deserialize<DemoLedger>(LedgerEditor.Text ?? "") ?? throw new InvalidDataException("Ledger is empty.");
            if (parsed.Version != 1 || parsed.Source != _ledger.Source || parsed.Fields is null || parsed.Fields.Count > 50 || parsed.SourceHash != _ledger.SourceHash)
                throw new InvalidDataException("Keep version and source; use at most 50 fields.");
            if (parsed.Fields.Any(f => f is null || !Regex.IsMatch(f.Id ?? "", "^[a-z][a-z0-9_]*$"))
                || parsed.Fields.Select(f => f.Id).Distinct().Count() != parsed.Fields.Count)
                throw new InvalidDataException("Use unique field IDs containing lowercase letters, digits and underscores.");
            foreach (var field in parsed.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.Label) || field.Value is null || field.Regions is null || field.Anchor is null || field.Section is null || field.Centroid is null
                    || field.CanonicalRole is null || field.Entity is null || field.Period is null || field.AddressComponent is null || field.AssemblyGroup is null)
                    throw new InvalidDataException("Fields require label, value, anchor, section and regions.");
                if (field.Status is not ("proposed" or "confirmed" or "missing")) throw new InvalidDataException("Status must be proposed, confirmed or missing.");
                if (field.Page is int page && (page < 0 || page >= Viewer.PageCount)) throw new InvalidDataException("Page is a zero-based index within this document.");
                if (field.Page is null && field.Regions.Count > 0) throw new InvalidDataException("Regions need a page index.");
                foreach (var region in field.Regions) _ = region.ToRegion();
            }
            var selectedId = _selected?.Id;
            // YAML edits also become feedback; do not let editing erase existing review history.
            parsed.Feedback = _ledger.Feedback;
            parsed.ReviewedNeighborhoods = _ledger.ReviewedNeighborhoods;
            parsed.Coalescing = _ledger.Coalescing;
            foreach (var field in parsed.Fields)
            {
                var old = _ledger.Fields.Find(f => f.Id == field.Id);
                if (old is null || _serializer.Serialize(old) != _serializer.Serialize(field))
                    RecordFeedback(old ?? field, "yaml-override", field.Value);
            }
            _ledger = parsed;
            await CaptureAddressesAsync(_ledger.Fields, reconcileRemoved: true);
            _selected = _ledger.Fields.Find(f => f.Id == selectedId);
            ConfirmButton.IsEnabled = _selected is not null;
            if (_selected is not null)
            {
                FieldValueBox.Text = _selected.Value;
                AnchorBox.Text = _selected.Anchor;
                SectionBox.Text = _selected.Section;
            }
            RefreshLedger();
            DemoStatus.Text = "YAML applied. Imported values and evidence updated.";
        }
        catch (Exception exception) { DemoStatus.Text = $"YAML rejected: {exception.Message}"; }
    }
}
