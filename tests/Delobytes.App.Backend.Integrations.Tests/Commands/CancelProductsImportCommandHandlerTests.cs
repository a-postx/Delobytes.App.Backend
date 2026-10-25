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

public class CancelProductsImportCommandHandlerTests
{
    private readonly Mock<IJobRunRepository> _jobRunRepo = new();

    private CancelProductsImportCommandHandler CreateHandler()
    {
        return new CancelProductsImportCommandHandler(_jobRunRepo.Object);
    }

    [Fact]
    public async Task Handle_JobNotFound_ThrowsKeyNotFoundException()
    {
        Guid jobRunId = Guid.NewGuid();

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobRun?)null);

        CancelProductsImportCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new CancelProductsImportCommand { JobRunId = jobRunId },
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{jobRunId}*");
    }

    [Fact]
    public async Task Handle_WrongJobType_ThrowsInvalidOperationException()
    {
        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            JobType = JobType.OrdersSync,
            Status = JobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        CancelProductsImportCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new CancelProductsImportCommand { JobRunId = jobRun.Id },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ProductsImport*");
    }

    [Theory]
    [InlineData(JobStatus.Success)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    public async Task Handle_AlreadyTerminalStatus_ThrowsInvalidOperationException(JobStatus terminalStatus)
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

        CancelProductsImportCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new CancelProductsImportCommand { JobRunId = jobRun.Id },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(JobStatus.Pending)]
    [InlineData(JobStatus.Running)]
    public async Task Handle_CancellableStatus_SetsCancelledStatus(JobStatus cancellableStatus)
    {
        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            JobType = JobType.ProductsImport,
            Status = cancellableStatus,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        _jobRunRepo
            .Setup(r => r.UpdateAsync(jobRun, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        CancelProductsImportCommandHandler handler = CreateHandler();
        await handler.Handle(new CancelProductsImportCommand { JobRunId = jobRun.Id }, CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Cancelled);
        _jobRunRepo.Verify(r => r.UpdateAsync(jobRun, It.IsAny<CancellationToken>()), Times.Once);
    }
}
