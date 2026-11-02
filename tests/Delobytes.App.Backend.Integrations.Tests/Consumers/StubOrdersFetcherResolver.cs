using System;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Resolver double for consumer tests that were written before channels were resolved by
/// (channel code, job type): answers exactly one combination - built by <see cref="ForWildberries"/>
/// or <see cref="ForOzon"/> from the test's own fetchers - and nothing else, so a test that
/// previously could only run one channel keeps running that channel in isolation.
/// </summary>
internal sealed class StubOrdersFetcherResolver : IOrdersFetcherResolver
{
    private readonly IOrdersFetchers? _fetchers;
    private readonly JobType _jobType;
    private readonly string _channelCode;

    private StubOrdersFetcherResolver(string channelCode, JobType jobType, IOrdersFetchers? fetchers)
    {
        _channelCode = channelCode;
        _jobType = jobType;
        _fetchers = fetchers;
    }

    /// <summary>
    /// Builds the Wildberries fetchers from already-constructed parts, so the resolution seam adds
    /// no new dependency graph to a test that only cares about the fetch itself.
    /// </summary>
    public static StubOrdersFetcherResolver ForWildberries(
        BackfillOrdersFetcher backfill,
        DeltaOrdersFetcher delta,
        BisectingOrdersReader windowReader,
        WildberriesImportOptions options)
    {
        return new StubOrdersFetcherResolver(
            "wildberries",
            JobType.OrdersImport,
            new WildberriesOrdersFetchers(backfill, delta, windowReader, Options.Create(options)));
    }

    /// <summary>
    /// Builds the Ozon FBO fetchers from already-constructed parts, for tests that exercise the
    /// Ozon branch of the orchestrator in isolation from Wildberries.
    /// </summary>
    public static StubOrdersFetcherResolver ForOzon(
        OzonBackfillOrdersFetcher backfill,
        OzonDeltaOrdersFetcher delta,
        IOrdersWindowReader windowReader,
        OzonImportOptions options)
    {
        return new StubOrdersFetcherResolver(
            "ozon",
            JobType.OrdersImportFbo,
            new OzonOrdersFetchers(backfill, delta, windowReader, Options.Create(options)));
    }

    /// <summary>
    /// Builds the Ozon FBS fetchers from already-constructed parts, for tests that exercise the
    /// seller-fulfilled schema through the same orchestrator.
    /// </summary>
    public static StubOrdersFetcherResolver ForOzonFbs(
        OzonFbsBackfillOrdersFetcher backfill,
        OzonFbsDeltaOrdersFetcher delta,
        OzonFbsOrdersReader windowReader,
        OzonImportOptions options)
    {
        return new StubOrdersFetcherResolver(
            "ozon",
            JobType.OrdersImportFbs,
            new OzonFbsOrdersFetchers(backfill, delta, windowReader, Options.Create(options)));
    }

    /// <summary>
    /// A resolver that supports nothing, for asserting the unsupported-combination failure.
    /// </summary>
    public static StubOrdersFetcherResolver Unsupported()
    {
        return new StubOrdersFetcherResolver(string.Empty, JobType.OrdersSync, null);
    }

    /// <inheritdoc/>
    public bool TryResolve(string channelCode, JobType jobType, out IOrdersFetchers? fetchers)
    {
        if (_fetchers != null
            && string.Equals(channelCode, _channelCode, StringComparison.OrdinalIgnoreCase)
            && jobType == _jobType)
        {
            fetchers = _fetchers;
            return true;
        }

        fetchers = null;
        return false;
    }
}
