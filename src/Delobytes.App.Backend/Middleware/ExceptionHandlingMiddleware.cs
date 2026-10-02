using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Services;
using FluentValidation;

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
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
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
        ErrorCode errorCode;
        string? message = null;
        IReadOnlyDictionary<string, string[]>? validationErrors = null;

        switch (exception)
        {
            case AppException appEx:
                errorCode = appEx.Code;
                message = appEx.Message;
                _logger.LogWarning("AppException [{Code}]: {Message}. {InnerException}", appEx.Code.Value, appEx.Message, appEx.InnerException);
                break;

            case ValidationException validationEx:
                // Raised by ValidationBehaviour when FluentValidation rejects a request. Without
                // this case the exception fell through to the default branch and every rejected
                // request answered 500 common.unexpected_error, naming no field -- so a client
                // could not tell an empty SKU from a genuine server fault.
                errorCode = ErrorCodes.Common.ValidationFailed;
                validationErrors = validationEx.Errors
                    .GroupBy(failure => ToCamelCaseField(failure.PropertyName))
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(failure => failure.ErrorMessage).ToArray());
                _logger.LogWarning(
                    "Validation failed: {ValidationErrors}",
                    string.Join("; ", validationEx.Errors.Select(f => $"{f.PropertyName}: {f.ErrorMessage}")));
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

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = errorCode.Status;

        // CorrelationIdMiddleware sets this header before the pipeline runs, so normally it is
        // already present. Defensive fallback for tests or non-standard host composition.
        if (!context.Response.Headers.ContainsKey(CorrelationHeaders.CorrelationId))
        {
            context.Response.Headers[CorrelationHeaders.CorrelationId] = CorrelationIdProvider.Resolve(
                context.Request.Headers[CorrelationHeaders.CorrelationId].ToString());
        }

        ErrorResponse body = ErrorResponse.FromCode(errorCode, message, validationErrors);
        string json = JsonSerializer.Serialize(body, JsonOptions);
        await context.Response.WriteAsync(json);
    }

    /// <summary>
    /// Converts a FluentValidation property name ("Sku", "Barcodes[0].Value") to the camelCase
    /// form the client sends, so the frontend can map errors back onto form fields.
    /// </summary>
    private static string ToCamelCaseField(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName) || char.IsLower(propertyName[0]))
        {
            return propertyName;
        }

        return char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
    }
}
