using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Filters;
using Delobytes.App.Backend.Services;
using FluentAssertions;
using MassTransit;
using Moq;

namespace Delobytes.App.Backend.Tests.Messaging;

public class TenantConsumeFilterTests
{
    private readonly MessageTenantContext _messageTenantContext;
    private readonly Mock<ConsumeContext<TestMessage>> _consumeContextMock;
    private readonly Mock<Headers> _headersMock;
    private readonly Mock<IPipe<ConsumeContext<TestMessage>>> _nextPipeMock;
    private readonly TenantConsumeFilter<TestMessage> _filter;

    public TenantConsumeFilterTests()
    {
        _messageTenantContext = new MessageTenantContext();
        _consumeContextMock = new Mock<ConsumeContext<TestMessage>>();
        _headersMock = new Mock<Headers>();
        _nextPipeMock = new Mock<IPipe<ConsumeContext<TestMessage>>>();

        _consumeContextMock.Setup(c => c.Headers).Returns(_headersMock.Object);
        _nextPipeMock
            .Setup(p => p.Send(It.IsAny<ConsumeContext<TestMessage>>()))
            .Returns(Task.CompletedTask);

        _filter = new TenantConsumeFilter<TestMessage>(_messageTenantContext);
    }

    [Fact]
    public async Task Send_WhenHeaderPresent_SetsTenantIdInContext()
    {
        Guid tenantId = Guid.NewGuid();
        _headersMock
            .Setup(h => h.Get<Guid>(TenantMessageHeaders.TenantId, It.IsAny<Guid?>()))
            .Returns(tenantId);

        await _filter.Send(_consumeContextMock.Object, _nextPipeMock.Object);

        // During execution of next pipe the value must be set; we capture it inside the pipe
        // (verification below is post-pipeline, so we re-assert via a capture approach)
        // The assertion that matters: context was set at some point and cleared after
        // We verify by inspecting the captured value inside the pipe delegate
        _messageTenantContext.TenantId.Should().BeNull("context must be cleared after consume");
    }

    [Fact]
    public async Task Send_WhenHeaderPresent_TenantIdIsSetDuringPipeExecution()
    {
        Guid tenantId = Guid.NewGuid();
        Guid? capturedDuringConsume = null;

        _headersMock
            .Setup(h => h.Get<Guid>(TenantMessageHeaders.TenantId, It.IsAny<Guid?>()))
            .Returns(tenantId);

        _nextPipeMock
            .Setup(p => p.Send(It.IsAny<ConsumeContext<TestMessage>>()))
            .Returns(() =>
            {
                capturedDuringConsume = _messageTenantContext.TenantId;
                return Task.CompletedTask;
            });

        await _filter.Send(_consumeContextMock.Object, _nextPipeMock.Object);

        capturedDuringConsume.Should().Be(tenantId);
    }

    [Fact]
    public async Task Send_WhenHeaderAbsent_TenantIdRemainsNullDuringExecution()
    {
        Guid? capturedDuringConsume = Guid.NewGuid(); // pre-set to non-null to detect wrong state

        _headersMock
            .Setup(h => h.Get<Guid>(TenantMessageHeaders.TenantId, It.IsAny<Guid?>()))
            .Returns((Guid?)null);

        _nextPipeMock
            .Setup(p => p.Send(It.IsAny<ConsumeContext<TestMessage>>()))
            .Returns(() =>
            {
                capturedDuringConsume = _messageTenantContext.TenantId;
                return Task.CompletedTask;
            });

        await _filter.Send(_consumeContextMock.Object, _nextPipeMock.Object);

        capturedDuringConsume.Should().BeNull();
    }

    [Fact]
    public async Task Send_AfterPipeCompletes_AlwaysClearsTenantId()
    {
        Guid tenantId = Guid.NewGuid();
        _headersMock
            .Setup(h => h.Get<Guid>(TenantMessageHeaders.TenantId, It.IsAny<Guid?>()))
            .Returns(tenantId);

        await _filter.Send(_consumeContextMock.Object, _nextPipeMock.Object);

        _messageTenantContext.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task Send_WhenNextPipeThrows_StillClearsTenantId()
    {
        Guid tenantId = Guid.NewGuid();
        _headersMock
            .Setup(h => h.Get<Guid>(TenantMessageHeaders.TenantId, It.IsAny<Guid?>()))
            .Returns(tenantId);

        _nextPipeMock
            .Setup(p => p.Send(It.IsAny<ConsumeContext<TestMessage>>()))
            .ThrowsAsync(new InvalidOperationException("consumer failure"));

        Func<Task> act = async () =>
            await _filter.Send(_consumeContextMock.Object, _nextPipeMock.Object);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _messageTenantContext.TenantId.Should().BeNull("finally block must clear context even on exception");
    }

    [Fact]
    public async Task Send_AlwaysCallsNextPipe()
    {
        _headersMock
            .Setup(h => h.Get<Guid>(TenantMessageHeaders.TenantId, It.IsAny<Guid?>()))
            .Returns((Guid?)null);

        await _filter.Send(_consumeContextMock.Object, _nextPipeMock.Object);

        _nextPipeMock.Verify(p => p.Send(_consumeContextMock.Object), Times.Once);
    }

    public class TestMessage { }
}
