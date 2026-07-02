# App icon design — "Knurled Tuner"

**Date:** 2026-06-26
**Status:** Approved, implemented

> **Historical record.** This spec dates from the web/MAUI era; the `src/` and
> `packaging/` paths it references were retired with those heads and now live
> in git history (`v0.2.1`). The icon it defines survives as the native app's
> `native/Nugsdotnet.Native/Assets/nugsdotnet.ico` and the repo banner art.

## Goal

Replace the stock MAUI template art (a `#512BD4` purple square + white ".NET"
wordmark) with a branded app icon that belongs to nugsdotnet's established
visual identity: the **1970s hi-fi RECEIVER ('74) cabinet** theme defined in
`src/Nugsdotnet.UI/wwwroot/css/app.css` (warm near-black walnut, amber VFD glow,
bone-white legend, bronze hairlines).

## Concept

A vintage receiver **tuning dial**: a knurled brass tuning knob on a dark
faceplate, ringed by a warm amber glow, with an amber pointer at 12 o'clock
aligned to a lit "tuned" tick on a crowning scale. Chosen by the user from four
motif sketches (VU meter, VFD lettermark, tuning dial, spectrum bars).

## Palette (from app.css)

- Cabinet / faceplate: `#15120D`, `#100b05`, deep `#0a0907`
- Bone-white legend / highlights: `#efe4cf`
- **Amber VFD glow (signature lit color): `#ffb22e`**, hot ember `#ff7a1a`
- Bronze / brass metal: `#3a3024` → `#6b5836` → `#b9924a`
- Aged label grey: `#9a8b6e`

## Key design decisions

- **Full-bleed opaque square** (a rectangular faceplate "device"), not a
  rounded-transparent tile. Thematically correct, and crisp on the taskbar; a
  bronze frame + inner rounded plate give it definition on light backgrounds too.
- **Amber halo ring** behind the knob is the element that carries small-size
  legibility. The stock knob detail (knurling, ticks) collapses below ~32px, so
  the halo was brightened/widened in refinement so the icon still reads as a
  glowing dial at 16–24px instead of "a dark tile with a speck."
- **Subtle vignette** darkens the cabinet corners for depth.
- **resvg-safe SVG**: gradients only, **no `<filter>`/blur** (resvg — the engine
  MAUI's resizetizer uses — does not fully support blur). All "glow" is stacked
  radial gradients; all repeated geometry (knurl ridges, ticks) is enumerated.

## Assets

| File | Role |
|------|------|
| `src/Nugsdotnet.App/Resources/AppIcon/appicon.svg` | MAUI app icon — single full-bleed `MauiIcon` (no `ForegroundFile`/`Color`) |
| `src/Nugsdotnet.App/Resources/Splash/splash.svg` | Splash — the knob "floats" (cabinet/screws removed) on the splash `Color="#0a0907"` |
| `packaging/nugsdotnet.ico` | Installer icon — multi-res 16/24/32/48/64/128/256, rendered per-size with resvg then packed with ImageMagick |

The old `appiconfg.svg` (the ".NET" wordmark foreground) was removed.

## Process

1. Pulled the exact palette + theme language from `app.css`.
2. Ran a 5-agent workflow generating diverse tuning-dial SVGs; rendered every
   candidate with resvg and curated to 3 finalists on a visual contact sheet.
3. User picked the "Knurled Tuner" (warm halo + brass knob).
4. Refined for production (brighter halo for 16px legibility + vignette),
   verified on dark/light backgrounds at 16–256px, user approved.

## Phase 2 note

When Android/iOS heads land, add an adaptive foreground (the knob element on a
transparent canvas, sized for circular masking) and re-introduce `ForegroundFile`
+ `Color` on `MauiIcon`. The master art is `appicon.svg`; the ICO is regenerated
by rendering it per-size with resvg and packing with ImageMagick.
