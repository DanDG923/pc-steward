# Task 48 — Opus review response and local remediation

Date: 2026-10-29. Requirements: `FR-DAT-12–15`, `AC-DAT-18`, `FRA-14–16`, retained `TM-04/TM-06`.

This is the parent's response to [the pinned independent review](TASK_48_PAYLOAD_OPUS_REVIEW.md), not a replacement of the reviewer report. The review was **conditionally ready to draft**, not a failed numbered phase gate. No Blocker was found; M1 had to be resolved before a host-plan request. Small findings are fixed locally; the substantial new M1 bootstrap/plan receives a fresh independent re-check.

## Answers and disposition

| Finding/question | Response and evidence |
|---|---|
| M1 / same-user adversary | The reviewed mutable-script/compiler-file substitution paths are removed. This owner-controlled UAC test still assumes a trusted Windows account/session: it does not isolate from active same-user compromise of CLR startup environment, the non-elevated caller, signer-key use or CurrentUser trust stores. No claim of complete same-user adversary isolation is made. The prior threat-model host proof explicitly excludes clean installer lifecycle. This narrower statement is disclosed for exact action-time approval. |
| M1 / writable scripts | Prepared exact caller uses reviewed encoded program contents, not mutable checkout `-File`. It hash-checks and copies six script files into a unique admin-owned ProgramFiles execution bundle before any of those scripts execute. Hashes are embedded in the approved immutable command, not read as authority from the writable bundle. |
| M1 / compiler TEMP | Bootstrap creates compiler directory with SYSTEM/Administrators FullControl and inheritable OWNER RIGHTS ReadPermissions/Synchronize; no Users grant. This prevents a file owner from gaining implicit WriteDac. TEMP/TMP are set before helper load; elevated helper and exit helper check protected code/root/compiler context before Add-Type. Framework references use trusted full paths. Bootstrap uses system module path/PATH and a protected PowerShell location. The caller requests native CWD System32; Set-Location alone does not change native process CWD. |
| L1 / signed hash lease | `Test-OwnerOnlySignedExecutable` returns a file record hashed from the held stream. New ledger hashes come from that record; provenance compares against the protected ledger before using it in the full-tree check. No released-file self-derived hash remains for signed executables. |
| L2 / certificate reuse | Pinned installer requires `-CreateOwnerOnlyCertificate`; `-CertificateThumbprint` is explicitly refused before elevation/effects. The default remains fresh RSA-2048/SHA-256, no timestamp. |
| L3 / cleanup commit order | Staging deletion precedes the completed ledger. A cleanup failure is a pre-commit failure following declared transaction-owned rollback, never a completed deployment followed by implicit rollback. |
| L4 / original null exit | Original elevated wrapper started a second child with no Verb and no `-Wait`, redirected its output, then called `WaitForExit`, `Refresh`, `ExitCode`; the latter was null. The outer wrapper itself originated from RunAs. New caller passes the original RunAs/PassThru object directly to the native waiter without `-Wait`, up to 900,000 ms. Tests for ordinary child exits 0/7 do not prove RunAs; actual elevated capture remains a required host observation. |
| L5 / duplicate JSON | A bounded decoded-key lexical pass rejects duplicate/case-colliding/escaped duplicate keys before ConvertFrom-Json. Complete otherwise-valid duplicate manifest is a meaningful negative fixture. |
| L5 / image type | Strict CMS parser now requires SPC_PE_IMAGE_DATAOBJ, with a cryptographically signed wrong-type negative fixture. |
| L5 / ordering/provenance | AST fixtures check staged hashes/image before signer mutation and staging removal before completed ledger; real provenance fixtures reject wrong recorded checksum/length/ledger hash. |
| L5 / pinned default | Validator defaults to pinned provenance; `-AllowLegacyPayload` explicitly opts into old v6 inspection. `-RequirePinnedPayload` remains explicit for the later plan; contradictory flags reject. Shared profile-selection function is directly tested. |

OWNER RIGHTS semantics are documented by [Microsoft's well-known SID reference](https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/manage/understand-security-identifiers): the ACE replaces the owner's implicit read-control/write-DACL grants. The actual elevated inheritance/access behavior is still a host-test prerequisite, not a synthetic-fixture claim.

## New files and protected execution contract

- `scripts/OwnerOnlyExecution.ps1`: pure .NET/PowerShell ACL creation/assertion and fixed NTFS/non-reparse environment checks; never compiles or mutates merely on import.
- `scripts/Invoke-OwnerOnlyApprovedRedeployment.ps1`: inert template requiring an exact embedded approval record; it is not run as a mutable `-File`. A prepared command embeds the ACL functions/template and exact authority as compressed/hash-checked immutable data. The loader uses only .NET calls before system module-path setup.
- The script bundle is copied by bounded stable read handles and raw SHA-256 checks to the exact private execution root; protected copies are verified again. Same installing SID, original protected ledger hash/signer, unchanged absent user data and no PCSteward processes are checked before removal.
- The bootstrap invokes only the fixed protected remover, pinned installer and independent validator. It performs exactly one approved removal, one fresh signer/pinned installation and mandatory new-profile validation; no retry/launch/provider operation is present.
- TEMP/TMP are restored in finally. Protected script/compiler/evidence infrastructure is **retained by the declared plan**, including on partial outcomes; there is no implicit recursive bootstrap cleanup. Deliberate removal of that infrastructure is a separate exact owner action. This avoids unsafe cleanup racing unknown children or invalidating the code needed to inspect the result.

## Local evidence and remaining limits

Regression run before changes failed at **“duplicate JSON keys rejected”**. Expanded fixtures now pass **85 assertions**, including memory ACL policy/OWNER RIGHTS and unsafe context checks, but no elevated directory creation or real certificate-store mutation. Earlier 65-assertion review evidence remains historical. Code and generated program parse; gzip round-trip and program hash match. The exact encoded program was smoke-tested only in a verified non-admin child: native exit 1 with “Exact installing SID and elevation required”, before any execution-root creation. No elevated transaction has run.

Final local gates after the source changes: focused **1/1**, Release build **0 warnings/errors**, full suite **291/291**, zero skipped. Logs are `closure-final-focused.log`, `closure-final-build.log`, `closure-final-tests.log`; the negative caller smoke is `non-elevated-bootstrap-negative.log` under ignored `artifacts/task-48-deployment/`. Explicit legacy validation still exits 0; default pinned validation rejects the old install with exit 1. Product `src/`, installed files/signer and user-data absence remain unchanged.

The native copy/ACL-at-creation API, actual compiler-file inheritance under elevation, native SHA-256 signing/Windows trust under the new signer, signer expiry and RunAs capture remain unproven until separate approval. Old installed signatures reconstruct correctly but are SHA-1, so they are strict-new-profile negatives, not proof of a future SHA-256 run.

Latest tests/build/pins and the fresh M1 re-check are recorded in the final exact host plan and implementation report. No source/installed state is silently declared reviewed after edits; the old ten-file v2 snapshot and report remain unchanged.

## Final M1 re-check follow-ups

[Opus's fresh re-check](TASK_48_PAYLOAD_OPUS_REREVIEW.md) verified all 19 v4 pins before/after and found **no Blocker or required bootstrap/artifact change**. It independently decoded and matched the encoded/stub/plain program, authority and six-script bundle, checked the claim/copy/hash/TEMP/failure ordering, and exercised an isolated non-elevated compiler-ACL simulation. Elevated behavior remains unproven.

Parent-only small follow-ups address M1-R1/R2 and L1/L2 without changing the reviewed bootstrap bytes: correct the trust/CWD claims above; prepare/pin the exact non-elevated caller with NonInteractive/process-only execution policy, stable literal-hash reads, RunAs/PassThru/no-Wait, native wait/exit record and protected result verification; list native Root delete/add prompts and partial outcomes in the plan. The caller's default CheckOnly mode passes with 102 candidate files, zero configured CLR override names and no enforced policy. Its exact child argv refuses non-admin execution with native exit 1 before root creation; CurrentUser RemoteSigned remains unchanged. The caller is a post-review parent addition, not falsely represented as part of Opus's reviewed v4 code. Actual RunAs capture is still unproven until approval.

The caller rejects `COR_*`, `CORECLR_*`, `COMPLUS_*` and `DOTNET_*` names in process/HKCU Environment before elevation. This is a best-effort configuration check, not evidence that the account is uncompromised or a race-proof security boundary. No variables, registry policy or trust settings were changed during checks.
