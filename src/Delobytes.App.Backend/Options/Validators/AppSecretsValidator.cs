using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Options.Validators;

/// <summary>
/// Валидатор секретов приложения.
/// </summary>
public class AppSecretsValidator : IValidateOptions<AppSecrets>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, AppSecrets options)
    {
        List<string> failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add($"{nameof(options.ConnectionString)} secret is not found.");
        }

        if (string.IsNullOrWhiteSpace(options.MessageBusConnectionString))
        {
            failures.Add($"{nameof(options.MessageBusConnectionString)} secret is not found.");
        }

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

        if (string.IsNullOrWhiteSpace(options.JwtSecretKey))
        {
            failures.Add($"{nameof(options.JwtSecretKey)} secret is not found.");
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
