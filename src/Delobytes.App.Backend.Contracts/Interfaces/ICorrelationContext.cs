namespace Delobytes.App.Backend.Contracts.Interfaces;

/// <summary>
/// Provides access to the correlation identifier of the current logical request.
/// The value is established by the host (HTTP middleware or message consume filter) and is
/// guaranteed to be non-null for any code running inside a request or a consumer scope.
/// </summary>
public interface ICorrelationContext
{
    /// <summary>
    /// Gets the correlation identifier of the current request.
    /// </summary>
    public string? CorrelationId { get; }
}
