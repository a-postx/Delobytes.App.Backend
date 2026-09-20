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
    public async Task TwoParallelRequests_CorrelationIdsDoNotMix()
    {
        // Both requests must share ONE logger and ONE sink. Using separate pipelines would be
        // trivially correct — each logger writes only to its own sink, so nothing can bleed.
        // Here, a single Serilog instance receives events from both concurrent async flows, and the
        // only thing separating them is the AsyncLocal stack maintained by LogContext. If the push
        // and pop happen on the wrong continuations the wrong ID ends up on an event.
        string firstId = "parallel-first-aaa";
        string secondId = "parallel-second-bbb";

        int arrivedCount = 0;
        TaskCompletionSource<bool> barrier = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using CorrelationPipeline pipeline = CorrelationPipeline.Create(async ctx =>
        {
            // Hold both requests inside CorrelationIdMiddleware at the same time so the two
            // LogContext scopes genuinely overlap on the thread pool.
            if (Interlocked.Increment(ref arrivedCount) == 2)
            {
                barrier.TrySetResult(true);
            }

            await barrier.Task;

            ctx.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Delobytes.Handler")
                .LogInformation("downstream ran");
        });

        await Task.WhenAll(
            pipeline.InvokeAsync(BuildContext(firstId)),
            pipeline.InvokeAsync(BuildContext(secondId)));

        List<LogEvent> allEvents = pipeline.Sink.Events;
        allEvents.Should().NotBeEmpty();

        List<LogEvent> withFirst = allEvents
            .Where(e => e.Properties.ContainsKey(LoggingLogKeys.CorrelationId) &&
                        e.Properties[LoggingLogKeys.CorrelationId].ToString().Contains(firstId))
            .ToList();

        List<LogEvent> withSecond = allEvents
            .Where(e => e.Properties.ContainsKey(LoggingLogKeys.CorrelationId) &&
                        e.Properties[LoggingLogKeys.CorrelationId].ToString().Contains(secondId))
            .ToList();

        withFirst.Should().NotBeEmpty("first request must produce at least one log entry");
        withSecond.Should().NotBeEmpty("second request must produce at least one log entry");

        // Every event must belong to exactly one of the two requests. A mismatch means the
        // AsyncLocal scopes bled across concurrent async flows.
        allEvents.Should().HaveCount(
            withFirst.Count + withSecond.Count,
            "every log event must carry exactly one of the two request correlation IDs");
    }

    [Fact]
    public async Task LogEntriesWrittenAfterRequestCompletes_DoNotCarryCorrelationId()
    {
        // LogContext.PushProperty is scoped to the using block inside InvokeAsync. Once the
        // middleware returns the property must be absent so stale identifiers cannot appear in
        // unrelated work that runs on the same thread or task after the request ends.
        using CorrelationPipeline pipeline = CorrelationPipeline.Create(_ => Task.CompletedTask);

        DefaultHttpContext context = BuildContext("scoped-id");
        await pipeline.InvokeAsync(context);

        int countAfterRequest = pipeline.Sink.Events.Count;

        pipeline.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Delobytes.OutsideScope")
            .LogInformation("written after request completed");

        List<LogEvent> externalEvents = pipeline.Sink.Events.Skip(countAfterRequest).ToList();

        externalEvents.Should().NotBeEmpty();
        externalEvents.Should().NotContain(
            e => e.Properties.ContainsKey(LoggingLogKeys.CorrelationId) &&
                 e.Properties[LoggingLogKeys.CorrelationId].ToString().Contains("scoped-id"),
            "correlation id must not appear in log entries written after the request scope ends");
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

        /// <summary>
        /// Exposes the container so tests can resolve services (e.g. ILoggerFactory) to emit
        /// log entries outside the request scope and verify they carry no correlation id.
        /// </summary>
        public IServiceProvider Services => _provider;

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
        private readonly object _lock = new object();
        private readonly List<LogEvent> _events = new List<LogEvent>();

        public List<LogEvent> Events
        {
            get
            {
                lock (_lock)
                {
                    return new List<LogEvent>(_events);
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_lock)
            {
                _events.Add(logEvent);
            }
        }
    }
}
