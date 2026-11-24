using System;
using System.Threading;
using System.Threading.Tasks;

using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// Limiter double that always grants a slot and records the feedback it was given, so a test can
/// assert which responses reached the pacing state.
/// </summary>
internal sealed class RecordingRateLimiter : IChannelRateLimiter
{
    public int SuccessReports { get; private set; }

    public int ThrottleReports { get; private set; }

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
        ThrottleReports++;

        return Task.FromResult(DateTimeOffset.UtcNow);
    }

    public Task ReportSuccessAsync(
        RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken)
    {
        SuccessReports++;

        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> GetNextRequestAllowedAtAsync(
        RateLimitScope scope, CancellationToken cancellationToken)
    {
        return Task.FromResult<DateTimeOffset?>(null);
    }
}
