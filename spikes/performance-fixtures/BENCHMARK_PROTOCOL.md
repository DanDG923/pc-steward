# Benchmark protocol

## Preconditions

Run only against the deterministic fixture files. The protocol neither queries WinGet nor reads installed inventory. For acceptance evidence, use an agreed clean Windows 11 x64 VM with at least 4 vCPU, 8 GB memory, SSD-equivalent storage and a fixed power plan; record the same redacted baseline capture before every comparative run.

## Commands

```powershell
pwsh -NoProfile -File .\spikes\performance-fixtures\tests\PerformanceFixture.Tests.ps1
pwsh -NoProfile -File .\spikes\performance-fixtures\New-PerformanceFixtures.ps1 -OutputPath .\spikes\performance-fixtures\fixtures\provider-records.json
pwsh -NoProfile -File .\spikes\performance-fixtures\Capture-BaselineSystem.ps1
pwsh -NoProfile -File .\spikes\performance-fixtures\Invoke-PerformanceBenchmark.ps1 -Scenario local -LocalRuns 30
pwsh -NoProfile -File .\spikes\performance-fixtures\Invoke-PerformanceBenchmark.ps1 -Scenario online -OnlineRuns 20 -MaxConnectorConcurrency 3
```

`-Scenario all` performs the 30 local and 20 online sequences in one machine-readable aggregate. The current raw aggregate is `results/benchmark-20260728T095015Z.json`; individual worker JSON and empty stderr captures beside it are retained. Do not discard failed or cancelled samples. Capture their exit codes and diagnostics in the resulting JSON and explain any exclusion in the report.

## What the harness measures

- elapsed time for each local or complete simulated-online pass;
- CPU time, plus normalization against one core and all logical processors;
- sampled direct-worker working set (summed during concurrent online work);
- cancellation-request-to-worker-exit acknowledgement;
- observed peak connector concurrency, with the default bound of three.

The harness purposefully does not imply product UI responsiveness, provider correctness, real network health or final resource compliance. The final implementation must retain equivalent measurements and add actual UI acknowledgement/recovery, final process-tree coverage, source timeout isolation, idle, cold-start, scheduled-exit and cache-quota measurements.

## Review checks

1. Confirm the fixture count is at least 300 and all expected edge categories are present.
2. Confirm exactly 30 local and 20 online aggregate samples, all with `completed` status.
3. Confirm `observedMaxConcurrency` does not exceed the configured bound.
4. Confirm cancellation records `cancelled` and preserve its acknowledgement duration.
5. Confirm the capture redacts host/user/network identifiers and does not make an acceptance claim when the baseline preconditions are missing.
