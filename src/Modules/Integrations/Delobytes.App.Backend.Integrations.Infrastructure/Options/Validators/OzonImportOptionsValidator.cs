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

        TimeSpan minimumRequestInterval = TimeSpan.FromSeconds(options.MinimumRequestIntervalSeconds >= 0
            ? options.MinimumRequestIntervalSeconds
            : OzonImportOptions.DefaultMinimumRequestIntervalSeconds);

        // Only meaningful when an in-process wait is actually allowed: with MaxInProcessWaitSeconds = 0
        // the fetcher never waits, it stops the run and continues later, so a minimum interval above
        // zero does not stall the run.
        if (maxInProcessWait > TimeSpan.Zero
            && minimumRequestInterval > TimeSpan.Zero
            && minimumRequestInterval >= maxInProcessWait)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(OzonImportOptions)}.{nameof(OzonImportOptions.MinimumRequestIntervalSeconds)} " +
                $"({minimumRequestInterval.TotalSeconds:F0}s) must be less than " +
                $"{nameof(OzonImportOptions.MaxInProcessWaitSeconds)} ({maxInProcessWait.TotalSeconds:F0}s), " +
                "otherwise every request would be throttled by the minimum interval and the run would stop.");
        }

        return ValidateOptionsResult.Success;
    }
}
