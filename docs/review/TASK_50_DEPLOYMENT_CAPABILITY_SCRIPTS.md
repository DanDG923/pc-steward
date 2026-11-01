# Task 50 — deployment capability script follow-up (ADR-022 alignment note)

**Authority.** [ADR-022](../adr/ADR-022-OWNER-ONLY-PROTECTED-TEST-DEPLOYMENT.md), last section: "Alignment note — protocol-2 capability statement", accepted by the owner on 2026-11-01. The work list is items 1–7 under "Follow-up script changes" in the [proposal record](TASK_50_ADR022_ALIGNMENT_NOTE_PROPOSED.md). The design comes from [adapter gates](TASK_50_ADAPTER_GATES.md) Part A, §A.3, §A.7 and §R A2-L1/A2-L2. Requirements: `FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`.

**Boundary.** These are script, payload and test changes only. No script was run against a host, a Program Files path, a certificate store or an installed root. The ledger schema (`pc-steward-owner-only-protected-test-v6`) and every writer's key set are unchanged, The C# `DeploymentCapabilityPolicy` changed in exactly one place, follow-up commit `0f5b54a`: Windows CI showed that Windows PowerShell 5.1 serializes the rollback writer's empty store list as `{}`, so `ValidStores` now accepts exactly an empty object for the Rollback variant only. Running any changed script on a host, and executing any redeploy plan, needs separate exact owner approval.

## What changed, item by item

| Item | Change |
|---|---|
| 1 | `OwnerOnlyPayloadIntegrity.ps1` gains the pure `ConvertFrom-OwnerOnlyDeploymentCapability`, which mirrors `TryParseCapability` byte for byte. `Assert-OwnerOnlyDeploymentCapabilityBytes` also requires protocol `2`. `Get-OwnerOnlyCandidatePayloadOrigin` returns `pinned-unsigned-v2` exactly when the manifest pins a root `PCSteward.DeploymentCapability.txt` of 1–1,024 B; a nested or case-variant entry is refused. `Read-OwnerOnlyCandidateManifest` takes an optional `-PayloadRoot` and returns `PayloadOrigin` and `HelperProtocol`. For a v2 manifest, `-PayloadRoot` is required. The capability file is read under a held `HoldStableFile` handle, its length and SHA-256 must equal the manifest entry, and then its bytes are validated. A v1 manifest behaves as before. |
| 2 | `Assert-OwnerOnlyPayloadProvenance` accepts a string origin of `pinned-unsigned-v1` or `pinned-unsigned-v2` only. It reads the pinned manifest with `-PayloadRoot <install root>` and requires the manifest-implied origin to equal the ledger origin. So v2 is recognised only with a valid pinned capability file, and v1 only without one. Every other case still throws `Unrecognised pinned-payload provenance.`. `Assert-OwnerOnlyPayloadProfile` accepts v1 and v2. |
| 3 | The installer validates the candidate with `-PayloadRoot $CandidateRoot` before any state, staging, certificate or signing effect, and re-validates the protected manifest copy against `$payloadStaging`, still before `New-OwnerOnlyCertificate`. It writes `payloadOrigin = $candidate.PayloadOrigin`, and the post-install check compares with that origin instead of the hard-coded v1. No other ledger key changed. |
| 4 | `Test-OwnerOnlyProtectedTestDeployment.ps1` adds `helperProtocol` to its report only for a v2 deployment. It re-reads the pinned manifest with the installed capability file. The v1 and legacy report shape is unchanged. |
| 5 | `Invoke-OwnerOnlyApprovedRedeployment.ps1` accepts plan schema `pc-steward-exact-redeploy-v1` (the existing plan) or `-v2`. Schema v1 cannot name origins and keeps `legacy-v6` → `pinned-unsigned-v1`, with the same `-AllowLegacyPayload` preflight and the same error text. Schema v2 must name `expectedOldOrigin` (from `legacy-v6`, `pinned-unsigned-v1` or `pinned-unsigned-v2`) and `expectedNewOrigin` (`pinned-unsigned-v1` or `pinned-unsigned-v2`). The pinned candidate's origin must equal the expected new origin before removal, and a v2 postcondition also requires `helperProtocol = 2`. A comment states that executing any new plan needs separate exact owner approval. The existing approved plan also pins the six old script hashes, so it refuses these new script bytes in any case. |
| 6 | `src/PCSteward.ElevatedHelper/PCSteward.DeploymentCapability.txt` (103 B) is checked in. `.gitattributes` marks it `-text`, so a Windows `autocrlf` checkout keeps LF. The helper project publishes it at the output and publish root. The private-beta Desktop-only package does not get it. |
| 7 | New real-writer fixture: `Fixtures/DeploymentCapabilityScripts.ps1`, driven by `DeploymentCapabilityScriptTests`. Windows-only v2 cases were added to `Fixtures/PayloadIntegrity.ps1`. A frozen copy of the pre-change v1-only validator is kept as `Fixtures/PreV2PayloadValidator.ps1.txt`, verbatim from blob `44af7c9` at `8496850`. |

## Tests

- `DeploymentCapabilityScriptTests.CheckedInCapabilityStatementEqualsTheCompiledCanonicalBytesAndIsPublishedAtTheRoot`: checks the file bytes against `CanonicalCapabilityBytes()`, the parse, the `.gitattributes` entry, and the csproj publish item with no `Link`.
- `BindingSetEqualsTheBuiltHelperDepsJsonProjectClosure`: this is the closest check that can be built here. The test project references the helper with `ReferenceOutputAssembly=false`, for build ordering only. The test reads the built `PCSteward.ElevatedHelper.deps.json` and checks that its `project` libraries equal `BindingSet` minus the seven fixed leaves. It also checks that every closure, helper and capability file exists in the output, and that the capability bytes there are canonical. A `dotnet publish -r win-x64` deps.json comparison is not made; it needs Windows publish evidence.
- `RealScriptWritersAndCapabilityParserAgreeWithThePolicy` has two rows, `pwsh` and `windows-powershell`. The fixture takes the **real** function and statement text from the parsed scripts: `Write-PendingLedger`, the catch-block rollback `Write-OwnerOnlyProtectedJson`, `$executableImages`, `$files`, `$completedState` and its write, and `Write-OwnerOnlyProtectedJson`. It executes that text with only the protected ACL write, `Set-OwnerOnlyAcl`, the PE identity read and the Authenticode query stubbed. It emits these ledgers: pending with a null thumbprint, pending with a certificate, rollback with zero stores (`null`), one store (a string) and several stores (an array), completed v1 and completed v2. Each one must satisfy:
  - no BOM;
  - `TryReadLedger` succeeds with the expected variant;
  - pending and rollback evaluate to Incomplete;
  - v1 evaluates to Absent;
  - v2 evaluates to **Supported** against the emitted manifest, the real capability file and the observations;
  - v2 with the v1 manifest, or with no capability file, evaluates to Mismatch.

  The same run also checks:
  - 29 capability vectors give identical results from the script parser and `TryParseCapability`;
  - the v1 manifest gives v1, the v2 manifest gives v2, and the nested, case-variant, empty and oversize entries are refused;
  - the profile and early provenance refusals;
  - the frozen pre-change validator throws `Unrecognised pinned-payload provenance.` on the real-writer v2 ledger;
  - every changed script and fixture parses with zero errors.

  Mutation checks were run here. Widening the script byte range to `0x7F` failed the parser comparison, and adding a key to `$completedState` failed `TryReadLedger`. Both changes were reverted.
- `DeploymentCapabilityPolicyTests.ExistingV1PlanStaysV1OnlyAndThePreChangeValidatorFailsClosedOnAV2Ledger` replaces the old v1-only script-text test, whose premise this slice intentionally ends.
- `Fixtures/PayloadIntegrity.ps1` is Windows PowerShell 5.1 and runs on Windows CI only. It adds real file, hold and hash cases:
  - v1 and v2 origin;
  - v2 without `-PayloadRoot`;
  - CRLF, protocol 3, route-missing and trailing-blank-line capability files, consistently pinned and refused;
  - pinned-then-drifted bytes;
  - full v2 provenance over a copied, synthetically signed tree;
  - a v1 origin with a capability file, and a v2 origin without one;
  - installed capability drift;
  - an AST check that both capability validations precede `New-OwnerOnlyCertificate`;
  - the v2 profile.

## Windows-evidence-only, and why

- **Windows PowerShell 5.1 bytes.** The deployment scripts run under Windows PowerShell 5.1: the bootstrap and fixtures use `powershell.exe`, and `Add-Type` references .NET Framework assemblies. The Linux run uses pwsh 7, whose `ConvertTo-Json` formatting differs. The `windows-powershell` row, with the same real writer text, runs only on Windows CI. It reports Inconclusive with that reason on Linux; it is not silently skipped.
- **The real file primitives.** `Read-OwnerOnlyDeploymentCapability`, `Read-OwnerOnlyCandidateManifest`, full provenance and the validator's `helperProtocol` path go through `PayloadIntegrity.Open`, which needs a drive-letter path, a fixed NTFS volume, `GetFinalPathNameByHandle` and `FindFirstStreamW`. The integrity script also calls `WindowsIdentity.GetCurrent()` at load. These run in `PayloadIntegrity.ps1` on Windows CI only.
- **Signing, ACLs, the mutex and the elevated redeploy path** remain host evidence that needs approval.
- **Legacy-v6 ledgers.** No current writer produces one. The pre-Task-48 writer used `Set-Content -Encoding utf8`, which writes a BOM under 5.1. The policy then reads the ledger as Unreadable, not Absent; this is a fail-closed refusal already pinned by `LegacyLedgerWrittenBySetContentWithTrailingNewlineIsStillAbsent`. Real-writer legacy generation is not claimed.

## Linux evidence

The Release builds of all four test projects have zero warnings and zero errors. Results:

- Domain 167/167.
- Architecture 42/42.
- Security: 415 passed, 9 failed, 1 Inconclusive (the `windows-powershell` row) of 425. The pre-change baseline at `8496850` was 412 passed, 9 failed of 421. The failed sets are identical: the 9 Windows-native classes, which include both `PinnedPayloadRejectsDriftEscapesAndUnapprovedSigningChanges` rows.
- Application: 780 passed, 103 failed of 883. The failed set is identical to the baseline.

The build has zero warnings. pwsh 7.x is at `/usr/bin/pwsh`. In this session the worktree isolation guard refused direct `pwsh` invocations, so the parse check and fixtures ran inside the test-spawned pwsh process.

## Independent review (Fable 5.1, scope `8496850..db64a7e`): CONDITIONAL, no safety defect

The reviewer confirmed the following:

- **Approved-plan safety.** A schema-v1 plan keeps the legacy-v6 preflight and the v1 postcondition. It refuses origin fields and cannot reach a v2 candidate, which is strictly tighter than before. The existing plan also pins the old script hashes.
- **Script/C# parser parity.** The script and C# capability parsers agree check for check across 29 shared vectors.
- **Ordering.** Validation runs before any effect or signing.
- **Provenance and validator.** Provenance, the validator's `helperProtocol` output and the capability-file bytes and attributes are correct.
- **No silent skip.** The Windows PowerShell 5.1 row really runs on Windows.

It reported one Medium finding: the Linux pwsh row masked the 5.1 `{}` serialization. That is fixed in `0f5b54a` (narrow and fail-closed, with unit tests). Its conditions are Windows CI green on `0f5b54a`, which proves the remaining 5.1 ledgers, and these corrected doc statements. Info items: a downgrade expressed by a new exact v2 plan is not forbidden by the accepted note; a v1 ledger with a v2 manifest fails with a capability error before the "Unrecognised" error, which is still fail-closed.

