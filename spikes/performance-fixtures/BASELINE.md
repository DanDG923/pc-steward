# Task 04 performance-baseline evidence

## Result

This spike establishes a repeatable fixture and measurement harness for `OD-06`; it does **not** demonstrate final PC Steward compliance with performance requirements. The final app must be measured by Task 29 on the agreed Windows 11 baseline.

The redacted capture in `baseline-system.json` observed Windows 11 Home build 26200, 16 logical processors, 31.15 GiB memory, PowerShell 7.6.4 and .NET SDKs 10.0.300–10.0.302. Storage remains unverified and the active plan is not the agreed fixed plan, so this host is not an acceptance baseline.

## Recorded spike results

`results/benchmark-20260728T095015Z.json` preserves every sample and raw worker result. All 30 local and 20 simulated healthy-online samples completed against 320 deterministic records.

| Measurement | Median | p95 | Notes |
|---|---:|---:|---|
| Local fixture pass | 555.99 ms | 584.68 ms | Child worker parses and visits all 320 records. |
| Local observed working set | — | 97.00 MiB | Direct worker process only. |
| Local CPU / total logical capacity | — | 5.830% | Process CPU time normalized over 16 logical processors. |
| Simulated healthy-online pass | 1,618.29 ms | 1,764.93 ms | Five local connector workers with a maximum of three active. |
| Online observed working set | — | 290.32 MiB | Sum of concurrently observed direct workers. |
| Online CPU / total logical capacity | — | 10.815% | Direct worker process tree only. |
| Cancellation acknowledgement | 503.54 ms | — | File-based cancellation request to a cancellable worker; status `cancelled`. |
| Observed connector concurrency | 3 | 3 | Bound requested and observed. |

## Measurement limitations

- The harness is a disposable PowerShell spike, has no UI, persistence, registry/AppX provider access, WinGet process, or real network source.
- “Healthy online” means deterministic local worker simulation. It is not a WinGet source-health result.
- The capture cannot prove SSD-equivalent storage, fixed power plan, clean-VM repeatability, or product-owner hardware relevance.
- Process-tree metrics include only the harness’s direct workers and exclude unrelated host work. The harness reports CPU time, sampled working set, cancellation acknowledgement and observed bounded concurrency; Task 29 must measure the final application’s complete tree and UI operability.
- No budget is claimed as met from this spike.

## Requirements supported

Evidence-design support only: `NFR-PERF-01`, `NFR-PERF-02`, `NFR-PERF-06`, `NFR-PERF-08`, `NFR-PERF-09`, `AC-NFR-01`, `AC-NFR-02`, `AC-NFR-03`, `AC-NFR-07`, `AC-NFR-09`.
