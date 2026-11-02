using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Tests <see cref="OrdersFetcherResolver"/>: each (channel, job type) pair resolves to exactly one
/// fetcher set, Ozon FBO and FBS resolve to different sets, and every other combination is refused
/// rather than silently served the wrong schema.
/// </summary>
public class OrdersFetcherResolverTests
{
    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));

    private OzonImportOptions _ozonOptions = new OzonImportOptions { MaxInProcessWaitSeconds = 0 };

    private OrdersBatchPublisher BuildPublisher()
    {
        OrdersImportCheckpointStore store = new OrdersImportCheckpointStore(
            new Mock<IJobCheckpointRepository>().Object,
            _timeProvider);

        return new OrdersBatchPublisher(new Mock<IEventPublisher>().Object, store);
    }

    private OzonOrdersFetchers BuildOzonFboFetchers(OzonImportOptions options, PacedOrdersSource pacedSource)
    {
        OrdersBackfillWalker walker = new OrdersBackfillWalker(
            new Mock<IOrdersBackfillStateRepository>().Object,
            BuildPublisher(),
            NullLogger<OrdersBackfillWalker>.Instance,
            _timeProvider);

        OzonFboOrdersReader reader = new OzonFboOrdersReader(
            pacedSource,
            NullLogger<CursorPagedOrdersReader>.Instance,
            Options.Create(options));

        return new OzonOrdersFetchers(
            new OzonBackfillOrdersFetcher(walker, reader),
            new OzonDeltaOrdersFetcher(
                reader,
                new Mock<ISyncCheckpointRepository>().Object,
                BuildPublisher(),
                NullLogger<OzonDeltaOrdersFetcher>.Instance,
                Options.Create(options),
                OrdersSchema.Fbo,
                OzonPostingStatuses.FboStatuses),
            reader,
            Options.Create(options));
    }

    private OzonFbsOrdersFetchers BuildOzonFbsFetchers(OzonImportOptions options, PacedOrdersSource pacedSource)
    {
        OrdersBackfillWalker walker = new OrdersBackfillWalker(
            new Mock<IOrdersBackfillStateRepository>().Object,
            BuildPublisher(),
            NullLogger<OrdersBackfillWalker>.Instance,
            _timeProvider);

        OzonFbsOrdersReader reader = new OzonFbsOrdersReader(
            pacedSource,
            NullLogger<CursorPagedOrdersReader>.Instance,
            Options.Create(options));

        return new OzonFbsOrdersFetchers(
            new OzonFbsBackfillOrdersFetcher(walker, reader),
            new OzonFbsDeltaOrdersFetcher(
                reader,
                new Mock<ISyncCheckpointRepository>().Object,
                BuildPublisher(),
                NullLogger<OzonDeltaOrdersFetcher>.Instance,
                Options.Create(options)),
            reader,
            Options.Create(options));
    }

    private WildberriesOrdersFetchers BuildWildberriesFetchers(PacedOrdersSource pacedSource)
    {
        WildberriesImportOptions options = new WildberriesImportOptions();

        OrdersBackfillWalker walker = new OrdersBackfillWalker(
            new Mock<IOrdersBackfillStateRepository>().Object,
            BuildPublisher(),
            NullLogger<OrdersBackfillWalker>.Instance,
            _timeProvider);

        BisectingOrdersReader reader = new BisectingOrdersReader(
            pacedSource,
            NullLogger<BisectingOrdersReader>.Instance,
            Options.Create(options));

        return new WildberriesOrdersFetchers(
            new BackfillOrdersFetcher(walker, reader),
            new DeltaOrdersFetcher(
                reader,
                new Mock<ISyncCheckpointRepository>().Object,
                BuildPublisher(),
                NullLogger<DeltaOrdersFetcher>.Instance),
            reader,
            Options.Create(options));
    }

    private OrdersFetcherResolver BuildResolver()
    {
        PacedOrdersSource pacedSource = new PacedOrdersSource(
            new AlwaysAllowOrdersRateLimiterForResolver(),
            _timeProvider);

        return new OrdersFetcherResolver(
            BuildWildberriesFetchers(pacedSource),
            BuildOzonFboFetchers(_ozonOptions, pacedSource),
            BuildOzonFbsFetchers(_ozonOptions, pacedSource));
    }

    [Fact]
    public void TryResolve_WildberriesOrdersImport_ReturnsTheWildberriesSet()
    {
        OrdersFetcherResolver resolver = BuildResolver();

        bool resolved = resolver.TryResolve("wildberries", JobType.OrdersImport, out IOrdersFetchers? fetchers);

        resolved.Should().BeTrue();
        fetchers.Should().BeOfType<WildberriesOrdersFetchers>();
    }

    [Fact]
    public void TryResolve_OzonFbo_ReturnsTheFboSet()
    {
        OrdersFetcherResolver resolver = BuildResolver();

        bool resolved = resolver.TryResolve("ozon", JobType.OrdersImportFbo, out IOrdersFetchers? fetchers);

        resolved.Should().BeTrue();
        fetchers.Should().BeOfType<OzonOrdersFetchers>();
    }

    [Fact]
    public void TryResolve_OzonFbs_ReturnsTheFbsSet()
    {
        // FBS resolves unconditionally now: the EnableFbs flag is gone, so a scheduled FBS job must
        // always find its fetchers.
        OrdersFetcherResolver resolver = BuildResolver();

        bool resolved = resolver.TryResolve("ozon", JobType.OrdersImportFbs, out IOrdersFetchers? fetchers);

        resolved.Should().BeTrue();
        fetchers.Should().BeOfType<OzonFbsOrdersFetchers>();
    }

    [Fact]
    public void TryResolve_OzonFboAndFbs_ResolveToDifferentInstances()
    {
        OrdersFetcherResolver resolver = BuildResolver();

        resolver.TryResolve("ozon", JobType.OrdersImportFbo, out IOrdersFetchers? fbo);
        resolver.TryResolve("ozon", JobType.OrdersImportFbs, out IOrdersFetchers? fbs);

        fbo.Should().NotBeSameAs(fbs,
            "sharing one set would let the FBS job read the FBO endpoint and its statuses");
        fbo!.WindowReader.Should().BeOfType<OzonFboOrdersReader>();
        fbs!.WindowReader.Should().BeOfType<OzonFbsOrdersReader>();
    }

    [Theory]
    [InlineData("ozon", JobType.OrdersImport)]
    [InlineData("ozon", JobType.OrdersSync)]
    [InlineData("ozon", JobType.ProductsImport)]
    [InlineData("wildberries", JobType.OrdersImportFbo)]
    [InlineData("wildberries", JobType.OrdersImportFbs)]
    [InlineData("wildberries", JobType.ProductsImport)]
    [InlineData("yandex.kit", JobType.OrdersImport)]
    [InlineData("", JobType.OrdersImport)]
    public void TryResolve_UnsupportedCombination_ReturnsFalse(string channelCode, JobType jobType)
    {
        OrdersFetcherResolver resolver = BuildResolver();

        bool resolved = resolver.TryResolve(channelCode, jobType, out IOrdersFetchers? fetchers);

        resolved.Should().BeFalse();
        fetchers.Should().BeNull();
    }

    [Theory]
    [InlineData("OZON")]
    [InlineData("Ozon")]
    [InlineData("ozon")]
    public void TryResolve_ChannelCodeIsCaseInsensitive(string channelCode)
    {
        OrdersFetcherResolver resolver = BuildResolver();

        resolver.TryResolve(channelCode, JobType.OrdersImportFbs, out IOrdersFetchers? fetchers).Should().BeTrue();
        fetchers.Should().BeOfType<OzonFbsOrdersFetchers>();
    }

    /// <summary>
    /// Limiter double sufficient for constructing the readers: no request is issued in these tests,
    /// only the resolution is asserted.
    /// </summary>
    private sealed class AlwaysAllowOrdersRateLimiterForResolver : IOrdersRateLimiter
    {
        public Task<RateLimitAcquireResult> TryAcquireAsync(RateLimitScope scope, CancellationToken cancellationToken)
        {
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
            DateTimeOffset? result = null;

            return Task.FromResult(result);
        }
    }
}
