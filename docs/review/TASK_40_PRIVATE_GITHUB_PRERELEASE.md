# Task 40 — private GitHub prerelease

**Date:** 2026-08-11
**Branch:** `phase/4-beta`
**Published source commit:** `d6d5caaadbd38050dc3c5fd712686c4b98b0b54f`
**Application candidate commit:** `1298d4ae5a747d7ee489c32746f5154049800263`
**Requirements:** `AC-DAT-18`, `AC-UPD-03/06`, `TM-04/18`

## Outcome

**PASS for an explicitly authorized private, owner-only GitHub copy.** A new private repository was created at `DanDG923/pc-steward`, the committed `phase/4-beta` source was pushed, and an owner-only prerelease was published at tag `v0.1.0-owner-only.1`. This does not change the product's `NOT READY FOR DISTRIBUTION` gate and is not a public/stable release, portable production installer, automatic update channel, or production-signing claim.

## Remote evidence

| Check | Result |
|---|---|
| Repository | `DanDG923/pc-steward` |
| Visibility | `PRIVATE` / `isPrivate: true` |
| Default branch | `phase/4-beta` |
| Release source/tag commit | `d6d5caaadbd38050dc3c5fd712686c4b98b0b54f` |
| Release | `PC Steward 0.1.0 - owner-only prerelease backup` |
| Tag | `v0.1.0-owner-only.1` |
| Release state | Published prerelease; not draft; not stable |
| Source asset | `PCSteward-0.1.0-owner-only-source.zip` — 3,311,204 bytes |
| Checksum asset | `PCSteward-0.1.0-owner-only-source.zip.sha256` — 104 bytes |
| SHA-256 | `AA95ACB4836632CB8B5E51D41548E2647E46BE8456A77E7CBBD0B9C6591E7E91` |
| Remote download verification | PASS — downloaded ZIP matched the uploaded checksum |

## Artifact boundary

The ZIP is a fresh `git archive` of the exact committed source, with 721 entries. It contains `docs/STATUS.md` and the guarded `Install-OwnerOnlyProtectedTestDeployment.ps1` route, and contains no `.pfx`, `.p12`, `.pem`, `.key`, or `.cer` file. The committed tree and history were checked for common credential patterns and secret-file extensions; the only textual pattern matches were harmless task/evidence names.

The archive intentionally does not claim that the locally signed `%ProgramFiles%` payload is portable. To recreate the update-capable owner-only installation, the owner must extract the source on the approved Windows machine and run the included protected installer from an elevated PowerShell session. That installer creates a new non-exportable 14-day CurrentUser certificate, builds the desktop/helper pair, applies protected ACLs, and validates the complete installation. Every application update still requires fresh evidence, review, explicit confirmation, and UAC where applicable.

## Authentication and publication notes

The existing GitHub CLI token had expired and was renewed through GitHub's official device flow. The initial push was rejected because the token lacked permission to upload the existing `.github/workflows/ci.yml`; the owner explicitly approved adding only the `workflow` scope, after which the committed branch pushed unchanged. The repository was created private before any commit was accepted. Ignored local databases, build outputs, certificates, protected-install state, and rollback artifacts were not uploaded.

No PC Steward application, source connector, application update, schedule, clear-data, or reset action was invoked by this GitHub publication task.
