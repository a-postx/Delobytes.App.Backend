using System.Reflection;
using Delobytes.App.Backend.Finance.Application.Expectation;
using Delobytes.App.Backend.Finance.Application.Ingestion;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Journal;
using Delobytes.App.Backend.Finance.Application.Projection;
using Delobytes.App.Backend.Finance.Infrastructure.Messaging;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence.Stores;
using Delobytes.App.Backend.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Delobytes.App.Backend.Finance.Infrastructure;

/// <summary>
/// Registers Finance module infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Finance infrastructure services to the DI container.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionString">Connection string.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddFinanceInfrastructure(this IServiceCollection services, string? connectionString)
    {
        if (connectionString == null)
        {
            throw new InvalidOperationException("Connection string is not configured.");
        }

        services.AddModuleDbContext<FinanceDbContext>((_, options) =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsHistoryTable("__FinanceMigrationsHistory", "finance")));

        services.AddScoped<IPnlJournalStore, PnlJournalStore>();
        services.AddScoped<IOrderLineRefStore, OrderLineRefStore>();
        services.AddScoped<IOrderLineCostStore, OrderLineCostStore>();
        services.AddScoped<IPnlExpectationStore, PnlExpectationStore>();
        services.AddScoped<IPnlLineStore, PnlLineStore>();
        services.AddScoped<IPnlReadStore, PnlReadStore>();
        services.AddScoped<IPnlFeedStateStore, PnlFeedStateStore>();
        services.AddScoped<JournalAppender>();
        services.AddScoped<PnlLineProjector>();
        services.AddScoped<ExpectationStateUpdater>();
        services.AddScoped<FinanceEntriesBatchImporter>();
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        // Registered independently of Integrations' own TryAddSingleton call: Finance must not
        // depend on that module being loaded first.
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        return services;
    }

    /// <summary>
    /// Registers Finance module MassTransit consumers.
    /// </summary>
    /// <param name="configurator">MassTransit bus registration configurator.</param>
    public static void AddFinanceConsumers(this IBusRegistrationConfigurator configurator)
    {
        configurator.AddConsumers(Assembly.GetExecutingAssembly());
    }
}

