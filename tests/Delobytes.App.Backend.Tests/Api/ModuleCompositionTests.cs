using System.Collections.Generic;
using System.Linq;
using Delobytes.App.Backend.Contracts.Modules;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Tests.Api;

/// <summary>
/// Pins the module set found by assembly scanning, so a module that silently stops being discovered
/// fails the build instead of serving 404 for its routes.
/// </summary>
public sealed class ModuleCompositionTests : IClassFixture<ApiHost>
{
    private readonly ApiHost _host;

    public ModuleCompositionTests(ApiHost host)
    {
        _host = host;
    }

    [Fact]
    public void CreateApp_DiscoversAllModules()
    {
        IReadOnlyList<IModule> modules = _host.Services.GetRequiredService<IReadOnlyList<IModule>>();

        modules.Select(module => module.Name)
            .Should().Equal("Catalog", "Finance", "Identity", "Integrations", "Sales");
    }
}
