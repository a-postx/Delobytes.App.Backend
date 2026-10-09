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
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public class ProcessProductsImportConsumerTests
{
    private readonly Mock<ISyncJobRepository> _syncJobRepo = new();
    private readonly Mock<IChannelApiClientFactory> _clientFactory = new();
    private readonly Mock<IChannelApiClient> _apiClient = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<ILogger<ProcessProductsImportConsumer>> _logger = new();

    private ProcessProductsImportConsumer CreateConsumer(int batchSize = 50)
    {
        WildberriesImportOptions options = new WildberriesImportOptions { BatchSize = batchSize };
        IOptions<WildberriesImportOptions> wrappedOptions = Options.Create(options);

        return new ProcessProductsImportConsumer(
            _syncJobRepo.Object,
            _clientFactory.Object,
            _eventPublisher.Object,
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
        publishedBatches[0].SyncJobId.Should().Be(syncJob.Id);
        publishedBatches[0].ChannelId.Should().Be(connection.ChannelId);
    }

    // -----------------------------------------------------------------------
    // Single page (51 cards, no next cursor) → one batch, isLastBatch = true
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_SinglePage_PublishesOneBatchMarkedLast()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        List<WildberriesCardSnapshot> cards = new List<WildberriesCardSnapshot>();
        for (int i = 1; i <= 51; i++)
        {
            cards.Add(BuildCard(i));
        }

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);
        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(cards, nextCursor: null));

        List<ProductImportBatchRequestedEvent> published = new List<ProductImportBatchRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductImportBatchRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer(batchSize: 50);

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        published.Should().HaveCount(1);
        published[0].IsLastBatch.Should().BeTrue();
        published[0].Cards.Should().HaveCount(51);
    }

    // -----------------------------------------------------------------------
    // Multi-page (two pages with cursor) → two batches, second is last
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_TwoPages_PublishesTwoBatchesSecondIsLast()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        List<WildberriesCardSnapshot> firstPage = new List<WildberriesCardSnapshot> { BuildCard(1), BuildCard(2) };
        List<WildberriesCardSnapshot> secondPage = new List<WildberriesCardSnapshot> { BuildCard(3) };

        ProductCardsCursor page1Cursor = new ProductCardsCursor
        {
            UpdatedAt = "2024-01-01T00:00:00Z",
            ProductId = 2,
        };

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(firstPage, nextCursor: page1Cursor));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 2),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(secondPage, nextCursor: null));

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
        published[0].Cards.Should().HaveCount(2);
        published[1].IsLastBatch.Should().BeTrue();
        published[1].Cards.Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Non-empty page followed by an empty terminal page -> ONE batch, marked last.
    //
    // Regression test for the Wildberries import counters bug: when the cursor is
    // exhausted WB answers with an empty page. Publishing that page as its own
    // IsLastBatch batch let the aggregator finalise the job with zero counters
    // while the real batch was still downloading photos.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_NonEmptyPageThenEmptyTerminalPage_PublishesSingleBatchMarkedLast()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        List<WildberriesCardSnapshot> cards = new List<WildberriesCardSnapshot>
        {
            BuildCard(1), BuildCard(2), BuildCard(3)
        };

        ProductCardsCursor page1Cursor = new ProductCardsCursor
        {
            UpdatedAt = "2024-01-01T00:00:00Z",
            ProductId = 3,
        };

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(cards, nextCursor: page1Cursor));

        // Cursor exhausted: WB returns an empty page, which must not become a batch.
        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 3),
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

        // Exactly one batch, carrying the three cards and flagged as terminal.
        published.Should().HaveCount(1, "пустой терминальный батч не публикуется");
        published[0].IsLastBatch.Should().BeTrue();
        published[0].Cards.Should().HaveCount(3);
        published[0].Cards.Select(c => c.NmId).Should().BeEquivalentTo(new long[] { 1, 2, 3 });
    }

    // -----------------------------------------------------------------------
    // Two non-empty pages then an empty terminal page -> two batches, the second marked last.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_TwoNonEmptyPagesThenEmptyTerminalPage_PublishesTwoBatchesSecondIsLast()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        List<WildberriesCardSnapshot> firstPage = new List<WildberriesCardSnapshot> { BuildCard(1), BuildCard(2) };
        List<WildberriesCardSnapshot> secondPage = new List<WildberriesCardSnapshot> { BuildCard(3) };

        ProductCardsCursor page1Cursor = new ProductCardsCursor
        {
            UpdatedAt = "2024-01-01T00:00:00Z",
            ProductId = 2,
        };

        ProductCardsCursor page2Cursor = new ProductCardsCursor
        {
            UpdatedAt = "2024-01-02T00:00:00Z",
            ProductId = 3,
        };

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(firstPage, nextCursor: page1Cursor));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 2),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(secondPage, nextCursor: page2Cursor));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 3),
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
        published[0].Cards.Should().HaveCount(2);
        published[1].IsLastBatch.Should().BeTrue();
        published[1].Cards.Should().HaveCount(1);
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

        ProductCardsCursor cursor = new ProductCardsCursor { UpdatedAt = "2024-01-01T00:00:00Z", ProductId = 10 };

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot> { BuildCard(1) }, nextCursor: cursor));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor>(x => x.ProductId == 10),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot> { BuildCard(2) }, nextCursor: null));

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Every cursor write is recorded, so a two-page run can be checked for the absence of an
        // intermediate checkpoint.
        List<string?> checkpoints = new List<string?>();
        _syncJobRepo
            .Setup(r => r.Update(It.IsAny<SyncJob>()))
            .Callback<SyncJob>(j => checkpoints.Add(j.NextCursor));

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // The job finished: the terminal write clears the cursor because there is nothing left to resume.
        syncJob.NextCursor.Should().BeNull();

        // Two pages are not enough to flush the buffer: page 1 goes out only when page 2 turns out
        // to be terminal, and that branch never checkpoints the page it pushes out. So no non-null
        // cursor is persisted anywhere in this run.
        List<string?> nonNullCheckpoints = checkpoints.Where(c => c != null).ToList();
        nonNullCheckpoints.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Intermediate checkpoint: with three pages the second page pushes the first one out of
    // the buffer, and that is the only place a non-null cursor is written. A two-page run
    // never reaches it — it goes straight to the terminal null checkpoint.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ThreePages_CheckpointsCursorOfPublishedBatch()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        ProductCardsCursor page1Cursor = new ProductCardsCursor { UpdatedAt = "2024-01-01T00:00:00Z", ProductId = 1 };
        ProductCardsCursor page2Cursor = new ProductCardsCursor { UpdatedAt = "2024-01-02T00:00:00Z", ProductId = 2 };

        _syncJobRepo
            .Setup(r => r.FindByIdWithConnectionAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);
        _syncJobRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot> { BuildCard(1) }, nextCursor: page1Cursor));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 1),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot> { BuildCard(2) }, nextCursor: page2Cursor));

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(x => x != null && x.ProductId == 2),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot> { BuildCard(3) }, nextCursor: null));

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        List<string?> checkpoints = new List<string?>();
        _syncJobRepo
            .Setup(r => r.Update(It.IsAny<SyncJob>()))
            .Callback<SyncJob>(j => checkpoints.Add(j.NextCursor));

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // The terminal write clears the cursor; a completed job has nothing to resume.
        syncJob.NextCursor.Should().BeNull();

        // Exactly one non-null checkpoint, and it points at the batch that was actually published
        // (page 1), not at the page that happened to be fetched next (page 2).
        List<string> nonNullCheckpoints = checkpoints.Where(c => c != null).Select(c => c!).ToList();
        nonNullCheckpoints.Should().HaveCount(1, "the first page is only pushed out of the buffer once the second page arrives");

        System.Text.Json.JsonSerializer
            .Deserialize<ProductCardsCursor>(nonNullCheckpoints[0])!
            .ProductId
            .Should().Be(1, "the checkpoint refers to the published batch, not the page fetched after it");
    }

    // -----------------------------------------------------------------------
    // Pending → Running transition on start
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
        _syncJobRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);
        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(new List<WildberriesCardSnapshot>(), nextCursor: null));
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        SyncJobStatus? statusAfterFirstSave = null;
        int saveCallCount = 0;
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                saveCallCount++;
                if (saveCallCount == 1)
                {
                    statusAfterFirstSave = syncJob.Status;
                }
            })
            .ReturnsAsync(1);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        statusAfterFirstSave.Should().Be(SyncJobStatus.Running);
        syncJob.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessAsync_MoreThan50Cards_PublishesSingleBatchWithAllCards()
    {
        // Arrange
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

        // Создаём 51 карточку - WB API может вернуть больше, чем batchSize в одном ответе
        List<WildberriesCardSnapshot> cards = new List<WildberriesCardSnapshot>();
        for (long i = 1; i <= 51; i++)
        {
            cards.Add(BuildCard(i));
        }

        _apiClient
            .Setup(c => c.GetProductCardsAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(cards, nextCursor: null));

        List<ProductImportBatchRequestedEvent> publishedBatches = new List<ProductImportBatchRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductImportBatchRequestedEvent, CancellationToken>((e, _) => publishedBatches.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer(batchSize: 50);

        // Act
        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // Assert
        publishedBatches.Should().HaveCount(1, "все карточки из одного API ответа публикуются как один батч");

        ProductImportBatchRequestedEvent batch = publishedBatches[0];
        batch.Cards.Should().HaveCount(51, "все 51 карточка должны быть в единственном батче");
        batch.IsLastBatch.Should().BeTrue("отсутствие курсора означает последний батч");

        // Проверяем наличие всех карточек
        batch.Cards.Select(c => c.NmId).Should().BeEquivalentTo(
            Enumerable.Range(1, 51).Select(i => (long)i),
            "все 51 карточка должны присутствовать");
    }

    [Fact]
    public async Task ProcessAsync_MultiplePagesWithLargeResponses_EachPageIsSeparateBatch()
    {
        // Arrange
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

        // Первая страница: 100 карточек с курсором
        List<WildberriesCardSnapshot> firstPageCards = new List<WildberriesCardSnapshot>();
        for (long i = 1; i <= 100; i++)
        {
            firstPageCards.Add(BuildCard(i));
        }

        ProductCardsCursor cursorAfterFirst = new ProductCardsCursor
        {
            UpdatedAt = "2024-01-10T12:00:00Z",
            ProductId = 100
        };

        // Вторая страница: 75 карточек, без курсора (последняя страница)
        List<WildberriesCardSnapshot> secondPageCards = new List<WildberriesCardSnapshot>();
        for (long i = 101; i <= 175; i++)
        {
            secondPageCards.Add(BuildCard(i));
        }

        // ИСПРАВЛЕНО: Setup для первого вызова (cursor == null)
        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(cur => cur == null),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(firstPageCards, cursorAfterFirst));

        // ИСПРАВЛЕНО: Setup для второго вызова (cursor != null)
        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.Is<ProductCardsCursor?>(cur => cur != null),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResponse(secondPageCards, nextCursor: null));

        List<ProductImportBatchRequestedEvent> publishedBatches = new List<ProductImportBatchRequestedEvent>();
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductImportBatchRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductImportBatchRequestedEvent, CancellationToken>((e, _) => publishedBatches.Add(e))
            .Returns(Task.CompletedTask);

        ProcessProductsImportConsumer consumer = CreateConsumer(batchSize: 50);

        // Act
        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // Assert
        publishedBatches.Should().HaveCount(2, "две страницы API приводят к двум батчам");

        // Первый батч: 100 карточек
        ProductImportBatchRequestedEvent firstBatch = publishedBatches[0];
        firstBatch.Cards.Should().HaveCount(100);
        firstBatch.IsLastBatch.Should().BeFalse();
        firstBatch.Cards.First().NmId.Should().Be(1);
        firstBatch.Cards.Last().NmId.Should().Be(100);

        // Второй батч: 75 карточек
        ProductImportBatchRequestedEvent secondBatch = publishedBatches[1];
        secondBatch.Cards.Should().HaveCount(75);
        secondBatch.IsLastBatch.Should().BeTrue();
        secondBatch.Cards.First().NmId.Should().Be(101);
        secondBatch.Cards.Last().NmId.Should().Be(175);
    }

    // -----------------------------------------------------------------------
    // Regression test for the Wildberries import counters bug (publisher side).
    //
    // 117 cards, BatchSize = 50 -> 3 batches: 50 + 50 + 17. Only the terminal batch
    // carries the total count, because the publisher cannot know it until pagination
    // finishes. Downstream, the aggregator uses that number to wait for all three
    // batch results instead of finalising on the small 17-card batch that happens to
    // finish first. If this number is wrong or zero, the job freezes at 17 forever.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ThreePages_TerminalBatchCarriesTotalBatchCount()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        SyncJob syncJob = BuildSyncJob(connection, SyncJobStatus.Pending);

        ProductCardsCursor page1Cursor = new ProductCardsCursor { UpdatedAt = "2024-02-01T00:00:00Z", ProductId = 50 };
        ProductCardsCursor page2Cursor = new ProductCardsCursor { UpdatedAt = "2024-02-02T00:00:00Z", ProductId = 100 };

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
}
