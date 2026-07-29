# Phase 2 — Update intelligence

## Objective

Resolve trusted WinGet source data into explainable, deterministic, non-executable update candidates, with version rules, confidence, manual mapping, pins, ignores, cached freshness, and zero false-positive actionable mappings in the fixture set.

## Required task order

1. **Task 16:** Version comparison engine
2. **Task 17:** Deterministic matching and confidence engine
3. **Task 18:** WinGet update intelligence, source cache, and freshness
4. **Task 19:** Manual decisions and update-intelligence UX
5. **Task 19A:** Prepare Phase 2 review evidence
6. **Task 20:** Phase 2 review gate

## Exit condition

The curated fixture set produces zero false-positive actionable mappings; all low-confidence or incompatible candidates remain non-actionable; source outages preserve clearly stale results.

## Scope boundaries

- Follow `AGENTS.md`.
- Do not implement Later/Never features.
- Do not begin the next phase until the phase review passes and the branch is merged.
- Requirement IDs may appear in more than one task when implementation and later hardening both provide evidence.

## Requirement and acceptance cross-reference

| ID | Requirement / acceptance criterion | MVP marker | Primary task(s) |
|---|---|---|---|
| `FR-VER-01` | Compare numeric dotted versions correctly when both sides provide them. | Yes | 16 |
| `FR-VER-02` | Handle prerelease labels and avoid offering Beta/Preview builds to Stable users by default. | Yes | 16 |
| `FR-VER-03` | Defer to connector-specific version semantics where generic comparison is unsafe. | Yes | 16 |
| `FR-VER-04` | Mark a result as unknown rather than inventing an ordering for incomparable versions. | Yes | 16 |
| `FR-VER-05` | Prevent a downgrade unless the user enters a dedicated future downgrade workflow. | Yes | 16 |
| `NFR-MNT-02` | Matching and version rules must be unit-testable without launching the UI. | Mandatory | 11, 16, 29 |
| `AC-MAT-04` | Architecture or channel incompatibility prevents an update from being actionable. | Mandatory | 16, 17, 18 |
| `AC-MAT-05` | Version strings that cannot be ordered safely produce an Unknown/Review state rather than a guessed update. | Mandatory | 16, 17, 18 |
| `FR-ID-01` | Maintain a canonical product identity separate from each install instance. | Yes | 11, 17 |
| `FR-ID-02` | Store exact package IDs returned by trusted connectors. | Yes | 17 |
| `FR-ID-03` | Compute a deterministic confidence level and explanation for each mapping. | Yes | 17 |
| `FR-ID-04` | Prevent low-confidence candidates from becoming executable update actions. | Yes | 17 |
| `FR-ID-05` | Allow a user to create, review and delete a manual mapping to a trusted connector package. | Yes | 17 |
| `FR-ID-06` | Preserve mapping origin: automatic, provider-supplied, user-confirmed or imported. | Yes | 17 |
| `FR-ID-07` | Detect channel conflicts such as Stable versus Beta where source metadata permits. | Partial | 17 |
| `AC-MAT-01` | Every actionable update displays a stable package ID, source, candidate version, confidence and explanation. | Mandatory | 17, 18 |
| `AC-MAT-02` | No low-confidence or unmatched candidate exposes an enabled Update button. | Mandatory | 17, 19 |
| `AC-MAT-03` | The curated fixture set produces zero false-positive executable update mappings. | Mandatory | 17, 18 |
| `AC-MAT-07` | The user can create and later delete a manual mapping to a trusted package ID. | Mandatory | 17, 19 |
| `FR-SRC-01` | Support the WinGet default package source through a connector abstraction. | Yes | 18 |
| `FR-SRC-02` | Support Microsoft Store package information available through WinGet. | Yes | 18 |
| `FR-SRC-03` | Refresh source metadata on demand and expose the last successful refresh time. | Yes | 18 |
| `FR-SRC-04` | Cache source results so previous data remains visible during a temporary outage. | Yes | 18 |
| `FR-SRC-05` | Treat source metadata as untrusted and validate field lengths, formats and allowed values. | Yes | 18 |
| `FR-SRC-10` | Let the user add only connector types supported by the product; no raw command connector. | Yes | 18 |
| `FR-UI-01` | Show counts for verified updates, review-required updates, current, unmanaged, pinned, ignored and failed items. | Yes | 18, 19 |
| `FR-UI-04` | Display source provenance, package ID and confidence explanation before an update action. | Yes | 18, 19 |
| `FR-UI-05` | Show last scan time and whether the displayed result is live or cached. | Yes | 13, 18, 19 |
| `FR-UI-06` | Provide a compact “attention needed” view. | Yes | 18, 19 |
| `NFR-PERF-02` | A full online check must complete within 90 seconds at the 95th percentile when sources are healthy; source timeouts must not block the whole application indefinitely. | Mandatory | 18, 29 |
| `NFR-REL-01` | A failed connector must not discard successful results from other connectors. | Mandatory | 18, 29 |
| `NFR-REL-04` | Cached results must always show their timestamp and must not be presented as a live successful check. | Mandatory | 18, 29 |
| `NFR-MNT-01` | Inventory providers and update connectors must implement stable interfaces and fail independently. | Mandatory | 12, 18, 29 |
| `NFR-MNT-03` | Provider commands, argument construction and result parsing must be isolated from presentation code. | Mandatory | 18, 21, 29 |
| `AC-MAT-06` | A temporary source failure leaves the last cached result visible with a stale timestamp and a source-unavailable warning. | Mandatory | 18 |
| `AC-DAT-04` | Network inspection confirms that the MVP contacts only enabled connectors and the optional PC Steward update endpoint. | Mandatory | 18 |
| `AC-NFR-02` | Full online scan meets NFR-PERF-02 when test sources are healthy over at least 20 repeated runs. | Mandatory | 18, 29 |
| `AC-NFR-05` | A connector timeout does not block results from other connectors. | Mandatory | 18, 29 |
| `AC-UX-06` | The UI always distinguishes live, cached and local-only scan results. | Optional — best effort | 13, 18, 19, 29 |
| `FR-POL-01` | Pin a product to its current version, a major/minor branch or a source channel where supported. | Partial MVP: current version/source package | 19 |
| `FR-POL-02` | Ignore an application indefinitely or until a selected date. | Yes | 19 |
| `FR-POL-03` | Record an optional reason for pinning, ignoring or manually mapping. | Yes | 19 |
| `FR-POL-04` | Show all active decisions in one settings view. | Yes | 19 |
| `FR-POL-05` | Make all decisions reversible. | Yes | 19 |
| `FR-UI-02` | Support search, sorting and filtering by status, publisher, source and scope. | Yes | 13, 19 |
| `FR-UI-03` | Show an application detail page with installed and candidate metadata side by side. | Yes | 13, 19 |
| `FR-UI-07` | Provide a detailed diagnostics view without exposing unnecessary implementation noise by default. | Yes | 13, 19 |
| `FR-HIS-02` | Show a timeline for each product. | Yes | 19, 23 |
| `FR-HIS-04` | Apply configurable retention limits to verbose logs and source caches. | Yes | 19, 23 |
| `AC-UX-01` | A first-time user can complete the first scan and identify available updates without using a command line. | Optional — best effort | 13, 19, 29 |
| `AC-UX-02` | All primary navigation, scan, review, update, clear-data and factory-reset actions are keyboard accessible. | Optional — best effort | 13, 14, 19, 26, 28, 29 |
| `AC-UX-03` | Status is expressed through text/icons as well as colour. | Optional — best effort | 13, 19, 29 |
| `AC-UX-04` | Windows text scaling and high-contrast mode do not hide primary controls or status text. | Optional — best effort | 13, 19, 26, 28, 29 |
| `AC-UX-05` | Every failure view contains a plain-language summary and a safe next action. | Optional — best effort | 19, 23, 26, 29 |
