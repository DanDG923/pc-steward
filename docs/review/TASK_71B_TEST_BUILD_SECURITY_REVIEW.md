# Task 71B — Enabled AI test-build re-review

**PASS for the exact enabled build and one-request procedure identified below.** T71-01 and T71-02 are fixed. Fresh synthetic verification preserves the ledger and invokes no removal callback for every refused reset state; ordinary startup behavior is preserved. The replacement artifact is complete, matches its reviewed manifest and is rejected by the release candidate's mark check. No new actionable security, privacy, paid-bound, continuity or release-path finding was established.

Reviewed **2026-11-15**. This pass clears the build for the owner's **separate later key and send approvals**; it grants neither approval. No downloaded application was launched or loaded for execution. No real key/credential-store action, AI-provider call, paid request, release, signing or installation occurred. This report stays local: no STATUS/TRACEABILITY change, commit, push or PR.

## 1. Immutable scope and inputs

| Item | Exact reviewed value |
|---|---|
| Source / branch | `12852c3b015c51309b4f367afb9476e2134989dc`, `phase/4-beta` |
| Workflow run | [43660727591](https://github.com/DanDG923/pc-steward/actions/runs/43660727591), attempt 1, successful `workflow_dispatch` |
| Artifact | `pc-steward-ai-first-live-test`, ID `14139662564` |
| ZIP SHA-256 | `99570e626fe3fc35ed588f1717924deebf9a0f99bd18db04b50304ea423a650d` |
| Manifest SHA-256 | `699F3543C97B13FB8BE0D6C0EEE570052C3B8D45F81AAE7CCDD14C23CB736334` |
| Model / tier / route | `gpt-6.1-sol` / `default` / `POST https://api.openai.com/v1/responses` |
| Local checkout | `codex/task-71b-test-build-security-review`, exactly the source above, no tracked overlay |

The remote branch matched this SHA at entry and final checks, and the named run remained the newest AI first live test build. No other commit is the review target. The prior source is used only as a comparison baseline. PR #133's first-parent diff contains the reset refactor, clear guard, test-build packaging/workflow changes, explanatory text and matching tests. It does not change Domain/Application AI logic, transport/credential adapters, AI persistence/migrations, or the update/schedule/self-update implementations.

Inputs: the pasted Task 71B prompt, AGENTS.md, pinned STATUS, Task 90 §9, Task 69 §§10/12 and the local Task 71 report. Earlier local Task 70/70B/70C/70D/70E/70F reports supplied historical context; their verdicts were not carried forward as proof of this enabled build. The independent report itself is unavailable; F1–F9 are assessed as recorded in Task 90 §9, without inventing unseen report details. The applicable unchanged ADR-028 A1/A2, ADR-029 and Phase G contracts retain their accepted authority and evidence limits.

Requirements addressed: **FR-AI-01–06, FR-BUD-01–04, FR-DAT-06–07/12–15, NFR-REL-02 and NFR-MNT-01–05**, plus this prompt's enabled-build action restrictions, artifact identity and one-request procedure.

## 2. T71-01 — fixed: reset-free test startup before shared-state work

**Original severity/scenario:** High. Launching the old test build with a legitimate installed-copy reset marker could complete that reset and delete the shared ledger/task/key before its UI restrictions applied.

**Current source evidence:** `src/PCSteward.Storage/FactoryReset.cs:187–207`, `src/PCSteward.Desktop/App.xaml.cs:63–82`.

- Desktop passes the compiled `AdvisoryAiFirstLiveTestBuild.IsTestBuild` getter into `RecoverAtStartup` at line 63.
- `RecoverAtStartupCore` returns Continue only for `None` at Storage line 195. With refusal enabled, it returns `RefusedInTestBuild` for **every other state**, before evaluating either callback delegate (lines 196–197).
- Desktop handles that result by showing the fixed pending-reset notice and returning at lines 64–67. The notice offers no recovery or Continue action.
- `Directory.CreateDirectory`, database construction/migration, explanation expiry and reconciliation are afterwards at lines 75–82. Fresh-store verification and ordinary onboarding are also afterwards. The preceding common-gate acquisition/bootstrap scope does not run those callbacks or reconciliation itself.

The result therefore covers **ResetPending, Partial, OnboardingPending, TransitionPending and Invalid**, including unreadable control state classified Invalid. Merely constructing the schedule registrar/delegates does not invoke schedule/key operations. None of those states reaches the shared database or creates a fresh store through this startup path.

### Fresh independent reproduction

An isolated ignored C# verification harness referenced the pinned Storage and Persistence projects with the test symbol enabled. In each disposable, internally allowlisted `pcsteward-reset-*` root it created a real synthetic ledger through reserve → claim → uncertain settlement, holding **40,480 micro-USD** of first-test liability. It then prepared each marker state and called the new internal `RecoverAtStartupForTesting` seam with **the compiled build getter** as the refusal argument and counted fake removal callbacks.

| Marker | Result | Schedule / key callbacks | Folder and ledger |
|---|---|---|---|
| None | Continue | 0 / 0 | Byte/timestamp snapshot unchanged; test outstanding 40,480 |
| ResetPending | RefusedInTestBuild | 0 / 0 | Same |
| Partial | RefusedInTestBuild | 0 / 0 | Same |
| OnboardingPending | RefusedInTestBuild | 0 / 0 | Same |
| TransitionPending | RefusedInTestBuild | 0 / 0 | Same |
| Invalid | RefusedInTestBuild | 0 / 0 | Same |

This directly reverses the earlier reproduction, which deleted the synthetic database and invoked removal callbacks. It proves the new reset step, not an actual WinUI host launch. Repository `AdvisoryAiFirstLiveTestStartupResetTests` independently checks all states with a populated $0.99 settled ledger, counted callbacks and a complete folder snapshot. The requested startup-reset plus FactoryReset selection passed **41 tests in each build mode**.

### Ordinary behavior and alternate paths

The false-refusal branch preserves the previous sequence: None continues; Invalid needs attention; ResetPending/Partial/TransitionPending attempt completion and stop on failure; successful completion and an existing OnboardingPending state recheck onboarding/key authority before proceeding. Desktop still checks semantic fresh-store state before showing onboarding.

The independent harness compared the new ordinary branch against the **previous startup control flow** for all six states, each with successful callbacks, a failing schedule callback, and an unverified key-removal callback: **18 parity cases passed**. It compared result kind/success/residual reasons, schedule/key callback counts, database existence and resulting marker state. These are fake callbacks and disposable roots, not real scheduler/credential tests. Existing FactoryReset and post-reset-flow fixtures also passed.

The following remaining methods are not globally removed; their production reachability matters:

- `App.xaml.cs:233–236` still supplies ordinary onboarding's `AcknowledgeOnboarding` callback. The test build can never get `startup.Kind == Onboarding`: with None it gets Continue, and every other state returns before MainWindow construction. `MainWindow.cs:180` selects Onboarding only from that startup flag; the navigation items expose no separate Onboarding entry. `MainWindow.Onboarding.cs:34` and `PostResetOnboardingFlow.Evaluate` therefore provide no reachable test-build Continue bypass.
- The acknowledgement core (`FactoryReset.cs:240–258`) independently requires OnboardingPending and a verified fresh store before key removal. It has not been weakened by the refactor.
- Desktop has no other call to `CompletePendingAsync` or `VerifyOnboardingBootstrap`; the new startup step owns both delegates.
- `ScheduledHost/Program.cs:43` returns for **any non-None marker before directory creation/migration**; line 50 rechecks before scanning. It does not complete/acknowledge resets, read/delete AI credentials or send AI requests. The installed task is not retargeted to the extracted host by this build.

No alternate startup/Continue route to reset completion or credential removal was found. This is a source/fixture reachability conclusion, not protection against arbitrary code already executing as the owner.

## 3. T71-02 — fixed: Data & reset clears are not wired to operations

**Original severity/scenario:** Low. Confirming a clear in the extracted build changed the installed copy's shared data despite the broad AI-only description.

`MainWindow.DataAndReset.cs:77–99` now handles test mode **before the switch that attaches operations**. Every card action is created through `DataActionButton` (line 53). In test mode its only click handler shows an unavailable notice, restores focus and returns the button; no destructive confirmation or operation handler is attached. This covers scan data, downloads, source metadata, logs, update history and mappings/policies. Factory reset has its separate pre-confirmation refusal at line 123.

The source boundary check passed and was inspected alongside the handlers, rather than treated as a behavioral WinUI test. The other call to download cleanup, on an update's completion view (`MainWindow.UpdateJob.cs:499`), requires an in-memory `updateJob`. Its only non-null assignment occurs after the refused update-review entry, so no reachable test UI path to that completion control was found. No actual clear button was clicked on the host.

Task 69 §12 line 182 now accurately states the remaining effects: ordinary reset-free startup can create/migrate the shared database, expire saved AI explanations and reconcile interrupted updates; scans/refreshes, connector consent, matching decisions, diagnostic collection/source settings, exports and copy remain available. These are **not an isolated profile or a read-only app**. They must not be used during this experiment. `AppIdentity.cs:16` no longer says the app only runs the AI test; it describes the restricted action families. Normal expiry is expressly disclosed separately. The remaining list is adequate for the inspected UI; a future new mutation route needs its own review.

## 4. Artifact, F1 and the build changes

The named ZIP was downloaded from GitHub and statically inspected, never executed. GitHub metadata identifies the exact requested run/source/artifact ID, reports `expired=false` and gives the supplied digest. Observed local results:

| Check | Actual result |
|---|---|
| Compressed size | **62,139,396 B** |
| ZIP SHA-256 | Exact supplied digest |
| Manifest SHA-256 | Exact supplied digest |
| Archive entries/files | **411 / 411** |
| Expanded file bytes | **169,345,184 B** |
| Outside `app/` | Only `ai-first-live-test-manifest.json`; no stray resource files |
| Manifest sourceCommit / runId | Exact `12852c3b015c51309b4f367afb9476e2134989dc` / `43660727591` |
| App coverage | **410 unique listed paths = 410 actual files**, every size/hash matched, no missing/unlisted app file |
| Unsafe archive paths | No rooted/traversal/colon path, duplicate name or symlink entry found before extraction |
| Elevated helper | No `PCSteward.ElevatedHelper*` file |
| OpenAI product version | `2.15.0+df2ac26b0f466aa022d5418833cdf70d86cfd40a` |
| OpenAI SHA-256 | `00D91FE575885FEC3380DD273E4A0E6377DAC89627D96142FC435B3B99CDD050` |
| Domain SHA-256 | `9BAB005BA6CA293D6D2F7E5F8B2FD4A2095CA679A2E8D51A9D1B18AFB8414755` |
| Desktop DLL SHA-256 | `EAE3A8B5A76D238CAD5F4127F3272329C0D4B45A67502B7EC0BBE5BE7ECD77A9` |

Separate PE metadata/IL inspection found the **type** `PCSteward.Domain.AdvisoryAiFirstLiveTestBuildMark`. Both `get_IsTestBuild` and `get_PaidRequests` contain IL **17 2A**: return true / enum value 1 (FirstLiveTest). This checks compiled getters, not only manifest claims or a name string. Assemblies were read as PE data, not loaded into an execution context.

**F1 is fixed for this artifact.** `New-AdvisoryAiFirstLiveTestBuild.ps1:44` gives `--output` the trailing separator, avoiding the previously duplicated `appApp.xbf`/`appResources` siblings. Lines 66 and 90 enumerate with `-Force` so hidden/system files count. The check at line 91 requires upload-file count = all app files + the manifest. Since the manifest is generated from the complete app listing and output creation is fresh/fail-on-existing, an extra outside file increases that count. The workflow sets `include-hidden-files: true`; empty directories are not an omitted executable-file exception.

The real run logs independently report:

```text
AI-TEST-BUILD commit=12852c3b015c51309b4f367afb9476e2134989dc files=410 manifestSha256=699F3543C97B13FB8BE0D6C0EEE570052C3B8D45F81AAE7CCDD14C23CB736334
With the provided path, there will be 411 files uploaded
SHA256 digest of uploaded artifact is 99570e626fe3fc35ed588f1717924deebf9a0f99bd18db04b50304ea423a650d
```

The archive verification covers **all** entries, correcting the earlier review's narrower app-file-only completeness check. The current counts, manifest and file contents agree. This verifies the downloaded artifact against the pinned hosted record, not a reproducible build or an uncompromised GitHub/toolchain guarantee.

## 5. Owner procedure and independent-review notes

The exact PowerShell folder-check body in Task 69 §12 was executed against the fresh extraction, substituting only the folder and the **reviewed** manifest hash. It printed `The folder matches the reviewed manifest.` A separate disposable copy was then mutated and restored for each scenario:

| Scenario | Documented check result |
|---|---|
| Changed app-file contents/size | Rejected |
| Missing listed file | Rejected |
| Extra app file | Rejected |
| Hidden extra app file | Rejected |
| Extra file outside app, reproducing the old sibling shape | Rejected |
| Changed manifest, with trusted review hash unchanged | Rejected |
| Restored copy | Passed |

`Get-ChildItem -Force` in the check includes hidden files. The hash anchor prevents accepting a self-edited manifest as authoritative; file-name lookup, sizes/hashes and counts reject missing/extra files. The root count rejects siblings beyond app and manifest. These are fixture proofs against copies, not claims about a later owner extraction. The title and mark alone are not authenticity checks, and same-user malware can still replace files after checking under the already accepted compromised-host residual.

| Finding/note as available in Task 90 §9 | Disposition and current evidence |
|---|---|
| **T71-01 / F3**: startup reset recovery | **Fixed**, §§2–3 above; all states and ordinary parity independently exercised. |
| **T71-02 / F6**: data clears and scope | **Fixed**, §3; clear operations are unwired in test mode and remaining shared writes are explicitly prohibited by the procedure. |
| **F1**: incomplete upload/manifest correspondence | **Fixed**, §4; 410 app files plus one manifest, no outside extras. |
| **F2/F4/F5**: rerun, installed-version check, key cleanup (grouped in the record) | **Fixed/answered** by §12 steps 1, 3 and 4, assessed individually below. The original independent report is unavailable, so no more specific unseen wording/severity assignment is inferred. |
| **F7**: one request is procedural | **Answered accurately**, §12 line 184 and §10 item 6; no assertion of a software one-send fuse. |
| **F8**: ambient enablement/private packager | **Answered with retained limitation**: environment/response-file enablement produces a marked build; the current release candidate refuses it. Root `.rsp` files are now included in the architecture scan. The non-release `New-PrivateBetaPackage.ps1` still has no mark check, explicitly accepted in §12 line 217. |
| **F9**: unsigned program warning | **Answered**, §12 step 2 warns about SmartScreen. It does not approve signing, elevation or skipping integrity verification. |

**Steps 1–4 judgment:**

1. **Use the reviewed run.** Line 190 downloads the named reviewed artifact and forbids starting another run as a substitute. Expiry/new-run wording retains code-review relevance only and requires new artifact checks; this report's artifact verdict does not authenticate a replacement ZIP.
2. **Check ZIP, then anchored manifest/folder.** Line 191 and the executable read-only check establish a concrete way to distinguish changed/extra/missing files. It must run on the owner's eventual fresh extraction immediately before an approved launch. The correct digest must come from the review, not from an editable sibling file.
3. **Compatible/reset-free installed state.** Line 192 requires About 0.1.17 and a normal installed-copy start/close before launching the test build; an unexpected pending-reset notice stops the procedure. Version 0.1.17 is the documented migrations-1–15 release, avoiding the 0.1.16 schema-13 downgrade problem. This review did not inspect the installed executable/database or perform that launch. An unfinished reset should be handled separately under the installed copy's approved recovery path, with external allowance continuity preserved; no reset/backup restore grants fresh $1 authority. The new test-build guard independently refuses residual/unreadable reset state even if the manual check was mistaken.
4. **Stop, Disconnect, then provider deletion.** After §10's one attempt, the per-request cap is set to $0 regardless of outcome. Step 4 then uses deliberate Disconnect and checks verified local key absence before closing/deleting the test folder; provider deletion is separate. Disconnect preserves the ledger and does not claim provider revocation. If either removal fails, stop and report the residual rather than call it clean or use Factory reset. No cleanup operation is performed by this review.

The procedure still requires separately established prior settled **and uncertain** test use, including external unknown/restored/reset history; an empty or newly declared ledger is not proof of no prior spending. The user states no key/send has occurred for this task, but no account/host audit was performed here. The synthetic replacement procedure also still requires a suitable existing Diagnostics record and exact inspection of the real interval/omission metadata; do not improvise an OS collection or send genuine records to make the test possible.

## 6. Brief regression audit of the previously passed controls

| Area | Fresh conclusion for this source/artifact |
|---|---|
| Release-path refusal | The real pure `Test-OwnerOnlyCandidateAiTestBuildMark` returns **true** on this downloaded Domain. Its unchanged call at `New-OwnerOnlyCandidate.ps1:470` throws before completing release payload production. The five candidate-builder fixtures pass. Release-payload still uses that builder; publish-release requires a successful manually started run of the exact release-payload workflow, not this test-build run. Neither release workflow changed in #133. No release script was executed. |
| Ambient build property | The unchanged conditional property can still arrive through environment or `.rsp`; the compiled symbol includes both paid policy and mark. Runtime Desktop guards consult the same Domain getter. This is an output restriction on the reviewed release path, not a promise that arbitrary local builds cannot be enabled. The final ordinary build has no mark. |
| App updates | All inspected WinGet user/machine single/batch/review-again UI routes reach `MainWindow.UpdateJob.cs:565`, before preparation at line 602. Chocolatey package/client entries retain separate refusals at lines 50/93. #133 does not change these routes. |
| Schedule / new reset / self-update | Save schedule refuses at `MainWindow.Settings.cs:94`; new reset refuses before confirmation at DataAndReset line 123. SelfUpdater is null in test mode (`MainWindow.SelfUpdate.cs:42`), so startup does not read/forget the installed self-update job; Check refuses before its GitHub client at line 146. #133 only adds the startup reset and clear restrictions discussed above. |
| Elevation | `App.xaml.cs:115–116` still passes null for the elevated runner. The artifact has no helper and its application manifest is `asInvoker`. Non-elevated execution code is still present; UI refusals remain material. No UAC/host launch was tested. |
| Egress | Exact POST `https://api.openai.com/v1/responses`, one approved body. Last transport guard checks method/absolute route/auth/body, strips SDK headers and recreates the closed set. TLS validation remains enabled, no redirects/cookies/default credentials, no tracing propagation, zero SDK retries, at most one guard attempt, 10-second connect and 45-second overall deadline. Source and transport fixtures pass; no real wire capture was made. |
| Actual JSON | `model`, fixed `instructions`, edited/redacted serialized `input`, `max_output_tokens:2048`, `store:false`, `truncation:"disabled"`, `service_tier:"default"`, `reasoning:{"effort":"low"}`, `prompt_cache_options:{"mode":"explicit"}` with no breakpoint. **`stream` and `background` are omitted**, not serialized false. No tools/files/previous-response/conversation/extra identifying metadata or token-counting call. Provider interpretation of omitted fields is separate from proof of these bytes. |
| Headers | HTTP authority `api.openai.com`; exact-key Bearer Authorization, `Accept: application/json`, `Content-Type: application/json`, `User-Agent: PCSteward-AdvisoryAI/1`, exact Content-Length. Authentication is masked in preview. No organization/project/SDK telemetry header survives the last guard. |
| Credential / answer isolation | The exact current-user target remains `PCSteward/OpenAI/ApiKey.v1`, with explicit consent, masked entry and no environment-key discovery. Reservation precedes dispatch-key read. Disconnect verifies removal and preserves accounting. Outputs remain bounded/redacted plain advisory text with no execution/trust/eligibility authority or automatic links. No provider strings enter the closed usage/reason/tier capture channel. The response/redaction/setup/explain suites passed. |

### First-request bound and accounting

The candidate/profile, packet limits, tariff attestation, ledger implementation and migrations are unchanged by #133. At **12,288 input / 2,048 billable output**, the recorded input/output rates yield:

```text
Listed rates:      12,288 × $2/M + 2,048 × $10/M   = $0.045056
10% uplift rates:  upward-rounded micro-USD total = $0.049562
Request cap: $0.05; monthly cap: $1.00; separate cumulative first-test envelope: $1.00
```

Thus $0.049562 is the tested worst case **with the recorded 10% uplift**; it is not the base-rate figure or an unconditional bound for arbitrary current prices/provider departures. The real preview can reserve less when its input upper bound is smaller. Coupled request/response/saved-item allowances remain 74,752 / 3,481,600 / 3,932,672 B, under the 4 MiB maxima, with room for the synthetic input.

The owner's B1(a) byte bound and B2 wide margins remain **accepted one-request policy, not measurements**. One future usage report can check the bound but cannot prove framing generally. Project-effective fresh prices and any separate throwaway-project funding backstop remain action-time checks. No current price/account balance, provider budget enforcement or invoice finality was queried or inferred here.

Fresh ledger tests preserve the serial admission → full reservation → exact-key read → durable unique dispatch claim → one send → settlement sequence. Settings/consent/key/tariff versions are rechecked; the common cross-process gate and clock/continuity/capacity rules remain. Settled and uncertain commitments, including compacted prior months, count toward $1. Restart never replays a claimed request or refunds an uncertain liability. Compaction conservation/proof rows, deduplication, tariff pruning and migration-14-to-15 tests pass; #133 makes no AI schema/accounting change. Sharing the installed store is intentional, with the documented external-restore/self-declaration limitation.

Reliable terminal complete/incomplete usage can settle at frozen rates independently of answer saving. Timeout, malformed/oversize reply, missing/conflicting usage, model mismatch or error shape keeps the reservation without a general automatic pause. Nonzero/unreadable cache writes, repeated/different/unrecognised tiers, unknown usage keys, cached input and unexpected conversation/background state keep untrusted usage and pause for review, including billing fields in otherwise refused bodies. Every occurrence is examined so a repeated/null field cannot hide a departure. A reliable charge above reservation is recorded in full and pauses. There is **no automatic retry**. A retained reservation can be less than a provider's off-contract charge; A2 explicitly accepts this detection-afterward residual for the supervised test, not ordinary use.

The owner must stop after the single attempt, save the $0 per-request cap and report even on an ordinary timeout that does not auto-pause. If that save cannot complete, close and do not send/relaunch for another attempt; recovery retains the liability. No success, reconnection, new month or cap change promotes FirstLiveTest to ordinary mode. CPU/working-set/WinUI/idle-I/O and complete physical DB/WAL/backup measurements remain open ordinary-use gates; logical quotas do not prove those physical bounds. Their absence alone does not block this accepted bounded experiment. No new #133 privacy, paid-bound or continuity regression was found within that scope.

## 7. Verification and limits

All tests used the exact pinned checkout and synthetic fixtures. Installed approved SDK **10.0.404** is within global.json's 10.0.403/latestPatch policy. No SDK installation, dependency change or identity/global configuration change was made. The isolated verification harness restored only existing approved project dependencies; no test-build workflow was locally forged or rerun.

| Command/selection, Release | Result |
|---|---|
| Application startup-reset + FactoryReset, `PCStewardAiFirstLiveTest=false` | 41 passed, 0 failed/skipped |
| Same selection, property true | 41 passed, 0 failed/skipped |
| Domain `FullyQualifiedName~AdvisoryAi`, property true / false | 240 / 240 passed, 0 failed/skipped |
| Application AI + reset + post-reset flow + schema + current-user lease + data-reset presentation + scheduled checks, property true / false | 244 / 244 passed, 0 failed/skipped |
| Architecture AI + ReferenceBoundaryTests + UiRedesignContractTests, property false | 77 passed, 0 failed/skipped |
| Security OwnerOnlyCandidateBuilderTests only, property false | 5 passed, 0 failed/skipped; inspected pure/synthetic fixture scope |
| **Total focused test executions** | **1,132 passed, 0 failed, 0 skipped**; includes intentional repetition of the narrow reset selection, not 1,132 distinct definitions |
| Independent startup harness | Exit 0; six enabled state cases and 18 ordinary parity cases; zero real task/key calls |
| Documented folder check and mutated copies | Authentic/restored copy passed; all six mutation scenarios rejected |
| Canonical `pwsh -NoProfile -File .\scripts\Build.ps1` | Exit 0, **0 warnings / 0 errors**; process-local property false, previous environment restored |
| Final ordinary output | Test-build mark absent |
| Scope | No tracked or staged change; the only new deliverable is this untracked local report; seven earlier reports preserved |

For reproducibility, the application regression filter was:

```text
FullyQualifiedName~AdvisoryAi|FullyQualifiedName~FactoryResetTests|FullyQualifiedName~PostResetOnboardingFlowTests|FullyQualifiedName~SqliteDatabaseTests|FullyQualifiedName~CurrentUserDataOperationLeaseTests|FullyQualifiedName~SettingsDataResetPresentationTests|FullyQualifiedName~ScheduledChecksTests
```

Each selection used `dotnet test <project> -c Release --no-restore -p:PCStewardAiFirstLiveTest=<true|false> --filter <selection>`. The narrow reset selection ran first, then the focused regressions and canonical build. Local logs/downloads/probe code are ignored verification scratch under `artifacts/task71b-review`; the report is the only deliverable. The independent harness did not execute any downloaded assembly or native credential/task adapter. Earlier reports and their verification evidence remain untouched.

**The canonical full suite was not run:** this task explicitly forbids signing. Full Security fixtures create certificates in `Cert:\CurrentUser\My` and call `Set-AuthenticodeSignature` (`Fixtures/ReleaseSigningScripts.ps1:28,40`, also PinnedReleaseSignatureTests). The five selected candidate-builder fixtures do not do those things. No full-suite pass, real credential lifecycle, actual host launch/UI measurement or provider enforcement is claimed. This is an explicit verification limit, not permission to sign or silently weaken the suite.

## 8. Final gate and remaining action-time prerequisites

**New findings: None.** T71-01 and T71-02 are independently closed for this source; F1 is directly closed by complete artifact verification, and the recorded F2–F9 procedure/policy notes are addressed as above. The accepted same-user compromise, self-declared external continuity and provider-policy residuals are retained honestly. No unresolved in-scope privacy, paid-bound, continuity or release-path defect was established.

A later owner-authorized launch still needs the actual compatible/reset-free host, authentic freshly checked folder and quiescent installed copy. Key entry/storage and the exact previewed send each require their own later approval, with current project-effective prices, real cumulative test continuity and the synthetic-only evidence procedure checked then. This report performs none of those actions and grants no ordinary use, release or promotion out of FirstLiveTest.

**PASS — the enabled test build at commit `12852c3b015c51309b4f367afb9476e2134989dc`, artifact SHA-256 `99570e626fe3fc35ed588f1717924deebf9a0f99bd18db04b50304ea423a650d`, model `gpt-6.1-sol` at the `default` tier, route `POST https://api.openai.com/v1/responses`, and the one-request Task 71 procedure.** This pass clears this exact build/procedure for the owner's separate key and send approvals. **It approves neither.**
