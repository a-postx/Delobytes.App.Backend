using Delobytes.App.Backend.Options;

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
}
