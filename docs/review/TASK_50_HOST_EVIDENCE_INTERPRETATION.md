# Task 50 — host-evidence result and interpretation (O5, A-Q1, A2-L3)

Requirements: `FR-UPD-15`, `FR-UPD-01`, `TM-03`, `TM-04`, `TM-07`, `TM-13`.

The owner ran the approved **Default** steps on 2026-11-01 under [plan revision 2.2](TASK_50_HOST_EVIDENCE_PLAN.md) §10, as recorded there (approval record):
- H1 printed the hash and `HASH-OK`;
- H1b gave `PARSE-OK`;
- H2 ran once;
- H5: the evidence was shared;
- H6 printed `CLEANUP-COMPLETE: <TEMP>\pcsteward-evidence-task50 removed`.

H3 and H4 were not approved and were not run. The agent ran nothing on the host. The raw evidence is [`TASK_50_HOST_EVIDENCE_RESULT.json`](TASK_50_HOST_EVIDENCE_RESULT.json), the owner's console paste of `Get-Content -Raw` on the run's `evidence.json`. It is semantically identical to the file, but the bytes may differ: the paste is not the hashed artifact. `scriptSha256` in the evidence equals the plan's Final SHA-256 `0A1C4377…84D4`.

## Host facts

| Fact | Value | Meaning |
|---|---|---|
| OS | 10.0.26300.0 | Windows 11 (a build newer than 26100) |
| PowerShell / CLR | 5.1.26100.9711 / 4.0.30319.42000 | The intended Windows PowerShell 5.1 host |
| `tokenElevationType` / `tokenElevated` | 3 (Limited) / 0 | A non-elevated **filtered token of a UAC split admin**: exactly the principal the route requires (design rev 6 §2.1) |
| Evidence volume = Program Files volume; file system | true; NTFS | The FileIdInfo semantics apply |

## Verdicts and spec decisions (plan §9)

| Case | Verdict | Decision |
|---|---|---|
| **O5** | **PASS-READABLE** | O5 native evidence is satisfied **for this host and the current Chocolatey state** (fact extension §10 N5), subject to the plan §1 binding rule: it binds the desktop file-first reader only within the probed file set and access shape. **The O5 owner contingency is not triggered.** Probed set, all read with 0 problems, 0 unreadable and 0 reparse: the root engine; all 56 release-tree entries (15,243,398 bytes); `helpers` (1 directory, 41 files, none unlisted) and `tools` (13 files, none unlisted) enumerated; `extensions` and `hooks` absent; `lib` enumerated (1 directory, 1 nupkg, readable); `chocolatey` registration present; `chocolatey.extension` absent; no store directory. |
| **A-Q1** | AQ1N, AQ1M, AQ1C all **PASS** | The A.4 step-3 hold design is **viable for direct process creation** with the native (suspended creation), .NET Framework and coreclr loaders. In every held case: the launch succeeded; the peer image identity equalled the held identity; FileIdInfo was stable before and after; and DELETE/WRITE_DATA replacement attempts returned **32 (sharing violation)** before launch, while running and after exit, including the lazily loaded `aq1-lazy.dll`. The DELETE control after release succeeded on the first attempt. The A2-L5 fallback is **not needed** on this evidence. This also supports the design rev 6 `ChocolateyHeldClientBinding`, which has no fallback. runas remains an inference (AQ1UAC not run). Admission still needs the ADR-025 exact-artifact host test. |
| **A2-L3** | A2L3S **PASS** | On `<SYSTEM32>\whoami.exe` and `<SYSTEM32>`, every write-class probe returned **5 (ACCESS_DENIED)** both without and **with** the share-read hold. The hold does not turn a protection result into a sharing violation, so `TryHold` followed by `validatePreLaunchTrust` (A.4 steps 3.1–3.2) is acceptable. The positive control behaved as expected: the run's own file was modifiable unheld, and under the hold the data/delete probes returned 32, giving fail-closed `probe-failed-closed:32`. **Analogue evidence only.** A2L3P (installed PC Steward files, H4) was not run, so the owner decides whether the analogue suffices or an exact-artifact check is required. |

Observation for the implementation record: under a `FileShare.Read` hold, the metadata-class probes (`FILE_WRITE_EA` 0x10, `FILE_WRITE_ATTRIBUTES` 0x100, `WRITE_DAC`, `WRITE_OWNER`) still **opened** on the caller-writable file. Share modes govern data and delete access only. Protection against metadata or DACL changes therefore rests on the ACL (requester-unmodifiable), which the native observer's `AccessCheck` evaluates (design rev 6 §2.2), not on the hold. This matches the design; it records why the hold is not an ACL substitute.

## Eligibility finding: the installed Chocolatey is not the pinned release

The probe compared the installed client with the compiled release pins (Chocolatey 2.7.5; `ChocolateyClientReleasePolicy`, release manifest JSON). They do not match:

| Item | Pinned 2.7.5 | Observed |
|---|---|---|
| Root engine `choco.exe` | 12,022,880 B, `4AB5785C…61C0` | 12,013,664 B, `9C858D0B687607FF5C06601469A69E642E7B126FCC9AD3F94E58FD85694C44F5` |
| Release tree | 56 entries | 56 present and readable; **50 match**. Mismatches: `choco.exe`, `helpers/Chocolatey.PowerShell.dll`, `tools/7z.dll`, `tools/7z.exe`, `tools/checksum.exe`, `tools/shimgen.exe` |
| `lib/chocolatey/chocolatey.nupkg` | 2.7.5 asset 5,719,618 B, `F86F99A1…5839` | 5,713,666 B, `AEC2DBD6CE91B7378EB4A0C21524E748D30332EA856E898404999F77328BD504` |

`matchesPinnedRelease = false` and `routeFindings` is empty. Per plan §9, **the route would refuse on this host** with `MachineInputsUnsupported`, which is an eligibility fact. This is safe behaviour: nothing is updated by an unreviewed client. No Chocolatey change is authorized or proposed here. The installed version is not identified by this evidence.

Owner choices, needed only before route admission and not now:
1. The owner changes the installed Chocolatey to exactly the pinned 2.7.5. This is an owner action outside PC Steward.
2. The pin moves to the installed release after a new review: identify the release, pin its published asset and release-tree manifest, and re-check the Profile V2 source-level behaviours (refresh guard, fixed arguments, loader/licensing, store layout) against that release's pinned sources.

## What this changes

- **O5:** closed for this host and state; the contingency is not triggered. The desktop file-first verifier (design rev 6 §7) is viable here.
- **A-Q1:** the hold path is viable for synthetic images (all three loaders); the fallback is not required. Still required: the runas inference and the exact signed artifact (ADR-025).
- **A2-L3:** closed as analogue evidence. The A2L3P exact check needs separate approval if the owner wants it.
- **Admission prerequisites still open:**
  - the Chocolatey release eligibility above;
  - the ADR-025 exact-artifact test (signed helper and desktop, linked-token match, `AccessCheck` on the real tree);
  - the separately approved v2 redeploy;
  - implementation slices N1–N6 with review and CI.

Independent review of this interpretation: pending.
