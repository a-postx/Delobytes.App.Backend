using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.DTOs.SyncJobs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Queries.GetSyncJobs;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Queries;

public class GetSyncJobsQueryHandlerTests
{
    private readonly Mock<ISyncJobRepository> _syncJobRepo = new();

    private GetSyncJobsQueryHandler CreateHandler()
    {
        return new GetSyncJobsQueryHandler(_syncJobRepo.Object);
    }

    [Fact]
    public async Task Handle_NoJobs_ReturnsEmptyList()
    {
        _syncJobRepo
            .Setup(r => r.GetProductsImportJobsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncJob>());

        GetSyncJobsQueryHandler handler = CreateHandler();
        GetSyncJobsResponse result = await handler.Handle(new GetSyncJobsQuery(), CancellationToken.None);

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithJobs_MapsAllFieldsCorrectly()
    {
        DateTimeOffset createdAt = DateTimeOffset.UtcNow.AddHours(-1);
        DateTimeOffset startedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        Guid connectionId = Guid.NewGuid();
        Guid jobId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        SyncJob job = new SyncJob
        {
            Id = jobId,
            ConnectionId = connectionId,
            JobType = JobType.ProductsImport,
            Status = SyncJobStatus.Running,
            CreatedAt = createdAt,
            StartedAt = startedAt,
            CompletedAt = null,
            ErrorMessage = null,
            RecordsProcessed = 100,
            RecordsImported = 80,
            RecordsCreated = 50,
            RecordsUpdated = 30,
            RecordsSkipped = 10,
            RecordsFailed = 10,
            RequestedByUserId = userId,
        };

        _syncJobRepo
            .Setup(r => r.GetProductsImportJobsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncJob> { job });

        GetSyncJobsQueryHandler handler = CreateHandler();
        GetSyncJobsResponse result = await handler.Handle(new GetSyncJobsQuery(), CancellationToken.None);

        result.Items.Should().HaveCount(1);
        SyncJobDto dto = result.Items[0];

        dto.Id.Should().Be(jobId);
        dto.ConnectionId.Should().Be(connectionId);
        dto.JobType.Should().Be("ProductsImport");
        dto.Status.Should().Be("Running");
        dto.CreatedAt.Should().Be(createdAt);
        dto.StartedAt.Should().Be(startedAt);
        dto.CompletedAt.Should().BeNull();
        dto.RecordsProcessed.Should().Be(100);
        dto.RecordsImported.Should().Be(80);
        dto.RecordsCreated.Should().Be(50);
        dto.RecordsUpdated.Should().Be(30);
        dto.RecordsSkipped.Should().Be(10);
        dto.RecordsFailed.Should().Be(10);
        dto.RequestedByUserId.Should().Be(userId);
    }

    [Fact]
    public async Task Handle_MultipleJobs_ReturnsAll()
    {
        List<SyncJob> jobs = new List<SyncJob>
        {
            new SyncJob
            {
                Id = Guid.NewGuid(), ConnectionId = Guid.NewGuid(),
                JobType = JobType.ProductsImport, Status = SyncJobStatus.Success,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            },
            new SyncJob
            {
                Id = Guid.NewGuid(), ConnectionId = Guid.NewGuid(),
                JobType = JobType.ProductsImport, Status = SyncJobStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
            },
        };

        _syncJobRepo
            .Setup(r => r.GetProductsImportJobsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        GetSyncJobsQueryHandler handler = CreateHandler();
        GetSyncJobsResponse result = await handler.Handle(new GetSyncJobsQuery(), CancellationToken.None);

        result.Items.Should().HaveCount(2);
    }
}
