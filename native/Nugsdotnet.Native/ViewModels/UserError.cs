using System.Net.Http;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>Maps exceptions to a short string the UI can show without leaking
/// URLs, status bodies, or stack-shaped messages.</summary>
internal static class UserError
{
    public static string From(Exception ex) => ex switch
    {
        OperationCanceledException => "Request timed out.",
        HttpRequestException => "Network error — try again.",
        InvalidOperationException { Message: { Length: > 0 } m }
            when m.StartsWith("sign-in failed", StringComparison.Ordinal) => m,
        _ => "Something went wrong.",
    };
}
