using SafeguardMcp.Tools;

namespace SafeguardMcp.Tests;

public class SchemaConsultationTrackerTests
{
    private const string Session = "session-A";

    [Fact]
    public void WasConsulted_FalseUntilRecorded()
    {
        var t = new SchemaConsultationTracker();
        Assert.False(t.WasConsulted(Session, "POST", "/v4/Users"));
        t.Record(Session, "POST", "/v4/Users");
        Assert.True(t.WasConsulted(Session, "POST", "/v4/Users"));
    }

    [Fact]
    public void Record_IsMethodSpecific()
    {
        var t = new SchemaConsultationTracker();
        t.Record(Session, "POST", "/v4/Users");
        Assert.True(t.WasConsulted(Session, "POST", "/v4/Users"));
        Assert.False(t.WasConsulted(Session, "PUT", "/v4/Users"));
    }

    [Fact]
    public void WasConsulted_IsCaseInsensitive()
    {
        var t = new SchemaConsultationTracker();
        t.Record(Session, "post", "/v4/users");
        Assert.True(t.WasConsulted(Session, "POST", "/v4/Users"));
    }

    [Fact]
    public void Consultation_PersistsAcrossCalls_WithinSameSession()
    {
        var t = new SchemaConsultationTracker();
        t.Record(Session, "POST", "/v4/Users");
        t.Record(Session, "PUT", "/v4/Assets/{id}");

        // A later, independent call for the same session still sees both records —
        // this is the HTTP-mode fix: memory survives across per-request DI scopes.
        Assert.True(t.WasConsulted(Session, "POST", "/v4/Users"));
        Assert.True(t.WasConsulted(Session, "PUT", "/v4/Assets/{id}"));
    }

    [Fact]
    public void Consultation_IsIsolatedBetweenSessions()
    {
        var t = new SchemaConsultationTracker();
        t.Record("session-A", "POST", "/v4/Users");

        Assert.True(t.WasConsulted("session-A", "POST", "/v4/Users"));
        Assert.False(t.WasConsulted("session-B", "POST", "/v4/Users"));
    }

    [Fact]
    public void BlankSessionId_CollapsesToSingleSession()
    {
        var t = new SchemaConsultationTracker();
        t.Record(null, "POST", "/v4/Users");

        // stdio has no MCP session id; null/empty/whitespace must all resolve to the
        // same default session so the memory still works as one process-wide session.
        Assert.True(t.WasConsulted("", "POST", "/v4/Users"));
        Assert.True(t.WasConsulted("   ", "POST", "/v4/Users"));
        Assert.True(t.WasConsulted(SchemaConsultationTracker.DefaultSessionId, "POST", "/v4/Users"));
    }

    [Fact]
    public void SessionEviction_DropsLeastRecentlyUsedSession()
    {
        var t = new SchemaConsultationTracker();

        // Record one endpoint under the oldest session, then touch it so it is the LRU.
        t.Record("oldest", "POST", "/v4/Users");

        // Fill up to the session cap with distinct fresh sessions, pushing "oldest" out.
        for (var i = 0; i < SchemaConsultationTracker.MaxSessions; i++)
            t.Record($"s{i}", "POST", "/v4/Users");

        Assert.False(t.WasConsulted("oldest", "POST", "/v4/Users"));
        Assert.True(t.WasConsulted($"s{SchemaConsultationTracker.MaxSessions - 1}", "POST", "/v4/Users"));
    }

    [Fact]
    public void PerSessionEntryCap_IsBounded()
    {
        var t = new SchemaConsultationTracker();

        // Overflow the per-session entry cap; the most recent entry must still be present
        // and the session must not be dropped.
        for (var i = 0; i < SchemaConsultationTracker.MaxEntriesPerSession + 50; i++)
            t.Record(Session, "POST", $"/v4/Endpoint{i}");

        var lastPath = $"/v4/Endpoint{SchemaConsultationTracker.MaxEntriesPerSession + 49}";
        Assert.True(t.WasConsulted(Session, "POST", lastPath));
    }

    [Theory]
    [InlineData(null, "/v4/Users")]
    [InlineData("POST", null)]
    [InlineData("", "")]
    public void RecordAndWasConsulted_IgnoreMissingArgs(string? method, string? path)
    {
        var t = new SchemaConsultationTracker();
        t.Record(Session, method, path);
        Assert.False(t.WasConsulted(Session, method, path));
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
