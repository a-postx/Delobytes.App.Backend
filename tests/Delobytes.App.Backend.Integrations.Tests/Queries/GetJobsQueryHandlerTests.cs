using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.DTOs.Jobs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Queries.GetJobs;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Queries;

public class GetJobsQueryHandlerTests
{
    private readonly Mock<IJobRunRepository> _jobRunRepo = new();

    private GetJobsQueryHandler CreateHandler()
    {
        return new GetJobsQueryHandler(_jobRunRepo.Object);
    }

    private void SetupPage(int totalCount, List<JobRun> items)
    {
        _jobRunRepo
            .Setup(r => r.GetPagedAsync(
                It.IsAny<JobType?>(),
                It.IsAny<Guid?>(),
                It.IsAny<JobStatus[]?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((totalCount, items));
    }

    [Fact]
    public async Task Handle_NoJobs_ReturnsEmptyPageWithTotals()
    {
        SetupPage(0, new List<JobRun>());

        GetJobsResponse result = await CreateHandler().Handle(new GetJobsQuery(), CancellationToken.None);

        result.Jobs.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(50);
    }

    [Fact]
    public async Task Handle_MapsJobRunFieldsToDto()
    {
        Guid jobRunId = Guid.NewGuid();
        Guid connectionId = Guid.NewGuid();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow.AddHours(-1);

        JobRun jobRun = new JobRun
        {
            Id = jobRunId,
            ConnectionId = connectionId,
            JobType = JobType.ProductsImport,
            Status = JobStatus.Success,
            CreatedAt = createdAt,
            StartedAt = createdAt.AddMinutes(1),
            CompletedAt = createdAt.AddMinutes(5),
            RecordsCreated = 11,
            RecordsUpdated = 12,
            RecordsSkipped = 13,
            RecordsFailed = 14,
        };

        SetupPage(1, new List<JobRun> { jobRun });

        GetJobsResponse result = await CreateHandler().Handle(new GetJobsQuery(), CancellationToken.None);

        JobDto dto = result.Jobs.Should().ContainSingle().Which;
        dto.Id.Should().Be(jobRunId);
        dto.ConnectionId.Should().Be(connectionId);
        dto.Type.Should().Be("products-import");
        dto.Status.Should().Be(JobStatus.Success);
        dto.RecordsCreated.Should().Be(11);
        dto.RecordsUpdated.Should().Be(12);
        dto.RecordsSkipped.Should().Be(13);
        dto.RecordsFailed.Should().Be(14);
        dto.CreatedAt.Should().Be(createdAt);
        dto.StartedAt.Should().Be(createdAt.AddMinutes(1));
        dto.CompletedAt.Should().Be(createdAt.AddMinutes(5));
    }

    [Fact]
    public async Task Handle_TypeFilter_IsParsedFromApiToken()
    {
        SetupPage(0, new List<JobRun>());

        await CreateHandler().Handle(
            new GetJobsQuery { Type = "orders-import" },
            CancellationToken.None);

        _jobRunRepo.Verify(
            r => r.GetPagedAsync(
                JobType.OrdersImport,
                It.IsAny<Guid?>(),
                It.IsAny<JobStatus[]?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_TypeToken_IsCaseInsensitive()
    {
        SetupPage(0, new List<JobRun>());

        await CreateHandler().Handle(
            new GetJobsQuery { Type = "Products-Import" },
            CancellationToken.None);

        _jobRunRepo.Verify(
            r => r.GetPagedAsync(
                JobType.ProductsImport,
                It.IsAny<Guid?>(),
                It.IsAny<JobStatus[]?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("productsimport")]
    [InlineData("products_import")]
    public async Task Handle_UnknownTypeToken_ThrowsInsteadOfSilentlyReturningEverything(string type)
    {
        GetJobsQueryHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(new GetJobsQuery { Type = type }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*foo*".Equals($"*{type}*") ? $"*{type}*" : $"*{type}*");

        _jobRunRepo.Verify(
            r => r.GetPagedAsync(
                It.IsAny<JobType?>(),
                It.IsAny<Guid?>(),
                It.IsAny<JobStatus[]?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_BlankType_MeansNoTypeFilter()
    {
        SetupPage(0, new List<JobRun>());

        await CreateHandler().Handle(new GetJobsQuery { Type = "   " }, CancellationToken.None);

        _jobRunRepo.Verify(
            r => r.GetPagedAsync(
                null,
                It.IsAny<Guid?>(),
                It.IsAny<JobStatus[]?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ConnectionAndStatusFilters_AreForwarded()
    {
        Guid connectionId = Guid.NewGuid();
        JobStatus[] statuses = new[] { JobStatus.Pending, JobStatus.Running };

        SetupPage(0, new List<JobRun>());

        await CreateHandler().Handle(
            new GetJobsQuery { ConnectionId = connectionId, Status = statuses },
            CancellationToken.None);

        _jobRunRepo.Verify(
            r => r.GetPagedAsync(
                null,
                connectionId,
                statuses,
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_EmptyStatusArray_TreatedAsNoStatusFilter()
    {
        SetupPage(0, new List<JobRun>());

        await CreateHandler().Handle(
            new GetJobsQuery { Status = Array.Empty<JobStatus>() },
            CancellationToken.None);

        _jobRunRepo.Verify(
            r => r.GetPagedAsync(
                It.IsAny<JobType?>(),
                It.IsAny<Guid?>(),
                null,
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NoPageRequested_DoesNotPage()
    {
        // Legacy contract kept from GET /product-imports: no page means "return everything".
        SetupPage(0, new List<JobRun>());

        await CreateHandler().Handle(new GetJobsQuery(), CancellationToken.None);

        _jobRunRepo.Verify(
            r => r.GetPagedAsync(
                It.IsAny<JobType?>(),
                It.IsAny<Guid?>(),
                It.IsAny<JobStatus[]?>(),
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PageRequested_ComputesSkipAndTake()
    {
        SetupPage(120, new List<JobRun>());

        GetJobsResponse result = await CreateHandler().Handle(
            new GetJobsQuery { Page = 3, PageSize = 20 },
            CancellationToken.None);

        _jobRunRepo.Verify(
            r => r.GetPagedAsync(
                It.IsAny<JobType?>(),
                It.IsAny<Guid?>(),
                It.IsAny<JobStatus[]?>(),
                40,
                20,
                It.IsAny<CancellationToken>()),
            Times.Once);

        result.TotalCount.Should().Be(120, "the total is counted before paging");
        result.Page.Should().Be(3);
        result.PageSize.Should().Be(20);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public async Task Handle_PageBelowOne_IsNormalisedToOne(int requestedPage, int expectedPage)
    {
        SetupPage(0, new List<JobRun>());

        GetJobsResponse result = await CreateHandler().Handle(
            new GetJobsQuery { Page = requestedPage, PageSize = 10 },
            CancellationToken.None);

        result.Page.Should().Be(expectedPage);
        _jobRunRepo.Verify(
            r => r.GetPagedAsync(
                It.IsAny<JobType?>(),
                It.IsAny<Guid?>(),
                It.IsAny<JobStatus[]?>(),
                0,
                10,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(500, 200)]
    public async Task Handle_PageSize_IsClamped(int requestedPageSize, int expectedPageSize)
    {
        SetupPage(0, new List<JobRun>());

        GetJobsResponse result = await CreateHandler().Handle(
            new GetJobsQuery { Page = 1, PageSize = requestedPageSize },
            CancellationToken.None);

        result.PageSize.Should().Be(expectedPageSize);
        _jobRunRepo.Verify(
            r => r.GetPagedAsync(
                It.IsAny<JobType?>(),
                It.IsAny<Guid?>(),
                It.IsAny<JobStatus[]?>(),
                0,
                expectedPageSize,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
