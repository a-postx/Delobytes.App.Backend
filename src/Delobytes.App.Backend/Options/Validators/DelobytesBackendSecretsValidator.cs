using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Options.Validators;

public class DelobytesBackendSecretsValidator : IValidateOptions<DelobytesBackendSecrets>
{
    public ValidateOptionsResult Validate(string? name, DelobytesBackendSecrets options)
    {
        List<string> failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add($"{nameof(options.ConnectionString)} secret is not found.");
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
