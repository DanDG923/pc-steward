# Phase 3 approved host-test exception

**Approved by:** Product owner in the Codex task, 2026-07-28  
**Status:** Active only while collecting Task 24A evidence  
**Expiry:** Task 25 review handoff; this exception is not a production-signing, update, or deployment approval.

## Scope

The product owner approved replacing the disposable-VM environment with this PC for the following isolated, host-only checks:

- Create a temporary self-signed code-signing certificate, trust it only for the current user, sign only disposable test copies of the desktop harness and elevated helper, and remove the certificate and trust entry afterward.
- Run a UAC acceptance and denial exercise for a no-op helper path. The acceptance path sends a deliberately malformed framed request after the authenticated pipe connects, requiring the helper to exit before it can construct or invoke WinGet. No update job, installer, provider cache, or real update is permitted.
- Create a scratch VHD and symbolic-link/race fixtures only beneath a newly generated temporary test root. Detach the VHD and remove all fixtures after evidence capture.

## Controls and boundaries

- Each run creates a unique directory beneath the current user's temporary directory. The run records the exact root before any removal and deletes only that resolved directory.
- The VHD is newly created in that root, carries only a test sentinel, is mounted only beneath that root, and is detached before the root is removed.
- The UAC test copies are separate from product outputs. The temporary certificate is neither retained nor used for production artifacts.
- Any UAC denial is recorded as a denial result; no attempt is made to bypass, automate, or suppress the prompt.
- This exception does not authorize a real or fixture software update, modifying provider caches, changing application data outside the temporary root, persistent trust configuration, or broad host cleanup.

## Residual risk

Host evidence is not equivalent to a clean Windows 11 VM. It cannot establish clean-image behavior, and it does not replace the still-missing real-update verification or desktop cleanup UI evidence. The Task 25 reviewer must retain those gaps unless separately approved and evidenced.

## Execution record — 2026-07-28

- A newly generated self-signed code-signing certificate (`D074528B22D2EAC3534564C903FCB4A66470568C`) signed only disposable copies of `PCSteward.Desktop.exe` and `PCSteward.ElevatedHelper.exe`. Both signatures verified as `Valid` before testing.
- UAC acceptance passed: the elevated helper connected to the signed desktop peer and exited `24` after receiving a deliberately malformed `{}` frame. Exit `24` is after the helper's mutual PID/user/session/path/signature/signer verification and before request acceptance, so no provider construction, WinGet command, installer, cache, or update job occurred.
- UAC denial passed: the harness returned `UAC-DENIED`; no helper process or provider job was started.
- The elevated scratch-root test passed 10 assertions: file and directory symbolic links, a deterministic post-validation swap, and a mounted 32 MB scratch VHD all failed closed while their sentinels survived.
- Teardown passed: zero `pc-steward-uac-evidence-*`/`pc-steward-host-evidence-*` temporary roots, zero `PCStewardEvidence` volumes, zero disposable helper processes/pipes, and no certificate entry in either `CurrentUser\\My` or `CurrentUser\\Root`.

This record proves only the approved pre-provider host substitute. It does not prove per-user no-prompt behavior, provider-disconnect cancellation, a real/fixture update's verification result, clean-image behavior, unconstrained concurrent ancestor races, or the desktop cleanup UI paths.
