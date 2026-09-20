using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Middleware;
using Delobytes.AspNetCore.Logging;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// Verifies that the identifier established by <see cref="CorrelationIdMiddleware"/> reaches Serilog
/// log events, not just the HTTP response header.
/// The middleware tests that use NullLogger cannot check this: NullLogger discards every call, so an
/// entry written outside the LogContext scope looks exactly like one written inside it. These tests
/// wire up a real Serilog pipeline with LogContext enrichment and an in-memory sink, and activate the
/// middleware through ApplicationBuilder so the test does not depend on where the logger is injected.
/// </summary>
public class CorrelationIdMiddlewareLoggingTests
{
    [Fact]
    public async Task AllLogEntriesWrittenDuringRequest_CarryCorrelationId()
    {
        // The identifier is only attached to events emitted while the property is pushed. An entry
        // written outside that scope is invisible in the log index, which makes the value shown to
        // the user useless for support.
        using CorrelationPipeline pipeline = CorrelationPipeline.Create(context =>
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Delobytes.App.Backend.Catalog.SomeHandler")
                .LogInformation("downstream handler ran");

            return Task.CompletedTask;
        });

        DefaultHttpContext context = BuildContext("support-ticket-id");

        await pipeline.InvokeAsync(context);

        AssertAllEntriesCarry(pipeline.Sink.Events, "support-ticket-id");
    }

    [Fact]
    public async Task WellFormedIncomingHeader_AppearsVerbatimInLogEntries()
    {
        using CorrelationPipeline pipeline = CorrelationPipeline.Create(_ => Task.CompletedTask);

        DefaultHttpContext context = BuildContext("client-supplied-id");

        await pipeline.InvokeAsync(context);

        pipeline.Sink.Events.Should().NotBeEmpty();
        AssertAllEntriesCarry(pipeline.Sink.Events, "client-supplied-id");

        // The value in the log, in HttpContext.Items and in the response header must be one value,
        // otherwise support greps for an identifier that exists in only one of the three places.
        pipeline.Sink.Events
            .Select(e => e.Properties[LoggingLogKeys.CorrelationId].ToString().Trim('"'))
            .Distinct()
            .Should().ContainSingle()
            .Which.Should().Be(context.Response.Headers[CorrelationHeaders.CorrelationId].ToString());
    }

    [Fact]
    public async Task MalformedIncomingHeader_IsReplacedConsistentlyInLogsAndHeader()
    {
        using CorrelationPipeline pipeline = CorrelationPipeline.Create(_ => Task.CompletedTask);

        DefaultHttpContext context = BuildContext("bad value with spaces");

        await pipeline.InvokeAsync(context);

        pipeline.Sink.Events.Should().NotBeEmpty();
        pipeline.Sink.Events
            .Should().NotContain(e => e.Properties[LoggingLogKeys.CorrelationId].ToString().Contains("bad value with spaces"));

        context.Response.Headers[CorrelationHeaders.CorrelationId].ToString()
            .Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public async Task TwoSequentialRequests_DoNotLeakIdentifierBetweenLogScopes()
    {
        // LogContext is AsyncLocal backed. A leaked property would make entries from the second
        // request look like they belong to the first chain.
        using CorrelationPipeline pipeline = CorrelationPipeline.Create(_ => Task.CompletedTask);

        await pipeline.InvokeAsync(BuildContext("first-request"));
        int eventsAfterFirst = pipeline.Sink.Events.Count;

        await pipeline.InvokeAsync(BuildContext("second-request"));

        List<LogEvent> firstRequestEvents = pipeline.Sink.Events.Take(eventsAfterFirst).ToList();
        List<LogEvent> secondRequestEvents = pipeline.Sink.Events.Skip(eventsAfterFirst).ToList();

        AssertAllEntriesCarry(firstRequestEvents, "first-request");
        AssertAllEntriesCarry(secondRequestEvents, "second-request");
    }

    [Fact]
    public async Task MiddlewareOwnCompletionEntry_CarriesCorrelationId()
    {
        // This is the line an operator would search for after a failure. If the middleware does not
        // log a completion entry at all, no event carries the StatusCode key and this test fails,
        // which is the intended signal rather than a false pass.
        using CorrelationPipeline pipeline = CorrelationPipeline.Create(_ => Task.CompletedTask);

        DefaultHttpContext context = BuildContext("completion-entry-id");
        context.Request.Method = "GET";
        context.Request.Path = "/api/catalogs/work-rates";

        await pipeline.InvokeAsync(context);

        List<LogEvent> completionEntries = pipeline.Sink.Events
            .Where(e => e.Properties.ContainsKey(LoggingLogKeys.StatusCode))
            .ToList();

        AssertAllEntriesCarry(completionEntries, "completion-entry-id");
    }

    private static void AssertAllEntriesCarry(List<LogEvent> events, string expected)
    {
        events.Should().NotBeEmpty();

        List<LogEvent> withProperty = events
            .Where(e => e.Properties.ContainsKey(LoggingLogKeys.CorrelationId))
            .ToList();

        withProperty.Should().HaveCount(
            events.Count,
            "every entry written during a request must carry the correlation id, otherwise it cannot be found by the value shown to the user");

        withProperty.Should().OnlyContain(
            e => e.Properties[LoggingLogKeys.CorrelationId].ToString().Contains(expected));
    }

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

    /// <summary>
    /// Activates <see cref="CorrelationIdMiddleware"/> the way the host does, so the test stays
    /// valid whether the middleware takes its logger through the constructor or through
    /// InvokeAsync, and asserts against a real Serilog pipeline rather than a null logger.
    /// </summary>
    private sealed class CorrelationPipeline : IDisposable
    {
        private readonly ServiceProvider _provider;

        private CorrelationPipeline(
            RequestDelegate middleware,
            CapturingSink sink,
            ServiceProvider provider)
        {
            Middleware = middleware;
            Sink = sink;
            _provider = provider;
        }

        public RequestDelegate Middleware { get; }

        public CapturingSink Sink { get; }

        public static CorrelationPipeline Create(RequestDelegate downstream)
        {
            CapturingSink sink = new CapturingSink();

            Serilog.ILogger serilogLogger = new Serilog.LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Information()
                .WriteTo.Sink(sink)
                .CreateLogger();

            ServiceCollection services = new ServiceCollection();

            // Must be registered before AddLogging: the latter only fills in ILoggerFactory when
            // nothing else provided one, and Logger<T> then resolves through this instance.
            services.AddSingleton<ILoggerFactory>(new SerilogLoggerFactory(serilogLogger, dispose: true));
            services.AddLogging();

            ServiceProvider provider = services.BuildServiceProvider();

            ApplicationBuilder builder = new ApplicationBuilder(provider);
            builder.UseMiddleware<CorrelationIdMiddleware>();
            builder.Run(downstream);

            return new CorrelationPipeline(builder.Build(), sink, provider);
        }

        public Task InvokeAsync(HttpContext context)
        {
            // ApplicationBuilder alone does not populate RequestServices; in a real host that is
            // done by the request services container middleware.
            context.RequestServices = _provider;

            return Middleware(context);
        }

        public void Dispose()
        {
            _provider.Dispose();
        }
    }

    /// <summary>
    /// Serilog sink that keeps emitted events in memory so assertions can inspect the final,
    /// enriched property set instead of the message template.
    /// </summary>
    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new List<LogEvent>();

        public void Emit(LogEvent logEvent)
        {
            Events.Add(logEvent);
        }
    }
}
