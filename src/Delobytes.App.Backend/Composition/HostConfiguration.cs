using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Contracts.Secrets;
using Delobytes.App.Backend.Extensions;
using Delobytes.App.Backend.Options;
using Delobytes.Extensions.Configuration;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Serilog;

namespace Delobytes.App.Backend.Composition;

internal static class HostConfiguration
{
    public static WebApplicationBuilder AddHostConfiguration(this WebApplicationBuilder builder)
    {
        builder.AddConfigurationSources();

        builder.ConfigureJsonOptions();

        builder.Services.Configure<HostOptions>(opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(30));

        builder.WebHost
            .CaptureStartupErrors(true)
            .UseShutdownTimeout(TimeSpan.FromSeconds(Timeouts.WebHostShutdownTimeoutSec));

        // Kestrel stays the default server, but configuring it through options instead of UseKestrel
        // keeps the server replaceable: a test host can swap in TestServer before the app is built.
        builder.Services.Configure<KestrelServerOptions>(options => options.AddServerHeader = false);
        builder.Services.Configure<KestrelServerOptions>(builder.Configuration.GetSection(nameof(AppSettings.Kestrel)));

        builder.Services.AddCustomOptions(builder.Configuration);

        return builder;
    }

    public static AppSecrets GetRequiredAppSecrets(this IConfiguration configuration)
    {
        AppSecrets? secrets = configuration.GetSection(nameof(AppSecrets)).Get<AppSecrets>();

        return secrets ?? throw new InvalidOperationException(nameof(secrets) + " not found");
    }

    private static void AddConfigurationSources(this WebApplicationBuilder builder)
    {
        // Tests and other hosts that receive secrets elsewhere set this flag and skip Lockbox entirely.
        if (!builder.Configuration.GetValue("YC:Enabled", true))
        {
            return;
        }

        builder.Configuration.AddYandexCloudLockboxConfiguration(config =>
        {
            builder.Configuration
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables();

            IConfigurationRoot tempConfig = new ConfigurationBuilder()
                   .AddJsonFile("appsettings.json")
                   .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: false, reloadOnChange: false)
                   .Build();

            config.PrivateKey = Environment.GetEnvironmentVariable("YC_PRIVATE_KEY") ?? throw new InvalidOperationException("YC_PRIVATE_KEY is required.");
            config.ServiceAccountId = tempConfig.GetValue<string>("YC:ServiceAccountId") ?? throw new InvalidOperationException("YC:ServiceAccountId is required.");
            config.ServiceAccountAuthorizedKeyId = tempConfig.GetValue<string>("YC:ServiceAccountAuthorizedKeyId") ?? throw new InvalidOperationException("YC:ServiceAccountAuthorizedKeyId is required.");
            config.SecretId = tempConfig.GetValue<string>("YC:ConfigurationSecretId") ?? throw new InvalidOperationException("YC:ConfigurationSecretId is required.");
            config.PathSeparator = '-';
            config.Optional = false;
            config.ReloadPeriod = TimeSpan.FromDays(7);
            config.LoadTimeout = TimeSpan.FromSeconds(20);
            config.OnLoadException += exceptionContext =>
            {
                Log.Warning(exceptionContext.Exception, "Failed to load secrets from Yandex Cloud Lockbox: {Message}", exceptionContext.Exception.Message);
            };
        });
    }
}
