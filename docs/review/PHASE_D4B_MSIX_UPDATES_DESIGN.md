# MSIX app updates through WinGet (design note, 2026-11-13)

Status: **built for release 0.1.12**. The owner chose (2026-11-13) to offer Windows Terminal like Elgato Wave Link, although the Microsoft Store installed it.

Requirements: `FR-INV-01`, `FR-UPD-01/15`. This builds on the per-user reviewed route ([C4](C4_REVIEWED_WINGET_USER_MODE.md), `WinGetUserSilentV1`) and the owner's matching rule from 2026-11-13: an exact WinGet link or a same-name, same-publisher match is offered without confirmation, and the owner is asked only when two packages tie.

## 1. What the owner's PC showed (0.1.11)

Two MSIX apps had real WinGet updates but stayed in "Needs review" with "The install scope isn't known" and "The app's architecture isn't known". Elgato Wave Link also showed twice. These facts come from a read-only diagnostic (shape only, no personal data):

| | Windows Terminal | Elgato Wave Link |
|---|---|---|
| MSIX package | `Microsoft.WindowsTerminal_8wekyb3d8bbwe`, x64, 1.25.2733.0, signed by the Store | `Elgato.WaveLink_g54w8ztgkx496`, x64, 3.3.0.4529, signed by the developer |
| Shown name and publisher | Windows Terminal / Microsoft Corporation | Elgato Wave Link / Corsair Memory, Inc. |
| WinGet package | `Microsoft.WindowsTerminal` 1.25.2733.0 → 1.25.3131.0, origin `winget` | `Elgato.WaveLink` 3.3.0.4529 → 3.4.0.4650, origin `winget` |
| WinGet's link to the local app | package family name (lower case), no product code | package family name (lower case), no product code |
| Installer | `msix`, no scope | `msix`, no scope |
| Installed scope WinGet records | none | none |

The installed MSIX version equals WinGet's version for both apps.

## 2. Why they weren't offered

1. **The MSIX app's identity included its version.** The MSIX inventory named each package by its *full* name (`Name_Version_Arch__PublisherId`), and the product key is a hash of that name. Every update therefore made a new product. Decisions such as skip or ignore did not carry over, and a post-update check could never find the reviewed product at the new version.
2. **The MSIX row never met WinGet's row.** The row used the package's internal name (`Microsoft.WindowsTerminal`) and its publisher ID (a hash) instead of the names Windows shows. Name matching failed, or found a publisher conflict (Wave Link), so WinGet's own installed record became a second row.
3. **WinGet's own row has no scope or architecture.** WinGet's installed list gives neither, and WinGet records no installed scope for an MSIX app. So even the matched row stayed a review item.

## 3. Design

### 3.1 Identity

- **The MSIX inventory names an app by its package family name** (`Name_PublisherId`). This is the same for every version and every architecture of the app. The product key becomes stable, so an update finds the same product. Only names in the Windows family-name format (a name of 3 to 50 letters, digits, dots or hyphens, an underscore, then the 13-character publisher ID) are accepted. Anything else is a malformed package.
- **Frameworks and resource packages keep their full package name.** They are never offered. Because they keep their full name, WinGet's family link can never reach them, and they never share a product with the app. The raw diagnostic record of each package is keyed by the same identity as its installation, so the details page still lists it.
- **Rows use the names Windows shows** (`Package.DisplayName`, `Package.PublisherDisplayName`). When Windows has none, or only an unresolved `ms-resource:` reference, the rows fall back to the package name and publisher ID. Shown names are read for apps only, never for frameworks or resource packs.
- **WinGet's own link.** WinGet's structured API reports the package family names it links to an installed package, just as it reports MSI product codes. When it reports exactly one well-formed family and no product code, the update record carries that family's MSIX product key. This is the same `ProviderSupplied` exact link used for uninstall entries, so the MSIX row is matched with no name guess, and WinGet's own record gets no row of its own. The family is linked **only when the update's applicable installer is itself `msix`**. Only then does Windows refuse a package from another family or publisher, and that check is what makes the family an exact identity. A product code and a family together, several families, a malformed family, or any other installer type give no link.
- **One-time effect.** MSIX product keys change once, from the full-name hash to the family-name hash. A skip, ignore or pin recorded on an MSIX row before 0.1.12 no longer applies. Before this change such decisions were lost at every app update anyway.

### 3.2 Scope and architecture

- **Installed side.** The MSIX catalog that PC Steward reads lists packages registered for the current account (scope User), with the package's own architecture.
- **Update side.** The architecture is the applicable installer's architecture, as before. When the installer names no scope and WinGet records no installed scope, the scope is **User** only if all three of these hold: (a) WinGet links the installed package to exactly one package family, (b) WinGet links it to no product code, and (c) the installer type is `msix`. An MSIX installer with no scope is registered for the account that runs it, which is how the per-user route runs it (`--scope user`). An installer that names Machine, or any other installer type, keeps its own or Unknown scope, and Unknown stays a review item.
- Any difference in architecture or scope is still a conflict and stays a review item.

### 3.3 Running and checking the update

The route does not change. It is the per-user reviewed route `WinGetUserSilentV1`: `winget upgrade --id <id> --version <target> --source winget --exact --scope user --architecture <arch> --disable-interactivity --silent`, unelevated. Windows itself refuses an MSIX update whose package family (name and publisher) differs from the installed package. The post-update check is also unchanged. WinGet's export must show exactly the target version, and the fresh scan must find exactly one MSIX instance of the same product at the target version, with scope User and the reviewed architecture.

### 3.4 Store-installed apps (owner's choice)

Windows Terminal was installed by the Microsoft Store (Store signature), but WinGet names `winget` as its origin and offers Microsoft's GitHub build of the same package family. The owner chose to offer it like any other MSIX app. Windows checks that the update belongs to the same package family before it installs it. The Store may still update the app later. An app WinGet links only to the `msstore` source (Copilot today) is unchanged.

### 3.5 Residual (Codex security review, PR #92)

The installer type is read when the source is checked, and read again when the review re-prepares the update from fresh evidence. Both reads must show `msix` for the app to be offered. The per-user command itself (`WinGetUserSilentV1`) does not pin the installer type. A trusted catalog that switched the installer type between the review and the run could therefore still run another installer type as the user. That needs the trusted WinGet catalog itself to be compromised, and such a catalog could already offer a malicious update to any app matched by name and publisher. For the MSIX case, the post-update check would then find no MSIX package at the target version and report the update as not verified. Binding the reviewed installer type into the per-user run belongs to the per-user installer binding work (separate thread), which reads the applicable installer at review.

### 3.6 An open app (host check of 0.1.14, owner's decision)

On the owner's PC, 0.1.14 updated Wave Link and Windows Terminal while both were open. WinGet reported success, but Windows registers a new MSIX version only after the app closes, so the post-update check found the old version and the result was "Installed, not verified". With both apps closed, the same updates were verified. The owner asked for the review to prompt the user to close the app, and chose to also offer "Close it for me" (2026-11-13).

- **An app package must be closed first.** When the reviewed installer type is `msix` and the running-app check matches a process, the review says "Close <app> to update it", the update button stays disabled, and there is no "update anyway". The workflow enforces the same rule when the update is confirmed: it checks again and refuses to start while the app is open, even if a warning was acknowledged. Other installer types keep the existing warning, which the owner can still acknowledge and update anyway. Only a copy open in the owner's own Windows session counts: an app package is registered per account, so a copy open only in another user's session is the usual warning, which can be acknowledged (independent review).
- **"Check again" refreshes the warning that is confirmed (independent review).** When "Check again" or "Close it for me" finds a different set of running apps, the review is confirmed with the warning now shown. Only that warning changes; the reviewed selections, the 30-minute limit and the single use stay, and each app is still checked again just before it starts.
- **"Close it for me" (`FR-UPD-06`).** The review offers "Close <app> for me" for any running app it matched, and "Check again". The button is offered only for an app open in the owner's own Windows session, and asks each matched process in that session to close its main window once, the same request as clicking the window's close button, so the app can ask to save or refuse. PC Steward then waits up to 10 seconds, checks again and says what happened: closed, still open, or no window to close (for example an app in the notification area, which the owner closes from there). It never ends a process, never touches another session, and only names that the running-app check reported (letters, digits, `.`, `-`, `_`, at most 64 characters, at most 32 names). Neither button approves or starts an update.
- **Not changed.** How a running app is detected (process names derived from the package ID), the routes, the helper and the post-update check.
- **Tests.** `RunningAppCloseTests` (8: another user's open copy is only a warning, "Check again" refreshes only the confirmed warning, only `msix` must be closed, an acknowledgement never covers an open app package, the review's wording and disabled button, a batch, the closer asks each window once and reports what is still open, and touches nothing else); `WingetUserVendorInstallerTests.AnOpenMsixAppIsNeverUpdatedEvenWhenTheWarningWasAcknowledged`; source contract `UiRedesignContractTests.UpdateJobReviewStateIsReleasedGuardedAndNeverMovesTheUser` (the update button waits for `CanConfirm` and for a check in progress).

## 4. Not changed

Desktop composition, the reviewed routes, the helper, and machine-scope (provisioned) installs. Frameworks and resource packs are never offered. Copilot keeps its current Store handling.

## 5. Host check (after 0.1.12)

Update Elgato Wave Link, then Windows Terminal, through PC Steward with both apps closed. Each should be verified ("Updated and checked"). After the scan each app should appear once on the Updates page. Discord and .NET Desktop Runtime 10 stay un-updated as test cases.

After 0.1.14 (§3.6): open Windows Terminal, review its update, and check that the update button is disabled with "Close Windows Terminal to update it". Choose "Close Windows Terminal for me": each Terminal window closes (a window with a running command may ask first), the review says "Closed: WindowsTerminal." and the update button is enabled.
