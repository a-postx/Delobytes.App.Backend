using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Integrations.Application.Commands.StartJob;
using Delobytes.App.Backend.Integrations.Application.Commands.StartProductsImport;
using Delobytes.App.Backend.Integrations.Application.DTOs.Jobs;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using MediatR;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Commands;

public class StartJobCommandHandlerTests
{
    private readonly Mock<IMediator> _mediator = new();

    private StartJobCommandHandler CreateHandler()
    {
        return new StartJobCommandHandler(_mediator.Object);
    }

    [Fact]
    public async Task Handle_ProductsImport_DispatchesStartProductsImportCommand()
    {
        Guid connectionId = Guid.NewGuid();
        Guid jobRunId = Guid.NewGuid();

        _mediator
            .Setup(m => m.Send(It.IsAny<StartProductsImportCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartProductsImportResponse { JobRunId = jobRunId });

        StartJobCommandHandler handler = CreateHandler();

        StartJobResponse response = await handler.Handle(
            new StartJobCommand { JobType = JobType.ProductsImport, ConnectionId = connectionId },
            CancellationToken.None);

        response.JobRunId.Should().Be(jobRunId);
        response.Type.Should().Be("products-import");

        _mediator.Verify(
            m => m.Send(
                It.Is<StartProductsImportCommand>(c => c.ConnectionId == connectionId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(JobType.OrdersImport)]
    [InlineData(JobType.OrdersSync)]
    public async Task Handle_NonStartableType_Throws_WithoutDispatching(JobType jobType)
    {
        StartJobCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new StartJobCommand { JobType = jobType, ConnectionId = Guid.NewGuid() },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        _mediator.Verify(
            m => m.Send(It.IsAny<StartProductsImportCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_OrdersImport_ErrorNamesTheJobTypeSoTheClientCanTellWhichCallFailed()
    {
        StartJobCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new StartJobCommand { JobType = JobType.OrdersImport, ConnectionId = Guid.NewGuid() },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*orders-import*");
    }

    [Fact]
    public void StartJobCommand_IsAdministratorOnly()
    {
        StartJobCommand command = new StartJobCommand();

        command.AllowedRoles.Should().BeEquivalentTo(new[] { Role.Administrator });
    }
}
