using Delobytes.App.Backend.Contracts.Secrets;
using Delobytes.App.Backend.Extensions;
using Delobytes.AspNetCore.Logging;
using Microsoft.AspNetCore.Http;

namespace Delobytes.App.Backend.Composition;

internal static class Observability
{
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        AppSecrets secrets = builder.Configuration.GetRequiredAppSecrets();

        // The remote (OpenSearch) sink is registered only when explicitly enabled. The same flag
        // makes AppSecretsValidator require the ElasticSearch secrets, so a deployment cannot
        // silently start without centralised logs.
        bool useRemoteSink = builder.Configuration.GetValue("Observability:UseRemoteSink", true);
        builder.AddSerilog(useRemoteSink ? secrets : null);

        builder.Services.AddHttpContextLogging(options =>
        {
            options.LogRequestBody = true;
            options.LogResponseBody = true;
            options.MaxBodyLength = 32759;
            // These paths carry passwords, authorization codes, JWTs, invitation tokens
            // or marketplace API keys in their bodies. The middleware can only skip a
            // whole path (exact match), so no request/response event is written for them.
            options.SkipPaths = new List<PathString>
            {
                "/metrics",
                "/api/auth/login",
                "/api/auth/register",
                "/api/auth/change-password",
                "/api/auth/yandex/callback",
                "/api/auth/google/callback",
                "/api/auth/create-tenant",
                "/api/tenant/switch",
                "/api/tenant/invitations",
                "/api/tenant/invitations/accept",
                "/api/integrations/connections",
            };
            options.SkipRequestHeaders = new List<string> { "Authorization" };
        });

        builder.Services.AddCustomHealthChecks(secrets);

        return builder;
    }
}
