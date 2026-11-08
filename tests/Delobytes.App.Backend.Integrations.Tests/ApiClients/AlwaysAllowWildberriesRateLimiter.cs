using System;
using System.Threading;
using System.Threading.Tasks;

using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// Limiter double that never refuses a slot and records nothing. Constructor sites that are not
/// about pacing use it so the adaptive limiter cannot decide whether their request goes out.
/// </summary>
internal sealed class AlwaysAllowWildberriesRateLimiter : IOrdersRateLimiter
{
    public Task<RateLimitAcquireResult> TryAcquireAsync(
        RateLimitScope scope,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new RateLimitAcquireResult
        {
            Acquired = true,
            NextRequestAllowedAt = DateTimeOffset.UtcNow
        });
    }

    public Task<DateTimeOffset> ReportRateLimitedAsync(
        RateLimitScope scope,
        RateLimitSignal signal,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(DateTimeOffset.UtcNow);
    }

    public Task<DateTimeOffset?> GetNextRequestAllowedAtAsync(
        RateLimitScope scope,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? result = DateTimeOffset.UtcNow;
        return Task.FromResult(result);
    }

    public Task ReportSuccessAsync(
        RateLimitScope scope,
        RateLimitSignal signal,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
