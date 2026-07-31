# PC Steward design handoff

This folder is the implementation-ready product-design package for the native PC Steward Windows desktop application. The sole production UI target is **WinUI 3 on the Windows App SDK for Windows 11 x64**; no website, embedded WebView or browser UI is part of the product. The product owner accepted on 2026-07-30 that unused transitive WinUI/WebView2 runtime support files may ship with the Windows App SDK payload; this does not authorize embedded browser features. This handoff is additive: no production application source was changed.

`design/native-prototype/` is the authoritative interactive design prototype. It is a separate, unpackaged WinUI 3 desktop application used to review hierarchy, interaction and native window behaviour without inspecting or changing the computer.

## Design direction

**Quiet authority** combines Windows 11 familiarity with explicit evidence, local-first privacy and conservative action design. The interface avoids health scores, urgency theatre and indiscriminate “clean everything” controls. Status, confidence, freshness and consequences are always expressed in words as well as colour.

## Package map

| File | Purpose |
|---|---|
| `DESIGN_SYSTEM.md` | Brand, themes, tokens, typography, layout, iconography, motion and Windows scaling rules |
| `UX_SPECIFICATION.md` | Users, risks, information architecture, journeys, states, interaction and content rules |
| `SCREEN_INVENTORY.md` | Complete native destination, screen, panel, dialog and state inventory |
| `COMPONENT_SPECIFICATION.md` | Reusable component anatomy, variants, behaviour and WinUI implementation notes |
| `ACCESSIBILITY_REVIEW.md` | WCAG 2.2 AA review, keyboard model, screen-reader guidance and test matrix |
| `CODEX_IMPLEMENTATION_BRIEF.md` | Small implementation phases, acceptance criteria, files, checks and tests |
| `design-tokens.json` | Machine-readable global, light, dark and high-contrast token definitions |
| `assets/svg/` | Original commercial-use vector concepts, selected identity and illustrations |
| `assets/png/` | Raster exports for Windows packaging and review |
| `native-prototype/` | Authoritative interactive WinUI 3 design prototype and validation instructions |
| `prototype/` | Superseded browser-rendered visual reference; archival only and not an implementation base |

## Native prototype review

From the repository root, run the authoritative native prototype:

```powershell
dotnet run --project design/native-prototype/PCSteward.NativePrototype.csproj
```

Use its navigation pane and **Prototype state** selector to inspect ready, first-run, scanning, complete, no-findings, partial-failure and offline states. The native prototype is illustrative only and never reads, scans, changes or deletes anything on the computer. Production implementation belongs in `src/PCSteward.Desktop` and must follow the phased implementation brief.

The older browser-rendered artifact in `design/prototype/` is retained only to preserve historical visual exploration. It is superseded, must not be published as PC Steward, and must not be used as the product architecture or implementation source.

## Scope note

The authoritative product specification defines PC Steward as a local software inventory and trusted update-orchestration tool. Startup review, general storage analysis, configuration concerns, “unwanted software” classification and broad cleanup are outside the approved MVP. They are designed here as clearly labelled future/read-only concepts so the requested information architecture is complete, but `CODEX_IMPLEMENTATION_BRIEF.md` explicitly prevents them from entering production until the product specification, threat model and acceptance criteria are amended.
