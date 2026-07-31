# Task 90 remediation plan

1. `FRA-01` — enforce the reset marker contract and shared data-operation lease across desktop startup, scan/source/update/verification/cleanup and scheduled-host work; add asynchronous contention regressions.
2. `FRA-02` — make exact scheduled-task removal/COM verification language-independent and retain the reset marker on every scheduler residual.
3. `FRA-03` — verify a fresh post-reset store and route the next desktop launch through explicit first-run onboarding without an automatic scan.
4. `FRA-04` — strengthen package signing verification/reporting without creating or trusting a release identity; retain the human signer/channel gate.
5. `FRA-05` — retain the unexecuted current-user update observation as a human-approved, safe-candidate gate.
6. `FRA-06` — inspect and reduce defensible idle UI work, then collect only reproducible local measurements; retain the agreed-baseline gate.
7. `FRA-07` — execute safe disposable standalone lifecycle checks and record their limits; retain supported-host scheduled/lifecycle evidence if not executable safely.
8. `FRA-08` — replace the About placeholder and correct user-scope/support/limitations release text.
9. `FRA-09` — make package manifests portable and payload-scoped; retain licence-review approval until a human reviews it.
10. `FRA-10` — retain declared WebView assets unless a supported publish exclusion is available; otherwise obtain the required architecture/design decision and launch smoke.

## Final reconciliation

- Regression evidence is Release build 0 warnings/errors and full suite 237/237 (55 Domain, 145 Application integration, 17 Architecture, 20 Security), including reset/scheduler, clean-profile and UI-thread ordering regressions.
- The production path updated current-user VS Code 1.129.0 to 1.130.0 from the official WinGet community source with no observed UAC or helper launch and exact post-update inventory verification.
- The final unsigned local-only archive has 94 payload files, 15 package components, 16 SBOM dependency nodes/32 edges, complete notices, and SHA-256 `20617AEFCFE42822534060CC7902BECFF5911D5B27C5F2274449A84935272FBF`. Mechanical validation and packaged launch/About/close smoke passed.
- `PO-EXC-90-SIGN-LOCAL-ONLY`, `PO-EXC-90-PERF-BOUNDARY`, and `PO-EXC-90-RESET-LIFECYCLE-OMISSIONS` close the named Task 90 dispositions only. They do not prove omitted observations or authorize external distribution.

Task 90 is complete and may be committed. The next permitted task is a fresh Task 31 audit under GPT-5.6 Sol / Extra High; the release gate remains NOT READY until that audit returns READY.
