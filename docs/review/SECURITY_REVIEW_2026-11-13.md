# Security review of the changes merged on 2026-11-13

**Scope.** What merged into `phase/4-beta` on 2026-11-13, read at `a84801a`: per-user vendor installers (#91), MSIX/Store updates and
package-family linking (#92), real app icons (#101, ADR-035), "Close it for me" (#103), the diagnostic timeline (#93, #105, and the
open #108), and the UI review PRs #94 to #106 where they touch data. The elevated-helper changes of the same day were also read
because they sit on the main boundary: the reopen watcher (#83) and the helper without a console (#90).

**How it was ranked.** The owner's approach: an exploit that needs the PC already compromised is low priority; anything that
crosses a real boundary (a user foothold reaching administrator, a supply-chain route, private data leaving through the export)
comes first.

**Result.** Nothing critical or high. No change today gives a program running as the owner a way into the elevated helper, no
package, workflow or build input changed (only `VersionPrefix`, a new project reference and the helper's `WinExe`), and the export
holds only sanitized records. One medium item was the owner's choice (a publisher check added to the review warning); the rest are low or informational and are accepted or
already accepted.

## Findings, most severe first

### M1. Per-user installers that ask for administrator permission wait in the account's own Temp folder (medium; owner's choice)

- **Where.** #91, `WingetUserInstallerRules`, `WingetUserInstallerText.ElevationWarning`; owner answer Q1-B offered per-user vendor
  installers whose manifest says `ElevationRequired` or `ElevatesSelf`.
- **What.** On the per-user route PC Steward starts WinGet unelevated, as the owner. WinGet downloads the installer into the
  account's Temp folder, checks its hash, then asks Windows to run it with administrator rights (UAC). That folder is writable by
  any program running as the owner, so such a program could replace the installer between WinGet's check and the prompt. If the
  owner then chooses Yes, the replacement runs as administrator. The machine-wide route avoids this by having the helper download
  into a protected folder; the per-user route cannot, because nothing elevated is involved until Windows asks.
- **Why medium, not high.** It needs code already running as the owner and it needs the owner to choose Yes on a Windows prompt,
  which names the verified publisher, or says "Unknown publisher" for an unsigned file. A program already running as the owner can
  show its own prompt without PC Steward. What PC Steward adds is that the owner is expecting a prompt at that moment. Not tested on
  a PC: whether WinGet holds the file locked between its check and the launch was not confirmed.
- **Owner's choice (2026-11-13 22:51Z): add a publisher check to the warning.** They stay offered; the review's warning now ends:
  "Before you choose Yes, check that Windows names the app's publisher as the verified publisher. If it says Unknown publisher or
  names someone else, choose No." The other options were to accept it as it was, or to stop offering them (back to Q1-A).

### L1. "Close it for me" chooses processes by name (low; accept)

- **Where.** #103, `PackageDerivedRunningApplicationDetector`, `WindowCloseRunningApplicationCloser`.
- **What.** The process names come from the package ID and its last part (`Microsoft.WindowsTerminal` gives `WindowsTerminal`).
  Another program with the same process name in the owner's session would also be asked to close.
- **Why low.** Only on the owner's click, only in the owner's session, only the window-close request (`CloseMainWindow`, the same
  as clicking X), never a force-quit, and an app can ask to save or refuse. A higher-integrity window does not receive the
  request (Windows blocks it). Worst case is an unrelated app asked to close. No change.

### L2. Icon reader: check-then-open gap (low; already accepted)

- **Where.** #101, ADR-035 rule 3.
- **What.** The path is checked for links and then opened. A program running as the owner could swap a folder for a link in
  between. Accepted by the owner on 2026-11-13; it gains nothing that program couldn't do itself. Re-read for this review: the
  parser (`AppIconFile`) checks every offset and length against the file and fixed limits, follows at most three resource levels,
  never loads the file as code and hands Windows' decoder one image of at most 1 MB. No new finding.

### L3. Deleted diagnostic timelines can remain in the database file's free space (informational; accept)

- **Where.** #93, `SqliteDiagnosticSnapshots`; the database runs without `secure_delete`.
- **What.** Expired or cleared timelines are deleted as rows; SQLite may leave their bytes in free pages until they are reused.
- **Why informational.** The file is in the owner's own profile, readable only by the owner and administrators, and the records
  were already sanitized before saving. Reading it needs access that already reaches the owner's files.

### I1. What the redaction covers (informational)

- The diagnostic text sanitizer removes paths (drive, network, `%VARIABLE%`, quoted), links, account SIDs and email addresses, then
  this PC's name, the account name and the domain name as whole words. Event logs render only identity, time and Kernel-Power 41's
  bugcheck code; Reliability reads six named properties and keeps only the product name as text; Windows Update history keeps the
  update title. The owner's full display name is not on the list; none of the sources read carries it, so no change.

## Checked with no finding

- **MSIX linking (#92).** A package family gives an exact link only when the applicable installer is itself `msix` and WinGet
  reports exactly one family and no product code. A family name includes the publisher's hash, so another publisher can't claim
  it without a certificate Windows trusts for that name. MSIX is never a machine-route installer type (`WinGetMachineInstallerType`).
  The shown names Windows gives are bounded and control characters refused.
- **Per-user vendor installers (#91).** Never reach the elevated helper (no `WinGetUserSilentV1` code in the helper). The installer
  read is the trusted predefined `winget` catalog, one exact ID, one exact version, user scope strict; Confirm re-reads it and
  refuses a change; `--installer-type` pins the run; a whole-PC change afterwards is reported as a failed check, never success.
- **"Close it for me" (#103).** A must-close app package is never started while it runs in this session, whatever was
  acknowledged; "Check again" only refreshes the warning, never the reviewed selections, the 30-minute ceiling or the single use.
- **Diagnostics (#93, #105, #108).** Read-only, unelevated, allowlisted at the source (event log provider and ID together, one
  narrow WQL query, local Windows Update history only). Every text is sanitized before it is kept, stored rows are re-checked when
  read, the export is canonical JSON written only where the owner chooses after a preview, and Clear scan data removes timelines.
  #108 only adds MsiInstaller 1033/1034 with an unknown outcome.
- **Reopen watcher and console (#83, #90).** The watcher refuses to run elevated, runs only as the protected helper copy, reads one
  canonical result file and starts the installed desktop as the owner with no shell. No console change alters what the helper runs.
- **UI review (#94 to #106).** No new file, network, clipboard, process or registry use beyond the icon reader; the first-run
  question still asks before any update check is turned on.
