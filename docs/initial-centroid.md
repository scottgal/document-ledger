# Initial centroid and template bootstrap

The first imported document must produce a useful starting object even when there is no matching template. The user can create it manually, ask a local generative model to propose fields, or combine both approaches. Every user override becomes feedback attached to the draft.

## Cold-start interaction

1. Open a new PDF, Word document or HTML snapshot. Show it on the left; the right-hand imported record starts empty for an unfamiliar document.
2. Draw a region, name a field, and attach its source; alternatively ask a local LLM to propose labels, anchors, sections and verbatim values.
3. Source-check proposals against the actual document representation. Attach page coordinates from extraction, never from the LLM. Keep them proposed until reviewed.
4. Let the user edit semantic role, value, label/anchor, section and source region. Record old/new observations and selector changes with source identity.
5. Initialize each field neighborhood immediately. A single example is a draft seed, with unknown dispersion and no empirical generalization claim.
6. Reuse the draft on the next document. Apply anchors and relations; present failed guards and ambiguous matches for correction. Compare the new neighborhood with earlier exemplars.
7. Add reviewed independent examples to coherent prototypes, retain exceptions, and propose common rules shared between local templates.

## Seed representation

| Feature view | Initial contents | Role |
| --- | --- | --- |
| Semantic | Field role, label, aliases, section, expected type/cardinality | Names and meaning before values are known |
| Neighborhood text | Nearby labels, headings, surrounding lines, competing field roles | Distinguish several addresses or dates on one page |
| Geometry | Normalized anchor/value boxes, page size/aspect, relative displacement, alignment, size, reading-order neighbors | Learn where values occur relative to stable structure |
| Style/structure | Fonts where available, emphasis, borders, table row/column, DOM ancestry, repeated blocks | DocExtractor features combined with StyloExtract locality |
| Embeddings | Label embedding, contextual neighborhood embedding, visual crop or joint text/image embedding; exact encoder identity | Retrieve related prototypes despite wording/layout variation |
| Evidence | Source hash, representation/version, page/crop/span, text quote, source path | Inspect and replay every observation |
| Learning state | Initial/draft, example identities, review state, positive/negative feedback, unresolved features | Refine with corrections without inventing confidence |

Missing features are explicit. A text-only document adapter may not provide fonts; an image-only page may lack OCR. Pending embeddings have an input recipe and model identity but no vector. Zero vectors do not represent “not computed.” A first prototype has one example and unknown variance; its model similarity is separate from reviewed precision.

For address initialization, use generic role descriptions and schema constraints as priors, then seed observed anchors and relations from the first form. Preserve “property address” and “correspondence address” as different roles. Initialize both text and visual views when the runtime is available; avoid collapsing those views into arbitrary concatenated numeric features. Qualify their weighting with the actual dataset.

## Model responsibilities

A small generative model can propose a vocabulary and initial selectors when the schema is unknown. The demo uses an installed local Ollama model for this step. Tev/Nimble then have a bounded set of fields, templates or candidates to choose among; their typed decisions fit that later stage. EmbeddingGemma 2 retrieves similar labels and neighborhoods. A multimodal decision engine is an optional comparison for ambiguities that require visual reasoning.

The model must not replace reviewed user choices. Subsequent proposals receive reviewed labels and anchors as context, and the UI appends suggestions rather than silently overwriting existing fields. Rejections and corrections should be retained as independent feedback, with the producing model and template revision. Repeatedly loading the same file must not inflate prototype support.

## What the current demo implements

- Empty template on a new document; manual field creation from a selected region or typed values.
- Local Ollama field suggestions, checked for matching value/anchor text on a common page.
- Editable field values, anchors, sections and region evidence; confirmations and YAML overrides create feedback.
- Initial label, nearby text, normalized anchor/value positions and an explicit pending embedding recipe in YAML.
- Draft replay using same-line label selectors on the next file in the current folder.

The source check proves that text is present, not that a semantic mapping is correct. All LLM suggestions remain reviewable. Full spatial neighbor graphs, font/table/DOM feature extraction, embedding inference, statistical centroid updates and shared-template induction remain research experiments. The current nearby-text seed follows extraction reading order; it is not yet a nearest-neighbor spatial index.
