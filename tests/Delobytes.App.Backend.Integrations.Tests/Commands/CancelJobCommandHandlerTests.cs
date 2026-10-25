using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Commands;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Commands;

public class CancelJobCommandHandlerTests
{
    private readonly Mock<IJobRunRepository> _jobRunRepo = new();

    private CancelJobCommandHandler CreateHandler()
    {
        return new CancelJobCommandHandler(_jobRunRepo.Object);
    }

    [Fact]
    public async Task Handle_JobNotFound_ThrowsKeyNotFoundException()
    {
        Guid jobRunId = Guid.NewGuid();

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobRun?)null);

        CancelJobCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new CancelJobCommand { JobRunId = jobRunId },
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{jobRunId}*");
    }

    [Fact]
    public async Task Handle_ScheduledOrdersImport_IsRejectedEvenWhilePending()
    {
        // Orders import is scheduler-driven: cancelling it would break watermark continuity,
        // so the type check must run before the status check and reject a Pending job.
        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            JobType = JobType.OrdersImport,
            Status = JobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        CancelJobCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new CancelJobCommand { JobRunId = jobRun.Id },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*orders-import*");

        jobRun.Status.Should().Be(JobStatus.Pending);
        _jobRunRepo.Verify(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(JobStatus.Success)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    public async Task Handle_TerminalStatus_ThrowsInvalidOperationException(JobStatus terminalStatus)
    {
        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            JobType = JobType.ProductsImport,
            Status = terminalStatus,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        CancelJobCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new CancelJobCommand { JobRunId = jobRun.Id },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _jobRunRepo.Verify(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(JobStatus.Pending)]
    [InlineData(JobStatus.Running)]
    public async Task Handle_CancellableProductsImport_SetsCancelledAndPersists(JobStatus cancellableStatus)
    {
        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            JobType = JobType.ProductsImport,
            Status = cancellableStatus,
            CreatedAt = DateTimeOffset.UtcNow,
            StartedAt = cancellableStatus == JobStatus.Running ? DateTimeOffset.UtcNow : null,
        };

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        _jobRunRepo
            .Setup(r => r.UpdateAsync(jobRun, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        CancelJobCommandHandler handler = CreateHandler();
        await handler.Handle(new CancelJobCommand { JobRunId = jobRun.Id }, CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Cancelled);
        _jobRunRepo.Verify(r => r.UpdateAsync(jobRun, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Cancellation_LeavesCompletedAtAndCountersToTheAggregator()
    {
        // In-flight batches still report results after the cancel request, so the handler must
        // not finalise CompletedAt or overwrite counters; the aggregator owns that.
        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            JobType = JobType.ProductsImport,
            Status = JobStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = null,
            RecordsCreated = 42,
        };

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        CancelJobCommandHandler handler = CreateHandler();
        await handler.Handle(new CancelJobCommand { JobRunId = jobRun.Id }, CancellationToken.None);

        jobRun.CompletedAt.Should().BeNull();
        jobRun.RecordsCreated.Should().Be(42);
    }
}
