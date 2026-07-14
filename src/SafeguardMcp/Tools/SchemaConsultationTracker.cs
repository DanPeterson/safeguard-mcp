using System;
using System.Collections.Generic;

namespace SafeguardMcp.Tools;

/// <summary>
/// Best-effort, per-session record of which endpoints the agent has inspected
/// with <see cref="SafeguardApiTool.Safeguard_Schema"/>. Used only to shape error
/// hint text: when a write (POST/PUT/PATCH) returns HTTP 400 and its endpoint was
/// never schema-inspected, the error path prepends a directive to read the schema
/// (which carries required + conditional fields) before retrying.
///
/// Registered as a singleton in both transports and partitioned by MCP session id
/// so consultation memory persists across a session's tool calls yet stays isolated
/// between different sessions/tenants. In stdio there is a single session, so a blank
/// session id collapses to <see cref="DefaultSessionId"/>. It stores only endpoint
/// template strings — never request bodies or credentials — so the worst case of any
/// mismatch is a suppressed or extra hint, never data disclosure.
///
/// Memory is bounded: at most <see cref="MaxSessions"/> sessions are retained (the
/// least-recently-used session is evicted when the cap is exceeded) and at most
/// <see cref="MaxEntriesPerSession"/> endpoints per session, so a long-running HTTP
/// process cannot grow without limit.
/// </summary>
internal sealed class SchemaConsultationTracker
{
    /// <summary>Stable session id used when no MCP session id is available (stdio).</summary>
    internal const string DefaultSessionId = "__default__";

    /// <summary>Maximum number of distinct sessions retained before LRU eviction.</summary>
    internal const int MaxSessions = 512;

    /// <summary>Maximum number of endpoint entries remembered per session.</summary>
    internal const int MaxEntriesPerSession = 256;

    private readonly object _gate = new();

    // sessionId -> set of "METHOD /template/path" keys. A plain dictionary under a lock
    // keeps the LRU bookkeeping simple and correct; consultation traffic is low-volume
    // relative to appliance I/O.
    private readonly Dictionary<string, HashSet<string>> _sessions =
        new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lru = new();
    private readonly Dictionary<string, LinkedListNode<string>> _lruNodes =
        new(StringComparer.Ordinal);

    /// <summary>Records that the given endpoint's schema was inspected in the session.</summary>
    public void Record(string sessionId, string method, string templatePath)
    {
        var key = MakeKey(method, templatePath);
        if (key == null)
            return;

        var session = NormalizeSessionId(sessionId);
        lock (_gate)
        {
            if (!_sessions.TryGetValue(session, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _sessions[session] = set;
                Touch(session);
                EvictSessionsIfNeeded();
            }
            else
            {
                Touch(session);
            }

            if (set.Add(key) && set.Count > MaxEntriesPerSession)
            {
                // Best-effort cap: drop an arbitrary existing entry. Losing an entry
                // only risks an extra hint, never a wrong action.
                foreach (var existing in set)
                {
                    if (!string.Equals(existing, key, StringComparison.OrdinalIgnoreCase))
                    {
                        set.Remove(existing);
                        break;
                    }
                }
            }
        }
    }

    /// <summary>Reports whether the given endpoint's schema was inspected in the session.</summary>
    public bool WasConsulted(string sessionId, string method, string templatePath)
    {
        var key = MakeKey(method, templatePath);
        if (key == null)
            return false;

        var session = NormalizeSessionId(sessionId);
        lock (_gate)
        {
            if (!_sessions.TryGetValue(session, out var set) || !set.Contains(key))
                return false;
            Touch(session);
            return true;
        }
    }

    private void Touch(string session)
    {
        if (_lruNodes.TryGetValue(session, out var node))
        {
            _lru.Remove(node);
            _lru.AddLast(node);
        }
        else
        {
            _lruNodes[session] = _lru.AddLast(session);
        }
    }

    private void EvictSessionsIfNeeded()
    {
        while (_sessions.Count > MaxSessions && _lru.First != null)
        {
            var oldest = _lru.First.Value;
            _lru.RemoveFirst();
            _lruNodes.Remove(oldest);
            _sessions.Remove(oldest);
        }
    }

    private static string NormalizeSessionId(string sessionId)
        => string.IsNullOrWhiteSpace(sessionId) ? DefaultSessionId : sessionId;

    private static string MakeKey(string method, string templatePath)
    {
        if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(templatePath))
            return null;
        return method.Trim() + " " + templatePath.Trim();
    }
}
