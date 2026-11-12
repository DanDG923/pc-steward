# Codex whole-repository security scan (2026-11-12): triage and fixes

Scan target: Git revision `e20c70d`, repository mode, static review only (no Windows runtime, signing, MSI or network
test). Ten reportable findings: one High, four Medium, five Low. This record checks each one against `phase/4-beta`
(`1530e9d`; none of the affected files changed since `e20c70d`) and tracks the fix. The scan report itself is the owner's
copy and is not committed.

| # | Severity | Finding (short) | Verdict on `phase/4-beta` | Fix |
|---|---|---|---|---|
| 1 | High | Signing a release payload can bless unverified SDK and NuGet inputs | Real. `global.json` allows a later SDK patch and the runner image may hold one; the release build restores packages (including the Native AOT runtime and ILCompiler packs compiled into the elevated helper) without checking their bytes; the signing script binds the run, artefact and commit but not the toolchain. | Release build uses the official SDK zip (SHA-512 pinned) and checks every restored package against committed SHA-512 pins before any project builds, with the package cache read-only to the build user; every PR gates on the pins; the signing script refuses a payload record without the matching toolchain pin digest. Residual: the GitHub-hosted runner image itself is trusted (no independent reproducible build). |
| 2 | Medium | Processing an authenticated release ZIP can exhaust the signing or install host | Real. `New-PCStewardRelease.ps1` uses `Expand-Archive` and `Install-PCStewardRelease.ps1` buffers each entry in a `MemoryStream` before the 512-file / 256 MiB manifest bounds run. Reaching it needs a compromised CI artefact first. | Entry-count, per-entry and total inflated-byte limits checked before extraction, then counted streaming writes, in the signing, publishing and install scripts. |
| 3 | Medium | Continuing after reset can reopen a substituted prior-state database | Real race (startup checks freshness once; Continue does not). Same-user code can already write the database at any other time, so no privilege boundary is crossed; update execution still needs fresh review and approval. | Continue re-runs the semantic fresh-store check immediately before the marker is acknowledged. |
| 4 | Medium | Keeping an owner-only test deployment lets same-user code reuse its trusted signer | Real in `Install-OwnerOnlyProtectedTestDeployment.ps1` (earlier review M2). No current exposure: the Task 50 cleanup (2026-11-12) found and left no owner-only test certificate, and that deploy route is retired in favour of the signed MSI. | The installer deletes the key-bearing `CurrentUser\My` certificate and its key once both images are signed, keeping only the public trust entries. |
| 5 | Medium | Approving a Chocolatey update can race into elevated CLR code loading | Same residual as Task 50 review M1, accepted by the owner on 2026-11-06 and recorded in ADR-025. | No change; acceptance stands. |
| 6 | Low | Copy diagnostic can expose unsanitized history identity fields | Real: only `diagnostic` passes through the redactor. | Every string field in the bundle is sanitized and length-bounded. |
| 7 | Low | Running ordinary CI executes mutable action tags | Real: `ci.yml` used `@v7`/`@v6` tags. | Every `uses:` in every workflow pinned to a full commit with a tag comment, enforced by an architecture test. |
| 8 | Low | Loading hostile local state can exhaust memory and pin scans | Real for the registry reader (all values of every key read, no budget, no cancellation inside the loop, so a stuck read can hold the scan lease). | Registry reader reads only the four values it uses, with key-count and value-size budgets and cancellation checks. SQLite and cache paging left as a recorded Low residual (same-user only). |
| 9 | Low | Clearing a junctioned category can enumerate an external tree | Real: `EnumerateOwnedObjects` lists the root before checking whether the root itself is a reparse point (later exact deletion still refuses). | The root is checked first; a reparse-point root is reported unsafe and not enumerated. |
| 10 | Low | Using a pre-populated Chocolatey cache can race an elevated installer | Real (earlier review L1): `cacheLocation` and its ancestors are checked, its existing descendants are not. | The observer walks the `cacheLocation` tree with the HTTP-cache rule (bounded, every node unmodifiable by and not owned by the requester, no reparse points). |

## Fix pull requests

| Findings | Pull request | State |
|---|---|---|
| 7 | CI action pins | open |
| 1 | Release toolchain pins | planned |
| 2 | Bounded release ZIP extraction | planned |
| 3, 6, 8, 9 | App hardening | planned |
| 4, 10 | Chocolatey cache walk, test-key removal | planned |

Script changes to `New-PCStewardRelease.ps1`, `Publish-PCStewardRelease.ps1` and `Install-PCStewardRelease.ps1` change
the hashes the next owner release paste pins; the paste is regenerated per release as usual.
