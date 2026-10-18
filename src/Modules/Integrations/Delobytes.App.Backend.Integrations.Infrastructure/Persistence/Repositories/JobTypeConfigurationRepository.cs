using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IJobTypeConfigurationRepository.
/// </summary>
public class JobTypeConfigurationRepository : IJobTypeConfigurationRepository
{
    private readonly IntegrationsDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobTypeConfigurationRepository"/> class.
    /// </summary>
    public JobTypeConfigurationRepository(IntegrationsDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public Task<JobTypeConfiguration?> GetByTypeAsync(JobType jobType, CancellationToken cancellationToken)
    {
        return _context.JobTypeConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.JobType == jobType, cancellationToken);
    }
}
