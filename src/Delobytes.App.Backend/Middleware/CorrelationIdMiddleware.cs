using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Services;
using Serilog.Context;

namespace Delobytes.App.Backend.Middleware;

/// <summary>
/// Establishes the correlation identifier for every HTTP request.
/// The identifier is accepted from the <see cref="CorrelationHeaders.CorrelationId"/> request header
/// when it is well formed and generated otherwise, so downstream code and the server log always have
/// a value. The header is written to the response using OnStarting callback, which guarantees it runs
/// after exception handling has prepared the response but before headers are sent to the client.
/// </summary>
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationIdMiddleware"/> class.
    /// </summary>
    /// <param name="next">Next middleware in the pipeline.</param>
    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = CorrelationIdProvider.Resolve(
            context.Request.Headers[CorrelationHeaders.CorrelationId].ToString());

        context.Items[CorrelationIdProvider.HttpContextItemKey] = correlationId;

        // Используем OnStarting чтобы установить заголовок ровно перед отправкой response,
        // после того как ExceptionHandlingMiddleware установит статус/тело при ошибке
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(CorrelationHeaders.CorrelationId))
            {
                context.Response.Headers[CorrelationHeaders.CorrelationId] = correlationId;
            }
            b
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
