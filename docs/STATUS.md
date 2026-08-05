# PC Steward build status

**Current phase:** Phase 4 — feature-change evidence readiness

**Current task:** Task 32 Chocolatey Community source and protected execution

**Reviewed implementation:** Task 32 implementation (fresh independent audit passed; live host evidence remains pending)

**Current branch:** `phase/4-beta`

**Phase gate:** **NOT READY — the Task 31 exact-hash exception expired on this implementation change**

## Task 32 scope and current boundary

The product owner explicitly promoted Chocolatey from the former Later connector scope and approved protected update execution. PC Steward now integrates only the fixed Chocolatey Community feed via a fixed ProgramData-root, Authenticode-validated `choco.exe`; custom configured sources, credentials, raw PowerShell/package scripts, installer paths and arbitrary source URLs cannot become PC Steward authority. Each Chocolatey update is machine-scope only, requires the existing review/UAC helper path, and is successful only after exact local re-inventory. No Chocolatey client was installed, queried, launched, or used to update an application during this task.

The prior Task 31 exception is expired by this code change. Every earlier `FRA-14–16` field/signing/distribution gap is again blocking, and Task 32 adds uncollected live Chocolatey discovery/update, UAC, timeout/cancellation and post-update evidence. The repository remains unsuitable for any distribution or release claim pending a new exact-artifact evidence task and independent audit.

## Prior audit outcome (superseded by Task 32 implementation change)

- `FRA-19` **Closed** — failed post-reset acknowledgement and successful acknowledgement with a residual marker both block before inventory load or Scan navigation; two executable scenarios pass.
- `FRA-20` **Closed** — historical lifecycle observations are bound to their complete old hash and no current-hash lifecycle claim is made.
- `FRA-18` **Closed** — focused reset evidence is accurately recorded as 25/25.
- `FRA-14` **Accepted exception** — current final-app/live/idle/process-tree/update/final-timeout performance remains `Unverified` but is non-blocking only for the exact owner-only candidate.
- `FRA-15` **Accepted exception** — current launch/update/replacement/task/reset/removal lifecycle remains `Unverified` but is non-blocking only for the exact owner-only candidate.
- `FRA-16` **Accepted exception** — signer/channel/recipient/support/distributable rollback inputs are omitted only because no distribution or other recipient is permitted.

The authoritative boundary is [Task 31 non-public evaluation exceptions](review/TASK_31_NONPUBLIC_EVALUATION_EXCEPTIONS.md). It expires on an implementation/hash change, another recipient, any distribution channel, upload/share/tag/publication, or a public/private-distribution/release-ready claim.

## Current verification

Task 32 focused Chocolatey fixtures: **6/6**. Release build: 0 warnings/errors. Full automated suite: **260/260** (55 Domain, 166 Application integration, 18 Architecture, 21 Security). The independent re-review passed: the original desktop process rejects a caller-writable/reparse/wrong-signer Chocolatey client before `runas`, while the helper revalidates canonical path, reparse state, WinTrust, exact signer and SHA-256 identity immediately before launch. The deterministic fixture workload also completed 30 local-inventory runs (median 1.85 ms, p95 2.12 ms) and 20 two-source online-check runs (median 15.54 ms, p95 16.10 ms); it is not final-app/live Chocolatey evidence. A user-initiated WizTree review on the preceding local beta was blocked by the required protected-helper layout before helper launch or provider execution; no application changed. The UI now classifies that `ProductFailed` result as failed rather than completed.

The earlier unsigned archive hashes are superseded and not evidence for this working tree. The new unsigned local archive `1F88E89C15AA5401BC7EB508502F94059C2CD51CDD546834AC185ABF00E35056` mechanically validates 97 payload files / 50,063,593 bytes, 15 components and 16 SBOM nodes / 32 edges. It has not been launched.

## Authority boundary

One owner-run local beta launch and a user-initiated WizTree review occurred after exact action-time consent. The protected helper rejected the non-installed beta path before helper launch or provider execution, so no installed application changed. No real scheduled task was created or removed, no owner data was reset or deleted, and nothing was signed, uploaded, shared or sent. The exception does not grant further actions. Every future launch or state-changing host action requires exact action-time approval.

## Next task

Any live Chocolatey discovery/update, elevation, or lifecycle evidence still needs exact action-time approval. The automated Task 32 implementation, fresh exact artifact and independent code audit are complete; a new exact-artifact evidence task is required before any release or distribution claim.
