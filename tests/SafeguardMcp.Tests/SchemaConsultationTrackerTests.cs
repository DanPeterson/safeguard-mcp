using SafeguardMcp.Tools;

namespace SafeguardMcp.Tests;

public class SchemaConsultationTrackerTests
{
    [Fact]
    public void WasConsulted_FalseUntilRecorded()
    {
        var t = new SchemaConsultationTracker();
        Assert.False(t.WasConsulted("POST", "/v4/Users"));
        t.Record("POST", "/v4/Users");
        Assert.True(t.WasConsulted("POST", "/v4/Users"));
    }

    [Fact]
    public void Record_IsMethodSpecific()
    {
        var t = new SchemaConsultationTracker();
        t.Record("POST", "/v4/Users");
        Assert.True(t.WasConsulted("POST", "/v4/Users"));
        Assert.False(t.WasConsulted("PUT", "/v4/Users"));
    }

    [Fact]
    public void WasConsulted_IsCaseInsensitive()
    {
        var t = new SchemaConsultationTracker();
        t.Record("post", "/v4/users");
        Assert.True(t.WasConsulted("POST", "/v4/Users"));
    }

    [Theory]
    [InlineData(null, "/v4/Users")]
    [InlineData("POST", null)]
    [InlineData("", "")]
    public void RecordAndWasConsulted_IgnoreMissingArgs(string? method, string? path)
    {
        var t = new SchemaConsultationTracker();
        t.Record(method, path);
        Assert.False(t.WasConsulted(method, path));
    }
}

public class SchemaConsultationGatingTests
{
    [Fact]
    public void Gating_Write400_NotConsulted_PrependsDirective()
    {
        var hint = ApiToolHelpers.ApplySchemaConsultationGating(
            statusCode: 400, method: "POST", templatePath: "/v4/Users",
            wasConsulted: false, baseHint: "Fix the fields listed under 'Validation errors' and retry.");

        Assert.StartsWith("You have not called Safeguard_Schema for POST /v4/Users", hint);
        Assert.Contains("Safeguard_Schema path=/v4/Users method=POST", hint);
        // Base hint is preserved after the directive.
        Assert.Contains("Fix the fields listed under 'Validation errors'", hint);
    }

    [Fact]
    public void Gating_Write400_NotConsulted_NullBaseHint_ReturnsDirectiveOnly()
    {
        var hint = ApiToolHelpers.ApplySchemaConsultationGating(
            400, "PUT", "/v4/Assets/{id}", wasConsulted: false, baseHint: null);

        Assert.NotNull(hint);
        Assert.Contains("You have not called Safeguard_Schema for PUT /v4/Assets/{id}", hint);
    }

    [Fact]
    public void Gating_NoOp_WhenAlreadyConsulted()
    {
        var baseHint = "Fix the fields listed under 'Validation errors' and retry.";
        var hint = ApiToolHelpers.ApplySchemaConsultationGating(
            400, "POST", "/v4/Users", wasConsulted: true, baseHint);

        Assert.Equal(baseHint, hint);
    }

    [Fact]
    public void Gating_NoOp_ForGetRequests()
    {
        var baseHint = "some hint";
        var hint = ApiToolHelpers.ApplySchemaConsultationGating(
            400, "GET", "/v4/Users", wasConsulted: false, baseHint);

        Assert.Equal(baseHint, hint);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    public void Gating_NoOp_ForNon400(int statusCode)
    {
        var baseHint = "some hint";
        var hint = ApiToolHelpers.ApplySchemaConsultationGating(
            statusCode, "POST", "/v4/Users", wasConsulted: false, baseHint);

        Assert.Equal(baseHint, hint);
    }

    [Fact]
    public void Gating_NoOp_WhenTemplatePathMissing()
    {
        var hint = ApiToolHelpers.ApplySchemaConsultationGating(
            400, "POST", templatePath: "", wasConsulted: false, baseHint: "x");

        Assert.Equal("x", hint);
    }
}
