using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Tests.Services;

/// <summary>
/// In-memory double for <see cref="IWildberriesMethodThrottleRepository"/>. A plain lock stands
/// in for the production advisory-lock/transaction pair: these tests exercise
/// <see cref="Delobytes.App.Backend.Integrations.Infrastructure.Services.WildberriesRateLimiter"/>'s
/// pacing algorithm, not the repository's own concurrency guarantees, and the real
/// implementation's <c>pg_advisory_xact_lock</c> call cannot run against the EF Core InMemory
/// provider the rest of this test suite uses.
/// </summary>
internal sealed class FakeWildberriesMethodThrottleRepository : IWildberriesMethodThrottleRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<(string KeyFingerprint, WildberriesApiMethod Method), WildberriesMethodThrottle> _rows = new();

    public Task<WildberriesMethodThrottle?> GetAsync(
        string keyFingerprint, WildberriesApiMethod method, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _rows.TryGetValue((keyFingerprint, method), out WildberriesMethodThrottle? row);

            return Task.FromResult(row);
        }
    }

    public Task UpsertAsync(WildberriesMethodThrottle throttle, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _rows[(throttle.KeyFingerprint, throttle.Method)] = throttle;
        }

        return Task.CompletedTask;
    }

    public Task<TResult> MutateAsync<TResult>(
        string keyFingerprint,
        WildberriesApiMethod method,
        Func<WildberriesMethodThrottle> createDefault,
        Func<WildberriesMethodThrottle, TResult> mutate,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            (string, WildberriesApiMethod) key = (keyFingerprint, method);

            if (!_rows.TryGetValue(key, out WildberriesMethodThrottle? throttle))
            {
                throttle = createDefault();
                throttle.KeyFingerprint = keyFingerprint;
                throttle.Method = method;
                _rows[key] = throttle;
            }

            TResult result = mutate(throttle);

            return Task.FromResult(result);
        }
    }
}
