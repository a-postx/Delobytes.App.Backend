using Delobytes.App.Backend.Sales.Domain.Entities;

namespace Delobytes.App.Backend.Sales.Application.Interfaces.Repositories;

/// <summary>
/// Repository for the Sales-side channel product projection: finds, stages and persists
/// <see cref="ChannelProductRef"/> rows.
/// </summary>
public interface IChannelProductRefRepository
{
    /// <summary>Finds the projection of a channel product link.</summary>
    public Task<ChannelProductRef?> FindByChannelProductIdAsync(
        Guid channelProductId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds a projection that still occupies the natural key
    /// <c>(TenantId, ChannelId, ExternalProductId)</c> under a different channel product id.
    /// </summary>
    public Task<ChannelProductRef?> FindStaleAsync(
        Guid channelId,
        string externalProductId,
        Guid newChannelProductId,
        CancellationToken cancellationToken);

    /// <summary>Stages a new projection.</summary>
    public void Add(ChannelProductRef projection);

    /// <summary>Stages the removal of a projection.</summary>
    public void Remove(ChannelProductRef projection);

    /// <summary>Persists every pending change.</summary>
    public Task SaveChangesAsync(CancellationToken cancellationToken);
}
