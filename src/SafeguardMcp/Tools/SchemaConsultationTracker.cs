using System;
using System.Collections.Concurrent;

namespace SafeguardMcp.Tools;

/// <summary>
/// Best-effort, per-session record of which endpoints the agent has inspected
/// with <see cref="SafeguardApiTool.Safeguard_Schema"/>. Used only to shape error
/// hint text: when a write (POST/PUT/PATCH) returns HTTP 400 and its endpoint was
/// never schema-inspected, the error path prepends a directive to read the schema
/// (which carries required + conditional fields) before retrying.
///
/// Lifetime mirrors <see cref="ISafeguardSession"/>: singleton in stdio (persists
/// across tool calls for the process) and scoped in HTTP (per request). It stores
/// only endpoint template strings — never request bodies or credentials — so the
/// worst case of any lifetime mismatch is a suppressed or extra hint, never data
/// disclosure.
/// </summary>
internal sealed class SchemaConsultationTracker
{
    private readonly ConcurrentDictionary<string, byte> _consulted =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records that the given endpoint's schema was inspected.</summary>
    public void Record(string method, string templatePath)
    {
        var key = MakeKey(method, templatePath);
        if (key != null)
            _consulted[key] = 1;
    }

    /// <summary>Reports whether the given endpoint's schema was inspected this session.</summary>
    public bool WasConsulted(string method, string templatePath)
    {
        var key = MakeKey(method, templatePath);
        return key != null && _consulted.ContainsKey(key);
    }

    private static string MakeKey(string method, string templatePath)
    {
        if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(templatePath))
            return null;
        return method.Trim() + " " + templatePath.Trim();
    }
}
