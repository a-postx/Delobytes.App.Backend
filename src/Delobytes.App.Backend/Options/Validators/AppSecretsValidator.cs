using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Options.Validators;

public class AppSecretsValidator : IValidateOptions<AppSecrets>
{
    public ValidateOptionsResult Validate(string? name, AppSecrets options)
    {
        List<string> failures = new List<string>();

        ////if (string.IsNullOrWhiteSpace(options.KeycloakManagementApiClientId))
        ////{
        ////    failures.Add($"{nameof(options.KeycloakManagementApiClientId)} secret is not found.");
        ////}

        ////if (string.IsNullOrWhiteSpace(options.KeycloakManagementApiClientSecret))
        ////{
        ////    failures.Add($"{nameof(options.KeycloakManagementApiClientSecret)} secret is not found.");
        ////}

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
