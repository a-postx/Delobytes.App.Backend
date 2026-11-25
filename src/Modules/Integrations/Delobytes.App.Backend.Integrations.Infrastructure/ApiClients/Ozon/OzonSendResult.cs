using System.Net;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// What the Ozon endpoint answered, before any caller-specific page is built. Never throws on an HTTP
/// status: the caller decides whether a 401 is a key-role problem, a 429 a deferral, or a 5xx a failed
/// run.
/// </summary>
/// <typeparam name="T">Shape the response body was read into.</typeparam>
internal sealed class OzonSendResult<T>
{
    private OzonSendResult()
    {
    }

    /// <summary>
    /// Gets a value indicating whether no request was issued because the limiter had not released the
    /// next slot.
    /// </summary>
    public bool SlotRefused { get; private init; }

    /// <summary>
    /// Gets a value indicating whether the request was issued and answered with <c>429</c>.
    /// </summary>
    public bool Throttled { get; private init; }

    public bool IsSuccess { get; private init; }

    public HttpStatusCode StatusCode { get; private init; }

    /// <summary>
    /// Gets the instant the caller may retry: the stored slot when the limiter refused, or the
    /// server-advised instant after a <c>429</c>.
    /// </summary>
    public DateTimeOffset? RetryAt { get; private init; }

    public T? Body { get; private init; }

    public static OzonSendResult<T> Refused(DateTimeOffset retryAt)
    {
        return new OzonSendResult<T>
        {
            SlotRefused = true,
            RetryAt = retryAt,
            StatusCode = HttpStatusCode.TooManyRequests,
        };
    }

    public static OzonSendResult<T> RateLimited(HttpStatusCode status, DateTimeOffset retryAt)
    {
        return new OzonSendResult<T>
        {
            Throttled = true,
            RetryAt = retryAt,
            StatusCode = status,
        };
    }

    public static OzonSendResult<T> Failed(HttpStatusCode status)
    {
        return new OzonSendResult<T> { StatusCode = status };
    }

    public static OzonSendResult<T> Ok(T? body)
    {
        return new OzonSendResult<T>
        {
            IsSuccess = true,
            StatusCode = HttpStatusCode.OK,
            Body = body,
        };
    }
}
