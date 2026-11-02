# Task 50 — host-evidence result and interpretation (O5, A-Q1, A2-L3)

Requirements: `FR-UPD-15`, `FR-UPD-01`, `TM-03`, `TM-04`, `TM-07`, `TM-13`.

The owner ran the approved **Default** steps on 2026-11-01 under [plan revision 2.2](TASK_50_HOST_EVIDENCE_PLAN.md) §10, as recorded there (approval record):
- H1 printed the hash and `HASH-OK`;
- H1b gave `PARSE-OK`;
- H2 ran once;
- H5: the evidence was shared;
- H6 printed `CLEANUP-COMPLETE: <TEMP>\pcsteward-evidence-task50 removed`.

H3 and H4 were not approved and were not run. The agent ran nothing on the host. The raw evidence is [`TASK_50_HOST_EVIDENCE_RESULT.json`](TASK_50_HOST_EVIDENCE_RESULT.json), the text of the owner's console paste of `Get-Content -Raw` on the run's `evidence.json`.
- The paste includes the 5.1 `\u003c`/`\u003e` escapes. A first commit (`7671d98`) had decoded them by mistake, and they are now restored to match the paste.
- Console copying may still change whitespace. The committed text is semantically identical to the run file, but it is not the hashed artifact. `scriptSha256` in the evidence equals the plan's Final SHA-256 `0A1C4377…84D4`.

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
| **O5** | **PASS-READABLE** | O5 native evidence, the **spec O5** (fact extension §9 O5 / §10 N5: the `ChocolateyClientStateData` inputs of the file-first postcheck), is satisfied **for this host and the current Chocolatey state**. It is subject to the plan §1 binding rule: it binds the desktop file-first reader only within the probed file set and access shape. **The O5 owner contingency is not triggered.** It does **not** show that the desktop role can complete design rev 6 §3 steps 1–3 and 5–9 (see "Still open"). Probed set, all read with 0 problems, 0 unreadable and 0 reparse: the root engine; all 56 release-tree entries (15,243,398 bytes); `helpers` (1 directory, 41 files, none unlisted) and `tools` (13 files, none unlisted) enumerated; `extensions` and `hooks` absent; `lib` enumerated (1 directory, 1 nupkg, readable); `chocolatey` registration present; `chocolatey.extension` absent; no store directory. Because `.chocolatey`, `extensions` and `hooks` were absent, those parts of the probed set were never exercised. The probe read files only; it never ran Chocolatey. |
| **A-Q1** | AQ1N, AQ1M, AQ1C all **PASS** | The A.4 step-3 hold design is **viable for direct, non-elevated process creation** of a held **main image** with the native (suspended creation), .NET Framework and coreclr loaders. In every held case: the launch succeeded; FileIdInfo was stable before and after; and DELETE/WRITE_DATA replacement attempts on held files returned **32 (sharing violation)** before launch, during the run and after exit. The DELETE control after release succeeded on the first attempt.<br>Details that limit the claim:<br>• AQ1N and AQ1M checked the peer identity (equal to the held identity). AQ1C has no peer check, by design.<br>• AQ1N's "during the run" probe ran while the process was still suspended (before `ResumeThread`).<br>• A held **lazily loaded DLL** (`aq1-lazy.dll`) was exercised only through the **managed** `Assembly.LoadFrom` path (AQ1M, AQ1C), and its probes ran before that load.<br>The A2-L5 fallback is **not triggered by this evidence**. It stays contingent on AQ1UAC (runas is the helper's only launch path; AQ1UAC was not run) or on the ADR-025 exact-artifact test. **Residuals for the real-client or exact-artifact test:** native `LoadLibrary` of a held DLL (for example `7z.exe` loading held `tools/7z.dll`), PowerShell reading or importing held `helpers` files, and an elevated launcher holding the files. These matter because `ChocolateyHeldClientBinding` has no fallback (design rev 6 §9). |
| **A2-L3** | A2L3S **PASS** | On `<SYSTEM32>\whoami.exe` and `<SYSTEM32>`, every write-class probe returned **5 (ACCESS_DENIED)** both without and **with** the share-read hold. The hold does not turn a protection result into a sharing violation, so `TryHold` followed by `validatePreLaunchTrust` (A.4 steps 3.1–3.2) is acceptable. The positive control behaved as expected: the run's own file was modifiable unheld, and under the hold the data/delete probes returned 32, giving fail-closed `probe-failed-closed:32`. **Analogue evidence only.** Ownership differs: System32 files are owned by TrustedInstaller, while the deployment is Administrators-owned. Per plan §9, the **ADR-025 exact-artifact test remains required**. A2L3P (installed PC Steward files, H4, not run) would be optional extra evidence. |

Observation for the implementation record: under a `FileShare.Read` hold, the metadata-class probes (`FILE_WRITE_EA` 0x10, `FILE_WRITE_ATTRIBUTES` 0x100, `WRITE_DAC`, `WRITE_OWNER`) still **opened** on the caller-writable file. Share modes govern data and delete access only. Protection against metadata or DACL changes therefore rests on the ACL (requester-unmodifiable), which the native observer's `AccessCheck` evaluates (design rev 6 §2.2), not on the hold. This matches the design; it records why the hold is not an ACL substitute.

## Eligibility finding: the installed Chocolatey is not the pinned release

The probe compared the installed client with the compiled release pins (Chocolatey 2.7.5; `ChocolateyClientReleasePolicy`, release manifest JSON). They do not match:

| Item | Pinned 2.7.5 | Observed |
|---|---|---|
| Root engine `choco.exe` | 12,022,880 B, `4AB5785C…61C0` | 12,013,664 B, `9C858D0B687607FF5C06601469A69E642E7B126FCC9AD3F94E58FD85694C44F5` |
| Release tree | 56 entries | 56 present and readable; **50 match**. Mismatches: `choco.exe`, `helpers/Chocolatey.PowerShell.dll`, `tools/7z.dll`, `tools/7z.exe`, `tools/checksum.exe`, `tools/shimgen.exe` |
| `lib/chocolatey/chocolatey.nupkg` | 2.7.5 asset 5,719,618 B, `F86F99A1…5839` | 5,713,666 B, `AEC2DBD6CE91B7378EB4A0C21524E748D30332EA856E898404999F77328BD504` |

`matchesPinnedRelease = false` and `routeFindings` is empty. Per plan §9, Profile V2 ("changed content refuses") and design rev 6 §3 step 2, **the route would refuse on this host** with `MachineInputsUnsupported`, which is an eligibility fact.

Other eligibility conditions were **not probed**:
- the helper HTTP cache (step 4);
- loader and licensing state (step 5);
- config features and `cacheLocation` (step 6).

Also, `registration.directories = 1`: `lib` holds only `chocolatey`, and there is no store. So this host currently has **no installed package the route could update**. This is safe behaviour: nothing is updated by an unreviewed client. No Chocolatey change is authorized or proposed here. The installed version is not identified by this evidence.

Owner choices, needed only before route admission and not now:
1. The owner changes the installed Chocolatey to exactly the pinned 2.7.5. This is an owner action outside PC Steward.
2. The pin moves to the installed release through a **Profile V2 revision with independent review**. That revision must:
   - identify the release;
   - pin its published asset and the release-tree manifest;
   - re-pin the reviewed client source commit and the NuGet fork;
   - update the engine file-version and signer check (design rev 6 §3 step 2, currently `2.7.5.0`), `ChocolateyClientReleasePolicy` and the compiled manifest table;
   - re-check the Profile V2 source-level behaviours (refresh guard, fixed arguments and registered options, loader/licensing, store layout) against that release's pinned sources.

## What this changes

- **O5 (spec scope):** closed for this host and state; the contingency is not triggered. The desktop file-first **postcheck inputs** are readable here.
- **A-Q1:** the hold is viable for held main images under direct non-elevated creation (native, .NET Framework, coreclr), with managed lazy loads. The fallback is not triggered, but stays contingent on runas evidence.
- **A2-L3:** closed as analogue evidence; the exact-artifact test remains required.

**Still open before admission:**
- the Chocolatey release eligibility above (owner choice 1 or 2);
- the other unprobed eligibility facts (HTTP cache, loader/licensing, config) and a target package on the host;
- desktop-role reads outside the spec O5 set: `config\chocolatey.config`, machine.config, the GAC, and `bin`/`logs`/`license` coverage (design rev 6 §3 steps 1–3, 5–9, §7);
- readability of files written by a future elevated upgrade (plan §1, §12);
- runas: AQ1UAC, or the exact-artifact test standing in for it;
- the A-Q1 residuals above (native held DLL loads, PowerShell helper reads, elevated launcher);
- the ADR-025 exact-artifact test: signed helper and desktop, linked-token match, `AccessCheck` on the real tree, and A2-L3 on the real deployment;
- the separately approved v2 redeploy;
- implementation slices N1–N6 with review and CI.

## Review record

An independent review (Opus, pinned `7671d98`) returned **CONDITIONAL** with no Blocker or High. It confirmed:
- every verdict against the raw fields and the script logic;
- the decision rows against the plan §9 PASS rows;
- the metadata-class observation;
- all eligibility numbers;
- that the JSON raises no privacy concern.

Its findings are applied above:
- three Medium overclaims: the fallback is "not triggered" rather than "not needed"; the lazy-load and loader scope is narrowed; O5 is held to the spec scope;
- Lows: corrected A-Q1 details, A2-L3 exact-artifact wording, re-pin scope, unprobed eligibility facts and no target package, the complete open list, and the provenance and escape restoration.

## Re-run after alignment to 2.7.5 (2026-11-02, approval record 2)

[Raw evidence](TASK_50_HOST_EVIDENCE_RESULT_2.json). Same script (SHA-256 `0A1C4377…84D4`), Default cases, no stop, 0 redactions.

- **Verdicts:** O5 **PASS-READABLE**; AQ1N, AQ1M, AQ1C **PASS**; A2L3S **PASS**. These confirm the earlier run on the aligned install.
- **The release now matches the pin**, except for one extra file:
  - engine `choco.exe` 12,022,880 B, SHA-256 `4AB5785C…61C0` = the 2.7.5 pin;
  - code list 56/56 matched;
  - `lib/chocolatey/chocolatey.nupkg` 5,719,618 B, `F86F99A1…5839` = the 2.7.5 public asset;
  - tools tree: 13 files, 0 unlisted.
- **Remaining mismatch:** the `helpers` tree has **42 files against 41 listed**; exactly one unlisted file. The probe's `matchesPinnedRelease` is therefore `false`, and observer step 2 would refuse ("an extra helpers file refuses"). The likeliest cause is a stale helper left by the in-place upgrade from the earlier release; this is a hypothesis until the file is named.
  - **Next:** the owner identifies the file with a read-only listing. Then either the owner removes it (an owner host action; for example a Chocolatey repair/reinstall of 2.7.5, or deleting the stale file), or, if it is part of a legitimate 2.7.5 install path, the reviewed manifest decision is revisited. Fail closed until then.
- **Split token present:** `tokenElevationType` = 3 (Limited) and not elevated. The desktop meets the L6 split-token availability constraint on this host.
- **Still open before admission:**
  - the extra helper file;
  - a Chocolatey-managed target package (the store has only the client);
  - AQ1UAC/runas (H3, not approved);
  - the ADR-025 exact-artifact test;
  - the v2 redeploy;
  - the proxy and client-certificate disclosure decision;
  - owner approval.
