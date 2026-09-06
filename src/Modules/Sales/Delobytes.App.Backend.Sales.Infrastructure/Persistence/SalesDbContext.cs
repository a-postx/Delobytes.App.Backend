namespace Delobytes.App.Backend.Sales.Infrastructure.Persistence;

using Delobytes.App.Backend.Sales.Domain.Entities;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF Core DbContext for the Sales module.
/// </summary>
public class SalesDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SalesDbContext"/> class.
    /// </summary>
    /// <param name="options">DbContext options.</param>
    public SalesDbContext(DbContextOptions<SalesDbContext> options)
        : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Return> Returns => Set<Return>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SalesDbContext).Assembly);

        modelBuilder.HasDefaultSchema("sales");
    }
}
