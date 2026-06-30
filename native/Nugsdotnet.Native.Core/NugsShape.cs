using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nugsdotnet.Native.Core;

/// <summary>
/// Helpers for digging into nugs's irregular JSON. Field names alternate between
/// camelCase and PascalCase across endpoints and some are pluralized
/// inconsistently, so callers pass a list of candidate keys. Pure and
/// dependency-free — fully unit-testable.
/// </summary>
public static class NugsShape
{
    /// <summary>Catalog endpoints usually wrap the payload in a Response object.</summary>
    public static JsonNode? Unwrap(JsonNode? root) =>
        root?["Response"] ?? root?["response"] ?? root;

    /// <summary>First non-null/non-empty property value from the candidate keys.</summary>
    public static string? Str(JsonNode? n, params string[] keys)
    {
        if (n is null) return null;
        foreach (var k in keys)
        {
            var v = n[k];
            if (v is JsonValue jv && jv.TryGetValue<string>(out var s) && !string.IsNullOrEmpty(s))
                return s;
            if (v is not null && v.GetValueKind() != JsonValueKind.Null)
            {
                // Skip empties too, so an empty value falls through to the next
                // candidate key (matches this method's "first non-empty" contract).
                var raw = v.ToString();
                if (!string.IsNullOrEmpty(raw)) return raw;
            }
        }
        return null;
    }

    /// <summary>First array property from the candidate keys.</summary>
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
