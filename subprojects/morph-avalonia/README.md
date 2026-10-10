# Morph for Avalonia — document ledger concept demo

A reusable native document viewer and a small demonstration of building extraction templates from documents. The document is on the left; imported fields and their editable ledger/template are on the right.

## Run

Requires .NET 10 and a desktop session. On this machine:

```sh
cd document-ledger/subprojects/morph-avalonia
dotnet run --project demo/Morph.Avalonia.Demo
```

Open a dataset invoice directly:

```sh
dotnet run --project demo/Morph.Avalonia.Demo -- \
  --document '/path/to/CompanyDocuments/invoices/invoice_10248.pdf'
```

1. An unfamiliar document starts with an empty imported record.
2. Draw a region, enter a field name and choose **Add**; or choose **Suggest with local LLM**.
3. Edit the field value, source anchor and optional section. **Confirm correction** records feedback and a reviewed neighborhood seed.
4. Inspect/edit **YAML**: labels, neighbors, normalized positions, evidence, pending embedding recipes and feedback.
5. Enable **Reuse this draft on the next document**, then choose **Next document**. The current demo replays same-line label selectors on the next file in that folder.

The synthetic Word and HTML examples start with a few supplied label selectors to show the review interaction immediately. **New template** clears the field set so those examples can also demonstrate a cold start.

**Settings** selects a loopback Ollama endpoint and an installed generative model. The default is `qwen3.5:0.8b`, present on the inspected machine. Suggestions use `/api/generate` with a JSON schema, up to three pages/16,000 characters and a 90-second timeout. No model is downloaded. Value and anchor text must match a common page before evidence is attached; proposals remain unconfirmed. Small-model coverage varies. Manual creation remains available without a model server.

## Address resolution

**Address example → Coalescing** starts with eight synthetic observations. Five property mentions/components resolve to one value; the correspondence role stays separate; Flat 2 and Flat 3 remain conflicting alternatives. Disable automatic coalescing to compare against eight raw observations. Select observations and record a reviewed merge, separation or rejection; undo retains the original event and evidence.

For a new address field, select it under Fields, then enter its canonical role, reviewed entity ID and effective period under Coalescing. Component fragments also need a reviewed source-block group. These assertions are not inferred by the current baseline. They appear in YAML with component evidence and versioned resolutions. Field changes supersede obsolete mappings without deleting their source evidence.

**Save resolutions / Load resolutions** persist the address ledger as JSON. Source links use the hashed derived PDF cached in the session's temporary directory. Keep those files with the snapshot; it is not a self-contained document archive. When reviewing another document's evidence, return to the current import before editing its fields.

```sh
dotnet run --project demo/Morph.Avalonia.Demo -- \
  --addresses --ux-test --script ux-scripts/coalescing.yaml --output artifacts/ux-coalescing
```

The independent [core project](../../src/Mostlylucid.DocumentLedger.Core/Mostlylucid.DocumentLedger.Core.csproj) has no Avalonia, Morph or model-server dependency. It supports a conservative UK-style address parser, deterministic scope/component guards and explicit review. Numerical embeddings, learned field aliases and inferred entities/periods remain follow-on work.

## Reusable components

| Project | Responsibility |
| --- | --- |
| `Mostlylucid.Morph.Documents` | PDF sessions; DOCX/HTML conversion; page images; text, search and normalized geometry; native lifetime |
| `Mostlylucid.Morph.Avalonia` | Page viewer; navigation/zoom/search; host highlights; pointer region selection |
| `Morph.Avalonia.Demo` | Two-panel UI, initial template creation, local generative starter, feedback and YAML editing |
| `Morph.Avalonia.Verification` | Focused engine/viewer checks and optional local corpus inspection |

Pinned packages: Morph.Pdf 1.21.0, Morph.PDFium 1.4.0 and Avalonia 12.1.2. Word/HTML convert once to an in-memory PDF; the same PDF supplies rendering and evidence coordinates. Existing PDFs are loaded directly. Bundled Noto Sans fonts have their [OFL license](src/Mostlylucid.Morph.Documents/Fonts/OFL.txt). Configure `DocumentRenderOptions` before opening to use a different font directory/fallback. The publisher's non-revenue OpenSource declaration is configured through September 2027.

Example host:

```xml
<viewer:MorphDocumentViewer x:Name="DocumentViewer" />
```

```csharp
await DocumentViewer.OpenAsync(path);
var matches = await DocumentViewer.SearchEvidenceAsync("Order ID:");
DocumentViewer.SetHighlights(matches.SelectMany(m => m.Regions.Select(r =>
    new DocumentHighlight("order-id", m.PageIndex, r, "Order ID"))));
DocumentViewer.RegionSelected += (_, selection) => AttachEvidence(selection);
await DocumentViewer.ShowHighlightAsync("order-id");
// Dispose on the UI thread when the owning view closes.
await DocumentViewer.DisposeAsync();
```

Viewer methods run on the Avalonia UI thread; conversion/rendering run in the engine's serialized background session. Coordinates use normalized top-left page space and zero-based page indexes. Multiple rectangles can share an evidence ID. PDF text is queried in unrotated space, then transformed into display coordinates; this avoids PDFium text-order changes on rotated pages. Search unions can span several rectangles, so do not assume the first rectangle covers a whole word.

## Exercise and tune the UI

Uses **Mostlylucid.Avalonia.UITesting**, as in lucidRESUME, with retrying expectations, screenshots and actual pointer events. The demo prefers the neighboring local framework project and otherwise uses NuGet 1.7.1. Override `UITestingProject` to point to another checkout.

Avalonia 12 moved its input root off Window. The local framework's pointer simulator has a small compatibility fix; [the patch](compat/avalonia12-input-root.patch) is included for other checkouts. Package 1.7.1 does not include that fix, so use the patched local framework for drag/touch/wheel scenarios.

```sh
dotnet run --project verification/Morph.Avalonia.Verification -- artifacts/verification

dotnet run --project demo/Morph.Avalonia.Demo -- \
  --ux-test --script ux-scripts/concept-demo.yaml --output artifacts/ux

dotnet run --project demo/Morph.Avalonia.Demo -- \
  --document '/path/to/invoices/invoice_10248.pdf' \
  --ux-test --script ux-scripts/template-replay.yaml --output artifacts/ux-replay

dotnet run --project demo/Morph.Avalonia.Demo -- \
  --document '/path/to/invoices/invoice_10248.pdf' \
  --ux-test --script ux-scripts/llm-starter.yaml --output artifacts/ux-llm
```

The replay script expects invoices 10248/10249 from the supplied dataset. The LLM script uses a real installed local model; its timing and proposals can vary. `--ux-repl` and `--ux-mcp` expose the framework's interactive modes. `--capture artifacts/demo.png --capture-only` saves a demo preview. Generated artifacts are ignored.

Inspect a local corpus without copying originals:

```sh
dotnet run --project verification/Morph.Avalonia.Verification -- \
  --dataset '/path/to/archive (3)' artifacts/dataset
```

This renders/extracts three samples per PDF directory and writes a JSON inventory, source hashes and derived samples. It does not measure field extraction accuracy.

## Scope

This is an inspectable, session-only concept demo. It has draft label replay and reviewed examples; it does not yet update numerical embedding centroids, infer shared rules, call Tev/Nimble, invoke StyloExtract, scrape URLs, OCR scanned pages, or store production import workspaces. Embedding recipes are explicitly `pending-model`, with no fabricated vectors.

Word/HTML layout and font substitution can differ from their original applications. HTML is parsed rather than executed in a browser; DOM-to-rendered-evidence alignment is a later adapter. Image-only PDFs render but need OCR for text extraction. The current renderer caps bitmap size and uses a 120-DPI image; high zoom scales that image. Native execution and UI exercises have been performed on this Mac; Windows/Linux packaging is unqualified.

See the parent project's [initial centroid specification](../../docs/initial-centroid.md), [research](../../docs/research.md) and [plan](../../docs/plan.md).
