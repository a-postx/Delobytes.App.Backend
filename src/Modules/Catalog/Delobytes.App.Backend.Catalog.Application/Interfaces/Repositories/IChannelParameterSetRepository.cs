using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IChannelParameterSetRepository
{
    public Task<ChannelParameterSet?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Returns all parameter sets for a channel, ordered by ValidFrom descending.
    /// </summary>
    public Task<List<ChannelParameterSet>> GetByChannelIdAsync(Guid channelId, CancellationToken ct);

    /// <summary>
    /// Returns the active parameter set for a channel (ValidFrom less or equal today, most recent).
    /// </summary>
    public Task<ChannelParameterSet?> GetActiveByChannelIdAsync(Guid channelId, CancellationToken ct);

    public void Add(ChannelParameterSet parameterSet);

    public Task<int> SaveChangesAsync(CancellationToken ct);
}
