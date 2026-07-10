using SafeguardMcp.Tools;

namespace SafeguardMcp.Tests;

public class SchemaConditionalRulesTests
{
    [Theory]
    [InlineData("POST", "/v4/IdentityProviders")]
    [InlineData("POST", "/v4/Assets")]
    [InlineData("POST", "/v4/AssetAccounts")]
    [InlineData("POST", "/v4/AccessPolicies")]
    public void Lookup_ReturnsNote_ForCuratedCollectionEndpoints(string method, string path)
    {
        var note = SchemaConditionalRules.Lookup(method, path);
        Assert.False(string.IsNullOrWhiteSpace(note));
        Assert.Contains("Conditional requirements", note);
    }

    [Fact]
    public void Lookup_MatchesTemplateWildcard_ForScheduleSubResource()
    {
        // Concrete partition id (-1 is the default partition) must resolve to the
        // /v4/AssetPartitions/{seg}/ChangeSchedules template.
        var note = SchemaConditionalRules.Lookup("POST", "/v4/AssetPartitions/-1/ChangeSchedules");
        Assert.NotNull(note);
        Assert.Contains("TimeZoneId", note);
    }

    [Fact]
    public void Lookup_MatchesTemplateWildcard_ForNumericId()
    {
        var note = SchemaConditionalRules.Lookup("POST", "/v4/AssetPartitions/42/CheckSchedules");
        Assert.NotNull(note);
        Assert.Contains("TimeZoneId", note);
    }

    [Fact]
    public void Lookup_IsCaseInsensitive_OnMethodAndPath()
    {
        Assert.NotNull(SchemaConditionalRules.Lookup("post", "/v4/identityproviders"));
    }

    [Fact]
    public void Lookup_ReturnsNull_ForUncuratedEndpoint()
    {
        Assert.Null(SchemaConditionalRules.Lookup("POST", "/v4/Users"));
    }

    [Fact]
    public void Lookup_ReturnsNull_WhenMethodDoesNotMatch()
    {
        // AssetGroups has a PUT rule but no POST rule.
        Assert.Null(SchemaConditionalRules.Lookup("POST", "/v4/AssetGroups/1"));
        Assert.NotNull(SchemaConditionalRules.Lookup("PUT", "/v4/AssetGroups/1"));
    }

    [Fact]
    public void Lookup_ReturnsNull_ForSegmentCountMismatch()
    {
        // /v4/AssetPartitions/-1 (no sub-resource) must not match the schedule template.
        Assert.Null(SchemaConditionalRules.Lookup("POST", "/v4/AssetPartitions/-1"));
    }

    [Theory]
    [InlineData(null, "/v4/Assets")]
    [InlineData("POST", null)]
    [InlineData("", "")]
    public void Lookup_ReturnsNull_ForMissingArgs(string? method, string? path)
    {
        Assert.Null(SchemaConditionalRules.Lookup(method, path));
    }
}
