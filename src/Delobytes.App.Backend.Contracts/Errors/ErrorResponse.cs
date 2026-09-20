namespace Delobytes.App.Backend.Contracts.Errors;

/// <summary>
/// Тело ответа об ошибке. TraceId намеренно отсутствует — он передаётся только в заголовке X-Correlation-Id.
/// </summary>
public sealed class ErrorResponse
{
    private ErrorResponse(ErrorCode code, string? message, IReadOnlyDictionary<string, string[]>? errors)
    {
        Code = code.Value;
        Message = message ?? code.DefaultMessage;
        Status = code.Status;
        Errors = errors;
    }

    public string Code { get; }

    public string Message { get; }

    public int Status { get; }

    // ASP.NET ValidationProblemDetails-совместимый формат: {"field": ["msg1", "msg2"]}
    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    public static ErrorResponse FromCode(ErrorCode code, string? message = null, IReadOnlyDictionary<string, string[]>? errors = null)
    {
        return new ErrorResponse(code, message, errors);
    }
}
