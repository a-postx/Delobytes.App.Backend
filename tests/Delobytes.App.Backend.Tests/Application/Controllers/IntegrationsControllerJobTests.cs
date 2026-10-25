using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Api.Controllers;
using Delobytes.App.Backend.Integrations.Application.Commands.CancelJob;
using Delobytes.App.Backend.Integrations.Application.Commands.StartJob;
using Delobytes.App.Backend.Integrations.Application.DTOs.Jobs;
using Delobytes.App.Backend.Integrations.Application.Queries.GetJob;
using Delobytes.App.Backend.Integrations.Application.Queries.GetJobs;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Controllers;

public class IntegrationsControllerJobTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly IntegrationsController _controller;

    public IntegrationsControllerJobTests()
    {
        _controller = new IntegrationsController(_mediator.Object);
    }

    [Fact]
    public async Task GetJobs_PassesQueryParametersThrough()
    {
        Guid connectionId = Guid.NewGuid();
        JobStatus[] statuses = new[] { JobStatus.Running };
        GetJobsResponse expected = new GetJobsResponse { TotalCount = 1, Page = 2, PageSize = 10 };

        _mediator
            .Setup(m => m.Send(
                It.Is<GetJobsQuery>(q =>
                    q.Type == "products-import"
                    && q.ConnectionId == connectionId
                    && q.Status == statuses
                    && q.Page == 2
                    && q.PageSize == 10),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        ActionResult<GetJobsResponse> result = await _controller.GetJobs(
            "products-import", connectionId, statuses, 2, 10, CancellationToken.None);

        OkObjectResult okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task GetJobs_NoFilters_SendsEmptyQuery()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<GetJobsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetJobsResponse());

        await _controller.GetJobs(null, null, null, null, null, CancellationToken.None);

        _mediator.Verify(
            m => m.Send(
                It.Is<GetJobsQuery>(q =>
                    q.Type == null && q.ConnectionId == null && q.Status == null && q.Page == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetJob_ReturnsDto()
    {
        Guid jobRunId = Guid.NewGuid();
        JobDto expected = new JobDto { Id = jobRunId, Type = "products-import" };

        _mediator
            .Setup(m => m.Send(It.Is<GetJobQuery>(q => q.JobRunId == jobRunId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        ActionResult<JobDto> result = await _controller.GetJob(jobRunId, CancellationToken.None);

        OkObjectResult okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task StartJob_ProductsImport_ReturnsAcceptedWithJobLocation()
    {
        Guid jobRunId = Guid.NewGuid();
        Guid connectionId = Guid.NewGuid();

        _mediator
            .Setup(m => m.Send(It.IsAny<StartJobCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartJobResponse { JobRunId = jobRunId, Type = "products-import" });

        ActionResult<StartJobResponse> result = await _controller.StartJob(
            "products-import",
            new StartJobRequest { ConnectionId = connectionId },
            CancellationToken.None);

        AcceptedAtActionResult accepted = result.Result.Should().BeOfType<AcceptedAtActionResult>().Subject;
        accepted.ActionName.Should().Be(nameof(IntegrationsController.GetJob));
        accepted.RouteValues.Should().ContainKey("id").WhoseValue.Should().Be(jobRunId);

        _mediator.Verify(
            m => m.Send(
                It.Is<StartJobCommand>(c =>
                    c.JobType == JobType.ProductsImport && c.ConnectionId == connectionId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StartJob_UnknownType_ReturnsBadRequest_WithoutDispatching()
    {
        ActionResult<StartJobResponse> result = await _controller.StartJob(
            "articles-import",
            new StartJobRequest { ConnectionId = Guid.NewGuid() },
            CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();

        _mediator.Verify(
            m => m.Send(It.IsAny<StartJobCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StartJob_OrdersImport_IsDispatchedAndRejectedByTheHandler()
    {
        // The controller must not pre-empt the startability rule; it maps the token and lets
        // the handler decide. Here the handler's rejection surfaces as InvalidOperationException.
        _mediator
            .Setup(m => m.Send(
                It.Is<StartJobCommand>(c => c.JobType == JobType.OrdersImport),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Задача типа 'orders-import' не может быть запущена вручную."));

        Func<Task> act = () => _controller.StartJob(
            "orders-import",
            new StartJobRequest { ConnectionId = Guid.NewGuid() },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("products-import")]
    [InlineData("Products-Import")]
    public async Task StartJob_TypeToken_IsCaseInsensitive(string token)
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<StartJobCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartJobResponse { JobRunId = Guid.NewGuid(), Type = "products-import" });

        ActionResult<StartJobResponse> result = await _controller.StartJob(
            token,
            new StartJobRequest { ConnectionId = Guid.NewGuid() },
            CancellationToken.None);

        result.Result.Should().BeOfType<AcceptedAtActionResult>();
    }

    [Fact]
    public async Task CancelJob_DispatchesCancelCommand_AndReturnsNoContent()
    {
        Guid jobRunId = Guid.NewGuid();

        _mediator
            .Setup(m => m.Send(It.IsAny<CancelJobCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        IActionResult result = await _controller.CancelJob(jobRunId, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();

        _mediator.Verify(
            m => m.Send(
                It.Is<CancelJobCommand>(c => c.JobRunId == jobRunId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
