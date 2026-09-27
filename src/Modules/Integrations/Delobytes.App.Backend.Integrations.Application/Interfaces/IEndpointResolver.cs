using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Resolves API endpoint URLs for channel templates.
/// </summary>
public interface IEndpointResolver
{
    /// <summary>
    /// Gets the endpoint URL for the specified template and operation type.
    /// </summary>
    /// <param name="templateId">System channel template identifier.</param>
    /// <param name="endpointType">Type of endpoint operation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Base URL for the specified endpoint type.</returns>
    /// <exception cref="InvalidOperationException">When endpoint is not found or inactive.</exception>
    Task<string> GetEndpointUrlAsync(Guid templateId, ChannelEndpointType endpointType, CancellationToken ct);
}
