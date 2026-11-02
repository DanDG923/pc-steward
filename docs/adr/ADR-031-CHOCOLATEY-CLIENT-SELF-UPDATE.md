# ADR-031 — Keeping the Chocolatey client itself up to date

**Status:** Accepted (owner, 2026-11-02): Option 2.
**Date:** 2026-11-02
**Decision owners:** Product owner and implementation reviewer
**Related requirements:** `FR-UPD-15`, `FR-UPD-01/03–09`, `TM-03/04/07/13`; ADR-025 (Chocolatey runtime-limits exception), Profile V2 ([TASK_50_CHOCOLATEY_PROFILE_V2.md](../review/TASK_50_CHOCOLATEY_PROFILE_V2.md))

## Context

The owner wants PC Steward to keep Chocolatey itself current, as good practice.

Today the Task 50 route trusts exactly one reviewed client release (2.7.5). The trust is per-file SHA-256, signer and engine file version, compiled into `ChocolateyReleaseManifest2_7_5` and `ChocolateyClientReleasePolicy`. The route deliberately refuses the `chocolatey` package as an update target (observer step 7, "client target refuses"), because the helper's safety checks rest on the exact client it holds while running. Letting that client replace itself through the same route would undo the check.

The owner's host has a different, unidentified client release (host evidence, eligibility finding), so the route cannot run there at all. Every future Chocolatey release would also break the single pin until a reviewed re-pin ships.

## Decision drivers

- Nothing unreviewed is ever executed or trusted. No release is trusted because remote metadata names it.
- Elevation happens only for an immutable, user-approved job, and success only after verification.
- Pins must keep pace with Chocolatey releases without weakening review.
- Smallest change to the reviewed Task 50 route.

## Options considered

1. **Manual only.** The owner updates Chocolatey by hand, and each new release needs a code re-pin. Simplest, but the route breaks on every Chocolatey release until a re-pin ships.
2. **A reviewed release list plus an in-app client update (recommended).**
   - PC Steward ships a *list* of independently reviewed Chocolatey releases, each with its full file manifest, signer and engine version, instead of a single pin.
   - When the installed client is a listed release and an equal or newer listed release exists, the app offers **Update Chocolatey to X**.
   - That runs as its own immutable, reviewed job: a review sheet, one UAC prompt, then the **trusted current client** runs a fixed compiled command that upgrades only the `chocolatey` package to exactly version X from the Community repository.
   - Afterwards PC Steward verifies that every installed client file matches X's reviewed manifest. Anything else is "Not verified" and the route stays closed.
3. **PC Steward installs the reviewed release nupkg itself.** This would work even from an unlisted client. It means executing a downloaded package's install script; the hash is pinned, but this is still a new installer path. It is more powerful and riskier, and is rejected for now.

## Proposed decision

Option 2:
- **Release list:** grows only through the same independently reviewed re-pin procedure used for 2.7.5, shipped with PC Steward releases. Each entry is the release's manifest table, the public nupkg size and SHA-256, the signer, and the engine version.
- **Client update job:** a dedicated compiled command, separate from package updates, built only from list data, never from remote metadata.
  - It is offered only from a listed, currently trusted client.
  - The target must be a listed release, normally the newest.
  - A downgrade is never offered.
- **Verification:** an exact manifest match for the target release. A restart-pending result is not accepted as success.
- **First alignment** from an unlisted client (the owner's host today) stays an owner action outside PC Steward, or happens through a reviewed re-pin that adds the installed release to the list.
- **No unattended updates:** the client update is user-initiated and reviewed, like every other job.

## Consequences

- The Profile V2 single-release policy becomes a list: `ChocolateyClientReleasePolicy`, the compiled manifests, observer step 2, and the engine-version check. The "client target refuses" rule stays for package jobs; the client update is a separate route with its own guard contract and tests.
- New review-sheet, Verify and history surfaces for "Chocolatey client update".
- Re-pin work happens per Chocolatey release, as an owner-approved PR with independent review.
- Host evidence (O5, A-Q1, A2-L3), the ADR-025 exact-artifact test and a v2 redeploy remain prerequisites. This ADR does not admit anything by itself.

## Owner decisions (2026-11-02)

1. Option 2 is accepted.
2. First alignment is done by the owner: the host's Chocolatey has been updated by hand to **2.7.5**, the release already on the reviewed list. Host evidence must be re-run to confirm the installed files match the 2.7.5 manifest; the earlier run predates the update.

3. **Runtime watchdog (2026-11-02):** option 1 is approved. The residual race remains, and the owner's acceptance of it is required separately before admission.
4. **Helper reply field (2026-11-02):** option W1 is approved, with these owner conditions:
   - a dedicated, strictly validated field in the helper reply;
   - it may only preserve or downgrade the result;
   - every required recovery warning is retained;
   - a missing or invalid field must never allow success;
   - explicit compatibility handling;
   - tests for a watchdog stop, a protection-release failure, and both combined.

   The Chocolatey self-update route stays disabled, pending the required review and the owner's separate acceptance of the remaining runtime risk.
5. **Outcome ordering (2026-11-02):** the owner confirmed that a Chocolatey self-update the watchdog stopped after it started is shown as **"not verified" with the recovery warning**, not "Failed". This is the §8.3 ordering Success > Failed > Unverified > Uncertain, where Failed→Unverified counts as a downgrade in assertion strength.
