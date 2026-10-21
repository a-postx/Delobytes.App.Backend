using System;
using System.Threading;
using System.Threading.Tasks;

using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// Limiter double that never refuses a slot and records nothing. Constructor sites that are not
/// about pacing use it so the adaptive limiter cannot decide whether their request goes out.
/// </summary>
internal sealed class AlwaysAllowWildberriesRateLimiter : IWildberriesRateLimiter
{
    public Task<RateLimitAcquireResult> TryAcquireAsync(
        string apiKey,
        WildberriesApiMethod method,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new RateLimitAcquireResult
        {
            Acquired = true,
            NextRequestAllowedAt = DateTimeOffset.UtcNow
        });
    }

    public Task<DateTimeOffset> ReportRateLimitedAsync(
        string apiKey,
        WildberriesApiMethod method,
        RateLimitSignal signal,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(DateTimeOffset.UtcNow);
    }

    public Task<DateTimeOffset?> GetNextRequestAllowedAtAsync(
        string apiKey,
        WildberriesApiMethod method,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? result = DateTimeOffset.UtcNow;
        return Task.FromResult(result);
    }

    public Task ReportSuccessAsync(
        string apiKey,
        WildberriesApiMethod method,
        RateLimitSignal signal,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
