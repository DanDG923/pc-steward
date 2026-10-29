# PC Steward 2.0 — Roadmap proposal

Status: **AGREED v2 — signed off by both reviewers.** Drafted by Claude Opus 5.5. GPT-6.1 Sol reviewed it (approve with changes), all changes were applied, and Sol gave final sign-off on 2026-10-29.
Supersedes: ChatGPT "PC Steward 2.0 — Complete Application Overhaul" (2026-10).
Basis: joint review in `PC_STEWARD_2.0_PROPOSAL_REVIEW.md`.

## 0. How to use this document

This is a **roadmap and requirement-delta proposal**, not an execution prompt. Do not paste it into an agent and ask for the whole thing.

1. Copy this file into the repo at `docs/product/PC_STEWARD_2_ROADMAP.md` and commit it.
2. Run **Task 43** (prompt in §9) in a fresh Codex chat. It only produces the evidence-backed delta table, the proposed requirement changes, the open decisions and a task outline.
3. Review Task 43's output and answer the owner decisions (§8).
4. Only then, run the follow-on documentation tasks. They draft the ADRs, phase briefs and numbered prompts. Then run one implementation task per fresh chat, following `AGENTS.md`, with the phase gates.

---

## 1. Mission

Grow PC Steward step by step, from a verified **update** manager into a verified **software manager** for one owner's Windows 11 PC, with local diagnostics and optional AI explanations. Use Patch My PC Home Updater as the usability benchmark for software management only. Do not copy its code, catalogue data or assets.

The existing repository, specification and ADRs are authoritative. **Existing code is kept unless a concrete, recorded defect justifies replacing it.** This is not a rewrite.

## 2. Starting point (keep)

As of Task 42 (`phase/4-beta`, 290/290 tests). The Task 42 source has been validated locally but is not yet deployed to the protected install:

- Stack: C#/.NET 10, WinUI 3 / Windows App SDK 1.8, SQLite, WinGet COM (ADR-003). This already matches the original proposal's target stack.
- 13 projects: Domain, Application, Persistence.Sqlite, Inventory.Windows (registry and MSIX), Provider.WinGet, Provider.Chocolatey, Execution, Storage, Scheduling.Windows, ScheduledHost, ElevatedHelper, Diagnostics, Desktop.
- Conservative identity matching and confidence; deterministic version comparison; pins, ignores and manual decisions.
- Immutable, reviewed update jobs (ADR-017). One-shot elevated helper (ADR-018). Sequential batches, including restart-required outcomes, with success claimed only after post-update verification (`UpdateBatchWorkflow.cs`).
- Scan-only scheduled host with no resident process (ADR-020). Notifications.
- App-owned artifact cache, Clear scan data and Factory reset with reparse-safe deletion (ADR-019).
- Task 35 "Steward Ledger" UI: five destinations and an Activity ledger.
- Owner-only protected deployment with a temporary signer (ADR-022). Live WinGet update evidence (WizTree).

## 3. Retained boundaries

These still apply. Changing any of them requires a new or amended ADR approved by the owner. A roadmap line is not enough.

- WinGet COM is authoritative for WinGet candidates (ADR-003). Task 32 separately authorises the fixed Chocolatey Community connector and its protected machine-scope update path.
- Never execute a command string, executable path, package ID or arguments obtained blindly from remote metadata or the database. Identifiers must be validated and freshly re-bound. Never use `Win32_Product`.
- Scanning never elevates. Elevation happens only for an immutable job the user has approved. There is no resident or elevated service.
- Low-confidence or incomparable matches can never be acted on. "Up to date" requires positive evidence.
- Success only after verification. No promises of atomicity or universal rollback. Partial batches are reported honestly.
- Deletion is limited to PC Steward's own allow-listed roots. Clear scan data, Delete downloaded files and Factory reset keep their separate meanings.
- No drivers, firmware, BIOS/UEFI, Windows Update installation, registry cleaning or "speed-up" claims.
- AI never makes a trust or execution decision.
- Respect organisational management (Intune, Group Policy) and explain any restriction rather than bypassing it.

## 4. Scope

### 4.1 In scope for 2.0 (in this order)

| WS | Workstream | Summary |
|---|---|---|
| WS1 | Owner-only redeploy | Deploy the Task 42 source to the protected install and validate it there. This is a separately authorised host action. |
| WS2 | Updater gap closure | Audit the §5 checklist against requirements and code. Implement only confirmed gaps. |
| WS3 | Catalogue installation | Search WinGet and install manually by exact package ID, through an explicitly extended reviewed-job contract. |
| WS4 | Manual uninstall | Remove one freshly resolved installed instance at a time, under a separate operation contract. |
| WS5 | Automation ADR | Draft and decide only. Covers user-scope, per-package allow-listed automatic updates. Not implemented in 2.0 unless the ADR is accepted and a later task is approved. |
| WS6 | Diagnostic timeline | A local, read-only evidence timeline of crashes, shutdowns, failed updates and PC Steward changes. |
| WS7 | AI explanation | One opt-in provider explains a failed update or a diagnostic timeline, using previewed, redacted evidence, within a budget. |

### 4.2 Deferred (each needs its own scope, threat model and test plan that works on this host)

Unattended or scheduled installation (implementation), signed-out and machine-scope automation, generic cleanup (temp files, browser caches, Recycle Bin, leftovers after uninstall), changing startup items, bulk uninstall, all-user or provisioned MSIX removal, installation profiles, Scoop/vendor/portable providers, Store installation or management beyond the existing Store-via-WinGet information, hardware metrics beyond basic OS/CPU/memory/disk, temperature sensors, crash-dump parsing, health scores, conversational "Ask PC Steward", dual-provider AI, AI release-note summaries, vulnerability awareness, self-update and installer packaging, public or other-recipient distribution.

### 4.3 Never

Arbitrary website/portal/mirror downloads, AI-generated commands or scripts, automatic credit purchase or auto-recharge, bypassing integrity or signature checks, unattended Windows reset.

## 5. Workstream requirements

Keep existing requirement IDs and their meanings. Give new IDs only to genuinely new requirements. Suggested prefixes: `FR-CAT-*`, `FR-UNI-*`, `FR-DIA-*`, `FR-AI-*`, `FR-BUD-*`.

### WS1 — Owner-only redeploy
- Use the existing ADR-022 installer and validator. Preserve the database with a backup and an integrity check.
- Accept on: the validator passes, the installed binaries match the commit build, the database is unchanged, and the app launches responsive under action-time owner approval.
- Out of scope: FRA-14–16 release evidence, signing, distribution.
- WS1 does not depend on the planning tasks. It can run whenever the owner authorises it.

### WS2 — Updater gap closure
Audit this checklist against requirements (`FR-UI-01–07`, `FR-UPD-05–11`, `FR-POL-01–05`, `FR-HIS-01–06`) and the implementation. Classify each item Exists / Partial / Missing, with file references. The existing status groups, policies, history, batch outcomes and restart-result handling are capabilities you start with. Do not presume them to be gaps.

Checklist:
1. Status groups: Updates available / Up to date (positive evidence only) / Unknown / Excluded / Failed or attention.
2. Operation states: queued, preparing, downloading, verifying, waiting for app closure, installing, verifying installation, completed, failed, cancelled, restart required.
3. Detecting that an app is in use, with a warning. Never terminate an app without explicit permission.
4. A silent or interactive installer choice, where the provider supports it.
5. Restart-requirement detection and display.
6. Per-app preferences: exclusions, pins, skip.
7. Progress, history, failure reporting, last successful scan and counts.
8. Update All and multi-select batch, with partial-outcome reporting.

Uncertain until audited: completeness of running-app detection and the silent/interactive choice. Implement only confirmed gaps, each as its own task with tests, reusing the Task 35 components.

### WS3 — Catalogue installation
- Search only enabled, consented WinGet sources through COM. Results show name, publisher, ID, version, source, and architecture/scope if known. Store information support is distinct from Store installation, and Store installation stays deferred unless authorised separately.
- Missing icons, categories or sizes are shown as **unavailable**. Tags are shown as tags, never as authoritative categories. No curated list in 2.0 (see owner decision D1).
- The install ADR must explicitly extend the update-only operation and helper contracts (ADR-017/018):
  - User-scope installs remain unelevated.
  - Machine-scope installs require fresh approval bound to that scope, and the protected one-shot helper independently revalidates the install job.
  - Execution is blocked if the scope is unknown, an existing installation is ambiguous, or the reviewed scope cannot be enforced.
  - "Not installed" must be established by a fresh inventory, not assumed.
- Before approval, a fresh review binds source, exact ID, version, scope and architecture. Success requires a verified installed state after re-inventory.
- Install queue: sequential, with the same partial-outcome reporting as batches. Favourites and recently installed are stored locally.

### WS4 — Manual uninstall
- Bind each uninstall to one freshly resolved installed instance: provider identity, scope, architecture and the applicable user. An exact catalogue ID alone is not enough when more than one instance matches, and ambiguity blocks execution.
- Supported routes only: WinGet by exact, instance-bound ID, or MSIX removal of the current user's registration only. Excluded: all-user removal, provisioned-package removal, frameworks, dependencies, and protected or system packages. Arbitrary registry `UninstallString` execution is excluded unless a separate ADR defines safe handling.
- Protected elevation may be extended only through an approved uninstall contract.
- A warning when the app is in use, and explicit confirmation. Verify the selected instance has been removed, reporting removed / still present / restart pending / unknown honestly.
- Leftover cleanup is out of scope.

### WS5 — Automation ADR (decision only)
- Candidate design: auto-update only **user-scope** WinGet packages that the owner has individually allow-listed. No elevation.
- The ADR must define:
  - durable consent bound to package ID, source and an optional version ceiling or major-version pin, and protection of that consent's integrity;
  - binding each run to fresh evidence;
  - revocation;
  - maintenance windows;
  - failure, retry and back-off handling;
  - notifications;
  - how ScheduledHost would change from scan-only.
- It must record what user scope does **not** limit (what an installer does once it runs).
- Outcome: accept, reject or defer. Implementation is a later, separately approved task. ScheduledHost stays scan-only until then.

### WS6 — Diagnostic timeline
- Read-only and bounded. Sources: the System/Application Event Log (e.g. Kernel-Power 41, EventLog 6008, BugCheck 1001, WER application faults), Reliability records, Windows Update history, and PC Steward's own install/update history.
- Check access on this host. Show records that are missing or denied as such. Never elevate a scan.
- Deterministic correlation: build the timeline and nearby changes, labelled as "occurred near", not "caused by". Kernel-Power 41 proves an unexpected shutdown happened, not its cause.
- Before implementation, the owner reviews requirements that define:
  - collection consent;
  - bounded lookback, count, size and retention;
  - the minimum fields stored;
  - redaction;
  - exports;
  - deletion acceptance criteria.
- Clear scan data removes the collected diagnostic snapshots while preserving update-job history (`FR-DAT-07`). Factory reset removes all diagnostic state owned by the app. Neither deletes Windows event logs or Reliability records.

### WS7 — AI explanation
- One provider first. The owner picks OpenAI or Anthropic (decision D2). A provider abstraction allows adding the second later. Use official SDKs; model IDs and pricing are configurable and checked at implementation time.
- Opt-in, per-provider consent, and an editable preview of exactly what will be sent. Redact usernames, profile paths, serials, device IDs, emails and tokens. Never send dumps or full logs. The UI states that preview and redaction cannot guarantee every secret is removed.
- Credentials: protected for the current user (DPAPI or Credential Manager). Never stored in config, exports, backups or logs. Removed on disconnect and on Factory reset.
- Egress: only to approved provider endpoints, over TLS, without forwarding credentials across redirects. Input/output tokens, retries, concurrency and timeouts are all bounded.
- Event logs and release notes are treated as untrusted data (prompt-injection resistant). Output is advisory only: no tools that change the system, and no action becomes eligible because of model output.
- Budget (`FR-BUD`):
  - a monthly app budget and a per-request cap, set by the owner in a stated currency;
  - a conservative cost reservation before each request, persisted transactionally so it survives a crash;
  - reconciliation against the usage the provider reports, with any reservation whose billing is uncertain kept until reconciled;
  - stop when the budget is exhausted, and block paid calls when pricing is unknown;
  - the UI makes clear the provider's billing is authoritative and the app only tracks its own spending;
  - no assumed promotional credit amounts.
- The app is fully functional with AI disabled or without keys. Tests use mock providers. Any live test needs a tiny, owner-approved budget.

## 6. Testing on this host

- VM testing is excluded (owner direction). Windows 11 Home has no Windows Sandbox or Hyper-V.
- Use: unit and fixture tests, fake providers, failure injection, and isolated fixture roots for storage.
- For install and uninstall: a short owner-approved list of small, harmless test packages, each live run approved separately.
- Any destructive feature without a viable plan on this host stays deferred.
- Windows-specific claims require evidence gathered on Windows. Performance claims require measurements (ADR-006 harness).

## 7. Phase order and gates

| Phase | Content | Gate |
|---|---|---|
| A1 | Task 43: delta table, proposed requirement changes, open decisions, task outline | Owner accepts the deltas and answers D1–D5 |
| A2 | Follow-on docs tasks: operation/security ADRs (Proposed), phase briefs, numbered prompts | Owner accepts the ADRs |
| B | WS1 owner-only redeploy (may run any time once authorised) | Validator and launch evidence |
| C | WS2 updater gaps; WS5 ADR decision drafted alongside | Phase review (Sol) |
| D | WS3 catalogue install | Security review gate (Sol, Extra High) and one owner-approved live install |
| E | WS4 manual uninstall | Security review gate and one owner-approved live uninstall of a test package |
| F | WS6 diagnostic timeline | Phase review |
| G | WS7 AI explanation and budgets | Security/privacy review gate and an owner-approved, budgeted live call |
| H | Reconsider the deferred list | Owner decision |

Security, migration, performance and traceability checks happen in every phase, not just at the end. A failed review runs Task 90 remediation and repeats the same review.

## 8. Owner decisions needed

- **D1** Catalogue categories: show WinGet tags only (recommended), or allow a small curated category map (which reverses the original "no curated list" rule)?
- **D2** First AI provider: OpenAI or Anthropic?
- **D3** The list of harmless live test packages for install and uninstall. Task 43 proposes candidates.
- **D4** Retention defaults for diagnostic snapshots and AI conversation history.
- **D5** AI budget currency, monthly limit and per-request cap.

## 9. Task 43 prompt (first task to run)

# 43 — PC Steward 2.0 scope reconciliation (delta only)

| Setting | Use |
|---|---|
| Model | **GPT-6.1 Sol** (supersedes the historical GPT-5.6 prompt defaults) |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | new `phase/5-v2-planning` from the current `phase/4-beta` head (record the base commit) |
| Run after | `docs/product/PC_STEWARD_2_ROADMAP.md` is committed by the owner |
| Expected commit | `docs: reconcile PC Steward 2.0 roadmap scope` |

**Primary requirement references:** existing `FR-UI-01–07`, `FR-UPD-01–13`, `FR-POL-01–05`, `FR-HIS-01–06`, `FR-SCH-01–04`, `FR-DAT-06–15`, `FR-ID-09`, `FR-SRC-02`, `FR-SRC-07–09`, and spec §4.3 non-goals. Proposed new prefixes: `FR-CAT`, `FR-UNI`, `FR-DIA`, `FR-AI`, `FR-BUD`.

## Copy/paste prompt

```text
Task 43 — reconcile the PC Steward 2.0 roadmap with the authoritative spec, ADRs and traceability. DELTA ONLY.

Documentation-only task: no production code, app builds, launches, deployments, updates, source refreshes or host changes. The usual build/test requirement is exempt because no code changes; state this in the completion report.

Read AGENTS.md, docs/STATUS.md, docs/product/PC_STEWARD_2_ROADMAP.md, and only the relevant sections of docs/product/PC_STEWARD_PRODUCT_SPEC.md, docs/requirements/PHASE_4.md, docs/requirements/TRACEABILITY.md and docs/adr/ (especially ADR-003, 017, 018, 019, 020, 022).

Produce docs/product/PC_STEWARD_2_DELTA.md containing:
1. Delta table: for every WS1–WS7 item and every Deferred item, list the existing requirement IDs, ADRs and code paths that already cover it, and classify it as Exists / Partial / New / Reverses-existing-decision.
2. WS2 audit: classify each item in the roadmap's WS2 checklist as Exists / Partial / Missing by reading the code, citing file paths. Record only confirmed gaps.
3. Proposed requirement changes: preserve existing IDs and meanings; allocate new IDs only for genuinely new requirements; for each proposed requirement give Yes/Later/Never status and draft acceptance criteria; where 2.0 would change a non-goal, mark it explicitly as a proposed amendment citing the roadmap. Do NOT edit PC_STEWARD_PRODUCT_SPEC.md yet.
4. Required ADR list: which new or amended ADRs WS3, WS4, WS5, WS6 and WS7 need, each with a one-paragraph problem statement and key threats. Do NOT draft the ADRs.
5. Task outline: proposed numbered tasks (44+) with one-line outcomes, phase, model/reasoning and gates. Do NOT write the prompts.
6. Live test candidates: 3–5 small, harmless WinGet packages suitable for owner-approved install/uninstall evidence, with justification. Do not install or query-run them.
7. Open questions and owner decisions D1–D5 plus any new ones.

Anything uncertain goes into open questions rather than a requirement. Do not expand scope beyond the roadmap. Update docs/STATUS.md (current task, next task = owner review of Task 43) and add a Task 43 entry to TRACEABILITY.md. Review the diff for scope creep, commit, and return the standard completion report.
```

## Completion rule

Do not start any follow-on documentation or implementation task until the owner has accepted Task 43's delta and answered D1–D5. A failed or disputed delta is corrected in the same task; the existing Task 90/91 remediation and escalation rules still apply.
