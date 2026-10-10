# Semantic field deduplication and coalescing

Status: required core extraction capability; contract and acceptance cases below. The current viewer/demo captures field evidence and reviewed neighbourhoods but does not implement this resolver.

## Purpose

Recover canonical structured fields from repeated, differently labelled or fragmented evidence. Coalescing belongs between candidate parsing and validation of resolved field cardinality. Validate individual candidate types and constraints before considering merges as well.

Keep four operations explicit:

1. **Field alignment:** map local labels/field definitions to a canonical semantic role, retaining document-specific extraction variants.
2. **Component assembly:** combine compatible spans or cells into one typed value, preserving component-level evidence.
3. **Observation equivalence:** identify repeated or equivalent observations within a compatible entity, role and effective-period scope.
4. **Canonical resolution:** select or assemble the supported value, retain conflicting alternatives and record the decision.

## Address contract

An address value may contain unit, building, street, locality, postcode and country components. Components can be absent or ambiguous. Retain raw source text, parsed values, normalisation version and provenance. Locale-sensitive normalisation must preserve distinctions such as unit and building identifiers.

The same address can fulfil property, applicant-current and correspondence roles. Those are separate field relationships, even if they refer to one shared address value. Matching an address also does not establish that two applicant or property entities are the same. An observation's entity association and role must be supported separately.

Complementary components can be assembled only when their entity, role, temporal scope and source context support belonging together. An absent unit is unknown, not proof of equality with every unit in that building. Conflicting unit/building/postcode components require review or distinct instances. Preserve historical addresses and uncertain dates.

## Core inputs and results

Inputs: schema/version, canonical role candidates, entity candidates, raw and parsed values/components, source document and representation identities, spans/regions, effective dates where available, extraction producer and lineage.

Results: stable field-instance and resolution IDs, supporting observation IDs, resolved component values with evidence IDs, equivalence groups, assembly relationships, unresolved alternatives, conflicts, and a versioned decision record. Outcomes include equivalent, complementary, distinct, conflicting and unresolved; multiple relationships can apply to different components of the same pair.

Every resolution records its rule/model/normaliser versions, evidence, rationale, review state and prior resolution. Merge and unmerge create replayable decisions without deleting observations or their original field mappings. Model proposals cannot silently rewrite reviewed identities or roles.

## Inference and learning

Use scoped candidate retrieval to bound comparisons. Deterministic parsed-component checks provide a baseline. Text/visual embeddings can retrieve plausible matches; an optional decision model can compare bounded candidate pairs. Similarity is evidence, not identity. Validate proposed groups for contradictions rather than blindly applying transitive closure to pairwise similarity edges.

Learn from reviewed equivalent, complementary and keep-separate examples. Scope field aliases to their applicable context. Qualify new assembly or merge rules against independent examples, including competing roles and near-matching units. Reimports, repeated model outputs for one region and known copied sources do not become independent support. Unknown lineage remains unknown.

This stage remains available with inference providers disabled through deterministic checks and explicit manual resolutions.

## Acceptance cases

| Case | Expected result |
| --- | --- |
| Property address / Subject property, compatible context | Same canonical field role; retain extraction variants. |
| Address in separate street, locality and postcode boxes | Assemble one value with evidence for each component. |
| Repeated address in header and body for one property | One resolved field with both source mentions retained. |
| Compatible application and valuation address observations | One resolved field after supported entity/role alignment. |
| Identical text serving property and correspondence roles | Distinct field relationships; optional shared address value. |
| Flat 2 versus Flat 3 in the same building | Distinct values or a visible conflict; no automatic merge. |
| Missing unit, overlapping address components | Unresolved identity unless other evidence supports it. |
| Postcode available only in a competing address section | Do not use it to complete the target field. |
| Historical versus current address | Preserve temporal observations and apply explicit resolution policy. |
| Similarity chain with conflicting endpoints | Reject automatic group coalescence; expose contradiction. |
| Same source imported or extracted repeatedly | Idempotent resolution and no additional independent support. |
| Incorrect merge reversed by review | Restore distinct instances with source and decision history intact. |

Evaluate false merges, missed equivalences, incorrect component assemblies, role preservation, evidence completeness and resolved cardinality. Report each separately from ordinary field detection accuracy.
