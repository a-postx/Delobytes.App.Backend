using System.Reflection;
using Delobytes.App.Backend.Application.Behaviours;
using Delobytes.App.Backend.Contracts.Modules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyModel;

namespace Delobytes.App.Backend.Composition;

internal static class ModuleComposition
{
    // Module assemblies are discovered from the application dependency graph, the same way ASP.NET
    // Core finds controllers. Adding a module therefore means adding a project reference, not editing
    // the host composition.
    private const string ModuleAssemblyPrefix = "Delobytes.App.Backend.";
    private const string ModuleAssemblySuffix = ".Api";

    public static WebApplicationBuilder AddModules(this WebApplicationBuilder builder)
    {
        IReadOnlyList<IModule> modules = DiscoverModules();
        ModuleContext context = new(builder.Configuration, builder.Configuration.GetRequiredAppSecrets());

        // Controllers live in the module assemblies, so their parts are registered explicitly
        // instead of relying on MVC guessing the application assembly from the entry point.
        IMvcBuilder mvc = builder.Services.AddControllers();

        foreach (IModule module in modules)
        {
            mvc.AddApplicationPart(module.GetType().Assembly);

            module.AddApi(builder.Services);
            module.AddInfrastructure(builder.Services, context);
        }

        builder.Services.AddMediatR(cfg =>
        {
            foreach (IModule module in modules)
            {
                cfg.RegisterServicesFromAssembly(module.HandlerAssembly);
            }

            // Authorization behaviour must run before validation behaviour.
            cfg.AddOpenBehavior(typeof(AuthorizationBehaviour<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
        });

        // Exposed for diagnostics and for the composition test that pins the module set.
        builder.Services.AddSingleton(modules);

        return builder;
    }

    private static IReadOnlyList<IModule> DiscoverModules()
    {
        string[] assemblyNames = (DependencyContext.Default?.RuntimeLibraries ?? Array.Empty<RuntimeLibrary>())
            .Select(library => library.Name)
            .Where(name => name.StartsWith(ModuleAssemblyPrefix, StringComparison.Ordinal)
                           && name.EndsWith(ModuleAssemblySuffix, StringComparison.Ordinal))
            .ToArray();

        IModule[] modules = assemblyNames
            .Select(Assembly.Load)
            .SelectMany(LoadableTypes)
            .Where(type => typeof(IModule).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })
            .Select(type => (IModule)Activator.CreateInstance(type)!)
            .OrderBy(module => module.Name, StringComparer.Ordinal)
            .ToArray();

        if (modules.Length == 0)
        {
            // Starting without modules would serve 404 for every business route with no error at all.
            throw new InvalidOperationException(
                "No IModule implementations were found. Check that the module *.Api assemblies are referenced by the host.");
        }

        return modules;
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
    }
}
