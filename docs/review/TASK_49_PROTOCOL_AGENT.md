# Task 49 — protocol-1 compatibility agent evidence

## Outcome and boundary

PASS for the assigned synthetic compatibility scope. Only this report and `tests/PCSteward.Security.FixtureTests/Task49ProtocolCompatibilityTests.cs` were created. No production or existing test file was edited. Existing shared changes were preserved. No commit, restore, package/network call, real helper/provider/application launch, UAC, deployment, scan or user-data action occurred. Main retains integration, STATUS, TRACEABILITY, Release build and full-suite verification.

Requirements: `FR-UPD-14`; retained `FR-UPD-02`, `FR-UPD-03`, `FR-UPD-04`, `FR-UPD-07`, `FR-UPD-09`, `TM-04`. This is regression/compatibility evidence, not new helper progress support or stronger ADR-025 new-protocol operation safety evidence.

Read AGENTS, STATUS, Task 49 prompt, Phase C, shared task contract, relevant ADR-018/025, Task 49 design dialogue, domain job digest, protocol/result/runner source and existing security fixtures. The accepted dialogue interpretation keeps desktop-local progress separate from helper protocol 1. No root CONTEXT/CONTEXT-MAP was present in the file inventory.

`git diff --quiet 607271d -- src/PCSteward.Execution/ElevatedHelperProtocol.cs` returned 0. Both working-file `git hash-object` and checkpoint `git rev-parse 607271d:src/PCSteward.Execution/ElevatedHelperProtocol.cs` returned `6f28fe9aae9476d52992c90f3a71c0a9ffcfa577`. The checkpoint protocol is byte-for-byte unchanged under Git normalization; helper lifecycle/result ownership changes elsewhere are not represented as unchanged binaries.

## Evidence added

- Fixed job/correlation IDs, nonce, approval/issue timestamps and digest; explicit literal UTF-8 request (18 fields) and completed/failed responses (6 fields). Expected serialization is not produced by the codec under test. Timestamp `+00:00` is deliberately pinned as JSON `\u002B00:00`, with stable field ordering. The fixed digest was independently calculated from the documented canonical newline-separated domain fields using PowerShell SHA-256 before tests were written; it is also pinned as a literal.
- Golden request decode preserves reviewed selection, IDs, approval and digest. Golden result decode preserves outcome, nullable/numeric exit code and diagnostic. `Completed` through the runner remains `AwaitingVerification`, not verified success (`FR-UPD-09`).
- A synthetic pending `OwnedCompletion` task reaches the local execution result. Before and after that task settles, projecting the existing scalar result fields produces exactly the same golden response and the request remains identical. Decoding the wire result yields no ownership task. The transport is an in-memory fixture; no helper is launched.
- Both strict readers reject each of 12 added fields: progress, phase, percent, percentage, ownedCompletion, OwnedCompletion, task, executionLifetime, sequence, completion, finalStatus and degraded. These are malformed-schema fixtures, not progress messages the helper supports.
- Changed request job/correlation with unchanged digest, wrong nonce, cross-job/wrong-nonce results and one-shot replay reject. A request has no independently supplied expected-job parameter: its job/correlation integrity is protected here by the digest, while response identity is compared against the expected job. This does not claim a digest authenticates owner intent or replace endpoint authentication.
- Existing -15-second future-skew and 120-second age boundaries accept; -16 and 121 seconds reject. Both schemas reject duplicate/missing fields, malformed/empty messages and 16,385-byte messages; valid JSON padded to the exact 16,384-byte bound accepts.
- Version 2, Install/Uninstall, user scope and conflicting provider/source requests reject. Result version/progress outcome/string exit-code substitutions reject. Reflection pins version, size and the sole three-parameter `CreateRequest(ImmutableUpdateJob, string, DateTimeOffset)` method. Fixed machine WinGet argument literals are unchanged.

## Commands and results

Every `dotnet test` below used this same synchronous Windows PowerShell wrapper (no async mutex ownership):

```powershell
$env:DOTNET_ROOT = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$mutex = [Threading.Mutex]::new($false, 'Local\PCSteward.Task49.Build')
$acquired = $false
$log = Join-Path $env:TEMP ('pc-steward-task49-protocol-<run>-' + [Guid]::NewGuid().ToString('N') + '.log')
try {
    try { $acquired = $mutex.WaitOne(60000) }
    catch [Threading.AbandonedMutexException] { $acquired = $true }
    if (-not $acquired) { throw 'Task49 build mutex not acquired; command stopped.' }
    & dotnet test tests\PCSteward.Security.FixtureTests\PCSteward.Security.FixtureTests.csproj --configuration Release --no-restore --filter $filter --logger 'console;verbosity=normal' *> $log
    $result = $LASTEXITCODE
    Get-Content $log -Tail 65
    Write-Output "LOG=$log EXIT=$result"
    exit $result
} finally {
    if ($acquired) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
```

SDK `dotnet --version`: **10.0.403**. Runtime inventory includes Microsoft.NETCore.App **10.0.14** under the process-local DOTNET_ROOT (also retains 10.0.11); no SDK/runtime was installed. Test host reports .NETCoreApp v10.0. Inventory commands in the retained run were inside the same acquired mutex and appended to its log.

| Run/filter | Result | Unique log under `%TEMP%` |
|---|---|---|
| `FullyQualifiedName~Task49ProtocolCompatibilityTests` initial compile | Exit 1: MSTEST0032 at lines 41/42 (constant assertions), CA1861 at line 184 (array argument); no tests executed | `pc-steward-task49-protocol-first-24b3a3c80b2f4f04aa8e16aa87c01fa6.log` |
| Same filter after test-only analyzer corrections | Exit 0, **30/30**, zero skipped; no warning/error diagnostics | `pc-steward-task49-protocol-focused-f0dd48bbf6dd42e9a31389f0608167cd.log` |
| Retained filter below | Exit 0, **37/37**, zero skipped; no warning/error diagnostics | `pc-steward-task49-protocol-retained-532b52a97dac4157800736b426554d6d.log` |
| Prelaunch filter below | Exit 0, **2/2**, zero skipped; fixture launch counts remain zero | `pc-steward-task49-protocol-prelaunch-550ca81e2a914876a1254225a456e5f2.log` |

Exact retained filter:

```text
FullyQualifiedName~Task49ProtocolCompatibilityTests|FullyQualifiedName~ElevatedHelperSecurityTests.Protocol|FullyQualifiedName~ElevatedHelperSecurityTests.ResponseIsBoundToJobAndNonceAndSanitized|FullyQualifiedName~ElevatedHelperSecurityTests.StaleOrSubstitutedEvidenceNeverReachesTheHelper|FullyQualifiedName~ElevatedHelperSecurityTests.BootstrapAcceptsOnlyProtocolPidPipeAndNonce|FullyQualifiedName~ElevatedHelperSecurityTests.MachineProviderBuildsOnlyTheFixedMachineScopeOperation
```

Exact prelaunch filter:

```text
FullyQualifiedName~ElevatedHelperSecurityTests.PreLaunchRejectionAndReplacementNeverInvokeRunasLauncher|FullyQualifiedName~ElevatedHelperSecurityTests.ChocolateyCallerTrustRejectionHappensBeforeTheElevatedHelperCanLaunch
```

The initial analyzer failure is **not behavioral red**. No production behavior was implemented in this agent's scope, so these compatibility tests should pass unchanged existing behavior; no intentional production mutation or fabricated red run was performed. Final validation covers 30 new cases and 9 selected existing regressions (39 distinct cases), not the full security project or repository suite.

## Limits and handoff

No source bug found in this scope. Strict schema/authenticated-job-data regression tests do not re-prove real pipe peer identity, installed signatures/ACLs, UAC, privileged execution, external installer quiescence, Desktop rendering or complete Task 49 progress behavior. Existing prelaunch tests use synthetic trust/launcher delegates. Actual endpoint authentication and installed-host capability retain their existing evidence/gates. The pending local task test proves serialization isolation, not cancellation/teardown correctness. Full build/suite and shared change integration remain with main. No consultation blocker arose; any future blocker is routed through main under the clarified AGENTS council rule.

No commit was made, per assigned authority. Scope review and `git diff --check` passed for the two allowed new files.
