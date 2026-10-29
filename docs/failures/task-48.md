# Task 48 — Exact installed candidate mismatch

**Final resolution — Task 48 complete:** Pinned artifact repair/deployment passed, and after owner .NET installation plus fresh explicit approval the app reached Overview and closed normally in **22.826 s / native exit 0**. Fresh copied database hash/integrity/schema checks and final protected validator pass. [Authoritative completion record](../review/TASK_48_COMPLETION.md) supersedes earlier open launch-stop statements below; both original failure records remain historical. No automatic retry, forced termination, distribution gate closure or Task 49.

**Later launch stop:** Artifact identity remains resolved, but the first separately approved launch stopped at a .NET prerequisite, native `0x80008096`, before product startup/data creation. The owner then installed .NET 10; no retry has run. [Current launch evidence and proposed next step](../review/TASK_48_LAUNCH_RUNTIME_STOP.md) supersede earlier unattempted-launch statements without changing the historical artifact failure below.

**Resolution, 2026-10-29:** The owner subsequently accepted the pinned-payload repair and approved its exact guarded removal/redeployment plan. [Actual execution](../review/TASK_48_PINNED_REDEPLOY_EXECUTION.md) passes native exit 0, strict pinned provenance, all 100 non-signed hashes and both signed-image reconstructions; desktop DLL/PDB now match the original approved bytes. This resolves the artifact gate without claiming build determinism. The original failure/diagnosis below is retained as history. Separate responsive launch approval/evidence remains pending; Task 48 is not yet complete.

Date: 2026-10-29. Requirements: `FR-DAT-12–15`, `AC-DAT-18`, `FRA-14–16`.

## Stop condition and authority

After the owner's explicit **“Approved”** response to the concrete fresh-install/temporary signer plan, one guarded elevated ADR-022 installation ran. The [Task 48 report](../review/TASK_48_OWNER_ONLY_REDEPLOY.md) contains the pinned source/scripts/candidate, authority, commands and complete sanitized evidence. The task's explicit **hash mismatch stops the run** condition was reached; no second installer attempt, launch, removal or rollback was performed.

## Commands and results

1. Pinned unchanged `powershell.exe -NoProfile -File .\scripts\Install-OwnerOnlyProtectedTestDeployment.ps1 -Install -CreateOwnerOnlyCertificate`, through a hidden elevated log-capturing wrapper under the same Windows SID. Installer stdout ends `validation: Pass`; stderr is empty. The wrapper recorded a null process exitCode, so the installer exit code is unknown rather than asserted 0. It was not retried.
2. `powershell.exe -NoProfile -File .\scripts\Test-OwnerOnlyProtectedTestDeployment.ps1`: directly captured **exit 0 / Pass**.
3. Read-only comparison of all installed relative paths with the pinned `unsigned-candidate-manifest.json`, requiring all non-signed file hashes to match: enclosing comparison command exited **1**, with exact error **“Installed payload drift: path differences 0; unsigned file differences 2. Stop without launch/retry.”**

| File | Expected SHA-256 | Actual SHA-256 |
|---|---|---|
| `PCSteward.Desktop.dll` | `12C1B111B9DD4832C5D0E49BB554A8766CC2B8795071A547102CEDB51450185D` | `DD6F7D245EC176D6E67073958C95FC87BBAC9F77A2EA73F52A819BBF2C9F12B7` |
| `PCSteward.Desktop.pdb` | `96B6B8C862FF42BDD626ADB7C26E59C7B415294123A6C4A04FB93C1659C4F2CC` | `7D3EFD6E11171073C0C048A0D141A7A05467AF64D00C385D9B8173D02DBB91BD` |

All 102 paths match; 98 of 100 non-signed hashes match. Both signed executables are valid with the same installer-owned signer. All 188 input hashes remain unchanged. The per-user state/database root remains absent, and no PCSteward process is running. Complete protected deployment remains installed; no retry/cleanup was authorized by the mismatch.

## Smallest suspected cause and next safe step

**Consultation update:** owner-requested Opus 5.5 analysis, independently rerun by the parent, disproves the output-path explanation below. The differing artifacts contain different CsWinRT AOT generator output; all three differing PDB document hashes are generator-owned, with identical embedded PDB paths and unchanged handwritten source. The underlying cross-process nondeterminism hypothesis remains unproven. See [the concrete pinned-payload fix proposal](../review/TASK_48_PINNED_PAYLOAD_FIX_PROPOSAL.md) and Task 48's consultation evidence. Recommended fix is to consume verified frozen payloads instead of recompiling during privileged deployment. ADR-022 amendment acceptance is pending; no production script/installed state changed. The original suspected-cause paragraph below is retained as historical diagnosis, not the current conclusion.

The two differing outputs are the WinUI desktop assembly and its debug symbols. The installer rebuilds them in a unique protected staging/output directory, while preparation used the repository's ignored artifact directory. Output-location-dependent generated WinUI/debug information is a plausible cause, but a limited read-only PDB marker check did not prove it. Do not relabel the candidate hash or waive the exact artifact gate on that inference.

Next technical step is isolated local reproducibility diagnosis across output directories, without invoking the installer or installed app. Establish exactly which metadata/generated inputs differ, then prepare a reproducible pinned artifact/deployment plan and obtain any new required host-action authority. Any deliberate removal of the retained protected roots/signer needs separate exact approval through the existing guarded remover. Launch remains unapproved and blocked by this failed gate. No automatic Task 49 or live provider operation follows.

## Files and retained evidence

Repository changes: `docs/review/TASK_48_OWNER_ONLY_REDEPLOY.md`, `docs/STATUS.md`, `docs/requirements/TRACEABILITY.md`, this failure record. No runtime source/project/dependency/script was changed. The ignored wrapper and raw authority/process/log/manifest/ledger evidence are under `artifacts/task-48-deployment/`; the approved candidate remains under `artifacts/task-48-preparation/`.

No Git identity/configuration change, commit, push or release. The installer-owned signer expires `2026-11-12T17:36:46Z`; it and the completed protected deployment are retained pending a separately authorized next action.
