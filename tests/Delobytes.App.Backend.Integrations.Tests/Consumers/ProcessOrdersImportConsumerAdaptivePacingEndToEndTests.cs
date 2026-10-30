using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Integrations.Infrastructure.Services;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using Delobytes.App.Backend.Integrations.Tests.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Exercises backfill and delta with the adaptive rate limiter and fixture-backed source.
/// </summary>
public class ProcessOrdersImportConsumerAdaptivePacingEndToEndTests
{
    private static readonly TimeSpan ObservedChangeTimeOfDay = TimeSpan.FromHours(12);
    private const string ApiKey = "wb-e2e-test-key";

    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);

    private readonly Mock<IJobRunRepository> _jobRunRepo = new();
    private readonly Mock<IJobCheckpointRepository> _jobCheckpointRepo = new();
    private readonly Mock<ISyncCheckpointRepository> _syncCheckpointRepo = new();
    private readonly Mock<IOrdersBackfillStateRepository> _backfillStateRepo = new();
    private readonly Mock<IChannelApiClientFactory> _clientFactory = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly JobRunExecutionContext _jobRunExecutionContext = new();

    private OrdersBackfillState? _persistedState;

    private static SystemChannelTemplate BuildWildberriesTemplate()
    {
        return new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "wildberries",
            DisplayName = "Wildberries",
            ApiBaseUrl = "https://suppliers-api.wildberries.ru",
            ApiVersion = "v3",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static Connection BuildConnection(SystemChannelTemplate template)
    {
        return new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            SystemChannelTemplateId = template.Id,
            Name = "Test WB",
            ApiKey = ApiKey,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            SystemChannelTemplate = template,
        };
    }

    private static JobRun BuildJobRun(Connection connection)
    {
        return new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            JobType = JobType.OrdersImport,
            Status = JobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            Connection = connection,
        };
    }

    private static List<ChannelOrderSnapshot> BuildOrdersForDay(DateTimeOffset dayFrom, int count = 2)
    {
        string dayKey = dayFrom.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        List<ChannelOrderSnapshot> orders = new List<ChannelOrderSnapshot>();

        for (int i = 0; i < count; i++)
        {
            string externalOrderId = $"ORDER-{dayKey}-{i}";

            orders.Add(new ChannelOrderSnapshot
            {
                ExternalOrderId = externalOrderId,
                ExternalOrderNumber = externalOrderId,
                OrderDate = dayFrom.AddHours(10),
                StatusChangedAt = dayFrom + ObservedChangeTimeOfDay,
                ExternalStatus = "new",
                Currency = "RUB",
                Lines = new List<ChannelOrderLineSnapshot>
                {
                    new ChannelOrderLineSnapshot
                    {
                        LineKey = externalOrderId,
                        ExternalLineId = externalOrderId,
                        ExternalProductId = $"nm-{externalOrderId}",
                        OfferId = $"SKU-{externalOrderId}",
                        ProductName = "Test Product",
                        Quantity = 1,
                        UnitPrice = 1000m,
                        BuyerUnitPrice = 950m,
                        UnitPriceBeforeDiscount = 1200m,
                        Currency = "RUB",
                        IsCancelled = false,
                        StatusChangedAt = dayFrom + ObservedChangeTimeOfDay,
                        ChannelReportedTotal = 1200m,
                    },
                },
            });
        }

        return orders;
    }

    private static int GetDayCountInScope()
    {
        return (int)ProcessOrdersImportConsumer.BackfillWindow.TotalDays + 1;
    }

    private static OrdersBackfillState Clone(OrdersBackfillState state)
    {
        return new OrdersBackfillState
        {
            Id = state.Id,
            TenantId = state.TenantId,
            ConnectionId = state.ConnectionId,
            NextDayToFetch = state.NextDayToFetch,
            OldestDayInScope = state.OldestDayInScope,
            IsCompleted = state.IsCompleted,
            CompletedAt = state.CompletedAt,
            CatchUpNextDay = state.CatchUpNextDay,
            HandoverWatermark = state.HandoverWatermark,
        };
    }

    private void UseStateStore()
    {
        _backfillStateRepo
            .Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _persistedState == null ? null : Clone(_persistedState));

        _backfillStateRepo
            .Setup(r => r.UpsertAsync(It.IsAny<OrdersBackfillState>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersBackfillState, CancellationToken>((state, _) => _persistedState = Clone(state))
            .Returns(Task.CompletedTask);
    }

    private void SetupRun(JobRun jobRun, FakeRateLimitedOrdersSource source)
    {
        _jobRunRepo
            .Setup(r => r.GetByIdWithConnectionAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        _jobRunRepo
            .Setup(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _jobCheckpointRepo
            .Setup(r => r.GetByJobRunIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobCheckpoint?)null);

        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(It.IsAny<Guid>(), JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _clientFactory
            .Setup(f => f.Create(It.IsAny<SystemChannelTemplate>()))
            .Returns(source);
    }

    private ProcessOrdersImportConsumer CreateConsumer(IWildberriesRateLimiter rateLimiter, int backfillRunBudgetSeconds = 1200)
    {
        WildberriesImportOptions options = new WildberriesImportOptions
        {
            MinOrdersWindow = TimeSpan.FromHours(1),
            BackfillRunBudgetSeconds = backfillRunBudgetSeconds,
            MaxInProcessWaitSeconds = 0,
        };

        return new ProcessOrdersImportConsumer(
            _jobRunRepo.Object,
            _jobCheckpointRepo.Object,
            _syncCheckpointRepo.Object,
            _backfillStateRepo.Object,
            _clientFactory.Object,
            _eventPublisher.Object,
            _jobRunExecutionContext,
            Options.Create(options),
            NullLogger<ProcessOrdersImportConsumer>.Instance,
            rateLimiter,
            _timeProvider,
            NullLoggerFactory.Instance,
            new JobRunLifecycle(_jobRunRepo.Object, _timeProvider),
            new OrdersContinuationScheduler(_jobRunRepo.Object, _timeProvider));
    }

    private OrdersImportCheckpointPayload? CaptureLastPayload()
    {
        OrdersImportCheckpointPayload? lastPayload = null;

        foreach (IInvocation invocation in _jobCheckpointRepo.Invocations)
        {
            if (invocation.Method.Name == nameof(IJobCheckpointRepository.UpsertAsync)
                && invocation.Arguments[0] is JobCheckpoint checkpoint)
            {
                lastPayload = JsonSerializer.Deserialize<OrdersImportCheckpointPayload>(checkpoint.Payload);
            }
        }

        return lastPayload;
    }

    private List<string> PublishedOrderIds()
    {
        List<string> ids = new List<string>();

        foreach (IInvocation invocation in _eventPublisher.Invocations)
        {
            if (invocation.Arguments.Count > 0
                && invocation.Arguments[0] is OrdersBatchImportRequestedEvent published)
            {
                ids.AddRange(published.Orders.Select(order => order.ExternalOrderId));
            }
        }

        return ids;
    }

    [Fact]
    public async Task ProcessAsync_WithRealAdaptiveRateLimiter_CompletesBackfillAcrossRunsThenTakesDeltaBranch()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);

        UseStateStore();

        WildberriesRateLimitOptions rateLimitOptions = new WildberriesRateLimitOptions
        {
            InitialIntervalSeconds = 1,
            MinIntervalSeconds = 0,
            SpeedUpAfterSuccesses = 1,
        };

        FakeWildberriesMethodThrottleRepository throttleRepository = new FakeWildberriesMethodThrottleRepository();
        WildberriesRateLimiter rateLimiter = new WildberriesRateLimiter(
            throttleRepository,
            Options.Create(rateLimitOptions),
            NullLogger<WildberriesRateLimiter>.Instance);

        FakeRateLimitedOrdersSource source = new FakeRateLimitedOrdersSource(
            rateLimiter,
            ApiKey,
            request => request.Flag == 1 ? BuildOrdersForDay(request.DateFrom) : new List<ChannelOrderSnapshot>());

        // --- run #1: only the first day clears the gate; the second is throttled mid-loop ---
        JobRun firstRun = BuildJobRun(connection);
        SetupRun(firstRun, source);

        ProcessOrdersImportConsumer consumer = CreateConsumer(rateLimiter);
        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = firstRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        _persistedState.Should().NotBeNull();
        _persistedState!.IsCompleted.Should().BeFalse("only the first day could clear the real pacing gate in one run");

        int expectedDays = GetDayCountInScope();

        WildberriesMethodThrottle? throttle = await throttleRepository.GetAsync(
            WildberriesKeyFingerprint.Compute(ApiKey), WildberriesApiMethod.SupplierOrders, CancellationToken.None);
        throttle.Should().NotBeNull();
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        // The limiter uses wall time; move its persisted slot instead of sleeping in the test.
        throttle!.NextRequestAllowedAt = DateTimeOffset.MinValue;
        await throttleRepository.UpsertAsync(throttle, CancellationToken.None);

        JobRun secondRun = BuildJobRun(connection);
        SetupRun(secondRun, source);

        ProcessOrdersImportConsumer secondConsumer = CreateConsumer(rateLimiter);
        await secondConsumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = secondRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        _persistedState.Should().NotBeNull();
        _persistedState!.IsCompleted.Should().BeTrue(
            "once the pace collapses to 0 after the first success, nothing gates the remaining days within the same run");

        List<string> publishedIds = PublishedOrderIds();
        publishedIds.Should().HaveCount((expectedDays + 1) * 2);
        publishedIds.Distinct().Should().HaveCount(expectedDays * 2);
        _persistedState.HandoverWatermark.Should().Be(_timeProvider.GetUtcNow() - ProcessOrdersImportConsumer.SafetyOverlap);

        OrdersImportCheckpointPayload? lastPayload = CaptureLastPayload();
        lastPayload.Should().NotBeNull();
        lastPayload!.IsBackfill.Should().BeTrue();
        lastPayload.IsBackfillCompleted.Should().BeTrue();
        lastPayload.HandoverWatermark.Should().Be(_persistedState.HandoverWatermark);
        lastPayload.MaxChangedAt.Should().BeNull("a backwards walk never claims the delta watermark itself");

        // --- run #3: the cursor is complete, so this run must take the delta branch ---
        JobRun thirdRun = BuildJobRun(connection);
        SetupRun(thirdRun, source);

        ProcessOrdersImportConsumer thirdConsumer = CreateConsumer(rateLimiter);
        await thirdConsumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = thirdRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        OrdersImportCheckpointPayload? deltaPayload = CaptureLastPayload();
        deltaPayload.Should().NotBeNull();
        deltaPayload!.IsBackfill.Should().BeFalse("the walk is complete; the next run must read the delta path, not re-walk the 90 days");
    }

    [Fact]
    public void DefaultBackfillRunBudget_StaysComfortablyBelowTheOrdersImportWatchdogTimeout()
    {
        // WatchdogHostedService fails any JobRun still Running once JobTypeConfiguration.TimeoutMinutes
        // elapses; OrdersImport seeds that to 30 minutes (DataSeeder.cs). BackfillRunBudgetSeconds
        // must stay well under that 1800s ceiling, or a run that legitimately uses its whole budget
        // would race the watchdog instead of stopping itself cleanly.
        const int ordersImportWatchdogTimeoutSeconds = 30 * 60;

        (WildberriesImportOptions.DefaultBackfillRunBudgetSeconds + WildberriesImportOptions.DefaultMaxInProcessWaitSeconds)
            .Should().BeLessThan(ordersImportWatchdogTimeoutSeconds);

        int safetyMarginSeconds = ordersImportWatchdogTimeoutSeconds
            - WildberriesImportOptions.DefaultBackfillRunBudgetSeconds - WildberriesImportOptions.DefaultMaxInProcessWaitSeconds;
        safetyMarginSeconds.Should().BeGreaterThanOrEqualTo(5 * 60, "the budget must leave comfortable room, not just technically fit");
    }
}
