using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence;

/// <summary>
/// Gives every active connection that predates <see cref="ConnectionScheduleProvisioner"/> the
/// orders and finance schedules its channel serves. Runs once as part of the explicit migrate
/// step, not on web host startup: a connection created by the running application is provisioned
/// by its ConnectionCreatedConsumer, so this only repairs the backlog and is a no-op afterwards.
/// </summary>
public sealed class ConnectionSchedulesBackfill : IDataBackfill
{
    private readonly IntegrationsDbContext _context;
    private readonly ITenantContextSetter _tenantContext;
    private readonly ConnectionScheduleProvisioner _provisioner;
    private readonly ILogger<ConnectionSchedulesBackfill> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionSchedulesBackfill"/> class.
    /// </summary>
    /// <param name="context">Integrations DbContext.</param>
    /// <param name="tenantContext">Tenant context, set per connection before each write.</param>
    /// <param name="provisioner">Creator of the missing schedules.</param>
    /// <param name="logger">Logger instance.</param>
    public ConnectionSchedulesBackfill(
        IntegrationsDbContext context,
        ITenantContextSetter tenantContext,
        ConnectionScheduleProvisioner provisioner,
        ILogger<ConnectionSchedulesBackfill> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _provisioner = provisioner;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "Integrations: job schedules of connections created before ConnectionCreatedConsumer";

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Cross-tenant sweep: there is no ambient tenant during the migrate step.
        List<Connection> activeConnections = await _context.Connections
            .IgnoreQueryFilters()
            .Include(c => c.SystemChannelTemplate)
            .Where(c => c.IsActive)
            .ToListAsync(cancellationToken);

        if (activeConnections.Count == 0)
        {
            _logger.LogInformation("Connection schedules backfill: no active connections, nothing to do.");
            return;
        }

        int createdCount = 0;

        foreach (Connection connection in activeConnections)
        {
            // TenantId is a shadow property on Connection; both the query filter and the
            // SetTenantId stamp on save read it from the ambient tenant context instead.
            Guid tenantId = (Guid)_context.Entry(connection).Property("TenantId").CurrentValue!;

            _tenantContext.SetTenantId(tenantId);

            try
            {
                createdCount += await _provisioner.ProvisionForConnectionAsync(connection, cancellationToken);
            }
            finally
            {
                _tenantContext.Clear();
            }
        }

        _logger.LogInformation(
            "Connection schedules backfill: created {Count} JobSchedule(s) across {Connections} active connection(s).",
            createdCount,
            activeConnections.Count);
    }
}
