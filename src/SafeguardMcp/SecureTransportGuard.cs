using System.Net;
using Microsoft.AspNetCore.Http;

namespace SafeguardMcp
{
    /// <summary>
    /// Fails closed on plaintext transport in HTTP mode. The Safeguard
    /// bearer travels in the <c>Authorization</c> header, so a
    /// non-HTTPS client&#8596;server hop would expose that credential in
    /// cleartext. A request is allowed only when its effective scheme
    /// (after <c>UseForwardedHeaders</c> has applied a trusted
    /// <c>X-Forwarded-Proto</c>) is HTTPS, it originates from loopback
    /// (local development / same-host proxy), it targets the health
    /// probe, or the operator has explicitly opted out via
    /// <c>MCP_ALLOW_INSECURE_HTTP=true</c>.
    /// </summary>
    internal static class SecureTransportGuard
    {
        public const string AllowInsecureEnvVar = "MCP_ALLOW_INSECURE_HTTP";

        // Kubernetes liveness/readiness probes reach the pod over plain
        // HTTP on the pod IP, so the health endpoint is never gated.
        private const string HealthPath = "/healthz";

        public static bool IsAllowInsecure(Func<string, string> getEnv)
            => bool.TryParse(getEnv(AllowInsecureEnvVar), out var value) && value;

        public static bool IsRequestAllowed(HttpContext context, bool allowInsecure)
        {
            if (context.Request.Path.StartsWithSegments(HealthPath))
                return true;
            if (allowInsecure)
                return true;
            if (context.Request.IsHttps)
                return true;
            var remote = context.Connection.RemoteIpAddress;
            return remote != null && IPAddress.IsLoopback(remote);
        }
    }
}
