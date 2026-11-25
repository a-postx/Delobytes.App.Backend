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

    /// <summary>
    /// Finds the order lines of the channel and external product whose catalogue link is missing or
    /// points at a different channel product. The second case is normal: a relink deletes the stale
    /// projection, so a line can point at an identifier that no longer has one.
    /// </summary>
    /// <param name="channelId">Channel the lines belong to through their order.</param>
    /// <param name="externalProductId">Channel-side product identity.</param>
    /// <param name="channelProductId">Channel product the link now resolves to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tracked lines to relink, with their orders loaded.</returns>
    public Task<IReadOnlyList<OrderLine>> LoadOrderLinesToRelinkAsync(
        Guid channelId,
        string externalProductId,
        Guid channelProductId,
        CancellationToken cancellationToken);

    /// <summary>Persists every pending change.</summary>
    public Task SaveChangesAsync(CancellationToken cancellationToken);
}

