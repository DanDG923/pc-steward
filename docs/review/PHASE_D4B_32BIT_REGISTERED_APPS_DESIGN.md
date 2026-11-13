# Phase D stage D4b follow-up: whole-PC updates for 64-bit apps with a 32-bit registration (design note)

Date: 2026-11-13. Status: **accepted by the owner (Q1 and Q2 as recommended, 2026-11-13, §7); stages 1 and 2 built for release 0.1.9 (§8).** The owner chose to have this note written before anything is built (2026-11-13, "Design note, then build"). It follows the D4b stage 4 host check on release 0.1.6, where WinGet listed four whole-PC updates on the owner's PC but PC Steward offered only GitHub CLI (and Discord, a per-user app). The two .NET Desktop Runtimes and Visual Studio Build Tools 2022 were never offered.

Builds on the [D4b design note](PHASE_D4B_MACHINE_WINGET_UPDATE_DESIGN.md) (route `WinGetMachineSilentV1`, helper checks H1–H9) and the owner decision "Matching without confirmation" in [V2_PLANNING_DECISIONS.md](../requirements/V2_PLANNING_DECISIONS.md). Release 0.1.7 already contains the two smaller fixes from the same host check: the post-update check follows WinGet's own link to a replaced uninstall entry, and registry inventory skips entries Windows hides (`SystemComponent` = 1).

**Out of scope:** Arm64 PCs, per-user installs, the Microsoft Store source, and any action on the owner's PC. Nothing here turns a route on or approves a host step.

## 1. What the owner's PC showed

A read-only diagnostic (2026-11-13) listed the uninstall entries for the four apps, with the registry view of each. All values are product facts; no personal data.

| App | Visible uninstall entry | View | DisplayVersion | WinGet installed / available |
|---|---|---|---|---|
| .NET Desktop Runtime 8 (x64) | `{4d0027d4-962f-4f65-a01b-f8764a5f4220}`, a Burn bundle | 32-bit | 8.0.33.36622 | 8.0.33 / 8.0.34 |
| .NET Desktop Runtime 10 (x64) | `{29A18B06-…}`, a Burn bundle | 32-bit | 10.0.14.50000 | 10.0.14 / 10.0.15 |
| Visual Studio Build Tools 2022 | the Visual Studio instance ID (an 8-character hex key) | 32-bit | 17.14.44 | 17.14.44 / 17.14.45 |
| GitHub CLI (offered, and updated) | `{F208D269-…}`, an MSI | 64-bit | 2.104.0 | — |

Each runtime also has hidden MSI entries in the 64-bit view, with the same display name and internal versions such as 64.132.61678. Release 0.1.7 no longer lists those.

Three things kept the three apps from being offered, and would also break the update if it were offered:

1. **Architecture is read from where the entry is registered.** `RegistryInventoryProvider` reports every 32-bit-view entry as `x86`. A Burn bundle engine and the Visual Studio installer are 32-bit programs, so they always register in the 32-bit view, even for a 64-bit product. Update intelligence then sees an x86 app and an x64 update (`ArchitectureConflict`). The reviewed route ties the two together too: `WingetMachineReviewedRoute.RegistrationDigest` looks for the registration in the view that matches the architecture (x86 means the 32-bit view, x64 the 64-bit view).
2. **The registry version is not the package version.** The runtime bundle reports 8.0.33.36622 while WinGet's package version is 8.0.33. The post-update check needs a registry instance at exactly the target version, so a successful update to 8.0.34 (registered as 8.0.34.36622) would be Inconclusive.
3. **Each runtime update replaces the entry.** A new runtime patch registers under a new bundle code. 0.1.7's WinGet link covers this for the post-update check.

Visual Studio Build Tools has only problem 1: its entry keeps its key and its DisplayVersion equals the package version.

## 2. The publisher's own evidence (winget-pkgs, checked 2026-11-13)

The `winget` source manifests say which installer writes which entry:

- **Desktop Runtime 8, version 8.0.33** (the installed version) has one installer per architecture. Each has its own `ProductCode`, repeated in `AppsAndFeaturesEntries` with `DisplayVersion: 8.0.33.36622`. The x64 installer's product code is `{4d0027d4-962f-4f65-a01b-f8764a5f4220}`, which is exactly the key of the owner's visible entry. The arm64 and x86 codes differ.
- **Desktop Runtime 8, version 8.0.34** (the update) is laid out the same way. Its x64 installer will register `{de6ce2b1-81f5-406c-a0ca-534ccdf2ad7e}` with `DisplayVersion: 8.0.34.36622`.
- **Desktop Runtime 10, version 10.0.14** has the same layout. Its x64 installer's product code is `{29A18B06-B190-4463-9FFC-BC0D70D80B7C}`, the owner's visible entry, with `DisplayVersion: 10.0.14.50000`.
- **Build Tools 2022, version 17.14.45** has a single installer: `Architecture: x64`, `Scope: machine`, type `exe`. It has no product code and no `AppsAndFeaturesEntries`.

WinGet's structured API (COM 1.29) gives a package's installed product codes, but not which installer each code belongs to. It has no installed-architecture field either. The per-installer facts are only in the manifest, and PC Steward already reads an exported manifest at review: the H7b `winget download` evidence, read by `WingetExportedManifest`.

## 3. The proposal

### 3.1 Inventory: separate the registration view from the architecture

Registry instances keep a new **registration view** (64-bit, 32-bit or per-user) as its own field. `Architecture` stays `x86` for a 32-bit-view entry by default, so genuine 32-bit apps behave exactly as today. The view is never used as proof of a 64-bit app.

### 3.2 Offering: a narrow "architecture checked at review" case

A whole-PC WinGet update stays unoffered for an architecture conflict, except in one case. The candidate becomes actionable, shown as "architecture is checked when you review", only when all of these hold:

- the matched registry entry is in the 32-bit view and is linked to the package by WinGet's own product code (High confidence, `RegistryProductKey` from 0.1.6), not by name;
- the candidate's installer, as WinGet's structured API chose it for this PC, is x64;
- there is no other reason for review.

Every other architecture difference stays a review item, exactly as today. If WinGet's own choice for this PC is not x64 (for example, because WinGet also reads the 32-bit registration as x86), the case does not apply and nothing changes. Stage 3's host check records what WinGet chooses on the owner's PC.

### 3.3 Review: prove the architecture from the manifest, or refuse

At review, after the existing target-version download and before anything is bound, the desktop downloads the **installed** version's manifest. It uses the same fixed `winget download` command line, for x64 and at the installed package version, reads it with the same strict reader, and then deletes the folder. The review binds x64 only on one of two proofs:

- **P1, product-code proof:** the installed version's x64 installer declares a product code (directly or in `AppsAndFeaturesEntries`) equal to the uninstall entry's key. This covers the .NET runtimes.
- **P2, single-architecture package:** neither the installed version nor the target offers any machine-scope installer for the PC's architectures other than x64. An exported manifest cannot show this, because `winget download` exports only the one selected installer (D4b §5.2; `WingetExportedManifest` refuses a second item). The complete list comes from WinGet's structured API on the same trusted catalog. For both versions, `PackageVersionInfo.GetApplicableInstaller` is called with machine scope and `AllowedArchitectures` set to x86 alone (and to Arm64 alone on an Arm64-capable PC). It must return no installer, while the x64-only call returns one. Any error, an unknown version or a missing catalog entry is no proof. If P2 holds, the app can only have come from an x64 installer of this package, and WinGet could only choose x64 again. This covers Build Tools. It is weaker than P1 (see Q1).

With neither proof, the review refuses with "PC Steward couldn't confirm this app's architecture, so it can't be updated here. Nothing was started." The job, the route shape and the helper's `--architecture x64` are unchanged. The registration digest now binds the view the entry was actually read from, instead of a view derived from the architecture. The helper already searches both views for the digest (H6), so the helper does not change.

The extra download costs one more installer download per review (about 55 MB for a runtime, a few MB for Build Tools). It happens only when the owner opens the review of one of these apps.

### 3.4 Post-update check: the target's registration, as the publisher declared it

When the target manifest's x64 installer declares `AppsAndFeaturesEntries`, the review also records its expected product code and DisplayVersion (for 8.0.34: `{de6ce2b1-…}` and 8.0.34.36622) as desktop-side post-update evidence. It is stored with the approved job, written once at Confirm and never changed. It is not part of `wgm1`, the canonical reviewed payload or the wire format: `TryGetWinGetMachineEvidence` keeps its five fields and the helper never sees it. Only the desktop's post-update check reads it. That check concludes nothing with privilege, so a same-user change to the stored value could only make the check fail or agree with what WinGet's export and the registry already show. After the update, the check accepts a registry instance only when all of these hold:

- the instance is at that product code (or the reviewed key, or 0.1.7's WinGet link);
- its DisplayVersion is exactly the bound one;
- it is in machine scope;
- WinGet's export shows exactly the target package version, as today.

Its architecture counts as the reviewed x64 because the same manifest proof covers it. Without bound entries, as for Build Tools, the check stays as today: the target version exactly, at the reviewed key.

## 4. What does not change

- No new route, wire format, helper check or prompt. One UAC prompt per app, as now.
- Name-only or near-name matches never use §3.2.
- WinGet still pins ID, version, source, scope, architecture and installer type, and refuses anything not newer.
- Dependencies and agreements are still refused (D4b Q3). The .NET runtime and Build Tools manifests declare neither, so the strict reader accepts them. The runtime manifests declare `Scope: machine` at the root, which the exported merged manifest writes into the installer, so the strict-scope rule (D4b Q1) is met.

## 5. Security

- **New input:** a second exported manifest, read by the same bounded strict reader, and WinGet's structured installer selection (P2), both only for comparison. It cannot widen what runs, because the helper still downloads and checks the target manifest itself (H7b).
- **Forging the proof:** this would need either a changed manifest in the protected `winget` source or a fake 32-bit-view HKLM entry with the right key. Both need administrator rights or a compromised Microsoft source. The owner's rule treats those as already compromised.
- **Wrong-architecture risk:** if the proof were wrong, a 32-bit app would be replaced by its 64-bit build. P1 rules this out by product code. P2's residual is a 32-bit app installed from an older, now-removed x86 installer of a package that has since dropped x86. WinGet itself would make the same choice.

## 6. Build stages

1. **Inventory and offering.** Add the registration view field (with a store migration if the instance table needs it), §3.2 and its tests. Nothing becomes runnable yet: review still refuses.
2. **Review proof and the post-update binding.** §3.3 and §3.4: the installed-version download, P1, P2 from the structured API's installer selection, the digest from the actual view, the desktop-side expected-entry record, the post-update check, and tests built from the real runtime and Build Tools manifest shapes.
3. **Host check** on the owner's PC: update .NET Desktop Runtime 8 and Build Tools through PC Steward.

Each stage gets one PR with a Codex review.

## 7. Owner questions

**Q1. Which proofs?** P1 alone covers the .NET runtimes (and other Burn or MSI packages that declare product codes). P2 adds Build Tools and other single-installer packages, on the weaker "only one architecture exists" argument. *Recommendation: both.*

**Q2. Offer before the proof?** §3.2 shows these apps as updatable before the architecture is proven, so a review can still end in "couldn't confirm". The alternative keeps them as review items until the owner opens them, which looks the same as today. *Recommendation: offer them, as §3.2 describes.*

**Owner answer (2026-11-13, 10:39 UTC).** In response to the prepared choice listing Q1 and Q2 with the recommendations above, the owner chose **"Accept both as recommended"**: both proofs (P1 product code, P2 single-architecture package), and these apps are offered with the architecture checked at review (§3.2). This accepts the design only. It does not approve a release, a host step or a real update.

## 8. Build notes (stages 1 and 2)

- **No store migration.** The registry provider already records only the 32-bit machine view as `Machine` + `x86`, so that pair is the registration view; the profile flag `RegisteredIn32BitView` is set only for a product with no WinGet instance of its own.
- **Version mapping.** WinGet's installed version (8.0.33) differs from the entry's DisplayVersion (8.0.33.36622), so the preparation no longer requires them to be equal for these apps. P1 ties them together from the installed version's own manifest. Without P1, P2 requires them to be equal. When an `AppsAndFeaturesEntries` item names the key, its DisplayVersion decides P1; the installer's own product code counts only when no item names the key.
- **Expected entry.** It travels with the approved job in memory (the review, then the batch item) to the post-update check. A job checked later without it, for example after an interruption, falls back to the exact target version and x64, which reads as Inconclusive for these apps: never a false success. Without a declared entry (Build Tools), the expected entry is the reviewed key at exactly the target version.
- **Confirm.** The fresh preparation must still ask for the proof, and its digest of the 32-bit view must equal the reviewed one. The proof is not repeated at Confirm: it reads publisher manifests, and the digest binds the entry's key and version.
- **P2 query.** The `winget` catalog is connected on its own (no installed-package correlation). A version must appear exactly once in the default channel. An installer returned when only x86 is allowed counts as found whatever its architecture, so it fails closed.
