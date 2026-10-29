# Task 47 — Phase briefs and numbered prompts

**Date:** 2026-10-29. **Branch:** phase/5-v2-planning. **Baseline HEAD:** cd6d4530ac4e8c1e31a07b1c9096ffd358e75342.

**Outcome: Task 47 and Phase A2 complete.** The owner explicitly accepted the corrected ADR-023–025, ADR-026 C (Defer) and all disclosed final choices on 2026-10-29 after both requested Opus 5.5 reviews. Final small author follow-ups are disclosed below and were not independently re-reviewed. Task 47 is accepted delta §5 row 47; completing it produces no future implementation or host-action authority.

## Final owner acceptance — current authority

The final acceptance question explicitly covered corrected ADR-023–025 and ADR-026 C, one frozen review permitting dispatch for 30 minutes, separate two-minute helper-request freshness, new install/uninstall 10-minute deadline plus up to five seconds teardown, and one exclusive gate across current-user sessions. It disclosed that Opus closed original findings and final small author clarifications resolve the remaining batch-expiry point. The owner answered **“Accept the corrected ADRs and final choices”**. All four ADR status/acceptance records, decision policies, phase briefs, prompt index/headers, STATUS and TRACEABILITY now reflect that decision. Earlier pending/proposal statements and reviewer responses below are historical and superseded only as to owner acceptance; their snapshot/review limitations remain accurate.

ADR-023–025 are Accepted; ADR-026 is Accepted — Option C (Defer). FR-UPD-12 stays Later and ScheduledHost scan-only. Q4 actual targeting/exclusion/disposable-fixture proof and all implementation/security/resource/migration/host-action gates remain. Task 48 is next numerically but needs its separate exact artifact/deployment/launch authority; Task 49 needs its own implementation instruction. No follow-on was started.

## Owner authority and choices

The owner requested “Complete task 47”. When asked about the Task 45 decisions, the owner instructed “Check them over with Opus 5.5 first”. The owner requested concrete defaults, then answered **“Accept these defaults”** to the catalogue/progress/warning/favourites/history policy in [planning decisions](../requirements/V2_PLANNING_DECISIONS.md), subject to corrections from review. No ADR-023–026 acceptance is inferred. ADR-027–029 were accepted separately in Task 46 and their existing working-tree changes are preserved.

## Deliverables and coverage

Eight phase briefs A2/B/C/D/E/F/G/H, shared component/execution/evidence contract, exact-host plans and accepted-default decision record. Twenty-five numbered prompts 48–72 follow the existing prompt layout and accepted model allocation. Index/playbook/copy-paste entry points link them without duplicating instructions. STATUS/TRACEABILITY record planning rather than feature evidence. All source/dependencies/tests and existing Phase 0–4 scope remain unchanged.

| Requirements | Implementation | Readiness / independent gate | Separate live evidence |
|---|---|---|---|
| FR-UPD-14/15 and retained FR-UPD-01–11, AC-UPD-01–11, UI/history | 49–52 | 53 / 54 | Real update/deployment only with separate authority |
| FR-CAT-01–06 | 55–56 | 57 / 58 | 59, exact route only |
| FR-UNI-01–04 | Q4 route proof then 60 | 61 / 62 | 63, selected absence/other-instance preservation |
| FR-DIA-01–05 | 64 | 65 / 66 | Approved read-only host coverage; no OS mutation |
| FR-AI-01–06; FR-BUD-01–04 | 67–68, mocks first | 69 / 70 | 71, exact approved call/cumulative $1 |
| Data/lifecycle/privacy/security/migration/performance | Shared contract and each owning implementation | Every phase readiness/gate | No mocks substituted for host claims |
| FR-UPD-12 / scheduling and full deferred/prohibited list | Deferral/decision only | ADR-026; 53/54; 72 | None authorized |
| Owner-only replacement/distribution boundary | 48 independent WS1 exception | Exact validator/artifact/database evidence | Separate launch; FRA-14–16 stays open |

## Requested Opus 5.5 review

Reviewer provider/model: Claude / claude-opus-5-5, effort high, shared working tree, read-only, no delegates. Initial four ADR hashes were verified by the reviewer before/after and independently by the parent before edits. No code/build/test/host/network/credential/external-post action was authorized in the review. Parent drafts appeared concurrently and were explicitly excluded from its reviewed snapshot.

Initial review: **changes needed** for ADR-023/024/025; ADR-026 recommended accept Defer. Three High findings (helper responsibility, durable dispatch, elevation guarantee), six Medium findings (absence, dependencies/agreements, typed safety options, interactive scope, install substitution, self/provider dependencies), and seven lower-priority notes including the ADR-026 wording. Parent corrections below are proposed contracts until owner acceptance; no implementation is claimed.

| Finding | Proposed correction |
|---|---|
| H1 | Explicit desktop-intent versus helper-fresh-safety responsibility matrix per operation; no SQLite/helper consent claim; new Update validation distinguished from unchanged old deployment. |
| H2 | Durable unique DispatchClaimed before provider/helper-UAC; claimed-without-final Uncertain; no replay and async-safe shared gate. |
| H3 | Remove prevention guarantee for third-party UAC; no app elevation/fallback, route proof, warning and explicit drift Unsafe/Unknown. |
| M1 | Exact provider absence plus all plausible linked/unlinked matches and enumerated view/user coverage; missing cross-user proof blocks affected route. |
| M2 | No newly installed/changed dependencies in v1; unknown blocks; agreements identity/terms/explicit acceptance bound to review. |
| M3 | Closed supported option set with force/bypass/override/custom location/args/hash/unknown-version/dependency-ignore exclusions. |
| M4 | Interactive only with evidence preventing reviewed scope/target/effects widening; post-drift detection alone insufficient. |
| M5 | Tested no-upgrade/no-repair invocation guard; no supported guard means unavailable route. |
| M6 | Exact self/provider/runtime deny-set plus complete dependency classification; unknown blocks. |
| L1–L7 / ADR-026 | MSIX app-data impact, one protocol/pair, two-minute per-item freshness/no auto-resume, shared lease/FR-RSC-02, Q5 self-contained history/copy lifecycle/pending verification/update-only AI scope, declared publisher, threat IDs and no signed-out/S4U/password automation. |

### Initial reviewer response (historical, before parent corrections)

# Review of the Task 45 ADRs (023–026)

**Verdict:** ADR-026 can be accepted as "Defer". ADR-023, 024 and 025 need edits before acceptance. The direction is sound, but five places at the privilege boundary and in recovery are ambiguous or claim more than the design can do, and the owner would be freezing them. I have not accepted anything on the owner's behalf. I made no edits or commits and ran no code, builds, tests, host checks or network calls.

**Hashes:** all four were checked before and after the review and are unchanged at HEAD `cd6d4530…`: ADR-023 `275FC98B…3370`, ADR-024 `F5D42064…C5C5`, ADR-025 `B4983CD1…B88E`, ADR-026 `D3744CCA…E758`. I read ADR-027–029 from the working tree: `19FB7826…E711`, `A5A6F1AB…27AA`, `B1DF4BBE…39EF`. A new untracked file, `docs/requirements/V2_PLANNING_DECISIONS.md`, appeared while I was reviewing. It is the parent's Task 47 work and I did not review it. Reviewer: Claude Opus 5.5 (`claude-opus-5-5`).

## Must fix before acceptance

**H1 — The ADRs never say which checks the elevated helper does itself.**
- **Where:** ADR-025:55 ("independent fresh validation of source consent…"), ADR-024:50 ("both the runner and any helper … revalidate … other-instance baseline, exclusions"), ADR-023:47.
- **Today:** the helper only does strict protocol, allowlist, digest and Chocolatey-client checks (`WindowsElevatedHelperTransport.cs:70–230`). The check against current evidence runs on the desktop side (`ElevatedUpdateRunner.cs:61–68`).
- **Problem:** consent is stored in SQLite, which the helper must never open. The baseline comes from the desktop.
- **What goes wrong:** an implementer either sends consent or baseline as request fields, which is the desktop-supplied assertion ADR-025:51 forbids. Or they apply "missing fresh validation … blocks the route", which also blocks protocol-2 machine updates and regresses them.
- **Fix:** add a per-operation responsibility table.
  - Desktop only: review binding, consent, approval age.
  - Helper re-resolves itself: trusted catalogue, exact ID/version/scope/architecture, absence (Install), and the instance discriminator plus the current instance set (Uninstall).
  - The baseline in the request is data the helper compares against, never proof.
  - State whether protocol-2 Update gains helper-side checks or keeps today's behaviour.

**H2 — No durable record is written before launch.**
- **Where:** ADR-025:65, ADR-023:49, ADR-024:52.
- **What goes wrong:** a user-scope install launches, then the desktop crashes before any event is saved. After restart the job looks approved but never started. ADR-025:47 stops it being resumed, but history now says "not started" while the app is actually installed. That breaks recent installs and the next absence check.
- **Fix:** commit a `DispatchClaimed` state change under the operation lease before launching any provider, or before UAC on the helper path. On restart, any job that was claimed but has no final result becomes Uncertain and gets a targeted inventory check. This mirrors ADR-029:69/77.

**H3 — "Unexpected elevation is blocked" cannot be guaranteed.**
- **Where:** ADR-023:47, and the same gap on ADR-024's WinGet user-scope route.
- **Problem:** a vendor installer launched without elevation can raise its own UAC prompt. If the user clicks Yes, a machine-scope install happens. PC Steward cannot prevent this, and the outcome is currently undefined.
- **Fix:** say PC Steward never elevates for the item and discloses this risk at review. If post-inventory finds the app in a scope that wasn't reviewed, report an explicit scope-mismatch/unsafe result, never Installed, and keep it out of recent installs.

**M1 — "Absence" is not defined.**
- **Where:** ADR-023:41, ADR-024:56.
- **Problem:** an exact-ID query misses copies WinGet has not linked to its package ID, such as 7-Zip installed from the vendor's MSI, or an x86/x64 pair. The phrase "available to the supported route" quietly narrows "complete": other Windows users' per-user installs can't be seen, and a machine install affects them too.
- **Fix:** absence means the exact-ID query returns zero **and** PC Steward's identity matching finds no possible match at any confidence. Any possible match counts as ambiguous and blocks. List which views are covered, and disclose (or block on) other user profiles. This is the owner's choice.

**M2 — Dependencies and licence agreements are unbound.**
- **Where:** ADR-023:42.
- **Problem:** WinGet installs the dependencies listed in a package's manifest (for example a VC++ redistributable, possibly machine-scope). These are not in the reviewed payload and are not verified afterwards.
- **Fix for dependencies:** allow an install only if the dependency set is known and empty, or if each dependency is reviewed and verified as its own item. An unknown set blocks.
- **Fix for agreements:** bind the identity of the agreements shown to the user, and their explicit acceptance, into the payload. Never set acceptance implicitly.

**M3 — Typed provider options can bypass safety.**
- **Where:** ADR-025:45, ADR-024:50.
- **Problem:** "No free-form flags" does not exclude typed options such as hash-mismatch allowance, force, override/custom arguments, install location, upgrading to an unknown version, policy bypass, or Chocolatey `--force`.
- **Fix:** list a fixed set of provider options per operation. Overrides and force options are permanently absent.

**M4 — Interactive mode defeats scope enforcement.**
- **Where:** ADR-025:37, ADR-023:41–42, FR-UPD-15.
- **Problem:** an interactive installer UI lets the user choose "all users", a different install location, or components.
- **Fix:** offer interactive mode for Install/Uninstall only where drift can be detected afterwards, and treat drift as a scope mismatch. Otherwise allow only silent mode wherever scope enforcement is claimed.

**M5 — An install can still turn into an upgrade or repair.**
- **Where:** ADR-023:47, ADR-025:42.
- **Problem:** something can be installed between the absence check and the provider call, and WinGet's install command can then upgrade or repair the existing copy.
- **Fix:** require the provider's no-upgrade behaviour where it exists (implementation-time evidence). Otherwise disclose the window and report an instance that existed beforehand as Unsafe or Unknown.

**M6 — PC Steward can remove itself or its own dependencies.**
- **Where:** ADR-024:38–42.
- **Problem:** nothing excludes PC Steward itself or App Installer (`Microsoft.DesktopAppInstaller`), which is a removable current-user MSIX that hosts WinGet COM. Removing it breaks every update, install and post-removal check. The same applies to the Windows App SDK runtime the desktop uses and the Chocolatey client.
- **Fix:** a fixed, compiled deny-list of these. Unknown cases block.

## Lower priority (can go into Task 47 or Q5 notes)

- **L1 (ADR-024:21/48):** removing a current-user MSIX deletes that package's app data. The review screen must say so.
- **L2 (ADR-025:53):** each desktop/helper pair should accept exactly one protocol version, pinned by the ADR-022 manifest. The new helper should not also accept protocol 1.
- **L3 (ADR-025:47):** extend "never auto-resume" from migration to any crash, exit or close. Add a Q1 limit on how old an approval can be in a long sequential queue.
- **L4 (ADR-025:65):** name the same cross-process data-operation lease and lock ordering as ADR-029:69, which notes the reset mutex is thread-bound and can't be held across `await`. Also cover the FR-RSC-02 scan-overlap rule.
- **L5 (Q5 inputs, ADR-023:55 / ADR-024:58):**
  - History rows should be self-contained with no foreign keys to scan rows, as ADR-027:80 already does.
  - Decide whether Clear update history also clears install/uninstall history, and whether R1's "keep diagnostic snapshot copies" applies (ADR-027:94).
  - "Recent installs" should mean "installed by PC Steward", not "installed now".
  - Define how a restart-pending install gets verified later.
  - The `FailedUpdate` explanation type does not cover failed installs or removals.
- **L6 (ADR-023:38):** label the publisher as declared by the catalogue, not verified, because look-alike package IDs exist.
- **L7 (ADR-023:9):** add TM-02, TM-04 and TM-13 to the related requirements.
- **ADR-026 nits:** add "no signed-out, S4U or password-based task" to the line-46 row, since §11.5 does not promote signed-out automation. The owner should know that option A can't really be chosen from this ADR; it only supports Defer or Reject. That is honest, not a defect.

## Not defects

These are capability evidence the ADRs deliberately leave for implementation time:
- Q4: whether WinGet COM can target a single instance for removal, and how to classify provisioned or dependent MSIX packages without elevation.
- Whether WinGet COM can enforce scope, and how it behaves when activated from an elevated process.
- Q1 numeric limits, Q3 detection coverage, and the real-machine evidence needed for UAC, scope and mode.

## Validation and completion gate

Fresh package checks pass: 46 Task 47/related Markdown documents decode strictly as UTF-8; all 306 checked relative links resolve; 25 unique prompts cover 48–72 with required entry/deliverable/file/test/exit/shared-contract fields and correct model settings; eight phase briefs exist; all 27 new accepted requirement IDs remain in the authoritative spec. New-file trailing-whitespace checks and git diff --check pass. The 200 uniquely keyed FR/AC/NFR traceability rows match HEAD except the two pre-existing Task 46 FR-AI-04/FR-BUD-01 owner amendments; no new requirement IDs or other changed row appears. Original rows outside that regex are preserved by the scoped insertion/diff review. Source/dependency/script/test/packaging/CI/SDK file diff is empty. New documents and tracked diff were reviewed for unsupported authority, secret sinks, scope creep, deletion/command authority and missing evidence gates.

Required canonical scripts pass on the normal Windows host using Windows PowerShell (PowerShell 7 absent) and the already approved `%LOCALAPPDATA%\Microsoft\dotnet` SDK 10.0.403 on this command's PATH/DOTNET_ROOT only. Commands: `& .\scripts\Build.ps1`, then `& .\scripts\Test.ps1`, unchanged scripts. Release build: exit 0, 0 warnings, 0 errors. Full suite: exit 0, **290/290 passed, zero skipped** — Domain 59, Application 181, Architecture 28, Security 22. Logs: `%TEMP%\pc-steward-task47-build.log` and `%TEMP%\pc-steward-task47-test.log`. Existing tests validate existing code, not future 2.0 features. No restore/dependency installation or product test change was needed.

Fresh Opus re-check is complete as recorded below; owner ADR acceptance remains pending. Git author identity remains unconfigured as inspected; no configuration change or inferred author identity is allowed. No commit yet. No push, deployment/launch/source refresh/package action, OS-record read, key action, schedule/data deletion, paid call or distribution occurred.

## Fresh Opus 5.5 re-check — completed

Fresh read-only delegate using Claude Opus 5.5 / high verified all six pinned target hashes before/after. The parent independently verified the primary four ADR hashes after the re-check before author follow-ups. The review closed the original H1–H3, M1–M6, L1–L7 and ADR-026 sign-in findings, with no remaining Blocker/High. It found N1 Medium (two-minute helper freshness had been incorrectly widened to waiting batch review age), plus N2–N6 Low clarifications. No reviewer accepted ADRs on the owner's behalf.

### Final author follow-ups for owner acceptance

- N1: propose one frozen update/install batch review with **30-minute review-to-item-dispatch ceiling**, fresh checks for each item and separate unchanged **two-minute just-in-time helper request**. Running work may finish within its separate deadline; unstarted expired items need new review. Single-target removal has no queue. New clock/wait/4-minute-first-item boundary fixtures assigned to Tasks 52/56/60.
- N2: read-only verification after an uncertain claim establishes installed state only. Verified PC Steward attribution/Recent installs also needs durable job-bound provider/result evidence; otherwise explicitly unknown attribution.
- N3: common **exclusive** current-user gate across processes/logon sessions, explicit compatibility/wait/expiry table, proposed **10-minute new install/uninstall item deadline plus at most 5-second bounded teardown attempt**, durable conflicting-work barrier if residual work remains unknown, namespace/ACL/async/abandonment proof. Readonly UI remains usable; existing Update deadlines unchanged. No service/reset-surviving marker.
- N4/N5/N6: align Q1/Q2/Q3/Q5 accepted-policy references, qualify helper checks for helper routes only, require deterministic versioned plausible-match predicate/fixtures before Task 56 route support.

These are small author follow-ups after the re-check, **not independently re-reviewed**, and are disclosed for the owner's final acceptance. No third delegate was started. The new 30-minute review/common-gate/new-operation deadline policies are proposed final choices, not silently attributed to the earlier accepted defaults. All ADR statuses remain Proposed until the owner accepts them. The reviewer recommended acceptance after resolving N1 by wording or explicit owner choice, with low points fixable as author edits/implementation conditions.

### Fresh reviewer response (before final author follow-ups)

# Opus 5.5 re-check of ADR-023–026 against the corrected working tree

**Verdict:** I found no remaining Blocker or High issue. All 6 High/Medium findings from the first review (H1–H3, M1–M6), all 7 Low findings (L1–L7) and the ADR-026 sign-in point are closed in the corrected text. One new Medium issue needs a small wording fix or an explicit owner choice before ADR-023–025 are accepted. ADR-026 can be accepted now as Option C (Defer). Acceptance is the owner's decision.

**Model:** Opus 5.5 (`claude-opus-5-5`).

**Hashes:** I checked HEAD `cd6d4530ac4e8c1e31a07b1c9096ffd358e75342` and the six pinned files before and after the review. All matched:

| File | SHA-256 |
|---|---|
| ADR-023 | `DFAACC06…63A9` ✓ |
| ADR-024 | `F43E1AE1…63CF0` ✓ |
| ADR-025 | `6806D83A…5C5EB` ✓ |
| ADR-026 | `EE4122BC…16036` ✓ |
| V2_PLANNING_DECISIONS | `18D4F1C7…B425` ✓ |
| V2_TASK_CONTRACT | `678F17FD…DC53DE6` ✓ |

I made no edits and ran no builds, tests, network calls or host actions.

## Closure of earlier findings

| ID | Status | Where it is closed |
|---|---|---|
| H1 helper responsibilities and consent | **Closed** | ADR-025:59–67 now has a table splitting desktop and helper duties. The desktop owns user consent; the helper re-resolves the evidence itself and treats the request's baseline as untrusted. ADR-023:47 and ADR-024:52 point to that table. |
| H2 durable prelaunch dispatch claim | **Closed** | ADR-025:81–87 requires a unique `DispatchClaimed` record, committed under the cross-process lock before launch. Claimed-without-final becomes Uncertain and is never replayed. Repeated in ADR-023:51 and ADR-024:56. |
| H3 false claim of preventing unexpected elevation | **Closed** | ADR-023:47, ADR-024:52 and ADR-025:49 now say a third-party installer's own UAC prompt cannot be prevented, that this must be disclosed, and that resulting drift is Unsafe/Unknown. |
| M1 incomplete absence check and views | **Closed** | ADR-023:40 lists the views (HKLM 64/32-bit, HKCU, current-user MSIX, WinGet), treats any possible match as ambiguous, and blocks when cross-user proof is unavailable. ADR-025:65 adds the helper's equivalent. |
| M2 dependencies and agreements not bound | **Closed** | ADR-023:41 and ADR-025:47: dependencies must be empty or already satisfied; the agreement version or digest and explicit acceptance are bound to the payload; changed agreements need new review. |
| M3 typed unsafe flags | **Closed** | ADR-025:47: force, hash/policy bypass, custom location and dependency-ignore cannot be expressed, including typed and Chocolatey equivalents. |
| M4 interactive scope drift | **Closed** | ADR-025:49: interactive mode only with prevention evidence; detecting drift afterwards is not enough. |
| M5 install→upgrade race | **Closed** | ADR-023:47: the route needs a documented, tested no-upgrade/no-repair guard, otherwise it stays unavailable. |
| M6 removing PC Steward, its provider or its runtime | **Closed** | ADR-024:46 and ADR-025:66: a compiled exact-identity deny-set; unknown classification blocks. |
| L1 MSIX app-data warning | Closed | ADR-024:52 |
| L2 exactly one protocol per desktop/helper pair | Closed | ADR-025:57 and :69 |
| L3 approval expiry, no auto-resume | Closed, but see N1 | ADR-025:51, ADR-024:56 |
| L4 async-safe shared lock and scan overlap | Closed, refinements in N3 | ADR-025:87 |
| L5 Q5 history, captured copies, restart verification, update-only FailedUpdate explanations | Closed, caveat in N2 | ADR-023:51 and :57, ADR-024:62 |
| L6 declared publisher | Closed | ADR-023:38 |
| L7 threat-model references | Closed | ADR-023:9 cites TM-02/04/13; the IDs exist in `THREAT_MODEL.md` |
| ADR-026 signed-out/S4U/password tasks | Closed | ADR-026:37 |

## Remaining findings (most important first)

**N1 — Medium: the two-minute approval rule breaks Update All and install queues.**
- **Where:** ADR-025:51, ADR-023:49, ADR-024:56, V2_PLANNING_DECISIONS:16, spec line 593 (G4).
- **The problem:** The owner accepted "retain the existing two-minute approval lifetime". In current code that limit only applies when the elevated helper admits a request (`ElevatedHelperProtocol.cs:14,201`); unelevated batch items have no age limit. ADR-025 widens it to every operation, measured "from its explicit per-item confirmation to dispatch", with no renewal in a queue.
- **Scenario:** A user reviews Update All for 6 apps. The first installer takes 4 minutes, so items 2–6 expire and each needs a fresh review. The same happens to an install queue, or when a user takes more than two minutes to close an app after the in-use warning. G4 promises one review of a frozen batch. Nothing becomes unsafe (it fails closed), but this changes existing unelevated batch behaviour beyond what the owner accepted, and it makes G4 and the FR-CAT-05 install queue largely non-functional.
- **Fix:** State the batch semantics explicitly as an owner choice. Either:
  - (a) a frozen batch review, plus a just-in-time per-item "Start" confirmation that opens that item's two-minute window after fresh revalidation; or
  - (b) keep the two-minute limit only for the helper request lifetime (unchanged) and set a separate owner-accepted maximum lifetime for the batch review.

  Then align ADR-024:56: uninstall is single-target, so "queued approvals / long queue" doesn't apply there.

**N2 — Low: reconciled-uncertain installs overstate who installed them.**
- **Where:** ADR-023:51 and V2_PLANNING_DECISIONS:34.
- **The problem:** A claimed-without-final item may never have actually launched. A later read-only check that finds the package installed (for example, the user installed it by hand) would still turn it into "verified installed **by PC Steward**" and add it to Recent installs.
- **Fix:** Reconciliation can verify the installed state, but the item should only count as a PC Steward install, or appear in Recent installs, when provider or result evidence links it to this job. Otherwise label it "installed; PC Steward attribution unknown".

**N3 — Low: the shared lock (gate) is underspecified.**
- **Where:** ADR-025:83 and :87.
- **Gaps:**
  - It doesn't define which operations can share the lock and which exclude each other (scans, AI, clear/reset, operations).
  - It doesn't say what happens when a ScheduledHost scan holds the lock and the item's approval runs out while waiting.
  - It has no installer timeout bound for install/uninstall, and doesn't say whether the lock stays held when a timed-out process tree is still alive.
- **Note:** "Shared" is ambiguous (shared lock vs common lock). The existing `Local\PCSteward.DataOperation.v1` (`FactoryReset.cs:13`) is scoped to one logon session, not to the user across sessions.
- **Fix:** Add a short table of which operations can run together and their wait/expiry behaviour. Say that the lock stays held until the process tree exits or a bounded teardown finishes. Have the implementation verify the lock's namespace scope.

**N4 — Low: some "open question" references are stale.**
- **Where:** ADR-023:11 and :39 ("Q1 must fix…"), ADR-024:11, ADR-025:11, :77 ("Q2 … stays open") and :79 ("Q3 must define…").
- **The problem:** Q1, Q2/Q3 and Q5 are now owner-accepted in V2_PLANNING_DECISIONS. Only Q4, Q9 and Q10 are still open.
- **Fix:** Point these lines to the accepted policy.

**N5 — Low: some route wording is imprecise.**
- **Where:** ADR-024:52.
- **The problem:** "both runner and helper freshly resolve" reads as if every route uses the helper. Current-user MSIX and user-scope WinGet have no helper step.
- **Fix:** Say "and, for helper routes, the helper".

**N6 — Low: "plausible identity match" has no deterministic definition.**
- **Where:** ADR-023:40.
- **The problem:** It blocks safely, but AGENTS.md requires deterministic, testable rules.
- **Fix:** Require Task 56 to define the matching predicate and its test fixtures.

## Objections and recommendation

- No objections to the safety design. Items that are only gated, such as unproven WinGet/MSIX targeting, the absent no-upgrade guard and missing host evidence, are correctly fail-closed with routes unavailable. They are not design defects.
- **ADR-026:** accept as Option C (Defer).
- **ADR-023–025:** recommend acceptance after fixing N1 (wording, or an explicit owner choice between (a) and (b)). Fold in N2–N6 as small edits, or carry them as conditions for the implementation tasks.

## Acceptance snapshot and final completion

Final author-follow-up validation: **46 documents, 307 resolving relative links, 25 prompts, eight phase briefs**, strict UTF-8/new-file whitespace/git diff checks pass; source/build/test/deployment diff remains empty. No code changes occurred after the passing build/290-test run, so those checks were not needlessly repeated. Final Proposed ADR hashes presented for owner acceptance: ADR-023 `1F698431C3EEDCDBB24B990E0C2B1AC1F6651A46884BC74CC82220DED04FAC3D`; ADR-024 `4BE6FE460D3AF43F84FA050807801358D488CADF51E27DE18848696D2FD8A6D8`; ADR-025 `240786799E3020DCB88A1CD24F93D546D659C02236235E041FB889863EBAF9BB`; ADR-026 `EE4122BCC525AE8657B0FD594A6B7DAC88F273067DF5B2396DF9A1DD20816036`. These include disclosed final author N1–N6 clarifications; the re-check's earlier pins remain the historical reviewed snapshot.

The owner accepted the exact corrected contracts and final choices above; this closes A2/Task 47. Finalization updates only acceptance/status wording in Markdown and preserves both reviewer snapshots and pre-existing Task 46 edits. Git authorship remains unconfigured; no commit or configuration change. No follow-on implementation/deployment/host/credential/data/schedule/paid action occurred. Exact-host/distribution/paid-call gates remain open.

Post-acceptance verification passes: **46 documents, 307 relative links, 25 correctly allocated/finalized prompts, eight phase briefs, four Accepted operation ADRs and 200 uniquely keyed preserved requirement rows** (only pre-existing Task 46 FR-AI-04/FR-BUD-01 amendments differ from HEAD). Strict UTF-8, new-document whitespace and git diff --check pass; source/dependency/build/test/deployment diff remains empty. The same Task 47 Release build and 290/290-test logs were re-read and verified; no code changed, so acceptance-only Markdown finalization did not repeat those passing checks. No Task 47 blocker remains.
