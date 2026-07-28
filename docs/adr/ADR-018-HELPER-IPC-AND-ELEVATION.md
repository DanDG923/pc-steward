# ADR-018 — Helper IPC and elevation

**Status:** Accepted
**Date:** 2026-07-28
**Decision owners:** Security reviewer and implementation reviewer
**Related requirements:** `FR-UPD-04`, `AC-UPD-03–05`, `TM-04`

## Context

Machine-scope updates may require UAC, but the desktop must never become generally elevated or expose a reusable privileged endpoint.

## Options considered

### Option A — One-shot signed helper over an identity-checked per-request named pipe

Elevation is delayed until execution and the helper has one narrow provider capability.

### Option B — Elevated service, broker or desktop process

This simplifies reuse but creates ambient privileged state and a broader attack surface prohibited by the MVP.

## Decision

Accept Option A. The standard-user desktop creates a random per-request named-pipe endpoint restricted to the current user and SYSTEM, then launches the fixed installed helper path through the Windows `runas` verb only after confirmation. Launch parameters contain only protocol/bootstrap data: protocol version, requesting process ID, pipe locator and nonce; never provider operation data.

The helper connects once, verifies the pipe server process ID, fixed signed desktop identity, requesting user/session, protocol and nonce, then accepts one bounded canonical request matching ADR-017. The desktop verifies the connected helper process, fixed signed helper identity and session. The helper rejects unknown/duplicate/overlong fields, replay, expiry, paths, URLs, environment, working directory, output destinations and free-form flags. It reconstructs the allowlisted WinGet operation from trusted code, returns a bounded structured result and exits. UAC denial, cancellation, timeout and crash are distinct. No helper secret or executable request is read from SQLite.

Option B is rejected by the no-resident-elevated-process requirement and because persistence would enlarge the privilege boundary.

## Consequences

### Positive

More privilege has less capability, and each approval creates a fresh authenticated one-request channel.

### Negative / trade-offs

Windows process identity, signing, pipe ACL and teardown behavior require isolated-environment security evidence.

### Follow-up controls and tests

Task 22 must prove ACLs, server/client PID and token/session checks, signature/path validation, nonce/replay/expiry, mutation, UAC denial, process-tree cancellation, single-request lifetime and teardown. Task 25 treats any spoofing, arbitrary input or surviving helper as a release blocker.

## Reconsider when

The selected Windows APIs cannot prove both endpoint identities across UAC, signing is unavailable for external beta, or isolated-environment tests expose a replay/spoofing path. No persistent-service fallback is allowed without a new product and threat-model decision.
