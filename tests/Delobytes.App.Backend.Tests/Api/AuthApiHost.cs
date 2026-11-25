using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Models;
using Delobytes.App.Backend.Identity.Infrastructure.Persistence;
using Delobytes.App.Backend.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Delobytes.App.Backend.Tests.Api;

/// <summary>
/// Boots the real pipeline for authentication HTTP tests: Identity persistence is replaced with
/// an in-memory database and both OAuth providers with deterministic stubs, so no external
/// service is contacted. Rate limits are raised by default; <see cref="RateLimitAuthApiHost"/>
/// keeps the production defaults.
/// </summary>
public class AuthApiHost : IAsyncLifetime
{
    private static readonly Dictionary<string, string?> BaseSettings = new()
    {
        ["YC:Enabled"] = "false",
        ["Database:MigrateOnStartup"] = "false",
        ["Background:Enabled"] = "false",
        ["Messaging:UseInMemory"] = "true",
        ["Observability:UseRemoteSink"] = "false",

        // Only satisfies startup validation; the in-memory identity context replaces the real one.
        ["AppSecrets:ConnectionString"] = "Host=localhost;Database=delobytes_tests;Username=tests;Password=tests",
        ["AppSecrets:JwtSecretKey"] = "tests-only-signing-key-0123456789abcdef",
    };

    private WebApplication _app = null!;

    public HttpClient Client { get; private set; } = null!;

    protected virtual IReadOnlyDictionary<string, string?> ExtraSettings { get; } = new Dictionary<string, string?>
    {
        // Functional tests must not exhaust the production buckets.
        ["RateLimiting:LoginPerMinute"] = "1000",
        ["RateLimiting:RegisterPerMinute"] = "1000",
        ["RateLimiting:OAuthCallbackPerMinute"] = "1000",
    };

    public async Task InitializeAsync()
    {
        string databaseName = "auth-http-" + Guid.NewGuid().ToString("N");

        Dictionary<string, string?> settings = new(BaseSettings, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string?> setting in ExtraSettings)
        {
            settings[setting.Key] = setting.Value;
        }

        _app = await Program.CreateApp(
            Array.Empty<string>(),
            builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(settings);
            },
            services => ConfigureTestServices(services, databaseName));

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

    private static void ConfigureTestServices(IServiceCollection services, string databaseName)
    {
        // The module registration left both the context and the Npgsql options configuration
        // behind; the options configuration is what actually applies UseNpgsql, so it must go too.
        services.RemoveAll<IdentityDbContext>();
        services.RemoveAll<DbContextOptions<IdentityDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
        services.AddModuleDbContext<IdentityDbContext>(
            (_, options) => options.UseInMemoryDatabase(databaseName));

        services.RemoveAll<IOAuthProvider>();
        services.AddScoped<IOAuthProvider>(_ => new StubOAuthProvider(
            OAuthProviderKind.Yandex,
            "YandexID",
            new OAuthUserInfo { ExternalId = "stub-yandex-sub", Email = "stub-yandex@example.com" }));
        services.AddScoped<IOAuthProvider>(_ => new StubOAuthProvider(
            OAuthProviderKind.Google,
            "GoogleID",
            new OAuthUserInfo { ExternalId = "stub-google-sub", Email = "stub-google@example.com" }));
    }

    private sealed class StubOAuthProvider : IOAuthProvider
    {
        private readonly OAuthUserInfo _userInfo;

        public StubOAuthProvider(
            OAuthProviderKind kind,
            string identityProviderName,
            OAuthUserInfo userInfo)
        {
            Kind = kind;
            IdentityProviderName = identityProviderName;
            _userInfo = userInfo;
        }

        public OAuthProviderKind Kind { get; }

        public string IdentityProviderName { get; }

        public Task<OAuthUserInfo> AuthorizeAsync(
            string code,
            string redirectUri,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_userInfo);
        }
    }
}

/// <summary>
/// Same host with production rate-limit defaults, used to verify the 429 responses.
/// </summary>
public sealed class RateLimitAuthApiHost : AuthApiHost
{
    protected override IReadOnlyDictionary<string, string?> ExtraSettings { get; }
        = new Dictionary<string, string?>();
}
