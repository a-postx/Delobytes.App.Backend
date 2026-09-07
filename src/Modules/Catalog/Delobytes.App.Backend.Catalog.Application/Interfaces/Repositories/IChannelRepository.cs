using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IChannelRepository
{
    public Task<Channel?> GetByIdAsync(Guid id, CancellationToken ct);

    public void Add(Channel channel);

    public Task<int> SaveChangesAsync(CancellationToken ct);
}
