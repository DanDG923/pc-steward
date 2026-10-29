# PC Steward — Product Specification

**Document type:** Product requirements, MVP baseline and accepted PC Steward 2.0 deltas
**Status:** Accepted 2.0 requirement deltas; architecture and implementation remain gated
**Version:** 0.4
**Date:** 29 October 2026
**Revision:** Task 44 promotes the owner-accepted Task 43 deltas and D1–D5 into requirements. Task 46 records the owner's later clarification that token and normal spending limits are editable: `FR-AI-04` adds input/output-token settings, and `FR-BUD-01` treats USD $200/month and $0.25/request as initial defaults. Other accepted scope, IDs and the separate $1 first-live-test cap are retained; no implementation is claimed.
**Working product name:** PC Steward  
**Primary owner:** Product owner / individual developer  

**Scope interpretation:** Existing MVP requirement IDs, wording and markers remain the baseline. Sections 9.13–9.18 and 11.5 specify the accepted 2.0 additions; “Yes — 2.0” means required in that scope, not implemented or accepted in a deployed artifact. The [Task 43 delta](PC_STEWARD_2_DELTA.md) records the accepted planning basis. [Task 44's amendment record](../review/TASK_44_ACCEPTED_REQUIREMENTS.md) identifies the exact amendments and remaining gates. No ADR is accepted or superseded by this specification revision, and no host action or paid call is authorized.

---

## 1. Executive summary

PC Steward is a local-first Windows desktop application that:

1. discovers software installed on a PC;
2. creates a clear, deduplicated inventory while preserving separate installation instances;
3. checks every **enabled trusted update source** for newer versions;
4. explains where each update came from and how confidently it matches the installed product;
5. lets the user review and apply supported updates safely;
6. records what changed, what failed, and what remains unmanaged; and
7. operates within bounded system-resource budgets while giving the user direct control over scan data, downloaded update files and complete local app state.

The product is intended to solve a common Windows maintenance problem: no single update mechanism has complete coverage, while existing tools often show only the packages they already know how to manage. PC Steward's defining promise is therefore:

> **Every discoverable installed application is accounted for, even when it cannot yet be updated automatically.**

The product must not claim to search or trust the entire public internet. “All available open sources” means all update connectors that the user has enabled and that meet the product's source-trust policy. Arbitrary search-engine results, download portals, mirrors and unknown executable links are out of scope.

---

## 2. Product vision

### 2.1 Vision statement

Give a Windows user one trustworthy place to answer four questions:

- What software is installed on this PC?
- Which applications are out of date?
- Which source proves that an update is available?
- Which updates can be applied safely, and what happened when they were attempted?

### 2.2 Product positioning

PC Steward is not a registry cleaner, “PC optimiser,” antivirus product, driver-download utility or enterprise endpoint-management platform. It is a transparent software inventory and update-orchestration tool for an individual Windows PC.

### 2.3 Core product principles

1. **Local first:** inventory and history remain on the PC unless the user explicitly exports them or approves the exact minimal, redacted evidence for the optional AI explanation in sections 9.17 and 16.2. No upload occurs by default.
2. **Evidence before automation:** an update must have a traceable source, product match and confidence level.
3. **Account for unknowns:** unmatched software is visible rather than silently omitted.
4. **Least privilege:** scanning and review run without administrator rights; elevation is requested only for a specific reviewed operation that needs it. The accepted 2.0 install/uninstall operations require their own approved contracts before implementation; existing update authority does not authorize them.
5. **No arbitrary web installers:** the application never chooses an executable merely because its name resembles an installed product.
6. **Explain every decision:** the user can see installed version, candidate version, source, confidence and planned action.
7. **Honest failure handling:** an update is successful only after post-installation verification.
8. **User control:** automatic installation is disabled in the MVP and no application is closed without clear consent.
9. **Resource respectful:** PC Steward has no permanently running service, uses bounded concurrency, remains cancellable and meets measurable CPU, memory, disk and scan-time budgets.
10. **Data is removable:** scan results, app-owned downloaded update files and the complete local application state can be deleted through separate, clearly labelled controls.

## 3. Problem statement

Windows software can be installed through multiple mechanisms, including traditional MSI or executable installers, Microsoft Store packages, package managers, per-user installers, self-updaters and portable applications. A package manager can update only the applications it can identify and map to a known package. This creates several user problems:

- installed software is spread across different inventory surfaces;
- the same product may appear more than once, for example as user and machine installations;
- version formats are inconsistent;
- some applications update themselves, some rely on the Store, and others require a fresh installer;
- package names and publisher names do not always match the installed display name;
- an apparent update may refer to a different product, channel, architecture or publisher;
- failures are frequently difficult to diagnose; and
- applications with no known source are often invisible in update tools.

PC Steward will create a canonical local inventory and reconcile it against trusted update sources without hiding uncertainty.

---

## 4. Goals and success outcomes

### 4.1 Product goals

| ID | Goal |
|---|---|
| G-01 | Produce a broad, understandable inventory of installed desktop applications and packaged apps. |
| G-02 | Identify available updates from every enabled trusted connector. |
| G-03 | Prevent false product-to-package matches from becoming executable update actions. |
| G-04 | Allow manual, user-approved installation of supported updates. |
| G-05 | Preserve a useful history of scans, update attempts, results and user decisions. |
| G-06 | Clearly identify software that is self-managed, unmatched, ignored or pinned. |
| G-07 | Work without an account and without uploading the software inventory. |
| G-08 | Provide a connector architecture that can gain coverage over time without weakening trust controls. |
| G-09 | Complete scans in a reasonable, measured time without materially degrading normal PC use. |
| G-10 | Give the user independent controls to clear scan data, clear PC Steward-owned downloaded update files and factory-reset the application. |

### 4.2 MVP user outcomes

The MVP is successful when a user can:

- install and launch PC Steward on a supported Windows 11 PC;
- complete a first scan without administrator elevation;
- see all applications found through the supported inventory mechanisms;
- distinguish current, outdated, self-managed, unmatched, pinned and ignored items;
- inspect source provenance and match confidence;
- update one or more WinGet- or Chocolatey Community-supported applications after explicit review;
- see whether each update succeeded, failed, requires a restart or could not be verified;
- retain history across restarts;
- export the inventory without creating an account;
- cancel a scan while the UI remains responsive;
- complete local and online scans within the defined performance targets and resource budgets;
- clear all current and historical scan-derived data without resetting unrelated app settings;
- delete PC Steward-owned downloaded update files from either Settings or the update-completion view; and
- factory-reset PC Steward so the next launch behaves like a fresh first launch while leaving the app itself installed.

### 4.3 Non-goals for the MVP

The MVP will not:

- search arbitrary websites or search engines for executable downloads;
- update BIOS, firmware, device drivers or Windows itself;
- act as an antivirus, vulnerability scanner or software-composition analysis service;
- clean the registry, delete temporary files or claim to “speed up” the PC;
- uninstall applications automatically;
- manage multiple PCs or provide a web administration portal;
- support macOS or Linux;
- discover every portable executable on every disk;
- alter game files, mods or Microsoft Flight Simulator content;
- silently close running applications;
- silently install updates;
- promise rollback for installers that do not provide one; or
- support enterprise policy enforcement, remote execution or central reporting.

**Accepted 2.0 clarification:** A manually confirmed installation or removal of one freshly resolved instance is specified separately in sections 9.14–9.15 and 11.5. It does not permit automatic/bulk uninstall, unattended installation, Windows Update execution or leftover cleanup. WS5 produces an automation ADR decision only; `FR-UPD-12` remains Later, `FR-UPD-01` still requires per-run confirmation, and `FR-SCH-03` remains scan-only. ADR-017/020 remain effective without an automation exception.

---

## 5. Intended users

### 5.1 Primary persona — Windows home power user

**Profile**

- Uses Windows 11 on a personally owned PC.
- Installs software from a mixture of vendor websites, Microsoft Store and package managers.
- Wants more coverage than a single package manager provides.
- Is comfortable reviewing technical details such as publisher, version, architecture and UAC prompts.
- Values transparency and control over one-click automation.

**Needs**

- A complete inventory rather than a partial list.
- A quick view of what needs attention.
- Safe batch updating for clearly matched applications.
- Visibility of unknown or unmanaged software.
- Useful error information when an installer fails.

### 5.2 Secondary persona — PC enthusiast or family technical helper

**Profile**

- Maintains one or several household PCs, but uses PC Steward on one machine at a time in the MVP.
- Wants repeatable maintenance and exportable inventory records.
- May test beta versions and deliberately pin selected applications.

### 5.3 Users not targeted by the MVP

- Enterprise endpoint administrators needing policy enforcement across a fleet.
- Users expecting fully unattended updates from unverified internet sources.
- Users requiring driver, BIOS or firmware management.
- Users on unsupported Windows versions.

---

## 6. Assumptions and constraints

### 6.1 Assumptions

- The PC is running a supported Windows 11 release.
- The initial build targets x64 hardware.
- WinGet is installed and available in the signed-in user's context.
- Chocolatey integration is optional; when enabled, the signed client is installed at its protected machine root.
- The user can approve UAC when an individual installer requires elevation.
- An internet connection is available for source refreshes and online update checks.
- Some applications will remain unmatched; this is an expected state, not a product failure.

### 6.2 Constraints

- The MVP must not depend on a continuously running Windows service.
- The MVP must not invoke `Win32_Product` for inventory discovery.
- The MVP must not perform an unrestricted full-disk executable scan.
- The UI and normal scan process must run as a standard user.
- Package metadata must be treated as untrusted input.
- The elevated component must not accept arbitrary command lines or arbitrary executable paths from the UI or database.
- The product must continue to provide local inventory and history when offline.
- Only one full scan may run at a time, and a full scan must not overlap update execution except for the controlled post-update verification scan.
- Inventory providers and online connectors must use bounded concurrency, explicit timeouts and cancellation.
- Scheduled scans must run without a persistent tray process or service, use a lower-priority execution mode where supported and exit after the scan finishes.
- PC Steward-owned logs, source metadata and downloaded update files must have bounded storage policies and user-accessible deletion controls.

## 7. Terminology and status model

### 7.1 Key terms

| Term | Definition |
|---|---|
| **Canonical product** | PC Steward's normalized identity for a software product, independent of a particular installation instance or source package. |
| **Install instance** | One detected installation of a product, including scope, architecture, path and version where known. A product can have multiple instances. |
| **Source connector** | A controlled integration that retrieves package or release metadata from a trusted update source. |
| **Package record** | A versioned software entry returned by a source connector. |
| **Mapping** | Evidence linking an install instance or canonical product to a source package. |
| **Update candidate** | A newer compatible package record proposed for an install instance. |
| **Confidence** | A deterministic assessment of how strongly the installed product and candidate package match. |
| **Self-managed** | An application expected to update through its own supported update mechanism. |
| **Unmanaged** | Software detected locally for which no approved update source or actionable mapping is available. |

### 7.2 User-facing statuses

| Status | Meaning | Action permitted in MVP |
|---|---|---|
| **Current** | Installed version is the same as, or newer than, the latest compatible trusted package. | None required |
| **Update available — verified** | A newer compatible package has a verified or high-confidence mapping. | Review and update |
| **Update available — review** | A likely update exists, but the mapping or channel requires manual confirmation. | Review only unless explicitly mapped |
| **Self-managed** | The product is expected to use its own updater or launcher. | Verify status; no forced update by default |
| **Unmanaged** | No approved update source is mapped. | Inspect and optionally create a manual mapping |
| **Unknown version** | Software is detected but its installed version cannot be reliably determined. | Inspect; no automatic update |
| **Pinned** | The user has chosen to stay on a specified version or branch. | No update until pin is removed or changed |
| **Ignored** | The user does not want notifications for this item. | Hidden from attention views but retained in inventory |
| **Update failed** | An attempted update returned a failure or post-update verification failed. | View diagnostics and retry |
| **Restart required** | The installer reported or PC Steward inferred that a reboot is required to complete verification. | Restart later or now |
| **Source unavailable** | The relevant connector could not complete its check. | Use cached result with a visible timestamp |

### 7.3 Match-confidence levels

| Level | Minimum evidence | Update behaviour in MVP |
|---|---|---|
| **Verified** | Exact package identity supplied by WinGet or an exact package-family/product identifier, with compatible architecture and scope. | Eligible for user-approved update |
| **High** | Strong deterministic match across product name, publisher, architecture, channel and a stable product identifier or validated user mapping. | Eligible after review |
| **Medium** | Name and publisher appear consistent but a stable identifier is absent or channel is unclear. | Display candidate; no installation until user confirms a mapping |
| **Low** | Name similarity or weak metadata only. | Informational only; never executable |
| **Unmatched** | No acceptable evidence. | No update action |

Confidence must be computed from rules. AI-generated similarity must never be the deciding control for installer execution.

---

## 8. Essential user journeys

### UJ-01 — First launch and consent

**Trigger:** User launches PC Steward for the first time.

**Journey**

1. The application explains what it will inspect locally and which network sources it will contact.
2. The user chooses whether to run an online source check immediately or perform a local-only inventory first.
3. The user confirms the default source set.
4. The application creates its local data store and begins the scan.

**Outcome**

- No account is required.
- No administrator prompt is shown merely to scan.
- The user can view and change source settings later.

### UJ-02 — Run a full software inventory and update check

**Trigger:** User selects **Scan now** or a scheduled scan starts.

**Journey**

1. PC Steward confirms that no conflicting scan or update job is active.
2. It collects local inventory from supported providers using bounded concurrency.
3. It normalizes and deduplicates product identities while retaining separate install instances.
4. Enabled connectors refresh their metadata or return a clear offline/source-error state.
5. The matcher creates candidate mappings and update states.
6. The UI displays progress, elapsed time and a **Cancel scan** action while remaining responsive.
7. The dashboard displays a summary, scan duration and the scan timestamp.

**Outcome**

The user can see what is current, outdated, unmatched, ignored, pinned, self-managed or uncertain. The scan either completes within the defined performance target or clearly identifies the connector/provider that timed out, failed or was cancelled.

### UJ-03 — Review an available update

**Trigger:** User opens an item marked **Update available**.

**Journey**

1. The detail view displays installed name, version, publisher, architecture and scope.
2. It displays candidate version, source, package identifier, channel and confidence.
3. It explains why the match is considered safe or why review is required.
4. It shows known installer behaviour, such as likely elevation, application closure or restart.
5. The user chooses **Update**, **Pin**, **Ignore**, **Remind later** or **Cancel**.

**Outcome**

No update is executed until the user explicitly chooses it.

### UJ-04 — Apply one supported update

**Trigger:** User chooses **Update** for a verified or approved mapping.

**Journey**

1. PC Steward creates an update job with immutable source and package identifiers.
2. It warns if the application appears to be running.
3. It asks the user to close the application or explicitly permit the updater to handle it.
4. It invokes the constrained update provider.
5. UAC appears only if the selected update requires it.
6. Progress, output and exit status are recorded.
7. PC Steward rescans the product after completion.
8. The UI reports success, failure, restart required or verification inconclusive.
9. If PC Steward-owned downloaded update files remain, the completion view shows **Delete downloaded files** with the amount of storage that will be recovered.

**Outcome**

The update history contains enough evidence to understand what occurred. The user can immediately remove app-owned downloaded files without deleting update history, scan results or the installed application.

### UJ-05 — Apply a reviewed batch of updates

**Trigger:** User selects several verified updates and chooses **Update selected**.

**Journey**

1. PC Steward displays the exact queue before execution.
2. The user can remove entries and choose whether to stop after a failure.
3. Jobs run sequentially in the MVP to reduce conflicts and simplify diagnosis.
4. Each item receives its own result and post-update verification.
5. The final summary separates successful, failed, skipped and restart-pending items.

**Outcome**

The user is never shown a single “success” state for a partially failed batch.

### UJ-06 — Resolve an unmatched application

**Trigger:** User opens an unmanaged application.

**Journey**

1. PC Steward shows all local metadata it has collected.
2. It lists candidate packages only where a connector produced plausible results.
3. The user may search a trusted connector by name or package identifier.
4. The user may confirm a mapping after comparing publisher, product, architecture and channel.
5. The mapping is stored with its origin and confirmation date.
6. A new update check is performed against that mapping.

**Outcome**

The application becomes managed without allowing arbitrary web URLs or shell commands.

### UJ-07 — Pin or ignore an application

**Trigger:** User does not want a proposed update.

**Journey**

1. The user chooses a reason: compatibility, beta/stable preference, hardware dependency, temporary deferral or other.
2. The user optionally chooses an expiry date.
3. The item leaves the normal attention queue but remains visible under pinned or ignored items.
4. The decision can be reversed at any time.

**Outcome**

Repeated scans respect the user's decision and preserve the rationale.

### UJ-08 — Investigate a failed update

**Trigger:** An update fails or cannot be verified.

**Journey**

1. The result page presents a plain-language summary.
2. It shows provider, package ID, attempted version, timestamps, exit code and sanitized logs.
3. It distinguishes download failure, source failure, elevation denial, installer failure, timeout and post-scan mismatch.
4. It offers safe actions: retry, rescan, open source/package information, mark as self-managed, or leave unresolved.

**Outcome**

The failure remains visible until resolved or dismissed; it is never silently converted to success.

### UJ-09 — Scheduled check and notification

**Trigger:** The configured Windows Task Scheduler task runs.

**Journey**

1. PC Steward performs a standard-user scan using the current user's context.
2. It does not install anything.
3. If attention is required, it raises a Windows notification showing the number of verified and review-required updates.
4. Opening the notification launches the dashboard.

**Outcome**

Scheduled checks provide awareness without unattended changes.

### UJ-10 — Export or clear selected local data

**Trigger:** User opens **Settings > Data & reset**, or an update has just completed.

**Journey**

1. The user may export the current inventory and optional history to CSV and JSON.
2. The page shows separate data categories and approximate sizes for scan data, source metadata, logs/history and PC Steward-owned downloaded update files.
3. The user may select **Clear scan data**. The confirmation explains that current inventory, raw provider records, update candidates and scan-run records will be removed while app settings, connector configuration, update-job history and app-owned downloaded files remain.
4. The user may select **Clear downloaded files**. PC Steward deletes only update payloads and temporary extraction files it owns and that are not in active use.
5. The same **Delete downloaded files** action is available on an update-completion screen when files remain.
6. Advanced controls may independently clear source metadata, logs, update history, manual mappings or policies.
7. Each operation reports success, recoverable space and any file it could not delete safely.

**Outcome**

The user can remove one category of local data without performing a full reset or affecting installed software.

### UJ-11 — Factory reset PC Steward

**Trigger:** User selects **Factory reset PC Steward** in the clearly separated reset area.

**Journey**

1. PC Steward checks for an active scan or update. The user must cancel or finish it before reset can proceed.
2. The confirmation lists everything that will be removed: the local database, current and historical scan data, source metadata, logs, update history, downloaded update files, mappings, pins, ignores, settings, consent records and the current-user scheduled task.
3. The user gives a second explicit confirmation.
4. PC Steward performs a path-safe local cleanup and reports any item it could not remove.
5. The application restarts or closes and, on the next launch, displays first-run onboarding and creates a new empty data store.

**Outcome**

PC Steward remains installed, but its local state is equivalent to a fresh installation. No previous settings, decisions, inventory or update history is presented.

## 9. Functional requirements

### 9.1 Onboarding and settings

| ID | Requirement | MVP |
|---|---|---|
| FR-ONB-01 | Explain local inventory collection, network source access, permissions and data storage before the first scan. | Yes |
| FR-ONB-02 | Permit a local-only first scan. | Yes |
| FR-ONB-03 | Show all enabled connectors and their trust category. | Yes |
| FR-ONB-04 | Allow the user to disable nonessential connectors. | Yes |
| FR-ONB-05 | Provide separate Stable and Beta application update channels for PC Steward itself. | Later |

### 9.2 Local inventory

| ID | Requirement | MVP |
|---|---|---|
| FR-INV-01 | Read machine-wide 64-bit uninstall registry entries. | Yes |
| FR-INV-02 | Read machine-wide 32-bit uninstall registry entries. | Yes |
| FR-INV-03 | Read current-user uninstall registry entries. | Yes |
| FR-INV-04 | Enumerate installed MSIX/AppX packages visible to the current user. | Yes |
| FR-INV-05 | Import installed-application information reported by WinGet. | Yes |
| FR-INV-06 | Preserve raw provider records for diagnosis while presenting normalized products in the UI. | Yes |
| FR-INV-07 | Deduplicate equivalent records without merging genuinely separate user/machine or architecture instances. | Yes |
| FR-INV-08 | Capture name, version, publisher, install scope, architecture, source provider, install date and path where safely available. | Yes |
| FR-INV-09 | Classify runtimes, redistributables, drivers, system components and applications separately where evidence allows. | Partial |
| FR-INV-10 | Discover applications in user-selected portable folders. | Later |
| FR-INV-11 | Inventory Chocolatey, Scoop, npm, pip, PowerShell and .NET global tools. | Later |
| FR-INV-12 | Detect self-updater services or scheduled tasks. | Later |

### 9.3 Identity resolution and matching

| ID | Requirement | MVP |
|---|---|---|
| FR-ID-01 | Maintain a canonical product identity separate from each install instance. | Yes |
| FR-ID-02 | Store exact package IDs returned by trusted connectors. | Yes |
| FR-ID-03 | Compute a deterministic confidence level and explanation for each mapping. | Yes |
| FR-ID-04 | Prevent low-confidence candidates from becoming executable update actions. | Yes |
| FR-ID-05 | Allow a user to create, review and delete a manual mapping to a trusted connector package. | Yes |
| FR-ID-06 | Preserve mapping origin: automatic, provider-supplied, user-confirmed or imported. | Yes |
| FR-ID-07 | Detect channel conflicts such as Stable versus Beta where source metadata permits. | Partial |
| FR-ID-08 | Compare existing and candidate publisher/signing identities for direct-download connectors. | Later |
| FR-ID-09 | Use AI only to explain or rank possible matches; never to authorize execution. | Later/optional |

### 9.4 Source connectors and update checks

| ID | Requirement | MVP |
|---|---|---|
| FR-SRC-01 | Support the WinGet default package source through a connector abstraction. | Yes |
| FR-SRC-02 | Support Microsoft Store package information available through WinGet. | Yes |
| FR-SRC-03 | Refresh source metadata on demand and expose the last successful refresh time. | Yes |
| FR-SRC-04 | Cache source results so previous data remains visible during a temporary outage. | Yes |
| FR-SRC-05 | Treat source metadata as untrusted and validate field lengths, formats and allowed values. | Yes |
| FR-SRC-06 | Permit a user-confirmed mapping to an official GitHub Releases repository for read-only version checks. | Desirable MVP |
| FR-SRC-07 | Add the fixed Chocolatey Community connector when its signed client is present; Scoop remains later. | Yes — Task 32 |
| FR-SRC-08 | Add signed, vendor-specific JSON, XML, RSS or API connectors. | Later |
| FR-SRC-09 | Search arbitrary websites or download portals. | Never |
| FR-SRC-10 | Let the user add only connector types supported by the product; no raw command connector. | Yes |

### 9.5 Version resolution

| ID | Requirement | MVP |
|---|---|---|
| FR-VER-01 | Compare numeric dotted versions correctly when both sides provide them. | Yes |
| FR-VER-02 | Handle prerelease labels and avoid offering Beta/Preview builds to Stable users by default. | Yes |
| FR-VER-03 | Defer to connector-specific version semantics where generic comparison is unsafe. | Yes |
| FR-VER-04 | Mark a result as unknown rather than inventing an ordering for incomparable versions. | Yes |
| FR-VER-05 | Prevent a downgrade unless the user enters a dedicated future downgrade workflow. | Yes |

### 9.6 Dashboard and application details

| ID | Requirement | MVP |
|---|---|---|
| FR-UI-01 | Show counts for verified updates, review-required updates, current, unmanaged, pinned, ignored and failed items. | Yes |
| FR-UI-02 | Support search, sorting and filtering by status, publisher, source and scope. | Yes |
| FR-UI-03 | Show an application detail page with installed and candidate metadata side by side. | Yes |
| FR-UI-04 | Display source provenance, package ID and confidence explanation before an update action. | Yes |
| FR-UI-05 | Show last scan time and whether the displayed result is live or cached. | Yes |
| FR-UI-06 | Provide a compact “attention needed” view. | Yes |
| FR-UI-07 | Provide a detailed diagnostics view without exposing unnecessary implementation noise by default. | Yes |

### 9.7 Update execution

| ID | Requirement | MVP |
|---|---|---|
| FR-UPD-01 | Require explicit selection and confirmation before every update or batch. | Yes |
| FR-UPD-02 | Execute only provider-defined, structured operations against an immutable package ID. | Yes |
| FR-UPD-03 | Never execute a command string retrieved from the local database or remote metadata. | Yes |
| FR-UPD-04 | Request elevation only when the provider determines it is needed. | Yes |
| FR-UPD-05 | Warn when a target application appears to be running. | Yes |
| FR-UPD-06 | Do not force-close an application without explicit consent. | Yes |
| FR-UPD-07 | Capture start time, end time, provider, package ID, target version, exit code and sanitized output. | Yes |
| FR-UPD-08 | Re-inventory the target after the installer exits. | Yes |
| FR-UPD-09 | Report success only when the expected version is detected or the provider supplies a reliable restart-pending result. | Yes |
| FR-UPD-10 | Run MVP batch updates sequentially. | Yes |
| FR-UPD-11 | Allow cancellation before the next queued update begins. | Yes |
| FR-UPD-12 | Support unattended automatic update rules. | Later |
| FR-UPD-13 | Download and execute installers directly from official vendor or GitHub sources. | Later, after signature/hash controls |

### 9.8 Pinning, ignoring and manual decisions

| ID | Requirement | MVP |
|---|---|---|
| FR-POL-01 | Pin a product to its current version, a major/minor branch or a source channel where supported. | Partial MVP: current version/source package |
| FR-POL-02 | Ignore an application indefinitely or until a selected date. | Yes |
| FR-POL-03 | Record an optional reason for pinning, ignoring or manually mapping. | Yes |
| FR-POL-04 | Show all active decisions in one settings view. | Yes |
| FR-POL-05 | Make all decisions reversible. | Yes |

### 9.9 History, logs and diagnostics

| ID | Requirement | MVP |
|---|---|---|
| FR-HIS-01 | Store scan summaries and update-job history locally. | Yes |
| FR-HIS-02 | Show a timeline for each product. | Yes |
| FR-HIS-03 | Distinguish source, download, installer, elevation, timeout and verification failures. | Yes |
| FR-HIS-04 | Apply configurable retention limits to verbose logs and source caches. | Yes |
| FR-HIS-05 | Allow a user to copy a sanitized diagnostic bundle. | Yes |
| FR-HIS-06 | Never include credentials, tokens or full environment dumps in exported diagnostics. | Yes |

### 9.10 Scheduling and notifications

| ID | Requirement | MVP |
|---|---|---|
| FR-SCH-01 | Allow an optional daily, weekly or manual-only scan schedule. | Yes |
| FR-SCH-02 | Use Windows Task Scheduler rather than an always-running service. | Yes |
| FR-SCH-03 | Scheduled scans may check but must not install updates in the MVP. | Yes |
| FR-SCH-04 | Show a Windows notification only when attention is required or a scan fails repeatedly. | Yes |
| FR-SCH-05 | Allow the schedule and notifications to be disabled independently. | Yes |

### 9.11 Export, data clearing and reset controls

| ID | Requirement | MVP |
|---|---|---|
| FR-DAT-01 | Export current inventory to CSV and JSON. | Yes |
| FR-DAT-02 | Optionally include update history in JSON export. | Yes |
| FR-DAT-03 | Warn that exports reveal installed software and potentially identifying machine metadata. | Yes |
| FR-DAT-04 | Provide advanced controls to clear source metadata, logs, update history or manual mappings/policies independently. | Yes |
| FR-DAT-05 | Provide an opt-in cloud synchronization option. | Later, separate product decision |
| FR-DAT-06 | Provide a dedicated **Clear scan data** button that removes current inventory, raw provider records, update candidates and scan-run records. | Yes |
| FR-DAT-07 | **Clear scan data** must preserve global settings, enabled connectors, schedule, update-job history and app-owned downloaded update files. User-created mappings, pins and ignores must be retained in a stable decision store and reapplied when the same product is rediscovered. | Yes |
| FR-DAT-08 | Provide a dedicated **Clear downloaded files** button in Settings that deletes only PC Steward-owned update payloads and temporary extraction files not in active use. | Yes |
| FR-DAT-09 | Show **Delete downloaded files** on the update-completion view whenever PC Steward-owned files remain, together with the space recoverable. | Yes |
| FR-DAT-10 | Display the current size, last-cleaned time and deletion scope for scan data and downloaded update files. | Yes |
| FR-DAT-11 | Do not delete Windows, WinGet, Microsoft Store or another provider's cache unless that provider exposes a documented, safe cleanup operation; disclose this boundary in the UI. | Yes |
| FR-DAT-12 | Provide a dedicated **Factory reset PC Steward** button that removes all local application data, settings, decisions, caches, logs, consent records and the current-user scheduled task, then returns the next launch to first-run onboarding. The installed app binaries remain. | Yes |
| FR-DAT-13 | Require an explicit confirmation for each destructive action and a second confirmation for factory reset. | Yes |
| FR-DAT-14 | Disable destructive controls while a dependent scan or update is active, or require the user to cancel the operation first. | Yes |
| FR-DAT-15 | Deletion operations must be idempotent, report locked or undeletable items accurately and never claim success while relevant app-owned data remains. | Yes |

### 9.12 Scan and resource management

| ID | Requirement | MVP |
|---|---|---|
| FR-RSC-01 | Display scan phase, elapsed time and a **Cancel scan** action for manual scans. | Yes |
| FR-RSC-02 | Permit only one active full scan and prevent it from overlapping update execution, except for a controlled post-update verification scan. | Yes |
| FR-RSC-03 | Apply bounded connector concurrency, per-provider timeouts and cancellation tokens so one source cannot consume unbounded resources or block the whole scan. | Yes |
| FR-RSC-04 | Run scheduled scans through Task Scheduler without an always-running process, at lower priority where supported, and exit after completion, cancellation or timeout. | Yes |
| FR-RSC-05 | Record scan duration and provider timings locally for performance diagnosis without collecting unrelated application-usage telemetry. | Yes |

### 9.13 Updater gap closure — accepted 2.0 additions

Complete only the confirmed WS2 gaps. G3 (reliable provider restart evidence) and G4 (reviewed multi-select/Update All with cancellation and per-item partial outcomes) implement existing `FR-UPD-09–11`, `FR-UI-01–07` and `AC-UPD-01–11`; they receive no duplicate IDs. A batch reviews a frozen set, freshly revalidates every item, explains excluded/stale rows and preserves earlier results. A target detected at review or appearing before provider start requires the existing warning/acknowledgement. Q2/Q3 remain open: no new skip-version policy, broader major/minor pin support, universal process detector or forced-closure feature is implied.

| ID | Accepted requirement and acceptance criteria | 2.0 scope |
|---|---|---|
| FR-UPD-14 | Review, queued/preparing, provider-observable execution phases, verification and final result are distinguishable; unavailable provider detail is explicitly indeterminate. A running-app warning offers cancel/close-manually/explicit continuation without a target kill. Mixed outcomes and cancellation retain per-item history; UI tests cover absence of fake download percentages or completion. | Yes — 2.0; gated |
| FR-UPD-15 | Where a provider can enforce it, review offers a typed silent/interactive mode whose value is immutable and revalidated by both runner and helper. Unsupported combinations are disabled/explained. Mode cannot inject arbitrary switches or bypass exact ID/version/scope/signature controls. Cancellation/timeout and unexpected interaction have honest outcomes. | Yes — 2.0; gated |

### 9.14 Catalogue installation — accepted 2.0 additions

Catalogue installation is a distinct manually reviewed operation through approved WinGet catalogues, not an upgrade of a fictitious installed product. Store information remains available under `FR-SRC-02`; Store installation and Chocolatey catalogue installation are outside this scope. Search metadata never supplies executable authority. D1 selects tags only, with no curated category map.

| ID | Accepted requirement and acceptance criteria | 2.0 scope |
|---|---|---|
| FR-CAT-01 | Search only enabled, explicitly consented approved WinGet catalogues through COM; bound result count, field lengths, timeout and cancellation. Results show name, publisher, exact ID, version, source and known scope/architecture; absent fields are unavailable. Store information cannot expose Install. | Yes — 2.0; gated |
| FR-CAT-02 | Icons/categories/sizes are unavailable when unsupported; tags are explicitly labelled tags and do not imply authoritative categories. No curated category map is implemented (D1). | Yes — 2.0; gated |
| FR-CAT-03 | Fresh inventory establishes not-installed status and resolves existing-instance ambiguity before approval. Review binds an explicit install operation, exact source/ID/version/scope/architecture; unknown or unenforceable scope and changed evidence block. Existing installations are not silently repaired/upgraded. | Yes — 2.0; gated |
| FR-CAT-04 | User installs execute unelevated. Machine installs require fresh scope-bound confirmation and the protected one-shot helper independently validates the install operation and fresh identity evidence; update/install substitution and replay fail closed. No arbitrary executable, URL or flags enter the job. | Yes — 2.0; gated |
| FR-CAT-05 | Install queue is sequential; each selected item has independent reviewed evidence and verified post-inventory installed state. Provider exit alone is not success. Cancellation prevents the next item; failures, ambiguous verification and partial outcomes remain visible without an atomic rollback promise. | Yes — 2.0; gated |
| FR-CAT-06 | Favourites are local and reversible; recently installed entries derive from verified PC Steward install outcomes rather than catalogue clicks or pre-existing apps. Retention/reset/clear ownership is documented and tested before persistence is added. | Yes — 2.0; gated |

### 9.15 Manual uninstall — accepted 2.0 additions

Removal requires a separate operation contract and one exact installed instance. A route without enforceable instance/user/scope targeting or complete exclusion evidence stays blocked or deferred (Q4). Existing update jobs, catalogue IDs, registry uninstall strings and inventory visibility are not removal authority.

| ID | Accepted requirement and acceptance criteria | 2.0 scope |
|---|---|---|
| FR-UNI-01 | One review binds one freshly resolved provider identity, installed instance, scope, architecture and applicable user; duplicate/unknown/changed instances block even if catalogue ID is exact. | Yes — 2.0; gated |
| FR-UNI-02 | Only a provider-enforceable instance-bound WinGet operation or current-user MSIX registration removal is eligible. All-user/provisioned MSIX, frameworks, dependencies and protected/system packages are excluded. Arbitrary registry uninstall strings and leftover cleanup are rejected. Unknown safety/instance proof means no action. | Yes — 2.0; gated |
| FR-UNI-03 | Target-in-use warning and explicit confirmation precede removal; no forced closure. Any required elevation uses a separately approved uninstall discriminator and independently validated one-shot helper; it cannot be requested by a scan or reused for other operations. | Yes — 2.0; gated |
| FR-UNI-04 | Re-inventory checks the selected instance's absence while preserving other instances. Report removed, still present, reliably restart pending or unknown; cancellation/interruption is never claimed as removal without verification. No universal rollback or deletion of user documents is promised. | Yes — 2.0; gated |

### 9.16 Diagnostic timeline — accepted 2.0 additions

Collection is read-only, consented and unelevated. Scope is bounded System/Application Event Log evidence (for example Kernel-Power 41, EventLog 6008, BugCheck 1001 and WER application faults), Reliability records, Windows Update history and PC Steward operation history. Host access and coverage are unproven until separately approved checks; missing/denied sources remain visible. D4 fixes content retention, while collection limits/fields and correlation rules remain Q1/Q6 gates.

| ID | Accepted requirement and acceptance criteria | 2.0 scope |
|---|---|---|
| FR-DIA-01 | After explicit collection consent, bounded unelevated readers collect the approved System/Application events, Reliability records and Windows Update history plus PC Steward operation history. Each unavailable, unsupported or denied source is identified; no scan requests elevation. | Yes — 2.0; gated |
| FR-DIA-02 | A repeatable deterministic timeline labels nearby changes as “occurred near”, never inferred causes. Kernel-Power 41 is labelled evidence of unexpected shutdown only. Tests include unrelated nearby changes, missing records and conflicting timestamps. | Yes — 2.0; gated |
| FR-DIA-03 | Diagnostic snapshots default to 30 days' local retention (D4). Before implementation, owner-approved collection policy also defines lookback, maximum count/bytes and minimum stored fields. Collection stops/truncates visibly at bounds, stores only that schema, redacts before persistence and handles hostile event text as data. | Yes — 2.0; gated |
| FR-DIA-04 | Clear scan data deletes collected diagnostic snapshots while preserving update-job history. Factory reset deletes all app-owned diagnostic state. Tests prove idempotence, locked/residual reporting and preservation of Windows Event Log/Reliability/Update records and exported files. | Yes — 2.0; gated |
| FR-DIA-05 | A requested diagnostic export previews the bounded minimum fields with redaction and sensitivity warning; export/cancel behavior and deletion/preservation scope match the owner-approved policy. No raw dump/full-log export is introduced through this feature. | Yes — 2.0; gated |

### 9.17 Optional AI explanation — accepted 2.0 additions

D2 selects OpenAI first for failed-update and diagnostic-timeline explanation. This feature is separate from `FR-ID-09`, which retains its Later/optional match-explanation meaning. No chat, AI mapping, release-note summary or second-provider implementation is promoted. Core inventory, updates, history and manual workflows remain usable with AI disabled or without a key.

| ID | Accepted requirement and acceptance criteria | 2.0 scope |
|---|---|---|
| FR-AI-01 | OpenAI is the first provider (D2), explaining a failed update or the diagnostic timeline after provider-specific opt-in. A provider abstraction and official SDK support configurable model IDs and current implementation-time pricing; no second provider, chat or release-note summary feature is shipped. | Yes — 2.0; gated |
| FR-AI-02 | Each call displays an editable preview of exactly the evidence that will leave the machine and requires approval of that payload. Usernames, profile paths, serials, device IDs, emails and tokens are redacted; no dumps or full logs are sent. UI discloses residual secret risk. Tests capture mock transport payload equality after edits/redaction. | Yes — 2.0; gated |
| FR-AI-03 | Current-user DPAPI or Credential Manager protects credentials; no plaintext config, export, backup or logging path includes them. Disconnect and Factory reset remove them and accurately report residual failure. Keys are never required for core use. | Yes — 2.0; gated |
| FR-AI-04 | Only approved provider TLS endpoints receive calls; credentials do not follow redirects to another origin. Input/output-token limits are user-editable upward/downward with compatible request/response/saved-item byte allowances validated together within verified finite model/resource bounds and effective spending caps. Show effective binding limits; do not accept cosmetic token increases defeated by unrelated smaller byte caps. Token edits do not automatically change money; changed unsent limits require fresh review/reservation. Retries, concurrency and timeouts retain reviewed bounds; cancellation/crash/transport cutoff retain uncertain spend unless complete reliable terminal usage permits settlement. | Yes — 2.0; gated |
| FR-AI-05 | Logs, events and release notes are untrusted prompt data. Mock hostile inputs cannot grant tools, run commands or alter deterministic eligibility; output is advisory, clearly uncertain where evidence is insufficient, and has no system-changing tool capability. | Yes — 2.0; gated |
| FR-AI-06 | Missing key, refusal, timeout, provider outage or exhausted budget leaves inventory/update/history/manual operations usable. Tests use mocks; a live call requires separate exact owner authority, with a $1 total cap for the first live test (D5). Saved redacted explanations have 30-day local retention; submitted prompts are not saved (D4). Minimal spending/usage records and unresolved reservations are retained separately as needed for accounting, without prompt/response content. No conversational history feature is inferred. | Yes — 2.0; gated |

### 9.18 AI spending controls — accepted 2.0 additions

D5 initially selected an app-only USD $200 monthly ceiling and $0.25 per-request cap. The owner's Task 46 clarification makes these initial defaults: normal monthly/per-request spending limits and input/output-token limits are user-editable upward or downward. Explicit spending changes apply prospectively, preserve recorded spending and uncertain commitments, and never reset monthly/test accounting or purchase credits. The first separately approved live test retains its independent $1 total cap. A provider credit benefit or reported balance never supplies additional app authority. Applicable accounting/transport ADRs and implementation evidence remain prerequisites.

| ID | Accepted requirement and acceptance criteria | 2.0 scope |
|---|---|---|
| FR-BUD-01 | PC Steward starts with USD $200/month and $0.25/request defaults; users can explicitly raise/lower normal spending limits without additional product-level dollar maxima. Validate finite non-negative exactly representable USD amounts; if both are positive require per-request <= monthly, while either zero disables paid calls and preserves the other setting. Explicit save shows old/new values and commitments. Edits apply prospectively with fresh unsent review/reservation, preserving settled/uncertain spending and period/test accounting; resetting defaults never refunds spend. Unknown pricing, invalid settings or insufficient allowance blocks a call. The app-only ledger does not cap other account usage; provider billing is authoritative. Credits never raise caps automatically. The separate first-live-test $1 cap remains. | Yes — 2.0; gated |
| FR-BUD-02 | Persist a conservative worst-case reservation transactionally before a request, within both caps, including bounded retries/output. Concurrent requests and crash/restart cannot double-spend the available balance; fixtures cover the commit/send and send/result crash windows. | Yes — 2.0; gated |
| FR-BUD-03 | Reconcile against provider-reported usage; preserve any reservation with uncertain billing until reconciled. An unavailable usage report/timeout never automatically restores its spend allowance. Monthly rollover, currency/pricing version and duplicate usage reports are covered under the accepted budget ADR. No promotional-credit amount is assumed. | Yes — 2.0; gated |
| FR-BUD-04 | No path purchases credits, enables automatic recharge or treats presumed promotional credits as extra budget. Mock exhaustion stops calls and offers only owner-controlled settings/account guidance. | Never |

The stated acceptance criteria require fixtures and applicable live evidence in their future implementation tasks. Task 44 records requirements only. Numeric bounds not fixed by D4/D5, favourite/operation-history lifecycle, credential adapter selection and uninstall feasibility remain explicit design gates in section 11.5; no arbitrary defaults are supplied here.

## 10. Trusted-source policy

### 10.1 Product rule

PC Steward checks **all enabled trusted connectors**, not the unrestricted internet.

### 10.2 Source tiers

| Tier | Source type | MVP treatment |
|---|---|---|
| **A — OS/vendor managed** | Microsoft Store or an application's supported self-updater | Detect and report; invoke only through supported provider mechanisms |
| **B — Curated package manager** | WinGet configured trusted sources | Primary actionable update source in MVP |
| **C — Verified official project** | User-confirmed official GitHub Releases repository or official vendor feed | Read-only version check in early MVP; direct install later |
| **D — Community package manager** | Fixed Chocolatey Community feed | Opt-in machine-scope connector only; client signer, feed URL, package ID, version, source provenance and package-script risk must be visible before review. Scoop remains later. |
| **E — Arbitrary web source** | Search results, download sites, mirrors, raw executable URL | Prohibited |

### 10.3 Required source metadata

A connector should supply, where available:

- connector and source name;
- stable package identifier;
- product name and publisher;
- release version and channel;
- supported architecture and scope;
- installer type;
- package/release URL for display;
- installer URL only inside the provider's constrained execution path;
- expected hash or package signature information;
- release timestamp;
- release notes URL;
- known restart or interactive-installer behaviour; and
- source refresh timestamp.

Missing metadata reduces confidence. It must never be silently fabricated.

### 10.4 Source disagreement

When two trusted sources report different latest versions:

1. PC Steward displays both results and their timestamps.
2. An official vendor feed, when identity is verified, may be labelled authoritative for version status.
3. Installation remains tied to the selected provider.
4. Automatic selection is not made where channel, architecture or publisher differs.
5. The user may choose a preferred source for that product.

---

## 11. MVP scope versus later features

### 11.1 MVP — private beta / version 0.1

**Platform and user**

- One Windows 11 x64 PC.
- One Windows user profile per local database.
- No account and no cloud service.

**Inventory**

- HKLM 64-bit uninstall entries.
- HKLM 32-bit uninstall entries.
- HKCU uninstall entries.
- Installed MSIX/AppX packages visible to the user.
- WinGet installed-application output.
- Normalization, deduplication and separate install instances.

**Update sources**

- WinGet default trusted sources, including Microsoft Store information exposed through WinGet.
- Optional Chocolatey Community source, only through its signed client at the canonical ProgramData root and only after explicit source disclosure. It uses the fixed `https://community.chocolatey.org/api/v2/` feed; configured custom sources, source credentials, raw scripts, installer paths and package-manager configuration are never imported as PC Steward authority.
- Chocolatey's local package list does not establish the original feed for an installed package. PC Steward therefore presents every Chocolatey candidate as review-only until the user confirms its exact Community package ID; only that explicit mapping may proceed to the normal immutable-job and elevation review.
- Optional user-confirmed GitHub Releases mapping for read-only version awareness, provided it does not delay the core build.

**Actions**

- Manual scan with progress and cancellation.
- Optional scheduled check through Task Scheduler.
- Review one or more verified updates.
- User-approved sequential updates through WinGet.
- Pin, ignore, retry and manually map to a trusted package.
- Post-update verification.
- Inventory and history export.
- Dedicated **Clear scan data** control.
- Dedicated **Clear downloaded files** control in Settings and after an update when applicable.
- Dedicated **Factory reset PC Steward** control that returns the app to first-run state without uninstalling it.

**Performance and resource use**

- Measured local and online scan-time targets.
- Responsive UI and cancellable scans.
- Bounded CPU, memory, concurrency, logs and app-owned cache use.
- No persistent background service or tray process after the UI closes.

**Safety**

- No direct arbitrary installer download.
- No unattended installation.
- No driver, firmware or OS update management.
- No continuously elevated process.
- Destructive cleanup may operate only on allowlisted PC Steward-owned paths and data stores.

### 11.2 Version 0.2–0.4 candidates

- Official GitHub Releases connector with deterministic tag parsing.
- Scoop inventory/connectors when installed.
- Portable application folders selected by the user.
- Better application classification and self-updater detection.
- Official vendor feed adapters.
- Release-note summaries.
- More granular pins by channel or major version.
- Improved restart detection.
- Import/export of mappings and policies.
- Arm64 build.

### 11.3 Version 1.0 candidates

- Signed direct-installer workflow for verified official vendor sources.
- Authenticode publisher continuity checks.
- SHA-256 verification when the source supplies a trusted hash.
- High-confidence automatic update rules, off by default.
- Maintenance windows and restart coordination.
- Multi-PC local network mode or optional encrypted cloud synchronization.
- End-of-support data and application lifecycle warnings.
- Startup/background-process review as a separate module.

### 11.4 Features requiring a separate risk decision

- Driver and firmware update assistance.
- Vulnerability intelligence and CVE mapping.
- Remote management.
- Central accounts and cross-device synchronization.
- Community-authored raw source connectors.
- AI-assisted automatic package mapping.

These features must not be added merely because they are technically possible; each requires an updated threat model and explicit product approval.

### 11.5 Accepted PC Steward 2.0 scope and gates — Task 44

The owner accepted the Task 43 delta and D1–D5 on 29 October 2026. This revision makes those requirement deltas authoritative without claiming implementation, accepted future ADRs, current host access or deployed behavior. The preceding version milestones describe the baseline and candidate backlog; only the exact additions below are promoted. “Improved restart detection” remains a later candidate beyond WS2's confirmed reliable-provider-evidence propagation gap.

| Workstream | Accepted scope | Required gate before implementation or live evidence |
|---|---|---|
| WS1 | Existing owner-only protected redeployment; no new product requirement. | Separately approved exact artifact, database backup/integrity, validator/hash preservation and approved responsive launch under ADR-022. Independent of A2; no distribution claim or closure of `FRA-14–16`. |
| WS2 | Confirmed G1–G4: honest progress, enforceable immutable installer mode, reliable restart evidence propagation, reviewed Update All/multi-select and partial/cancel outcomes. | Applicable ADR-017/018 changes, bounded phase/task contract, focused fixtures, build/full suite and phase review. Preserve existing policy/status/history controls; Q2/Q3 are not new features. |
| WS3 | Enabled/consented WinGet COM search, tags-only metadata, distinct reviewed install, local favourites and verified recent installs (`FR-CAT-01–06`). | Task 45 proposed install/job/helper ADRs and necessary ADR-003/015/017/018/022 amendments accepted by the owner, Q1 search bounds, Q5 lifecycle, implementation/security gate and separately approved live install. |
| WS4 | One manually confirmed, freshly resolved instance through a supported WinGet or current-user MSIX route (`FR-UNI-01–04`). | Task 45 proposed uninstall/job/helper decisions accepted by the owner, Q4 enforceable targeting/exclusion evidence and viable host test, Q5 history lifecycle, implementation/security gate and separate live removal approval. Unsupported routes stay blocked/deferred. |
| WS5 | Draft and decide a user-scope WinGet allowlist automation ADR: accept, reject or defer. | Task 45 decides durable consent/integrity, exact source/ID/optional version ceiling, freshness, revocation, windows, bounded retries/back-off, notifications, installer limits and possible ScheduledHost changes. Decision only; implementation and any specification/ADR exception need later separate approval. ScheduledHost stays scan-only. |
| WS6 | Bounded read-only timeline, deterministic nearby-change labels, consent/redaction/export and app-owned lifecycle (`FR-DIA-01–05`). D4: 30-day local snapshot retention. | Task 46 diagnostic ADR and relevant lifecycle/redaction/persistence amendments accepted by the owner; Q1 source/schema/lookback/count/bytes/export limits, Q6 time/correlation rules and separately approved host access checks. No scan elevation or deletion of OS records. |
| WS7 | OpenAI-first advisory failure/timeline explanation, exact edited-payload approval, current-user protected credentials and transactional app-only spending (`FR-AI-01–06`, `FR-BUD-01–04`). D4: 30-day saved redacted explanations, no saved submitted prompts. Task 46 owner clarification: token and normal spending limits are editable; USD $200/month and $0.25/request are initial defaults. First live test remains $1 total. | Task 46 AI/privacy/credential and budget ADRs accepted by the owner, Q1 transport bounds, Q7 accounting semantics, Q8 DPAPI/Credential Manager selection and exact deletion design. Mocked security/privacy/cost gate precedes separate provider/model/payload/cost approval for any live call. |

Task 47 writes bounded phase briefs and numbered prompts only after applicable ADR acceptance. The [accepted roadmap](PC_STEWARD_2_ROADMAP.md) §4.2 deferred list remains Later in full; §4.3 prohibitions remain Never. No category map, Store installation, additional actionable provider, unattended/signed-out/machine-scope automation, generic/browser/Recycle Bin/leftover cleanup, startup control, health score, dump analysis, AI commands or Windows reset is promoted. Existing Chocolatey update scope remains unchanged; it is not catalogue install authority.

D3 selects `7zip.7zip` solely as the later controlled test candidate. Fresh absence/instance/version/architecture/enforceable scope and installer-effect review remain mandatory. A relied-on installation is not disposable, package selection is not install/uninstall approval, and every live action needs separate exact authority. Use unit/fixture/fake-provider/failure-injection evidence and isolated app-owned fixture roots; the owner excludes VM/Sandbox/Hyper-V testing for this roadmap. A destructive route without a viable host test stays deferred. Windows and performance claims require actual Windows evidence and ADR-006 measurements.

Q5 must settle favourite/recent-install and install/uninstall-history retention, clear/reset ownership and scan-reference integrity before new persistence exists. This revision does not choose their schema or silently inherit update-history retention. Q9 remains exact-artifact redeployment authority. Q10's requirement wording is recorded here; future operation/security ADR acceptance and the WS5 decision remain separate. The historical decisions and full question wording remain in the [Task 43 delta](PC_STEWARD_2_DELTA.md) §7.

---

## 12. Data to be stored

### 12.1 Storage approach

- Primary store: local SQLite database.
- Location: per-user application data directory under `%LocalAppData%`.
- Logs: separate per-user log directory with size and age limits.
- Source cache: local, replaceable and clearly timestamped.
- Download cache: a separate, allowlisted PC Steward-owned directory for update payloads or temporary extraction files created by PC Steward. It may be empty when an external provider owns the download process.
- All app-owned cache paths must be derived internally; source metadata, package names and user input must never determine an arbitrary deletion path.
- No cloud database in the MVP.
- The current Windows account's file permissions protect the data at rest; PC Steward does not claim protection from an already-compromised local administrator.

### 12.2 Conceptual data model

#### DeviceProfile

| Field | Purpose |
|---|---|
| DeviceId | Locally generated identifier |
| DeviceName | Local display only; omitted or redacted from diagnostic export by default |
| OsEdition / OsBuild | Compatibility and troubleshooting |
| Architecture | x64 initially |
| AppVersion | PC Steward version used for the scan |
| CreatedAt / UpdatedAt | Record lifecycle |

#### CanonicalProduct

| Field | Purpose |
|---|---|
| ProductId | Internal stable ID |
| NormalizedName | Display/search identity |
| NormalizedPublisher | Publisher identity where known |
| ProductCategory | Application, runtime, system component, driver, game/launcher or unknown |
| StableIdentityKey | Deterministic product fingerprint used to reattach user decisions after scan data is cleared; must not depend solely on a volatile row ID |
| PreferredSourceId | User-selected preferred source, optional |
| CreatedAt / UpdatedAt | Record lifecycle |

#### InstallInstance

| Field | Purpose |
|---|---|
| InstallInstanceId | Internal stable ID |
| ProductId | Link to canonical product |
| InventoryProvider | Registry, MSIX/AppX, WinGet or future provider |
| ProviderNativeId | Product code, package family name or provider ID |
| DisplayName | Raw installed display name |
| InstalledVersionRaw / Normalized | Version evidence |
| PublisherRaw | Installed publisher evidence |
| Scope | User or machine |
| Architecture | x86, x64, Arm64, neutral or unknown |
| InstallLocation | Optional; local-only unless explicitly exported |
| InstallDate | Optional |
| FirstSeenAt / LastSeenAt | Lifecycle |
| RawMetadataJson | Bounded diagnostic metadata; no secrets |

#### SourceDefinition

| Field | Purpose |
|---|---|
| SourceId | Stable connector/source ID |
| ConnectorType | WinGet, Store, GitHub Releases or future connector |
| DisplayName | User-facing name |
| TrustTier | A–D |
| Enabled | Connector state |
| Configuration | Validated connector-specific configuration |
| LastRefreshAt / LastSuccessAt | Freshness |
| LastError | Sanitized error summary |

#### SourcePackage

| Field | Purpose |
|---|---|
| SourcePackageId | Internal ID |
| SourceId | Parent source |
| NativePackageId | Stable source package ID |
| ProductName / Publisher | Source metadata |
| VersionRaw / Normalized | Candidate version |
| Channel | Stable, Beta, Preview, ESR or unknown |
| Architecture / Scope | Compatibility |
| InstallerType | Metadata only |
| ReleaseDate | Optional |
| MetadataJson | Validated and bounded source metadata |
| RetrievedAt | Cache timestamp |

#### ProductMapping

| Field | Purpose |
|---|---|
| MappingId | Internal ID |
| InstallInstanceId or ProductId | Current mapping target when scan data exists |
| StableTargetKey | Persisted product/instance fingerprint used to reattach a user-confirmed mapping after scan data is cleared |
| SourceId / NativePackageId | Package target |
| Confidence | Verified, High, Medium, Low |
| EvidenceJson | Deterministic reasons |
| Origin | Provider, automatic rule, user-confirmed or imported |
| ConfirmedAt | Auditability |
| Active | Reversible state |

#### UpdateCandidate

| Field | Purpose |
|---|---|
| CandidateId | Internal ID |
| InstallInstanceId | Target installation |
| MappingId | Supporting mapping |
| InstalledVersion / CandidateVersion | Comparison |
| Status | Current, available, review, unknown or source unavailable |
| CompatibilityResult | Architecture/channel/scope checks |
| Explanation | User-facing deterministic explanation |
| CreatedAt / ExpiresAt | Freshness |

#### ScanRun

| Field | Purpose |
|---|---|
| ScanRunId | Internal ID |
| Trigger | Manual, scheduled or post-update |
| Mode | Local-only or online |
| StartedAt / CompletedAt | Timing |
| ProviderResults | Counts and sanitized errors |
| SummaryCounts | Dashboard snapshot |
| AppVersion | Reproducibility |

#### UpdateJob and UpdateJobEvent

| Field | Purpose |
|---|---|
| UpdateJobId | Stable job ID |
| InstallInstanceId | Target |
| SourceId / NativePackageId | Immutable provider target |
| RequestedVersion | Expected version |
| Status | Queued, running, successful, failed, cancelled, restart required, inconclusive |
| RequestedAt / StartedAt / EndedAt | Timing |
| RequiresElevation | Permission record |
| ExitCode | Provider result |
| VerificationResult | Post-update result |
| SanitizedOutput | Bounded diagnostics |
| Event records | Step-by-step progress without secrets |

#### DownloadedArtifact

| Field | Purpose |
|---|---|
| DownloadedArtifactId | Internal stable ID |
| UpdateJobId | Optional update job association |
| SourceId / NativePackageId / Version | Provenance and target identity |
| RelativeCachePath | Path relative to the allowlisted PC Steward download-cache root; never an arbitrary absolute path from metadata |
| SizeBytes | Space reporting and quota enforcement |
| ExpectedHash / ObservedHash | Optional integrity evidence where available |
| State | Downloading, ready, in use, retained, deletion pending or deleted |
| CreatedAt / LastUsedAt / DeleteAfter | Retention and cleanup |
| LastDeletionError | Sanitized, user-visible failure if cleanup could not complete |

#### AppPolicy

| Field | Purpose |
|---|---|
| ProductId or InstallInstanceId | Current policy target when scan data exists |
| StableTargetKey | Persisted product/instance fingerprint used to reapply the policy after a later scan |
| PolicyType | Ignore, pin, preferred source, channel or reminder |
| Value | Validated policy value |
| Reason | Optional user note |
| ExpiresAt | Optional |
| CreatedAt / UpdatedAt | Lifecycle |

#### GlobalSettings and ConsentRecord

Stores schedule, notification, retention, enabled sources, export defaults, first-run consent version, privacy choices, scan-concurrency limits, performance preferences and app-owned download-cache retention/quota settings.

### 12.3 Data minimisation

PC Steward must not intentionally collect or store the following, except for the narrow optional provider-credential amendment below:

- passwords, tokens or browser cookies;
- software licence keys or product keys;
- personal documents or file contents;
- browser history;
- email content;
- a full environment-variable dump;
- arbitrary registry hives;
- precise usage telemetry for third-party applications; or
- cloud identifiers in the MVP.

**Accepted 2.0 credential/privacy amendment:** An explicitly connected OpenAI provider may use one current-user credential protected by DPAPI or Credential Manager (`FR-AI-03`; adapter selection is Q8). This exception does not permit collection of unrelated passwords, tokens, licence keys, browser data, cloud identifiers or environment dumps. Credentials never enter config, exports, backups, logs, diagnostic snapshots or saved explanations; disconnect and Factory reset delete them with accurate residual reporting. Diagnostics redact before persistence, AI uses the exact approved redacted payload, and submitted prompts are not saved. This defines the permitted design only; accepted ADRs and implementation gates remain prerequisites.

### 12.4 Retention defaults

| Data | Default retention |
|---|---|
| Current inventory | Until replaced or the user selects **Clear scan data** |
| Scan summaries and provider timing records | 90 days or until the user selects **Clear scan data** |
| Update history | Until the user deletes it; consider one year default after beta |
| Verbose logs | 30 days or 100 MB total, whichever occurs first |
| Source package metadata cache | Seven days, refreshed sooner on demand |
| PC Steward-owned downloaded update files after verified success | Seven days or the configurable 2 GB soft cap, whichever causes cleanup first; immediately removable by the user |
| Failed or incomplete app-owned download files | 24 hours by default unless retained for an active retry |
| Failed diagnostic bundle | Created only when the user requests it |
| 2.0 collected diagnostic snapshots | 30 days locally (D4), or earlier **Clear scan data** / Factory reset |
| 2.0 saved redacted AI explanations | 30 days locally (D4), or earlier Factory reset; further content-clearing behavior requires the accepted lifecycle design |
| 2.0 submitted AI prompts | Not saved (D4); no conversational history feature |
| 2.0 minimal spending/usage records and unresolved reservations | Separate from prompt/response content; retain as needed for accounting/reconciliation under the accepted budget ADR (Q7), not automatically expired at 30 days |

The download-cache soft cap excludes a file currently needed by an active update. Reaching the cap triggers oldest-first cleanup of inactive app-owned files and must never delete installed applications, provider-owned caches or rollback data outside PC Steward's directory.

Retention values must be configurable where practical.

### 12.5 Data-control semantics

| Control | Deletes | Preserves | Result |
|---|---|---|---|
| **Clear scan data** | Current inventory, scan-derived raw provider records, update candidates, scan runs/summaries, provider timing records and 2.0 collected diagnostic snapshots | Global settings, connector configuration, schedule, update-job history, app-owned downloaded files and stable user decisions/mappings | Dashboard returns to a no-scan state; a later scan repopulates inventory and reapplies matching user decisions |
| **Clear downloaded files** | Inactive update payloads and temporary extraction files in the PC Steward-owned download-cache root | Installed software, source metadata, scan data, update history, settings and provider-owned caches | Cache size is recalculated and any locked/failed item is reported |
| **Factory reset PC Steward** | Database, scan data, source metadata, logs, update history, downloaded files, user decisions, settings, consent records, current-user scheduled task and all new app-owned diagnostic/AI state, spending records/reservations and protected provider credentials | Installed third-party applications, PC Steward binaries, Windows Event Log/Reliability/Windows Update records and user exports | Next launch behaves as a first launch with a new empty store; residual state/credential failures block a complete-reset claim |

Factory reset is an application-state reset, not an uninstaller and not a Windows restore operation.

These 2.0 additions clarify `FR-DAT-06–07/12–15` and `AC-DAT-06–07/12–16` without changing their existing preservation guarantees. None of these controls deletes OS records, third-party caches, installed applications, leftovers or user exports. A compiled credential-adapter deletion operation targets only PC Steward's exact credential identity; it does not create arbitrary file/store deletion authority. Q5 governs new operation-history/favourites categories and Q8 governs the credential adapter before implementation.

## 13. Authentication and permissions

### 13.1 Authentication

The MVP requires **no PC Steward account**.

- The signed-in Windows session is the local user identity.
- Each Windows profile has a separate application database and settings.
- Core workflows require no password, OAuth flow, email address or cloud token. The optional 2.0 AI provider credential is the sole protected-credential exception described in sections 9.17 and 12.3; it is not a PC Steward account or cloud sign-in requirement.
- There is no remote-access listener.

### 13.2 Permission model

| Operation | Required context |
|---|---|
| Launch UI | Standard user |
| Read per-user inventory | Standard user |
| Read ordinary machine uninstall metadata | Standard user where Windows permits |
| Query WinGet and Store source data | Signed-in user context |
| Run local-only or online scan | Standard user |
| Update a per-user application | Standard user when provider supports it |
| Update a machine-wide application | On-demand UAC elevation for that operation |
| Change PC Steward schedule for current user | Standard user through Task Scheduler where possible |
| Clear scan data | Standard user |
| Clear PC Steward-owned downloaded files | Standard user |
| Factory-reset PC Steward and remove its current-user scheduled task | Standard user |
| Delete other selected local PC Steward data | Standard user |
| 2.0 catalogue install | User scope unelevated; machine scope only through the approved distinct install contract and protected one-shot helper |
| 2.0 manual uninstall | Applicable user/instance-bound supported route; any elevation requires the approved distinct uninstall contract |
| 2.0 diagnostic collection or AI explanation | Standard user; denied diagnostic access never triggers elevation; AI credentials remain current-user only |

The added rows are gated design requirements, not capabilities granted to the current helper. Scans, scheduled checks and AI output cannot create execution authority.

### 13.3 Elevated helper requirements

If an elevated helper is needed, it must:

- be a separately signed binary;
- start only for a user-approved operation;
- exit when the operation completes;
- accept only a small, versioned set of structured commands;
- validate caller identity and session;
- use restrictive IPC access controls;
- receive immutable source/package identifiers rather than arbitrary command lines;
- reconstruct the permitted provider command itself;
- reject unknown providers, flags, paths and protocols;
- write sanitized results to a controlled channel; and
- never read executable commands from SQLite or remote metadata.

No always-elevated tray process or service is permitted in the MVP.

**Accepted 2.0 operation amendment:** Install and uninstall need separate typed operation discriminators, fresh exact evidence and user approvals, independent runner/helper revalidation, replay/substitution rejection and operation-specific verification. Neither may be encoded as an update or supplied as a general command. ADR-017/018 and the current helper remain update-only until the owner accepts Task 45's applicable operation/security amendments and their later implementation is separately authorized. ADR-022's protected deployment/signing boundary remains effective.

### 13.4 Future cloud permissions

Any later multi-PC or cloud feature requires a new specification covering account authentication, MFA, device enrolment, encryption, data residency, recovery and remote-action approvals. It is not an extension of the MVP permission model.

---

## 14. Platforms and deployment requirements

### 14.1 Supported platform

**MVP target**

- Windows 11, currently supported releases.
- x64 architecture.
- Home and Pro editions where required Windows components are available.
- Standard desktop session; Windows S mode is not a target.
- Internet access required only for online source checks and updates.

**Later**

- Windows 11 Arm64.
- Additional supported Windows editions after testing.

Windows 10 is not an MVP target.

### 14.2 Recommended implementation baseline

This section guides implementation but may be changed through an architecture decision record if a prototype identifies a stronger option.

- Language: C#.
- Runtime: current supported patch of .NET 10 LTS.
- Desktop UI: WinUI through the current stable Windows App SDK.
- Local database: SQLite.
- Architecture: separate UI, application/domain core, inventory providers, source connectors and update-execution components.
- Scheduling: Windows Task Scheduler.
- Logging: structured local logs with redaction and bounded retention.
- Testing: unit tests for normalization/version/matching rules; integration tests against isolated Windows test environments.

The initial WinGet integration may use a constrained process adapter if that is the most reliable supported route, but it must sit behind an interface so a later API-based implementation does not change product behaviour.

### 14.3 Packaging and distribution

The MVP build must support:

- a reproducible release build;
- code signing for distributed binaries before external beta use;
- a standalone Windows x64 ZIP package that runs without installation;
- an explicit removal workflow that deletes the extracted binaries, removes the exact current-user scheduled task and lets the user retain or remove per-user data through the existing in-app factory reset;
- a documented manual update path for PC Steward itself; and
- a stable per-user data root so replacing the standalone package retains settings and history.

**Preferred distribution options to evaluate**

1. Signed standalone Windows x64 ZIP for the private beta, operating as standard user only.
2. A protected signed installer plus separately signed helper only in a future reviewed deployment if machine-scope update execution is restored.
3. Private distribution during development, with Microsoft Store distribution considered after the security and update model is stable.

The standalone package must omit the elevated helper. Machine-scope candidates may be displayed for awareness but must be non-actionable before job creation. The packaging choice and any future elevation-capable deployment must be recorded in architecture decisions.

### 14.4 Deployment requirements

- The application must not require SQL Server, Docker, a browser extension or a cloud backend.
- Normal startup must not require administrator rights.
- Removing the standalone package means closing PC Steward, running its exact scheduled-task cleanup script, and deleting the extracted package directory.
- Per-user inventory/history data is retained by default; the user removes it through **Factory reset PC Steward** before deleting the package.
- A separate in-app **Factory reset PC Steward** action must delete all local state and the current-user scheduled task while leaving installed binaries in place.
- Development builds must be visibly labelled and must not silently update into production builds.
- Closing the application must leave no PC Steward background process running unless a user-approved update or scheduled scan is actively in progress; that operation must exit when complete.

## 15. Security requirements

### 15.1 Security objectives

1. Do not execute the wrong package for an installed application.
2. Do not let remote metadata become an arbitrary command-execution channel.
3. Minimise privileged code and privileged execution time.
4. Preserve installer provenance and update history.
5. Protect the user's software inventory from unnecessary disclosure.
6. Fail closed when identity, channel, architecture or source trust is uncertain.
7. Ensure data-clearing and reset controls can delete only PC Steward-owned data and cannot be redirected to arbitrary files or directories.

### 15.2 Threats and required controls

| Threat | Required controls |
|---|---|
| Malicious or compromised source metadata | Validate schemas and lengths; treat strings as data; never execute source-supplied commands; display source; allow source disablement. |
| False application/package match | Deterministic confidence rules; stable identifiers; architecture/channel checks; manual confirmation; no action for low confidence. |
| Installer substitution or tampering | Use provider verification in the MVP; later require HTTPS, expected hash where authoritative, signature validation and revalidation immediately before execution. |
| Command injection | Avoid shell invocation; pass structured argument arrays; use allowlisted executables and flags; never concatenate user/source strings into commands. |
| Privileged-helper abuse | Short-lived helper, restrictive IPC, caller validation, small command surface, immutable package IDs and no arbitrary path execution. |
| Downgrade or replay | Compare versions and reject lower candidates by default; show source timestamps; require a separate explicit downgrade workflow later. |
| Time-of-check/time-of-use replacement | Verify downloaded artefact after download and immediately before execution when direct downloads are introduced. |
| Tampered local database | Treat database content as untrusted on read; never store executable command lines; revalidate all provider/package IDs. |
| Misleading success | Post-update inventory verification; preserve provider exit code; distinct inconclusive and restart-pending states. |
| Privacy leakage | No account, no default telemetry, no inventory upload, local exports only, redaction and retention controls. |
| Compromised PC Steward update | Signed releases, secure update channel and rollback/manual recovery documentation for PC Steward itself. |
| Denial of service from malformed inventory/source data | Bounded fields, timeouts, cancellation, isolated connector errors and resilient database transactions. |
| Resource exhaustion from scans or caches | Single active scan, bounded concurrency, lower-priority scheduled work, measurable CPU/memory budgets, storage quotas and cancellable operations. |
| Unsafe cleanup, path traversal or junction abuse | Delete only from fixed app-owned roots; canonicalize paths; reject reparse points/junction escapes; never use source/user strings as deletion paths; test destructive operations with hostile fixtures. |
| Partial or misleading factory reset | Transactional reset plan, explicit confirmation, active-job exclusion, accurate failure reporting and first-run verification on restart. |

### 15.3 Installer and package rules

For the MVP:

- actioned updates must run through WinGet or another explicitly approved provider;
- PC Steward must pass a stable package ID and source, not merely a display name;
- ambiguous provider results must stop and require review;
- source-agreement is useful evidence but is not a replacement for package identity;
- no raw URL can be converted directly into an update job;
- package manager or installer output must be sanitized before display and storage; and
- an update that requests unexpected publisher or channel changes must stop where detectable.

For later direct-download connectors:

- the source must be mapped to an official vendor/project identity;
- the download must use HTTPS;
- hash verification is required when a trusted authoritative hash is published;
- Authenticode signature validity and publisher continuity must be checked for signed Windows installers;
- unsigned installers require an explicit product-level exception and may remain manual-only;
- the artefact must be scanned by the operating system's configured security controls; and
- the artefact must be revalidated immediately before launch.

### 15.4 Development security requirements

- Enable nullable reference types and strict compiler warnings.
- Keep secrets out of source control. Core workflows require none; optional 2.0 provider credentials are protected only through the reviewed current-user adapter and never enter config, exports, backups or logs.
- Pin dependencies through lock files where supported.
- Generate a software bill of materials for release builds.
- Run dependency and static analysis in CI.
- Require review for changes to matching, source trust, update execution, elevation and destructive data-deletion code.
- Sign release artefacts before external distribution.
- Maintain a security policy and a responsible disclosure route before public release.

### 15.5 Destructive data-operation rules

- Cleanup code may target only fixed, internally configured PC Steward data roots and database records.
- Before deleting a file or directory, PC Steward must canonicalize the path and verify that it remains inside the expected allowlisted root.
- Cleanup must not follow directory junctions, symbolic links, mount points or other reparse points outside the allowed root.
- Remote metadata, package names, publisher names, log contents and arbitrary user text must never become deletion paths.
- Files associated with an active update job are protected from deletion.
- **Clear scan data** must use a database transaction and leave the app in either the previous valid state or a valid no-scan state.
- Factory reset must use a deterministic reset manifest and verify on the next launch that no previous database, cache, schedule or settings were reloaded.
- Locked files or schedule-removal failures must be reported accurately and may be queued for deletion on restart; the UI must not claim a complete reset until verification succeeds.
- For 2.0, extend the deterministic lifecycle manifest and tests to diagnostic snapshots, saved redacted explanations, minimal spending state and the exact protected provider credential. Verify idempotence, locked/residual reporting and no old-state reload. Retain the existing canonical-root/reparse controls; credential deletion uses the compiled exact adapter identity, never a metadata-supplied target. No OS record, provider cache, uninstall leftover or user export becomes a deletion target.

## 16. Privacy requirements

### 16.1 Privacy stance

PC Steward is local-first and does not require telemetry to function.

### 16.2 Requirements

- No inventory, path, publisher, version or update history is uploaded by default.
- No account is required.
- Network requests are limited to enabled update connectors, the product's own update check when enabled, and the narrow optional 2.0 AI exception: an explicitly connected provider's approved TLS endpoint may receive only the exact edited/redacted failure or timeline evidence approved for that call. Source consent is not AI consent. No credential forwarding across origins, dump/full-log upload, general cloud synchronization or remote-action authority follows.
- AI remains off without provider opt-in, a protected key, valid known pricing, sufficient reserved budget and exact payload approval. Preview/redaction cannot guarantee every secret is removed, and the UI must say so. Model output is advisory untrusted text with no system-changing tools or influence on deterministic eligibility.
- The UI must show which connectors are enabled and when each was last contacted.
- Optional analytics, if ever added, must be off by default during private beta and separately consented.
- Crash reports must be user-initiated or explicitly opted in and sanitized.
- Exports must warn about the sensitivity of installed-software inventories.
- Users can independently clear scan data and PC Steward-owned downloaded update files.
- Users can factory-reset all local PC Steward state without uninstalling the application.
- Data clearing and factory reset must work without an internet connection and must not transmit a deletion event.
- Logs must not contain credentials, tokens, licence keys or document contents.
- The application must document that a local administrator or malware already controlling the PC may access local data; PC Steward does not claim to defend against a compromised operating system.

## 17. User experience and accessibility requirements

### 17.1 Primary navigation

Recommended main sections:

1. **Overview** — scan state and attention summary.
2. **Applications** — full searchable inventory.
3. **Updates** — verified and review-required update queue.
4. **Unmanaged** — unmatched and unknown-version applications.
5. **History** — scans and update jobs.
6. **Decisions** — pins, ignores, mappings and preferred sources.
7. **Settings** — sources, schedule, notifications, retention, privacy, exports and a distinct **Data & reset** area.

### 17.2 Update detail design

Every actionable update must show:

- installed product name and version;
- installed publisher, scope and architecture where known;
- candidate version and channel;
- source name and stable package ID;
- match confidence and plain-language explanation;
- whether the application appears to be running;
- likely elevation and restart behaviour;
- release information link where available; and
- the exact action buttons and consequences.

### 17.3 Data management design

The **Data & reset** area must:

- show separate cards for **Scan data**, **Downloaded update files**, **Source metadata**, **Logs and history**, and **Factory reset**;
- show an approximate size and last-updated or last-cleaned time where meaningful;
- use the exact primary labels **Clear scan data**, **Clear downloaded files** and **Factory reset PC Steward**;
- explain in plain language what each action deletes and preserves before confirmation;
- place factory reset in a visually separated destructive-action area rather than beside routine cleanup controls;
- provide a second confirmation for factory reset;
- disable or explain unavailable controls during an active scan or update; and
- provide **Delete downloaded files** on an update-completion view when app-owned files remain.

### 17.4 Accessibility

The MVP should, on a best-effort basis:

- support full keyboard navigation for primary journeys;
- provide accessible names for controls and status indicators;
- avoid communicating status by colour alone;
- respect Windows text scaling and high-contrast settings;
- provide visible focus indicators;
- use readable error text rather than error codes alone; and
- avoid rapidly changing or indeterminate progress without a text description.

### 17.5 Tone

- Calm, factual and non-alarmist.
- Avoid “critical” or “dangerous” unless the product later has authoritative security evidence.
- Prefer “unmanaged” or “source not found” to “unsafe.”
- Never imply that an update is guaranteed to fix a PC problem.

---

## 18. Non-functional requirements

### 18.1 Performance and resource efficiency

| ID | Requirement |
|---|---|
| NFR-PERF-01 | Local inventory must complete within 15 seconds at the 95th percentile on the agreed baseline PC with 300 provider records. |
| NFR-PERF-02 | A full online check must complete within 90 seconds at the 95th percentile when sources are healthy; source timeouts must not block the whole application indefinitely. |
| NFR-PERF-03 | The UI must remain responsive during scans and updates. |
| NFR-PERF-04 | The application should cold-start to an interactive shell within five seconds on the baseline PC. |
| NFR-PERF-05 | With the UI open and no active job for five minutes, average PC Steward CPU usage must remain at or below 1% of total system CPU, working set should remain at or below 300 MB, and the app must not perform recurring disk or network activity. |
| NFR-PERF-06 | During a local scan on the baseline PC, the combined PC Steward process tree must average no more than 20% total CPU and remain below 500 MB peak working set. App-owned work must not sustain more than 50% total CPU for over five seconds. |
| NFR-PERF-07 | Closing the UI must leave no resident PC Steward process when no approved update or scheduled scan is active. A scheduled scan process must exit within 30 seconds after completion, cancellation or final timeout handling. |
| NFR-PERF-08 | A scan cancellation request must be acknowledged in the UI within two seconds and return the application to an operable state within five seconds, including termination of cancellable child-provider work. |
| NFR-PERF-09 | Only one full scan may run at a time. Online connector concurrency defaults to a maximum of three and must be configurable internally without a code rewrite. |
| NFR-PERF-10 | PC Steward-owned downloaded update files must be bounded by a configurable soft quota with a 2 GB default, excluding a file actively required by an update. |

The performance budgets apply to PC Steward and child processes it launches. They do not include unrelated installer work after control has been handed to a third-party interactive installer, but PC Steward must still remain responsive and report that distinction.

### 18.2 Reliability

| ID | Requirement |
|---|---|
| NFR-REL-01 | A failed connector must not discard successful results from other connectors. |
| NFR-REL-02 | Database writes must be transactional so interrupted scans do not corrupt the last known good inventory. |
| NFR-REL-03 | A cancelled or crashed batch must preserve completed job results and leave unstarted jobs clearly queued or cancelled. |
| NFR-REL-04 | Cached results must always show their timestamp and must not be presented as a live successful check. |
| NFR-REL-05 | The application must recover gracefully from malformed or missing registry values. |

### 18.3 Maintainability

| ID | Requirement |
|---|---|
| NFR-MNT-01 | Inventory providers and update connectors must implement stable interfaces and fail independently. |
| NFR-MNT-02 | Matching and version rules must be unit-testable without launching the UI. |
| NFR-MNT-03 | Provider commands, argument construction and result parsing must be isolated from presentation code. |
| NFR-MNT-04 | Database migrations must be versioned and reversible during development. |
| NFR-MNT-05 | Architecture decisions affecting security or deployment must be recorded under `docs/adr`. |

### 18.4 Observability

| ID | Requirement |
|---|---|
| NFR-OBS-01 | Each scan and update job receives a correlation ID. |
| NFR-OBS-02 | Logs use structured events and severity levels. |
| NFR-OBS-03 | Diagnostics distinguish user cancellation from product or installer failure. |
| NFR-OBS-04 | Logs are bounded by retention and file-size policy. |
| NFR-OBS-05 | User-facing diagnostics can be exported in sanitized form. |

---

## 19. MVP acceptance criteria

### 19.1 Test environment

Acceptance testing must use:

- supported, clean Windows 11 x64 test environments on the releases in scope; these may be physical or virtual;
- an agreed baseline performance environment with at least 4 vCPU, 8 GB RAM and SSD-equivalent storage, using the same power plan for repeatable tests;
- the product owner's real PC for exploratory testing after isolated safety tests;
- a curated fixture set containing machine-wide MSI/EXE apps, per-user apps, MSIX/Store apps, runtimes/components, duplicate user/machine instances, an app with no version, an unmatched app and at least ten WinGet-updatable applications;
- both current and deliberately outdated test versions where licences permit; and
- hostile cleanup fixtures containing locked files, nested directories and reparse-point/junction attempts inside the test cache root.

### 19.2 Inventory acceptance

| ID | Acceptance criterion |
|---|---|
| AC-INV-01 | Given applications registered in each supported inventory surface, a scan creates a record for every fixture entry expected from that surface. |
| AC-INV-02 | Duplicate raw records for the same install instance are presented once, while distinct user/machine or x86/x64 instances remain separately inspectable. |
| AC-INV-03 | The scan does not invoke `Win32_Product` and does not trigger MSI repair operations. |
| AC-INV-04 | A malformed or incomplete registry entry does not crash the scan; it is either represented with unknown fields or recorded as a provider warning. |
| AC-INV-05 | A local-only scan makes no update-source network request. |
| AC-INV-06 | A normal scan does not require or request administrator elevation. |
| AC-INV-07 | The application shows the provider and freshness of every displayed inventory record. |

### 19.3 Matching and update-check acceptance

| ID | Acceptance criterion |
|---|---|
| AC-MAT-01 | Every actionable update displays a stable package ID, source, candidate version, confidence and explanation. |
| AC-MAT-02 | No low-confidence or unmatched candidate exposes an enabled Update button. |
| AC-MAT-03 | The curated fixture set produces zero false-positive executable update mappings. |
| AC-MAT-04 | Architecture or channel incompatibility prevents an update from being actionable. |
| AC-MAT-05 | Version strings that cannot be ordered safely produce an Unknown/Review state rather than a guessed update. |
| AC-MAT-06 | A temporary source failure leaves the last cached result visible with a stale timestamp and a source-unavailable warning. |
| AC-MAT-07 | The user can create and later delete a manual mapping to a trusted package ID. |

### 19.4 Update execution acceptance

| ID | Acceptance criterion |
|---|---|
| AC-UPD-01 | No installer runs without the user selecting it and confirming the individual or batch review screen. |
| AC-UPD-02 | The update runner accepts only approved provider operations and rejects arbitrary executable paths or command strings. |
| AC-UPD-03 | A per-user update that does not require elevation completes without a UAC prompt. |
| AC-UPD-04 | A machine-wide update requests UAC only when execution begins, not during scanning or review. |
| AC-UPD-05 | If the user declines UAC, the job is recorded as cancelled/denied rather than failed or successful. |
| AC-UPD-06 | After a successful test update, the post-update scan detects the expected newer version. |
| AC-UPD-07 | A non-zero provider or installer result is never displayed as success. |
| AC-UPD-08 | If the installer reports success but the expected version is not detected, the result is Inconclusive or Restart required, not Success. |
| AC-UPD-09 | Batch execution runs sequentially and reports each item's independent result. |
| AC-UPD-10 | Cancelling a batch prevents the next unstarted item from running and preserves completed results. |
| AC-UPD-11 | Update jobs record timestamps, provider, package ID, versions, exit code and sanitized diagnostic output. |

### 19.5 Data, privacy and permission acceptance

| ID | Acceptance criterion |
|---|---|
| AC-DAT-01 | Closing and reopening PC Steward preserves inventory, mappings, decisions and update history. |
| AC-DAT-02 | CSV and JSON exports match the displayed current inventory and identify export time. |
| AC-DAT-03 | The product functions for inventory and history while offline. |
| AC-DAT-04 | Network inspection confirms that the MVP contacts only enabled connectors and the optional PC Steward update endpoint. |
| AC-DAT-05 | No account, email address or cloud sign-in is required. |
| AC-DAT-06 | Selecting **Clear scan data** removes current inventory, raw scan/provider records, update candidates and scan-run records, and the dashboard returns to a no-scan state. |
| AC-DAT-07 | After **Clear scan data**, global settings, enabled sources, schedule, update-job history and app-owned downloaded files remain unchanged. A subsequent scan can reapply a previously saved pin, ignore or manual mapping to the same product. |
| AC-DAT-08 | Selecting **Clear downloaded files** removes all inactive files in the PC Steward-owned download-cache root, updates the displayed size to zero where no locked item remains, and does not alter installed applications, scan data, source metadata or update history. |
| AC-DAT-09 | When an update completes and app-owned downloaded files remain, the result view shows **Delete downloaded files** and reports the correct recoverable size. |
| AC-DAT-10 | Provider-owned Windows, WinGet or Store caches remain untouched unless a documented provider cleanup adapter is explicitly implemented and tested. |
| AC-DAT-11 | A locked or protected app-owned file produces an accurate partial-cleanup result and is not silently reported as deleted. |
| AC-DAT-12 | Factory reset removes the SQLite database, scan data, source metadata, logs, update history, app-owned downloaded files, settings, consent records, user decisions and current-user scheduled task after two confirmations. |
| AC-DAT-13 | After factory reset and restart, first-run onboarding appears and no previous inventory, history, settings, mappings, pins or ignores is visible. PC Steward remains installed. |
| AC-DAT-14 | Clear and reset controls require no administrator elevation for the per-user MVP data model. |
| AC-DAT-15 | Clear and reset actions are unavailable during an active update or scan until the user cancels or completes it. |
| AC-DAT-16 | Cleanup tests prove that a junction, symbolic link or crafted metadata value cannot cause deletion outside the allowlisted PC Steward data roots. |
| AC-DAT-17 | Default logs contain no credentials, licence keys, browser data or document contents. |
| AC-DAT-18 | Removing the standalone package deletes the extracted binaries and exact current-user scheduled task. Per-user data is retained by default; running **Factory reset PC Steward** before package removal provides the explicit remove-data choice. |

**Accepted 2.0 acceptance amendments:** `AC-DAT-04` retains the baseline connector/update-endpoint check and gains a separate opt-in AI matrix: AI-disabled, unconsented, unapproved-payload, unknown-price and exhausted-budget cases make no AI request; an approved call contacts only the approved TLS provider endpoint with exactly the edited/redacted payload; cross-origin redirects never receive credentials. This is a required future test, not Task 44 network-inspection evidence. `AC-DAT-06–07/12–16` additionally exercise collected-snapshot deletion, preserved update history, app-owned AI/credential reset, residual failures and OS-record/export preservation as specified in sections 9.16–9.17 and 12.5. Their original baseline criteria remain unchanged above. New favourites and install/uninstall history require Q5's accepted lifecycle before persistence tests are defined.

### 19.6 UX and accessibility acceptance

**Product-owner priority decision (ADR-021):** `AC-UX-01–09` are Optional — best effort. They remain implementation goals, but missing evidence does not fail a phase or release gate.

| ID | Acceptance criterion |
|---|---|
| AC-UX-01 | A first-time user can complete the first scan and identify available updates without using a command line. |
| AC-UX-02 | All primary navigation, scan, review, update, clear-data and factory-reset actions are keyboard accessible. |
| AC-UX-03 | Status is expressed through text/icons as well as colour. |
| AC-UX-04 | Windows text scaling and high-contrast mode do not hide primary controls or status text. |
| AC-UX-05 | Every failure view contains a plain-language summary and a safe next action. |
| AC-UX-06 | The UI always distinguishes live, cached and local-only scan results. |
| AC-UX-07 | The Data & reset page visibly separates routine cleanup from factory reset and shows what each action deletes, preserves and can recover in storage. |
| AC-UX-08 | Factory reset requires a second explicit confirmation and cannot be triggered by a single accidental click or keyboard activation. |
| AC-UX-09 | During a scan, the user can see elapsed time, current phase and an enabled **Cancel scan** action. |

### 19.7 Performance and resilience acceptance

| ID | Acceptance criterion |
|---|---|
| AC-NFR-01 | Local inventory meets NFR-PERF-01 on the agreed baseline test system over at least 30 repeated runs after warm-up. |
| AC-NFR-02 | Full online scan meets NFR-PERF-02 when test sources are healthy over at least 20 repeated runs. |
| AC-NFR-03 | Cancelling a scan or connector request returns the UI to an operable state without data corruption and meets NFR-PERF-08. |
| AC-NFR-04 | Terminating the application during a scan leaves the previous successful inventory readable on next launch. |
| AC-NFR-05 | A connector timeout does not block results from other connectors. |
| AC-NFR-06 | Five-minute idle measurement meets the CPU, memory, disk and network limits in NFR-PERF-05. |
| AC-NFR-07 | Local-scan process-tree measurement meets the CPU and memory limits in NFR-PERF-06. |
| AC-NFR-08 | Closing PC Steward with no active work leaves no PC Steward process after 30 seconds. |
| AC-NFR-09 | Attempts to start a second scan are rejected or routed to the existing scan, and measured connector concurrency never exceeds the configured bound. |
| AC-NFR-10 | Filling the app-owned download cache beyond its soft quota evicts only inactive oldest files and never blocks or deletes the artifact required by an active update. |
| AC-NFR-11 | A scheduled scan runs without launching a persistent tray app and exits within the NFR-PERF-07 limit. |

### 19.8 Release acceptance / definition of done

The MVP is ready for private beta only when:

- all mandatory acceptance criteria pass;
- the zero-false-positive fixture requirement passes;
- the threat model has been reviewed against the implemented architecture;
- release binaries are signed for distribution outside the developer's own machine;
- critical update-execution and privilege code has automated tests and manual review;
- dependency, static-analysis and secret-scanning checks pass;
- an SBOM is generated for the release;
- standalone launch, replacement-upgrade, scheduled-task cleanup, package removal and factory-reset paths have been tested on a supported Windows test machine;
- scan performance and idle-resource acceptance criteria pass on the agreed baseline;
- destructive cleanup path-safety, locked-file and partial-reset tests pass;
- known limitations are documented in the application and release notes; and
- the product owner has completed a real-PC scan in local-only mode before enabling update execution.

---

## 20. Product metrics

The MVP should calculate these metrics locally for product evaluation. They must not be uploaded without separate consent.

| Metric | Definition | Initial target |
|---|---|---|
| Inventory coverage | Supported fixture entries detected / expected entries | 100% for agreed fixture surfaces |
| Actionable match precision | Correct actionable mappings / all actionable mappings | 100% in fixture tests |
| Managed coverage | Install instances with verified/high mapping / eligible app instances | Baseline measurement; improve over releases |
| Update success rate | Verified successful jobs / attempted supported jobs | At least 95% in controlled fixture tests |
| Verification rate | Jobs with definitive post-update state / completed jobs | At least 95% in controlled fixture tests |
| Median and 95th-percentile local scan time | Time for local inventory | Median under 10 seconds; 95th percentile within NFR-PERF-01 |
| Median and 95th-percentile online scan time | Time for healthy full online checks | 95th percentile within NFR-PERF-02 |
| Idle CPU and working set | Five-minute idle measurement | Within NFR-PERF-05 |
| Local-scan CPU and peak working set | PC Steward process-tree measurement | Within NFR-PERF-06 |
| App-owned download-cache size | Retained inactive update files | At or below configured quota |
| Cleanup completion rate | Requested app-owned items successfully removed / eligible items | 100%, excluding accurately reported locked files |
| Source failure isolation | Scans where one failed connector blocks others | 0 |
| Crash-free sessions | Sessions without unhandled crash | At least 99.5% during private beta |
| Unmanaged resolution rate | User-mapped items / manually reviewed unmanaged items | Measure, no initial target |

Precision is more important than coverage. A missing mapping is preferable to a wrong executable mapping. Low resource use is a release requirement, not an optional optimisation.

## 21. Key risks and mitigations

| Risk | Consequence | Mitigation |
|---|---|---|
| Wrong package match | Installs the wrong product or channel | Stable IDs, deterministic confidence, zero-false-positive acceptance, manual mapping, no low-confidence execution |
| WinGet output or behaviour changes | Broken parsing or update execution | Adapter abstraction, version capability check, integration tests, graceful unsupported-state message |
| Source metadata is stale | User believes an older package is current | Show timestamps, refresh controls, source disagreement view |
| Installer requires interaction or closes apps | Interrupted work or failed batch | Detect/declare interactivity where possible, explicit review, sequential jobs, no force close |
| Installer exit code is unreliable | False success/failure | Post-update rescan and separate inconclusive state |
| Multiple installations of same app | Wrong instance updated or confusing result | Preserve install instances; show scope/architecture; provider-specific targeting |
| Non-standard versions | Incorrect update ordering | Connector-specific comparators and Unknown state |
| Elevated helper expands attack surface | Local privilege escalation opportunity | Short-lived, signed, constrained helper; restrictive IPC; independent security review |
| Privacy concerns about software inventory | Loss of trust | Local-only storage, no account, no default telemetry, clear exports/deletion |
| Scope expands into generic “PC optimiser” | Poor quality and unsafe changes | Explicit non-goals and product principles |
| Direct vendor updates are added too early | Supply-chain and matching risk | Keep direct execution outside MVP; require new controls and acceptance tests |
| Scanning consumes excessive CPU, memory or time | Poor gaming/work performance and user distrust | Measurable budgets, bounded concurrency, cancellation, lower-priority scheduled work and repeatable performance tests |
| Download cache grows without limit | Unnecessary disk consumption | Separate app-owned cache, visible size, user cleanup button, retention and soft quota |
| Cleanup or factory reset deletes the wrong data | Data loss outside PC Steward | Allowlisted roots, path canonicalization, reparse-point protection, transactional data deletion and hostile-fixture tests |
| Factory reset only partially completes | Old state unexpectedly returns | Reset manifest, second confirmation, accurate partial-failure reporting and first-launch verification |

---

## 22. Open product and architecture decisions

These decisions are intentionally left for focused design or prototype work. They should be recorded in `docs/adr` before implementation is locked.

| ID | Decision | Default until decided |
|---|---|---|
| OD-01 | Final product name and visual identity | Use “PC Steward” internally |
| OD-02 | WinUI packaged MSIX versus unpackaged/signed installer | Prototype both elevation and WinGet access; prefer signed MSIX if requirements are met |
| OD-03 | WinGet CLI adapter versus supported programmatic API | Start behind an abstraction; choose the most stable supported route after spike |
| OD-04 | Whether read-only GitHub Releases checks are mandatory for MVP | Include only if core WinGet inventory/update flow is stable |
| OD-05 | How aggressively to group runtimes and redistributables | Preserve raw instances; begin with conservative grouping |
| OD-06 | Baseline hardware for performance tests | Product owner's PC plus an agreed, recorded Windows baseline (physical or virtual) |
| OD-07 | Default schedule | Weekly, disabled until first-run consent |
| OD-08 | Product licence and whether source code will be public | Undecided; private repository during initial build |
| OD-09 | Application self-update mechanism | Manual signed release for first private build; automate only after signing/distribution decision |
| OD-10 | Whether diagnostic exports include installation paths by default | Default to redacted paths with an explicit include option |
| OD-11 | Final default download-cache quota and retention after real installer-size testing | Start with a 2 GB soft quota, seven-day successful-file retention and 24-hour failed-file retention |
| OD-12 | Exact process-priority mechanism for scheduled scans | Prefer BelowNormal priority and Task Scheduler conditions without an always-running coordinator |
| OD-13 | Factory-reset implementation: in-process restart, next-launch cleanup marker or short-lived unelevated cleanup helper | Choose the simplest design that can verify complete deletion without privileged or arbitrary-path capability |

---

## 23. Suggested implementation sequence — no code yet

### Phase 0 — Planning and spikes

- Approve this product specification.
- Create architecture decision record template.
- Define the repeatable performance baseline and create a scan benchmark harness.
- Prototype read-only inventory providers.
- Prototype WinGet capability detection, list parsing/API access and update invocation in an isolated disposable Windows environment.
- Prototype standard-user UI plus on-demand elevation.
- Prototype path-safe deletion within a disposable app-data root, including locked files and reparse-point attempts.
- Decide packaging and provider integration approach.
- Build the fixture application matrix.

### Phase 1 — Inventory vertical slice

- App shell and navigation.
- SQLite schema and migrations.
- Registry, MSIX/AppX and WinGet inventory providers.
- Canonical product/install-instance model.
- Scan progress, cancellation and provider timing.
- Scan history and application list.
- Local-only scan and export.
- **Clear scan data** with preserved stable user decisions.
- Idle and local-scan performance instrumentation and tests.

**Exit condition:** inventory, clear-scan-data and local performance acceptance criteria pass without update execution.

### Phase 2 — Update intelligence

- WinGet source refresh and package mapping.
- Confidence explanations.
- Version comparison and channel/architecture checks.
- Current/update/review/unmanaged statuses.
- Manual mapping, pin and ignore.

**Exit condition:** matching fixture reaches zero false-positive actionable mappings.

### Phase 3 — Safe update execution

- Immutable update jobs.
- Constrained provider runner.
- On-demand elevation if needed.
- Sequential batch queue.
- Post-update verification.
- Results, logs and retry flow.
- App-owned downloaded-artifact tracking and bounded cache.
- **Delete downloaded files** in Settings and on update completion.

**Exit condition:** update execution and downloaded-file cleanup acceptance criteria pass in supported clean Windows test environments.

### Phase 4 — Scheduling, polish and private beta

- Task Scheduler integration.
- Windows notifications.
- Accessibility pass.
- Retention, privacy and advanced data-deletion controls.
- **Factory reset PC Steward** with reset verification.
- Scheduled-scan priority and no-resident-process verification.
- Signing, installer, SBOM and release documentation.
- Controlled test on the product owner's PC.

**Exit condition:** all resource, cleanup, reset, security and release acceptance criteria pass.

## 24. Codex handoff requirements

When this specification is moved into a repository, Codex should be instructed to:

1. treat this document as the product source of truth;
2. propose an architecture and task plan before writing production code;
3. maintain requirement IDs in issues, tests and pull-request descriptions;
4. create architecture decision records for the open decisions;
5. implement the smallest vertical slice first;
6. keep the update runner, source connectors and privileged helper isolated;
7. write tests for version parsing, normalization and matching before enabling execution;
8. build a repeatable performance harness before claiming that scan speed or resource-use requirements pass;
9. model scan data, user decisions, source metadata, logs and downloaded update files as distinct lifecycle categories;
10. implement **Clear scan data**, **Clear downloaded files** and **Factory reset PC Steward** as separate actions with the exact preservation/deletion semantics in this specification;
11. threat-model every destructive file operation and test path canonicalization, locked files and reparse points;
12. avoid adding features explicitly listed as later or prohibited;
13. never use arbitrary shell commands, unrestricted web downloads or `Win32_Product`; and
14. stop an implementation path that would weaken the source-trust, local-first, least-privilege, resource-budget or path-safe-deletion requirements.

The first Codex task should be architecture and repository planning only, not a request to “build the whole app.”

## 25. Reference basis

The specification is informed by the following official product documentation current on 27 July 2026:

- Microsoft WinGet overview: https://learn.microsoft.com/windows/package-manager/winget/
- WinGet list command: https://learn.microsoft.com/windows/package-manager/winget/list
- WinGet upgrade command: https://learn.microsoft.com/windows/package-manager/winget/upgrade
- WinGet source security guidance: https://learn.microsoft.com/windows/package-manager/winget/source
- Windows Installer uninstall registry properties: https://learn.microsoft.com/windows/win32/msi/uninstall-registry-key
- WinUI / Windows App SDK overview: https://learn.microsoft.com/windows/apps/windows-app-sdk/
- WinUI recommendation for Windows desktop apps: https://learn.microsoft.com/windows/apps/winui/winui3/
- .NET support policy: https://dotnet.microsoft.com/platform/support/policy/dotnet-core
- ChatGPT Projects overview: https://help.openai.com/en/articles/10169521-projects-in-chatgpt

These references inform platform and capability assumptions; the product's safety requirements remain stricter than merely relying on a package manager's existence.

---

## 26. Approval checklist

Before implementation begins, the product owner should explicitly approve or amend:

This is the historical MVP checklist. Task 44 records acceptance of the 2.0 requirement deltas and D1–D5 in section 11.5 only; unchecked historical items and future ADR/security/live-action gates are not automatically signed off by this revision.

- [ ] Product vision and promise.
- [ ] Windows 11 x64-only MVP.
- [ ] Local-only, account-free MVP.
- [ ] WinGet and the fixed, explicitly enabled Chocolatey Community source as actionable update providers. Chocolatey actions are machine-scope only, use the protected elevation helper, and retain the same individual review, immutable-job, bounded-output and post-update verification gates.
- [ ] No drivers, firmware, Windows Update or arbitrary website downloads.
- [ ] Manual approval before every installation.
- [ ] Separate install instances rather than over-aggressive deduplication.
- [ ] Optional weekly scheduled checks with no scheduled installation.
- [ ] Data fields and retention defaults.
- [ ] Security and privacy requirements.
- [x] Bounded CPU, memory, disk, concurrency and scan-time requirements are mandatory for the MVP.
- [x] A dedicated **Clear scan data** button is mandatory for the MVP.
- [x] Dedicated **Clear downloaded files** controls in Settings and after applicable updates are mandatory for the MVP.
- [x] A dedicated **Factory reset PC Steward** button returning the app to first-run state is mandatory for the MVP.
- [ ] Acceptance criteria and fixture-test approach.
- [ ] Open decisions to be resolved by architecture spikes.

Once approved, changes to security boundaries, source trust, update execution, authentication or cloud use should update this specification and receive a new version number.
