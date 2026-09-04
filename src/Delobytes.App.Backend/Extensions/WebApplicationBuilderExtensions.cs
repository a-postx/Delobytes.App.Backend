using System.Diagnostics;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Delobytes.App.Backend.Options;
using Microsoft.AspNetCore.Http.Json;
using Serilog;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Exceptions.Core;
using Serilog.Exceptions.EntityFrameworkCore.Destructurers;
using Serilog.Formatting.OpenSearch;
using Serilog.Sinks.OpenSearch;

namespace Delobytes.App.Backend.Extensions;

internal static class WebApplicationBuilderExtensions
{
    public static WebApplicationBuilder AddOptionsWithValidation(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddCustomOptions(builder.Configuration)
            .AddOptionsValidationOnStartup();

        AppSecrets? secrets = builder.Configuration.GetSection(nameof(AppSecrets)).Get<AppSecrets>();

        if (secrets == null)
        {
            throw new InvalidOperationException(nameof(secrets) + " not found");
        }

        return builder;
    }

    public static WebApplicationBuilder ConfigureJsonOptions(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder, nameof(builder));

        // патч-библиотека заменена на поддерживаемую, удалить при случае
        // https://github.com/myquay/JsonPatch/blob/master/README.md

        // патч
        ////JsonPatchSettings.Options = new JsonPatchOptions
        ////{
        ////    PathResolver = new ExactCasePropertyPathResolver(new JsonValueConverter()),
        ////    RequireJsonPatchContentType = false,
        ////};

        // прочее
        builder.Services.Configure<JsonOptions>(o =>
        {
            if (builder.Environment.IsDevelopment())
            {
                o.SerializerOptions.WriteIndented = true;
            }

            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
        });

        return builder;
    }

    /// <summary>
    /// Configures Serilog with OpenSearch and Console sinks.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="secrets">Application secrets.</param>
    /// <returns>The same builder for chaining.</returns>
    public static WebApplicationBuilder AddSerilog(this WebApplicationBuilder builder, AppSecrets? secrets)
    {
        builder.Host
            .UseSerilog((ctx, svc, logConf) => UpdateReloadableLoggerConfig(ctx, svc, logConf, secrets));

        return builder;
    }

    /// <summary>
    /// Добавляет расширенный логер с отправкой данных в удалённые системы
    /// </summary>
    private static void UpdateReloadableLoggerConfig(HostBuilderContext context, IServiceProvider services, LoggerConfiguration loggerConfig, AppSecrets? secrets)
    {
        ////Serilog.Debugging.SelfLog.Enable(msg => Console.WriteLine(msg));

        string osPlatform = string.Empty;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            osPlatform = OSPlatform.Windows.ToString();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            osPlatform = OSPlatform.Linux.ToString();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            osPlatform = OSPlatform.OSX.ToString();
        }
        else
        {
            osPlatform = "unknown";
        }

        loggerConfig
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("AppName", Program.AppName ?? string.Empty)
            .Enrich.WithProperty("Version", Program.AppVersion != null ? Program.AppVersion.ToString() : string.Empty)
            .Enrich.WithProperty("NodeId", Node.Id)
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("ProcessName", Process.GetCurrentProcess().ProcessName)
            .Enrich.WithProperty("MachineName", Environment.MachineName)
            .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
            .Enrich.WithProperty("EnvironmentUserName", Environment.UserName)
            .Enrich.WithProperty("OSPlatform", osPlatform)
            .Enrich.FromMassTransitMessage()
            ////.Enrich.FromCustomMbMessageContext()
            .Enrich.WithExceptionDetails(new DestructuringOptionsBuilder()
                .WithDefaultDestructurers()

                // ускоряет расструктуризатор EF Core
                .WithDestructurers(new[] { new DbUpdateExceptionDestructurer() }));

        loggerConfig.WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information);

        if (secrets is null || string.IsNullOrEmpty(secrets.ElasticSearchUrl)
                            || string.IsNullOrEmpty(secrets.ElasticSearchUser)
                            || string.IsNullOrEmpty(secrets.ElasticSearchPassword))
        {
            Log.Information("Sending logs to remote log managment systems is disabled.");
            return;
        }

        Dictionary<string, string> customIndexTemplateSettings = new Dictionary<string, string>
        {
            { "index.lifecycle.name", context.HostingEnvironment.IsProduction() ? "delobytes-logs-prod-policy" : "delobytes-logs-dev-policy" },
        };

        loggerConfig.WriteTo.OpenSearch(new OpenSearchSinkOptions(new Uri(secrets.ElasticSearchUrl))
        {
            ModifyConnectionSettings = conn =>
            {
                conn.BasicAuthentication(secrets.ElasticSearchUser, secrets.ElasticSearchPassword);

                // "https://rc1b-8k9r4mkxxxxxxxxxx.mdb.yandexcloud.net:9200"
                conn.ServerCertificateValidationCallback(TimeWebCloudRootCaCertificateValidationCallback);
                ////conn.EnableDebugMode(conn =>
                ////{
                ////   string info = conn.DebugInformation;
                ////});
                return conn;
            },
            BatchPostingLimit = 1000,
            Period = TimeSpan.FromSeconds(10),
            MinimumLogEventLevel = LogEventLevel.Debug,
            EmitEventFailure = EmitEventFailureHandling.RaiseCallback,
            FailureCallback = (e) =>
            {
                Console.WriteLine("Unable to submit log event to ELK: " + e.MessageTemplate + "\nError: " + e.Exception);
            },

            // в отличие от индекса, шаблон политики не может быть создан через АПИ в OpenSearch
            // https://github.com/opensearch-project/OpenSearch/commit/1bb7b53f267c715235faac04f64e82132ef81843
            AutoRegisterTemplate = true,
            AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.OSv1,
            OverwriteTemplate = false,
            RegisterTemplateFailure = RegisterTemplateRecovery.IndexAnyway,
            TemplateName = context.HostingEnvironment.IsProduction() ? "delobytes-logs-prod-template" : "delobytes-logs-dev-template",
            TemplateCustomSettings = customIndexTemplateSettings,
            InlineFields = true,
            IndexFormat = context.HostingEnvironment.IsProduction() ? "delobytes-logs-prod-{0:yyyy.MM.dd}" : "delobytes-logs-dev-{0:yyyy.MM.dd}",
            DeadLetterIndexName = context.HostingEnvironment.IsProduction() ? "delobytes-prod-deadletter-{0:yyyy.MM.dd}" : "delobytes-dev-deadletter-{0:yyyy.MM.dd}",
            CustomFormatter = new ExceptionAsObjectJsonFormatter(renderMessage: true, inlineFields: true),
            BufferCleanPayload = (failingEvent, statuscode, exception) =>
            {
                dynamic e = Newtonsoft.Json.Linq.JObject.Parse(failingEvent);
                return Newtonsoft.Json.JsonConvert.SerializeObject(new Dictionary<string, object>()
                {
                    { "@timestamp", e["@timestamp"] },
                    { "level", "Error" },
                    { "message", "Error: " + e.message },
                    { "messageTemplate", e.messageTemplate },
                    { "failingStatusCode", statuscode ?? 0 },
                    { "failingException", exception },
                });
            },
        });
    }

    /// <summary>
    /// Проверка сертификата у провайдера Timeweb. Провайдер работает с невалидным сертификатом,
    /// поэтому просто проверяем его отпечаток.
    /// </summary>
    private static bool TimeWebCloudRootCaCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        string rootCAThumbprint = "D6731572C5858C3921B03F0F17A0F418C8D39CD4";

        // remove this line if commercial CAs are not allowed to issue certificate for your service.
        if ((sslPolicyErrors & SslPolicyErrors.None) > 0)
        {
            return true;
        }

        // get last chain element that should contain root CA certificate
        // but this may not be the case in partial chains
        X509Certificate2 projectedRootCert = chain.ChainElements[^1].Certificate;

        return projectedRootCert.Thumbprint == rootCAThumbprint;
    }
}
