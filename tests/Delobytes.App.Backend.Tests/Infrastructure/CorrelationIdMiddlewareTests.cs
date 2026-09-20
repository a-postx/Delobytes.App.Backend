using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Middleware;
using Delobytes.App.Backend.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// Tests for <see cref="CorrelationIdMiddleware"/>.
/// </summary>
public class CorrelationIdMiddlewareTests
{
    private static CorrelationIdMiddleware BuildMiddleware(RequestDelegate next)
        => new CorrelationIdMiddleware(next);

    private static DefaultHttpContext BuildContext(string? incomingHeader = null)
    {
        DefaultHttpContext context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        if (incomingHeader is not null)
        {
            context.Request.Headers[CorrelationHeaders.CorrelationId] = incomingHeader;
        }

        return context;
    }

    private static Task InvokeAsync(CorrelationIdMiddleware middleware, HttpContext context)
        => middleware.InvokeAsync(context);

    [Fact]
    public async Task Invoke_AlwaysWritesCorrelationHeaderToResponse()
    {
        DefaultHttpContext context = BuildContext();

        await InvokeAsync(BuildMiddleware(_ => Task.CompletedTask), context);

        context.Response.Headers[CorrelationHeaders.CorrelationId].ToString()
            .Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public async Task Invoke_WithWellFormedIncomingHeader_EchoesTheSameValue()
    {
        DefaultHttpContext context = BuildContext("client-trace-42");

        await InvokeAsync(BuildMiddleware(_ => Task.CompletedTask), context);

        context.Response.Headers[CorrelationHeaders.CorrelationId].ToString()
            .Should().Be("client-trace-42");
    }

    [Fact]
    public async Task Invoke_WithMalformedIncomingHeader_DoesNotEchoIt()
    {
        DefaultHttpContext context = BuildContext("bad value with spaces");

        await InvokeAsync(BuildMiddleware(_ => Task.CompletedTask), context);

        context.Response.Headers[CorrelationHeaders.CorrelationId].ToString()
            .Should().NotBe("bad value with spaces");
    }

    [Fact]
    public async Task Invoke_StoresCorrelationIdInHttpContextItems()
    {
        DefaultHttpContext context = BuildContext("pinned-id");

        await InvokeAsync(BuildMiddleware(_ => Task.CompletedTask), context);

        context.Items[CorrelationIdProvider.HttpContextItemKey].Should().Be("pinned-id");
    }

    [Fact]
    public async Task Invoke_CorrelationIdIsAvailableToDownstreamMiddleware()
    {
        DefaultHttpContext context = BuildContext();
        string? observed = null;

        await InvokeAsync(
            BuildMiddleware(ctx =>
            {
                observed = ctx.Items[CorrelationIdProvider.HttpContextItemKey] as string;
                return Task.CompletedTask;
            }),
            context);

        observed.Should().NotBeNull();
        observed.Should().Be(context.Response.Headers[CorrelationHeaders.CorrelationId].ToString());
    }

    [Fact]
    public async Task Invoke_HeaderIsSetBeforeTheRestOfThePipelineRuns()
    {
        // This is what makes the header survive responses produced by the framework itself,
        // such as a routing 404 that never reaches application code.
        DefaultHttpContext context = BuildContext();
        bool headerPresentDownstream = false;

        await InvokeAsync(
            BuildMiddleware(ctx =>
            {
                headerPresentDownstream = ctx.Response.Headers.ContainsKey(CorrelationHeaders.CorrelationId);
                return Task.CompletedTask;
            }),
            context);

        headerPresentDownstream.Should().BeTrue();
    }

    [Fact]
    public async Task Invoke_WhenDownstreamThrows_HeaderIsStillPresent()
    {
        DefaultHttpContext context = BuildContext("survives-failure");

        // InvokeAsync deliberately does not catch: ExceptionHandlingMiddleware sits above and is
        // responsible for turning this into a response. The header must already be on the response.
        Func<Task> act = () => InvokeAsync(
            BuildMiddleware(_ => throw new InvalidOperationException("boom")),
            context);

        await act.Should().ThrowAsync<InvalidOperationException>();
        context.Response.Headers[CorrelationHeaders.CorrelationId].ToString().Should().Be("survives-failure");
    }

    [Fact]
    public async Task Invoke_AlwaysCallsNextMiddleware()
    {
        DefaultHttpContext context = BuildContext();
        bool nextCalled = false;

        await InvokeAsync(
            BuildMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }),
            context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Invoke_WithAlreadyStartedResponse_DoesNotThrow()
    {
        DefaultHttpContext context = BuildContext();
        await context.Response.StartAsync();

        Func<Task> act = () => InvokeAsync(BuildMiddleware(_ => Task.CompletedTask), context);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Invoke_TwoRequests_ProduceIndependentIdentifiers()
    {
        DefaultHttpContext first = BuildContext();
        DefaultHttpContext second = BuildContext();

        await InvokeAsync(BuildMiddleware(_ => Task.CompletedTask), first);
        await InvokeAsync(BuildMiddleware(_ => Task.CompletedTask), second);

        first.Response.Headers[CorrelationHeaders.CorrelationId].ToString()
            .Should().NotBe(second.Response.Headers[CorrelationHeaders.CorrelationId].ToString());
    }
}
