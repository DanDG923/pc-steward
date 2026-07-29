# Task 29A — Prototype-parity UI completion evidence

**Status:** Complete — existing feature surfaces aligned; no new product authority added.

## Scope and reference

`design/native-prototype/` is the authoritative visual and interaction reference for this task. The work was intentionally limited to the existing desktop application's Overview, Scan, Applications, Updates, Activity, Privacy & data, and Settings surfaces. It did not add future navigation or modules, change the scan/update/deletion/scheduling/elevation workflows, refresh a source, or invoke a destructive action.

## Delivered parity

| Surface | Production result | Existing behavior retained |
|---|---|---|
| Overview | Evidence-first summary card, source/freshness facts, explicit no-automatic-change statement, and a four-card status grid. | Counts derive from real update intelligence; actions still only open Updates or begin an explicit scan. |
| Applications | Dedicated inventory page with search, status/publisher/source/scope filters, sort, matching count, and bounded list viewport. | Filtering remains view-only and item activation still opens the existing details/review flow. |
| Scan and Updates | Dedicated pages rather than the prior all-in-one Overview; copy states cached/local-only/source boundaries. | Scan remains explicit/read-only; Updates remains evidence-only and does not offer an install action. |
| Activity | Compact local-record view for scan state, decision/mapping counts, and source evidence. | No fabricated history or telemetry is introduced. |
| Privacy & data | Dedicated data page separates routine cleanup from the existing factory-reset danger zone. | Existing scope disclosures and confirmation-gated controls are unchanged. |
| Settings | Dedicated trusted-source card ahead of existing schedule/notification controls. | Connector disclosure/consent and scan-only schedule boundaries are unchanged. |

Overview and Updates now read the unfiltered local inventory, so an Apps filter cannot make cross-page counts or evidence disappear. The focused view-model regression test covers that boundary.

## Verification

- Release build: `pwsh -NoProfile -File .\scripts\Build.ps1` — passed, 0 warnings, 0 errors.
- Full suite: `pwsh -NoProfile -File .\scripts\Test.ps1` — passed, 191 tests.
- Release desktop visual/UI Automation review on 2026-07-29 confirmed named navigation and primary controls across Overview, Applications, Scan, Updates, Activity, Privacy & data, and Settings. The review used local cached inventory/source evidence only; it did not start a scan or source check, alter a connector/schedule, export/delete data, or start an update. The desktop closed normally afterward.

## Requirement links and limits

This task provides presentation evidence for `FR-UI-01–07`, `FR-ONB-03–04`, `FR-DAT-01/03–04/06–15`, `FR-SCH-01–05`, and best-effort `AC-UX-01–09`; their pre-existing behavioral evidence remains owned by earlier tasks. Keyboard/Narrator/high-contrast/text-scale matrices are still incomplete under ADR-021, so they are not claimed as passed here. The approved Task 29 idle-performance exception `PO-EXC-29-PERF-01` remains unchanged; this visual work does not remediate or pass `NFR-PERF-05` / `AC-NFR-06`.
