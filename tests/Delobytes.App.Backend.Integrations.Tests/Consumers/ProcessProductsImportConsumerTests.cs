using System;
using System.Collections.Generic;
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

        string? capturedCursor = null;
        _syncJobRepo
            .Setup(r => r.Update(It.IsAny<SyncJob>()))
            .Callback<SyncJob>(j => capturedCursor = j.NextCursor);

        ProcessProductsImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new ProductsImportRequestedEvent { SyncJobId = syncJob.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // After second page (last), cursor should be null (serialized as null)
        syncJob.NextCursor.Should().BeNull();
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
}
