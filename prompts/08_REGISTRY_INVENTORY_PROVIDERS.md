# 08 — Registry inventory providers

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **Medium** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/1-inventory` |
| Run after | Task 07 complete |
| Expected commit | `feat: inventory Windows uninstall registry` |

**Primary requirement references:** `FR-INV-01`, `FR-INV-02`, `FR-INV-03`, `FR-INV-06`, `FR-INV-08`, `FR-INV-09`, `AC-INV-01`, `AC-INV-03`, `AC-INV-04`, `AC-INV-06`, `AC-INV-07`, `NFR-REL-05`

## Copy/paste prompt

```text
Task 08 — implement machine and current-user uninstall-registry inventory providers.

Requirements are listed under Task 08 in `docs/requirements/TRACEABILITY.md`.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- `docs/requirements/PHASE_1.md`
- directly relevant provider interfaces and persistence/domain code.

Implement read-only providers for:
- HKLM 64-bit uninstall view;
- HKLM 32-bit uninstall view;
- HKCU uninstall entries for the current user.

Capture safely available fields required by the specification, preserve raw records, and emit provider warnings for malformed data. Scanning must not require elevation, invoke MSI, query `Win32_Product`, enumerate unrelated registry areas, or fail the full provider because one entry is malformed.

Use an injectable/testable registry abstraction. Add fixtures for missing names/versions/publishers, malformed dates, oversized fields, duplicate keys, x86/x64 separation, and access-denied records.

Run targeted tests, then the full build/test suite. Update status and traceability.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
