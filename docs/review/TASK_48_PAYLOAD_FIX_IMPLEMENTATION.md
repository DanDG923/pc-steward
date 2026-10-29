# Task 48 — Accepted payload fix implementation and evidence

Date: 2026-10-29. Requirements: `FR-DAT-12–15`, `AC-DAT-18`, `FRA-14–16`, retained `TM-04`.

**Current result: local implementation/automated gates and fresh M1 bootstrap review complete; parent caller/doc conditions addressed; ready to request exact host approval. No removal/redeploy/launch has occurred.** Owner acceptance **“I also accept the changes”** applies to the [concrete ADR-022 amendment](TASK_48_PINNED_PAYLOAD_FIX_PROPOSAL.md). [Final readiness](TASK_48_FINAL_HOST_READINESS.md) and the [exact plan](TASK_48_EXACT_REDEPLOY_PLAN.md) supersede earlier pending-review passages. The [original failed host run](TASK_48_OWNER_ONLY_REDEPLOY.md) remains separately reported.

## Current review-closure state

The [original pinned review](TASK_48_PAYLOAD_OPUS_REVIEW.md) is preserved. The [parent response and remedies](TASK_48_PAYLOAD_REVIEW_CLOSURE.md) addresses M1 and L1–L5: protected, hash-verified scripts/immutable encoded bootstrap/private compiler TEMP with OWNER RIGHTS; held signed hashes bound to ledger; fresh signer only; staging cleanup before completion; strict default profile with explicit legacy opt-in; duplicate JSON/type/provenance/order fixtures. Expanded tests pass **85 assertions**, focused **1/1**, canonical Release **0 warnings/errors**, full **291/291**, zero skipped. The [exact later host plan](TASK_48_EXACT_REDEPLOY_PLAN.md) is prepared but unapproved. Its exact encoded command rejects non-elevated execution in a real negative smoke test; no elevated bootstrap ran. Earlier 65-assertion/snapshot/proposed-default passages below are historical and superseded by this current record.

## Change and technical dialogue

The installer consumes required pinned `CandidateRoot`, `CandidateManifestPath` and `CandidateManifestSha256` inputs. All installer-time `dotnet build/publish` calls are removed. It validates a complete bounded manifest/tree, copies into protected staging, creates descendants/manifest/ledger with admin-owned ACLs at creation, and rechecks staged byte identity before certificate-store mutation. Existing fixed roots, SID, mutex, collision refusal, certificate journal, transaction-owned rollback and separate guarded removal remain.

The shared helper rejects non-fixed/non-NTFS/UNC sources, reparse paths/ancestors, aliases/traversal/ADS/reserved names, hidden/empty extras, missing/changed files, malformed sizes/hashes and case/file-directory collisions. Stable resolved-file read handles deny concurrent write/delete. Default streams only are copied. The staged full hashes, not source-side checks, establish accepted signing contents. Normal non-elevated copy methods serve synthetic fixtures/materialization; production calls `CopyTreeProtected` with atomic ACL creation. Protected creation/actual ACL policy still requires the later approved elevated-host integration run; synthetic tests are not claimed as that evidence.

Open technical dialogue with Opus refined the design: reconstruct exact unsigned bytes and compare the manifest's raw SHA-256, recording only the original checksum; require standard FileAlignment-aligned PE32+ x64 sections, no unsigned overlay, exact certificate offset/EOF and one <=64 KiB certificate record. A separate native SIP digest is unnecessary because full original-byte reconstruction plus Windows Valid supplies the required guarantees. CMS checks enforce definite minimal DER lengths, one certificate/signer, no CRLs/unsigned/countersigner attributes, RSA and SHA-256 in both digest positions, and a closed signed-attribute list. The Windows signature must be **Valid, embedded Authenticode, same signer**, checked while a stable no-write/no-delete handle remains open. CMS cryptography never substitutes for Windows file/trust/time policy.

Read-only inspection found the old PowerShell signing call used SHA-1 by default despite a SHA-256 certificate. New signing explicitly requests `-HashAlgorithm SHA256`; new temporary certificates explicitly request RSA 2048/SHA-256. The strict profile runs on signed protected staging **before** files reach the install root, then again through provenance validation on installed files. No timestamp is requested or allowed, so validation fails closed after the temporary signer expires. Validation must run under the installing SID and its CurrentUser trust stores.

The completed v6 ledger gains `payloadOrigin=pinned-unsigned-v1`, the raw approved manifest hash/protected manifest and two executable original checksums/lengths. The shared validator preserves explicit `legacy-v6` behavior and rejects incomplete new-route provenance. `-RequirePinnedPayload` prevents a legacy Pass being counted as acceptance of a new deployment. The existing remover is unchanged and remains separately approved.

`Wait-OwnerOnlyDeploymentProcess` retains an authorized child handle and reads its actual kernel exit code after a bounded wait; null never means success, and it never starts/kills/retries a process. The later deployment caller will consume it. Tests prove actual isolated child exits 0/7; elevated caller integration remains a later host check.

## Verification and limits

| Evidence / command | Actual result |
|---|---|
| Initial focused test before helper | Expected RED assertion: pinned payload integrity interface unavailable |
| Added native-exit behavior before helper | Expected RED assertion: native exit-code capture interface unavailable |
| ADS negative before stream rejection | Expected RED assertion: alternate data stream rejected |
| Direct PowerShell fixture | **65 assertions PASS**, real NTFS junction and alternate-stream fixtures; no installed-root/certificate-store mutation |
| Focused normal MSTest `PayloadDeploymentIntegrityTests` | **1/1 PASS**; executes the actual script helper/fixtures under Windows PowerShell |
| `powershell.exe -NoProfile -File scripts/Build.ps1` | **Exit 0; Release; 0 warnings/errors** |
| `powershell.exe -NoProfile -File scripts/Test.ps1` | **Exit 0; 291/291, zero skipped**: Domain 59, Application 181, Architecture 28, Security 23 |
| Actual old apphosts, read-only | Both reconstruct to their original pinned raw hashes; Windows Valid remains available under a held read handle |
| Actual old signature strict-profile check | SHA-1 signatures rejected by the new profile, as expected; no new native signing run claimed |
| Updated normal installed validator | **Exit 0, `payloadOrigin=legacy-v6`**, preserving original protection checks |
| Same validator `-RequirePinnedPayload` | **Exit 1**, “The installed deployment is legacy; pinned-payload evidence is required.” |
| Diff/parser/UTF-8/source boundary | Script parsing and whitespace pass; no `src/` product runtime changes |

The synthetic CMS positive/negative fixtures use throwaway in-memory RSA certificates and real CMS cryptography; **only the Windows trust-query result is mocked**. They do not sign an executable with Windows or touch certificate stores. The Framework countersign API could not use that ephemeral CNG key on this host; a countersignature attribute negative exercises the profile's unconditional unsigned-attribute rejection instead. No claim of a cryptographically valid native countersignature fixture is made. A real staged SHA-256 signing/Windows trust run and elevated atomic ACL creation are not tested before separate host approval.

Logs: ignored `artifacts/task-48-deployment/payload-red.log`, `payload-exit-red.log`, `payload-dialogue-red.log`, `payload-final-fixtures.log`, `final-focused.log`, `final-build.log`, `final-tests.log`, `legacy-validator-after-fix.log`, `required-profile-negative.log`. Build host: native Windows x64, Windows PowerShell 5.1, approved SDK 10.0.403 on command-only PATH; no SDK/dependency installation or persistent configuration change.

## Pinned candidate outside OneDrive

The original candidate's OneDrive files have reparse attributes and are correctly rejected by the strict elevated importer. A non-elevated copy of the same approved bytes was made and verified; no elevated cloud-reparse exception was introduced. Its initial directory was renamed to avoid sharing the app data-root prefix. Final location:

- Root: `C:\Users\swigg\AppData\Local\OwnerOnlyDeploymentCandidates\task-48-43F209AD1E79`
- Payload: `payload\` under that root; **102 exact files**, same original manifest/content hashes, no reparse paths or alternate streams.
- Manifest: `unsigned-candidate-manifest.json`, SHA-256 `43F209AD1E79E5B2A251F29D723059F419789E44FF72671D68D5C7C7A0B3B7AE`.

This user-writable local copy is a policy/availability convenience, not a trust boundary. Its bytes must still match inside protected staging. PC Steward never manages/deletes this folder; removing the prepared candidate is a separate owner action. Earlier source hash reads/materialization may have invoked OneDrive's non-elevated file filter; network-idle/recall behavior was not measured and is not claimed. No future approved elevated read will use the cloud source.

## Independent review snapshot and pending host boundary

HEAD remains `cd6d4530ac4e8c1e31a07b1c9096ffd358e75342`; pre-existing Task 46/47 edits are retained. The exact ten-file review snapshot is `artifacts/task-48-deployment/implementation-review-pins-v2.json`, SHA-256 **`5A19C8F82B328BEC4B948C6D921DB5C7FB8C5C496C18BEDF3DF314BDDA249202`**. It includes installer, validator, both new helpers, the MSTest and its two fixture files, ADR-022, accepted design and implementation plan. The original guarded remover has not changed.

Fresh independent Opus review must verify those pins before/after and distinguish demonstrated issues from unproven host integration. No exact new host plan is approved. The retained installation/signer and absent `%LOCALAPPDATA%\PCSteward` state remain as in the failed run. Any later plan needs exact guarded removal of that deployment/signer, one installation of the reviewed candidate/script snapshot, mandatory `-RequirePinnedPayload` validation and separate launch approval. No Task 49, public distribution, source refresh or app update follows.

No commit: Git authorship remains unconfigured and no Git identity/configuration has changed.
