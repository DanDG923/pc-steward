# Task 90 — process-attributed network evidence blocked

## Goal

Capture `AC-DAT-04` metadata only: process/parent PID, timestamp, destination host/IP and port, connector state, exit state, and dropped-event status for local-only and explicitly enabled production paths.

## Safe work completed

- In the Release desktop app, disabled both WinGet disclosures, completed a local-only scan, and verified the update-check control was disabled.
- Explicitly approved and enabled `WinGet package sources`, ran the production update check, then restored the disabled Local-only state.
- Ran temporary `netsh trace` InternetClient sessions with `capture=no`; packet capture was disabled. Deleted the temporary ETW/CSV artifacts after aggregate analysis.

## Attempts and exact blockers

1. Narrow metadata-only ETW session:

   `netsh trace start ... provider=Microsoft-Windows-Kernel-Process keywords=0x10 ... provider=Microsoft-Windows-TCPIP keywords=0x8600 ...`

   Result: `Failed to start the tracing session (error=5). The requested operation requires elevation (Run as administrator).`

2. Temporary 15-second `Win32_ProcessStartTrace` monitor restricted to `PCSteward.Desktop.exe` and `winget.exe` process/parent IDs and timestamps.

   Result: the monitor process did not create its constrained metadata file in this environment, so no process-attributed event data was available to join to the broad trace.

## Smallest suspected cause

The command sandbox can run `netsh trace` InternetClient sessions but does not provide a Windows-administrator token for the targeted process/TCP ETW providers. The background event-monitor launch is also unavailable under the current desktop/session boundary.

## Required next action

Run the same metadata-only trace under a controlled Windows-administrator token or approved endpoint instrumentation. Do not capture packets, payloads, package listings, or package identifiers.
