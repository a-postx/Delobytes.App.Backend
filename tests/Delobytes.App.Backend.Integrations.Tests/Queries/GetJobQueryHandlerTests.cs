using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.DTOs.Jobs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Queries.GetJob;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Queries;

public class GetJobQueryHandlerTests
{
    private readonly Mock<IJobRunRepository> _jobRunRepo = new();

    private GetJobQueryHandler CreateHandler()
    {
        return new GetJobQueryHandler(_jobRunRepo.Object);
    }

    [Fact]
    public async Task Handle_NotFound_ThrowsKeyNotFoundException()
    {
        Guid jobRunId = Guid.NewGuid();

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobRun?)null);

        GetJobQueryHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new GetJobQuery { JobRunId = jobRunId },
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{jobRunId}*");
    }

    [Fact]
    public async Task Handle_ProductsImport_MapsAllFields()
    {
        Guid jobRunId = Guid.NewGuid();
        Guid connectionId = Guid.NewGuid();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow.AddHours(-2);

        JobRun jobRun = new JobRun
        {
            Id = jobRunId,
            ConnectionId = connectionId,
            JobType = JobType.ProductsImport,
            Status = JobStatus.Running,
            CreatedAt = createdAt,
            StartedAt = createdAt.AddMinutes(1),
            CompletedAt = null,
            Error = null,
            RecordsCreated = 100,
            RecordsUpdated = 50,
            RecordsSkipped = 30,
            RecordsFailed = 20,
        };

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        JobDto dto = await CreateHandler().Handle(
            new GetJobQuery { JobRunId = jobRunId },
            CancellationToken.None);

        dto.Id.Should().Be(jobRunId);
        dto.ConnectionId.Should().Be(connectionId);
        dto.Type.Should().Be("products-import");
        dto.Status.Should().Be(JobStatus.Running);
        dto.CreatedAt.Should().Be(createdAt);
        dto.StartedAt.Should().Be(createdAt.AddMinutes(1));
        dto.CompletedAt.Should().BeNull();
        dto.RecordsCreated.Should().Be(100);
        dto.RecordsUpdated.Should().Be(50);
        dto.RecordsSkipped.Should().Be(30);
        dto.RecordsFailed.Should().Be(20);
        dto.Error.Should().BeNull();
    }

    [Fact]
    public async Task Handle_OrdersImport_EmitsCamelCaseTypeToken()
    {
        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            JobType = JobType.OrdersImport,
            Status = JobStatus.Success,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
        };

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        JobDto dto = await CreateHandler().Handle(
            new GetJobQuery { JobRunId = jobRun.Id },
            CancellationToken.None);

        dto.Type.Should().Be("orders-import");
    }

    [Fact]
    public async Task Handle_FailedJob_SurfacesError()
    {
        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            JobType = JobType.ProductsImport,
            Status = JobStatus.Failed,
            CreatedAt = DateTimeOffset.UtcNow,
            Error = "Ошибка подключения к Wildberries API",
        };

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        JobDto dto = await CreateHandler().Handle(
            new GetJobQuery { JobRunId = jobRun.Id },
            CancellationToken.None);

        dto.Status.Should().Be(JobStatus.Failed);
        dto.Error.Should().Be("Ошибка подключения к Wildberries API");
    }
}
