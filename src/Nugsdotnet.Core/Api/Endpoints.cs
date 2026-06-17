using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nugsdotnet.Core.Nugs;
using Nugsdotnet.Shared;

namespace Nugsdotnet.Core.Api;

public static class Endpoints
{
    public static void MapApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        // --- session ---------------------------------------------------------

        api.MapGet("/session", async (NugsClient nugs, CancellationToken ct) =>
        {
            try
            {
                var s = await nugs.GetSessionAsync(ct);
                return Results.Ok(new SessionInfo(true, s.UserId, s.PlanDescription, s.IsAccessible));
            }
            catch
            {
                return Results.Ok(new SessionInfo(false));
            }
        });

        api.MapPost("/login", async (
            LoginRequest? body, IConfiguration cfg, NugsClient nugs, CancellationToken ct) =>
        {
            var email = body?.Email ?? cfg["Nugs:Email"] ?? Environment.GetEnvironmentVariable("NUGS_EMAIL");
            var pwd = body?.Password ?? cfg["Nugs:Password"] ?? Environment.GetEnvironmentVariable("NUGS_PASSWORD");
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(pwd))
            {
                return Results.BadRequest(new ErrorResponse("missing credentials"));
            }
            try
            {
                await nugs.LoginAsync(email, pwd, ct);
                return Results.Ok();
            }
            catch (Exception ex)
            {
                return Results.Json(new ErrorResponse(ex.Message), statusCode: 401);
            }
        });

        api.MapPost("/logout", async (NugsClient nugs, CancellationToken ct) =>
        {
            await nugs.LogoutAsync(ct);
            return Results.Ok();
        });

        // --- catalog ---------------------------------------------------------
        // These return the raw nugs JSON. The client picks whichever fields
        // are populated (response shapes are inconsistent across endpoints).

        api.MapGet("/search", async (
            [FromQuery] string q, NugsClient nugs, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest(new ErrorResponse("missing q"));
            return Results.Ok(await nugs.SearchAsync(q, ct));
        });

        api.MapGet("/album/{id}", async (
            string id, NugsClient nugs, CancellationToken ct) =>
            Results.Ok(await nugs.GetAlbumAsync(id, ct)));

        api.MapGet("/artists", async (NugsClient nugs, CancellationToken ct) =>
            Results.Ok(await nugs.GetAllArtistsAsync(ct)));

        // Image proxy — strips the user's nugs.net browser cookies (which
        // get attached to any cross-site request) and forwards bytes with
        // a long Cache-Control so the browser only fetches each once.
        // SSRF guard: only paths beginning with /images/ are allowed.
        api.MapGet("/image", async (
            [FromQuery] string path, HttpContext ctx, NugsClient nugs,
            ILogger<NugsClient> log, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(path) ||
                !path.StartsWith("/images/", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ErrorResponse("invalid path"));
            }

            // Catalog API returns paths like "/images/shows/foo.jpg" but the
            // actual host is the nugs CDN with a /livedownloads prefix.
            // The ?h= query param is a CDN-side resize directive (pixels);
            // 400 is plenty for our grid cards.
            var url = "https://assets-01.nugscdn.net/livedownloads" + path + "?h=400";
            using var upstream = await nugs.FetchPublicAsync(url, ct);

            var ct_header = upstream.Content.Headers.ContentType?.ToString() ?? "(none)";
            var len = upstream.Content.Headers.ContentLength;
            log.LogInformation(
                "image proxy: {url} -> {status} {ct} {len} bytes",
                url, (int)upstream.StatusCode, ct_header, len);

            if (!upstream.IsSuccessStatusCode)
            {
                return Results.StatusCode((int)upstream.StatusCode);
            }

            ctx.Response.StatusCode = (int)upstream.StatusCode;
            ctx.Response.Headers.ContentType = ct_header == "(none)" ? "image/jpeg" : ct_header;
            ctx.Response.Headers.CacheControl = "public, max-age=86400, immutable";
            CopyHeader(upstream, ctx, "Content-Length");

            await using var body = await upstream.Content.ReadAsStreamAsync(ct);
            await body.CopyToAsync(ctx.Response.Body, ct);
            return Results.Empty;
        });

        api.MapGet("/artist/{id}/shows", async (
            string id, [FromQuery] int? offset, NugsClient nugs, CancellationToken ct) =>
            Results.Ok(await nugs.GetArtistShowsAsync(id, offset ?? 1, 100, ct)));

        // --- audio streaming -------------------------------------------------

        api.MapGet("/play/{trackId}", async (
            string trackId,
            HttpContext ctx,
            NugsClient nugs,
            StreamInspector inspector,
            ILogger<NugsClient> log,
            CancellationToken ct) =>
        {
            var session = await nugs.GetSessionAsync(ct);
            // Reuse a recently-resolved pick (preload of N+1, a cold-load
            // fallback, or a replay) instead of re-probing all four platforms.
            var pick = inspector.TryGetResolvedPick(trackId)
                ?? await nugs.ResolveBestStreamAsync(trackId, session, ct);
            if (pick is null)
            {
                return Results.NotFound(new ErrorResponse("no stream"));
            }
            inspector.CacheResolvedPick(trackId, pick);  // read-through reuse
            // Seed the dashboard cache so a parallel /stream-info request reuses
            // this pick instead of re-probing all four platforms.
            inspector.StorePick(trackId, pick);
            log.LogInformation("track {t}: serving {fmt} via platform {p}",
                trackId, pick.Format, pick.PlatformId);

            // HLS-only tracks need segment-by-segment proxying; punt for v0.1.
            if (pick.Format == AudioFormat.Hls)
            {
                return Results.Json(
                    new ErrorResponse("HLS-only track — not yet supported"),
                    statusCode: 415);
            }

            var range = ctx.Request.Headers.Range.ToString();
            var upstream = await nugs.FetchAudioAsync(pick.Url, range, ct);

            // Override upstream Content-Type — nugs's CDN sends bogus values
            // (e.g. audio/mp4a-latm for ALAC) that browsers refuse to decode.
            ctx.Response.StatusCode = (int)upstream.StatusCode;
            ctx.Response.Headers.ContentType = NugsClient.GetMimeType(pick.Format);
            CopyHeader(upstream, ctx, "Content-Length");
            CopyHeader(upstream, ctx, "Content-Range");
            CopyHeader(upstream, ctx, "Accept-Ranges", "bytes");

            await using var body = await upstream.Content.ReadAsStreamAsync(ct);
            await body.CopyToAsync(ctx.Response.Body, ct);
            upstream.Dispose();
            return Results.Empty;
        });

        // Resolved-stream metadata for the dashboard (real format + exact specs
        // parsed from the file header). Reuses the pick /play already cached.
        api.MapGet("/stream-info/{trackId}", async (
            string trackId, NugsClient nugs, StreamInspector inspector, CancellationToken ct) =>
        {
            Session session;
            try { session = await nugs.GetSessionAsync(ct); }
            catch { return Results.Json(new ErrorResponse("not logged in"), statusCode: 401); }

            var info = await inspector.GetStreamInfoAsync(trackId, session, nugs, ct);
            return info is null
                ? Results.NotFound(new ErrorResponse("no stream"))
                : Results.Ok(info);
        });
    }

    private static void CopyHeader(
        HttpResponseMessage upstream, HttpContext ctx, string name, string? fallback = null)
    {
        if (upstream.Content.Headers.TryGetValues(name, out var v) ||
            upstream.Headers.TryGetValues(name, out v))
        {
            ctx.Response.Headers[name] = v.ToArray();
        }
        else if (fallback is not null && !ctx.Response.Headers.ContainsKey(name))
        {
            ctx.Response.Headers[name] = fallback;
        }
    }
}
