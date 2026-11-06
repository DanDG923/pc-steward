# PC Steward design handoff

This folder is the implementation-ready product-design package for the native PC Steward Windows desktop application. The sole production UI target is **WinUI 3 on the Windows App SDK for Windows 11 x64**; no website, embedded WebView or browser UI is part of the product. The product owner accepted on 2026-07-30 that unused transitive WinUI/WebView2 runtime support files may ship with the Windows App SDK payload; this does not authorize embedded browser features. This handoff is additive: no production application source was changed.

The interactive design prototypes (`design/native-prototype/`, a WinUI 3 app, and `design/prototype/`, an older browser-rendered visual reference) were removed on 2026-11-06 at the owner's request. The production desktop app in `src/PCSteward.Desktop` is now the reference for interaction and native window behaviour. Both prototypes can still be read in git history at commit `ee4fadb`. Older documents that mention them describe that history.

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

## Scope note

The authoritative product specification defines PC Steward as a local software inventory and trusted update-orchestration tool. Startup review, general storage analysis, configuration concerns, “unwanted software” classification and broad cleanup are outside the approved MVP. They are designed here as clearly labelled future/read-only concepts so the requested information architecture is complete, but `CODEX_IMPLEMENTATION_BRIEF.md` explicitly prevents them from entering production until the product specification, threat model and acceptance criteria are amended.
