using SafeguardMcp.Catalog;
using SafeguardMcp.Tools;

namespace SafeguardMcp.Tests;

public class EnumValueHintTests
{
    // 70010: "Invalid filter - enum value is not defined: <Value>". The pre-existing
    // "Invalid filter property" branch does NOT match this message, so it used to fall
    // through to the generic 400 hint. It should now route the agent to the enum member
    // list instead.
    [Fact]
    public void GetErrorHint_EnumValueNotDefined_RoutesToEnumReference()
    {
        var ctx = new ErrorContext("Core", "GET", "/v4/Platforms");
        var hint = ApiToolHelpers.GetErrorHint(
            statusCode: 400,
            apiMessage: "Invalid filter - enum value is not defined: ActiveDirectory.",
            hasModelState: false,
            ctx,
            paths: System.Array.Empty<ApiSchemaPropertyPath>(),
            requestPath: "/v4/Platforms",
            templateMatched: true);

        Assert.NotNull(hint);
        Assert.Contains("ActiveDirectory", hint);
        Assert.Contains("topic=enum", hint);
        Assert.Contains("case-sensitive", hint);
    }

    [Fact]
    public void GetErrorHint_EnumValueNotDefined_IncludesSchemaPathWhenKnown()
    {
        var ctx = new ErrorContext("Core", "GET", "/v4/Platforms");
        var hint = ApiToolHelpers.GetErrorHint(
            statusCode: 400,
            apiMessage: "Invalid filter - enum value is not defined: ActiveDirectory.",
            hasModelState: false,
            ctx,
            paths: System.Array.Empty<ApiSchemaPropertyPath>(),
            requestPath: "/v4/Platforms",
            templateMatched: true);

        Assert.Contains("Safeguard_Schema path=/v4/Platforms", hint);
    }

    [Fact]
    public void GetErrorHint_EnumValueNotDefined_DoesNotFallThroughToGeneric400()
    {
        var ctx = new ErrorContext("Core", "GET", "/v4/Platforms");
        var hint = ApiToolHelpers.GetErrorHint(
            statusCode: 400,
            apiMessage: "Invalid filter - enum value is not defined: Nope.",
            hasModelState: false,
            ctx,
            paths: System.Array.Empty<ApiSchemaPropertyPath>(),
            requestPath: "/v4/Platforms",
            templateMatched: true);

        Assert.DoesNotContain("Check request body format", hint);
    }
}
