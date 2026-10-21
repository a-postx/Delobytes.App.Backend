using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;


namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public partial class ProcessOrdersBatchImportCompletedConsumerTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ProcessAsync_FetchIncomplete_DoesNotFinaliseEvenAtOrAbovePublishedCount(int recorded)
    {
        JobRun run = BuildJobRun();
        SetupDefaults(run, totalBatches: 1, recordedBatches: recorded);
        _jobCheckpointRepo.Setup(r => r.GetByJobRunIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new JobCheckpoint
        {
            JobRunId = run.Id,
            Payload = JsonSerializer.Serialize(new OrdersImportCheckpointPayload { TotalBatches = 1, IsFetchCompleted = false }),
        });
        OrdersBatchImportCompletedEvent message = BuildEvent(run.Id);

        await CreateConsumer().ProcessAsync(message, message.BatchId, CancellationToken.None);

        run.Status.Should().Be(JobStatus.Running);
        run.CompletedAt.Should().BeNull();
        _jobRunRepo.Verify(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _syncCheckpointRepo.Verify(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ProcessAsync_TerminalResultInAnyPosition_FinalisesOnlyAfterAllResults(int terminalPosition)
    {
        JobRun run = BuildJobRun();
        SetupDefaults(run, totalBatches: 3);
        int recorded = 0;
        _jobRunRepo.Setup(r => r.CountBatchResultsAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => recorded);
        _jobCheckpointRepo.Setup(r => r.GetByJobRunIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new JobCheckpoint
        {
            JobRunId = run.Id,
            Payload = JsonSerializer.Serialize(new OrdersImportCheckpointPayload { TotalBatches = 3, IsFetchCompleted = true, MaxChangedAt = FetchedMaxChangedAt }),
        });
        _jobRunRepo.Setup(r => r.GetBatchResultTotalsAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(BuildTotals(2, 0, 0, 0));
        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        for (int index = 0; index < 3; index++)
        {
            OrdersBatchImportCompletedEvent message = BuildEvent(run.Id, created: index == terminalPosition ? 0 : 1, updated: 0, skipped: 0);
            recorded++;
            await consumer.ProcessAsync(message, message.BatchId, CancellationToken.None);
            if (index < 2)
            {
                run.Status.Should().Be(JobStatus.Running);
                _jobRunRepo.Verify(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
            }
        }

        run.Status.Should().Be(JobStatus.Success);
        run.RecordsCreated.Should().Be(2);
        _jobRunRepo.Verify(r => r.UpdateAsync(run, It.IsAny<CancellationToken>()), Times.Once);
        _syncCheckpointRepo.Verify(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_LegacyJsonWithoutFetchCompleted_Finalises()
    {
        JobRun run = BuildJobRun();
        SetupDefaults(run);
        _jobRunRepo.Setup(r => r.GetBatchResultTotalsAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(BuildTotals(3, 1, 1, 0));
        _jobCheckpointRepo.Setup(r => r.GetByJobRunIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new JobCheckpoint
        {
            JobRunId = run.Id,
            Payload = "{\"TotalBatches\":1}",
        });
        OrdersBatchImportCompletedEvent message = BuildEvent(run.Id);

        await CreateConsumer().ProcessAsync(message, message.BatchId, CancellationToken.None);

        run.Status.Should().Be(JobStatus.Success);
        _jobRunRepo.Verify(r => r.UpdateAsync(run, It.IsAny<CancellationToken>()), Times.Once);
    }
}
