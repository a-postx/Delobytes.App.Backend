using System.Text.Json;
using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Services;

namespace Delobytes.App.Backend.Middleware;

/// <summary>
/// Global exception handling middleware.
/// Maps known exception types to ErrorCode and returns a consistent JSON error envelope.
/// Unknown exceptions are logged and returned as 500 without internal details.
/// CorrelationId is set only in the response header (X-Correlation-Id), not in the body.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

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
        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                "Response has already started, cannot write error response for: {Message}",
                exception.Message);
            return;
        }

        ErrorCode errorCode;
        string? message = null;

        switch (exception)
        {
            case AppException appEx:
                errorCode = appEx.Code;
                message = appEx.Message;
                _logger.LogWarning("AppException [{Code}]: {Message}", appEx.Code.Value, appEx.Message);
                break;

            case UnauthorizedAccessException:
                errorCode = ErrorCodes.Common.Unauthorized;
                _logger.LogWarning("Unauthorized: {Message}", exception.Message);
                break;

            case Integrations.Application.ConflictException:
                errorCode = ErrorCodes.Common.Conflict;
                _logger.LogWarning("Conflict: {Message}", exception.Message);
                break;

            case InvalidOperationException:
                errorCode = ErrorCodes.Common.ValidationFailed;
                _logger.LogWarning("Bad request: {Message}", exception.Message);
                break;

            case KeyNotFoundException:
                errorCode = ErrorCodes.Common.NotFound;
                _logger.LogWarning("Not found: {Message}", exception.Message);
                break;

            default:
                errorCode = ErrorCodes.Common.Unexpected;
                _logger.LogError(exception, "Unhandled exception");
                break;
        }

        try
        {
            // Устанавливаем correlation ID ДО записи тела ответа
            if (!context.Response.Headers.ContainsKey(CorrelationHeaders.CorrelationId))
            {
                string correlationId = CorrelationIdProvider.Resolve(
                    context.Request.Headers[CorrelationHeaders.CorrelationId].ToString());
                context.Response.Headers[CorrelationHeaders.CorrelationId] = correlationId;
            }

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = errorCode.Status;

            ErrorResponse body = ErrorResponse.FromCode(errorCode, message);
            string json = JsonSerializer.Serialize(body, JsonOptions);
            await context.Response.WriteAsync(json);
        }
        catch (Exception writeEx)
        {
            _logger.LogError(
                writeEx,
                "Failed to write error response. Original error: {OriginalMessage}",
                exception.Message);
        }
    }
}
