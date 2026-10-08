# ADR-024 — Provider precedence, adoption and Chocolatey setup

**Status:** Accepted
**Date:** 2026-10-08
**Decision owners:** Product owner and security reviewer
**Related requirements:** `FR-INS-04/06`, `FR-PRV-01–04`, `FR-SRC-07`, `AC-V3-11`
**Related decisions:** D-007, Q-006, Q-007

## Context

v0.3 installs new apps and can use either WinGet or Chocolatey for the same product. Chocolatey manages only packages it registered itself. Its upstream installer is a PowerShell script fetched from the web, which `AGENTS.md` prohibits. Both providers can report different versions for the same app.

## Decision drivers

- Never switch the installer of an existing app behind the user's back.
- Never run raw scripts or URLs.
- Prefer the provider with Microsoft-hosted manifests and per-user scope support.

## Options considered

### Option A — WinGet-preferred precedence, explicit adoption, Chocolatey set up through WinGet

### Option B — Always prefer Chocolatey where available

Chocolatey would gain broader coverage of some apps, but package scripts would run as administrator for every install, and there are no user-scope installs.

### Option C — Ask the provider question for every operation

Asking every time is the safest option, but it is tedious and adds nothing over a stored per-app preference.

## Decision

Accept Option A.

- **Installed apps** keep the provider that manages them: the WinGet-matched ARP entry, or the Chocolatey-registered package. Automation never changes the provider.
- **New installs** use the catalog default. That is WinGet unless WinGet lacks the app or the owner chose Chocolatey for that entry. The owner may override per app (`FR-PRV-04`).
- **Adoption** (installing a Chocolatey package over an app installed another way) is an explicit, labelled Install in a reviewed batch. It is never offered for automatic updates.
- **Provider disagreement:** when both providers report different versions, both are shown. The app is excluded from automation until a preferred provider is set.
- **Chocolatey setup:** when Chocolatey is absent, **Set up Chocolatey** queues a reviewed machine-scope WinGet Install of `Chocolatey.Chocolatey`.
  - After the install, the existing trusted-client checks run before any Chocolatey use: canonical root, no reparse points, a layout the caller cannot modify, a trusted signature and the exact signer.
  - No code path downloads or runs the Chocolatey bootstrap script. An architecture test enforces this.
- **WinGet absent:** PC Steward links to Microsoft Store App Installer and does not install WinGet.

## Consequences

### Positive

Installs are predictable. Chocolatey's administrator-script risk is opt-in and visible.

### Negative / trade-offs

Some apps may update more slowly through WinGet than through Chocolatey unless the owner sets a preference.

### Follow-up controls and tests

Task 50 covers absent, untrusted and present client states, and the architecture test that forbids bootstrap paths. Task 48 adds argv snapshots for every provider, scope and kind. Task 47 proves preferred-provider and disagreement handling.

## Reconsider when

WinGet's App Installer is unavailable on supported Windows builds, or the Chocolatey client's trust identity changes.
