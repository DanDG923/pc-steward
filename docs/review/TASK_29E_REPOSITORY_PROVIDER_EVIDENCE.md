# Task 29E — Repository evidence, provider resilience, and readable freshness

## Outcome

Repository checks now make two bounded, read-only calls per enabled source: the source-managed installed-package list and its available updates. The result merges them by exact package ID:

- a managed installed package with no newer version is recorded with its installed version as the available version, producing explicit `Current` evidence;
- a managed installed package with a newer version keeps that newer source version;
- untrusted, local-only, unavailable, and candidate-only rows are not promoted to repository-managed status.

WinGet may print a source warning before its table. The parser now finds the actual table delimiter rather than assuming the first line is the header. The AppX catalog no longer accesses protected package installation paths, skips inaccessible metadata, and returns a bounded `msix:catalog-unavailable` warning rather than faulting the whole scan. Local completion and source timestamps now use a readable local date and time.

## Recorded-host evidence

- `winget source list` shows the default Community `winget` source and the registered `msstore` source.
- The machine-trusted, Microsoft-signed App Installer `winget.exe` resolves successfully.
- The bounded app process captured a valid WinGet table preceded by `Failed when searching source; results will not be included: msstore`; this was the parser defect.
- The Microsoft Store source is currently unavailable on this host. No Store-managed/current claim is made while that source cannot answer.

## Verification

- Focused test coverage includes source-warning table parsing, current/update source merging, display-only authority boundaries, AppX-resilient presentation, and readable freshness.
- Release build: 0 warnings, 0 errors.
- Full suite: 196 passed.

## Residual verification

A live refresh was not performed by the agent because it sends installed package identifiers to the enabled third-party source under the disclosure. The user should launch the new Release build and select **Refresh connected repositories** to establish fresh local evidence. The `msstore` source will remain visibly unavailable until its Windows/Store connectivity issue is resolved. `PO-EXC-29-PERF-01` is unchanged and is not a pass claim.
