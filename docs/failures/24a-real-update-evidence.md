# Task 24A — real-update evidence and remediations

**Date:** 2026-07-28  
**Status:** Completed under a separately approved, one-time PC-host Firefox fixture; desktop outcome presentation requires a future live replay.

## Evidence and outcome

- `winget show --id Mozilla.Firefox --exact --version 152.0.6 --source winget --accept-source-agreements` confirmed the exact Mozilla package and installer hash.
- `winget install --id Mozilla.Firefox --exact --version 152.0.6 --source winget --accept-source-agreements --accept-package-agreements --disable-interactivity` installed the disposable fixture.
- `winget list --id Mozilla.Firefox --exact --source winget --accept-source-agreements` recorded `152.0.6` with `153.0.1` available.
- The desktop source check reproduced a parser defect: `winget upgrade --source winget --disable-interactivity` emits a four-column table, whereas the connector required a source column. Commit `2eaeace` accepts the known requested source when that column is omitted.
- After explicit review and UAC approval, the final PC Steward attempt completed the Firefox update. A direct exact post-update query returned `Mozilla Firefox (x64 en-US)  Mozilla.Firefox  153.0.1` and no Available column.
- The temporary certificate (`E518B26A2D8C82EA03E9E2068C58C7274D76F7C5`) was removed from current-user `My` and `Root`; Firefox was then uninstalled and a final exact query found no installed package.

## Defects found and remediated

- The desktop originally exposed no reviewed execution action. It now permits only fresh verified review, immutable confirmation/revalidation, and controlled post-update verification.
- The real four-column WinGet table was parsed as source-unavailable. The provider now applies the explicitly requested source only to the known source-omitted table shape.
- Source unavailability from a different provider could hide a usable WinGet candidate. Source availability is now evaluated per owning source.
- Concrete registry evidence and WinGet inventory were normalized as separate products. Exact display-name/version correlation now supplies local scope/architecture evidence only when it is unambiguous.
- The elevated helper was missing its managed DLL beside the desktop output. The desktop build now copies that runtime file.
- The outer review dialog re-rendered after it was hidden for the inner confirmation/UAC flow, causing a WinUI lifecycle crash after provider completion. The review-started path now returns before that stale continuation can render.

## Residual risk and next action

The package-version outcome is proven, but the final lifecycle fix has not been replayed live because Firefox was deliberately removed once the evidence was captured. The Task 25 reviewer (Sol / Extra High) should retain that as a residual risk and decide whether a future approved disposable update replay is warranted.
