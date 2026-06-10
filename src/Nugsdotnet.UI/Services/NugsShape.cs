using System.Text.Json.Nodes;

namespace Nugsdotnet.UI.Services;

/// <summary>
/// Helpers for digging into nugs's irregular JSON. Field names alternate
/// between camelCase and PascalCase across endpoints; some are pluralized
/// inconsistently. This module owns the shape-dependent parts so component
/// code stays readable.
/// </summary>
public static class NugsShape
{
    /// <summary>Catalog endpoints typically wrap their payload in a Response object.</summary>
    public static JsonNode? Unwrap(JsonNode? root) =>
        root?["Response"] ?? root?["response"] ?? root;

    /// <summary>Pulls the first non-null property from a list of candidate names.</summary>
    public static string? Str(JsonNode? n, params string[] keys)
    {
        if (n is null) return null;
        foreach (var k in keys)
        {
            var v = n[k];
            if (v is JsonValue jv && jv.TryGetValue<string>(out var s) && !string.IsNullOrEmpty(s))
                return s;
            if (v is not null && v.GetValueKind() != System.Text.Json.JsonValueKind.Null)
                return v.ToString();
        }
        return null;
    }

    public static JsonArray? Arr(JsonNode? n, params string[] keys)
    {
        if (n is null) return null;
        foreach (var k in keys)
        {
            if (n[k] is JsonArray a) return a;
        }
        return null;
    }
}
