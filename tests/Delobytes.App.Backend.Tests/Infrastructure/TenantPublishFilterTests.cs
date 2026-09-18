using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Filters;
using FluentAssertions;
using MassTransit;
using Moq;

namespace Delobytes.App.Backend.Tests.Messaging;

public class TenantPublishFilterTests
{
    private readonly Mock<ITenantContext> _tenantContextMock;
    private readonly Mock<PublishContext<TestMessage>> _publishContextMock;
    private readonly Mock<SendHeaders> _headersMock;
    private readonly Mock<IPipe<PublishContext<TestMessage>>> _nextPipeMock;
    private readonly TenantPublishFilter<TestMessage> _filter;

    public TenantPublishFilterTests()
    {
        _tenantContextMock = new Mock<ITenantContext>();
        _publishContextMock = new Mock<PublishContext<TestMessage>>();
        _headersMock = new Mock<SendHeaders>();
        _nextPipeMock = new Mock<IPipe<PublishContext<TestMessage>>>();

        _publishContextMock.Setup(c => c.Headers).Returns(_headersMock.Object);
        _nextPipeMock
            .Setup(p => p.Send(It.IsAny<PublishContext<TestMessage>>()))
            .Returns(Task.CompletedTask);

        _filter = new TenantPublishFilter<TestMessage>(_tenantContextMock.Object);
    }

    [Fact]
    public async Task Send_WhenTenantIdPresent_SetsTenantIdHeader()
    {
        Guid tenantId = Guid.NewGuid();
        _tenantContextMock.Setup(c => c.TenantId).Returns(tenantId);

        await _filter.Send(_publishContextMock.Object, _nextPipeMock.Object);

        _headersMock.Verify(
            h => h.Set(TenantMessageHeaders.TenantId, (object)tenantId, It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task Send_WhenTenantIdNull_DoesNotSetAnyHeader()
    {
        _tenantContextMock.Setup(c => c.TenantId).Returns((Guid?)null);

        await _filter.Send(_publishContextMock.Object, _nextPipeMock.Object);

        _headersMock.Verify(
            h => h.Set(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task Send_AlwaysCallsNextPipe_WhenTenantIdPresent()
    {
        _tenantContextMock.Setup(c => c.TenantId).Returns(Guid.NewGuid());

        await _filter.Send(_publishContextMock.Object, _nextPipeMock.Object);

        _nextPipeMock.Verify(p => p.Send(_publishContextMock.Object), Times.Once);
    }

    [Fact]
    public async Task Send_AlwaysCallsNextPipe_WhenTenantIdNull()
    {
        _tenantContextMock.Setup(c => c.TenantId).Returns((Guid?)null);

        await _filter.Send(_publishContextMock.Object, _nextPipeMock.Object);

        _nextPipeMock.Verify(p => p.Send(_publishContextMock.Object), Times.Once);
    }

    public class TestMessage { }
}
