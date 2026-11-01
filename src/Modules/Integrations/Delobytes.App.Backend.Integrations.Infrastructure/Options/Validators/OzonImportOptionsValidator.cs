using Delobytes.App.Backend.Integrations.Application.Options;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Options.Validators;

/// <summary>
/// Validates that the Ozon orders import run budget plus the maximum in-process throttle wait
/// stays below the 30-minute OrdersImport watchdog timeout, mirroring
/// <see cref="WildberriesImportOptionsValidator"/>.
/// </summary>
public class OzonImportOptionsValidator : IValidateOptions<OzonImportOptions>
{
    internal static readonly TimeSpan MaximumAllowedTotal = TimeSpan.FromMinutes(30);

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, OzonImportOptions options)
    {
        TimeSpan budget = TimeSpan.FromSeconds(options.BackfillRunBudgetSeconds > 0
            ? options.BackfillRunBudgetSeconds
            : OzonImportOptions.DefaultBackfillRunBudgetSeconds);

        TimeSpan maxInProcessWait = TimeSpan.FromSeconds(options.MaxInProcessWaitSeconds >= 0
            ? options.MaxInProcessWaitSeconds
            : OzonImportOptions.DefaultMaxInProcessWaitSeconds);

        TimeSpan total = budget + maxInProcessWait;

        if (total >= MaximumAllowedTotal)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(OzonImportOptions)}.{nameof(OzonImportOptions.BackfillRunBudgetSeconds)} " +
                $"({budget.TotalSeconds:F0}s) plus {nameof(OzonImportOptions.MaxInProcessWaitSeconds)} " +
                $"({maxInProcessWait.TotalSeconds:F0}s) must total less than " +
                $"{MaximumAllowedTotal.TotalMinutes:F0} minutes, but got {total.TotalSeconds:F0}s.");
        }

        return ValidateOptionsResult.Success;
    }
}
