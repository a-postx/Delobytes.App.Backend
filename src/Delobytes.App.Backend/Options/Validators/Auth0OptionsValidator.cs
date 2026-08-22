using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Options.Validators;

/// <summary>
/// Валидатор настроек Auth0.
/// </summary>
public class Auth0OptionsValidator : IValidateOptions<Auth0Options>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, Auth0Options options)
    {
        List<string> failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Domain))
        {
            failures.Add($"{nameof(options.Domain)} is not configured. Set Auth0:Domain in appsettings.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add($"{nameof(options.Audience)} is not configured. Set Auth0:Audience in appsettings.");
        }

        if (failures.Count > 0)
        {
            return ValidateOptionsResult.Fail(failures);
        }

        return ValidateOptionsResult.Success;
    }
}
