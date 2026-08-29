namespace Delobytes.App.Backend.Middleware;

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

/// <summary>
/// Global exception handling middleware.
/// Maps known exception types to appropriate HTTP status codes and returns
/// a consistent JSON error envelope: { "message": "..." }.
/// Unknown exceptions are logged and returned as 500 without internal details.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ExceptionHandlingMiddleware"/> class.
    /// </summary>
    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        HttpStatusCode statusCode;
        string message;

        switch (exception)
        {
            case UnauthorizedAccessException:
                statusCode = HttpStatusCode.Unauthorized;
                message = exception.Message;
                _logger.LogWarning("Unauthorized: {Message}", exception.Message);
                break;

            case InvalidOperationException:
                statusCode = HttpStatusCode.BadRequest;
                message = exception.Message;
                _logger.LogWarning("Bad request: {Message}", exception.Message);
                break;

            case KeyNotFoundException:
                statusCode = HttpStatusCode.NotFound;
                message = exception.Message;
                _logger.LogWarning("Not found: {Message}", exception.Message);
                break;

            default:
                statusCode = HttpStatusCode.InternalServerError;
                message = "An unexpected error occurred.";
                _logger.LogError(exception, "Unhandled exception");
                break;
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        string body = JsonSerializer.Serialize(new { message }, JsonOptions);
        await context.Response.WriteAsync(body);
    }
}
