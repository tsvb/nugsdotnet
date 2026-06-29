using System.Text.Json.Nodes;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Tests;

public class NugsShapeTests
{
    [Fact]
    public void Unwrap_pulls_PascalCase_response_envelope()
    {
        var node = JsonNode.Parse("""{"Response":{"x":1}}""");
        Assert.NotNull(NugsShape.Unwrap(node)!["x"]);
    }

    [Fact]
    public void Unwrap_pulls_camelCase_envelope_and_passes_through_bare_objects()
    {
        Assert.NotNull(NugsShape.Unwrap(JsonNode.Parse("""{"response":{"y":2}}"""))!["y"]);
        Assert.NotNull(NugsShape.Unwrap(JsonNode.Parse("""{"z":3}"""))!["z"]);
    }

    [Fact]
    public void Str_picks_the_first_present_candidate_key()
    {
        var node = JsonNode.Parse("""{"ArtistName":"Phish"}""");
        Assert.Equal("Phish", NugsShape.Str(node, "artistName", "ArtistName", "name"));
    }

    [Fact]
    public void Str_skips_null_values_and_returns_the_next_candidate()
    {
        var node = JsonNode.Parse("""{"b":null,"c":"hit"}""");
        Assert.Equal("hit", NugsShape.Str(node, "b", "c"));
    }

    [Fact]
    public void Str_stringifies_non_string_values()
    {
        var node = JsonNode.Parse("""{"id":12345}""");
        Assert.Equal("12345", NugsShape.Str(node, "id"));
    }

    [Fact]
    public void Arr_returns_the_first_array_candidate()
    {
        var node = JsonNode.Parse("""{"Items":[1,2,3]}""");
        Assert.Equal(3, NugsShape.Arr(node, "items", "Items")!.Count);
    }

    [Fact]
    public void Str_and_Arr_are_null_safe()
    {
        Assert.Null(NugsShape.Str(null, "x"));
        Assert.Null(NugsShape.Arr(null, "x"));
    }
}
