using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Services;
using Serilog.Context;

namespace Delobytes.App.Backend.Middleware;

/// <summary>
/// Establishes the correlation identifier for every HTTP request.
/// The identifier is accepted from the <see cref="CorrelationHeaders.CorrelationId"/> request header
/// when it is well formed and generated otherwise, so downstream code and the server log always have
/// a value. The header is written to the response before the rest of the pipeline runs, which means
/// responses produced outside the application's own handlers — routing 404, authentication 401,
/// unhandled exceptions — still carry it.
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
        context.TraceIdentifier = correlationId;

        // Set directly rather than through Response.OnStarting: OnStarting does not run for a
        // response that completes without flushing, and a header set here survives the rest of the
        // pipeline, including exception handling, which does not clear the response.
        if (!context.Response.HasStarted)
        {
            context.Response.Headers[CorrelationHeaders.CorrelationId] = correlationId;
        }

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
