# The Listening Journal — Home dashboard redesign

**Date:** 2026-08-23
**Status:** Approved design, pending implementation plan
**Scope:** New listening-journal data layer + Home page redesign ("The Wrap") + dedicated Artists page

## Problem

The Home dashboard is a functional workbench (continue hero, recents/stash rails, A–Z grid) but gives no reason to return: nothing accumulates, nothing is yours. The app tracks no listening history — recents are capped at 12 entries and nothing measures time listened.

## Goal

Make the dashboard a listening journal that rewards returning: hours, shows, tracks, night-run streaks, personal charts, and a night-by-night timeline, all in the RECEIVER '74 instrument idiom. The user chose:

- **Delight flavor:** "Progressed — my history feels rich"
- **Layout:** **C · The Wrap** — the dashboard *is* the journal; the A–Z artist grid moves off Home
- **Journal hero style:** **C · VU meters** — needle-and-scale meters, next milestone as the red zone
- **Extras:** milestones, streak grace night, last-night recap
- **Cold start:** "the journal begins tonight" — no backfill, stats start from zero at ship

## 1. Data layer — `ListeningJournal` (new, Core)

**File:** `native/Nugsdotnet.Native.Core/ListeningJournal.cs`
**Storage:** `%LOCALAPPDATA%\nugsdotnet\accounts\{userId}\journal.json`, atomic writes + semaphore locking, identical discipline to `RecentsStore`/`StashStore`. Plain JSON, nothing sensitive.

### Recorded data

| Structure | Fields | Powers |
|---|---|---|
| **Daily aggregate** (per local calendar date, capped 400 nights, pruned oldest) | listening seconds, tracks completed, shows touched (≥ 30 s threshold, same as "show heard"), per-artist seconds, per-show seconds (capped 24 shows/night) | timeline, streaks, charts, hours, night tooltip |
| **Show total** (per containerId, uncapped) | seconds, tracks completed, artist, title, date, venue, last played | "shows heard", milestones |
| **Milestones reached** | id → date fired | fire-once amber pulse |
| **Counters** | total tracks completed | TRACKS meter |

### Definitions (pure, testable)

- **Listening night:** a local calendar date with ≥ 5 minutes (300 s) of accumulated listening.
- **Grace night:** one skipped night inside a run renders hollow (◇) and does not break the run; two consecutive skipped nights end it. Best run records the maximum.
- **Show heard:** a show with ≥ 1 track played ≥ 30 s cumulative.
- **Track counted:** natural completion or position ≥ 95%.
- **Milestones:** shows heard 10 / 25 / 50 / 100 / 250 / 500 / 1000 · hours 10 / 50 / 100 / 500 · night run 7 / 14 / 30. Each fires once; the pulse shows on the first visit after crossing.

### Pure math class

All computation lives in a static, I/O-free class (the `HomeDashboard` pattern): streak-with-grace, best run, top-N artists over a trailing window (default 30 nights), 14-night timeline arrays, milestone evaluation, VU scale mapping (value → needle %, scale endpoints, red-zone start = next milestone). Gated by `Nugsdotnet.Native.Tests`.

### Tracker (app layer)

`JournalTracker` samples `PlayerService` on a ~15 s tick. While `IsPlaying`, it accumulates elapsed seconds into the current day, current show, and current artist, and counts track completions via the player's natural-advance signal. Flushes to the store at most every ~60 s and on app exit. Paused time never accumulates. A crash loses ≤ 60 s. Journal write failures are swallowed — never break playback.

## 2. Home page — "The Wrap"

Top to bottom:

1. **Greeting zone.** `NIGHT 14 ON THE RECEIVER` (Big Shoulders ExtraBold, as today). Right side: run state `6◆ NIGHT RUN · BEST 9`. Under the greeting, dim mono **last-night recap**: `last night — 1.8 hrs · 2 shows · Goose led` (only when yesterday has data; first visit of a day re-greets).

2. **Journal hero.** Three VU meters — HOURS ON AIR / SHOWS LOGGED / TRACKS — each a scale with needle at current level, printed endpoints, red zone starting at the next milestone, value in DM Mono beside it. Below, two cells: **TOP ARTISTS · 30 NIGHTS** (ranked rows, amber bars scaled to leader, click row → artist page) and **LAST 14 NIGHTS** (amber bars; hollow for grace/skipped nights; hover tooltip: that night's shows + minutes). Milestone line under the band: `40 shows logged ▸ next: 50`, amber pulse on the visit where it first fires.

3. **Resume strip.** Continue hero compressed to one row: small art, title · artist · show, position, PLAY/PAUSE, open show. `ON THE DECK` when playing. Keeps today's keyboard/behavior contract.

4. **Tonight's shelf.** Recents + stash merged into one art-card rail — recents first, deduped by container, stash filling behind, capped at 24 cards; `see all` → StashPage.

5. **Your artists.** Chips as today, plus `full index ▸` → ArtistsPage.

**Retired from Home:** RECENT/STASH/ARTISTS meters (replaced by run state), the large continue card, separate RECENTLY PLAYED and STASH rails.

**Day-one empty state:** hero renders `THE JOURNAL BEGINS TONIGHT — press play and the meter starts running`; meters show `—` at rest, never fake numbers. Modules fill as data lands.

## 3. ArtistsPage (new)

New page hosting the existing virtualized A–Z GridView, letter strip, and filter, moved from Home unchanged. Reachable via `full index ▸` on Home and back-navigation returns Home. Home keeps the `YourArtists` chips.

## 4. Errors, edges, testing

- Journal writes never break playback; corrupt `journal.json` → start fresh.
- Day keyed by local date string (DST-safe; no 24 h deltas).
- Daily aggregates pruned at 400 nights; show totals bounded by real listening.
- Sign-out clears the journal view (store persists on disk, account-scoped, like stash/recents).
- **Tests (xUnit, Core):** streak-with-grace, best run, top-N trailing window, milestone fire-once, day-aggregate merge, show-heard threshold, store round-trip + prune, VU scale/needle mapping.
- **Perf:** hero binds from precomputed aggregates; 15 s tracker tick; no per-frame work.

## Out of scope

- Stash/recents backfill into the journal (journal begins at ship).
- Any server sync; the journal is local-only.
- Editing/history corrections UI.
