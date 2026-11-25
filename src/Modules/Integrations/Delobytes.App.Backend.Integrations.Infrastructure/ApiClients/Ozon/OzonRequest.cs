namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// One Ozon call: a POST of a ready body to a path, metered under one limiter method.
/// </summary>
/// <param name="Path">Path below the bound base URL, starting with '/'.</param>
/// <param name="RateLimiterMethod">Key the limiter meters. Ozon counts the API key, not the URL.</param>
/// <param name="Body">Serialized JSON body.</param>
/// <param name="SlotWaitTolerance">
/// Null means "do not wait": a slot that is not free now becomes a refusal. A value absorbs a short
/// pacing wait in-process and defers only a refusal beyond it.
/// </param>
internal sealed record OzonRequest(
    string Path,
    string RateLimiterMethod,
    string Body,
    TimeSpan? SlotWaitTolerance);
