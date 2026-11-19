using Delobytes.App.Backend.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence;

/// <summary>
/// Builds the Integrations context for the EF Core CLI. The host cannot serve as a design-time
/// startup project because <c>Program</c> loads Yandex Lockbox configuration before the host is
/// built; this factory only needs the model. Not registered in DI and free of interceptors: there
/// is no design-time user or tenant, and neither affects the model.
/// </summary>
public sealed class IntegrationsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<IntegrationsDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Database=delobytes;Username=postgres;Password=postgres";

    /// <inheritdoc/>
    public IntegrationsDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? FallbackConnectionString;

        DbContextOptions<IntegrationsDbContext> options = new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__IntegrationsMigrationsHistory", "integrations"))
            .Options;

        return new IntegrationsDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
    }
}
