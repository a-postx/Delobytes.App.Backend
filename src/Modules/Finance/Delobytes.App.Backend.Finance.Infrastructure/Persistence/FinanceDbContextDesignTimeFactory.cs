using Delobytes.App.Backend.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Delobytes.App.Backend.Finance.Infrastructure.Persistence;

/// <summary>
/// Builds the Finance context for the EF Core CLI. The host cannot serve as a design-time startup
/// project because <c>AddFinanceInfrastructure</c> requires a configured connection string; this
/// factory only needs the model. Not registered in DI and free of interceptors: there is no
/// design-time user or tenant, and neither affects the model.
/// </summary>
public sealed class FinanceDbContextDesignTimeFactory : IDesignTimeDbContextFactory<FinanceDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Database=delobytes;Username=postgres;Password=postgres";

    /// <inheritdoc/>
    public FinanceDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? FallbackConnectionString;

        DbContextOptions<FinanceDbContext> options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__FinanceMigrationsHistory", "finance"))
            .Options;

        return new FinanceDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
    }
}
