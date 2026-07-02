# Repo look-and-feel — creative README + front door

**Date:** 2026-06-26
**Status:** Approved, implemented

> **Historical record.** Written when the repo was dual-head (web + MAUI); those
> heads were later retired in favour of the native WinUI 3 app, and the README
> was rewritten around it. The RECEIVER '74 voice, banner, and badge style this
> spec established carry forward unchanged.

## Goal

Refresh the repo's "look and feel" so the front door matches the app's
established **RECEIVER '74 / VFD** aesthetic and the new branded icon — and, while
there, fix the README's drift (it described only the web head; the repo is now
**dual-head**: web + native MAUI Blazor Hybrid).

## Scope (approved: full pass)

- Rewrite `README.md` in the receiver voice, dual-head-accurate.
- Generate a **hero banner** image + a **social-preview card**.
- Add status **badges** (palette-themed).
- Set repo **topics**.
- Remove the stock `dotnet_bot.png` cruft.

## Decisions

- **Approach:** one rich, themed README (the whole story up front) with deep
  technical bits in `<details>` blocks so it reads as a showcase *and* stays
  skimmable. (Alternative — lean hero + links to `docs/` — rejected: buries the
  personality.)
- **Voice:** full thematic — receiver-flavored section names (*Why this rig
  exists*, *Signal path*, *Power on*, *Off the shelf*, *Front panel*, *On the
  dial*, *Under the hood*) with crisp technical content underneath.
- **Banner:** the **VFD-readout** variant — amber `nugsdotnet` glowing on a
  smoked-glass tuner strip with a dial scale, the floating knob to the left.
  Set in the app's actual **DM Mono** font (its VFD/readout typeface).

## Assets

| File | Role |
|------|------|
| `docs/assets/banner.png` | README hero, 1600×400 |
| `docs/assets/social-card.png` | GitHub social preview, 1280×640 (upload via repo Settings → Social preview — not settable by API) |

Both palette-locked to `app.css` (amber `#ffb22e`, walnut `#15120D`, bone
`#efe4cf`, bronze). The **signal-path ASCII diagram** is generated with column
math (`make-diagram.js`) so every box border and connector aligns.

## Generation recipe (for future edits)

resvg renders the cabinet/scale background SVG; the **floating knob** (the splash
variant) is composited on; ImageMagick draws the DM Mono wordmark with an amber
glow (blur an amber copy, crisp layer on top). DM Mono is OFL-licensed (fetched
from google/fonts). Same engine/constraints as the icon — gradients, no blur
filters in the SVG layer. Scripts live in the scratchpad; the committed PNGs are
the deliverables.

## Repo metadata

- Topics: `nugs` `live-music` `blazor` `blazor-webassembly` `maui`
  `blazor-hybrid` `dotnet` `csharp` `winget` `music-player` `desktop-app`.
- Removed `src/Nugsdotnet.App/Resources/Images/dotnet_bot.png` (unused template art).

## Accuracy fixes folded in

- README now documents the **native head** + its **loopback Kestrel**
  (`127.0.0.1:0`) for WebView audio, the shared `Nugsdotnet.UI` RCL, and the
  per-project roles — replacing the old web-only description.
- Dropped the obsolete "Removing the legacy Node scaffold" section (already gone).
