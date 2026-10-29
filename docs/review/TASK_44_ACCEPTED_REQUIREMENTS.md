# Task 44 — Accepted requirements and specification amendments

**Date:** 2026-10-29. **Branch:** `phase/5-v2-planning`. **Documentation base:** `7227d46` (Task 43 owner decisions). **Implementation base:** Task 42 `52b43130130b25a850f43f1fe478d112c3ce9cb7`.

**Outcome:** Accepted requirements are written in specification v0.3 and traceability. Documentation checks pass; the subsequent owner-authorized Task 45 project recovery closes the required build/test gate (Release build 0 warnings/errors; full suite 290/290). The owner subsequently authorized their established identity per commit invocation, resolving the commit blocker without persistent configuration changes. Task 44 is committed locally with the Task 45 documentation; A2 still awaits owner ADR decisions. The initial toolchain-block evidence below is historical and superseded by the recovery record.

## Authority and scope

The task is row 44 of the owner-accepted [Task 43 delta](../product/PC_STEWARD_2_DELTA.md) §5: record accepted requirement deltas and explicit specification amendments with retained IDs/meanings. The owner accepted that delta and answered D1–D5 in Task 43, recorded at `7227d46`. The [roadmap](../product/PC_STEWARD_2_ROADMAP.md) remains unchanged. GitHub issue #44 does not exist; the repository delta/STATUS task sequence defines this work.

Only the accepted Yes/Never requirements enter the specification. Existing requirement rows and their markers remain unchanged, no deferred feature receives a speculative ID, and the accepted text is not evidence of implementation. The retained update-only ADRs/helper still govern existing execution until the applicable future amendments are accepted and implemented. No ADR is edited, authored or accepted here; no phase brief, prompt or production change is made.

## Requirement coverage

| IDs | Count | Specification | Required future work, not Task 44 evidence |
|---|---|---|---|
| `FR-UPD-14–15` | 2 | §9.13 | Truthful observable phases and enforceable reviewed mode; applicable ADR/job/helper review, 49–50 and 53–54. G3/G4 use existing IDs in 51–52. |
| `FR-CAT-01–06` | 6 | §9.14 | Bounded WinGet search, honest metadata, fresh absence and distinct exact install authority, sequential verification, local favourites/recent installs; 45/47 and 55–59. |
| `FR-UNI-01–04` | 4 | §9.15 | One enforceable installed instance, exclusion proof, reviewed removal/elevation and selected-instance verification; 45/47 and 60–63. |
| `FR-DIA-01–05` | 5 | §9.16 | Consented bounded read-only timeline, non-causal correlation, redaction/retention, app-only deletion and requested exports; 46/47 and 64–66. |
| `FR-AI-01–06` | 6 | §9.17 | OpenAI-first optional advisory explanation, exact edited preview, protected key/egress and keyless core; 46/47 and 67–71. |
| `FR-BUD-01–04` | 4 | §9.18 | Hard app caps, transactional conservative reservations, uncertain-charge reconciliation and Never purchase/recharge; 46/47 and 68–71. |
| **Total** | **27** | No ID collisions/reassignment | No passing feature/live evidence claimed. |

The new traceability section gives every individual ID a scope marker and pending task mapping. `FR-BUD-04` is Never; the other 26 are Yes for gated 2.0 scope. They do not become MVP implementation claims. Existing `FR-ID-09` remains Later/optional, `FR-UPD-12` remains Later and `FR-POL-01` remains Partial MVP.

## Explicit amendments and retained meanings

| Specification sections / existing IDs | Accepted amendment | Boundary retained |
|---|---|---|
| Header, §§2.3/4.3/11.5 | Distinguish the MVP baseline from accepted 2.0 requirements and exact future gates. Manual install/uninstall are distinct operations. | No automatic/bulk uninstall, unattended installation, source promotion or distribution authority. The roadmap deferred list remains Later in full. |
| §§9.13/11.5; `FR-UPD-01–11`, `AC-UPD-01–11`, `FR-UI-01–07`, `FR-HIS-01–06` | G3 requires reliable provider restart evidence propagation. G4 reviews a frozen eligible batch, freshly revalidates each item, runs sequentially and retains per-item partial/cancellation outcomes. Preserve running-app warning if detected at review or before execution. | No duplicate IDs or broader process-control/skip-version/major-minor pin feature; Q2/Q3 remain open. |
| §§9.14–9.15/13.2–13.3; ADR-017/018/022 references | Require typed install/uninstall discriminators, exact fresh evidence, independent runner/helper validation and replay/substitution rejection, with protected on-demand elevation only where approved. | Existing update approval never authorizes install/removal; no fake upgrade or general command protocol. Current helper/ADRs unchanged, including ADR-003's Provisional status. |
| §§2.3/12.3/13.1/15.4/16.2; `AC-DAT-04` clarification in §19.5 | Permit only explicitly consented AI-provider egress of exactly approved edited/redacted failure/timeline evidence, and one protected current-user provider credential. Add the AI-off/denied/approved-payload/redirect network acceptance matrix. | No default upload, PC Steward account, cloud sync or remote action; source consent is not AI consent. Credentials never enter config/export/backup/logs; model output never authorizes execution. No live egress/key action here. |
| §§9.16–9.17/12.4–12.5/15.5/19.5; `FR-DAT-06–07/12–15`, `AC-DAT-06–07/12–16` | Clear scan data also deletes collected snapshots while preserving update history. Reset includes all app-owned diagnostic/AI/accounting state and exact protected credentials, with residual reporting and next-launch verification. | Separate scan/download/reset controls, stable decisions, compiled roots/credential identities and canonical/reparse safeguards remain. Never delete OS event/Reliability/Update records, installed apps, provider caches, leftovers or user exports. |
| §§9.17–9.18/11.5/12.4 | D4: 30-day local snapshots/saved redacted explanations, no saved submitted prompts; minimal accounting/reservations remain separate. D5: USD $200/month, $0.25/request and $1 total first-live-test ceiling. | Provider billing is authoritative, app ledger covers only PC Steward, unknown prices block, uncertain reservations remain held, no assumed promotional-credit balance or automatic purchase/recharge. |
| §11.5; `FR-UPD-12`, `FR-SCH-01–05`, `FR-RSC-04` | WS5 produces an accept/reject/defer ADR decision covering durable consent, integrity, exact source/ID/optional ceiling, freshness, revocation, windows, back-off, notifications and installer limits. | No automation implementation, per-run approval exception, scheduling execution reference, elevation or resident host. ADR-017/020 remain effective. |

## Owner decisions and remaining gates

D1 tags only; D2 OpenAI first; D3 `7zip.7zip` as a later controlled candidate only; D4/D5 as above. No already relied-on installation becomes disposable, and each install and uninstall still needs fresh exact action-time approval. No current package version/scope, host diagnostic access, SDK/model/endpoint/pricing or provider credit balance is asserted.

Q1 search/diagnostic/transport bounds; Q2 skip semantics; Q3 running-app coverage; Q4 enforceable uninstall targeting/exclusion evidence and viable fixture; Q5 favourites/recent-install and new operation-history lifecycle; Q6 correlation/time rules; Q7 accounting boundaries/pricing/retries/reconciliation; Q8 credential adapter/deletion; Q9 exact-artifact host authority all remain at their relevant gates. Q10's accepted requirement wording is recorded; it does not accept future operation/security ADRs or decide WS5. No arbitrary numeric defaults or schema choices are invented.

Next numbered work is Task 45 Proposed operation/job/helper and WS5 ADR drafting after Task 44 finalization; Task 46 owns diagnostic/AI/budget ADRs, and Task 47 waits for applicable owner ADR acceptance. No feature implementation or host action follows from this report. Existing Phase 4/`FRA-14–16` distribution gates remain open.

## Verification and finalization boundary

Static checks compare all baseline specification requirement/acceptance rows and original traceability rows against `7227d46`, verify the 27 accepted delta IDs appear once in both new requirement tables, confirm accepted Yes/Never markers, check D1–D5 values and remaining gates, validate relative file links and review the full diff for scope and accidental implementation/ADR changes. Final static results: **PASS** — 195 specification and 173 traceability baseline requirement/acceptance rows preserved verbatim; 27 unique accepted IDs in each new table, zero collisions, 26 gated Yes and one Never; full new acceptance text matches the accepted delta apart from the documented D1/D4/D5 wording updates; all D1–D5 values and Q1–Q10 gates present; 21 relative Markdown file links resolve; no malformed Unicode/new trailing whitespace; `git diff --check` passes. Scope is exactly the five Markdown files listed in the failure record, with code, ADRs, roadmap/delta, phase briefs, scripts, tests and Git index unchanged. Full diff and new documents were reviewed for scope, secrets, unsafe path/command authority and unsupported implementation claims.

The canonical `pwsh -NoProfile -File .\scripts\Build.ps1` cannot start: `pwsh` is not available. The installed Windows PowerShell fallback invokes the same build and test scripts, but each exits `-2147450735` before compilation/test discovery: no .NET SDK is installed, while `global.json` requests 10.0.302. No product test ran and no build passed. The [failure record](../failures/task-44.md) preserves commands, exact errors and the smallest unblock. No packages/software are installed and no global/toolchain configuration changes are attempted.

Task 43's explicit build/test exemption was specific to that earlier task; no Task 44 exemption was inferred. Product tests are not added for documentation-only edits. The initial blocked state is superseded by the owner-authorized SDK/project recovery described next.

## Subsequent validation closure

The owner authorized SDK 10.0.403 selection and restoration of the missing persistence project. [Task 45 project recovery](TASK_45_PROJECT_RESTORE.md) records exact-blob restoration, the narrow source-directory ignore exception, 18/18 restored projects, SQLite 19/19, Architecture 28/28, Release build 0 warnings/errors and full suite **290/290**. The unchanged scripts ran under installed Windows PowerShell with the user-local SDK; no gate exemption or PowerShell installation was needed. This satisfies Task 44's required build/test evidence without changing its accepted specification text. Git initially refused committing because author identity was unset. The owner then explicitly authorized their established identity; per-invocation author/committer values enabled local commits without persistent Git configuration changes. Build recovery is `659b58f`; accepted requirements and Proposed ADR/completion records are committed separately. Task 45 ADRs remain Proposed, and no feature/deployment acceptance follows.
