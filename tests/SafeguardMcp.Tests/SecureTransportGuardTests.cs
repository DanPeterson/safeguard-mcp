#nullable disable

using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace SafeguardMcp.Tests
{
    /// <summary>
    /// Locks down the fail-closed HTTPS transport guard. In HTTP mode the
    /// Safeguard bearer travels in the <c>Authorization</c> header, so a
    /// non-HTTPS, non-loopback request must be refused unless the operator
    /// has explicitly opted out. These tests exercise the pure predicate
    /// <see cref="SecureTransportGuard.IsRequestAllowed"/> against a
    /// <see cref="DefaultHttpContext"/> shaped the way the pipeline sees a
    /// request after <c>UseForwardedHeaders</c> has run.
    /// </summary>
    public class SecureTransportGuardTests
    {
        private static HttpContext Context(string scheme, IPAddress remoteIp, string path = "/mcp")
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Scheme = scheme;
            ctx.Request.Path = path;
            ctx.Connection.RemoteIpAddress = remoteIp;
            return ctx;
        }

        [Fact]
        public void HttpsRequest_Allowed()
        {
            var ctx = Context("https", IPAddress.Parse("203.0.113.10"));
            Assert.True(SecureTransportGuard.IsRequestAllowed(ctx, allowInsecure: false));
        }

        [Fact]
        public void HttpFromLoopback_Allowed()
        {
            var ctx = Context("http", IPAddress.Loopback);
            Assert.True(SecureTransportGuard.IsRequestAllowed(ctx, allowInsecure: false));
        }

        [Fact]
        public void HttpFromIpv6Loopback_Allowed()
        {
            var ctx = Context("http", IPAddress.IPv6Loopback);
            Assert.True(SecureTransportGuard.IsRequestAllowed(ctx, allowInsecure: false));
        }

        [Fact]
        public void HttpFromRemote_Refused()
        {
            var ctx = Context("http", IPAddress.Parse("203.0.113.10"));
            Assert.False(SecureTransportGuard.IsRequestAllowed(ctx, allowInsecure: false));
        }

        [Fact]
        public void HttpFromRemote_AllowInsecure_Allowed()
        {
            var ctx = Context("http", IPAddress.Parse("203.0.113.10"));
            Assert.True(SecureTransportGuard.IsRequestAllowed(ctx, allowInsecure: true));
        }

        [Fact]
        public void HealthProbeOverHttpFromRemote_Allowed()
        {
            // Kubernetes probes hit /healthz over plain HTTP on the pod IP.
            var ctx = Context("http", IPAddress.Parse("10.1.2.3"), path: "/healthz");
            Assert.True(SecureTransportGuard.IsRequestAllowed(ctx, allowInsecure: false));
        }

        [Fact]
        public void HttpWithNullRemote_Refused()
        {
            var ctx = Context("http", remoteIp: null);
            Assert.False(SecureTransportGuard.IsRequestAllowed(ctx, allowInsecure: false));
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("TRUE", true)]
        [InlineData("false", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("yes", false)]
        public void IsAllowInsecure_ParsesEnv(string value, bool expected)
        {
            var env = new Dictionary<string, string> { [SecureTransportGuard.AllowInsecureEnvVar] = value };
            Assert.Equal(expected, SecureTransportGuard.IsAllowInsecure(k => env.TryGetValue(k, out var v) ? v : null));
        }
    }
}
