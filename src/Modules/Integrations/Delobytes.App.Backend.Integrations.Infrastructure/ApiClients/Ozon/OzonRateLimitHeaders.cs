using System.Globalization;
using System.Net.Http.Headers;
using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// Reads the <c>Ratelimit-Remaining</c>, <c>Retry-After</c> and <c>x-request-id</c> headers of an
/// Ozon response into a <see cref="RateLimitSignal"/>.
/// </summary>
/// <remarks>
/// Parsing is total, like the Wildberries counterpart: an absent, empty, non-numeric or negative
/// value yields <c>null</c> and never throws. Ozon sends <c>Ratelimit-Remaining</c> on both success
/// and <c>429</c>, and <c>Retry-After</c> on <c>429</c> only, so a missing wait is the normal case
/// on a success. Only the delay-seconds form of <c>Retry-After</c> is honoured; the HTTP-date form
/// and any other value are reported as absent, which makes the limiter fall back to its own
/// interval instead of trusting a duration it could not compute.
/// </remarks>
internal static class OzonRateLimitHeaders
{
    private const string RemainingHeaderName = "Ratelimit-Remaining";
    private const string RetryAfterHeaderName = "Retry-After";
    private const string RequestIdHeaderName = "x-request-id";

    /// <summary>
    /// Parses the rate-limit headers of <paramref name="response"/>.
    /// </summary>
    /// <param name="response">Response whose headers should be read.</param>
    /// <returns>
    /// Parsed signal, with <see cref="RateLimitSignal.StatusCode"/> taken from the response.
    /// <see cref="RateLimitSignal.Limit"/>, <see cref="RateLimitSignal.ResetSeconds"/> are left
    /// null: Ozon reports neither.
    /// </returns>
    public static RateLimitSignal Parse(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new RateLimitSignal
        {
            Remaining = ReadInt32(response, RemainingHeaderName),
            RetrySeconds = ReadInt32(response, RetryAfterHeaderName),
            RequestId = ReadString(response, RequestIdHeaderName),
            StatusCode = (int)response.StatusCode,
        };
    }

    private static int? ReadInt32(HttpResponseMessage response, string headerName)
    {
        string? rawValue = ReadString(response, headerName);

        if (rawValue == null)
        {
            return null;
        }

        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            return null;
        }

        return value < 0 ? null : value;
    }

    private static string? ReadString(HttpResponseMessage response, string headerName)
    {
        if (!TryGetFirstValue(response.Headers, headerName, out string? value) && response.Content != null)
        {
            TryGetFirstValue(response.Content.Headers, headerName, out value);
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool TryGetFirstValue(HttpHeaders headers, string headerName, out string? value)
    {
        value = null;

        if (!headers.TryGetValues(headerName, out IEnumerable<string>? values))
        {
            return false;
        }

        foreach (string candidate in values)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                value = candidate.Trim();

                return true;
            }
        }

        return false;
    }
}
