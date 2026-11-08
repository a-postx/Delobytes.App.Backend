using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;
using Delobytes.App.Backend.Integrations.Application.Orders;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Tests the per-schema Ozon window readers: each reader pins the <see cref="OrdersSchema"/> it puts
/// on the request and paces on its own API method, so a job type can never be served the sibling
/// schema's endpoint or the sibling's rate-limit bucket.
/// </summary>
public class OzonSchemaReadersTests
{
    private static readonly DateTimeOffset WindowFrom = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.FromHours(3));

    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Records every <see cref="RateLimitScope"/> the paced source asks about. The source does not
    /// reserve a slot itself - the API client does - so the scope it paces on is observable only
    /// through <see cref="GetNextRequestAllowedAtAsync"/>, which it consults on the budget path.
    /// </summary>
    private sealed class RecordingRateLimiter : IOrdersRateLimiter
    {
        public List<RateLimitScope> Scopes { get; } = new List<RateLimitScope>();

        public Task<RateLimitAcquireResult> TryAcquireAsync(RateLimitScope scope, CancellationToken cancellationToken)
        {
            Scopes.Add(scope);

            return Task.FromResult(new RateLimitAcquireResult
            {
                Acquired = true,
                NextRequestAllowedAt = DateTimeOffset.UtcNow,
            });
        }

        public Task<DateTimeOffset> ReportRateLimitedAsync(
            RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken)
        {
            return Task.FromResult(DateTimeOffset.UtcNow);
        }

        public Task ReportSuccessAsync(
            RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<DateTimeOffset?> GetNextRequestAllowedAtAsync(
            RateLimitScope scope, CancellationToken cancellationToken)
        {
            Scopes.Add(scope);
            DateTimeOffset? result = null;

            return Task.FromResult(result);
        }
    }

    private sealed class SinglePageOrdersSource : IOrdersSource
    {
        public List<OrdersRequest> Requests { get; } = new List<OrdersRequest>();

        public Task<OrdersPage> GetOrdersAsync(Guid connectionId, OrdersRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(new OrdersPage
            {
                Orders = new List<ChannelOrderSnapshot>(),
                HasMore = false,
            });
        }
    }

    private static Connection BuildConnection()
    {
        return new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            ApiKey = "client-id",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            SystemChannelTemplate = new SystemChannelTemplate { Code = "ozon" },
        };
    }

    private static OrdersRequest BuildRequest(OrdersSchema? schema)
    {
        return new OrdersRequest
        {
            DateFrom = WindowFrom,
            DateTo = WindowFrom.AddDays(1).AddSeconds(-1),
            Selection = OrdersSelectionMode.Placed,
            Limit = 100,
            Schema = schema,
        };
    }

    private (OrdersImportContext Context, RunCounters Progress) BuildRun(
        SinglePageOrdersSource source, JobType jobType, bool deadlineAlreadyPassed = false)
    {
        Connection connection = BuildConnection();

        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            JobType = jobType,
            Status = JobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            Connection = connection,
        };

        DateTimeOffset deadline = deadlineAlreadyPassed
            ? _timeProvider.GetUtcNow().AddMinutes(-1)
            : _timeProvider.GetUtcNow().AddMinutes(10);

        OrdersImportContext context = OrdersImportContextBuilder.Build(
            jobRun: jobRun,
            connection: connection,
            ordersSource: source,
            tuning: TestTunings.Ozon(BuildOptions(), jobType),
            deadline: deadline,
            windowTo: WindowFrom.AddDays(1));

        return (context, new RunCounters());
    }

    private static OzonImportOptions BuildOptions()
    {
        return new OzonImportOptions { MaxInProcessWaitSeconds = 0 };
    }

    private CursorPagedOrdersReader CreateFboReader(RecordingRateLimiter limiter)
    {
        return new CursorPagedOrdersReader(
            new PacedOrdersSource(limiter, _timeProvider),
            NullLogger<CursorPagedOrdersReader>.Instance,
            TestTunings.Ozon(BuildOptions()),
            OrdersSchema.Fbo);
    }

    private CursorPagedOrdersReader CreateFbsReader(RecordingRateLimiter limiter)
    {
        return new CursorPagedOrdersReader(
            new PacedOrdersSource(limiter, _timeProvider),
            NullLogger<CursorPagedOrdersReader>.Instance,
            TestTunings.Ozon(BuildOptions(), JobType.OzonOrdersImportFbs),
            OrdersSchema.Fbs);
    }

    [Fact]
    public async Task FboReader_PinsFboSchemaOnTheIssuedRequest()
    {
        SinglePageOrdersSource source = new SinglePageOrdersSource();
        (OrdersImportContext context, RunCounters progress) = BuildRun(source, JobType.OzonOrdersImportFbo);

        FetchOutcome outcome = await CreateFboReader(new RecordingRateLimiter()).ReadWindowAsync(
            BuildRequest(OrdersSchema.Fbs), context, progress, CancellationToken.None);

        outcome.Should().BeOfType<FetchOutcome.DeltaRead>();
        source.Requests.Should().ContainSingle();
        source.Requests[0].Schema.Should().Be(OrdersSchema.Fbo,
            "a caller that asks for the wrong schema must not be served the FBS endpoint");
    }

    [Fact]
    public async Task FbsReader_PinsFbsSchemaOnTheIssuedRequest()
    {
        SinglePageOrdersSource source = new SinglePageOrdersSource();
        (OrdersImportContext context, RunCounters progress) = BuildRun(source, JobType.OzonOrdersImportFbs);

        FetchOutcome outcome = await CreateFbsReader(new RecordingRateLimiter()).ReadWindowAsync(
            BuildRequest(null), context, progress, CancellationToken.None);

        outcome.Should().BeOfType<FetchOutcome.DeltaRead>();
        source.Requests.Should().ContainSingle();
        source.Requests[0].Schema.Should().Be(OrdersSchema.Fbs,
            "a request that names no schema must not fall through to the FBO endpoint");
    }

    [Fact]
    public async Task FboReader_PacesOnPostingFboList()
    {
        SinglePageOrdersSource source = new SinglePageOrdersSource();
        RecordingRateLimiter limiter = new RecordingRateLimiter();
        (OrdersImportContext context, RunCounters progress) =
            BuildRun(source, JobType.OzonOrdersImportFbo, deadlineAlreadyPassed: true);

        FetchOutcome outcome = await CreateFboReader(limiter).ReadWindowAsync(
            BuildRequest(OrdersSchema.Fbo), context, progress, CancellationToken.None);

        outcome.Stopped().Cause.Should().Be(StopCause.TimeBudget);
        limiter.Scopes.Should().NotBeEmpty();
        limiter.Scopes.Should().OnlyContain(scope => scope.Method == OrdersRateLimiterMethods.OzonPostingFboList);
        limiter.Scopes.Should().OnlyContain(scope => scope.ChannelCode == "ozon");
        limiter.Scopes.Should().OnlyContain(scope => scope.ApiKey == "client-id");
        source.Requests.Should().BeEmpty("a run past its deadline issues no request");
    }

    [Fact]
    public async Task FbsReader_PacesOnPostingFbsList()
    {
        SinglePageOrdersSource source = new SinglePageOrdersSource();
        RecordingRateLimiter limiter = new RecordingRateLimiter();
        (OrdersImportContext context, RunCounters progress) =
            BuildRun(source, JobType.OzonOrdersImportFbs, deadlineAlreadyPassed: true);

        FetchOutcome outcome = await CreateFbsReader(limiter).ReadWindowAsync(
            BuildRequest(OrdersSchema.Fbs), context, progress, CancellationToken.None);

        outcome.Stopped().Cause.Should().Be(StopCause.TimeBudget);
        limiter.Scopes.Should().NotBeEmpty();
        limiter.Scopes.Should().OnlyContain(
            scope => scope.Method == OrdersRateLimiterMethods.OzonPostingFbsList,
            "FBO and FBS are paced on separate methods");
        limiter.Scopes.Should().OnlyContain(scope => scope.ChannelCode == "ozon");
        source.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task FbsReader_MatchingSchema_KeepsThePagingFieldsOfTheRequest()
    {
        SinglePageOrdersSource source = new SinglePageOrdersSource();
        (OrdersImportContext context, RunCounters progress) = BuildRun(source, JobType.OzonOrdersImportFbs);

        OrdersRequest request = BuildRequest(OrdersSchema.Fbs);
        request.Statuses = OzonPostingStatuses.FbsStatuses;
        request.Selection = OrdersSelectionMode.LastChanged;

        await CreateFbsReader(new RecordingRateLimiter()).ReadWindowAsync(
            request, context, progress, CancellationToken.None);

        source.Requests[0].DateFrom.Should().Be(WindowFrom);
        source.Requests[0].DateTo.Should().Be(request.DateTo);
        source.Requests[0].Selection.Should().Be(OrdersSelectionMode.LastChanged);
        source.Requests[0].Limit.Should().Be(100);
        source.Requests[0].Statuses.Should().BeEquivalentTo(OzonPostingStatuses.FbsStatuses);
    }

    [Fact]
    public async Task FbsReader_RequestWithoutStatuses_DoesNotInventAny()
    {
        // Statuses belong to the delta fetcher: a backfill day carries none, because posting/fbs/list
        // filters a backfill window by in_process_at alone.
        SinglePageOrdersSource source = new SinglePageOrdersSource();
        (OrdersImportContext context, RunCounters progress) = BuildRun(source, JobType.OzonOrdersImportFbs);

        await CreateFbsReader(new RecordingRateLimiter()).ReadWindowAsync(
            BuildRequest(null), context, progress, CancellationToken.None);

        source.Requests[0].Statuses.Should().BeNull();
    }
}
