using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces.Repositories;

public interface ISystemChannelTemplateRepository
{
    public Task<SystemChannelTemplate?> GetByCodeAsync(string code, CancellationToken ct);
    public Task<List<SystemChannelTemplate>> GetAllActiveAsync(CancellationToken ct);
}
