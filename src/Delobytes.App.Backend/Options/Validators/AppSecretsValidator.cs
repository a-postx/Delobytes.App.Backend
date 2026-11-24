using Delobytes.App.Backend.Contracts.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Options.Validators;

/// <summary>
/// Валидатор секретов приложения.
/// </summary>
public class AppSecretsValidator : IValidateOptions<AppSecrets>
{
    private readonly IConfiguration _configuration;

    public AppSecretsValidator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, AppSecrets options)
    {
        List<string> failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add($"{nameof(options.ConnectionString)} secret is not found.");
        }

        if (string.IsNullOrWhiteSpace(options.JwtSecretKey))
        {
            failures.Add($"{nameof(options.JwtSecretKey)} secret is not found.");
        }

        // The in-memory transport is a deliberate choice for tests and local runs. Anywhere else a
        // missing broker must stop startup: events would be accepted and silently dropped.
        bool useInMemoryTransport = _configuration.GetValue<bool>("Messaging:UseInMemory");
        if (!useInMemoryTransport && string.IsNullOrWhiteSpace(options.MessageBusConnectionString))
        {
            failures.Add($"{nameof(options.MessageBusConnectionString)} secret is not found.");
        }

        // Shipping logs to OpenSearch is on by default; if it is on, the secrets must be there,
        // otherwise the sink is skipped and centralised logging is silently lost.
        bool useRemoteSink = _configuration.GetValue<bool?>("Observability:UseRemoteSink") ?? true;
        if (useRemoteSink)
        {
            if (string.IsNullOrWhiteSpace(options.ElasticSearchUrl))
            {
                failures.Add($"{nameof(options.ElasticSearchUrl)} secret is not found.");
            }

            if (string.IsNullOrWhiteSpace(options.ElasticSearchUser))
            {
                failures.Add($"{nameof(options.ElasticSearchUser)} secret is not found.");
            }

            if (string.IsNullOrWhiteSpace(options.ElasticSearchPassword))
            {
                failures.Add($"{nameof(options.ElasticSearchPassword)} secret is not found.");
            }
        }

        if (failures.Count > 0)
        {
            return ValidateOptionsResult.Fail(failures);
        }
        else
        {
            return ValidateOptionsResult.Success;
        }
    }
}
