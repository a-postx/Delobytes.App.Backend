namespace Delobytes.App.Backend.Contracts.Interfaces;

/// <summary>
/// Starts a brand new correlation chain for a unit of work that has no ambient one to inherit —
/// typically some background service.
/// </summary>
public interface ICorrelationScope
{
    /// <summary>
    /// Generates a new correlation identifier and makes it the ambient value.
    /// </summary>
    /// <returns>A handle that ends the scope when disposed.</returns>
    public IDisposable BeginNew();
}
