using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Integrations.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public class ProcessProductsImportConsumerTests
{
    private readonly Mock<IJobRunRepository> _jobRunRepo = new();
    private readonly Mock<IJobCheckpointRepository> _jobCheckpointRepo = new();
    private readonly Mock<IRawApiResponseRepository> _rawApiResponseRepo = new();
    private readonly Mock<IChannelApiClientFactory> _clientFactory = new();
    private readonly Mock<IChannelApiClient> _apiClient = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<ILogger<ProcessProductsImportConsumer>> _logger = new();

    // Real instance rather than a mock: JobRunExecutionContext has no interface/virtual
    // members for Moq to override, and it is cheap enough (AsyncLocal-backed) to exercise for
    // real, the same way MessageTenantContextTests exercises MessageTenantContext directly.
    private readonly JobRunExecutionContext _jobRunExecutionContext = new();

    private ProcessProductsImportConsumer CreateConsumer(int batchSize = 50)
    {
        WildberriesImportOptions options = new WildberriesImportOptions { BatchSize = batchSize };
        IOptions<WildberriesImportOptions> wrappedOptions = Options.Create(options);

        return new ProcessProductsImportConsumer(
            _jobRunRepo.Object,
            _jobCheckpointRepo.Object,
            _rawApiResponseRepo.Object,
            _clientFactory.Object,
            _eventPublisher.Object,
            _jobRunExecutionContext,
            wrappedOptions,
            _logger.Object);
    }

    private static SystemChannelTemplate BuildWildberriesTemplate()
    {
        return new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "wildberries",
            DisplayName = "Wildberries",
            ApiBaseUrl = "https://suppliers-api.wildberries.ru",
            ApiVersion = "v2",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static Connection BuildConnection(SystemChannelTemplate template, bool isActive = true)
    {
        return new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            SystemChannelTemplateId = template.Id,
            Name = "Test WB",
            ApiKey = "test-key",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            SystemChannelTemplate = template,
        };
    }

    private static JobRun BuildJobRun(Connection connection, JobStatus status = JobStatus.Pending)
    {
        return new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            JobType = JobType.ProductsImport,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            Connection = connection,
        };
    }

    private static ApiResponse<ProductCardsData> BuildSuccessResponse(
        List<WildberriesCardSnapshot> cards,
        ProductCardsCursor? nextCursor = null)
    {
        return new ApiResponse<ProductCardsData>
        {
            IsSuccess = true,
            StatusCode = 200,
            Timestamp = DateTimeOffset.UtcNow,
            Data = new ProductCardsData
            {
                Cards = cards,
                NextCursor = nextCursor,
                TotalCount = cards.Count,
            },
        };
    }

    private static ApiResponse<ProductCardsData> BuildFailureResponse(string errorMessage, int statusCode = 500)
    {
        return new ApiResponse<ProductCardsData>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            ErrorMessage = errorMessage,
            Timestamp = DateTimeOffset.UtcNow,
            Data = new ProductCardsData { Cards = new List<WildberriesCardSnapshot>() },
        };
    }

    private static WildberriesCardSnapshot BuildCard(long nmId = 1)
    {
        return new WildberriesCardSnapshot
        {
            NmId = nmId,
            Name = $"Product {nmId}",
            VendorCode = $"SKU-{nmId}",
        };
    }

    private static List<WildberriesCardSnapshot> BuildCards(long firstNmId, int count)
    {
        List<WildberriesCardSnapshot> cards = new List<WildberriesCardSnapshot>();

        for (int i = 0; i < count; i++)
        {
            cards.Add(BuildCard(firstNmId + i));
        }

        return cards;
    }

    /// <summary>
    /// Sets up every IJobRunRepository/IJobCheckpointRepository call ProcessProductsImportConsumer
    /// makes unconditionally, with the non-cancelled, no-saved-checkpoint defaults most tests need.
    /// Individual tests override specific setups afterwards.
    /// </summary>
    private void SetupDefaults(JobRun jobRun)
    {
        _jobRunRepo
            .Setup(r => r.GetByIdWithConnectionAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        _jobRunRepo
            .Setup(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _jobRunRepo
            .Setup(r => r.GetStatusAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => jobRun.Status == JobStatus.Cancelled ? JobStatus.Cancelled : JobStatus.Running);

        _jobCheckpointRepo
            .Setup(r => r.GetByJobRunIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobCheckpoint?)null);

        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // -----------------------------------------------------------------------
    // JobRun not found
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_JobRunNotFound_ReturnsWithoutAction()
    {
        Guid jobRunId = Guid.NewGuid();

        _jobRunRepo
            .Setup(r => r.GetByIdWithConnectionAsync(jobRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobRun?)null);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRunId, ConnectionId = Guid.NewGuid() },
            CancellationToken.None);

        _clientFactory.Verify(f => f.Create(It.IsAny<SystemChannelTemplate>()), Times.Never);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Terminal statuses skip processing
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(JobStatus.Success)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    public async Task ProcessAsync_TerminalStatus_SkipsWithoutAction(JobStatus terminalStatus)
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, terminalStatus);

        _jobRunRepo
            .Setup(r => r.GetByIdWithConnectionAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        _clientFactory.Verify(f => f.Create(It.IsAny<SystemChannelTemplate>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // Inactive connection -> fail job
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_InactiveConnection_FailsJob()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template, isActive: false);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Failed);
        jobRun.Error.Should().NotBeNullOrEmpty();
        _jobRunRepo.Verify(r => r.UpdateAsync(jobRun, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _clientFactory.Verify(f => f.Create(It.IsAny<SystemChannelTemplate>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // Empty result (zero cards) -> single batch, isLastBatch = true
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_EmptyResult_PublishesOneBatchAndFinishes()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot>(), nextCursor: null));

        List<ProductBatchImportRequestedEvent> published = new List<ProductBatchImportRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductBatchImportRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        published.Should().HaveCount(1);
        published[0].IsLastBatch.Should().BeTrue();
        published[0].Cards.Should().BeEmpty();
        published[0].TotalBatches.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Single page, cards present, no next cursor -> one batch marked last
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_SinglePage_PublishesOneBatchMarkedLast()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(1, 5), nextCursor: null));

        List<ProductBatchImportRequestedEvent> published = new List<ProductBatchImportRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductBatchImportRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        published.Should().HaveCount(1);
        published[0].IsLastBatch.Should().BeTrue();
        published[0].Cards.Should().HaveCount(5);
        published[0].TotalBatches.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Two non-empty pages, second has no next cursor -> two batches, second is last
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_TwoPages_PublishesTwoBatchesSecondIsLast()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        ProductCardsCursor cursor1 = new ProductCardsCursor { UpdatedAt = "2024-01-01T00:00:00Z", ProductId = 5 };

        SetupDefaults(jobRun);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(1, 5), cursor1));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 5),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(6, 3), nextCursor: null));

        List<ProductBatchImportRequestedEvent> published = new List<ProductBatchImportRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductBatchImportRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        published.Should().HaveCount(2);
        published[0].IsLastBatch.Should().BeFalse();
        published[0].Cards.Should().HaveCount(5);
        published[1].IsLastBatch.Should().BeTrue();
        published[1].Cards.Should().HaveCount(3);
        published[1].TotalBatches.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Non-empty page, then empty terminal page -> single batch (the buffered one)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_NonEmptyPageThenEmptyTerminalPage_PublishesSingleBatchMarkedLast()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        ProductCardsCursor cursor1 = new ProductCardsCursor { UpdatedAt = "2024-01-01T00:00:00Z", ProductId = 5 };

        SetupDefaults(jobRun);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(1, 5), cursor1));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 5),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot>(), nextCursor: null));

        List<ProductBatchImportRequestedEvent> published = new List<ProductBatchImportRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductBatchImportRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        published.Should().HaveCount(1);
        published[0].IsLastBatch.Should().BeTrue();
        published[0].Cards.Should().HaveCount(5);
        published[0].TotalBatches.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Two non-empty pages, then an empty terminal page -> two batches, second is last
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_TwoNonEmptyPagesThenEmptyTerminalPage_PublishesTwoBatchesSecondIsLast()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        ProductCardsCursor cursor1 = new ProductCardsCursor { UpdatedAt = "2024-01-01T00:00:00Z", ProductId = 5 };
        ProductCardsCursor cursor2 = new ProductCardsCursor { UpdatedAt = "2024-01-02T00:00:00Z", ProductId = 8 };

        SetupDefaults(jobRun);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(1, 5), cursor1));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 5),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(6, 3), cursor2));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 8),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot>(), nextCursor: null));

        List<ProductBatchImportRequestedEvent> published = new List<ProductBatchImportRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductBatchImportRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        published.Should().HaveCount(2);
        published[0].IsLastBatch.Should().BeFalse();
        published[0].Cards.Should().HaveCount(5);
        published[1].IsLastBatch.Should().BeTrue();
        published[1].Cards.Should().HaveCount(3);
        published[1].TotalBatches.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Redelivery mid-import -> resume from the cursor saved in JobCheckpoint
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_AlreadyRunning_ResumeFromSavedCheckpointCursor()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Running);

        ProductCardsCursor savedCursor = new ProductCardsCursor
        {
            UpdatedAt = "2024-06-01T00:00:00Z",
            ProductId = 999,
        };

        SetupDefaults(jobRun);

        _jobCheckpointRepo
            .Setup(r => r.GetByJobRunIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobCheckpoint
            {
                JobRunId = jobRun.Id,
                Payload = JsonSerializer.Serialize(savedCursor),
                UpdatedAt = DateTimeOffset.UtcNow,
            });

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor>(x => x.ProductId == 999),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot> { BuildCard(1000) }, nextCursor: null));

        List<ProductBatchImportRequestedEvent> published = new List<ProductBatchImportRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductBatchImportRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // Called with cursor from the saved checkpoint, not null.
        _apiClient.Verify(
            c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor>(x => x.ProductId == 999),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        published.Should().HaveCount(1);
        published[0].IsLastBatch.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // WB API failure -> job marked Failed, ProductsImportCompletedEvent published
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ApiError_FailsJobAndPublishesCompletedEvent()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFailureResponse("Authentication failed: Unauthorized", statusCode: 401));

        List<ProductsImportCompletedEvent> completedEvents = new List<ProductsImportCompletedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductsImportCompletedEvent, CancellationToken>((e, _) => completedEvents.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Failed);
        jobRun.Error.Should().Contain("Authentication failed");
        jobRun.CompletedAt.Should().NotBeNull();

        completedEvents.Should().ContainSingle();
        completedEvents[0].JobRunId.Should().Be(jobRun.Id);
        completedEvents[0].IsSuccess.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Checkpoint persisted after the first batch is published
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_TwoPages_SavesCheckpointAfterFirstBatch()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        ProductCardsCursor cursor1 = new ProductCardsCursor { UpdatedAt = "2024-01-01T00:00:00Z", ProductId = 5 };

        SetupDefaults(jobRun);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(1, 5), cursor1));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 5),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(6, 3), nextCursor: null));

        List<JobCheckpoint> savedCheckpoints = new List<JobCheckpoint>();
        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<JobCheckpoint, CancellationToken>((c, _) => savedCheckpoints.Add(c))
            .Returns(Task.CompletedTask);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        savedCheckpoints.Should().ContainSingle(
            "the checkpoint is only advanced for the first (buffered) batch once it is actually published");
        savedCheckpoints[0].JobRunId.Should().Be(jobRun.Id);

        ProductCardsCursor? persistedCursor = JsonSerializer.Deserialize<ProductCardsCursor>(savedCheckpoints[0].Payload);
        persistedCursor.Should().NotBeNull();
        persistedCursor!.ProductId.Should().Be(5, "it must checkpoint the cursor of the published batch, not the one just fetched");
    }

    // -----------------------------------------------------------------------
    // Three pages -> checkpoint always refers to the cursor of the published batch
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ThreePages_CheckpointsCursorOfPublishedBatch()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        ProductCardsCursor cursor1 = new ProductCardsCursor { UpdatedAt = "2024-01-01T00:00:00Z", ProductId = 5 };
        ProductCardsCursor cursor2 = new ProductCardsCursor { UpdatedAt = "2024-01-02T00:00:00Z", ProductId = 8 };

        SetupDefaults(jobRun);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(1, 5), cursor1));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 5),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(6, 3), cursor2));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 8),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot>(), nextCursor: null));

        List<JobCheckpoint> savedCheckpoints = new List<JobCheckpoint>();
        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<JobCheckpoint, CancellationToken>((c, _) => savedCheckpoints.Add(c))
            .Returns(Task.CompletedTask);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        savedCheckpoints.Should().ContainSingle(
            "only the first batch is buffered-then-published mid-loop; the final (terminal) batch is published on break without a further checkpoint write");

        ProductCardsCursor? persistedCursor = JsonSerializer.Deserialize<ProductCardsCursor>(savedCheckpoints[0].Payload);
        persistedCursor!.ProductId.Should().Be(5);
    }

    // -----------------------------------------------------------------------
    // Pending -> Running transition
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_PendingJob_TransitionsToRunning()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot>(), nextCursor: null));

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Running);
        jobRun.StartedAt.Should().NotBeNull();
        _jobRunRepo.Verify(r => r.UpdateAsync(jobRun, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // -----------------------------------------------------------------------
    // Large single page (> typical batch size) stays one batch - batching is per API response
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_MoreThan50Cards_PublishesSingleBatchWithAllCards()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(1, 100), nextCursor: null));

        List<ProductBatchImportRequestedEvent> published = new List<ProductBatchImportRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductBatchImportRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        published.Should().HaveCount(1);
        published[0].Cards.Should().HaveCount(100);
        published[0].IsLastBatch.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Multiple pages with large responses -> each page is a separate batch
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_MultiplePagesWithLargeResponses_EachPageIsSeparateBatch()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        ProductCardsCursor cursor1 = new ProductCardsCursor { UpdatedAt = "2024-01-01T00:00:00Z", ProductId = 50 };

        SetupDefaults(jobRun);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(1, 50), cursor1));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 50),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(51, 30), nextCursor: null));

        List<ProductBatchImportRequestedEvent> published = new List<ProductBatchImportRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductBatchImportRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        published.Should().HaveCount(2);
        published[0].Cards.Should().HaveCount(50);
        published[1].Cards.Should().HaveCount(30);
        published[1].IsLastBatch.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Terminal batch carries the correct total batch count
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ThreePages_TerminalBatchCarriesTotalBatchCount()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        ProductCardsCursor cursor1 = new ProductCardsCursor { UpdatedAt = "2024-01-01T00:00:00Z", ProductId = 50 };
        ProductCardsCursor cursor2 = new ProductCardsCursor { UpdatedAt = "2024-01-02T00:00:00Z", ProductId = 80 };

        SetupDefaults(jobRun);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(1, 50), cursor1));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 50),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(51, 30), cursor2));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 80),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(81, 17), nextCursor: null));

        List<ProductBatchImportRequestedEvent> published = new List<ProductBatchImportRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductBatchImportRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // Three API pages, each non-empty -> three separate batches: the look-ahead buffer
        // publishes a page only once the next fetch proves it was not terminal, one page behind
        // the page just received.
        published.Should().HaveCount(3);
        published[0].Cards.Should().HaveCount(50);
        published[0].IsLastBatch.Should().BeFalse();
        published[1].Cards.Should().HaveCount(30);
        published[1].IsLastBatch.Should().BeFalse();
        published[2].Cards.Should().HaveCount(17);
        published[2].IsLastBatch.Should().BeTrue();
        published[2].TotalBatches.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Cooperative cancellation: status re-read from the DB on every page, loop stops
    // after the current page instead of continuing until the cursor is exhausted
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_CancelledMidLoop_StopsAfterCurrentPageAndPublishesPartialBatchAsTerminal()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Running);

        ProductCardsCursor cursor1 = new ProductCardsCursor { UpdatedAt = "2024-01-01T00:00:00Z", ProductId = 5 };

        SetupDefaults(jobRun);

        // WB's cursor still reports more data is available...
        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(1, 5), cursor1));

        // ...but an operator cancelled the job while this page's HTTP call was in flight, so the
        // re-read status must force the loop to treat this page as terminal and stop.
        _jobRunRepo
            .Setup(r => r.GetStatusAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JobStatus.Cancelled);

        List<ProductBatchImportRequestedEvent> published = new List<ProductBatchImportRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductBatchImportRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // Only the one page already in flight was fetched; the loop did not continue to
        // whatever page WB's cursor would have pointed to next.
        _apiClient.Verify(
            c => c.GetProductCardsAsync(It.IsAny<ProductCardsCursor?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);

        published.Should().ContainSingle("the in-flight page's records must not be discarded on cancellation");
        published[0].Cards.Should().HaveCount(5);
        published[0].IsLastBatch.Should().BeTrue("cancellation forces the current page to be treated as terminal");
    }

    // -----------------------------------------------------------------------
    // RawApiResponse capture: ambient JobRunId is available to API client HTTP calls
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_WhileCallingApi_JobRunExecutionContextCarriesCurrentJobRunId()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        Guid? observedJobRunId = null;

        // RawApiResponseCaptureHandler reads _jobRunExecutionContext.JobRunId from inside the
        // HTTP pipeline, i.e. while the API client's call is in flight. The callback below
        // captures what the ambient value actually is at that exact moment, standing in for the
        // handler without pulling MassTransit/HttpClientFactory into a unit test.
        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback(() => observedJobRunId = _jobRunExecutionContext.JobRunId)
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot>(), nextCursor: null));

        ProcessProductsImportConsumer consumer = CreateConsumer();

        _jobRunExecutionContext.JobRunId.Should().BeNull("no import is running yet");

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        observedJobRunId.Should().Be(jobRun.Id, "the API call must be attributable to this JobRun");
    }

    [Fact]
    public async Task ProcessAsync_AfterCompletion_JobRunExecutionContextIsCleared()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot>(), nextCursor: null));

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // Leaking the ambient JobRunId past this call would let an unrelated later HTTP call
        // on the same thread (e.g. connection validation) be mis-attributed to this JobRun.
        _jobRunExecutionContext.JobRunId.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedException_StillClearsJobRunExecutionContext()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Failed);
        _jobRunExecutionContext.JobRunId.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_InactiveConnection_NeverSetsJobRunExecutionContext()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template, isActive: false);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        _clientFactory.Verify(f => f.Create(It.IsAny<SystemChannelTemplate>()), Times.Never);
        _jobRunExecutionContext.JobRunId.Should().BeNull();
    }
}
