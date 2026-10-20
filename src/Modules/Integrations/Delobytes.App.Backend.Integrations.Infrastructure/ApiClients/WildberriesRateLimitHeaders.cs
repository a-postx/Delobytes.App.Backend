using System.Globalization;
using System.Net.Http.Headers;

using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// Reads the <c>x-ratelimit-*</c> and <c>x-request-id</c> headers of a Wildberries response into
/// a <see cref="RateLimitSignal"/>.
/// </summary>
/// <remarks>
/// Parsing is deliberately total: a header that is absent, empty, non-numeric or negative yields
/// <c>null</c> and never throws. The header set differs per status code by design — <c>reset</c>
/// and <c>retry</c> exist only on a <c>429</c>, <c>limit</c> and <c>remaining</c> on both — so a
/// missing value is the normal case on a success, not a malformed response.
/// HTTP/2 delivers header names lowercased, and <see cref="HttpHeaders"/> matches names
/// case-insensitively, so no name normalization is performed here.
/// </remarks>
internal static class WildberriesRateLimitHeaders
{
    private const string LimitHeaderName = "x-ratelimit-limit";
    private const string RemainingHeaderName = "x-ratelimit-remaining";
    private const string ResetHeaderName = "x-ratelimit-reset";
    private const string RetryHeaderName = "x-ratelimit-retry";
    private const string RequestIdHeaderName = "x-request-id";

    /// <summary>
    /// Parses the rate-limit headers of <paramref name="response"/>.
    /// </summary>
    /// <param name="response">Response whose headers should be read.</param>
    /// <returns>
    /// Parsed signal, with <see cref="RateLimitSignal.StatusCode"/> taken from the response.
    /// </returns>
    public static RateLimitSignal Parse(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new RateLimitSignal
        {
            Limit = ReadInt32(response, LimitHeaderName),
            Remaining = ReadInt32(response, RemainingHeaderName),
            ResetSeconds = ReadInt32(response, ResetHeaderName),
            RetrySeconds = ReadInt32(response, RetryHeaderName),
            RequestId = ReadString(response, RequestIdHeaderName),
            StatusCode = (int)response.StatusCode
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

        // A negative allowance or wait is meaningless and would move the next allowed instant
        // backwards, so it is discarded rather than trusted.
        return value < 0 ? null : value;
    }

    private static string? ReadString(HttpResponseMessage response, string headerName)
    {
        // A response header is the documented location; the content collection is consulted as a
        // fallback because a proxy in front of the API may reclassify the header, and losing the
        // 429 advice there would silently downgrade pacing to the fallback interval.
        string? value = null;

        if (!TryGetFirstValue(response.Headers, headerName, out value) && response.Content != null)
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
