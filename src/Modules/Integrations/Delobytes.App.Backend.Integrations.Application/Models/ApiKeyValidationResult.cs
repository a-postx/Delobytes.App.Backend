namespace Delobytes.App.Backend.Integrations.Application.Models;

public class ApiKeyValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }

    public static ApiKeyValidationResult Success()
    {
        return new () { IsValid = true };
    }

    public static ApiKeyValidationResult Failure(string message)
    {
        return new () { IsValid = false, ErrorMessage = message };
    }
}
