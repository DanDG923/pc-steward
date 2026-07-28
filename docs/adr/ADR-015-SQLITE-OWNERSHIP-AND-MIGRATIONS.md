# ADR-015 — SQLite ownership and migrations

**Status:** Accepted
**Date:** 2026-07-28
**Decision owners:** Implementation reviewer
**Related requirements:** `NFR-REL-02`, `NFR-MNT-04`, `AC-DAT-01`, `AC-DAT-06–07`

## Context

SQLite must preserve the last-known-good inventory, stable user decisions and append-oriented update evidence across UI and scheduled processes without becoming execution or deletion authority.

## Options considered

### Option A — Persistence-owned database with explicit migrations and units of work

All SQL, connection policy, staging promotion and schema evolution remain in one adapter.

### Option B — Direct SQLite access from use cases and hosts

This reduces initial interfaces but spreads transaction, migration and trust policy across the application.

## Decision

Accept Option A. `PCSteward.Persistence.Sqlite` exclusively owns the per-user database, SQL, connection creation, schema version and migrations. Hosts access it only through Application ports; ElevatedHelper never opens it.

Migrations are ordered, immutable after release and applied transactionally under an exclusive per-user migration lease before normal access. Development migrations provide tested down steps where reversible; release recovery uses a pre-migration backup and forward repair rather than silently downgrading user data. The adapter enables foreign keys, uses WAL with an explicitly tested durability setting, bounds busy time, and serializes writes through application units of work.

Scans write a staging snapshot and atomically promote it. Stable decisions are stored independently from disposable scan rows. Update jobs are immutable records with append-only events. Rows are untrusted on read: executable commands, free-form arguments and authoritative absolute deletion paths are forbidden.

Option B is rejected because it cannot reliably enforce transaction ownership, migration order or the rule that database data grants no authority.

## Consequences

### Positive

Transaction and recovery behavior is centralized, testable and consistent across hosts.

### Negative / trade-offs

Use cases require explicit repository and unit-of-work contracts; WAL and backup behavior require Windows filesystem tests.

### Follow-up controls and tests

Tasks 07/12 test clean creation, every migration, development reversal, failed-migration recovery, busy/concurrent hosts, cancellation before promotion, crash recovery and tampered rows. Clear scan data must be one transaction that preserves stable decisions and unrelated lifecycle categories.

## Reconsider when

WAL or the selected durability policy fails on a supported local filesystem, multi-process contention cannot meet reliability budgets, or a schema change cannot preserve upgrade data.
