# Mythos whole-application security review (2026-11-21): triage and fixes

Review target: `phase/4-beta` at `c1a1acb` (after PR #135). It was read-only: no build, no tests run, no key, no provider call
and no install. The owner ran it with the second model, Mythos 5.2, against a pinned prompt that listed the accepted residuals
and asked for an exact check on the OpenAI API key. The report itself is the owner's copy and is not committed.

**Verdict: PASS.** There is no Critical or High finding. The API key check found nowhere the key is written outside Windows
Credential Manager (target `PCSteward/OpenAI/ApiKey.v1`, local-machine persistence). The key isn't in the database, logs,
exports, the request preview, the helper or any child process. It is removed on Disconnect and factory reset. It isn't in the
repository, its history, CI or the artefacts. The review found one Medium and six Low or Info findings.

Ranking follows the owner's rule: a finding that needs the PC already compromised is low priority. Anything that crosses a real
boundary comes first, and of these findings only the supply-chain route (MY-01) does.

| # | Severity | Finding (short) | Verdict on `phase/4-beta` | Fix |
|---|---|---|---|---|
| MY-01 | Medium | The WiX SDK package that builds the MSI wasn't byte-pinned. In the release workflow it ran with the runner's rights before the payload was uploaded, on the runner's own SDK | Real. Every other release package and the SDK were pinned (Codex scan finding 1). This one was restored by version only, from the machine's NuGet sources. | `release-toolchain-pins.json` pins it (`msiSdk`: nuget.org address, size, SHA-512; NuGet's own catalog gives the same hash). `New-PCStewardMsi.ps1` downloads it, checks it before dotnet starts, and lets NuGet restore only from a local feed holding that one file, into a new empty package folder. All other sources and fallback folders are cleared, and the build fails if anything else was restored. The reference MSI (release workflow and CI MSI job) is built with the pinned SDK zip. |
| MY-02 | Low | `main` was trusted as a release branch, but it doesn't exist and its ruleset doesn't restrict creation | Real. It still needs write access to GitHub and the owner's signature. | `main` is removed from the five allow-lists: both release workflows, `ReleaseRunEvidence`, and the signing and publishing scripts. `GITHUB_RULESETS.md` says what to do before `main` is used. No change to the owner's GitHub settings is needed. |
| MY-03 | Low | Extra copies of the key in memory: the key dialogs read `Password` on every change, and Credential Manager's returned buffer was freed without being cleared | Real. Hardening only: memory a same-user program can read is accepted (ADR-028 Q8). | The dialogs no longer read the box to enable their button (an empty box is refused on Save). The native credential buffer is overwritten before `CredFree`. |
| MY-04 | Info | Windows clipboard history (and sync) keeps a pasted key | Windows behaviour, not PC Steward's. | One sentence beside the key box: if clipboard history is on, delete the key from it after pasting. The crash-report case is accepted; MY-03 reduces what a report could hold. |
| MY-05 | Info | The retired ADR-022 trust rule (any Windows-trusted signer, same raw certificate) was still accepted at helper launch and in both peer checks | Real; it needs administrator rights to place files in `%ProgramFiles%\PC Steward`. | Removed: only a pair carrying the same compiled release pin is accepted. The CI MSI job already uses a compiled throwaway pin, and the owner's install carries the release pin. |
| MY-06 | Info | A planted reset marker makes the next start run a factory reset, including key removal | Real. Same-user denial of service only: the marker carries no path, and every category is compiled in. | **Proposed: accept, no change** (owner's rule; same class as ADR-032 review I3). |
| MY-07 | Info | The first CI job kept the checkout token and picked the SDK by a floating version | Real. The token is read-only and there are no secrets. | `persist-credentials: false` and `global-json-file: global.json`. A new architecture test holds every workflow to both. |

## What the fixes change for the owner

- **Release paste.** `New-PCStewardMsi.ps1`, `New-PCStewardRelease.ps1`, `Publish-PCStewardRelease.ps1` and the pin file
  change. The next release paste pins their new hashes, as for every release.
- **Signing on the owner's PC.** The MSI build now downloads the WiX SDK package itself (about 20 MB, the same nuget.org file
  as before) and refuses it if a byte differs. Nothing else changes.
- **The key dialogs.** "Save key and connect" and "Save new key" are enabled before a key is pasted. Pressing one with an empty
  box shows "Paste your API key first." and saves nothing.
- **A WiX version change** now means updating `msiSdk` in the pin file in the same pull request, like any other release
  package.

## Residuals

- The download of the WiX SDK on the owner's PC lands in the owner's temp folder, so a same-user program could swap it between
  the check and NuGet's read. That is the signing-session residual of ADR-034 H1 and M3. The elevated install step still
  installs only an MSI whose tables equal the CI reference, which is now built from pinned inputs only.
- The GitHub-hosted runner image and GitHub Actions stay trusted (Codex scan finding 1 residual, unchanged).
