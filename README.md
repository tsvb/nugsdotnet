# nugsdotnet

A personal local web client for [nugs.net](https://nugs.net), written in C#.
Exists because the official nugs UI has slow search, weak queue/playlist UX,
and no keyboard shortcuts. Personal use against your own subscription only —
no content is downloaded, redistributed, or stripped of DRM.

## Stack

- **Server:** ASP.NET Core 9 minimal API (`Nugsdotnet.Server`).
- **Client:** Blazor WebAssembly (`Nugsdotnet.Client`), served by the same host.
- **Shared:** DTOs in `Nugsdotnet.Shared`.

The server hosts the WASM client and proxies all nugs API calls. Two reasons
the browser can't talk to nugs directly:

1. **CORS** — nugs's API doesn't permit cross-origin calls from `localhost`.
2. **Audio headers** — the audio CDN requires `Referer: play.nugs.net` and a
   mobile `User-Agent`. Browsers won't let JS set those, so we proxy.

```
┌──────────────────┐  /api/*   ┌─────────────────────┐   TLS   ┌──────────┐
│ Blazor WASM      │ ────────► │ ASP.NET Core (Kestrel)│ ──────► │ nugs.net │
│ (in browser)     │           │ ./tokens.json         │         └──────────┘
└──────────────────┘  static   └─────────────────────┘
        ▲                              │
        └──────────────────────────────┘
              served by Kestrel
```

## Endpoints

The unofficial nugs API surface is documented by community projects:

- [Sorrow446/Nugs-Downloader](https://github.com/Sorrow446/Nugs-Downloader) (Go)
- [Dniel97/orpheusdl-nugs](https://github.com/Dniel97/orpheusdl-nugs) (Python)

Check those when an endpoint or shape stops working.

## Run it

You need .NET 9 SDK. Then:

```powershell
# add your nugs credentials with user-secrets (preferred)
dotnet user-secrets set "Nugs:Email" "you@example.com" --project src/Nugsdotnet.Server
dotnet user-secrets set "Nugs:Password" "your-password" --project src/Nugsdotnet.Server

dotnet run --project src/Nugsdotnet.Server
```

The server binds `http://localhost:5273`. Open it, sign in (the "use
credentials from appsettings/env" checkbox is on by default), search, click
through to a show, hit ▶ on a track.

If you'd rather not use user-secrets, you can also set environment variables
`NUGS_EMAIL` and `NUGS_PASSWORD`, or fill them into `appsettings.json` (which
is not gitignored — be careful).

If you log into nugs via Apple/Google SSO, the password grant won't work for
you. v0.2 will support pasting an `access_token` from the play.nugs.net
devtools — see `token.md` in the Nugs-Downloader repo for how to grab one.

## Keyboard shortcuts

| key       | action                                  |
| --------- | --------------------------------------- |
| `/`       | Focus the search bar                    |
| `space`   | Play / pause                            |
| `n`       | Next track in queue                     |
| `p`       | Previous track in queue                 |
| `Esc`     | Blur a focused input                    |

Shortcuts are bound at the window level via JS interop (`audio-interop.js`),
so they work anywhere — except inside `<input>`/`<textarea>` elements where
the keys do their normal thing.

## Roadmap

- **v0.1** — auth, search, album & artist views, single-track playback.
- **v0.2** *(current)* — full artist list landing page, queue + autoplay through albums, prev/next + global keyboard shortcuts, add-to-queue / play-next.
- **v0.3** — persistent now-playing across reloads, scrubber metadata, fast date-browser per artist.
- **v0.4** — library/favorites sync, history, fuzzy local search index, mini-player, optional offline cache.

## Notes for hacking

- Tokens live in `tokens.json` next to the server (gitignored). Refresh
  happens automatically ~60s before expiry.
- Catalog endpoints return raw `JsonNode`. The Razor components dig out
  fields defensively because nugs's response shapes use inconsistent casing
  (`artistID` vs `ArtistID`) and pluralization. Toggle the `json` button in
  the topbar to inspect any view's raw response.
- `platformID` mapping for `bigriver/subPlayer.aspx`:
  `1=ALAC, 2=FLAC 16/44, 3=MQA 24/48, 4=360RA, 5=AAC 150k, 6=HLS`. We prefer
  FLAC, fall back through the probe list, and skip HLS-only tracks for now.
- The `audio-interop.js` shim in `wwwroot/js/` exists because Blazor can't
  call `play()`/`pause()` on a media element through `ElementReference`
  alone. It's small on purpose.

## Removing the legacy Node scaffold

The original v0.1 was Node + React. Once the C# version runs, you can
delete the leftover folders:

```powershell
Remove-Item -Recurse -Force package.json, server, web, .env.example
```
