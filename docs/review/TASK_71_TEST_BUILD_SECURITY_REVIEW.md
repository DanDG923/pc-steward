# Task 71 — Enabled AI test-build security, privacy and cost review

**FAIL.** The pinned artifact is the intended enabled test build, and its request accounting and release-path refusal pass the checks below. However, starting it with a legitimate pending factory-reset marker can complete the installed copy's reset before the test-build UI guard runs. That can delete the shared ledger, remove the exact AI credential and remove the installed scheduled task. **T71-01 (High) blocks the launch/key/send procedure.**

Reviewed 2026-11-15. This is a local review report only. No product code, STATUS or TRACEABILITY changed; no commit, push, PR, release, signing or installation was made. No key was entered or read, no AI-provider request was made, and the downloaded application was never launched or loaded for execution. Verification used source inspection, static PE metadata/IL, synthetic stores and substituted adapters.

## 1. Exact scope, requirements and drift

| Item | Reviewed identity |
|---|---|
| Source | `97699d938eb8f26c7bf43311df10faa36239c275` on `phase/4-beta` |
| Workflow | [AI first live test build, run 43656505549](https://github.com/DanDG923/pc-steward/actions/runs/43656505549), attempt 1, `workflow_dispatch`, success |
| Artifact | `pc-steward-ai-first-live-test`, artifact ID `14138756008` |
| ZIP SHA-256 | `ad48ca878f11f4a1430abd78dc328a3df096a177e40a12eb42897316eddcc1e9` |
| Manifest SHA-256 | `3B7FAE4DB0F2B8E27C750477200FB63E6C622396605BCDEAEC2D2DE6C5043AF0` |
| Model / tier / route | `gpt-6.1-sol` / `default` / `POST https://api.openai.com/v1/responses` |
| Local review branch | `codex/task-71-test-build-security-review`, exact source above, no tracked overlay |

The remote branch had already advanced to `293a07fcd493258a1dd02954c698a137cee654ba`. Entry and final checks found only four differences from the pinned source: `docs/STATUS.md`, `docs/review/TASK_64_DIAGNOSTIC_TIMELINE_PLAN.md`, `src/PCSteward.Diagnostics.Windows/ReliabilityDiagnostics.cs`, and `tests/PCSteward.Application.IntegrationTests/WindowsDiagnosticReaderTests.cs`. No AI or build file differed. The named run remained the newest AI test-build run, created `2026-11-15T00:06:15Z`, with the exact pinned source. The prompt's conditional stop therefore did not apply. This report reviews **only 97699d9 and the named artifact**, not the newer diagnostics implementation.

Requirements addressed: **FR-AI-01–06, FR-BUD-01–04, FR-DAT-06–07/12–15, NFR-REL-02 and NFR-MNT-01–05**, with the directly relevant accepted ADR-028 A1/A2 and ADR-029 lifecycle/accounting contracts. The enabled-build action boundary and preservation of existing local state are also explicit requirements of this review prompt and Task 69 §12.

Inputs included AGENTS.md, pinned STATUS, prompt 71, Phase G, the shared task contract, ADR-028 including A1/A2, ADR-029, Task 69 including §§6–7, 10 and 12, and the Task 90 remediation record. All six available local Task 70/70B/70C/70D/70E/70F reports were consulted. Their historical verdicts were not treated as proof for this enabled artifact. The independent reviews' findings are available through the remediation record; their original reports are not in the repository. Source and fresh tests independently establish the behavior described below.

## 2. Findings

### T71-01 — High: startup reset recovery bypasses the test-build reset restriction

**Primary location:** `src/PCSteward.Desktop/App.xaml.cs:63`, particularly the call at **line 65**. Related locations: onboarding credential recheck at **line 82**; the real credential removal callback at **line 234**; `src/PCSteward.Storage/FactoryReset.cs:130`, **137** and **149**; `src/PCSteward.Scheduling.Windows/ScheduledCheckRegistration.cs:139–148`. The new UI refusal exists only at `src/PCSteward.Desktop/MainWindow.DataAndReset.cs:113`.

**Concrete scenario.** The installed copy previously armed a legitimate reset, then exited or failed with a `ResetPending`, `Partial` or `TransitionPending` marker. The owner follows Task 69 §12: closes the installed copy and launches the reviewed test build from its fresh folder. Before creating its main window or offering the key step, `App.OnLaunched` opens the same state root and calls `FactoryReset.CompletePendingAsync` without checking `AdvisoryAiFirstLiveTestBuild.IsTestBuild`. The callbacks are the real current-user scheduled-task removal and real exact-key removal. The reset engine then removes downloads, logs, `pcsteward.db` and its sidecars. A schedule or credential residual does not prevent subsequent database-category deletion; it makes the overall reset partial. An `OnboardingPending` start also reaches exact-key removal verification at line 82.

The schedule callback targets the fixed task name `PCSteward.ScheduledCheck`, not a separate task for the extracted folder. The credential callback targets `PCSteward/OpenAI/ApiKey.v1`. The data root is `%LOCALAPPDATA%\PCSteward` (`FactoryReset.cs:15`). Thus deleting the helper from the artifact and refusing a new Reset click do not protect these installed/shared objects on startup. This scenario requires an existing legitimate reset state; it does not assume marker forgery or a compromised host.

**Independent synthetic proof.** An ignored, local verification program referenced the pinned Storage/Persistence projects with `PCStewardAiFirstLiveTest=true`. It created an internally allowlisted disposable `pcsteward-reset-*` temporary root, migrated a synthetic database, and recorded a claimed then uncertain first-live-test reservation of **40,480 micro-USD**. It armed only that synthetic root through the existing internal fixture seam and invoked the same reset core that the startup call uses, with fake schedule and credential removal callbacks. Observed output:

```text
test-build=True
test-outstanding-before-reset=40480
synthetic-startup-reset-succeeded=True
database-exists-after-reset=False
fake-removal-callbacks=schedule:1; credential:1
onboarding-recheck=True; credential-callbacks:2
```

No real task, Credential Manager entry, installed database or downloaded app was used. This dynamically proves the reset engine's effect in enabled mode; the unguarded real startup wiring is established by source inspection, not a host launch. The existing reset fixture `AiKeyRemovalRunsBeforeTheDatabaseIsDeletedAndAnUnverifiedRemovalKeepsTheMarker` also exercises deletion despite a credential residual. The new architecture test, `AdvisoryAiFirstLiveTestBuildBoundaryTests.cs:107–109`, checks only the **arming** method's text and misses startup recovery.

**Impact and boundary.** Launching the purportedly constrained test can remove installed settings/history, the exact key and task, and the accounting evidence needed for the cumulative $1 test check. This is not proof of an automatic refund or automatic second send: fresh setup's prior-use gate still exists, and the owner's external accounting requirement still applies. It is nevertheless an unresolved shared-state and continuity risk before the one-request procedure even begins. Earlier reset authorization does not establish that the enabled test-build procedure safely preserves these objects.

**Required resolution.** Make an enabled test build refuse startup on pending/partial/transition/onboarding reset state **before invoking any reset/key/task callback, migration or fresh-store creation**. Recovery should be performed separately in an appropriately approved installed build. Add an enabled-startup behavior fixture for every reset marker state, with a populated ledger and removal callback counters, proving no deletion or callback occurs. Rebuild and independently review a newly pinned source/artifact. Do not clear a real marker or reset the owner's data to make this artifact appear safe.

### T71-02 — Low: the “only runs the one AI test” description overstates data isolation

**Locations:** `docs/review/TASK_69_AI_EVIDENCE_GPT_6_1_SOL.md:170`; `src/PCSteward.Desktop/AppIdentity.cs:16`; enabled clear controls at `src/PCSteward.Desktop/MainWindow.DataAndReset.cs:83–88` and the common clear handler at **line 101**.

**Concrete scenario.** While preparing the AI test, the owner opens Data & reset in the extracted copy and confirms Clear update history or Clear mappings and policies. These controls do not check the build mode and execute against the installed copy's shared store. Removing the extracted folder afterwards does not undo those changes. Scan/download/source-metadata/log clears likewise remain available. These are explicit user-confirmed operations, not hidden sends or automatic factory reset, and ordinary category clears preserve the AI ledger. They still make the broad AI-only wording misleading.

**Disposition.** This Low finding does not independently demonstrate a paid-bound failure. Narrow the description to the four restricted action families and explicitly disclose the remaining shared-data controls, or disable unnecessary mutating controls in this build. The supervised procedure should prohibit using them. T71-01 is the blocking startup defect; this disclosure issue is separate.

## 3. Compile switch and release-path refusal

`Directory.Build.props:22–24` appends `PCSTEWARD_AI_FIRST_LIVE_TEST` when the MSBuild property is `true`. `AdvisoryAiPolicy.cs:219–281` ties all four relevant facts to that same symbol:

- the sole reviewed profile is `AdvisoryAiFirstLiveTestBuild.Candidate`;
- `PaidRequests` is `FirstLiveTest`;
- `IsTestBuild` is true;
- the metadata type `AdvisoryAiFirstLiveTestBuildMark` exists.

Without the symbol the reviewed list is empty, paid requests are Disabled, IsTestBuild is false and the mark type is absent. Desktop guards consult the Domain assembly's runtime getter, so an environment property enabling Domain does not leave Desktop guarding on a separately mismatched preprocessor symbol. The two-mode focused tests passed; the final ordinary Release build had no mark.

**An ordinary build can be enabled by ambient MSBuild input.** A controlled process-environment probe setting `PCStewardAiFirstLiveTest=true` made `dotnet msbuild ... -getProperty:PCStewardAiFirstLiveTest,DefineConstants` report `true` and include the symbol. An isolated ignored project with a `Directory.Build.rsp` containing `-p:PCStewardAiFirstLiveTest=true` did the same. The prior process environment was restored. Neither probe edited root build files. Therefore “only the script sets it” is a checked-in-source convention, not enforced build provenance. The architecture source allowlist also does not enumerate root `.rsp` files (`AdvisoryAiFirstLiveTestBuildBoundaryTests.cs:121–124`).

A stray property in another workflow, environment variable or response file can consequently produce a **marked test build**, including when the caller thought it was an ordinary build. It does not produce an unmarked enabled Domain through this switch. Directly defining the symbol also compiles the mark. There is no demonstrated provenance guarantee for arbitrary local builds, and no such build is approved here.

**The reviewed release pipeline rejects that output.** `Test-OwnerOnlyCandidateAiTestBuildMark` (`scripts/New-OwnerOnlyCandidate.ps1:98–104`) reads the payload-root Domain DLL and checks for the NUL-terminated metadata name. Its call at **line 470**, after payload assembly and before manifest/notices completion, throws on a marked payload. Extracting and invoking just this real pure function against the downloaded artifact returned **true**. The ordinary rebuilt Domain did not contain the mark. The five candidate-builder fixtures passed, including the synthetic mark check.

The ordinary string constant containing the marker's name is a UTF-16 user string; it does not cause the ASCII metadata-name test to reject an ordinary Domain assembly. In the downloaded assembly, separate PE metadata inspection confirmed that the **type** actually exists. The current desktop build is framework-dependent and untrimmed; no reviewed step strips this empty marker from Domain. A future trimming/assembly-rewriting pipeline would need fresh proof rather than inheriting this result.

`release-payload.yml` invokes `Test-OwnerOnlyCandidateBuilderCI.ps1 -ReleasePayload`, which uses the guarded candidate builder. `publish-release.yml:41–42` and `scripts/Publish-PCStewardRelease.ps1:164–165` require a successful, manually started run of the exact `release-payload.yml` workflow; they do not accept this AI workflow's run/artifact as a release. The test artifact contains no MSI, release check or updater entry. No release or signing script was executed during this review.

**Judgment:** the mark check is adequate for accidental ambient enablement through the **current reviewed release path**. It is not a cryptographic origin check, a guarantee that every local build is disabled, or protection against an authorized person deliberately replacing assemblies/bypassing the pipeline. No concrete release-path escape was reproduced. Explicitly setting false in ordinary workflows and checking compiled policy would be useful additional defense, not a substitute for the output check or a demonstrated blocker here.

## 4. Artifact identity and folder integrity

The artifact was downloaded from the named GitHub run and inspected locally without executing it. Both supplied SHA-256 values matched, and were rechecked at the end. The archive had 417 entries, **62,143,034 compressed bytes** and **169,353,144 expanded bytes**. Path checks found no rooted/traversal/colon paths or symlink entries before extraction into a new ignored verification folder.

The manifest has the exact source commit and run ID above, `reviewedModel=gpt-6.1-sol`, `paidRequests=FirstLiveTest`, and `notARelease=true`. Its **410 listed app files matched all 410 actual app files**, including every size and SHA-256, with zero mismatches and no unlisted app file. No `PCSteward.ElevatedHelper.exe`, DLL or other helper-named file was present. The desktop manifest requests `asInvoker` (`src/PCSteward.Desktop/app.manifest:13`).

| Additional static evidence | Result |
|---|---|
| `PCSteward.Domain.dll` SHA-256 | `C9614FED864F6BA14F77992207C3487F31A2D001B87C2EB0466FA8276AB7C118` |
| `PCSteward.Desktop.dll` SHA-256 | `9D2A40DE21406152FB120C0A78F3D84978E41EF31D839399991228756754D90C` |
| Domain metadata type | `PCSteward.Domain.AdvisoryAiFirstLiveTestBuildMark` present |
| `AdvisoryAiBuildPolicy.get_PaidRequests` IL | `17 2A`: return enum value 1, FirstLiveTest |
| `AdvisoryAiFirstLiveTestBuild.get_IsTestBuild` IL | `17 2A`: return true |
| OpenAI assembly product version | `2.15.0+df2ac26b0f466aa022d5418833cdf70d86cfd40a` |
| OpenAI assembly SHA-256 | `00D91FE575885FEC3380DD273E4A0E6377DAC89627D96142FC435B3B99CDD050` |

This ties the downloaded ZIP to the expected hosted build record and verifies the compiled policy independently of the manifest's assertions. It does not claim a reproducible binary build or prove an uncompromised GitHub runner/toolchain.

**Owner procedure.** Task 69 §12 steps 1–2 pin the run/source/ZIP digest and require fresh extraction. Those distinguish the authentic ZIP from another download. The manifest can distinguish changed/missing/unlisted files in the extracted app **if checked against the trusted manifest hash**; the title or marker alone cannot authenticate a folder. The written steps do not explicitly perform the supplied manifest-hash and complete per-file/no-extra-file comparison. Make that check explicit immediately before an approved launch, using the hashes recorded here rather than trusting an editable manifest in the folder. This review actually performed that comparison; an owner's later folder is a separate object and needs its own check.

The accepted same-user-compromise residual in §12 remains accepted: checking a folder cannot prevent malicious code already running as the user from replacing it afterwards or reading the key. That residual is not reopened as a new blocker. There is no claim that this folder was tampered with, nor a claim that the unsigned title constitutes authenticity.

## 5. Action paths and shared-state effects

| Path | Enabled-build behavior at the pinned source |
|---|---|
| WinGet user and machine Update, single/batch, Home/Updates/Details and history Review again | All inspected buttons lead to `ReviewAndExecuteAsync`; `MainWindow.UpdateJob.cs:565` refuses before `BeginBatchReviewAsync` at line 602. No preparation, approval or dispatch through those UI routes. |
| Elevated WinGet runner | Not composed: `App.xaml.cs:129–130` passes null for the elevated runner. The non-elevated runner still exists, so the common UI refusal remains material. |
| Chocolatey package Update | `MainWindow.Chocolatey.cs:50` refuses before opening/preparing the reviewed operation. |
| Chocolatey client Update | Its separate entry is refused at `MainWindow.Chocolatey.cs:93`, before review/probing/approval. |
| Save schedule | The shared Save handler refuses at `MainWindow.Settings.cs:94` before `ApplyScheduleAsync` at line 101. Editing the controls alone does not register/save. Normal startup may read schedule status. **Startup reset removal is the exception in T71-01.** |
| New Factory reset | `MainWindow.DataAndReset.cs:113` refuses before `FactoryReset.Arm`. **Existing reset/onboarding recovery remains live**, T71-01. |
| PC Steward self-update, including installed job record | `MainWindow.SelfUpdate.cs:42` returns no updater in test mode. `LoadSelfUpdateStatusAsync` returns before reading/forgetting an existing job; the Check action refuses at line 146 before constructing its GitHub client. Download/install routes require that absent updater. No self-update record is consumed by these test UI routes. |
| Data clears | Scan, downloads, source metadata, logs, update history, mappings/pins/ignores remain enabled with their existing confirmations and lease. Their effects are on shared installed data. Source-specific explanation deletion does not refund accounting. See T71-02. |
| Scan, source/connector settings, matching decisions | Existing manual scan/refresh and consent/mapping/pin/ignore controls remain; they can write inventory, metadata, settings and decisions outside AI tables. They do not gain AI send authority or bypass the update guard. |
| Diagnostics | Collection/snapshot save remains available (`MainWindow.Diagnostics.cs:296–300`), as do source settings and user-confirmed exports (`334–355`). These are real local reads/writes, not synthetic-only by virtue of the build. Do not initiate a new OS collection merely to improvise the test evidence. |
| Exports/clipboard | Existing inventory CSV/JSON picker and sanitized diagnostics copy can write a selected file or clipboard. They do not include AI keys/prompts by design, but are additional host actions requiring their own deliberate use. |
| Normal startup writes | Shared-root creation/migration, 30-day saved-AI expiry (`App.xaml.cs:89–93`) and interrupted-update reconciliation (`94–97`) still run. This is not a read-only or separate-profile executable. |

The source-only boundary fixtures are useful regression checks for the named routes, not proof of “every action” in their test method's title. No actual WinUI interaction, UAC behavior or installed-host action was exercised here. The downloaded artifact includes other supporting assemblies, including execution/scheduling code; missing the elevated helper does not remove non-elevated capabilities or the startup reset code.

### Shared database, credentials, migrations and return to installed copy

There is no test-specific data directory or credential namespace. Sharing is intentional for continuity: the same settled and uncertain detailed and compacted test commitments count against $1. Closing/reopening, key replacement/reconnect, tariff/limit changes and month rollover do not reset that cumulative ledger. The common current-user cross-process lease serializes mutations and send work; startup fails busy when it cannot acquire the gate. It is not continuous exclusion of a scheduled scan for the entire time the window is open.

Task 69 §12 requires migrations compatible with the installed release and states that 0.1.17 has migrations 1–15. In this source `SqliteDatabase.cs:86–93` applies 14/15 and validates the AI schema inside migration handling. Focused tests include populated migration-14-to-15 upgrades and existing-store preservation. The enabled switch adds **no migration or alternate schema**. This supports sharing with the documented compatible installed build, not an arbitrary older executable or experimental schema-15 layout. A successful migration does not prove that an older installed binary can subsequently consume the changed store.

Before any future launch, separately establish the actual installed version/schema compatibility and quiescent/reset-free state under host authority. This review did not inspect the installed app, its real database, tasks or credentials. Stop on unexpected recovery, stale/corrupt/missing state or a migration mismatch; do not reset, restore an older backup or assert no prior use to obtain a zero ledger. A backup can protect content but cannot be rolled back to erase a paid/uncertain commitment. External/coordinated restore remains the accepted self-declaration limitation, not tamper-proof accounting.

Returning to an ordinary disabled build leaves the test ledger/settings intact and removes the compiled AI availability; `App.xaml.cs:211–218` composes setup/send adapters only for a reviewed model. Deleting the extracted folder **does not** delete the shared database or `PCSteward/OpenAI/ApiKey.v1`. Task 69 §10 calls for provider-side throwaway-key deletion after the test, but provider revocation is distinct from local Disconnect. Any later cleanup plan should explicitly arrange approved Disconnect with verified exact-target absence before removing the only enabled setup UI, plus separate provider revocation, while preserving the ledger. Do not use Factory reset for cleanup. This is planning advice, not authority to perform either key operation now.

## 6. Enabled advisory path, payload and isolation

The candidate becomes available to setup and Explain only because the reviewed Domain registry is populated. `App.xaml.cs:213–218` composes exactly the registered OpenAI credential/transport pair and passes **the compiled FirstLiveTest policy** to the request workflow. Constructing these adapters makes no key read or network call.

### Exact egress and payload

The compiled destination is **POST `https://api.openai.com/v1/responses`**, standard HTTPS port 443, with no custom endpoint, query, organization/project routing or secondary provider. The canonical text-only JSON contains:

- `model: "gpt-6.1-sol"`;
- the fixed `AdvisoryAiInstructions.Text` and one serialized approved evidence `input` string;
- `max_output_tokens: 2048` for the Task 71 limits;
- `store: false`, `truncation: "disabled"`;
- `service_tier: "default"`, `reasoning: { "effort": "low" }`;
- `prompt_cache_options: { "mode": "explicit" }`, with no cache breakpoint.

The foreground non-streaming call omits `stream` and `background`; it does **not** serialize those two fields as false. No tools, files, metadata, previous response, conversation or hidden token-counting request is added. The provider's interpretation of omitted fields remains separate from proof of the actual outgoing bytes. Draft edits/removals are re-redacted and rebuilt before the exact UTF-8 body view. The frozen in-memory approval binds that body, provider/model/profile, source and settings/consent/key/tariff versions; persisted requests cannot grant fresh send authority.

The transport's last guard (`OpenAiResponsesTransport.cs:164–183`) verifies the exact method/absolute route, key and approved body bytes, then removes SDK-added request headers and recreates the closed application headers:

```text
Host: api.openai.com                 (HTTP authority)
Authorization: Bearer <exact key>    (masked in preview)
Accept: application/json
Content-Type: application/json
User-Agent: PCSteward-AdvisoryAI/1
Content-Length: <approved UTF-8 byte length>
```

Runtime tracing propagation, cookies and default credentials are disabled. No organization/project/SDK telemetry header is retained. TLS/host validation is not bypassed. Redirects are blocked both in the handler and before the SDK can follow a 3xx. The SDK has zero retries and a second guard attempt is refused. Overall/connect deadlines remain 45/10 seconds; compressed/oversize/error paths remain bounded, and automatic decompression is off. These facts are source/mock-transport proof, not a packet capture or proof of an uncompromised OS/proxy/provider.

### Credentials, content and output authority

The enabled setup path still requires explicit provider consent and masked entry. The store uses only the exact current-user Generic Credential target `PCSteward/OpenAI/ApiKey.v1`, not an environment-key fallback or enumeration. Key size/UTF-8 validation and exact-target write/read/delete verification are retained. Dispatch reads the key only after reservation; disconnect invalidates consent/approvals and preserves accounting and retained redacted explanations. Failed removal remains visible. Normal startup does not probe a key simply because it exists; **pending-reset/onboarding startup does**, as described in T71-01.

The key, submitted evidence/body, body fingerprints and raw provider errors do not enter normal ledger/export/log sinks. Managed immutable strings and the OS credential store remain the accepted same-user-memory residual. Disconnect is not provider revocation. No native Credential Manager action was exercised by this review; adapter tests use synthetic native seams.

Answers are bounded, redacted again and displayed as plain advisory text. They cannot change eligibility/confidence, construct jobs, invoke the helper, supply executable arguments or open model links automatically. Saved content has source-bound clear behavior, 30-day expiry, a coupled per-item bound and an 8 MiB aggregate cap. Saving failure is separate from charging: it cannot refund a valid settlement. The best-effort secret warning remains necessary, including paths/display names/unlabelled dotted numbers that cannot reliably be distinguished. The leading-compressed IPv6 and version false-positive regression cases pass in both modes.

## 7. Reservation, response and recovery

### Candidate, B1/B2 and the first request

The compiled candidate matches Task 69 §6: profile 1, context 1,050,000, maximum output 128,000, 8,192 framing tokens, byte-level input-bound flag, 192 bytes per output token, 1 MiB response envelope, standard-price input threshold 272,000, default tier, low reasoning and explicit no-cache policy. The Task 71 limits are **12,288 input / 2,048 billable output / $0.05 request / $1.00 monthly**, plus the separate cumulative $1 envelope.

| Calculation at Task 69's recorded prices | Result |
|---|---:|
| Maximum input, 12,288 × $2/M | $0.024576 |
| Maximum output including reasoning, 2,048 × $10/M | $0.020480 |
| Maximum reservation at those prices | **$0.045056** |
| Reservation with 10% uplift and upward micro-USD rounding | **$0.049562** |
| Coupled complete request allowance | 74,752 B |
| Coupled response allowance | 3,481,600 B |
| Coupled saved-item allowance | 3,932,672 B |
| Evidence-input room after fixed instructions and framing | 3,399 B |

All byte allowances are under their 4 MiB hard ceilings. The input/output total is far below the context and standard-price thresholds. These values and exact boundary/overflow/settings checks passed fresh tests. The actual preview can reserve less than this maximum because its local input upper bound can be below the configured input limit. The request workflow reserves its computed full bound, not an average answer length or promotional credit.

B1 option (a) and B2 “Use wide safety margins” remain the owner's **scoped one-request policy**, explicitly accepted in ADR-028 A2. They are not a measured tokenizer, provider framing maximum or response-size theorem. A later usage count can check the reported input against the preview bound, but a single under-bound count cannot isolate/measure framing. The 8,192-token margin and these limits are adequate for the previously accepted supervised experiment **on that accepted policy basis**; they do not establish ordinary-use support or reopen the owner's choice.

The reservation prices the approved default/no-cache request. A provider departure can cost more than the retained reservation; detecting it after the call is not a pre-call invoice ceiling. That disclosed A2 residual, project-effective price confirmation and the separate low-funded/throwaway-project backstop remain necessary. No current project tariff, prepaid balance or provider budget-enforcement guarantee was queried here. Fresh effective rates, project conditions and any permitted backstop must be checked under separate later authority; the quoted prices are the pinned packet's arithmetic inputs.

### Admission and durability

`AdvisoryAiRequestWorkflow.cs:119–151` rechecks connected state, reviewed model, matching provider adapters, current tariff and approval binding, then performs full reservation. The wall/monotonic clock watch can persist a review block before reservation. SQLite serializes the reservation with current stored versions/limits/continuity and commitments; it checks request/month/test caps, one-in-flight state, clock/freshness and ledger capacity. The cumulative test check includes settled and uncertain commitments across months and compacted rows. It is not an editable normal budget or a one-send fuse.

Key acquisition follows reservation. A durable compare-and-set dispatch claim precedes the one transport attempt (`171–187`). A failed/uncertain claim cannot send automatically. Settlement writes ignore caller cancellation so a cancelled UI cannot silently drop a liability. Restart releases only provably unclaimed rows; claimed-without-final becomes uncertain and is not replayed. Corrupt/inconsistent totals or settings block paid use rather than manufacture a zero balance. All of this remains subject to T71-01's separate destructive startup path and the disclosed external-restore limitation.

### Response outcomes and the stop procedure

| Outcome | Accounting / pause / next action |
|---|---|
| Reliable terminal complete or supported terminal-incomplete response | Settle at frozen rates using complete consistent usage, including reasoning within output; incomplete remains labelled. Safe saving is independent. |
| Timeout, cancellation, redirect, malformed/non-JSON/oversize reply, missing/inconsistent usage, refusal/error shape or model-name mismatch alone | Keep the whole reservation uncertain. **No automatic retry. No general automatic pause is promised.** Stop the test manually regardless. |
| Reported counts above the approved bound or reliable computed charge above reservation | Above-bound handling pauses; a reliable over-reservation charge is recorded in full. No automatic second request. |
| Nonzero/unreadable cache write, different/repeated/unrecognised tier, unknown usage key, cached input on this no-cache request, or unexpected background/conversation state | Usage becomes untrusted, full reservation retained and durable review pause set. Unexpected state text is not shown/saved as an answer. The real invoice may exceed that retained amount. |
| Failure to persist final settlement | Previously durable claim/liability remains; recovery treats it as uncertain rather than replaying or refunding. Stop and report. |

`AdvisoryAiResponse.cs:192–211,347–423` independently scans **every** usage/details/cache-write and service-tier occurrence, including bodies rejected for another reason. A repeated cache path with a reported counter is unreadable and pauses; repeated containers with no cache-write field only make the body untrusted/uncertain. Null tier copies count toward repetition. Only an app-owned known-tier constant can be displayed; an absent or single null tier is accepted as not reported for this test. Unknown usage keys and nonempty/unexpected conversation state are departures. Zero-input answers cannot settle for nothing. Strict-shape surprises remain a real availability possibility, accurately disclosed in the revised packet.

The owner can stop after one attempt by cancelling further work, setting per-request cap to $0 and closing the build; zero disables new paid admission without deleting prior commitments. After an ordinary timeout the software would still permit a **new separately approved** request within remaining limits, so Task 69 §10 item 6's mandatory $0 step is essential. If the UI/storage cannot save $0, close it and do not relaunch/send; no automatic request is scheduled. Nothing in the artifact auto-promotes to ordinary mode after success, reconnect, cap edit or a new month. None of these stopping instructions authorizes the first send now.

### Compaction, tariffs, migrations and physical resources

The enabled switch does not alter the reviewed AI persistence algorithms. `SqliteAdvisoryAi.cs:408–441` compacts only eligible old settled/released detail inside the caller's write transaction and savepoint. It checks a valid, unpaused ledger/accepted clock first, then proves current-month settled/outstanding, cumulative test settled/outstanding, in-flight state and all-time settled money unchanged. Failed proofs roll back the savepoint and persist review, rather than committing partial compaction (`609–636`). Unresolved liabilities remain detailed.

Migration 15's compacted proof rows retain request/response IDs, frozen tariff reference, charge, first-test flag and five usage counts. Reads recompute charges and month totals/counts from those rows; orphaned/mismatched proof rows invalidate the ledger. Response/request deduplication survives compaction. Tariff pruning removes only superseded unreferenced rates for the model/tier; it preserves rates needed by detailed/compacted proofs. Focused corruption, conservation, pruning and migration tests passed in both modes.

The 16 MiB check uses `LogicalBytes(requests,tariffs,compacted,months) = 1,024 × requests + 512 × tariffs + 512 × compacted + 64 × months`, and admits only if another 1,024 B request fits (`SqliteAdvisoryAi.cs:388–390`; `AdvisoryAiAccounting.cs:155–162`). The separate 10,000 detailed-row guard remains. This logical accounting ceiling is not the physical SQLite file/WAL/index/backup bound. Compacted IDs remain retained and can eventually exhaust capacity; refusal is the intended result, not deletion of spend.

CPU/process-tree peak working set, actual WinUI responsiveness, sustained idle disk/network, saturation and complete DB/WAL/backup physical-resource acceptance remain open for ordinary use. Arithmetic and bounded parser/storage fixtures do not prove those host measurements. Their absence alone need not prohibit the scoped supervised bounded request under A2, but it does not waive them for ordinary use. Here **T71-01 independently blocks even the supervised procedure**.

## 8. Task 71 packet and authority disposition

| Earlier Task 70 §7 item | Enabled-build review disposition |
|---|---|
| 1. B1/B2, coupled profile and effective project prices | Accurately scoped by A2 and Task 69 §§6–7/10; fresh project-effective prices/attestation remain an action-time prerequisite. No model/pricing measurement is invented. |
| 2. Exact enabled source/artifact/SDK/policy | Pinned and verified here, including compiled getters/mark, 410 files and release-path refusal. **Artifact still fails startup safety, T71-01.** |
| 3. Synthetic-only evidence and exact final body/header inspection | Task 69 §10 item 3 supplies the exact Contoso replacement, excludes other records and discloses the real interval/omission metadata. A suitable existing diagnostic record and its selection must be established under authority; do not improvise a host collection. No draft or exact send was performed here. |
| 4. Separate key approval, send approval and cumulative continuity | Preserved in the packet. Shared ledger is intentional, but the pending-reset launch path threatens its preservation before setup. Actual prior spend, installed compatibility/reset-free state and external unknown liabilities have not been audited on the host. |
| 5. Sanitized usage/cost capture | Closed counts/status/tier/reason reporting and cache missing/zero/unreadable distinctions are tested. Task 70F's model-mismatch pause wording is corrected. Do not record raw answer/error/body/hash/key. Invoice evidence remains provider-side. |
| 6. Operational one-request stop | Explicit $0 per-request save after the one attempt, whatever its result; no retry without a new task/approval. This is a supervised procedure, not an automatic one-request fuse. |
| 7. Separate ordinary-use/test-mode-exit gate | Preserved: compiled FirstLiveTest never auto-promotes. Physical resources, any missing-tier policy revision and ordinary-use provider/token evidence stay open. |

The previous privacy/accounting fixes are exercised by the fresh response, redaction, setup, ledger, explain and candidate suites in enabled mode, including repeated billing fields, tiers, unknown counters, cached input, conversation state, corruption, compaction and migration. No previously reported parser/accounting issue was independently reproduced as unfixed in this source. That does not transfer a historical disabled-build PASS onto this build's startup behavior.

The next permitted task is remediation/review planning. Before any key or one-send approval can be sought: resolve T71-01, produce and pin a new reviewed artifact, correct/disclose the remaining data scope, make folder verification explicit and establish the real compatible/reset-free/quiescent host and cumulative test continuity. Key entry and the precise final request still require **separate later owner approvals**. A report, matching hashes or passing fixtures do not authorize a launch, credential action, OS collection, paid call or release.

## 9. Verification commands, results and limits

All local tests used the pinned source and synthetic fixtures, never the installed database/key. Approved installed SDK: .NET **10.0.404**, accepted latest-patch roll-forward from global.json's 10.0.403. No SDK/dependency installation or Git identity/configuration change was made.

| Check | Result |
|---|---|
| Domain AI suite, Release, `PCStewardAiFirstLiveTest=false` | 240 passed; 0 failed/skipped |
| Application AI + FactoryReset + SqliteDatabase + current-user data-operation lease suites, Release, property false | 215 passed; 0 failed/skipped |
| Architecture AI suite, Release, property false | 19 passed; 0 failed/skipped |
| Security `OwnerOnlyCandidateBuilderTests` only, Release, property false | 5 passed; 0 failed/skipped; pure/synthetic fixture scope inspected before running |
| Domain AI suite, Release, property true | 240 passed; 0 failed/skipped |
| Same Application selection, Release, property true | 215 passed; 0 failed/skipped |
| **Total focused test executions** | **934 passed, 0 failed, 0 skipped**, across both modes; not 934 distinct test definitions |
| Canonical `pwsh -NoProfile -File .\scripts\Build.ps1`, ordinary mode | Exit 0; 0 warnings, 0 errors; final ordinary Domain has no mark |
| Synthetic enabled-mode startup reset reproduction | Exit 0; reproduced the destructive reset-core effect and onboarding credential callback, as recorded in T71-01 |
| Environment / Directory.Build.rsp property probes | Both demonstrated ambient enablement; no tracked build file changed |
| Actual extracted candidate mark function | True on downloaded Domain; ordinary Domain mark absent; actual candidate/release/signing pipeline was not executed |
| Artifact checks repeated after verification | ZIP/manifest unchanged; all 410 app file sizes/hashes match; no helper or extra app file |

Representative commands, with the existing approved local restore completed as needed:

```powershell
dotnet test tests/PCSteward.Domain.UnitTests/PCSteward.Domain.UnitTests.csproj -c Release --no-restore -p:PCStewardAiFirstLiveTest=false --filter FullyQualifiedName~AdvisoryAi
dotnet test tests/PCSteward.Domain.UnitTests/PCSteward.Domain.UnitTests.csproj -c Release --no-restore -p:PCStewardAiFirstLiveTest=true --filter FullyQualifiedName~AdvisoryAi
dotnet test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj -c Release --no-restore -p:PCStewardAiFirstLiveTest=false --filter 'FullyQualifiedName~AdvisoryAi|FullyQualifiedName~FactoryResetTests|FullyQualifiedName~SqliteDatabaseTests|FullyQualifiedName~CurrentUserDataOperationLeaseTests'
dotnet test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj -c Release --no-restore -p:PCStewardAiFirstLiveTest=true --filter 'FullyQualifiedName~AdvisoryAi|FullyQualifiedName~FactoryResetTests|FullyQualifiedName~SqliteDatabaseTests|FullyQualifiedName~CurrentUserDataOperationLeaseTests'
dotnet test tests/PCSteward.ArchitectureTests/PCSteward.ArchitectureTests.csproj -c Release --no-restore -p:PCStewardAiFirstLiveTest=false --filter FullyQualifiedName~AdvisoryAi
dotnet test tests/PCSteward.Security.FixtureTests/PCSteward.Security.FixtureTests.csproj -c Release --no-restore -p:PCStewardAiFirstLiveTest=false --filter FullyQualifiedName~OwnerOnlyCandidateBuilderTests
pwsh -NoProfile -File .\scripts\Build.ps1
git diff --name-status 97699d938eb8f26c7bf43311df10faa36239c275 293a07fcd493258a1dd02954c698a137cee654ba
```

For the canonical build, `PCStewardAiFirstLiveTest=false` was set only in the child process environment and the previous value restored afterwards. A restricted-sandbox build returned exit 1 with zero compiler diagnostics; the same unchanged canonical build on the normal approved host succeeded. An isolated verification-project restore similarly succeeded on the normal host after a sandbox-only failure. These environment failures are not reported as compiler/test failures or fixed by weakening product checks.

**The canonical full test suite was not run.** The explicit task says **no signing**. Full Security fixtures include real `New-SelfSignedCertificate` into `Cert:\CurrentUser\My` and `Set-AuthenticodeSignature` (`Fixtures/ReleaseSigningScripts.ps1:28,40`; also `PinnedReleaseSignatureTests.cs:222,225`). Running them would violate that boundary. The five selected candidate-builder fixtures use inspected synthetic payloads/pure functions and do not sign/install. This is a stated verification limit, not a full-suite PASS or permission to sign. No new product tests were added because this task permits only the report.

The local report is the only new deliverable; earlier untracked review reports were preserved. Downloads, logs and isolated verification scratch live under the ignored `artifacts/task71-review` directory, outside product code and release output. No STATUS entry, commit, PR or publication was made, as explicitly requested for this task.

## 10. Final gate

Software evidence establishes the exact downloaded artifact, enabled compiled policy, bounded one-attempt advisory pipeline, retained uncertainty, cumulative test admission and current release-path refusal. It also establishes the unguarded shared reset recovery at startup. Provider tokenizer/framing/caching/tier behavior, current project prices, invoice finality, actual host state and UI/resource measurements remain separate evidence; none was inferred from these fixtures.

**FAIL — the enabled test build at commit `97699d938eb8f26c7bf43311df10faa36239c275`, artifact SHA-256 `ad48ca878f11f4a1430abd78dc328a3df096a177e40a12eb42897316eddcc1e9`, model `gpt-6.1-sol` at the `default` tier, route `POST https://api.openai.com/v1/responses`, and the one-request Task 71 procedure.** T71-01 is an unresolved continuity/shared-state risk. This report clears neither the key step nor the send step, and authorizes no launch, release or ordinary use.
