# Document import research

Research date: 10 October 2026. The recommended foundation is Morph for Word conversion, Morph.PDFium for PDF rendering and text geometry, and a native Avalonia review surface. The useful learning mechanisms already appear across Morty, Importer, StyloExtract, and StyloMail.

## Current Papyrine capabilities

Morph has moved from SimonCropp to [Papyrine](https://github.com/Papyrine). The ecosystem includes Morph, Morph.PDFium, Parchment, Excelsior, OpenXmlKit, OpenXmlStreaming, OpenXmlHtml, Naiad, PandocNet, and krillaNet. Pick components for a concrete task rather than bringing the entire family into the client.

| Component | Role in this project |
| --- | --- |
| [Morph](https://github.com/Papyrine/Morph) and Morph.Pdf | Convert Word into a paginated PDF for preview. Semantic HTML and Markdown are additional outputs. Current inputs also include spreadsheets and presentations. |
| [Morph.PDFium](https://github.com/Papyrine/Morph.PDFium) | Render existing or generated PDFs; extract text; search; obtain text rectangles. Includes native assets for Windows, macOS, and Linux. |
| [Morph.Blazor](https://github.com/Papyrine/Morph/blob/main/src/Morph.Blazor/README.md) | A reference for page navigation, rendering on demand, and selectable text. It is a browser component, so native Avalonia needs its own control. |
| [OpenXmlKit](https://github.com/Papyrine/OpenXmlKit) | Optional ergonomic Word structure reader/writer over the Open XML SDK. Retain direct SDK access for source paths. |
| [Parchment](https://github.com/Papyrine/Parchment) | Optional later generation of Word reports from reviewed ledger data. |
| [Excelsior](https://github.com/Papyrine/Excelsior) | Optional later spreadsheet import/export support. |
| [krillaNet](https://github.com/Papyrine/krillaNet) | PDF creation and HTML conversion; evaluate only if the project needs a second PDF writer. |

The NuGet flat-container index lists Morph **1.21.0**, while GitHub's latest release API reports **1.20.0**, published 5 October. Pin the actual NuGet packages during implementation. Changes since the September lucidRESUME evaluation include [1.14 OpenSource exemption](https://github.com/Papyrine/Morph/releases/tag/1.14.0), [1.15 selectable text and viewer](https://github.com/Papyrine/Morph/releases/tag/1.15.0), embedded font fallback, sheet pagination, and [1.20 GetPageTexts](https://github.com/Papyrine/Morph/releases/tag/1.20.0).

Morph's `GetPageTexts` follows its pagination, but excludes headers, footers, and notes. The detailed retained layout tree is internal; `Morph.Blazor` accesses it as a friend assembly. Its `PageTextLayer` publicly exposes text, with coordinates and source mappings internal. Do not promise those as a supported native API. See [DocumentConverter](https://github.com/Papyrine/Morph/blob/4f65fda0f1c8380f1f45ea63f610c65dfd055199/src/Morph/OpenXml/DocumentConverter.cs), [PageTextLayer](https://github.com/Papyrine/Morph/blob/4f65fda0f1c8380f1f45ea63f610c65dfd055199/src/Morph.Blazor/Services/PageTextLayer.cs), and [assembly declarations](https://github.com/Papyrine/Morph/blob/4f65fda0f1c8380f1f45ea63f610c65dfd055199/src/Morph/Morph.csproj).

The proposed native route converts DOCX once to vector PDF, then renders and locates text against that exact PDF. Preserve the original DOCX and structural paths separately. Matching a rendered passage to a DOCX path may be ambiguous; mark that explicitly. Shared pagination improves preview consistency but does not establish full Word fidelity.

## Open source configuration

The current [publisher configuration](https://github.com/Papyrine/Morph/blob/4f65fda0f1c8380f1f45ea63f610c65dfd055199/src/Directory.Packages.props) defines `OpenSource` for projects that do not generate revenue, with donations and sponsorship excluded from revenue. It is bounded to twelve months. Use the documented owner properties `Papyrine_SponsorshipExemption` and `Papyrine_SponsorshipExemptionUntil`; revisit the declaration at expiry. This follows the intended OSS project scope. It supersedes the September evaluation's absence of an applicable declaration. [SponsorCheck consumer documentation](https://github.com/SimonCropp/SponsorCheck/blob/main/docs/ConsumerUsage.md).

## Existing project foundations

Earlier document-import work supplied general design patterns: linked evidence selection, label-relative and table-cell extraction, layout exemplars, source manifests, versioned mappings and reviewed aliases. This independent prototype implements those ideas with synthetic examples.

Public companion projects provide the reusable foundations:

| Project | Pattern used here |
| --- | --- |
| [StyloExtract](https://github.com/scottgal/styloextract) | Structural fingerprints, reusable role selectors, apply checks, refit and versioned extraction state. |
| lucidRESUME | Morph preview integration and scripted Avalonia UI exercises. |
| StyloMail | Bounded local decisions, provider failures and recorded runtime identity. |

These observations establish architectural precedents; this research did not benchmark extraction accuracy.

## Local decisions

[Tev1](https://ollama.com/library/tev1:0.8b) offers 0.8B and 4B variants through Ollama 0.35 or later at `/v1/systemone`. [Nimble](https://ollama.com/library/nimble) is 9B, making it an optional larger local provider. Both support typed decision questions. Evaluate Tev1 0.8B first for bounded field/template candidate ranking, then compare 4B and Nimble on the same document corpus.

Decision models select among supplied choices. They need a candidate extraction layer. Include a none/uncertain option; use neutral candidate IDs and evidence descriptions. Returned confidence expresses probability concentration, which requires workload calibration before any automatic acceptance policy. Persist model digest, server version, question version, bounded evidence, and answer. Replaying a recorded decision is reproducible; rerunning inference is a new observation.

For image-only pages, add local [Tesseract](https://github.com/tesseract-ocr/tesseract) OCR with its coordinates. [Docling](https://github.com/docling-project/docling) can be an optional worker for difficult document structure. Neither is required to open and review digital documents in the first subproject.

## Source revisions

| Repository | Inspected HEAD |
| --- | --- |
| stylobot-extract | `d91357b3896f94bef78b276d779eb5e144a21804` |
| lucidRESUME | `87c886f150218c993429653cbecdaf2215f60b7c` |
| stylomail | `ed9284efacf71393dad18cafbc949c9d3d38f298` |
| Papyrine/Morph | `4f65fda0f1c8380f1f45ea63f610c65dfd055199` |
| Papyrine/Morph.PDFium | `5fa5caa77bf26138ffed19ebc70e7e93f4746eea` |

Local inspection used the working files at these revisions; uncommitted local changes, if any, are not represented by HEAD. Current upstream files and release metadata were retrieved directly because indexed GitHub views contained older snapshots.

## EmbeddingGemma 2 and combined decisions

The new release is **EmbeddingGemma 2**, announced 6 October 2026. It supports text, images, audio and video in one embedding space. The architecture is modular: 270M text parameters, 170M vision and 300M audio; text plus vision is 440M. Output is 768 dimensions, with 512/256/128 truncation and renormalization. The context is 8,192 tokens. Its license is Apache 2.0. Start the document experiment with text and vision at 768 dimensions; qualify 256 dimensions separately. The model card warns that 128 dimensions reduces multimodal quality. [Google model card](https://ai.google.dev/gemma/docs/embeddinggemma/model_card_2), [launch announcement](https://blog.google/innovation-and-ai/technology/developers-tools/embeddinggemma-2/).

The Google checkpoint supports SentenceTransformers multimodal dictionaries. For this experiment, encode the cropped field neighborhood together with its heading and labels, and compare it with reviewed exemplars. Record the checkpoint, encoder configuration, task prefix, crop recipe, output dimension and normalization. Inference guidance specifies bfloat16 or float32; float16 can produce invalid or degraded results. Use the documented asymmetric query/document prefixes for retrieval, and a consistent symmetric task for clustering. [Google checkpoint and usage](https://huggingface.co/google/embeddinggemma-2), [multimodal tutorial](https://ai.google.dev/gemma/docs/embeddinggemma/multimodal-embeddinggemma-with-sentence-transformers).

[Ollama's model listing](https://ollama.com/library/embeddinggemma-2) now includes text and vision variants, but the shown `/api/embed` example is text-only. Its tag metadata says 256K context while its readme says 8K. Use the upstream 8K limit and verify the installed runtime's actual multimodal embedding contract before choosing that adapter. A local SentenceTransformers worker is the clearest documented starting point; this research has not downloaded or benchmarked the model.

Google's new MediaPipe Decision Maker can also use EmbeddingGemma backends for categorical, boolean and ordinal decisions. Options are precomputed; the embedding backend applies within-question centering/whitening and normalization. It supports multimodal context and options, and can share a UniversalEmbedder provider. Published platform guides cover Android, iOS, Python and Web; no official .NET adapter was established here. This is a useful alternative experiment alongside Tev/Nimble. Its decision-option centroid is separate from our persistent document-family prototypes. [Decision Maker guide](https://developers.google.com/edge/mediapipe/solutions/decision/decision_maker).

Proposed combination: deterministic rules generate candidates; embeddings retrieve likely local templates and field neighborhoods; Tev/Nimble choose between bounded candidates using labels, OCR/text, layout features and exemplar descriptions. A text decision model cannot consume an embedding vector as visual understanding. When visual evidence cannot be expressed adequately in text, use a multimodal decision backend or retain human review. Similarity and model certainty must be calibrated against labeled imports.

## HTML extraction and lightweight scraping

StyloExtract provides the natural HTML adapter: retain a fetched HTML snapshot, its URL/base URI, acquisition time and hash; identify repeated DOM content areas; extract through local templates; feed the same imported-record panel. HTML selectors retain DOM paths, text anchors and section context. Rendering through Morph provides a review surface but does not reproduce browser layout or execute JavaScript.

The first extension should accept local HTML snapshots and explicit single-page URL fetches. Bound response size and duration, resolve approved resources against the original base URI, and record redirects. Browser-backed acquisition for JavaScript-heavy pages is a later adapter. Repeated crawling, scheduling and destination integrations are outside the concept demo. DOM extraction and rendered-page evidence need an explicit alignment step; a CSS selector is not itself a PDF rectangle.

## UI testing foundation

Use `Mostlylucid.Avalonia.UITesting` from lucidRESUME: YAML actions, retrying expectations, named controls, screenshots and raw pointer events. The demo references the local framework when present and otherwise its 1.7.1 package. Exercising the demo exposed Avalonia 12's move of the input root from Window to its presentation source; the local pointer simulator now resolves that root before dispatch. Keep the reusable Morph control independent of the testing framework.

## Supplied company-document dataset

The local folder `~/Downloads/archive (3)` contains 2,676 PDFs and a CSV with 2,676 text/class rows. PDF groups are 830 invoices, 830 purchase orders, 809 shipping orders, 23 monthly inventory reports and 184 category-specific monthly reports. CSV labels are `invoice`, `purchase Order`, `ShippingOrder` and `report`; it has `text`, `label`, `word_count` columns and no source-file key or field/rectangle annotations.

Initial inspection rendered and extracted text from three PDFs per group (15 total). These samples have digital text, recurring section labels, customer/shipping addresses and product tables. Shared order IDs suggest related documents across families; verify any cross-document linkage from source content. CSV classification labels do not constitute extraction ground truth. Keep PDF/CSV splits grouped by shared order and layout to reduce leakage; category/month report partitions also need grouped evaluation.

Use the corpus locally through `--document` and Next document to demonstrate cold-start creation and draft replay on changing values. Do not include the downloaded PDFs in the repository. The inspection report, derived previews and text samples are generated under ignored `artifacts/dataset`; full-corpus extraction accuracy has not been evaluated.
