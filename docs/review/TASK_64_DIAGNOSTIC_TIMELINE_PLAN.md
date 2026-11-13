# Task 64 — Bounded local diagnostic timeline: staged build plan and interfaces

**Status:** In progress (stages 1–2 of 5 built). Owner started Phase F on 2026-11-13 alongside the D4b update work; that instruction is the explicit order exception the Phase F brief requires (Phases D installs and E removals are paused by the owner). ADR-027 is Accepted; nothing here widens it.

**Owner choices (2026-11-13):** the timeline lives on a new **Diagnostics** page in the navigation, not on Activity. Reliability is read through one narrow, time-filtered, cancellable WMI query unless the owner prefers it shown as unsupported in v1.

## Stages

| Stage | Scope | Host access |
|---|---|---|
| 1 | Domain records, policy arithmetic, time normalization, sanitizer/admission, canonical JSON, deterministic timeline and "occurred near"; Application reader port, one-shot consent and collection orchestrator | None |
| 2 | SQLite migration 13 (snapshot-owned tables), whole-snapshot 30-day expiry, 64 MiB oldest-first pruning, Clear scan data removes snapshots, Clear update history keeps them and says so, factory reset coverage, PC Steward history reader | None |
| 3 | System/Application event log readers (`EventLogReader`, XPath time + provider/event-ID allowlist), Reliability (`Win32_ReliabilityRecords`, time-filtered WQL), Windows Update history (WUA `QueryHistory`, read only) | Fixtures only; real reads need a separate approved host check |
| 4 | Desktop Diagnostics page: per-collection consent naming sources/fields/limits/retention, timeline with per-source coverage, incident selection, export preview and owner-chosen destination | None |
| 5 | Tasks 65/66 evidence and independent review; read-only host check on the owner's PC with approval | Owner-approved |

## Stage 1 interfaces (implemented)

Domain (`src/PCSteward.Domain/DiagnosticTimeline.cs`, `DiagnosticRecordAdmission.cs`):

- `DiagnosticSourceKind` (closed order: PC Steward history, System, Application, Reliability, Windows Update history), `DiagnosticSourceOutcome`, `DiagnosticSourceReason`, `DiagnosticEventKind`, `DiagnosticOutcomeKind`, `DiagnosticTimeQuality`.
- `DiagnosticCollectionPolicy` — ADR-027 v1 values; `OperatingSystemSlots(a) = min(500, floor((2000-a)/4))`; `ReaderBudget(remaining, n) = min(15 s, remaining/n)`; seven-day lookback ending at the captured start; 30-day expiry. A 5-second write reserve is held inside the 60-second deadline; a 16 KiB envelope reserve inside the 8 MiB budget.
- `DiagnosticTimeNormalizer` — UTC normalization keeping the original offset; DST-ambiguous/invalid local times become `Ambiguous` with no instant; times after the collection start are `Future` (never clamped); a source-reported conflict is `Conflicting`.
- `DiagnosticRecordCandidate` (reader output, untrusted) → `DiagnosticRecordAdmission.Admit` → `DiagnosticRecord` (stored). Admission checks closed source/kind/outcome sets, record-id charset/length, event-id range and time consistency, sanitizes every text field, and enforces 4 KiB of canonical UTF-8 JSON (shortening summary, then detail, then subject). Missing identities get a deterministic weak identity.
- `DiagnosticTextSanitizer` — strips control/format/bidi characters, collapses whitespace, replaces drive/UNC/variable paths, links, SIDs, email addresses and host-supplied device/account names, cuts by Unicode scalar.
- `DiagnosticRecordJson` — the one canonical projection used for size, weak identity and (stage 4) export.
- `DiagnosticSnapshot`, `DiagnosticSourceResult`, `DiagnosticTimelineBuilder.Build/NearbyChanges` — sort by UTC, source order, record id; untimed section; within-source de-duplication only; ±15 minutes inclusive; only known-time PC Steward completions are labelled; fixed noncausal wording for unexpected shutdowns and proximity.

Application (`src/PCSteward.Application/DiagnosticCollection.cs`):

- `IDiagnosticSourceReader` with `DiagnosticReadRequest`/`DiagnosticReaderResult`.
- `DiagnosticCollectionConsent` — selected sources, lookback (≤ 7 days), disclosed policy version; spent by one collection.
- `DiagnosticCollectionOrchestrator.CollectAsync` — sequential readers, app history first (≤ 15 s), OS readers with equal slots and time shares, per-reader deadline through `TimeProvider`, results from a reader that overran its share discarded, owner cancellation returns no snapshot, every source reported. It writes nothing.

## Stage 2 storage (implemented, schema 13)

`src/PCSteward.Application/DiagnosticSnapshots.cs`, `src/PCSteward.Persistence.Sqlite/SqliteDiagnosticSnapshots.cs`:

- Tables `diagnostic_snapshots` (one row per snapshot, fixed-width UTC text, `payload_bytes`), `diagnostic_source_results` (one row per source, closed names, counts) and `diagnostic_records` (snapshot id, ordinal, source, record id, canonical JSON). Records and source results cascade only from their snapshot. No foreign key to `update_job_history`: captured app-history rows are snapshot-owned copies (ADR-027 R1).
- `IDiagnosticSnapshotStore.SaveDiagnosticSnapshotAsync(snapshot, now, protectedIds)` — one transaction: delete expired, insert, then delete the oldest snapshots not protected until the total is at most 64 MiB; if only protected snapshots would remain over the limit, nothing changes (`CapacityHeldByProtectedSnapshots`). The result reports expired and capacity-pruned counts so the UI can disclose shortened retention.
- `List`/`Load` exclude snapshots whose expiry has passed, even if not yet deleted. `Load` is whole or nothing: each row must re-serialize byte-identically (`DiagnosticRecordJson.TryParse`) and pass `DiagnosticRecordAdmission.IsValidStored`; counts and `payload_bytes` must match, otherwise `Unreadable`.
- `DeleteExpiredDiagnosticSnapshotsAsync` is bounded opportunistic maintenance at start or use; there is no scheduler.
- Clear scan data deletes all three tables and counts snapshots; Clear update history leaves them; factory reset deletes the database file set.
- `IDiagnosticAppHistorySource` + `PcStewardHistoryDiagnosticReader`: update history in `[from, to]` newest first, limited to the record slot plus one to detect more; subject is the current display name, falling back to the package ID.

## Out of scope here

Matching, update candidates, update execution, the Updates and Activity pages (owned by the D4b update threads), AI explanations (Phase G).
