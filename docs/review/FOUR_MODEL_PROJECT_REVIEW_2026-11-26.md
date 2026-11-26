# Four-model project review (2026-11-26)

**Target.** Branch `claude/four-model-review-fixes` at `d2f10cc`. This is the code of release 0.1.20: tag `v0.1.20`
differs from it only by the version number and a STATUS entry. `origin/phase/4-beta` has one later commit (`ac5e1d9`, a
test-only change) that was not reviewed.
The review itself was read-only: `git status` after it showed this file as the only change. Fixes were made afterwards
at the owner's request (next section).

**Verdict.** No Critical or High finding. Three Medium findings, all ordinary bugs that need no attacker: healthy cached
sources are shown as "Offline" after every reload until the next check (F-22), a timed-out self-update install is reported as having kept the
old version (F-23), and a scheduled check never notifies about updates that are ready (F-24). Eight Low findings follow.
The nine fixes made on 2026-11-25 were re-read by a model that did not write them; no regression was found, and one fix
(F-15) is incomplete for rows that older builds already stored (F-29).

Findings are numbered on from the 2026-11-25 review (F-01 to F-21), which this review does not repeat.

## Fixes made on 2026-11-26

Fixed on branch `claude/panel-review-1126-fixes` (from `origin/phase/4-beta` at `c30d892`) by the coordinator: F-22,
F-23, F-24 (owner decision: count ready updates and show the counts), F-25, F-29, F-30, F-31 (the prompt only) and F-32.
**No second model has reviewed these fixes.** Left open: F-26, F-27, F-28 and the second half of F-31. See
`docs/STATUS.md` for what each fix does and does not cover. Three limits worth knowing:

- F-22: a failed check is not saved, so a reload after it shows the last good result and its age without a warning.
- F-23: only the wording and the result code changed. PC Steward still reopens after an install with no observed exit,
  because ADR-032 §7a lists that outcome; changing it is an owner decision on the ADR.
- F-25 and F-31 have no tests (no seam to fake a failing package; a one-line desktop mapping).

## How it was run

| Reviewer | Settings | First pass | Second pass |
|---|---|---|---|
| Claude Fable 5.2 (coordinator) | medium effort | WinGet per-user installer check, exported-manifest reader, process wrapper, version comparison, update intelligence, update history presentation, running-app detection and close; baseline build and tests | Checked every Medium finding and the refuted one against the code |
| Claude Opus 5.5 | high effort | Fix commits `36d89f6` and `16b35fa`; SQLite; storage gate and lease; paid-request path (IDs `OP-`) | Tried to refute `GN-3` to `GN-6` |
| ChatGPT Astra 6.2 | high reasoning effort | Fix commits `ed6bb5d` and `fdcff0a`; helper endpoint, protocol-2 interpreter, wire codec, self-update, MSI checks, dispatch workflows (IDs `AS-`) | Tried to refute `GN-1`, `GN-2`, `OP-1` |
| Gemini 4 Neon | high thought level | Fix commit `d2f10cc`; Chocolatey native files; scheduling and scheduled host; desktop presentation and XAML; diagnostics and inventory readers (IDs `GN-`) | Tried to refute `AS-1` to `AS-4` |

The slices were rotated so no model reread its 2026-11-25 area, and they started from that review's "Not reviewed" list.
Each first pass was independent. In the second pass each finding went to a different model with the instruction to prove
it wrong. "Confirmed by" names who agreed after reading the code.

**Limits.** All findings come from reading the code, except: the legacy exit-code read in F-29 (reproduced by Astra with
the pinned `Microsoft.Data.Sqlite` 10.0.14 in memory) and the refutation of `GN-2` (Astra's probe on a real token). No
installer, scheduled task or desktop UI was run. The coordinator's own first pass found nothing. Unread files are listed
at the end.

## Baseline

- Build: succeeded, 0 warnings, 0 errors.
- Tests: 4,872 passed, 1 failed, 4 skipped (Domain 900, Architecture 279, Application integration 1,864 with 1 skipped,
  Security fixtures 1,829 with 3 skipped).
- The one failure is `RedeployToolingFunctionsHoldTheirContracts`, the environmental failure the owner accepted on
  2026-11-25 (F-21).

## Dropped or refuted

- **`GN-2`, refuted.** Claim: a stale Windows error code 122 makes `NativeTokenReader.TryReadFacts` reject a valid token
  (`NativeTokenReader.cs:32-33`). The project targets .NET 10 and `IsTokenRestricted` is declared with
  `SetLastError = true` (`ProtectedNativeMethods.cs:179`), so the runtime clears the last error before the call. Astra
  seeded the error with 122 and read back 0 on a real token. Gemini's own check used a runtime without that behaviour.
- **Part of `GN-4`, refuted.** The scheduled MSIX scan leaving out non-application packages is deliberate and pinned by
  `MsixInventoryIdentityBoundaryTests.cs:31-38`.
- **Nothing was dropped under the owner's rule** (needs an already-compromised PC): no such finding was raised.
- **Exceptions documents:** `TASK_90_OWNER_EXCEPTIONS.md`, `TASK_31_NONPUBLIC_EVALUATION_EXCEPTIONS.md` and
  `PHASE_3_HOST_TEST_EXCEPTION.md` were read by Gemini and skimmed by Opus. No finding below re-raises them. For the
  self-update findings Gemini also checked ADR-032, its security review, the Mythos review and the 2026-11-13 security
  review; none records these behaviours as accepted. The clean-up order behind F-28 is intended (Mythos S4).

## Findings

### Medium

**F-22 (`GN-1`). After any reload of saved results, healthy update sources are shown as "Offline: an update source
couldn't be reached".** `FR-UI-05`. Raised by Gemini as High; confirmed by Astra and the coordinator at Medium.
- `MainWindow.cs:212` loads saved results through `LoadUpdateIntelligenceAsync` (`InventoryWorkflow.cs:197`), which asks
  for a refresh with `online: false`. `UpdateSourceRefreshService.cs:37` and `:64` then add the warning `source:offline`
  to every cached snapshot, though nothing was contacted.
- `HomePresentation.cs:113` turns that warning into the Offline state, ahead of the "recent success counts as Live" rule
  on line 114, which a reload can therefore never reach. Home and Updates show it in attention colour; Settings shows
  the same sentence.
- The app detail page adds "(couldn't be reached since)" to every source that is not Live and has a previous success
  (`AppDetailSummaryPresentation.cs:240`).
- When: app start, and after a pin, ignore, skip, undo, manual match, scan or finished update. It lasts until the next
  online check, which only runs when the owner asks for one (the Check button or F5; nothing starts one at launch). It
  affects wording only; no update decision changes.
- Not verified: in the running app.
- The tests pin the pieces separately (`UpdateSourceRefreshServiceTests.cs:14`,
  `HomeActivityDecisionsPresentationTests.cs:92`) but none covers a reload after a successful check.
- Fix: a load that contacts nothing should return the cached snapshot without `source:offline`; say "couldn't be reached"
  only when a refresh was tried and failed.

**F-23 (`AS-1`). A self-update install that runs past its 30-minute limit is reported as "Windows could not install …
and kept …", and the app reopens.** `FR-UPD-14`, ADR-032 §7–8. Raised by Astra as High; confirmed by Gemini and the
coordinator at Medium.
- `WindowsSelfUpdateHost.cs:280-284`: on the limit it tries to kill `msiexec`, ignores a failed kill, does not wait for
  exit and returns null. `SelfUpdateFinish.cs:181` records that as `InstallFailed`.
- `SelfUpdateDesktop.cs:212-213` treats `InstallFailed` as final without checking the running version or the installed
  files. `SelfUpdateText.cs:112` states the old version was kept, and `SelfUpdateReopen.cs:112` reopens the app.
- Nothing on that path shows the install rolled back or stopped. Leaving the method also restores the per-user install
  policy while Windows Installer may still be working.
- Medium, not High: the limit is 30 minutes for a small MSI with no custom actions, so it should be rare, and a later
  update still checks the installed release.
- Not verified: what Windows Installer does when its client is killed mid-upgrade.
- Fix: record an "unknown" outcome when the exit was not observed, don't reopen automatically for it, and check the
  installed state before naming a version.

**F-24 (`GN-3`, part a). A scheduled check never notifies about updates that are ready to install.** `FR-SCH-04`,
`UJ-09`. Raised by Gemini; confirmed by Opus and the coordinator.
- `ScheduledHost/Program.cs:83` takes "attention" only from `AttentionNeeded`, which counts `ReviewRequired` and
  `FailedUnavailable` (`UpdateIntelligenceModels.cs:107`). `VerifiedUpdate` is not counted, so a check that finds only
  ready updates saves "Completed" and shows no notification.
- The product spec's UJ-09 step 3 (`PC_STEWARD_PRODUCT_SPEC.md:391`) says the notification shows "the number of verified
  and review-required updates". The toast text has no counts either (`Program.cs:90`).
- No recorded decision against this was found in STATUS, ADR-020, the exceptions documents or the 2026-11-25 review.
  `ScheduledChecksTests.cs:32-35` tests only the policy's booleans.
- Rating: Opus put it between Low and Medium; the coordinator rates it Medium because the spec names verified updates.
- Owner decision needed if this is intended: otherwise count verified updates as attention and put the counts in the
  toast.

### Low

Ratings that changed: F-25, F-26, F-27 and F-28 were raised as Medium. The second-pass reader put F-25 between Low and
Medium and the others at Low; the coordinator rates all four Low. Gemini rates F-32 as information only.

| # | Source | Finding | Where | Confirmed by |
|---|---|---|---|---|
| F-25 | `GN-4` | The scheduled MSIX scan reads each package with no per-package error handling, unlike the desktop's. If one package throws, the scan is still saved as completed and every MSIX app is missing from the saved inventory until the next good scan, because saving replaces all rows. Not verified: that a real package state makes `InstalledLocation` throw | `ScheduledInventoryProviders.cs:41-52`, `InventoryScanOrchestrator.cs:60-69`, `SqliteDatabase.cs:130` | Opus (partly) |
| F-26 | `GN-3` part b | When the local scan works but an online source keeps failing and older cached results exist, the scheduled check saves "Completed" with a failure count of 0. With no cache the affected apps become "unavailable" and a notification does fire, so the quiet case is narrow. FR-SCH-04 speaks of a scan failing, and the scan did succeed | `ScheduledHost/Program.cs:87-88`, `UpdateSourceRefreshService.cs:54-55` | Opus (partly) |
| F-27 | `AS-2` | The restart check after MSI exit 3010 compares estimated boot times with a 120-second margin. A forward clock correction of more than that in the same boot passes it. If the files already match, the app says Updated before the restart (true for the install folder); if they don't, it reports a blocking failure instead | `WindowsSelfUpdateHost.cs:818`, `SelfUpdateDesktop.cs:77`, `:204-211` | Gemini (partly); coordinator read the code |
| F-28 | `AS-3` | Each self-update attempt copies the ZIP and MSI into a new folder under Program Files before the helper confirms with GitHub. A failed confirmation leaves that folder, and app start, uninstall and factory reset don't remove it. Each retry needs a new check, download and UAC approval, so growth is slow | `PcStewardSelfUpdate.cs:155`, `SelfUpdateSession.cs:276-322` | Gemini (partly) |
| F-29 | `OP-1` | F-15's fix covers new writes only. A negative exit code stored by 0.1.19 or earlier on a PC whose minus sign is U+2212 is text in the database. The app detail timeline reads it as 0 ("exit code 0"); Activity reads it as "None recorded". Not verified: that any real database holds such a row | `SqliteDatabase.cs:521`, `SqliteActivityHistory.cs:73` | Astra; reproduced |
| F-30 | `GN-5` | The "update not applicable" outcome has an empty glyph where every neighbouring outcome has an icon, so its icon slot is blank. New in `a8c1545` | `UpdateJobPresentation.cs:693` | Opus (checked the bytes); coordinator |
| F-31 | `GN-6` | A Chocolatey match that needs review shows the raw source ID `chocolatey-community` in the confirmation prompt. Opus corrected the second half: the manual-match source list is fixed to WinGet and Microsoft Store, so for a Chocolatey app it silently preselects WinGet. Not verified: that a Chocolatey match reaches the prompt | `StewardPresentation.cs:103-112`, `MainWindow.Review.cs:46`, `MainWindow.Detail.cs:66-74` | Opus (partly) |
| F-32 | `AS-4` | When the running build is newer than the published release, the check says "You have the newest published version (older version)". No download is offered, which is right; only the wording is off | `GitHubReleaseClient.cs:155`, `SelfUpdateText.cs:41` | Gemini; coordinator |

## Review of the 2026-11-25 fixes

No model reviewed its own fix. Only `36d89f6` (Opus) and `d2f10cc` (Gemini) got a reader who had not seen the change
before: Astra had already reviewed `ed6bb5d` and the F-09/F-13 fix on 2026-11-25, and Opus had already reviewed
`16b35fa`. Those three are second reads by the same model, not independent ones.

- **`ed6bb5d` (F-01, F-03), read by Astra:** no regression. The tests would fail on the old code: one counts Chocolatey
  checks on the WinGet route, another runs the real batch coordinator and reads its history.
- **`16b35fa` (F-04, F-06), read by Opus:** no regression. The `+` rule applies only to WinGet and Store IDs and matches
  the parser and manifest rules; a duplicated ID drops only that package.
- **`36d89f6` (F-05, F-14, F-15), read by Opus:** correct for new writes; every caller of the changed bind helper was
  checked. Incomplete for old rows: see F-29.
- **`fdcff0a` (F-09, F-13), read by Astra:** no regression. The host-level test gap was already disclosed.
- **`d2f10cc` (follow-ups), read by Gemini and Opus:** no regression. There is no test for an unparseable flag value.

## Still open from 2026-11-25

F-02 (owner decision: left unfixed), F-07, F-08, F-10 to F-12, F-16 to F-21, and improvements H-1 to H-6. Not re-reviewed
and not counted above.

## Areas read with nothing found

- Coordinator: the WinGet per-user installer rule and query, the exported-manifest reader, `OwnedProcessWork`, restart
  exit codes, the post-update registry link, `VersionComparison`, `UpdateIntelligence`, `ReviewedUpdateHistory`,
  running-app detection and "Close it for me".
- Opus: SQLite migrations 1–18 and the stores; `CurrentUserOperationGate`, `CurrentUserDataOperationLease`,
  `DataOperationLease`; the paid-request ledger, workflow and `OpenAiResponsesTransport` (nothing that sends a request
  twice or leaves one uncounted).
- Astra: helper frame correlation, route-specific reply validation, MSI table comparison, deployment capability checks,
  reviewed dispatch settlement.
- Gemini: the Chocolatey launch job, protected tree walker, client-update watchdog, profile observer and route probe;
  `ScheduledCheckRegistration`; every XAML resource key used by `StewardPresentation.cs`; the diagnostics and inventory
  readers (no `Win32_Product`).

## Not reviewed

- Advisory AI: every Domain and Application `AdvisoryAi*` file except `AdvisoryAiRequestWorkflow.cs` (budget, redaction,
  evidence, response, policy, setup, explain, answer format, accounting), `WindowsCredentialManagerStore.cs`, ADR-029.
  Opus did not reach them.
- Scripts and workflows: `scripts/*.ps1` and `.github/workflows/`.
- SQLite: `SqliteReviewedOperations.cs`, `SqliteReviewedUpdateHistory.cs`, `SqliteUpdateDispatches.cs`.
- Storage: `FactoryReset.cs`, `DownloadedArtifactCache.cs`, `PostResetOnboardingFlow.cs`, `ReviewDownloadFolders.cs`.
- Execution: files outside the named self-update, endpoint, codec, interpreter and MSI files. Astra read
  `DeploymentCapabilityPolicy.cs` and parts of the endpoint, codec, interpreter and workflows with comments filtered out.
- Chocolatey: `ChocolateyReleaseManifest2_7_5.cs` (a table of pinned hashes).
- Desktop: the `MainWindow.*.cs` files were read only where a finding led.
- WinGet and Domain: the files the coordinator did not list above, including `WingetCandidateCompatibility.cs`,
  `WingetMachineHelperNative.cs`, `ProductPackageMatching.cs` and `DiagnosticRecordAdmission.cs`.
- Tests: not read line by line.

## Suggested order

1. F-22 (shown after every app start until the owner runs a check).
2. F-24, after the owner confirms what a scheduled notification should cover; F-26 with it.
3. F-23, with F-27 (same self-update result path).
4. F-25 and F-30 (small, contained).
5. F-29, F-31, F-28, F-32 as convenient.

`docs/STATUS.md` and `docs/requirements/TRACEABILITY.md` record the fixes above.
