using System.Reflection;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Interceptors;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;
using Delobytes.App.Backend.Contracts.Interfaces;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Catalog.Infrastructure;

/// <summary>
/// Registers Catalog module infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Catalog infrastructure services to the DI container.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionString">Connection string.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddCatalogInfrastructure(this IServiceCollection services, string? connectionString)
    {
        if (connectionString == null)
        {
            throw new InvalidOperationException("Connection string is not configured.");
        }

        // AuditableEntityInterceptor depends on scoped IUserContext, so it must be scoped too.
        // EF Core resolves interceptors from the service provider that owns the DbContext,
        // which is the same scope — so a scoped interceptor is safe here.
        services.AddScoped<AuditableEntityInterceptor>();

        services.AddDbContext<CatalogDbContext>((serviceProvider, options) =>
        {
            AuditableEntityInterceptor auditInterceptor = serviceProvider.GetRequiredService<AuditableEntityInterceptor>();

            options.UseNpgsql(connectionString, npgsqlOptions =>
                    npgsqlOptions.MigrationsHistoryTable("__CatalogMigrationsHistory", "catalog"))
                .AddInterceptors(auditInterceptor);
        });

        services.AddScoped<IChannelRepository, ChannelRepository>();
        services.AddScoped<IChannelParameterSetRepository, ChannelParameterSetRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<IComponentRepository, ComponentRepository>();
        services.AddScoped<IComponentPriceRepository, ComponentPriceRepository>();
        services.AddScoped<ICostTypeRepository, CostTypeRepository>();
        services.AddScoped<IProductChannelCostRepository, ProductChannelCostRepository>();
        services.AddScoped<IWorkRateRepository, WorkRateRepository>();
        services.AddScoped<IProductWorkRateRepository, ProductWorkRateRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();

        return services;
    }

    /// <summary>
    /// Registers Catalog module MassTransit consumers.
    /// </summary>
    /// <param name="configurator">MassTransit bus registration configurator.</param>
    public static void AddCatalogConsumers(this IBusRegistrationConfigurator configurator)
    {
        configurator.AddConsumers(Assembly.GetExecutingAssembly());
    }
}
