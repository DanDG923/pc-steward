# Codex whole-repository security scan (2026-11-12): triage and fixes

Scan target: Git revision `e20c70d`, repository mode, static review only (no Windows runtime, signing, MSI or network
test). Ten reportable findings: one High, four Medium, five Low. This record checks each one against `phase/4-beta`
(`1530e9d`; none of the affected files changed since `e20c70d`) and tracks the fix. The scan report itself is the owner's
copy and is not committed.

| # | Severity | Finding (short) | Verdict on `phase/4-beta` | Fix |
|---|---|---|---|---|
| 1 | High | Signing a release payload can bless unverified SDK and NuGet inputs | Real. `global.json` allows a later SDK patch and the runner image may hold one; the release build restores packages (including the Native AOT runtime and ILCompiler packs compiled into the elevated helper) without checking their bytes; the signing script binds the run, artefact and commit but not the toolchain. | Release build uses the official SDK zip (SHA-512 pinned) and checks every restored package against committed SHA-512 pins before any project builds, with the package cache read-only to the build user; every PR gates on the pins; the signing script refuses a payload record without the matching toolchain pin digest. Residual: the GitHub-hosted runner image itself is trusted (no independent reproducible build). |
| 2 | Medium | Processing an authenticated release ZIP can exhaust the signing or install host | Real. `New-PCStewardRelease.ps1` uses `Expand-Archive` and `Install-PCStewardRelease.ps1` buffers each entry in a `MemoryStream` before the 512-file / 256 MiB manifest bounds run. Reaching it needs a compromised CI artefact (with a matching GitHub digest) first, and the effect is a failed or stalled host step, not code execution. | **Accepted, no change** (owner guidance 2026-11-12: a finding that needs the system already compromised is low priority). |
| 3 | Medium | Continuing after reset can reopen a substituted prior-state database | Real race (startup checks freshness once; Continue does not). Same-user code can already write the database at any other time, so no privilege boundary is crossed; update execution still needs fresh review and approval. | Continue re-runs the semantic fresh-store check immediately before the marker is acknowledged. |
| 4 | Medium | Keeping an owner-only test deployment lets same-user code reuse its trusted signer | Real in `Install-OwnerOnlyProtectedTestDeployment.ps1` (earlier review M2). No current exposure: the Task 50 cleanup (2026-11-12) found and left no owner-only test certificate, that deploy route is retired in favour of the signed MSI, and use of the key needs code already running as the owner plus the owner approving its UAC prompt. | **Accepted, no change** (retired route; owner guidance as for 2). If the route is ever revived, remove the key after signing first. |
| 5 | Medium | Approving a Chocolatey update can race into elevated CLR code loading | Same residual as Task 50 review M1, accepted by the owner on 2026-11-06 and recorded in ADR-025. | No change; acceptance stands. |
| 6 | Low | Copy diagnostic can expose unsanitized history identity fields | Real: only `diagnostic` passes through the redactor. | Every string field in the bundle is sanitized and length-bounded. |
| 7 | Low | Running ordinary CI executes mutable action tags | Real: `ci.yml` used `@v7`/`@v6` tags. | Every `uses:` in every workflow pinned to a full commit with a tag comment, enforced by an architecture test. |
| 8 | Low | Loading hostile local state can exhaust memory and pin scans | Real for the registry reader (all values of every key read, no budget, no cancellation inside the loop, so a stuck read can hold the scan lease). | Registry reader reads only the four values it uses, with key-count and value-size budgets and cancellation checks. SQLite and cache paging left as a recorded Low residual (same-user only). |
| 9 | Low | Clearing a junctioned category can enumerate an external tree | Real: `EnumerateOwnedObjects` lists the root before checking whether the root itself is a reparse point (later exact deletion still refuses). | The root is checked first; a reparse-point root is reported unsafe and not enumerated. |
| 10 | Low | Using a pre-populated Chocolatey cache can race an elevated installer | Real (earlier review L1): `cacheLocation` and its ancestors are checked, its existing descendants are not. It needs a requester-writable or reparse subfolder already left inside the protected cache (the attacker cannot create one there), then a timed swap. | **Accepted for now, no change.** The fix (walk the cache with the step-4 HTTP-cache rule) is small in the observer, but every helper-role fixture builds its cache as the test user, which that rule refuses, so the fixtures need rework first. Revisit with the next Chocolatey route change. |

## Fix pull requests

| Findings | Pull request | State |
|---|---|---|
| 7 | CI action pins | in this PR |
| 1 | Release toolchain pins | in this PR |
| 3, 6, 8, 9 | App hardening | in this PR |
| 2, 4, 5, 10 | Accepted | no change |

The change to `New-PCStewardRelease.ps1` changes the hash the next owner release paste pins; the paste is regenerated per
release as usual.

## Finding 1 in detail

`scripts/release-toolchain-pins.json` pins Microsoft's official SDK zip (URL, size, SHA-512; the version must equal
`global.json`) and every NuGet package the three release projects restore for `win-x64`, including the download-only Native
AOT runtime and ILCompiler packs (id, version, SHA-512 of the `.nupkg`). `Test-OwnerOnlyCandidateBuilderCI.ps1` now, on every
run (so every pull request exercises the release path):

1. downloads the pinned SDK zip into a new administrators-only folder and checks its size and SHA-512; that folder holds
   only this SDK and is first on `PATH` for every child, so `latestPatch` cannot pick a runner SDK;
2. has a first disposable standard user restore the three projects of a separate worktree into an empty cache;
3. removes that user, makes the cache administrators-only, and checks it: the restored graph equals the pins exactly, each
   `.nupkg` and its `.sha512` record match, every extracted file equals its `.nupkg` entry, and nothing else is in the cache.
   Any difference stops the run before a release project builds and prints the pin file a fresh restore would need;
4. has a second disposable user build with read-only access to the SDK and the cache, then checks that the build's restore
   records name exactly the checked set and the pinned SDK version.

A repository `nuget.config` clears the package sources down to nuget.org and clears fallback package folders. The first CI
run showed why: the hosted runner's machine-wide NuGet configuration added a second package folder to every release
project's restore, which a package could have come from without passing through the checked cache.

The pins hold 31 packages: the 30 the three projects use, plus one older `Microsoft.WindowsAppSDK.Foundation`
(1.8.260505001) that NuGet downloads only while resolving the graph (pin kind `resolution`). It is checked like the others.
Three of the pinned hashes were cross-checked against fresh downloads from nuget.org. CI run 43296391507 (head `a8d2f09`)
passed both builder jobs with `TOOLCHAIN-PACKAGES used=30 pinned=31 problems=0` and `TOOLCHAIN-OK sdk=10.0.403`.

The same run's `setup-dotnet` step installed SDK **10.0.404** under `latestPatch` for the other jobs, which shows the finding
was real: before this change a release payload could be built with a newer SDK than the one reviewed. Release payloads now
build only with the pinned 10.0.403.

The release record (`release-payload.json`) gains `toolchain.pinsSha256` and `toolchain.sdkVersion`;
`New-PCStewardRelease.ps1` refuses to sign unless they equal the pin file of its own clean clone at the payload commit.
Updating the SDK or any package now means updating the pin file in the same pull request (CI prints it).

**Residual (needs owner acceptance):** the GitHub-hosted runner image and the GitHub Actions service are still trusted; there
is no second, independent build to compare with.
