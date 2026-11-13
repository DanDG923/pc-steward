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
- **WinGet's own link.** WinGet's structured API reports the package family names it links to an installed package, just as it reports MSI product codes. When it reports exactly one well-formed family and no product code, the update record carries that family's MSIX product key. This is the same `ProviderSupplied` exact link used for uninstall entries, so the MSIX row is matched with no name guess, and WinGet's own record gets no row of its own. A product code and a family together, several families, or a malformed one give no link.
- **One-time effect.** MSIX product keys change once, from the full-name hash to the family-name hash. A skip, ignore or pin recorded on an MSIX row before 0.1.12 no longer applies. Before this change such decisions were lost at every app update anyway.

### 3.2 Scope and architecture

- **Installed side.** The MSIX catalog that PC Steward reads lists packages registered for the current account (scope User), with the package's own architecture.
- **Update side.** The architecture is the applicable installer's architecture, as before. When the installer names no scope and WinGet records no installed scope, the scope is **User** only if all three of these hold: (a) WinGet links the installed package to exactly one package family, (b) WinGet links it to no product code, and (c) the installer type is `msix`. An MSIX installer with no scope is registered for the account that runs it, which is how the per-user route runs it (`--scope user`). An installer that names Machine, or any other installer type, keeps its own or Unknown scope, and Unknown stays a review item.
- Any difference in architecture or scope is still a conflict and stays a review item.

### 3.3 Running and checking the update

The route does not change. It is the per-user reviewed route `WinGetUserSilentV1`: `winget upgrade --id <id> --version <target> --source winget --exact --scope user --architecture <arch> --disable-interactivity --silent`, unelevated. Windows itself refuses an MSIX update whose package family (name and publisher) differs from the installed package. The post-update check is also unchanged. WinGet's export must show exactly the target version, and the fresh scan must find exactly one MSIX instance of the same product at the target version, with scope User and the reviewed architecture.

### 3.4 Store-installed apps (owner's choice)

Windows Terminal was installed by the Microsoft Store (Store signature), but WinGet names `winget` as its origin and offers Microsoft's GitHub build of the same package family. The owner chose to offer it like any other MSIX app. Windows checks that the update belongs to the same package family before it installs it. The Store may still update the app later. An app WinGet links only to the `msstore` source (Copilot today) is unchanged.

## 4. Not changed

Desktop composition, the reviewed routes, the helper, and machine-scope (provisioned) installs. Frameworks and resource packs are never offered. Copilot keeps its current Store handling.

## 5. Host check (after 0.1.12)

Update Elgato Wave Link, then Windows Terminal, through PC Steward with both apps closed. Each should be verified ("Updated and checked"). After the scan each app should appear once on the Updates page. Discord and .NET Desktop Runtime 10 stay un-updated as test cases.
