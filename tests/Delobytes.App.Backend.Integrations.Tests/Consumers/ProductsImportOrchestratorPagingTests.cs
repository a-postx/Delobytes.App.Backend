using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Application.Products;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// A page and a batch are two different sizes. These tests pin the split introduced for Ozon, whose
/// attributes endpoint returns up to 1000 full cards per page while a message must stay small: the
/// orchestrator fetches by page size, publishes by batch size, and reports the event count the
/// aggregator compares against its recorded batch rows.
/// </summary>
public class ProductsImportOrchestratorPagingTests
{
    private readonly Mock<IJobRunRepository> _jobRunRepo = new();
    private readonly Mock<IJobCheckpointRepository> _jobCheckpointRepo = new();
    private readonly Mock<IChannelApiClientFactory> _clientFactory = new();
    private readonly Mock<IChannelApiClient> _apiClient = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<ILogger<ProductsImportOrchestrator>> _logger = new();
    private readonly JobRunExecutionContext _executionContext = new();

    [Fact]
    public async Task ProcessAsync_1000CardPageWithBatchOf200_PublishesFiveEventsOfTwoHundred()
    {
        Harness harness = await RunAsync(pageSize: 1000, batchSize: 200, pages: new[]
        {
            Page(Cards(1, 1000), cursor: Cursor("c1")),
            Page(Cards(1, 0), cursor: null),
        });

        harness.Published.Should().HaveCount(5);
        harness.Published.Should().OnlyContain(batch => batch.Cards.Count == 200);
        harness.Published.Take(4).Should().OnlyContain(batch => !batch.IsLastBatch);
        harness.Published[4].IsLastBatch.Should().BeTrue();

        _apiClient.Verify(
            c => c.GetProductCardsAsync(It.IsAny<Guid>(), It.IsAny<ProductCardsCursor?>(), 1000, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessAsync_TerminalPublish_FinalChunkCarriesTheRunTotal()
    {
        Harness harness = await RunAsync(pageSize: 500, batchSize: 200, pages: new[]
        {
            Page(Cards(1, 250), cursor: Cursor("c1")),
            Page(Cards(1, 0), cursor: null),
        });

        harness.Published.Should().HaveCount(2);
        harness.Published[0].Cards.Should().HaveCount(200);
        harness.Published[0].IsLastBatch.Should().BeFalse();
        harness.Published[0].TotalBatches.Should().Be(0);

        harness.Published[1].Cards.Should().HaveCount(50);
        harness.Published[1].IsLastBatch.Should().BeTrue();
        harness.Published[1].TotalBatches.Should().Be(2);
    }

    [Fact]
    public async Task ProcessAsync_CheckpointAdvancesOnlyAfterAllChunksOfThePage()
    {
        // Page 1 is buffered, page 2 proves it was not terminal, so publishing page 1 must finish
        // (both chunks) before its cursor is checkpointed.
        Harness harness = await RunAsync(pageSize: 400, batchSize: 200, pages: new[]
        {
            Page(Cards(1, 400), cursor: Cursor("c1")),
            Page(Cards(1, 10), cursor: Cursor("c2")),
            Page(Cards(1, 0), cursor: null),
        });

        harness.Operations.Should().ContainInOrder("publish", "publish", "checkpoint");

        string checkpointPayload = harness.CheckpointPayloads.Should().ContainSingle().Subject;
        using JsonDocument payload = JsonDocument.Parse(checkpointPayload);
        payload.RootElement.GetProperty("Token").GetString().Should().Be("c1");
    }

    [Fact]
    public async Task ProcessAsync_PageSmallerThanBatch_PublishesOneEvent()
    {
        Harness harness = await RunAsync(pageSize: 1000, batchSize: 200, pages: new[]
        {
            Page(Cards(1, 50), cursor: null),
        });

        harness.Published.Should().ContainSingle();
        harness.Published[0].Cards.Should().HaveCount(50);
        harness.Published[0].IsLastBatch.Should().BeTrue();
        harness.Published[0].TotalBatches.Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_EmptyTerminalPageWithNothingBuffered_PublishesOneEmptyTerminalBatch()
    {
        Harness harness = await RunAsync(pageSize: 1000, batchSize: 200, pages: new[]
        {
            Page(Cards(1, 0), cursor: null),
        });

        harness.Published.Should().ContainSingle();
        harness.Published[0].Cards.Should().BeEmpty();
        harness.Published[0].IsLastBatch.Should().BeTrue();
        harness.Published[0].TotalBatches.Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_EmptyTerminalPageWithBufferedPage_PublishesOnlyThatPagesChunks()
    {
        Harness harness = await RunAsync(pageSize: 1000, batchSize: 200, pages: new[]
        {
            Page(Cards(1, 400), cursor: Cursor("c1")),
            Page(Cards(1, 0), cursor: null),
        });

        harness.Published.Should().HaveCount(2, "the buffered page is chunked and no empty batch is added");
        harness.Published.Should().OnlyContain(batch => batch.Cards.Count == 200);
        harness.Published[1].IsLastBatch.Should().BeTrue();
        harness.Published[1].TotalBatches.Should().Be(2);
    }

    [Fact]
    public async Task ProcessAsync_FailedPage_PublishesNothingAndFailsTheRun()
    {
        Harness harness = await RunAsync(pageSize: 1000, batchSize: 200, pages: new[]
        {
            Page(Cards(1, 400), cursor: Cursor("c1")),
            Failure("API is down"),
        });

        harness.Published.Should().BeEmpty("a failed page must not finalise the run");
        harness.JobRun.Status.Should().Be(JobStatus.Failed);
        harness.JobRun.Error.Should().Be("API is down");
    }

    private async Task<Harness> RunAsync(int pageSize, int batchSize, PageResponse[] pages)
    {
        SystemChannelTemplate template = new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "ozon",
            DisplayName = "Ozon",
            ApiBaseUrl = "https://api-seller.ozon.ru",
            ApiVersion = "v1",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Connection connection = new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            SystemChannelTemplateId = template.Id,
            Name = "Test Ozon",
            ApiKey = "test-key",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            SystemChannelTemplate = template,
        };

        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            JobType = JobType.OzonProductsImport,
            Status = JobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            Connection = connection,
        };

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

        List<string> checkpointPayloads = new List<string>();
        List<string> operations = new List<string>();

        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<JobCheckpoint, CancellationToken>((checkpoint, _) =>
            {
                checkpointPayloads.Add(checkpoint.Payload);
                operations.Add("checkpoint");
            })
            .Returns(Task.CompletedTask);

        _clientFactory.Setup(f => f.Create(template)).Returns(_apiClient.Object);

        int pageIndex = 0;

        _apiClient
            .Setup(c => c.GetProductCardsAsync(
                It.IsAny<Guid>(),
                It.IsAny<ProductCardsCursor?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                PageResponse page = pages[Math.Min(pageIndex, pages.Length - 1)];
                pageIndex++;

                return page.IsFailure
                    ? new ApiResponse<ProductCardsData>
                    {
                        IsSuccess = false,
                        ErrorMessage = page.ErrorMessage,
                        StatusCode = 500,
                        Timestamp = DateTimeOffset.UtcNow,
                        Data = new ProductCardsData(),
                    }
                    : new ApiResponse<ProductCardsData>
                    {
                        IsSuccess = true,
                        StatusCode = 200,
                        Timestamp = DateTimeOffset.UtcNow,
                        Data = new ProductCardsData
                        {
                            Cards = page.Cards,
                            NextCursor = page.NextCursor,
                            TotalCount = page.Cards.Count,
                        },
                    };
            });

        List<ProductBatchImportRequestedEvent> published = new List<ProductBatchImportRequestedEvent>();

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductBatchImportRequestedEvent, CancellationToken>((e, _) =>
            {
                published.Add(e);
                operations.Add("publish");
            })
            .Returns(Task.CompletedTask);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<JobCheckpoint, CancellationToken>((_, _) => operations.Add("checkpoint"))
            .Returns(Task.CompletedTask);

        // Restores the payload recording the second setup above replaced: one callback collects
        // both the payload and the ordering marker.
        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<JobCheckpoint, CancellationToken>((c, _) =>
            {
                checkpointPayloads.Add(c.Payload);
                operations.Add("checkpoint");
            })
            .Returns(Task.CompletedTask);

        IProductsImportTuningResolver tunings = new FixedTuningResolver(pageSize, batchSize);

        ProductsImportOrchestrator orchestrator = new ProductsImportOrchestrator(
            _jobRunRepo.Object,
            _jobCheckpointRepo.Object,
            _clientFactory.Object,
            _eventPublisher.Object,
            _executionContext,
            tunings,
            _logger.Object,
            TimeProvider.System);

        await orchestrator.ProcessAsync(
            new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        return new Harness(published, checkpointPayloads, operations, jobRun);
    }

    private static PageResponse Page(List<ChannelProductCard> cards, ProductCardsCursor? cursor)
    {
        return new PageResponse
        {
            Cards = cards,
            NextCursor = cursor,
        };
    }

    private static PageResponse Failure(string errorMessage)
    {
        return new PageResponse
        {
            IsFailure = true,
            ErrorMessage = errorMessage,
        };
    }

    private static ProductCardsCursor Cursor(string token)
    {
        return new ProductCardsCursor { Token = token };
    }

    private static List<ChannelProductCard> Cards(long firstId, int count)
    {
        List<ChannelProductCard> cards = new List<ChannelProductCard>();

        for (int index = 0; index < count; index++)
        {
            cards.Add(new ChannelProductCard
            {
                ExternalProductId = (firstId + index).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Name = "Card " + (firstId + index),
                ChannelCode = "ozon",
                BarcodeType = "ozon",
            });
        }

        return cards;
    }

    private sealed class FixedTuningResolver : IProductsImportTuningResolver
    {
        private readonly int _pageSize;
        private readonly int _batchSize;

        public FixedTuningResolver(int pageSize, int batchSize)
        {
            _pageSize = pageSize;
            _batchSize = batchSize;
        }

        public ProductsImportTuning Resolve(string channelCode)
        {
            return new ProductsImportTuning(_pageSize, _batchSize);
        }
    }

    private sealed class PageResponse
    {
        public List<ChannelProductCard> Cards { get; init; } = new();

        public ProductCardsCursor? NextCursor { get; init; }

        public bool IsFailure { get; init; }

        public string? ErrorMessage { get; init; }
    }

    private sealed record Harness(
        List<ProductBatchImportRequestedEvent> Published,
        List<string> CheckpointPayloads,
        List<string> Operations,
        JobRun JobRun);
}
