# Standalone package security review

**Reviewer setting:** GPT-5.6 Sol / High  
**Date:** 2026-07-30  
**Scope:** product-owner-approved replacement of the traditional installer with a standalone Windows x64 ZIP  
**Verdict:** PASS for the revised architecture and automated boundary; release observations remain incomplete.

## Security decision

A caller-writable standalone package cannot safely ship the existing sibling elevation helper. The accepted package therefore operates as standard user only:

- production composition is `UpdateExecutionCapabilities.UserScopeOnly`;
- machine-scope preparation stops before structured execution authority or immutable job creation;
- the standalone executor has no elevated transport and independently rejects a machine-scope job;
- the desktop project and generated payload contain no `PCSteward.ElevatedHelper`;
- user-scope jobs retain exact source/ID/version, explicit confirmation, fixed WinGet arguments and post-update verification;
- read-only machine inventory remains visible, but cannot become a machine update action.

No arbitrary command/path/arguments, remote metadata field, database value or package mapping can restore elevation. The dormant installed-mode helper source remains constrained by its historical protected-root gate and is not packaged.

## Removal boundary

The bundled script accepts no path or task name, refuses while PC Steward processes are running and addresses only `PCSteward.ScheduledCheck`. It never deletes per-user data or package files. The user retains data by default, uses the existing two-confirmation factory reset for the remove-data choice, then deletes the extracted package folder.

## Evidence

- Focused standalone preparation, executor and composition regressions: 3/3 passed.
- Release build: 0 warnings, 0 errors.
- Full suite: 209 passed — Domain 55, Application integration 124, Architecture 10, Security 20.
- Generated ZIP contains desktop, scheduled host, cleanup script, XBF/PRI resources and no elevated helper.
- First staged payload crashed with WinUI `0xc000027b`; comparison found missing generated XBF/PRI resources. The script was corrected and the rebuilt payload remained running for a five-second on-machine launch observation.
- Exact scheduled task was absent; cleanup script completed twice with no error or state creation.

## Residual evidence

- approved signing and trusted-channel distribution;
- positive exact-task removal plus unrelated-task isolation;
- retain-data replacement and remove-data factory-reset/package-deletion sequence;
- real controlled user-scope update and on-machine machine-scope no-UAC presentation;
- visual-mode/icon observations and the accepted performance exception.

These are Missing release observations, not security design failures and not Pass claims. Task 30A must carry them into final evidence readiness. Any helper file in the standalone payload, production `ElevatedUpdateRunner` composition, machine-scope job creation or UAC prompt is a release blocker.
