namespace Delobytes.App.Backend.Integrations.Application.Models;

/// <summary>
/// Parsed <c>x-ratelimit-*</c> headers of one Wildberries response. All fields are nullable on
/// purpose: <see cref="ResetSeconds"/> and <see cref="RetrySeconds"/> appear only on a <c>429</c>,
/// <see cref="Limit"/> and <see cref="Remaining"/> appear on both, and any header may be absent
/// or non-numeric.
/// </summary>
public sealed record RateLimitSignal
{
    /// <summary>
    /// Gets the <c>x-ratelimit-limit</c> value. Present on both <c>200</c> and <c>429</c>.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Gets the <c>x-ratelimit-remaining</c> value. Present on both <c>200</c> and <c>429</c>.
    /// </summary>
    public int? Remaining { get; init; }

    /// <summary>
    /// Gets the <c>x-ratelimit-reset</c> value, in seconds. <c>429</c> only.
    /// </summary>
    public int? ResetSeconds { get; init; }

    /// <summary>
    /// Gets the <c>x-ratelimit-retry</c> value, in seconds. <c>429</c> only.
    /// </summary>
    public int? RetrySeconds { get; init; }

    /// <summary>
    /// Gets the <c>x-request-id</c> value. Present on both; the only handle for vendor-side
    /// support and therefore always logged.
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// Gets the HTTP status code of the response the headers were parsed from.
    /// </summary>
    public int StatusCode { get; init; }
}
