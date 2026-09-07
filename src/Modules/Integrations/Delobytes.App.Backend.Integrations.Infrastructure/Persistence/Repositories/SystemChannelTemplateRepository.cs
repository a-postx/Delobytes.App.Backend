using Delobytes.App.Backend.Integrations.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;

public class SystemChannelTemplateRepository : ISystemChannelTemplateRepository
{
    private readonly IntegrationsDbContext _context;

    public SystemChannelTemplateRepository(IntegrationsDbContext context)
    {
        _context = context;
    }

    public Task<SystemChannelTemplate?> GetByCodeAsync(string code, CancellationToken ct)
    {
        return _context.SystemChannelTemplates
                .FirstOrDefaultAsync(t => t.Code == code && t.IsActive, ct);
    }

    public Task<List<SystemChannelTemplate>> GetAllActiveAsync(CancellationToken ct)
    {
        return _context.SystemChannelTemplates
                .Where(t => t.IsActive)
                .ToListAsync(ct);
    }
}
