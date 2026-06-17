using Microsoft.Extensions.DependencyInjection;
using Nugsdotnet.Core.Nugs;

namespace Nugsdotnet.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the nugs.net client stack: NugsClient (typed HttpClient with a
    /// 5-minute timeout — FLAC files can be hundreds of MB), TokenStore
    /// (singleton at the given path), and StreamInspector (singleton cache).
    /// Called by both the web Server and the MAUI loopback host so the two
    /// heads share one registration story.
    /// </summary>
    public static IServiceCollection AddNugsCore(
        this IServiceCollection services, string tokenPath)
    {
        // No global timeout: audio bodies stream for the whole track (long FLAC
        // sets, plus a preload connection opened early while another track
        // plays). FetchAudioAsync bounds connect+headers per request instead.
        services.AddHttpClient<NugsClient>(c =>
        {
            c.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddSingleton(new TokenStore(tokenPath));
        services.AddSingleton<StreamInspector>();
        return services;
    }
}
