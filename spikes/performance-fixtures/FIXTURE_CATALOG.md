# Deterministic provider fixture catalog

`New-PerformanceFixtures.ps1` generates `fixtures/provider-records.json` deterministically. The checked-in output contains 320 safe, fictional provider records and five expected mapping controls; it contains no installed-software inventory or remote metadata.

| Fixture group | Count | Expected handling |
|---|---:|---|
| Normal applications | 288 | Stable, user/machine and x86/x64 combinations; deterministic exact-match controls. |
| Runtime/component records | 12+ | Retained as inventory records; mapping policy is separately testable. |
| Malformed/missing fields | 5 | Warning or unknown fields; never a scan crash. |
| Duplicate raw records | 2 | Same raw install instance; expected conservative deduplication. |
| User/machine split | 2 | Same family, separately inspectable scopes. |
| x86/x64 split | 2 | Same family, separately inspectable architectures. |
| Prerelease/channel conflict | 1 | Recorded but not actionable across channel policy. |
| Incomparable/blank version | 3 | Review/unknown, never guessed ordering. |
| False-match and unmatched controls | 3 | Explicitly non-actionable; zero executable false positives is the future acceptance expectation. |
| Hostile/oversized data | 2 | Representation or warning only; never command input. |

The named edge fixtures are `EDGE-001` through `EDGE-020`. They cover malformed name and architecture, missing and blank versions, a raw duplicate pair, user/machine and x86/x64 pairs, runtime, prerelease, incomparable version, a deliberate false match, Store/AppX-like scope, quoted/metacharacter text, empty publisher, architecture conflict, unmatched application and oversized field behavior.

Mapping controls retain their expected actionability in the JSON: one exact positive control and four negative controls for false match, channel conflict, incomparable version and architecture conflict. They are fixture assertions, not an implementation of matching policy.

Relevant requirements: `AC-INV-01`, `AC-INV-02`, `AC-INV-04`, `AC-MAT-03`, `AC-MAT-04`, `AC-MAT-05`, `NFR-MNT-02`, `NFR-REL-05`.
