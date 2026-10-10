# Demonstration evidence

Run on 10 October 2026 on the local Mac. These checks exercise the concept and rendering component; they do not establish production extraction accuracy.

| Exercise | Result | Evidence |
| --- | --- | --- |
| Public-checkout dependency path | Solution build passed with 0 warnings/errors | Built with the neighboring UI-testing project disabled, exercising the published package fallback. |
| Semantic coalescing core | 98 checks passed | Alias alignment, assembly, component provenance, scope/units/period guards, conflicting similarity chains, rejection/supersession, split history, support counts, idempotent reimports and JSON snapshot replay. |
| Coalescing UI script | 55 actions passed | `artifacts/ux-coalescing/result.json`: eight observations/four fields, layer toggle, reviewed merge/undo, rejection/undo, YAML round-trip, correction/reversion and prior-document source navigation. |
| Article quality loop | Markdown and browser checks passed | Actual site Markdig/TOC parser; four tables, six code fences, three existing screenshots and working TOC anchors. Mermaid rendered; images loaded and no horizontal page overflow at desktop and 390px mobile width. C# excerpts compared with source. |
| Engine and viewer verification | 48 checks passed | Word/PDF/HTML rendering, searchable geometry, cancellation, invalid input, disposal, replacement races, navigation/search/zoom. All four rotations with and without crop offsets; evidence regions intersect dark pixels in the rendered document. |
| Concept UI script | 36 actions passed | `subprojects/morph-avalonia/artifacts/ux/result.json` and named screenshots. |
| Cold-start local LLM script | 27 actions passed | `artifacts/ux-llm/result.json`: unfamiliar invoice, manual seed, real installed `qwen3.5:0.8b` proposal, source matching, user override and feedback. |
| Template replay UI script | 18 actions passed | `artifacts/ux-replay/result.json`: manually teach Order ID on invoice 10248; reuse draft on invoice 10249; confirm a second independent source. |
| Dataset inspection | 15 PDF samples rendered and text extracted | `artifacts/dataset/inspection.json`, previews and text samples; three per directory family. |

UI scripts use the lucidRESUME `Mostlylucid.Avalonia.UITesting` framework, with a local Avalonia 12 pointer-root compatibility fix. They exercise named controls, retrying expectations and real drag input. The reusable viewer has no dependency on that framework.

The small generative model's field coverage varies. Source checking rejects unmatched anchors/values; a passing interaction test does not mean a complete or correct extraction. A second invoice was also used for direct suggestion diagnostics. Numerical embeddings, Tev/Nimble decisions, OCR and StyloExtract integration have not been run. Initial centroid YAML stores label/neighborhood/position features and explicitly pending embedding inputs.

The address parser is a conservative UK-style baseline, with entity/period scope supplied explicitly. JSON snapshots retain source/cache paths; portability requires keeping the referenced PDF representations. The checks qualify the listed cases, not corpus-wide address accuracy.

The loop caught and fixed immutable-collection/date serialization in YAML, nearest-region selection for repeated text, cache creation on an external-PDF cold start, and empty groups when all members of a reviewed merge are rejected.

Artifacts are generated locally and ignored. Downloaded corpus originals remain in the supplied Downloads folder. The repository does not bundle them.
