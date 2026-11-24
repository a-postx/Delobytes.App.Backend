using Delobytes.App.Backend.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Delobytes.App.Backend.Persistence;

/// <summary>
/// Registers a module DbContext with the persistence services every module shares:
/// the audit interceptor and the <see cref="TimeProvider"/> it depends on.
/// Registration lives here once so a module cannot silently omit the audit interceptor.
/// </summary>
public static class ModuleDbContextExtensions
{
    /// <summary>
    /// Adds a module DbContext configured by <paramref name="configure"/>, with the shared
    /// audit interceptor attached.
    /// </summary>
    /// <typeparam name="TContext">Module DbContext type.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Provider and module-specific options; called for every context instance.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> configure)
        where TContext : DbContext
    {
        services.TryAddSingleton(TimeProvider.System);

        // Scoped: the interceptor depends on the scoped IUserContext, and EF Core resolves
        // interceptors from the provider that owns the DbContext — the same scope.
        services.TryAddScoped<AuditableEntityInterceptor>();

        services.AddDbContext<TContext>((serviceProvider, options) =>
        {
            configure(serviceProvider, options);
            options.AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>());
        });

        return services;
    }
}
