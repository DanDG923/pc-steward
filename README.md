# PC Steward Codex execution pack

> **Pre-release source. Not for distribution or general use.**
> PC Steward is an unfinished, local-first Windows 11 app for keeping installed software up to date. No release is supported.
> The source is licensed under the [MIT License](LICENSE) (see [ADR-008](docs/adr/ADR-008-LICENCE-AND-SOURCE-AVAILABILITY.md)). It comes with no warranty.
> The owner-only install and test scripts under `scripts/` create and trust code-signing certificates, change ACLs under `%ProgramFiles%` and need an elevated session. Do not run them on a machine you care about.
> To report a security problem, see [SECURITY.md](SECURITY.md).

Start with [`CODEX_BUILD_PLAYBOOK.md`](CODEX_BUILD_PLAYBOOK.md).

## Contents

- `AGENTS.md` — compact durable instructions automatically available to Codex.
- `.codex/config.toml` — Terra/Medium default, Sol review model, low verbosity, Standard speed, workspace sandbox.
- `docs/product/` — approved specification, project instructions, and decision log.
- `docs/requirements/` — phase-sized requirement briefs and complete traceability.
- `docs/adr/` — ADR template and, later, accepted architecture decisions.
- `docs/architecture/` — architecture, threat model, and execution evidence created by Codex.
- `docs/STATUS.md` — compact handoff between fresh chats.
- `prompts/` — exact numbered prompts.
- `spikes/` and production solution folders are created during the numbered workflow.

## Production UI evidence

- [`docs/UI_CURRENT_STATE_AUDIT.md`](docs/UI_CURRENT_STATE_AUDIT.md) — pre-redesign production audit.
- [`docs/COMPETITOR_UI_RESEARCH.md`](docs/COMPETITOR_UI_RESEARCH.md) — current official-source research across ten comparable products.
- [`docs/SELECTED_UI_DIRECTION.md`](docs/SELECTED_UI_DIRECTION.md) — selected Steward Ledger direction and decision rationale.
- [`docs/UI_DESIGN_SYSTEM.md`](docs/UI_DESIGN_SYSTEM.md) — production token, component, shell, status, and maintenance contract.
- [`docs/UI_REDESIGN_CHANGELOG.md`](docs/UI_REDESIGN_CHANGELOG.md) — before/after implementation record and preserved behaviour.
- [`docs/UI_VALIDATION_REPORT.md`](docs/UI_VALIDATION_REPORT.md) — exact build, test, performance, real-app, screenshot, accessibility, and limitation evidence.

## Non-negotiable workflow

Use one fresh chat per numbered task, one phase branch at a time, Standard speed, and the recommended model. Do not ask Codex to build the whole app in one turn.
