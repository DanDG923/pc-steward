# ADR-008 — Licence and source availability

**Status:** Accepted
**Date:** 2026-07-28
**Decision owners:** Product owner
**Related requirements:** release definition of done and dependency licence review
**Related open decision:** `OD-08`

## Context

Repository visibility and product licensing affect dependency choices, notices and external distribution but not runtime trust controls.

## Options considered

### Option A — Private repository through private beta

Preserves decision time and matches the specification default.

### Option B — Public source under an approved licence

Improves transparency/contribution potential but needs legal and disclosure readiness.

## Decision

Accept Option A through private beta. The repository and builds make no public-source or open-source claim. Dependency selection must be compatible with private distribution, and any public repository or licence requires an explicit product-owner decision plus updated notices before publication.

## Product-owner decision — public source repository, 2026-11-06

The product owner decided to make the GitHub repository `DanDG923/pc-steward` public, so that Windows CI on GitHub-hosted runners can run without private-repository billing. This supersedes Option A for repository visibility only.

- **Licence:** PC Steward's own source is licensed under the MIT License (`LICENSE` at the repository root). Third-party components keep their own licences, and the payload notices above still apply to anything shipped. Changing the licence later needs a fresh product-owner decision, and it cannot withdraw MIT from code already published.
- **Distribution gate unchanged:** the product stays `NOT READY FOR DISTRIBUTION`. Making the source visible is not a release, does not publish any build, and does not change the private-beta notices, the payload licence acceptance above, or the signing position.
- **Before the switch:** a read-only audit of every branch and the full history found no credentials, private keys or signing files. Commit metadata (author name and email) and some local user paths, a machine name and one account SID in older evidence become public. History is not rewritten, because Task 50 and other plans pin exact commit hashes.
- **Controls:** `SECURITY.md` gives a private reporting route. The README says the code is pre-release and that the owner-only install scripts change certificate trust and ACLs. CI defaults to a read-only token, and workflows from outside contributors need owner approval before they run.

## Consequences

No public-source claim; dependency licences must still be compatible with private distribution.

## Task 90 product-owner engineering acceptance — 2026-07-30

The product owner accepted engineering use of the payload-scoped MIT, Apache-2.0, WebView2 BSD-style, and Microsoft redistribution terms for this private beta, conditional on shipping the mapped licence and notice texts. This explicitly includes the directly shipped `Microsoft.WindowsPackageManager.InProcCom` 1.10.340 native runtime under MIT and its package `NOTICE.txt`. This is a release-engineering scope decision based on the generated package evidence; it is not legal advice, a legal opinion, or a change to the repository's private-source status. Any new dependency, package version, public distribution, or changed redistribution condition requires a fresh review.

### Task 54B licence re-check — 2026-11-12

The owner's acceptance of [ADR-003 amendment 1](ADR-003-AMENDMENT-1-WINGET-COM-PROJECTION.md) (Option A, 2026-11-12) moves `Microsoft.WindowsPackageManager.ComInterop` and the directly shipped `Microsoft.WindowsPackageManager.InProcCom` runtime from 1.10.340 to 1.29.380, and names a fresh licence check as part of that change. Result: both 1.29.380 packages still declare SPDX `MIT` with the same copyright line and still carry `NOTICE.txt`, which ships verbatim in the generated notices as before. The notice file now lists the whole winget-cli component inventory (1,382 → 11,003 lines); every listed component licence is permissive (MIT, Apache-2.0, BSD, Zlib, BSL-1.0, HPND, curl, ISC, plus two scancode references for build-time metadata), with no copyleft component. The licence terms for this private beta are therefore unchanged and the acceptance above carries over to 1.29.380 on the same conditions. Details: [Task 54B record](../review/TASK_54B_WINGET_COM_1_29_ADOPTION.md) §4. Not legal advice.

### Release payload notices — 2026-11-12 (Phase C close-out)

The acceptance above is conditional on shipping the mapped licence and notice texts, but the release payload builder (`New-OwnerOnlyCandidate.ps1`) never wrote them: signed releases 0.1.0–0.1.3 carry no `THIRD-PARTY-NOTICES.txt` (gap found in Task 53A). From the next release the builder writes it into the payload before the manifest, using the same licence map as the private-beta package (now in `scripts/PCStewardThirdPartyNotices.ps1`). It also covers the .NET runtime that Native AOT compiles into the elevated helper (`Microsoft.NETCore.App.Runtime.NativeAOT.win-x64` and `runtime.win-x64.Microsoft.DotNet.ILCompiler`, both SPDX `MIT` with `THIRD-PARTY-NOTICES.TXT`), which the private-beta package never shipped. No licence term changes. Details: [Task 53 report](../review/TASK_53_PHASE_C_EVIDENCE_READINESS.md) §7.3. Not legal advice.

### Follow-up controls and tests

Maintain third-party notices and machine-readable dependency/licence inventory.

## Evidence the upcoming work must collect

- Task 06 dependency/licence constraints.
- Task 30 SBOM, licence scan, notices and distribution model.
- Product-owner approval before any repository visibility or licence change.

## Reconsider when

External beta/publication is planned or a dependency imposes incompatible obligations.
