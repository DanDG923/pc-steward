# Owner decisions and evidence waiting (updated 2026-11-02, midday)

Nothing has been merged, released or switched on. Both draft PRs are green on Windows CI: Task 50 (DanDG923/pc-steward#12) at `2461683`, and the Task 73 overhaul (DanDG923/pc-steward#13, stacked on #12) at `a0cb2ac`.

## Done today (owner requests)
- **Update all available** (SPEC §9.13 G4) and **Skip this version** (owner decision; SPEC amended). Both are reviewed, fixed and green. Elevation is one UAC prompt per machine-scope app.
- **ADR-031 accepted.** Owner alignment to Chocolatey 2.7.5 is confirmed by the evidence re-run, after the owner removed `Chocolatey.PowerShell.dll.old`. This was owner-reported; the next approved run must confirm it.
- **ADR-031 core** (release list, offer rule, client route, release-at-resume binding, exact postcheck) plus the **R5 guard and runtime watchdog** (option 1). Built, reviewed, hardened and green. Not wired end to end, not admitted.

## Decisions for the owner
1. **The Chocolatey self-update residual (R5)** needs a decision before that route could ever be admitted:
   - **(a)** accept the small residual race that remains after the guard and watchdog; or
   - **(b)** build **option 2** first: run the self-update under a non-user identity, which needs its own ADR. This closes the race.
2. **Profile V2 proxy and client-certificate disclosure:** does the ADR-025 runtime-limits text supersede it?
3. **Optional H3** (the runas/UAC probe) needs a separate approval when wanted.
4. **Merge order:** #12 first or together with #13. Merging needs explicit owner approval.

## Still to build (no owner input needed)
- The ADR-031 client-route helper item and stage machine, wire and dispatcher acceptance, desktop verifier wiring, and Desktop UI ("Update Chocolatey to X").
- Follow-ups: split `MainWindow.cs`; sanitizer coverage of Windows paths containing spaces.

## Owner-host evidence still needed before any release or admission
- The ADR-025 exact-artifact test of the signed helper, and a v2 redeploy (approval).
- A confirming evidence run (`matchesPinnedRelease: true` on 2.7.5).
- UI runtime checks: Narrator, keyboard, theme and high contrast, scaling, UAC accepted and refused, Save schedule refusal, clipboard after close.
- For the self-update: a real self-upgrade on a disposable VM once a second release is reviewed into the list.
- A Chocolatey-managed target package, if package updates through Chocolatey are wanted.
