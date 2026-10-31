using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// Orders limiter double that never refuses a slot and records nothing. Tests whose subject is
/// request shape or response mapping use it so pacing cannot decide whether the request goes out.
/// </summary>
internal sealed class AlwaysAllowOrdersRateLimiter : IOrdersRateLimiter
{
    public Task<RateLimitAcquireResult> TryAcquireAsync(
        RateLimitScope scope, CancellationToken cancellationToken)
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
