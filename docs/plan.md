# Document ledger implementation plan

## Active scope: concept demo

Deliver the reusable Morph viewer plus a small two-panel demo. Keep the document left, imported values right, and YAML in a secondary tab. The demo should explain the learning loop in a few minutes; production persistence, entity reconciliation, packaging and connectors are optional later work.

1. Render synthetic Word/PDF and local HTML with linked evidence.
2. Exercise field selection, corrections, region capture and YAML editing through the lucidRESUME Avalonia framework; inspect screenshots and tune the interaction.
3. Add two form variants and a competing address area; demonstrate a draft neighborhood selector replaying across changed values.
4. Add StyloExtract behind the HTML adapter and an explicit single-page fetch for a lightweight scraping example.
5. Compare deterministic selectors, text embeddings, image/text neighborhood embeddings, and embeddings plus Tev/Nimble decisions. Add MediaPipe Decision Maker as a separate candidate if its runtime is practical.

Steps 1–2 are the current implementation, together with empty-template creation, a local generative starter, initial neighborhood seeds and draft label replay. Steps 3–5 are the next concept experiments. The larger milestone table below describes possible product development after the demo, not a requirement for this first pass.

## Later milestones

| Milestone | Work | Acceptance |
| --- | --- | --- |
| 1 Morph Avalonia component | Independent .NET library and demo. Convert DOCX through Morph.Pdf; render PDF through Morph.PDFium. Navigation, zoom, search and host highlights. | Synthetic multipage Word and PDF open; search highlights agree with the displayed page; page/zoom changes preserve normalized coordinates; load failure and cancellation do not replace a newer document; native macOS execution demonstrated. |
| 2 Workspace and ledger | SQLite, artifact hashes, generic schema, entities, observations, evidence and review history. Build the document/ledger split view. | Three synthetic documents contribute to one entity; clicking each field opens its source; contradictory dates and values retain both observations; reopening preserves decisions. |
| 3 Deterministic templates | Family signatures, label/region/table selectors, transforms, guards, versioned packs and replay. | A reviewed example produces a draft; a second document applies it without inference; negative documents fail applicability; output validation can reject a family match. |
| 4 Local decisions | Bounded candidate generation, Ollama SystemOne adapter, question packs, provenance, failure handling and comparison harness. | Compare Tev1 0.8B, 4B and Nimble on identical labeled cases; report precision, abstention, latency and memory. No invented values; unavailable provider leaves review usable. |
| 5 Learning and qualification | Scoped feedback, negative examples, independent holdouts, template diffs, promotion and drift queue. | Repeated imports do not inflate evidence; bad revisions cannot silently replace a promoted template; replay identifies every producing version. |
| 6 OCR and tables | Local OCR adapter, mixed digital/scanned pages, table continuations and repeated row extraction. | Evidence rectangles map correctly through rotation, crop and OCR transforms; low OCR quality stays reviewable; split-page tables preserve row identity. |
| 7 Packaging and export | Workspace/template archives, JSON/CSV, signed desktop packages and platform-specific native assets. | Offline operation after dependency/model setup; clean install on each claimed OS; export roundtrip retains typed values and provenance. |

## First subproject architecture

`subprojects/morph-avalonia` contains a document engine, an Avalonia control, a desktop demo and focused verification. Pin package versions and use the publisher's OpenSource declaration for the intended non-revenue OSS scope. Keep native assets explicit in packaging.

The engine owns document bytes, derived PDFs, page sizes, rendering, search and geometric text extraction. Its asynchronous operations serialize document lifetime and release native resources. The control owns viewport state, background operation cancellation, bitmap lifetime, search presentation and normalized overlays. The host owns file picking, schema fields, evidence IDs and persistence.

Use current public APIs. A later native text geometry API from Morph could remove the DOCX-to-PDF bridge; place that change behind the document engine rather than spreading upstream internals across the UI.

## Qualification corpus

Use synthetic examples first: a multipage form with repeated labels, changed values, dates/currencies, a table, a missing field and a conflicting companion document. Add layout variants, merged cells, headers/footers, rotation, crop offsets, scan noise and documents that look similar but represent a different family. Keep induction examples separate from qualification examples.

Record correctness, field coverage, false automatic acceptance, abstention, evidence alignment, time to first page, page render latency, model latency and peak memory. Performance budgets become release targets after the first measured baseline. Do not transfer unrelated model benchmark accuracy to document import.

## Decisions to revisit

The working project name and OSS license remain provisional. The specification assumes one user per local workspace, generic schemas, PDF/DOCX first and an optional local model server. Confirm collaboration and export destinations when they become implementation dependencies. OCR quality and Word-to-structural-source alignment need separate qualification from rendering.
