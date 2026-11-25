# Four-model project review (2026-11-25)

**Target.** `phase/4-beta` at `07e27e8` (release 0.1.19). Read-only: no code, test or setting was changed and nothing is committed.

**Verdict.** No Critical finding. Two High findings, both ordinary bugs that need no attacker: whole-PC WinGet updates
depend on Chocolatey being installed (F-01), and every per-user registry app is labelled 64-bit (F-02). Three Medium and
sixteen Low findings follow. The deletion, reset, helper-pipe and paid-request paths were read and nothing new was found in
them.

## Fixes made on 2026-11-25

Fixed on branch `claude/four-model-review-fixes`, each by one model and reviewed by another: F-01 and F-03 (Opus; reviewed
by Astra), F-04 and F-06 (Gemini; reviewed by Opus, with two follow-ups applied), F-05, F-14 and F-15 (Astra; reviewed by
Gemini), F-09 and F-13 (coordinator; reviewed by Astra, whose deadline fault Astra then fixed). F-02 is left unfixed by owner
decision. Every other finding below is still open. See `docs/STATUS.md` for what each fix does and does not cover.

## How it was run

| Reviewer | Settings | First pass | Second pass |
|---|---|---|---|
| Claude Fable 5.2 (coordinator) | medium effort | Application layer, scheduled host, baseline build and tests | Checked every High, Medium and partly confirmed finding against the code |
| Claude Opus 5.5 | high effort | Update execution, elevation, deletion, self-update, scheduling (IDs `OP-`) | None |
| ChatGPT Astra 6.2 | high reasoning effort | Advisory AI, SQLite, release scripts and workflows (IDs `AS-`) | Tried to refute the `GN-` and `OP-` findings |
| Gemini 4 Neon | high thought level | Providers, domain rules, diagnostics, desktop UI (IDs `GN-`) | Tried to refute the `AS-` and `FB-` findings |

Each first pass was independent, with no sight of the others. In the second pass each finding went to a different model
with the instruction to prove it wrong. "Confirmed by" below names who agreed after reading the code. `FB-` are the
coordinator's own findings.

**Limits.** Opus read about half its slice; the parts not read are listed at the end. Astra and Gemini also list files
they did not finish. All findings come from reading the code, except the two SQLite behaviours in F-05 and F-14, which
were reproduced in an in-memory SQLite database.

## Baseline

- Build: succeeded, 0 warnings, 0 errors.
- Tests: 4,815 passed, 1 failed, 4 skipped (Domain 885, Architecture 277, Application integration 1,838, Security
  fixtures 1,815).
- The one failure, `RedeployToolingFunctionsHoldTheirContracts`
  (`tests/PCSteward.Security.FixtureTests/RedeployToolingScriptTests.cs:305`), is environmental and the owner accepted it
  on 2026-11-25. PowerShell 7.7.0 was installed for this review by WinGet as an app package, so a `pwsh` process has a
  Windows package identity and the test expects none. See F-21.

## The owner's rule and the exceptions documents

- **Rule (owner, restated 2026-11-25):** something that needs the PC already compromised is not counted as a finding.
  Earlier wording is in `SECURITY_REVIEW_2026-11-13.md` and `MYTHOS_WHOLE_APP_REVIEW_2026-11-21.md`.
- **Dropped under the rule:**
  - `OP-7`: one `WinVerifyTrust` import lacks the System32 search rule
    (`src/PCSteward.Execution/WindowsElevatedHelperTransport.cs:682`). It matters only to someone who can already write to
    the protected install folder.
  - `AS-2`: the publish script does not re-check the final asset list before publishing
    (`scripts/Publish-PCStewardRelease.ps1:233`). It needs GitHub write access to the repository (same class as the
    accepted MY-02 and the `TM-19` residual in ADR-032), and the updater refuses any release without exactly the two
    expected, verified assets. Kept only as optional hardening H-1.
- **Recorded decision, not a finding:** `OP-13`. Uninstalling after a failed post-install check is ADR-032's rule.
- **Not reachable today, not counted:** `GN-10` (a Preview candidate against an unknown installed channel; WinGet
  compatibility maps every channel to Unknown, which already blocks it) and `OP-6` (ledger paths differing only in case;
  nothing writes such rows).
- **Exceptions documents read:** `TASK_90_OWNER_EXCEPTIONS.md`, `TASK_31_NONPUBLIC_EVALUATION_EXCEPTIONS.md` and
  `PHASE_3_HOST_TEST_EXCEPTION.md`. They cover unproven performance, signing and distribution, reset or lifecycle field
  tests and manual UI evidence, for exact owner-only candidates. No finding below re-raises any of them. F-08 and F-11
  touch reset code but are code faults, not missing field evidence.
- **Earlier accepted items read:** Codex scan #2, #4, #5 and #10 (`CODEX_SECURITY_SCAN_2026-11-12.md`), L1 to L3 and M1
  in `SECURITY_REVIEW_2026-11-13.md`, and MY-06 in the Mythos review. None matches a finding below. The by-name process
  matching in "Close it for me" (L1) was seen again and is not re-raised.
- **Design records checked for the two High findings:** nothing records the Chocolatey check as intended on the WinGet
  route; `TASK_50_QUALIFICATION_PROBE_SPEC.md:191` calls it "a pre-launch gate of a real Chocolatey update". Nothing
  records a decision on per-user architecture; `PHASE_D4B_32BIT_REGISTERED_APPS_DESIGN.md` puts per-user installs out of
  scope.
- **Not searched:** the full text of every ADR and review record for other accepted items.

## Findings

### High

**F-01 (`OP-1`). Whole-PC WinGet updates are refused before the UAC prompt unless Chocolatey is installed and trusted.**
`FR-UPD-01`, `FR-UPD-15`. Raised by Opus; confirmed by Astra and the coordinator.
- `WingetMachineUpdateComposition.cs:15` builds the one production transport with its default constructor.
  `ReviewedHelperProtocol2Transport.cs:215-216` binds its caller-trust check to `ChocolateyCallerTrusted`, and line 238
  applies it to every route, including `WinGetMachineSilentV1`. The check throws when
  `%ProgramData%\chocolatey\bin\choco.exe` is missing (`ChocolateyProvider.cs:246`).
- Result: on a PC without Chocolatey the update is refused with no UAC prompt, then recorded as in F-03.
- Why it was missed: the owner's PC has Chocolatey, and no test builds the transport through the default constructor.
- Fix: make the check depend on the route, and add a test that the WinGet route never consults Chocolatey. The comments at
  lines 186 and 200 are out of date.

**F-02 (`GN-1`). Every per-user registry app is recorded as 64-bit.** AGENTS.md architecture-conflict rule, `FR-UPD-01`.
Raised by Gemini; confirmed by Astra and the coordinator.
- `RegistryInventoryProvider.cs:117` gives "x86" only to the 32-bit machine view. The current-user uninstall key is shared
  by 32-bit and 64-bit apps, so a 32-bit per-user app becomes "x64".
- `ProductPackageMatching.cs:286-303` asks for independent architecture proof only for machine entries in the 32-bit view.
  A 32-bit per-user app can therefore be offered an x64 update, with `--architecture x64` in the reviewed command
  (`WingetReviewedUpdateArguments.cs:49`), or be wrongly blocked against an x86 one.
- Not verified: whether WinGet or the vendor installer would complete the switch on a real PC.
- **Owner decision (2026-11-25): left unfixed.** Recording per-user architecture as Unknown would stop every per-user
  registry update and change those apps' stable keys, because the label is part of the key
  (`RegistryLocalInventoryProvider.cs:22`). A real fix needs a designed proof of installed architecture, as the 32-bit
  machine view has. The case is a 32-bit app installed per-user on 64-bit Windows, not a 32-bit PC.

### Medium

**F-03 (`OP-2`). A refusal before the helper starts is recorded as "Result not verified".** `FR-UPD-14`. Raised by Opus;
confirmed by Astra.
- `ElevatedUpdateRunner.cs:57` marks elevation as requested before the exchange, which sets `InvocationPossible`
  (`UpdateProgress.cs:182`). `UpdateBatchWorkflow.cs:349-353` then turns the failure into `VerificationRequired`.
- The user is told the update may have started when it never ran. A real UAC decline keeps its own result. It never
  produces a false Success.

**F-04 (`GN-2`). Packages with `+` in their ID are shown but can't be updated.** `FR-UPD-01`. Raised by Gemini; confirmed
by Astra and the coordinator.
- The WinGet parsers accept `+` (`WingetInstalledInventoryProvider.cs:440`); the job rule does not
  (`UpdateJobs.cs:103`). `Notepad++.Notepad++` and `Microsoft.VCRedist.2015+.x64` end at "The source evidence contained
  an unsupported value" (`InventoryWorkflow.cs:612-619`). It fails safe.
- No recorded decision excludes `+`. The Chocolatey `_` part of the original claim was refuted.

**F-06 (`GN-3`). Two rows with the same ID in `winget upgrade` output fail that source's whole refresh.** `FR-UPD-01`.
Raised by Gemini; confirmed by Astra.
- `WingetInstalledInventoryProvider.cs:296` builds a dictionary with no de-duplication. The source falls back to its cache
  or shows as unavailable; other sources continue.
- The duplicate-ID warning at line 316 can never fire, because line 300 already kept only single entries.
- Not verified: how often real WinGet output repeats an ID.

### Low

**F-05 (`AS-3`, `AS-4`). Update history is never pruned, and one kind of event repeats without limit.** Raised by Astra as
Medium; Gemini and the coordinator rate it Low, because nothing calls the pruning and reads are capped. SQLite behaviour
reproduced. `FR-HIS-04` asks for retention of verbose logs and source caches, so it only partly applies.
- `PruneAsync` (`SqliteDatabase.cs:516`) has no production caller, so retention never runs.
- If it were called, its per-job cap still would not work: the limit is bound as text (`SqliteDatabase.cs:594`), and
  `row_number > '2'` deleted 0 rows where an integer bind deleted 1.
- The test at `SqliteDatabaseTests.cs:368` passes only because a different delete removes one row first.
- `RecoverInterruptedAsync` (`SqliteDatabase.cs:500-510`) adds a new "recovered after interruption" event for every job
  still in `VerificationRequired` on every later update run, and moves its time forward. F-03 is one way a job gets there: its status is written to the same table (`SqliteDatabase.cs:488`).
- Reads are capped at 500 or 256 rows, so the effect is a slowly growing table and misleading repeated entries.

**F-07 (`GN-6`). Some desktop actions are not held back while other work runs.** Raised by Gemini and
kept at Medium by Astra; the coordinator rates it Low, because the app closes as soon as the reset is armed, which ends
the collection or scan.
- Factory reset can be armed during diagnostics collection: neither the button's guard (`MainWindow.cs:333-334`) nor the
  final check (`MainWindow.DataAndReset.cs:135`) covers it.
- Closing the window cancels neither a scan nor a diagnostics collection, and a finishing scan still renders
  (`MainWindow.Operations.cs:112-114`).
- Refuted or covered: source refresh and connector changes take the data-operation lease, and a late Chocolatey
  "Check again" checks for cancellation before it renders.


| # | Source | Finding | Where | Confirmed by |
|---|---|---|---|---|
| F-08 | `OP-3` | `schtasks.exe` runs with no time limit, and reset recovery at start-up waits for it while holding the data-operation mutex | `ScheduledCheckRegistration.cs:56-73`, `FactoryReset.cs:127` | Astra |
| F-09 | `OP-4`, `FB-1` | A scheduled check that times out or throws saves no status and never raises the failure count, so the repeated-failure notice (`FR-SCH-04`) can't fire | `ScheduledHost/Program.cs:39-90` | Opus and coordinator independently; Gemini |
| F-10 | `OP-5` | Download registration and retention have no production caller, so the quota and retention never run. A self-update download takes no data-operation lease, so "Clear downloaded files" can remove its committed files | `DownloadedArtifactCache.cs:46`, `:66`; `MainWindow.SelfUpdate.cs:187` | Astra (partly) |
| F-11 | `AS-5` | The Clear scan data preview leaves out saved AI explanations that the clear deletes, so it can show 0 items | `SqliteDatabase.cs:365`, `:383` | Gemini |
| F-12 | `AS-1` | An IPv6 address with six two-digit groups in a row is caught by the hardware-address rule first, leaving two groups unredacted. Rare in practice | `AdvisoryAiRedaction.cs:286-288` | Gemini, coordinator |
| F-13 | `FB-2` | Every JSON export has an empty update history and says "Not collected until Phase 3." (`FR-DAT-02`) | `InventoryExportService.cs:45-46` | Gemini |
| F-14 | `AS-6` | The "needs connector-specific version comparison" flag is stored as the text `True` and reads back as false. No provider sets it today | `SqliteDatabase.cs:311`, `:321` | Gemini; reproduced |
| F-15 | Gemini, second pass | Exit codes are written with the PC's regional number format. Where the minus sign is U+2212 (for example Swedish or Finnish), a negative exit code reads back as 0 or empty | `SqliteDatabase.cs:487-488`, `:594` | Not cross-checked |
| F-16 | `GN-4` | A source with no linked installed app would make `winget list` unreadable and the source unavailable. Real WinGet output for that state is not recorded | `WingetInstalledInventoryProvider.cs:179`, `:283-289` | Astra (partly) |
| F-17 | `GN-5` | WinGet output is read without setting UTF-8, so a non-ASCII name could shift the fixed columns and fail the source. No capture proves it happens | `WingetInstalledInventoryProvider.cs:184-210`, `:514-531` | Astra (partly) |
| F-18 | `GN-7` | One post-update check still compares versions as exact text, so `2.104.0.0` against `2.104.0` can end as "Installed, not verified" when an MSI changes its product code | `WingetPostUpdateRegistryLink.cs:73` | Astra (partly) |
| F-19 | `GN-9` | Pinned Chocolatey packages are listed as update candidates. The reviewed route still refuses them | `ChocolateyProvider.cs:140-145` | Astra |
| F-20 | `GN-8` | The WinGet and Chocolatey process wrappers can throw if the process exits just before it is killed, and then wait without limit for its output | `WingetInstalledInventoryProvider.cs:527-556`, `ChocolateyProvider.cs:199-222` | Not cross-checked |
| F-21 | Coordinator | Without PowerShell 7, three script tests fail while others skip. With it installed as an app package, one test fails because it expects a process with no package identity (code 15700) | `ReleaseSigningScriptTests.cs:91`, `RedeployToolingScriptTests.cs:305` | Observed |

## Coordinator's review of the partly confirmed findings

Read again at the owner's request. Each line gives what the code shows.

- **F-04 (`GN-2`):** stands at Medium. The rule at `UpdateJobs.cs:103` has no `+`, and the catch at
  `InventoryWorkflow.cs:616` gives the message.
- **F-05 (`AS-3`):** mechanism right; Low. `PruneAsync` has no caller in `src`.
- **F-07 (`GN-6`):** two parts right. The reset check at `MainWindow.DataAndReset.cs:135` leaves out diagnostics
  collection, and the `Closed` handler at `MainWindow.cs:169` cancels only the review preparation. Low.
- **F-10 (`OP-5`):** right that `RegisterAsync` and `EnforceRetentionAndQuotaAsync` have no caller in `src`. The
  self-update overlap was not re-read; it rests on Astra's reading.
- **F-16 (`GN-4`):** the code path is right: `ParseTable` (`WingetInstalledInventoryProvider.cs:179`) has no single-line
  case, and a non-zero exit is fatal too. Whether a real PC reaches it is still unknown. Low.
- **F-17 (`GN-5`):** neither process start sets an output encoding (lines 514 and 530). The desktop app has no console,
  and to the coordinator's understanding .NET then reads redirected output as UTF-8, so only a console-hosted run could
  be affected. Not verified. Low, possibly nothing.
- **F-18 (`GN-7`):** right: `WingetPostUpdateRegistryLink.cs:73` compares with exact text. It matters only when the link
  is the deciding evidence. Low.
- **H-1 (`AS-2`):** order of operations right; dropped as a finding under the owner's rule.

## Improvements

- **H-1 (`AS-2`).** Re-check the final draft asset list (names, count, digests) just before publishing.
- **H-2 (`AS-7`).** Fitting a long diagnostic timeline recounts the remaining records on every step
  (`AdvisoryAiEvidence.cs:160`); count once and decrement.
- **H-3 (`OP-12`).** Either wire download registration and retention, or delete the unused code and its tests.
- **H-4 (`GN-11`, `GN-12`).** `WuaHistorySource` handles only access-denied COM errors; `BoundedScanEventSink` strips only
  line breaks; two installed App Installer versions are ordered as text, not as versions
  (`WingetInstalledInventoryProvider.cs:605`).
- **H-5 (`GN-13`).** Tests to add: `+` IDs through `UpdateJobSelection`, duplicate upgrade IDs, the no-match `winget list`
  output, per-user architecture, three terminal events through `PruneAsync`.
- **H-6.** `AGENTS.md` names `pwsh` for the canonical commands; say which PowerShell 7 install the tests expect. An MSI
  install was not tried here.

## Areas read with nothing found

- Opus: update execution and result recording (Success only after the post-update inventory finds the version), the
  protocol-2 pipe set-up and peer checks, the helper bootstrap, deletion and factory reset (reparse-point, mount-point and
  hard-link checks), `GitHubReleaseClient`, `ReleaseVersion`.
- Astra: no new way to send a paid request twice or under-count a reservation; all four workflows pin actions to full
  commit hashes; migration ordering.
- Gemini: no `Win32_Product` anywhere; MSIX inventory, event-log and reliability readers, `VersionComparison`, the WinGet
  argument builders and source guard.

## Not reviewed

- Execution: `ReviewedHelperEndpoint.cs` after about line 330, most of `ReviewedUpdateProtocol2Interpreter`,
  `ReviewedUpdateWireCodec`, the self-update helper protocol and install policy, the MSI digest and structure checks,
  `DeploymentCapabilityPolicy`, most of `WindowsSelfUpdateHost`.
- Application: `ReviewedUpdateDispatchWorkflow`, `ReviewedUpdateExecutionWorkflow`, `UpdateDispatch*`.
- Storage: `CurrentUserOperationGate`, `CurrentUserDataOperationLease`.
- Chocolatey: the native launch, token, tree-walker and watchdog files.
- Desktop: `StewardPresentation.cs` and the XAML resources.
- Scripts and tests: not every PowerShell script or test fixture was read line by line.

## Suggested order

1. F-01, then F-03 with it (same path).
2. F-02.
3. F-04 and F-06 (both stop ordinary updates).
4. F-05, with F-14 and F-15 (one helper, `SqliteDatabase.cs:594`, binds everything as text).
5. F-07 and F-09.
6. The remaining Low findings as convenient.

`docs/STATUS.md` and `docs/requirements/TRACEABILITY.md` were not updated: this review changed nothing they track.
