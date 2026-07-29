# 30 — Packaging, release evidence, SBOM, and private beta preparation

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/4-beta` |
| Run after | Task 29 complete |
| Expected commit | `build: prepare signed-package evidence and private beta` |

**Primary requirement references:** `AC-DAT-18`

## Copy/paste prompt

```text
Task 30 — prepare the private-beta package and release evidence using the accepted packaging ADR.

Read the Phase 4 brief, packaging ADR, environment report, all phase reviews, and release definition of done.

Implement or produce:
- reproducible Release build scripts;
- package/installer configuration;
- development signing only when an appropriate certificate already exists or the user explicitly approves certificate creation/use;
- versioning and release notes;
- install, upgrade, launch, uninstall, retain-data, remove-data, scheduled-task cleanup, and first-run test procedures;
- SBOM and dependency inventory;
- static analysis, dependency vulnerability, secret, licence, and package-integrity reports using approved tooling;
- known limitations and privacy/security statement;
- a clean-VM evidence checklist.
- packaging checks for the selected PC Steward mark at 16, 24, 32, 48 and 256 px, correct display name/icon, light/dark/high-contrast launch, and no external font, icon-pack, WebView or browser-runtime dependency.

Do not publish, push, tag, upload, buy a certificate, or claim production signing. Do not mark a clean-VM test passed unless it actually ran and produced evidence.

Create `docs/release/PRIVATE_BETA_CHECKLIST.md`, `docs/release/KNOWN_LIMITATIONS.md`, and `docs/release/RELEASE_EVIDENCE.md`. Build the candidate and run all locally available release tests. Update status and traceability.
```

## Completion rule

Do not proceed to Task 30A until this task is committed and `docs/STATUS.md` points to Task 30A. Review tasks must pass; otherwise run Task 90 and repeat the same review.
