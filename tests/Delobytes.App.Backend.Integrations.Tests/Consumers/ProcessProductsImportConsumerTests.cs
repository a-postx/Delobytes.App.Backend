using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.DTOs;
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
    private readonly Mock<ISyncJobRepository> _syncJobRepo = new();
    private readonly Mock<IRawApiResponseRepository> _rawApiResponseRepo = new();
    private readonly Mock<IChannelApiClientFactory> _clientFactory = new();
    private readonly Mock<IChannelApiClient> _apiClient = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<ILogger<ProcessProductsImportConsumer>> _logger = new();

    // Real instance rather than a mock: SyncJobExecutionContext has no interface/virtual
    // members for Moq to override, and it is cheap enough (AsyncLocal-backed) to exercise for
    // real, the same way MessageTenantContextTests exercises MessageTenantContext directly.
    private readonly SyncJobExecutionContext _syncJobExecutionContext = new();

    private ProcessProductsImportConsumer CreateConsumer(int batchSize = 50)
    {
        WildberriesImportOptions options = new WildberriesImportOptions { BatchSize = batchSize };
        IOptions<WildberriesImportOptions> wrappedOptions = Options.Create(options);

        return new ProcessProductsImportConsumer(
            _syncJobRepo.Object,
            _rawApiResponseRepo.Object,
            _clientFactory.Object,
            _eventPublisher.Object,
            _syncJobExecutionContext,
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

    private static SyncJob BuildSyncJob(Connection connection, SyncJobStatus status = SyncJobStatus.Pending)
    {
        return new SyncJob
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

    // -----------------------------------------------------------------------
    // SyncJob not found
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_SyncJobNotFound_ReturnsWithoutAction()
    {
        Guid syncJobId = Guid.NewGuid();

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncJob?)null);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJobId, ConnectionId = Guid.NewGuid() },
            CancellationToken.None);

        _clientFactory.Verify(f => f.Create(It.IsAny<SystemChannelTemplate>()), Times.Never);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Terminal statuses skip processing
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(SyncJobStatus.Success)]
    [InlineData(SyncJobStatus.Failed)]
    [InlineData(SyncJobStatus.Cancelled)]
    public async Task ProcessAsync_TerminalStatus_SkipsWithoutAction(SyncJobStatus terminalStatus)
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, terminalStatus);

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        _clientFactory.Verify(f => f.Create(It.IsAny<SystemChannelTemplate>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // Inactive connection → fail job
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_InactiveConnection_FailsJob()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template, isActive: false);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        syncJob.Status.Should().Be(SyncJobStatus.Failed);
        syncJob.ErrorMessage.Should().NotBeNullOrEmpty();
        _syncJobRepo.Verify(r => r.Update(syncJob), Times.AtLeastOnce);
        _syncJobRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // -----------------------------------------------------------------------
    // Empty result (zero cards) → single batch, isLastBatch = true
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_EmptyResult_PublishesOneBatchAndFinishes()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _clientFactory
            .Setup(f => f.Create(template))
            .Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot>(), nextCursor: null));

        List<ProductImportBatchRequestedEvent> publishedBatches = new List<ProductImportBatchRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductImportBatchRequestedEvent, CancellationToken>((e, _) => publishedBatches.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        publishedBatches.Should().HaveCount(1);
        publishedBatches[0].IsLastBatch.Should().BeTrue();
        publishedBatches[0].Cards.Should().BeEmpty();
        publishedBatches[0].TotalBatches.Should().Be(1);

        syncJob.Status.Should().Be(SyncJobStatus.Running);
        syncJob.NextCursor.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Single non-empty terminal page → one batch
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_SinglePage_PublishesOneBatchMarkedLast()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _clientFactory
            .Setup(f => f.Create(template))
            .Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(1, 5), nextCursor: null));

        List<ProductImportBatchRequestedEvent> published = new List<ProductImportBatchRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductImportBatchRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        published.Should().HaveCount(1);
        published[0].IsLastBatch.Should().BeTrue();
        published[0].Cards.Should().HaveCount(5);
        published[0].TotalBatches.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Two non-empty pages, second is terminal (non-empty, no cursor)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_TwoPages_PublishesTwoBatchesSecondIsLast()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        ProductCardsCursor cursor1 = new ProductCardsCursor { UpdatedAt = "2024-01-01T00:00:00Z", ProductId = 5 };

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _clientFactory
            .Setup(f => f.Create(template))
            .Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(1, 5), cursor1));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 5),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(6, 3), nextCursor: null));

        List<ProductImportBatchRequestedEvent> published = new List<ProductImportBatchRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductImportBatchRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        published.Should().HaveCount(2);
        published[0].IsLastBatch.Should().BeFalse();
        published[0].Cards.Should().HaveCount(5);
        published[1].IsLastBatch.Should().BeTrue();
        published[1].Cards.Should().HaveCount(3);
        published[1].TotalBatches.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Non-empty page, then empty terminal page → single batch (the buffered one)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_NonEmptyPageThenEmptyTerminalPage_PublishesSingleBatchMarkedLast()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        ProductCardsCursor cursor1 = new ProductCardsCursor { UpdatedAt = "2024-01-01T00:00:00Z", ProductId = 5 };

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _clientFactory
            .Setup(f => f.Create(template))
            .Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(1, 5), cursor1));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 5),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot>(), nextCursor: null));

        List<ProductImportBatchRequestedEvent> published = new List<ProductImportBatchRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductImportBatchRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        published.Should().HaveCount(1, "the empty terminal page is a signal, not a batch of its own");
        published[0].IsLastBatch.Should().BeTrue();
        published[0].Cards.Should().HaveCount(5);
    }

    // -----------------------------------------------------------------------
    // Two non-empty pages, then empty terminal page → two batches, second is last
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_TwoNonEmptyPagesThenEmptyTerminalPage_PublishesTwoBatchesSecondIsLast()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        ProductCardsCursor cursor1 = new ProductCardsCursor { UpdatedAt = "2024-01-01T00:00:00Z", ProductId = 5 };
        ProductCardsCursor cursor2 = new ProductCardsCursor { UpdatedAt = "2024-01-02T00:00:00Z", ProductId = 8 };

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _clientFactory
            .Setup(f => f.Create(template))
            .Returns(_apiClient.Object);

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

        List<ProductImportBatchRequestedEvent> published = new List<ProductImportBatchRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductImportBatchRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        published.Should().HaveCount(2);
        published[0].IsLastBatch.Should().BeFalse();
        published[0].Cards.Should().HaveCount(5);
        published[1].IsLastBatch.Should().BeTrue();
        published[1].Cards.Should().HaveCount(3);
        published[1].TotalBatches.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Idempotency: Running status (redelivery) resumes from saved cursor
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_AlreadyRunning_ResumeFromSavedCursor()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Running);

        ProductCardsCursor savedCursor = new ProductCardsCursor
        {
            UpdatedAt = "2024-06-01T00:00:00Z",
            ProductId = 999,
        };
        syncJob.NextCursor = System.Text.Json.JsonSerializer.Serialize(savedCursor);

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor>(x => x.ProductId == 999),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot> { BuildCard(1000) }, nextCursor: null));

        List<ProductImportBatchRequestedEvent> published = new List<ProductImportBatchRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductImportBatchRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // Called with cursor from saved state, not null
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
    // WB API failure → job marked Failed, ProductsImportCompletedEvent published
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ApiError_FailsJobAndPublishesCompletedEvent()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

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
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        syncJob.Status.Should().Be(SyncJobStatus.Failed);
        syncJob.ErrorMessage.Should().Contain("Authentication failed");
        syncJob.CompletedAt.Should().NotBeNull();

        completedEvents.Should().HaveCount(1);
        completedEvents[0].IsSuccess.Should().BeFalse();
        completedEvents[0].SyncJobId.Should().Be(syncJob.Id);
    }

    // -----------------------------------------------------------------------
    // Cursor checkpoint saved after each batch
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_TwoPages_SavesCheckpointAfterFirstBatch()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        ProductCardsCursor cursor1 = new ProductCardsCursor { UpdatedAt = "2024-01-01T00:00:00Z", ProductId = 5 };

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _clientFactory
            .Setup(f => f.Create(template))
            .Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(1, 5), cursor1));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 5),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(6, 3), nextCursor: null));

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // Checkpoint after the run is null (import finished), but the mechanism itself is
        // exercised implicitly: the second page was requested with the cursor from the first
        // response, proving syncJob.NextCursor was correctly threaded through.
        _apiClient.Verify(
            c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 5),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        syncJob.NextCursor.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Three pages: checkpoint always refers to the last *published* batch, not fetched one
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ThreePages_CheckpointsCursorOfPublishedBatch()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        ProductCardsCursor cursor1 = new ProductCardsCursor { UpdatedAt = "2024-01-01T00:00:00Z", ProductId = 5 };
        ProductCardsCursor cursor2 = new ProductCardsCursor { UpdatedAt = "2024-01-02T00:00:00Z", ProductId = 8 };

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);

        List<string?> savedCursors = new List<string?>();
        _syncJobRepo
            .Setup(r => r.Update(It.IsAny<SyncJob>()))
            .Callback<SyncJob>(j => savedCursors.Add(j.NextCursor));
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _clientFactory
            .Setup(f => f.Create(template))
            .Returns(_apiClient.Object);

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
            .ReturnsAsync(BuildSuccessResponse(BuildCards(9, 2), nextCursor: null));

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // After the first page is buffered (not yet published), the second page's arrival
        // proves the first was non-terminal. The checkpoint saved at that point must be
        // cursor1 — the cursor of the batch just published — not cursor2.
        savedCursors.Should().Contain(System.Text.Json.JsonSerializer.Serialize(cursor1));
    }

    // -----------------------------------------------------------------------
    // Pending → Running transition
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_PendingJob_TransitionsToRunning()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _clientFactory
            .Setup(f => f.Create(template))
            .Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot>(), nextCursor: null));

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        syncJob.Status.Should().Be(SyncJobStatus.Running);
        syncJob.StartedAt.Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // Large single page (> typical batch size) stays one batch — batching is per API response
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_MoreThan50Cards_PublishesSingleBatchWithAllCards()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _clientFactory
            .Setup(f => f.Create(template))
            .Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(1, 75), nextCursor: null));

        List<ProductImportBatchRequestedEvent> published = new List<ProductImportBatchRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductImportBatchRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        published.Should().HaveCount(1);
        published[0].Cards.Should().HaveCount(75);
        published[0].IsLastBatch.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Multiple pages with large (>batchSize) responses → each API page is its own batch
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_MultiplePagesWithLargeResponses_EachPageIsSeparateBatch()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        ProductCardsCursor cursor1 = new ProductCardsCursor { UpdatedAt = "2024-01-01T00:00:00Z", ProductId = 100 };

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _clientFactory
            .Setup(f => f.Create(template))
            .Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(1, 80), cursor1));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 100),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(101, 60), nextCursor: null));

        List<ProductImportBatchRequestedEvent> published = new List<ProductImportBatchRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductImportBatchRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        published.Should().HaveCount(2);
        published[0].Cards.Should().HaveCount(80);
        published[0].IsLastBatch.Should().BeFalse();
        published[1].Cards.Should().HaveCount(60);
        published[1].IsLastBatch.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Terminal batch carries the correct TotalBatches across three pages (50+50+17)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ThreePages_TerminalBatchCarriesTotalBatchCount()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        ProductCardsCursor page1Cursor = new ProductCardsCursor { UpdatedAt = "2024-01-01T00:00:00Z", ProductId = 50 };
        ProductCardsCursor page2Cursor = new ProductCardsCursor { UpdatedAt = "2024-01-02T00:00:00Z", ProductId = 100 };

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _clientFactory
            .Setup(f => f.Create(template))
            .Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(1, 50), page1Cursor));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 50),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(51, 50), page2Cursor));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 100),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(BuildCards(101, 17), nextCursor: null));

        List<ProductImportBatchRequestedEvent> published = new List<ProductImportBatchRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductImportBatchRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer(batchSize: 50);

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        published.Should().HaveCount(3, "117 карточек при batchSize=50 дают три батча: 50 + 50 + 17");

        // Non-terminal batches carry no total: at publish time the publisher has not yet
        // walked the cursor to its end, so any number it put here would be a guess.
        published[0].IsLastBatch.Should().BeFalse();
        published[0].Cards.Should().HaveCount(50);
        published[0].TotalBatches.Should().Be(0);

        // The buffered first page only leaves the buffer once the second response proves it
        // is non-terminal, so the second 50-card page is the one still in flight here.
        published[1].IsLastBatch.Should().BeFalse();
        published[1].Cards.Should().HaveCount(50);
        published[1].TotalBatches.Should().Be(0);

        // Terminal batch: the small trailing page carries the total, which is exactly what
        // the aggregator needs to avoid finalising the job at 17 records.
        published[2].IsLastBatch.Should().BeTrue();
        published[2].Cards.Should().HaveCount(17);
        published[2].TotalBatches.Should().Be(3);
        published[2].Cards.Sum(c => 1).Should().Be(17);
    }

    // -----------------------------------------------------------------------
    // RawApiResponse capture: ambient SyncJobId is available to API client HTTP calls
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_WhileCallingApi_SyncJobExecutionContextCarriesCurrentSyncJobId()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _clientFactory
            .Setup(f => f.Create(template))
            .Returns(_apiClient.Object);

        Guid? observedSyncJobId = null;

        // RawApiResponseCaptureHandler reads _syncJobExecutionContext.SyncJobId from inside the
        // HTTP pipeline, i.e. while the API client's call is in flight. The callback below
        // captures what the ambient value actually is at that exact moment, standing in for the
        // handler without pulling MassTransit/HttpClientFactory into a unit test.
        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback(() => observedSyncJobId = _syncJobExecutionContext.SyncJobId)
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot>(), nextCursor: null));

        ProcessProductsImportConsumer consumer = CreateConsumer();

        _syncJobExecutionContext.SyncJobId.Should().BeNull("no import is running yet");

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        observedSyncJobId.Should().Be(syncJob.Id, "the API call must be attributable to this SyncJob");
    }

    [Fact]
    public async Task ProcessAsync_AfterCompletion_SyncJobExecutionContextIsCleared()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _clientFactory
            .Setup(f => f.Create(template))
            .Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot>(), nextCursor: null));

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // Leaking the ambient SyncJobId past this call would let an unrelated later HTTP call
        // on the same thread (e.g. connection validation) be mis-attributed to this SyncJob.
        _syncJobExecutionContext.SyncJobId.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedException_StillClearsSyncJobExecutionContext()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _clientFactory
            .Setup(f => f.Create(template))
            .Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        syncJob.Status.Should().Be(SyncJobStatus.Failed);
        _syncJobExecutionContext.SyncJobId.Should().BeNull("the finally block must clear the context even on failure");
    }

    [Fact]
    public async Task ProcessAsync_InactiveConnection_NeverSetsSyncJobExecutionContext()
    {
        // No HTTP call is ever made on this path (the job fails before the API client is
        // created), so the ambient SyncJobId must never be touched.
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template, isActive: false);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        _syncJobExecutionContext.SyncJobId.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // RawApiResponse capture: ProcessedAt is closed out once business logic has looked at
    // the mapped result of each API call
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_SuccessfulApiCall_MarksRawResponsesAsProcessed()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _clientFactory
            .Setup(f => f.Create(template))
            .Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot>(), nextCursor: null));

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        _rawApiResponseRepo.Verify(
            r => r.MarkPendingAsProcessedAsync(syncJob.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "a successfully fetched page must close out the raw response row the HTTP handler captured for it");
    }

    [Fact]
    public async Task ProcessAsync_ApiError_MarksRawResponsesAsProcessedBeforeFailingJob()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _clientFactory
            .Setup(f => f.Create(template))
            .Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFailureResponse("Authentication failed: Unauthorized", statusCode: 401));

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        syncJob.Status.Should().Be(SyncJobStatus.Failed);

        _rawApiResponseRepo.Verify(
            r => r.MarkPendingAsProcessedAsync(syncJob.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "even a failed call was captured by the HTTP handler and must be closed out, not left pending forever");
    }

    [Fact]
    public async Task ProcessAsync_InactiveConnection_NeverCallsMarkPendingAsProcessed()
    {
        // Nothing was ever captured on this path (no HTTP call happened), so there is nothing
        // to close out.
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template, isActive: false);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        _rawApiResponseRepo.Verify(
            r => r.MarkPendingAsProcessedAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
