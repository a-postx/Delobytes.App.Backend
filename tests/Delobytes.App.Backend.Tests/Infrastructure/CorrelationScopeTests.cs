using Delobytes.App.Backend.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Serilog.Core;
using Serilog.Events;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// Tests for <see cref="CorrelationScope"/>, the Variant-B helper background services use to
/// start a fresh correlation chain for a unit of work that has no ambient one to inherit.
/// </summary>
public class CorrelationScopeTests
{
    private static CorrelationContext BuildCorrelationContext()
    {
        Mock<IHttpContextAccessor> accessorMock = new Mock<IHttpContextAccessor>();
        accessorMock.SetupGet(a => a.HttpContext).Returns((HttpContext?)null);

        return new CorrelationContext(accessorMock.Object);
    }

    [Fact]
    public void BeginNew_MakesANonEmptyIdAmbientThroughCorrelationContext()
    {
        CorrelationContext correlationContext = BuildCorrelationContext();
        CorrelationScope scope = new CorrelationScope(correlationContext);

        using (scope.BeginNew())
        {
            correlationContext.CorrelationId.Should().NotBeNull();
            correlationContext.CorrelationId.Should().NotBe(Guid.Empty);
        }
    }

    [Fact]
    public void Dispose_ClearsTheAmbientIdWhenThereWasNonePrior()
    {
        CorrelationContext correlationContext = BuildCorrelationContext();
        CorrelationScope scope = new CorrelationScope(correlationContext);

        IDisposable handle = scope.BeginNew();
        handle.Dispose();

        // A leaked identifier would make unrelated work that runs afterwards on the same scope
        // look like part of a chain that has already ended.
        correlationContext.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void Dispose_RestoresWhateverWasAmbientBeforeBeginNew()
    {
        // Safe-to-nest guarantee: a future caller wrapping several BeginNew() calls inside an
        // outer one must get the outer id back, not null, once the inner one ends.
        CorrelationContext correlationContext = BuildCorrelationContext();
        CorrelationScope scope = new CorrelationScope(correlationContext);

        Guid outerId = Guid.NewGuid();
        correlationContext.SetCorrelationId(outerId);

        using (scope.BeginNew())
        {
            correlationContext.CorrelationId.Should().NotBe(outerId);
        }

        correlationContext.CorrelationId.Should().Be(outerId);
    }

    [Fact]
    public void TwoConsecutiveScopes_ProduceIndependentIdentifiers()
    {
        CorrelationContext correlationContext = BuildCorrelationContext();
        CorrelationScope scope = new CorrelationScope(correlationContext);

        Guid? first;
        using (scope.BeginNew())
        {
            first = correlationContext.CorrelationId;
        }

        Guid? second;
        using (scope.BeginNew())
        {
            second = correlationContext.CorrelationId;
        }

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first.Should().NotBe((Guid)second!);
    }

    [Fact]
    public void AllLogEntriesWrittenInsideTheScope_CarryTheCorrelationId()
    {
        CorrelationContext correlationContext = BuildCorrelationContext();
        CorrelationScope scope = new CorrelationScope(correlationContext);

        CapturingSink sink = new CapturingSink();
        Serilog.ILogger serilogLogger = new Serilog.LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Information()
            .WriteTo.Sink(sink)
            .CreateLogger();

        Guid? observedId;
        using (IDisposable handle = scope.BeginNew())
        {
            observedId = correlationContext.CorrelationId;
            serilogLogger.Information("work done inside the scope");
        }

        serilogLogger.Information("work done after the scope ended");

        sink.Events.Should().HaveCount(2);

        sink.Events[0].Properties.Should().ContainKey("CorrelationId");
        sink.Events[0].Properties["CorrelationId"].ToString().Should().Contain(observedId.ToString()!);

        sink.Events[1].Properties.Should().NotContainKey("CorrelationId");
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrowAndDoesNotDoubleRestore()
    {
        CorrelationContext correlationContext = BuildCorrelationContext();
        CorrelationScope scope = new CorrelationScope(correlationContext);

        IDisposable handle = scope.BeginNew();

        Action act = () =>
        {
            handle.Dispose();
            handle.Dispose();
        };

        act.Should().NotThrow();
        correlationContext.CorrelationId.Should().BeNull();
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new List<LogEvent>();

        public void Emit(LogEvent logEvent)
        {
            Events.Add(logEvent);
        }
    }
}
