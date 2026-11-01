# Task 50 — protocol-2 reviewed fact extension: passive codec slice

Requirements: `FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13` (codec layer of [the rev-3 specification](TASK_50_PROTOCOL2_REVIEWED_FACT_EXTENSION.md)). This slice grants no endpoint, route admission, deployment, UAC or update authority.

## Implemented

- `ReviewedUpdateWireCodec` response is the exact 20-property shape of §2, with the five new fields at positions 14–18 and `diagnostic` last. Protocol `"2"` is redefined in place (O1). The 15-field reader and writer are gone, and an explicit 15-field frame is a negative test. The request is unchanged.
- New internal types in Execution: `ReviewedHelperQualification`, `ReviewedHelperRefusalReason` and `ReviewedHelperFacts`, which holds the nullable observed installed version, candidate version and fingerprint.
- `StructuralViolations` implements `UpdateWireFacts.IsConsistent` plus S1–S13 (§3.2). If any predicate fails, `CreateResponse` throws and `TryReadResponse` returns false. Over the three defined invocation values, S3 and S4 are logically equivalent, so a violation reports both.
- Strictness is retained: whole-frame re-encode comparison, exact property-name set and order, exact enum spelling (`ToString` round-trip), the 16 KiB bound and the diagnostic rules.

## O9 choice: (a) visibility reuse

`PCSteward.Execution` already references `PCSteward.Provider.Chocolatey`. The Architecture tests place no constraint on that reference or on `InternalsVisibleTo`, and the two assemblies have no colliding top-level type names. Chocolatey therefore grants `InternalsVisibleTo` to Execution. `IsNormalizedVersion` accepts exactly the strings of at most 128 characters for which `ChocolateyNativeVersion.TryParse` succeeds and `Normalized` equals the input. The §8 differential test still runs, so any future divergence fails. Its deterministic corpus combines 20,000 generated draws (structured and random), length boundaries from 1 to 140, fixed edge cases, and the `Normalized` form of every parseable entry. The test requires more than 1,000 accepted and more than 1,000 rejected strings.

## Evidence

The largest structurally valid frame is exactly 5,083 B, as §5 states. It needs `aggregateSettlementConfirmed = false`; the settled variant is 5,082 B. It round-trips byte-exactly.

## Deferred to later slices

- The P1–P6 adapter and its predicate tests.
- Correlation-after-budget and H rules.
- Phases (M4).
- Late-stage helper behaviour (M1, N1–N4).
- Status and outcome mapping.
- Candidate-check value and history persistence, including the history-reader half of the O9 differential.
- The verifier (L5).
- The capability seam (L-4).
- The N5 supersession note.

## Independent review — PASS (`fc7aebb`)

A fresh-context reviewer (Fable 5.1), pinned to `fc7aebb`, returned **PASS** with no Blocker, High or Medium findings. Its independent oracle, written from the spec text, enumerated 983,040 discrete combinations against `StructuralViolations` with zero disagreements. All 478 valid shapes round-trip byte-exactly. A 452,400-frame mutation fuzz produced no uncaught exceptions, and culture probes showed no drift. The largest valid frame is 5,083 B (unsettled) and the settled variant is 5,082 B, as the author reported. The spec's "from stage D" phrase for that fixture is a Low doc nit, recorded here and not otherwise changed.

Low (architecture), now fixed: the blanket `InternalsVisibleTo` from Provider.Chocolatey to Execution is replaced by a narrow public `ChocolateyVersionGrammar.IsNormalized(value, maximumLength)` facade over the pinned parser. Execution no longer sees Chocolatey internals. Codec tests 149/149 and Architecture 32/32 still pass.
