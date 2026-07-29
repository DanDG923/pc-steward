# Asset notes

All assets in this folder were created specifically for PC Steward. They use only geometric vector construction, system typography and the colour tokens in `design-tokens.json`; no third-party brand asset or stock icon was copied.

## Selected direction

`svg/logo-mark.svg` is the preferred mark. The enclosing open loop represents stewardship without implying antivirus protection. The three internal tiles represent inventory, evidence and user-approved action. The opening communicates that PC Steward is transparent and user-controlled rather than a sealed “magic” optimiser.

`svg/logo-lockup.svg` pairs the mark with the working display name. `PCSteward` remains the stable technical identifier per ADR-001; display branding can change without altering package or data identities.

## Alternative concepts

- `svg/concept-a-steward-loop.svg` — selected; calm, distinct at small sizes.
- `svg/concept-b-ledger-lens.svg` — stronger analysis metaphor, slightly more technical.
- `svg/concept-c-guided-orbit.svg` — softer maintenance metaphor, less legible below 24 px.

## Illustration rules

- Use illustrations only in onboarding, true empty states and offline/permission education.
- Never place an illustration above an urgent operational failure.
- Preserve the original `viewBox`; do not stretch.
- Decorative illustrations receive an empty accessible name. Informative diagrams receive a concise adjacent text alternative.
- App icons should be exported from the selected square source at 16, 24, 32, 48, 64, 128 and 256 px, then inspected pixel-by-pixel.

## PNG exports and share card

- `png/logo-mark-{16,24,32,48,64,128,256,512}.png` are deterministic exports of the selected vector mark.
- `ico/pc-steward.ico` is the multi-resolution Windows executable and title-bar icon, containing 16, 20, 24, 32, 40, 48, 64, 128 and 256 px frames.
- `png/pc-steward-social-card.png` is the 1200 × 630 review-card master also used by the prototype as `public/og.png`.
- `export_png_assets.py` is the repeatable PNG, Windows icon and share-card export/composition utility.

The share-card background was created with the built-in image-generation tool, then combined with exact system-rendered typography and the deterministic mark. Final prompt:

> Use case: ads-marketing. Asset type: landscape social preview background for the PC Steward product-design prototype. Create an original, premium abstract technology composition that communicates calm stewardship, evidence, and local control for a Windows 11 desktop utility. Use a deep midnight-blue to cobalt field with restrained soft light and frosted-glass planes. On the right half, place a precise looping path of translucent blue ribbons around a warm amber evidence point with a few orderly connected record nodes. Preserve the left 45 percent as quiet negative space. Calm, trustworthy and quietly authoritative. No text, letters, logos, UI screenshots, people, hardware, shield, checkmark badge, broom, gear, antivirus imagery or watermark.
