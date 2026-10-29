# Task 48 — Fresh independent re-check of the M1 protected bootstrap and exact host plan

Date: 2026-10-29. Reviewer: fresh Opus 5.5 review delegate (read-only; no subagents/worktrees). Requirements: `FR-DAT-12–15`, `AC-DAT-18`, `FRA-14–16`, `TM-04/TM-06`.

## Snapshot identity and constraints honoured

- HEAD `cd6d4530ac4e8c1e31a07b1c9096ffd358e75342`. Pin manifest `artifacts/task-48-deployment/implementation-review-pins-v4.json` SHA-256 `C420A961…F227F5` (matches). All **19** pinned hashes matched **before and after** this review (`artifacts/task-48-m1-rereview/pins-before.log`, `pins-after.log`).
- I did not execute the prepared bootstrap, stub or template, and did not use elevation, UAC, install/removal, the certificate store (beyond a read-only `Test-Path`), the network, Git mutation or source edits. The only writes are this report and the isolated probes/logs under `artifacts/task-48-m1-rereview/`. A synthetic compiler simulation directory was created there and then deleted.

## Independent evidence

| Probe | Result |
|---|---|
| `decode-compare.ps1` | The encoded file (18,604 chars, SHA `3D3B1DD8…6C3F`) base64/UTF-16 decodes byte-for-byte to `bootstrap-stub.ps1` (`B33D6178…F33D`). Gunzipping the stub's literal gives exactly the `bootstrap-plain.ps1` bytes (`74B6CE6A…3B49`, 16,734 B). The stub checks that same literal hash before `. [scriptblock]::Create`. |
| Plain composition | The plain program is exactly: here-string(`authority.json`) + `scripts/OwnerOnlyExecution.ps1` + LF + `scripts/Invoke-OwnerOnlyApprovedRedeployment.ps1`. The embedded JSON equals `authority.json`. Nothing else is present. Both the plain program and the stub parse with 0 errors. |
| Six-file bundle | All six authority hashes equal the v4 pins **and** the bytes in `scripts-v2`. `scripts-v2` contains exactly those six files. |
| Host baseline (read-only, non-elevated) | Source/candidate/manifest/ProgramFiles paths pass `Assert-OwnerOnlyExecutionPath`. The execution root is absent. The old ledger is `05884590…4585`. `%LOCALAPPDATA%\PCSteward` is absent. There are 0 PCSteward processes. |
| `input-autovar.ps1` | Assigning the `$input` automatic variable at template :89 works when dot-sourced from `-EncodedCommand` at top level. This is a style nit only. |
| `compiler-sim.ps1` | Uses the exact `-Directory -Compiler` descriptor. For a non-elevated simulation, owner and Administrators were replaced by the current SID. `CreateDirectory(path, DirectorySecurity)` works. A second `CreateNew` claim is rejected. With TEMP/TMP redirected, `Add-Type` writes `.cs`/`.tmp` there, cleans up, and loads in memory. Files inherit `OWNER RIGHTS:ReadPermissions,Synchronize`. This is not proof of elevated owner/ACL-at-creation. |
| `readonly-probes.ps1` | `Set-Location` changes only the PowerShell location; `[Environment]::CurrentDirectory` stays unchanged (see L1). `Test-Path` on a missing PS drive returns `False`. The `Cert:` drive is available. HKCU\Environment holds only `Path,TEMP,TMP,OneDrive*,ChocolateyLastPathUpdate`: no `COR_*`/`COMPLUS_*`/`DOTNET_*` today. |
| `stdout-purity-ast.ps1` (heuristic) | No uncaptured success-stream output was found in the installer, remover or validator besides their final JSON. The bootstrap's `…\|ConvertFrom-Json` should therefore not fail falsely, though this is not proven live. |

## What holds up under challenge

- **Code identity:** the approved immutable string carries the program and the authority. It does not trust the writable checkout or `-File`. Script bytes are read once under a share-read handle, hashed in memory, and written from the **same** buffer with `CreateNew` and an admin-owned descriptor at creation. They are re-hashed and their ACLs checked before the first dot-source at template :106. Swapping the source after the hash cannot change what runs. Reparse checks on the source are defence-in-depth only.
- **Compiler chain:** helper :2–6 and waiter :7–11 run `Assert-OwnerOnlyExecutionEnvironment` before any elevated `Add-Type`, so an elevated run outside the fixed bundle, or with a different TEMP/TMP, refuses. The compiler directory is new, is not writable by Users, and its OWNER RIGHTS ACE removes implicit owner WriteDac even if csc's files end up user-owned. `PSModulePath`/`PATH` are reset at template :58–59, before the first cmdlet autoload. The stub uses only .NET types.
- **Claim/atomicity:** an admin-only parent plus a unique GUID name, then a `CreateNew` claim. A losing concurrent run never sets `$ownsExecution`, so it cannot write `result.json` into the winner's transaction.
- **Failure boundaries:**
  - Any failure before `approved-removal` leaves the old deployment intact.
  - A removal-phase failure is reported with its phase.
  - A failure in the installer phase relies on the installer's own transaction rollback. The old deployment stays removed, as the plan discloses.
  - A validation failure leaves the new install in place for inspection.
  - TEMP/TMP are restored in `finally`. Infrastructure is retained, and the process never retries or loops.
- **L1–L5 closure (spot-checked):**
  - `-CertificateThumbprint` is refused (installer :112).
  - Staging is removed before the completed ledger (:172→:175).
  - Signed hashes come from `Test-OwnerOnlySignedExecutable` (:158/:163).
  - Profile selection is strict-default with an explicit legacy mode (validator :5/:37).
  - I did not re-audit the CMS/duplicate-key code beyond the passing fixtures.

## Findings (no Blocker)

### M1-R1 — Medium (owner decision, doc-only): the residual same-user surface is understated

`docs/review/TASK_48_PAYLOAD_REVIEW_CLOSURE.md:11` says "No same-user trust exception is adopted". The new code closes every route in the original M1: the mutable checkout script, the compiler `%TEMP%` swap, and user module/PATH resolution. However, a same-user process that is active during the install still has routes that **no bootstrap code can close**:

1. **CLR environment injection.** Elevated processes receive the user's HKCU\Environment variables. `COR_ENABLE_PROFILING`/`COR_PROFILER`/`COR_PROFILER_PATH` (and `COMPlus_*`) load a user-chosen DLL into the elevated `powershell.exe` before line 1 of the stub. This is a well-known UAC bypass class. None are set today (probe).
2. **The caller process.** Between its literal-hash check and the ShellExecute call, the non-elevated caller is same-user debuggable. UAC shows only `powershell.exe`, not the arguments.
3. **The signer key.** The fresh signer's private key is DPAPI-protected under the user's own credentials. `NonExportable` is not a boundary against same-user code.
4. **The trust stores.** CurrentUser TrustedPublisher (silently) and CurrentUser Root (with one click) are writable by the same user. So `Valid` is inherently same-user trust.

Scenario: same-user malware sets `COR_PROFILER*` in HKCU\Environment before the owner clicks UAC. It then runs as admin inside the bootstrap and can sign or modify anything; every subsequent check is subvertible.

Smallest fix:

- **Docs:** reword the closure and plan to say that M1's file/compiler TOCTOU routes are closed, and that an active same-user adversary during the install remains residual. This is consistent with Microsoft's position that same-desktop UAC is not a security boundary.
- **Optional best-effort caller preflight (read-only):** refuse if HKCU\Environment or the caller's environment contains `COR_*`, `CORECLR_*`, `COMPLUS_*` or `DOTNET_*`. This is cheap, but it is detection, not a boundary.

This does not block requesting the test. The owner should knowingly accept the narrowed statement instead of "no same-user trust".

### M1-R2 — Medium (plan completeness): the exact non-elevated caller is not prepared or pinned

`docs/review/TASK_48_EXACT_REDEPLOY_PLAN.md:37` describes the caller only in prose. No caller artifact exists in `artifacts/task-48-redeploy-plan-v2/`; the preparation script, `Prepare-ExactRedeploymentPlan-v2.ps1`, generates none.

The caller fixes several things the approval depends on:

- the exact argv;
- the stable-handle read plus the literal-hash comparison;
- which `Wait-OwnerOnlyDeploymentProcess.ps1` it loads, and the 900,000 ms timeout;
- the RunAs/PassThru/no-`-Wait` pattern;
- where the native exit code is recorded.

Scenario: the caller is typed ad hoc at approval time with `-Wait`, `-File`, a mistyped hash or a missing timeout. That would repeat the original null-exit problem or weaken the immutability claim that M1 relies on.

Smallest fix: generate a small `invoke-caller-v2.ps1` (non-elevated) next to the encoded file, pin its SHA-256 in a v5 manifest, and name it in the approval. Its argv should be:

`-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand <verified string>`

- `-NonInteractive` turns any unforeseen PowerShell prompt in the **hidden** window into an error instead of a 15-minute hang.
- An explicit execution policy removes the dependency on the same-user HKCU policy. Today the effective policy is `CurrentUser RemoteSigned`, so the run would work. If that policy ever became Restricted or AllSigned, the run would fail at template :106 after consuming the single-use execution root.

None of this changes the 74B6…/3D3B… program artifacts.

### L1 — Low: "protected working directory" covers only the PowerShell location

Template :103 calls `Set-Location`. The probe shows the process CWD (`[Environment]::CurrentDirectory`) is unchanged; csc/CodeDom children and native DLL search use the process CWD. Elevated RunAs processes normally start in `System32`, so the impact is probably nil, but nothing asserts it.

Fix: add `[Environment]::CurrentDirectory=$scripts` next to :103. This requires regenerating the program (`-v3`) and its pins, so only do it if the program is regenerated for another reason. Otherwise, correct the claim in the closure (:13) and plan (:42).

### L2 — Low: Root-store deletion prompt and remover ordering are not in the plan

The unchanged remover deletes the install root (Remove :84) **before** removing certificates (:85). Deleting from CurrentUser\Root raises a Windows confirmation dialog from the hidden-window elevated process. If the owner declines or misses it, the old install is gone, the state root and signer remain, and the result is phase `approved-removal`. Recovery then needs separate approval.

Evidence that a hidden-window process can still show this UI: the original run's Root *add* succeeded from a `-WindowStyle Hidden` child, and Task 39 used the remover live.

Fix (plan text): list the expected prompts in order: UAC; "delete" old Root `61FC…`; "install" the new Root signer. State that each needs "Yes", and give the consequence of declining.

### L3 — Low/Info: robustness nits (no change required)

- Template :121 checks for the old signer with `Test-Path Cert:\…`. On a missing drive this returns `False` (false-absent). It is safe today only because `Get-Acl` at :77 autoloads `Microsoft.PowerShell.Security`, and the remover's `Assert-ExactCertificateAbsent` uses `X509Store`. Prefer `X509Store` at the next regeneration.
- Template :128 does not cross-check that the validator's thumbprint equals the installer's and differs from `oldThumbprint`. This is cosmetic, because removal is already verified.
- `Wait-OwnerOnlyDeploymentProcess.ps1` is copied into the protected bundle but is not used elevated. This is harmless.

## Still unproven (must stay explicit; I confirm none is claimed)

1. Elevated owner/DACL at creation via `CreateDirectory`/`FileStream(…, FileSecurity)` with owner Administrators.
2. Actual elevated csc inheritance and owner under the compiler ACL.
3. Native `Set-AuthenticodeSignature -HashAlgorithm SHA256` output passing strict CMS.
4. Windows `Valid` under the new signer, and the 14-day expiry.
5. RunAs native exit capture through the retained ShellExecuteEx handle.
6. The full new validator profile on a real install.
7. Live behaviour of the Root delete/add prompts under this exact hidden route.

## Questions for the parent and owner

1. Will the owner accept the M1-R1 wording ("file/compiler TOCTOU closed; an active same-user adversary during install is residual")? If not, no UAC-based same-user design can meet the stricter bar. The alternative is a separate admin account or offline signing, which is a scope change.
2. Will the exact caller be generated and pinned (M1-R2) before the approval request, so the owner approves concrete text plus three hashes?
3. Should L1/L3 be folded into a `-v3` regeneration now? I'd skip it unless something else forces regeneration. That avoids re-pinning churn, and the impact is negligible.

## Verdict

**Ready to request one exact owner-approved live removal/redeploy test once two doc/plan-level items are done:**

- M1-R1: correct the residual-trust wording and get the owner's informed acceptance.
- M1-R2: prepare and pin the exact non-elevated caller with `-NonInteractive` and an explicit execution policy, and list the expected prompts (L2).

No change to the reviewed bootstrap code or the 74B6…/3D3B… artifacts is required. The plain program corresponds exactly to template + ACL functions + authority + the six pinned scripts. Hash, copy, execute and TEMP ordering are correct, and failure/cleanup boundaries match the plan. This review does not validate the live route and grants no host authority.
