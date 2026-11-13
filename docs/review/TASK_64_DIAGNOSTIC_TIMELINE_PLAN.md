# Task 64 — Bounded local diagnostic timeline: staged build plan and interfaces

**Status:** In progress (stages 1–4 of 5 built). Owner started Phase F on 2026-11-13 alongside the D4b update work; that instruction is the explicit order exception the Phase F brief requires (Phases D installs and E removals are paused by the owner). ADR-027 is Accepted; nothing here widens it.

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

## Stage 3 Windows readers (implemented, synthetic and CI-host evidence only)

`src/PCSteward.Diagnostics.Windows` (references Application only; never referenced by the scheduled host, helper or execution):

- **System and Application event logs** (`EventLogDiagnosticCatalog`, `WindowsEventLogQueryRunner`). One structured wevtapi query per channel, newest first, with one `Select` per allowlisted provider and event ID, each bounded by the inclusive time window: System — Kernel-Power 41 and EventLog 6008 (unexpected shutdown), WER-SystemErrorReporting 1001 (bugcheck); Application — Application Error 1000, Application Hang 1002, Windows Error Reporting 1001. Only channel, provider, event ID, record ID, `TimeCreated` and Kernel-Power 41's numeric `BugcheckCode` are rendered; no message text, event data, user or computer. Events are taken in batches of 16 with a 0.5 s wait, stopping at the slot count plus one; cancellation calls `EvtCancel`.
- **Reliability** (`ReliabilityDiagnosticCatalog`, `WmiReliabilityRecordSource`, `ReliabilityDiagnosticReader`). WQL on `Win32_ReliabilityRecords` in `root\cimv2` on this PC, naming six properties (log, record number, source, event identifier, time, product name), an inclusive window and the allowlist (application crash, hang, error report, unexpected shutdown, MSI install/failed install/removal, Windows Update install/failure). WMI is called through its own COM interfaces (locator by class ID; only `ConnectServer`, `ExecQuery`, `Next` and the property read are declared), with the impersonation level set on each proxy. Results are taken one at a time with a 250 ms wait, so cancellation is checked at least that often and a stalled provider cannot hold the reader. WQL cannot order, so the reader walks back from the end of the window in stretches that double from one hour (eight queries cover seven days) and keeps a stretch only when all its records fit. ADR-027's cap applies to every matching record materialized, so each query may materialize at most the slots left, counting the one extra record that proves more matched; the first stretch that doesn't fit ends the read and reports the source as truncated. The kept records are always every record in the newest part of the window (Codex review on #105). No message, user, computer or insertion strings; no WMI method call or write; never `Win32_Product`.
- **Windows Update history** (`WindowsUpdateHistoryCatalog`, `WuaHistorySource`). `Microsoft.Update.Session` → `CreateUpdateSearcher` → `GetTotalHistoryCount` and `QueryHistory` pages of 50, newest first, stopping at the first entry older than the window, the slot count plus one, or 2,000 inspected entries (reported as truncated). Projects title, operation, result and HRESULT. No search, download, install, hide or service change. `GetTotalHistoryCount`, `QueryHistory` and WMI's `ConnectServer` are synchronous with no timeout; each route allows one call at a time in the whole app, so an abandoned call is never joined by another and a later collection reports the source as not read while it runs (Codex review on #105).
- `WindowsDiagnosticReaders.Create()` returns the four readers for the stage 4 page. Anything the route returns outside the allowlist is passed on as `DiagnosticRecordCandidate.Unreadable`, so admission counts it as rejected and the source is reported as partial.

**For the stage 5 host check (not yet proven on the owner's PC):** the exact provider names and the Kernel-Power `BugcheckCode` value type on Windows 11; the Reliability source names and whether WMI returns newest records first when a source is truncated; that `IUpdateHistoryEntry.Date` is UTC; read access for a standard user. CI proves only that Windows accepts both event log queries.

## Stage 4 Diagnostics page (implemented, synthetic and CI evidence only)

`src/PCSteward.Application/DiagnosticsPresentation.cs`, `src/PCSteward.Desktop/MainWindow.Diagnostics.cs`:

- **Navigation.** `ShellPage.Diagnostics` is an eighth navigation item, after Activity (owner's choice of a new page; ADR-030's seven items plus this one). Activity says what PC Steward did; Diagnostics sets that beside what Windows recorded.
- **Consent each time.** "Collect a timeline…" opens a dialog naming the five sources in collection order and what each reads, what is never read, the 7-day lookback and the 30-day keep. Every source is ticked by default; Collect needs at least one. The dialog's answer becomes one `DiagnosticCollectionConsent` (policy version 1), spent by that collection. Nothing is remembered and nothing runs in the background.
- **Collection.** A fresh `DiagnosticCollectionOrchestrator` per consent (PC Steward history read under the common data gate, then the four stage 3 readers; the account, domain and computer names are the redaction tokens). It runs off the UI thread with "Stop collecting"; a stop or failure saves nothing. A completed snapshot is saved with the shown timeline protected from pruning; timelines removed early to make room are disclosed.
- **Timeline.** The newest saved timeline opens by itself: a summary (records, sources read, dates, kept until), a warning when partial or when nothing could be read (never a clean result), "What was read" with every source's result in words, then day groups newest first. Each record is a closed row (kind, subject, outcome, time) whose facts, the fixed unexpected-shutdown note and any PC Steward change within 15 minutes (with the fixed noncausal note) are inside. Records without a usable time are listed apart. Older saved timelines are listed under it.
- **Export.** "Export…" previews the record count and each source's result, warns that app names and error codes are included, then asks where to save. The file is one JSON object (`pc-steward-diagnostic-timeline`, format 1): policy version, times, every source result and each record in the stored canonical projection. Over 8 MiB it is refused, not cut.
- **Clear scan data** names saved diagnostic timelines on its card and dialog, and the page reads its list again afterwards.

## Out of scope here

Matching, update candidates, update execution, the Updates and Activity pages (owned by the D4b update threads), AI explanations (Phase G).
