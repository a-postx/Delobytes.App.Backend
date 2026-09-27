using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Commands.CancelSyncJob;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Commands;

public class CancelSyncJobCommandHandlerTests
{
    private readonly Mock<ISyncJobRepository> _syncJobRepo = new();

    private CancelSyncJobCommandHandler CreateHandler()
    {
        return new CancelSyncJobCommandHandler(_syncJobRepo.Object);
    }

    [Fact]
    public async Task Handle_JobNotFound_ThrowsKeyNotFoundException()
    {
        Guid jobId = Guid.NewGuid();

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncJob?)null);

        CancelSyncJobCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new CancelSyncJobCommand { SyncJobId = jobId },
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{jobId}*");
    }

    [Fact]
    public async Task Handle_WrongJobType_ThrowsInvalidOperationException()
    {
        SyncJob job = new SyncJob
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            JobType = JobType.OrdersSync,
            Status = SyncJobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        CancelSyncJobCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new CancelSyncJobCommand { SyncJobId = job.Id },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ProductsImport*");
    }

    [Theory]
    [InlineData(SyncJobStatus.Success)]
    [InlineData(SyncJobStatus.Failed)]
    [InlineData(SyncJobStatus.Cancelled)]
    public async Task Handle_AlreadyTerminalStatus_ThrowsInvalidOperationException(SyncJobStatus terminalStatus)
    {
        SyncJob job = new SyncJob
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            JobType = JobType.ProductsImport,
            Status = terminalStatus,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        CancelSyncJobCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new CancelSyncJobCommand { SyncJobId = job.Id },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(SyncJobStatus.Pending)]
    [InlineData(SyncJobStatus.Running)]
    public async Task Handle_CancellableStatus_SetsCancelledAndCompletedAt(SyncJobStatus cancellableStatus)
    {
        SyncJob job = new SyncJob
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            JobType = JobType.ProductsImport,
            Status = cancellableStatus,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CancelSyncJobCommandHandler handler = CreateHandler();
        await handler.Handle(new CancelSyncJobCommand { SyncJobId = job.Id }, CancellationToken.None);

        job.Status.Should().Be(SyncJobStatus.Cancelled);
        job.CompletedAt.Should().NotBeNull();
        _syncJobRepo.Verify(r => r.Update(job), Times.Once);
        _syncJobRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
