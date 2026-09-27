using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.DTOs.SyncJobs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Queries.GetSyncJob;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Queries;

public class GetSyncJobQueryHandlerTests
{
    private readonly Mock<ISyncJobRepository> _syncJobRepo = new();

    private GetSyncJobQueryHandler CreateHandler()
    {
        return new GetSyncJobQueryHandler(_syncJobRepo.Object);
    }

    [Fact]
    public async Task Handle_JobNotFound_ThrowsKeyNotFoundException()
    {
        Guid jobId = Guid.NewGuid();

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncJob?)null);

        GetSyncJobQueryHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new GetSyncJobQuery { SyncJobId = jobId },
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{jobId}*");
    }

    [Fact]
    public async Task Handle_JobFound_ReturnsMappedDto()
    {
        Guid jobId = Guid.NewGuid();
        Guid connectionId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow.AddHours(-2);

        SyncJob job = new SyncJob
        {
            Id = jobId,
            ConnectionId = connectionId,
            JobType = JobType.ProductsImport,
            Status = SyncJobStatus.Success,
            CreatedAt = createdAt,
            StartedAt = createdAt.AddMinutes(1),
            CompletedAt = createdAt.AddMinutes(10),
            ErrorMessage = null,
            RecordsProcessed = 200,
            RecordsImported = 150,
            RecordsCreated = 100,
            RecordsUpdated = 50,
            RecordsSkipped = 30,
            RecordsFailed = 20,
            RequestedByUserId = userId,
        };

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        GetSyncJobQueryHandler handler = CreateHandler();
        GetSyncJobResponse result = await handler.Handle(
            new GetSyncJobQuery { SyncJobId = jobId },
            CancellationToken.None);

        result.Job.Should().NotBeNull();
        SyncJobDto dto = result.Job;

        dto.Id.Should().Be(jobId);
        dto.ConnectionId.Should().Be(connectionId);
        dto.JobType.Should().Be("ProductsImport");
        dto.Status.Should().Be("Success");
        dto.CreatedAt.Should().Be(createdAt);
        dto.RecordsProcessed.Should().Be(200);
        dto.RecordsImported.Should().Be(150);
        dto.RecordsCreated.Should().Be(100);
        dto.RecordsUpdated.Should().Be(50);
        dto.RecordsSkipped.Should().Be(30);
        dto.RecordsFailed.Should().Be(20);
        dto.RequestedByUserId.Should().Be(userId);
    }

    [Fact]
    public async Task Handle_JobWithError_MapsErrorMessageCorrectly()
    {
        SyncJob job = new SyncJob
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            JobType = JobType.ProductsImport,
            Status = SyncJobStatus.Failed,
            CreatedAt = DateTimeOffset.UtcNow,
            ErrorMessage = "Ошибка подключения к Wildberries API",
        };

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        GetSyncJobQueryHandler handler = CreateHandler();
        GetSyncJobResponse result = await handler.Handle(
            new GetSyncJobQuery { SyncJobId = job.Id },
            CancellationToken.None);

        result.Job.Status.Should().Be("Failed");
        result.Job.ErrorMessage.Should().Be("Ошибка подключения к Wildberries API");
    }
}
