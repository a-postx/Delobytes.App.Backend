using Delobytes.App.Backend.Contracts.Errors;

namespace Delobytes.App.Backend.Contracts.Errors;

public class AppException : Exception
{
    public AppException(ErrorCode code, string? message = null, Exception? innerException = null)
        : base(message ?? code.DefaultMessage, innerException)
    {
        Code = code;
    }

    public ErrorCode Code { get; }
}
