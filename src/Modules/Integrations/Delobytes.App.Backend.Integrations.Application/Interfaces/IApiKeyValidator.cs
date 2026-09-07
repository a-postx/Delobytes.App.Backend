using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

public interface IApiKeyValidator
{
    public Task<ApiKeyValidationResult> ValidateAsync(
        string apiKey,
        string? apiSecret,
        Dictionary<string, string>? settings,
        CancellationToken ct);
}
