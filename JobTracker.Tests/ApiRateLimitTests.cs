using JobTracker.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JobTracker.Tests;

public class ApiRateLimitTests
{
    private ApiRateLimitMiddleware CreateMiddleware(RequestDelegate next, int maxRequests = 5, int windowSeconds = 60)
    {
        var logger = NullLogger<ApiRateLimitMiddleware>.Instance;
        return new ApiRateLimitMiddleware(next, logger, maxRequests, windowSeconds);
    }

    [Fact]
    public async Task NonApiPath_PassesThrough()
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });

        var context = new DefaultHttpContext();
        context.Request.Path = "/some-page";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;

        await middleware.InvokeAsync(context);
        Assert.True(called);
    }

    [Fact]
    public async Task ApiPath_UnderLimit_PassesThrough()
    {
        var callCount = 0;
        var middleware = CreateMiddleware(_ => { callCount++; return Task.CompletedTask; }, maxRequests: 5);

        for (int i = 0; i < 5; i++)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/jobs";
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
            context.Response.Body = new MemoryStream();
            await middleware.InvokeAsync(context);
        }

        Assert.Equal(5, callCount);
    }

    [Fact]
    public async Task ApiPath_OverLimit_Returns429()
    {
        var callCount = 0;
        var middleware = CreateMiddleware(_ => { callCount++; return Task.CompletedTask; }, maxRequests: 3);

        HttpContext? lastContext = null;
        for (int i = 0; i < 5; i++)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/jobs";
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.100");
            context.Response.Body = new MemoryStream();
            await middleware.InvokeAsync(context);
            lastContext = context;
        }

        Assert.Equal(3, callCount); // Only first 3 should pass through
        Assert.Equal(429, lastContext!.Response.StatusCode);
    }

    [Fact]
    public async Task DifferentClients_HaveSeparateLimits()
    {
        var callCount = 0;
        var middleware = CreateMiddleware(_ => { callCount++; return Task.CompletedTask; }, maxRequests: 2);

        // Client 1: 2 requests
        for (int i = 0; i < 2; i++)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/jobs";
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");
            context.Response.Body = new MemoryStream();
            await middleware.InvokeAsync(context);
        }

        // Client 2: 2 requests
        for (int i = 0; i < 2; i++)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/jobs";
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.2");
            context.Response.Body = new MemoryStream();
            await middleware.InvokeAsync(context);
        }

        Assert.Equal(4, callCount); // All should pass — different clients
    }

    [Fact]
    public async Task XForwardedFor_UsedAsClientId()
    {
        var callCount = 0;
        var middleware = CreateMiddleware(_ => { callCount++; return Task.CompletedTask; }, maxRequests: 1);

        // First request with forwarded IP
        var context1 = new DefaultHttpContext();
        context1.Request.Path = "/api/jobs";
        context1.Request.Headers["X-Forwarded-For"] = "203.0.113.50";
        context1.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        context1.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context1);

        // Second request — same forwarded IP, should be rate-limited
        var context2 = new DefaultHttpContext();
        context2.Request.Path = "/api/jobs";
        context2.Request.Headers["X-Forwarded-For"] = "203.0.113.50";
        context2.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        context2.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context2);

        Assert.Equal(1, callCount);
        Assert.Equal(429, context2.Response.StatusCode);
    }
}
