using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IChannelParameterSetRepository
{
    Task<ChannelParameterSet?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Returns all parameter sets for a channel, ordered by ValidFrom descending.
    /// </summary>
    Task<List<ChannelParameterSet>> GetByChannelIdAsync(Guid channelId, CancellationToken ct);

    /// <summary>
    /// Returns the active parameter set for a channel (ValidFrom <= today, most recent).
    /// </summary>
    Task<ChannelParameterSet?> GetActiveByChannelIdAsync(Guid channelId, CancellationToken ct);

    void Add(ChannelParameterSet parameterSet);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
