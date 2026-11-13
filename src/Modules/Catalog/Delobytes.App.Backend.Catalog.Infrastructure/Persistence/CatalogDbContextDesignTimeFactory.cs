using Delobytes.App.Backend.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence;

/// <summary>
/// Builds the Catalog context for the EF Core CLI. The host cannot serve as a design-time
/// startup project because <c>AddCatalogInfrastructure</c> requires a configured connection
/// string; this factory only needs the model. Not registered in DI and free of interceptors:
/// there is no design-time user or tenant, and neither affects the model.
/// </summary>
public sealed class CatalogDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Database=delobytes;Username=postgres;Password=postgres";

    /// <inheritdoc/>
    public CatalogDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? FallbackConnectionString;

        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__CatalogMigrationsHistory", "catalog"))
            .Options;

        return new CatalogDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
    }
}
