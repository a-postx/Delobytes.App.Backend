using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace Delobytes.App.Backend.Tests.Api;

/// <summary>
/// Boots the real application pipeline in-process through <see cref="Program.CreateApp"/>.
/// </summary>
public sealed class ApiHost : IAsyncLifetime
{
    private static readonly Dictionary<string, string?> TestSettings = new()
    {
        // Host behaviour that needs external services is disabled explicitly, not by environment name.
        ["YC:Enabled"] = "false",
        ["Database:MigrateOnStartup"] = "false",
        ["Background:Enabled"] = "false",
        ["Messaging:UseInMemory"] = "true",
        ["Observability:UseRemoteSink"] = "false",

        // Only satisfies startup validation; a smoke test never opens these endpoints.
        ["AppSecrets:ConnectionString"] = "Host=localhost;Database=delobytes_tests;Username=tests;Password=tests",
        ["AppSecrets:JwtSecretKey"] = "tests-only-signing-key-0123456789abcdef",
    };

    private WebApplication _app = null!;

    public HttpClient Client { get; private set; } = null!;

    public IServiceProvider Services => _app.Services;

    public async Task InitializeAsync()
    {
        _app = await Program.CreateApp(
            Array.Empty<string>(),
            builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(TestSettings);
            });

        await _app.StartAsync();

        Client = _app.GetTestServer().CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
