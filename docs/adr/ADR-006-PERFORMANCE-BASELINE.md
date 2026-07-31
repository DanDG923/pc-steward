# ADR-006 — Performance baseline

**Status:** Accepted
**Date:** 2026-07-28
**Decision owners:** Product owner and implementation reviewer
**Related requirements:** `NFR-PERF-01–10`, `AC-NFR-01–11`
**Related open decision:** `OD-06`

## Context

Budgets need a repeatable Windows 11 x64 baseline and process-tree measurements.

## Options considered

### Option A — Product-owner PC only

Realistic but difficult to reproduce and may be overpowered.

### Option B — Fixed supported Windows baseline plus exploratory real PC

Repeatable gate with separate real-world evidence.

## Decision

Accept Option B. The repeatable acceptance baseline is a supported Windows 11 x64 environment with at least 4 logical processors, 8 GB RAM, SSD-equivalent storage and a fixed recorded power plan. It may be physical or virtual. Product-owner-PC results are reported separately unless that PC is the recorded baseline.

Option A is rejected because an unrecorded changing physical PC cannot provide a reproducible acceptance comparison.

## Consequences

Environment variance must be recorded; final claims are comparable.

### Follow-up controls and tests

Raw results include environment/build IDs, warm-up, every sample, median and p95.

## Evidence the upcoming spike must collect

- Task 04 baseline capture and 300-record harness.
- Process-tree CPU/working set/cancellation/concurrency measurement reliability.
- 30 warmed local and 20 healthy online protocol repeatability.
- Task 29 confirmation on the final application.

## Task 04 evidence update — 2026-07-28

`spikes/performance-fixtures/` now supplies a deterministic 320-record fixture matrix, redacted system capture, raw machine-readable 30-local/20-simulated-online benchmark result, direct-worker CPU/working-set instrumentation, cancellation probe and bounded-concurrency observation. The recorded aggregate is `results/benchmark-20260728T095015Z.json`: local p95 584.68 ms, simulated-online p95 1,764.93 ms, cancellation acknowledgement 503.54 ms and maximum observed connector concurrency 3.

This evidence validates the accepted measurement approach only. It is not a product-performance claim: the host has unverified SSD-equivalent storage and no agreed fixed power plan, the online path is a local simulation, and the future application/UI/process tree does not yet exist. Final-application evidence on the recorded baseline remains mandatory for acceptance criteria; a VM is optional.

## Task 90 baseline acceptance — 2026-07-30

The product owner accepted the physical 3XS PC as the ADR-006 final-application baseline with the fixed `3XS-Overclock` power plan. The retained capture records Windows 11 Home build `26200`, AMD Ryzen 7 9800X3D with 16 logical processors, 31.15 GiB RAM and NVMe storage. Results from this host are therefore baseline results rather than exploratory product-owner-PC results, provided each protocol records the same fixed plan and environment identity.

The clean packaged idle observations on this baseline measured 0.4521% total-system CPU / 181,592,064 B peak working set from cold start and 0.1491% / 186,441,728 B after warm-up. These establish the CPU and memory portions only. Early process-attributed I/O and unverified network attribution are retained under the separately approved Task 90 performance exception; the untouched mandatory threshold is not silently redefined.

## Reconsider when

Baseline variance prevents stable thresholds or the target-user hardware floor changes.
