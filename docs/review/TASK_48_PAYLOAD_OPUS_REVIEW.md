# Task 48 — Independent Opus review of the pinned-payload fix

Date: 2026-10-29. Reviewer: fresh Opus 5.5 review delegate (read-only; no subagents/worktrees). Requirements: `FR-DAT-12–15`, `AC-DAT-18`, `FRA-14–16`, `TM-04`.

## Snapshot identity

- HEAD `cd6d4530ac4e8c1e31a07b1c9096ffd358e75342`; pin manifest `artifacts/task-48-deployment/implementation-review-pins-v2.json` SHA-256 `5A19C8F8…DA249202` (matches).
- All ten pinned file hashes matched **before** and **after** the review. Host: Windows PowerShell 5.1.26100.
- No install/removal/UAC/certificate-store change, app launch, network, Git mutation or source edit. The only writes were this report and probe scripts/logs under `artifacts/task-48-formal-review/`.

## Independent evidence gathered

| Probe (read-only / isolated) | Result |
|---|---|
| `probe.ps1`: approved candidate manifest `43F209AD…B7AE` through `Read-OwnerOnlyCandidateManifest` + `AssertTree` on `%LOCALAPPDATA%\OwnerOnlyDeploymentCandidates\task-48-43F209AD1E79\payload` | **PASS**: 102 entries, 50,051,220 bytes, max relative path 67 chars (≈164 chars once staged, under MAX_PATH) |
| `GetUnsignedImage` on both candidate apphosts | PASS (PE32+ x64, aligned, no overlay; original checksum 0) |
| `AssertSignedImage` on the retained installed apphosts against candidate identities | PASS for both: the reconstructed bytes equal the raw manifest hashes |
| CMS shape of the retained installed apphosts (made by the old route, default `IncludeChain`) | 1 certificate, 1 signer, RSA-2048 key, signed attributes exactly {9.3, 9.4, 311.2.1.11, 311.2.1.12}, 0 unsigned attributes. `AssertSignerBlob` passes its strict DER, envelope and 5-field SignedData checks, then rejects at "SHA-256 is required" |
| `addtype-temp-probe.ps1` (TEMP redirected to an isolated directory) | `Add-Type` in the helper writes `*.tmp` and the compiled `*.dll` to `%TEMP%`, then loads the assembly in memory (`Location=''`) |
| Re-ran `Fixtures/PayloadIntegrity.ps1` directly | **PASS, 65 assertions** |
| PS 5.1 `ConvertFrom-Json` with a duplicate `path` key | Accepted silently; the last value wins; the property set still reads `path,bytes,sha256` |

The CMS evidence makes it likely that the native `Set-AuthenticodeSignature -HashAlgorithm SHA256` output fits the profile: the old blobs pass every structural gate before the SHA-1 rejection. It is still not a proven native SHA-256 run.

## Findings

No Blocker found. Most of the design holds up when I challenge it. Staged full-tree hashes come before any signer mutation. Signing and the strict check run in protected staging before the install copy. Exact original-byte reconstruction (with only the checksum restored and the security directory zeroed) combined with a Windows Valid result under the same no-write/no-delete held handle covers every Authenticode-hashed byte, and every byte outside the certificate table. Certificate offset equals the original EOF, there is one record and the padding is zero. Rejection of reparse points on ancestors and descendants, plus final-path checks on each handle, defeats junction and swap races on the source side. The protected v6 ledger stays compatible with the unchanged remover: the remover does not check the ledger's shape, `candidate-manifest.json` gets the exact DACL, and staging is either removed on success or rolled back.

### M1 — Medium (High if `TM-04` treats same-user non-elevated code as an adversary): the elevated route still executes code from user-writable locations

- `scripts/Install-OwnerOnlyProtectedTestDeployment.ps1:115` dot-sources the helper from `$PSScriptRoot`, which is the user-writable OneDrive checkout. `scripts/OwnerOnlyPayloadIntegrity.ps1:3` then runs `Add-Type` inside the elevated process, and the probe shows csc writes intermediates and the compiled DLL to the invoking user's `%TEMP%` before loading them. Files there inherit the user's FullControl.
- Failure scenario: a same-user, non-elevated process watches `%TEMP%`. It replaces the csc-produced DLL between compile and load, or it edits `OwnerOnlyPayloadIntegrity.ps1` after the owner checks the pins but before line 115 runs. Either way it gets arbitrary code into the elevated installer before the signer is created, and that code can sign anything.
- This is strictly better than the old elevated `dotnet build`. It still contradicts the accepted rationale of removing user-writable build intermediates from the privileged path. The pin manifest gives review-time identity only, not execution-time identity.
- Smallest fix:
  1. Before the dot-source in the installer, create a unique admin-only directory with a protected ACL (for example under `%SystemRoot%\Temp`), point `$env:TMP` and `$env:TEMP` at it, and restore and remove it in `finally`.
  2. In the host plan, copy the pinned installer, helper, validator and remover into an admin-only directory, re-verify their SHA-256 values there, and run only that copy.

  Alternatively, record an explicit owner acceptance that same-user code is trusted at install time.

### L1 — Low: the validator derives the signed-apphost hash outside the validation lease

- `scripts/OwnerOnlyPayloadIntegrity.ps1:303-305`: `Test-OwnerOnlySignedExecutable` releases its handle, then `Get-FileHash` recomputes the hash, and `AssertTree` checks the file against that self-derived value. Swapping the file between those two steps is undetected by the provenance step. Only Administrators can write the install root, and validator line 33 separately compares against the ledger `files`, so the impact is limited.
- Fix: hash from the held stream inside `Test-OwnerOnlySignedExecutable`, return it, and compare it with the ledger `files` entry. The same applies to `Install…:164`.

### L2 — Low: the "fixed RSA-2048/SHA-256 profile" is enforced only on the created-certificate route

- `Install…:149` accepts any `-CertificateThumbprint` that has a private key. `AssertSignerBlob` (`OwnerOnlyPayloadIntegrity.ps1:247`) checks the RSA OID and the digest, but not key size or subject. The ledger then records `ownedCertificateStores=@()`.
- Fix: the plan uses `-CreateOwnerOnlyCertificate` only. Either reject `-CertificateThumbprint` for `pinned-unsigned-v1`, or require RSA ≥2048 and the owner-only subject.

### L3 — Low: a staging-cleanup failure rolls back a fully validated deployment

- `Install…:180` removes staging after the completed ledger is written (line 175) and after both validations (lines 177–178). If an antivirus handle on a freshly signed staged `.exe` makes `Remove-Item -Recurse` fail with "directory not empty", the catch block deletes the install root and the certificates. The state removal will probably hit the same handle, which leaves a `rollbackRequired` ledger. Recovery still works, but it costs an approved host run.
- Fix: remove staging immediately after the install copy at line 171, before the completed ledger. Alternatively, leave staging inside the protected state root for the remover to delete.

### L4 — Low: the exit-code fixture does not exercise the real caller's process origin

- `Wait-OwnerOnlyDeploymentProcess.ps1:20` takes `process.Handle` lazily. The fixture (`PayloadIntegrity.ps1:179`) uses `Start-Process` without `-Verb`. In PS 5.1 my understanding is that this path returns a `GetProcessById`-backed object with no retained handle. If the child exits (or its PID is reused) before the helper runs, the helper fails, or in the reuse case could wait on the wrong process. The real `-Verb RunAs` path retains the `ShellExecuteEx` handle and is fine, but it is untested.
- Fix: the later caller passes the `-Verb RunAs -PassThru` object straight to the helper, without `-Wait`. Use a timeout long enough for the CurrentUser\Root consent dialog. Report RunAs capture as unproven until it has been run.

### L5 — Low: hardening and test gaps

- Duplicate JSON keys are accepted (`OwnerOnlyPayloadIntegrity.ps1:268-272`; probe confirmed). A raw manifest can therefore read differently to a human or another parser than to the installer. Fix: reject duplicate keys by tokenizing, or accept this as fine because the manifest is generated locally and the installer and validator share one parser.
- The SpcIndirectData type OID (`SPC_PE_IMAGE_DATAOBJ`) is not checked (`OwnerOnlyPayloadIntegrity.ps1:254`). Windows Valid covers this, so it is cosmetic.
- There is no fixture for:
  - installer ordering (no certificate or store mutation before the staged `AssertTree` and `GetUnsignedImage`). Only "no `dotnet`" is checked statically, at `PayloadIntegrity.ps1:174-176`;
  - a wrong ledger `originalChecksum` or a tampered `executableImages.bytes`;
  - the `-RequirePinnedPayload` branch (proven only by the manual host log).

  A static AST ordering check and two provenance negatives would be cheap to add.
- `CopyTreeProtected` and `WriteProtectedFile` (owner set at creation) are unexercised, because they need elevation. Documented as such.

## Unproven host limits (must stay explicit in any plan)

1. Elevated creation of the owner and DACL at creation time through the .NET Framework `FileSecurity`/`DirectorySecurity` constructors, and their exact match with `Assert-ExactDacl`.
2. Native `SignerSignEx` SHA-256 output passing `AssertSignerBlob`. This is likely, given the old blob shape, but unproven. A failure would happen after the certificate is created, and the rollback path would then run for real.
3. Windows Valid under the elevated token's CurrentUser stores for the new signer, and its fail-closed expiry at 14 days.
4. RunAs native exit capture.

## Recommended verdict

**Conditionally ready to draft (not approve) the exact later removal/redeploy plan.** Before presenting it:

- resolve M1, by code (TEMP redirect) plus plan (run from an admin-only, hash-verified script copy), or by explicit owner acceptance;
- have the plan mandate `-CreateOwnerOnlyCertificate`, `-RequirePinnedPayload` and the RunAs caller pattern from L4.

L1–L3 are small local fixes, and are worth doing first because each one could cost an approved host run. This review does not validate the live route and gives no host authority.

## Questions for the parent

1. Does `TM-04` treat same-user non-elevated code as an adversary during this elevated install? That decides whether M1 is High or Medium, and whether running from an admin-only script copy is mandatory.
2. Which exact caller pattern produced the original null exit code (`-Wait`, `-Verb RunAs`, or no verb)? Will the later caller use `Start-Process -Verb RunAs -PassThru` and give a timeout of at least several minutes?
3. Should `-CertificateThumbprint` be refused for the pinned route (L2)?
4. After a successful redeploy, should the validator default to `-RequirePinnedPayload` and keep legacy mode only as an explicit opt-in?
