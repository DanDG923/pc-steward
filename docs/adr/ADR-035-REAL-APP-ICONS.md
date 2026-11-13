# ADR-035 — Real app icons in the lists

**Status:** Accepted. The owner chose option A on 2026-11-13 ("Icons first, one release"). Built as described under "As built".
**Date:** 2026-11-13
**Decision owners:** Product owner and implementation reviewer
**Related requirements:** `FR-INV-01–04` (the entries the scan reads, unchanged), `FR-DAT-12` (no new stored data), `AC-UX-01` (best effort); [ADR-030](ADR-030-DESKTOP-UI-UX-OVERHAUL-DIRECTION.md) (desktop UI direction); UI review 2026-11-13, item 20

## Context

Every app in PC Steward's lists is a grey tile with two letters ("DI", "MW", "72" for 7-Zip). The UI review found that real icons would help the owner find an app faster than any other single change, and would make the lists look like Windows.

An icon has to come from somewhere PC Steward does not own:

- A **Store-type app** names its logo in its own package manifest. The logo is an image file in the package's install folder, which Windows protects from change by ordinary programs.
- A **classic app** names its icon in the `DisplayIcon` value of its uninstall entry in the registry, as a file path with an optional icon number (for example `C:\Program Files\App\app.exe,0`). The installer wrote that value. The scan reads that entry today, but only five named values, and `DisplayIcon` is not one of them.

So showing icons means reading one more registry value and opening files that other software placed. Both are local and read-only. The questions this record answers are which files PC Steward may open, how, and what it keeps.

## Decision drivers

- Nothing an installer wrote may make PC Steward run code, reach the network or write a file.
- The elevated helper and the scheduled check must not be involved at all.
- No new stored data, so the three data actions (clear scan data, clear downloaded files, factory reset) keep their meaning.
- A missing or refused icon must be invisible: the two-letter tile stays.
- It must be checkable without the owner's PC: rules as portable, unit-tested functions, and a count in the Windows launch smoke test.

## What could go wrong

1. **A path that points off this PC.** `DisplayIcon` can be a network path (`\\server\share\icon.ico`). Opening it would make Windows connect to that server and offer the owner's sign-in to it. This is the one risk here that crosses a real boundary, because data would leave the PC.
2. **Loading someone else's code.** Asking the Windows shell for "the icon of this file" can load icon handlers that other programs installed into PC Steward's process. Loading an `.exe` or `.dll` the ordinary way would run its start-up code.
3. **A damaged or hostile icon.** The file's icon data is read by Windows' own icon code, the same code Explorer and Settings use for the same entries. PC Steward runs it without administrator rights.
4. **Slow or huge files.** A list of 300 apps must not wait on 300 file reads, and a very large file must not be read whole.

A program that can already write the owner's registry or files as the owner can do far more than change an icon, so risks 3 and 4 are low priority under the owner's stated security approach. Risk 1 and risk 2 are designed out below.

## Options considered

### Option A — Store logos and classic app icons, under the rules below (recommended)

Every app that names a usable icon gets it. This is the option that delivers what the review asked for, since most apps on a typical PC are classic apps. It needs the most care, which the rules below provide.

### Option B — Store-type apps only

Only packaged apps get their logo; classic apps keep the two-letter tile. It opens no file outside Windows' protected package folders and reads no new registry value. It is the smallest change, but on the owner's PC it would leave most rows as they are, and a list with a few icons among many tiles looks unfinished.

### Option C — Keep the two-letter tiles

No new file or registry reads. The lists stay as they are.

## Decision (Option A)

PC Steward shows an app's own icon in its lists when, and only when, all of these hold. Otherwise it shows the two-letter tile.

1. **Where.** Icons are read only by the desktop app, without administrator rights, for rows that are on screen. The elevated helper, the scheduled check and the scan never read an icon or `DisplayIcon`.
2. **Which entry.** For a classic app, `DisplayIcon` is read from the same uninstall entry the scan recorded for that app, and from nowhere else. For a Store-type app, the logo is the one Windows' package API returns for that package.
3. **Which paths.** The path must be a full path on a fixed local drive that goes through no link (see "As built"). Refused: network paths, mapped network drives, removable drives, device paths (`\\?\`, `\\.\`), a path with a stream name, a relative path, and any value that contains an environment variable. Classic app files must end in `.ico`, `.exe` or `.dll`.
4. **How.** The file is opened for reading as data. It is never run and never loaded as code. One icon image is taken out of the file's bytes (see "As built"); PC Steward does not ask the shell for a file's icon, so no other program's icon handler is loaded.
5. **How much.** One small icon (32 units, at the display's scale) per app. Reading happens off the UI thread, a few at a time, and each read is abandoned if it is slow.
6. **What is kept.** Icons are kept in memory for the session only, with an upper limit. Nothing is written to PC Steward's data folder and the `DisplayIcon` text is not stored. Nothing is sent anywhere.
7. **When it fails.** Any refusal or error shows the two-letter tile and is not reported to the owner. The app's name is still the row's accessible name; an icon is decoration and is never the only way to tell rows apart.

## As built (2026-11-13)

Two rules were built more strictly than first written. Neither widens what is read.

- **Rule 1, "on screen" is measured.** A tile's icon is read only when some part of the tile is inside what the window shows. A row further down a page, a row in a closed group and a row the list has built ahead for scrolling read nothing until they come into view. An icon already read this session is shown at once, with no read.
- **Rule 3, links are refused, not resolved.** Before the file is opened, every folder on the way to it and the file itself are checked. A junction, a symbolic link, a cloud placeholder or any other reparse point at any step means no icon, so an app installed through a link keeps its tile.
- **Rule 3, what the check does not cover (accepted residual, raised by the independent review).** The check and the open are two steps. A program already running as the owner, and able to create links, could swap a folder for a link in between, and the open would follow it, to another PC if the link points there. That program can already open the same place itself as the same person, so nothing is gained by going through PC Steward: no boundary is crossed. This is the case this record's "What could go wrong" section sets aside under the owner's stated security approach. Closing it fully would mean holding every folder on the path open against renaming while the icon is read, which could make another program's installer fail in that moment; that cost is not worth paying for a case with no gain. Reconsider if the icon reader ever runs with more rights than the owner, which rule 1 forbids.
- **Rule 4, no Windows icon-resource function is called on the file.** A small reader in `PCSteward.Application` (`AppIconFile`) finds one icon image in the bytes of an `.ico`, or in the resource table of an `.exe` or `.dll`, with every offset and size checked against the file's length and fixed limits on how much is read. It hands that one image (at most 1 MB) to Windows' image decoder as an in-memory icon. The file is never mapped or loaded, so risk 2 does not arise, and the reader is portable code with its own tests, including truncated and overwritten files. Risk 3 remains for the one image the decoder is given, unelevated, as the record already accepted.
- **Rule 5, a slow read.** A read that takes longer than two seconds is not waited for and the row keeps its letters. The read keeps its place among the four until it really ends, so stalled reads can never number more than four and a stalled read is not started a second time. If its icon arrives late it is remembered, and shown the next time the row is.
- **Where the code is.** The rules and the reader are `AppIconRules` and `AppIconFile` (portable). The registry and file reads are `AppIconLoader` in the desktop app, used only by `MainWindow.AppIcons.cs`. The scan's list of registry values is unchanged.

## Consequences

### Positive

- Lists are faster to scan and look native.
- No new stored data and no change to the scan, the helper or the data actions.
- The path rules are a portable function with unit tests, including every refused form in rule 3.

### Negative / trade-offs

- The desktop app reads one more registry value and opens files it did not before. This must be said in the privacy text on About.
- The registry, file and image-decoding calls are Windows-only code that cannot be compiled or run off Windows; they are checked by Windows CI and the launch smoke test, then on the owner's PC. The path rules and the reader are portable and unit-tested.
- Some apps name no icon, or one that is refused, and keep the tile.

### Follow-up controls and tests

- Unit tests for the path rules (rule 3) and for parsing `DisplayIcon` (quotes, icon number, refusals).
- A source contract that the icon reader is used only by the desktop lists, and that the helper, scheduled host and inventory projects do not reference it.
- The launch smoke test logs how many listed apps showed an icon and that the app stayed running.
- A line in About's privacy text.

## Reconsider when

- Windows changes how packaged app logos are exposed, or the image decoder in rule 4 is found to load third-party code.
- The owner wants icons kept between sessions, which would add stored data and needs its own decision.
