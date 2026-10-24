using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Filters;
using Delobytes.App.Backend.Services;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Delobytes.App.Backend.Tests.Messaging;

/// <summary>
/// Tests for <see cref="CorrelationPublishFilter{T}"/> and <see cref="CorrelationConsumeFilter{T}"/>.
/// </summary>
public class CorrelationFiltersTests
{
    private readonly Mock<ICorrelationContext> _correlationContextMock;
    private readonly Mock<PublishContext<TestMessage>> _publishContextMock;
    private readonly Mock<SendHeaders> _publishHeadersMock;
    private readonly Mock<IPipe<PublishContext<TestMessage>>> _nextPublishPipeMock;
    private readonly CorrelationPublishFilter<TestMessage> _publishFilter;

    private readonly CorrelationContext _correlationContext;
    private readonly Mock<ConsumeContext<TestMessage>> _consumeContextMock;
    private readonly Mock<Headers> _consumeHeadersMock;
    private readonly Mock<IPipe<ConsumeContext<TestMessage>>> _nextConsumePipeMock;
    private readonly CorrelationConsumeFilter<TestMessage> _consumeFilter;

    public CorrelationFiltersTests()
    {
        _correlationContextMock = new Mock<ICorrelationContext>();
        _publishContextMock = new Mock<PublishContext<TestMessage>>();
        _publishHeadersMock = new Mock<SendHeaders>();
        _nextPublishPipeMock = new Mock<IPipe<PublishContext<TestMessage>>>();

        _publishContextMock.Setup(c => c.Headers).Returns(_publishHeadersMock.Object);
        _nextPublishPipeMock
            .Setup(p => p.Send(It.IsAny<PublishContext<TestMessage>>()))
            .Returns(Task.CompletedTask);

        _publishFilter = new CorrelationPublishFilter<TestMessage>(_correlationContextMock.Object);

        Mock<IHttpContextAccessor> accessorMock = new Mock<IHttpContextAccessor>();
        accessorMock.SetupGet(a => a.HttpContext).Returns((HttpContext?)null);
        _correlationContext = new CorrelationContext(accessorMock.Object);

        _consumeContextMock = new Mock<ConsumeContext<TestMessage>>();
        _consumeHeadersMock = new Mock<Headers>();
        _nextConsumePipeMock = new Mock<IPipe<ConsumeContext<TestMessage>>>();

        _consumeContextMock.Setup(c => c.Headers).Returns(_consumeHeadersMock.Object);
        _nextConsumePipeMock
            .Setup(p => p.Send(It.IsAny<ConsumeContext<TestMessage>>()))
            .Returns(Task.CompletedTask);

        _consumeFilter = new CorrelationConsumeFilter<TestMessage>(_correlationContext);
    }

    [Fact]
    public async Task Publish_WithExistingCorrelationId_PropagatesItIntoHeaders()
    {
        Guid chainId = Guid.NewGuid();
        _correlationContextMock.Setup(c => c.CorrelationId).Returns(chainId);

        await _publishFilter.Send(_publishContextMock.Object, _nextPublishPipeMock.Object);

        _publishHeadersMock.Verify(
            h => h.Set(CorrelationHeaders.CorrelationId, chainId.ToString()),
            Times.Once);
    }

    [Fact]
    public async Task Publish_WithEmptyNativeCorrelationId_SynchronisesItWithOurs()
    {
        // No saga claims PublishContext.CorrelationId yet in this project, so when it is empty it
        // is free to mirror our own identifier — making the chain visible to broker-level tooling
        // and fault messages without a second, project-specific enricher.
        Guid chainId = Guid.NewGuid();
        _correlationContextMock.Setup(c => c.CorrelationId).Returns(chainId);

        await _publishFilter.Send(_publishContextMock.Object, _nextPublishPipeMock.Object);

        _publishContextMock.VerifySet(c => c.CorrelationId = chainId, Times.Once);
    }

    [Fact]
    public async Task Publish_WithNativeCorrelationIdAlreadySet_DoesNotOverwriteIt()
    {
        // A saga's message send topology (CorrelatedBy<Guid>) or an explicit CorrelateById may have
        // already set this field to the business id it uses to locate its instance; our chain id
        // must not clobber it. The header still carries our id regardless — that is a separate,
        // unrelated mechanism.
        Guid sagaId = Guid.NewGuid();
        _publishContextMock.SetupGet(c => c.CorrelationId).Returns(sagaId);
        _correlationContextMock.Setup(c => c.CorrelationId).Returns(Guid.NewGuid());

        await _publishFilter.Send(_publishContextMock.Object, _nextPublishPipeMock.Object);

        _publishContextMock.VerifySet(c => c.CorrelationId = It.IsAny<Guid?>(), Times.Never);
        _publishHeadersMock.Verify(
            h => h.Set(CorrelationHeaders.CorrelationId, It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task Publish_WithoutCorrelationId_GeneratesOne()
    {
        _correlationContextMock.Setup(c => c.CorrelationId).Returns((Guid?)null);

        await _publishFilter.Send(_publishContextMock.Object, _nextPublishPipeMock.Object);

        // Never publishes a message that carries no correlation id: startup events and other
        // out-of-band publishes must still be traceable.
        _publishHeadersMock.Verify(
            h => h.Set(CorrelationHeaders.CorrelationId, It.IsAny<string>()),
            Times.Once);

        _publishContextMock.VerifySet(c => c.CorrelationId = It.IsAny<Guid?>(), Times.Once);
    }

    [Fact]
    public async Task Publish_WithEmptyGuidCorrelationId_GeneratesSubstitute()
    {
        _correlationContextMock.Setup(c => c.CorrelationId).Returns((Guid?)Guid.Empty);

        await _publishFilter.Send(_publishContextMock.Object, _nextPublishPipeMock.Object);

        _publishHeadersMock.Verify(
            h => h.Set(CorrelationHeaders.CorrelationId, Guid.Empty.ToString()),
            Times.Never);
    }

    [Fact]
    public async Task Publish_AlwaysCallsNextPipe()
    {
        _correlationContextMock.Setup(c => c.CorrelationId).Returns(Guid.NewGuid());

        await _publishFilter.Send(_publishContextMock.Object, _nextPublishPipeMock.Object);

        _nextPublishPipeMock.Verify(p => p.Send(_publishContextMock.Object), Times.Once);
    }

    [Fact]
    public void Publish_Probe_RegistersFilterScope()
    {
        Mock<ProbeContext> probeMock = new Mock<ProbeContext>();
        Mock<ProbeContext> scopeMock = new Mock<ProbeContext>();

        probeMock.Setup(p => p.CreateScope("filters")).Returns(scopeMock.Object);

        _publishFilter.Probe(probeMock.Object);

        scopeMock.Verify(p => p.Add("filterType", "correlationPublish"), Times.Once);
    }

    [Fact]
    public async Task Consume_WithHeaderPresent_MakesItAvailableDuringPipeExecution()
    {
        Guid chainId = Guid.NewGuid();
        Guid? observedInsidePipe = null;
        _consumeHeadersMock
            .Setup(h => h.Get<string>(CorrelationHeaders.CorrelationId, It.IsAny<string?>()))
            .Returns(chainId.ToString());

        _nextConsumePipeMock
            .Setup(p => p.Send(It.IsAny<ConsumeContext<TestMessage>>()))
            .Returns(() =>
            {
                observedInsidePipe = _correlationContext.CorrelationId;
                return Task.CompletedTask;
            });

        await _consumeFilter.Send(_consumeContextMock.Object, _nextConsumePipeMock.Object);

        observedInsidePipe.Should().Be(chainId);
    }

    [Fact]
    public async Task Consume_ClearsContextAfterPipeExecution()
    {
        _consumeHeadersMock
            .Setup(h => h.Get<string>(CorrelationHeaders.CorrelationId, It.IsAny<string?>()))
            .Returns(Guid.NewGuid().ToString());

        await _consumeFilter.Send(_consumeContextMock.Object, _nextConsumePipeMock.Object);

        // A leaked identifier would make the next message handled by this scope look like part of
        // an unrelated chain.
        _correlationContext.CorrelationId.Should().BeNull();
    }

    [Fact]
    public async Task Consume_WhenPipeThrows_StillClearsContext()
    {
        _consumeHeadersMock
            .Setup(h => h.Get<string>(CorrelationHeaders.CorrelationId, It.IsAny<string?>()))
            .Returns(Guid.NewGuid().ToString());
        _nextConsumePipeMock
            .Setup(p => p.Send(It.IsAny<ConsumeContext<TestMessage>>()))
            .ThrowsAsync(new InvalidOperationException("consumer failed"));

        Func<Task> act = () => _consumeFilter.Send(_consumeContextMock.Object, _nextConsumePipeMock.Object);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _correlationContext.CorrelationId.Should().BeNull();
    }

    [Fact]
    public async Task Consume_WithoutHeader_GeneratesOneSoConsumerLogsAreTraceable()
    {
        Guid? observedInsidePipe = null;
        _consumeHeadersMock
            .Setup(h => h.Get<string>(CorrelationHeaders.CorrelationId, It.IsAny<string?>()))
            .Returns((string?)null);

        _nextConsumePipeMock
            .Setup(p => p.Send(It.IsAny<ConsumeContext<TestMessage>>()))
            .Returns(() =>
            {
                observedInsidePipe = _correlationContext.CorrelationId;
                return Task.CompletedTask;
            });

        await _consumeFilter.Send(_consumeContextMock.Object, _nextConsumePipeMock.Object);

        observedInsidePipe.Should().NotBeNull();
        observedInsidePipe.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Consume_WithMalformedHeader_GeneratesSubstituteInstead()
    {
        // The header on the wire is still a plain string, so a legacy or mis-instrumented producer
        // can still send something that is not a GUID — it must not be parsed as one.
        Guid? observedInsidePipe = null;
        _consumeHeadersMock
            .Setup(h => h.Get<string>(CorrelationHeaders.CorrelationId, It.IsAny<string?>()))
            .Returns("bad value with spaces");

        _nextConsumePipeMock
            .Setup(p => p.Send(It.IsAny<ConsumeContext<TestMessage>>()))
            .Returns(() =>
            {
                observedInsidePipe = _correlationContext.CorrelationId;
                return Task.CompletedTask;
            });

        await _consumeFilter.Send(_consumeContextMock.Object, _nextConsumePipeMock.Object);

        observedInsidePipe.Should().NotBeNull();
        observedInsidePipe.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Consume_AlwaysCallsNextPipe()
    {
        _consumeHeadersMock
            .Setup(h => h.Get<string>(CorrelationHeaders.CorrelationId, It.IsAny<string?>()))
            .Returns(Guid.NewGuid().ToString());

        await _consumeFilter.Send(_consumeContextMock.Object, _nextConsumePipeMock.Object);

        _nextConsumePipeMock.Verify(p => p.Send(_consumeContextMock.Object), Times.Once);
    }

    [Fact]
    public void Consume_Probe_RegistersFilterScope()
    {
        Mock<ProbeContext> probeMock = new Mock<ProbeContext>();
        Mock<ProbeContext> scopeMock = new Mock<ProbeContext>();

        probeMock.Setup(p => p.CreateScope("filters")).Returns(scopeMock.Object);

        _consumeFilter.Probe(probeMock.Object);

        scopeMock.Verify(p => p.Add("filterType", "correlationConsume"), Times.Once);
    }

    /// <summary>
    /// Round trip: what the publish filter writes into the headers is what the consume filter reads.
    /// Protects against the two filters drifting apart on the header name.
    /// </summary>
    [Fact]
    public async Task PublishThenConsume_PreservesTheSameIdentifier()
    {
        Guid chainId = Guid.NewGuid();
        _correlationContextMock.Setup(c => c.CorrelationId).Returns(chainId);

        string? capturedHeader = null;
        _publishHeadersMock
            .Setup(h => h.Set(CorrelationHeaders.CorrelationId, It.IsAny<string>()))
            .Callback<string, string>((_, value) => capturedHeader = value);

        await _publishFilter.Send(_publishContextMock.Object, _nextPublishPipeMock.Object);

        _consumeHeadersMock
            .Setup(h => h.Get<string>(CorrelationHeaders.CorrelationId, It.IsAny<string?>()))
            .Returns(() => capturedHeader);

        Guid? observedInsideConsumer = null;
        _nextConsumePipeMock
            .Setup(p => p.Send(It.IsAny<ConsumeContext<TestMessage>>()))
            .Returns(() =>
            {
                observedInsideConsumer = _correlationContext.CorrelationId;
                return Task.CompletedTask;
            });

        await _consumeFilter.Send(_consumeContextMock.Object, _nextConsumePipeMock.Object);

        observedInsideConsumer.Should().Be(chainId);
    }

    public class TestMessage
    {
    }
}
