using Delobytes.App.Backend.Integrations.Application.Options;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Options.Validators;

/// <summary>
/// Validates that the orders import run budget plus the maximum in-process throttle wait stays
/// below the 30-minute OrdersImport watchdog timeout. A configuration above that boundary lets a
/// run race the watchdog instead of stopping itself cleanly: the watchdog would fail the JobRun
/// while its consumer is still fetching, and the broker would redeliver a message whose work is
/// already in flight.
/// </summary>
public class WildberriesImportOptionsValidator : IValidateOptions<WildberriesImportOptions>
{
    internal static readonly TimeSpan MaximumAllowedTotal = TimeSpan.FromMinutes(30);

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, WildberriesImportOptions options)
    {
        TimeSpan budget = TimeSpan.FromSeconds(options.BackfillRunBudgetSeconds > 0
            ? options.BackfillRunBudgetSeconds
            : WildberriesImportOptions.DefaultBackfillRunBudgetSeconds);

        TimeSpan maxInProcessWait = TimeSpan.FromSeconds(options.MaxInProcessWaitSeconds >= 0
            ? options.MaxInProcessWaitSeconds
            : WildberriesImportOptions.DefaultMaxInProcessWaitSeconds);

        TimeSpan total = budget + maxInProcessWait;

        if (total >= MaximumAllowedTotal)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(WildberriesImportOptions)}.{nameof(WildberriesImportOptions.BackfillRunBudgetSeconds)} " +
                $"({budget.TotalSeconds:F0}s) plus {nameof(WildberriesImportOptions.MaxInProcessWaitSeconds)} " +
                $"({maxInProcessWait.TotalSeconds:F0}s) must total less than " +
                $"{MaximumAllowedTotal.TotalMinutes:F0} minutes, but got {total.TotalSeconds:F0}s.");
        }

        return ValidateOptionsResult.Success;
    }
}
