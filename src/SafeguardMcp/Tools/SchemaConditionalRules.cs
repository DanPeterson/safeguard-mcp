using System;
using System.Collections.Generic;
using System.Text;

namespace SafeguardMcp.Tools;

/// <summary>
/// Curated, wire-verified conditional / cross-field requirements for a small set
/// of high-traffic write endpoints. The OpenAPI-derived schema lists every
/// property and its type, but it cannot express appliance business rules such as
/// "a directory identity provider also needs a service account and a domain name".
/// Those rules only surface as HTTP 400s after a failed POST/PUT — the exact
/// failures observed most often when agents drive Safeguard_Execute.
///
/// Each note is appended to <see cref="SafeguardApiTool.Safeguard_Schema"/> output
/// so a single Schema read can prevent the retry loop. Provenance: every rule here
/// was reproduced from live appliance validation messages (error codes are cited).
/// Keep notes short, factual, and tied to an observed error code.
/// </summary>
internal static class SchemaConditionalRules
{
    private sealed record Rule(string Method, string Template, string Note);

    // Templates use {seg} as a single-segment wildcard (matches ids, -1, GUIDs).
    private static readonly Rule[] Rules =
    {
        new("POST", "/v4/IdentityProviders",
            "Conditional requirements (from appliance validation):\n"
            + "  - Directory providers: set TypeReferenceName to a defined enum value "
            + "(Safeguard_Reference topic=enum name=\"IdentityProviderTypeReferenceName\"); "
            + "'ActiveDirectory' etc. are rejected as free text (70010).\n"
            + "  - DirectoryProperties.DomainName is required for a directory (70007).\n"
            + "  - A service account is required: set ServiceAccountId, or "
            + "ServiceAccountName together with ServiceAccountDomainName (60242 / 60203).\n"
            + "  - The service account credential must resolve against the forest root, "
            + "or the create fails at bind time (60197)."),

        new("POST", "/v4/Assets",
            "Conditional requirements (from appliance validation):\n"
            + "  - NetworkAddress is required (70007).\n"
            + "  - AssetPartitionId must be a valid non-zero database ID; -1 is rejected (70000). "
            + "GET /v4/AssetPartitions to pick a real id.\n"
            + "  - PlatformId is required and must match a platform (GET /v4/Platforms).\n"
            + "  - When ConnectionProperties uses a service account, its domain name is "
            + "required for directory-based platforms (60203)."),

        new("POST", "/v4/AssetAccounts",
            "Conditional requirements (from appliance validation):\n"
            + "  - AssetId is required and must reference an existing asset.\n"
            + "  - Directory accounts also require DomainName.\n"
            + "  - Account Name must be unique on the asset; a duplicate returns 50002."),

        new("POST", "/v4/AccessPolicies",
            "Conditional requirements (from appliance validation):\n"
            + "  - When approval is required, ApproverProperties must include at least one "
            + "approver set; otherwise the create fails with 60194.\n"
            + "  - RequesterProperties / AccessRequestProperties must match the policy's "
            + "AccessRequestType."),

        new("POST", "/v4/AssetPartitions/{seg}/ChangeSchedules",
            "Conditional requirements (from appliance validation):\n"
            + "  - A valid TimeZoneId (IANA/Windows time-zone id) is required for the "
            + "PasswordChangeSchedule schedule (60354)."),

        new("POST", "/v4/AssetPartitions/{seg}/CheckSchedules",
            "Conditional requirements (from appliance validation):\n"
            + "  - A valid TimeZoneId (IANA/Windows time-zone id) is required for the "
            + "PasswordCheckSchedule schedule (60354)."),

        new("PUT", "/v4/AssetGroups/{seg}",
            "Conditional requirements (from appliance validation):\n"
            + "  - Grouping rules (dynamic membership) can only be set on a dynamic group. "
            + "Setting rules on a static group returns 60551; create/convert the group as "
            + "dynamic first."),
    };

    /// <summary>
    /// Returns the conditional-requirements note for an endpoint, or null when the
    /// (method, path) pair has no curated rule. Matching is case-insensitive and
    /// treats <c>{...}</c> template segments as single-segment wildcards so concrete
    /// paths like <c>/v4/AssetPartitions/-1/ChangeSchedules</c> resolve.
    /// </summary>
    internal static string Lookup(string method, string path)
    {
        if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var rule in Rules)
        {
            if (!rule.Method.Equals(method, StringComparison.OrdinalIgnoreCase))
                continue;
            if (TemplateMatches(rule.Template, path))
                return rule.Note;
        }

        return null;
    }

    private static bool TemplateMatches(string template, string path)
    {
        var t = Split(template);
        var p = Split(path);
        if (t.Count != p.Count)
            return false;

        for (int i = 0; i < t.Count; i++)
        {
            var seg = t[i];
            if (seg.Length >= 2 && seg[0] == '{' && seg[^1] == '}')
                continue; // wildcard segment
            if (!seg.Equals(p[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static List<string> Split(string path)
    {
        var result = new List<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            result.Add(part);
        return result;
    }
}
