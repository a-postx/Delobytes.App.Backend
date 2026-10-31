using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Infrastructure.Options.Validators;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Validators;

public class WildberriesImportOptionsValidatorTests
{
    private readonly WildberriesImportOptionsValidator _validator = new();

    [Fact]
    public void Validate_DefaultOptions_ReturnsSuccess()
    {
        ValidateOptionsResult result = _validator.Validate(null, new WildberriesImportOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_TotalBelowBoundary_ReturnsSuccess()
    {
        WildberriesImportOptions options = new WildberriesImportOptions
        {
            BackfillRunBudgetSeconds = 25 * 60,
            MaxInProcessWaitSeconds = 4 * 60,
        };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_TotalExactlyAtBoundary_ReturnsFailure()
    {
        WildberriesImportOptions options = new WildberriesImportOptions
        {
            BackfillRunBudgetSeconds = 20 * 60,
            MaxInProcessWaitSeconds = 10 * 60,
        };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_TotalAboveBoundary_ReturnsFailureNamingBothSettings()
    {
        WildberriesImportOptions options = new WildberriesImportOptions
        {
            BackfillRunBudgetSeconds = 1800,
            MaxInProcessWaitSeconds = 600,
        };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        string failure = Assert.Single(result.Failures);
        failure.Should().Contain(nameof(WildberriesImportOptions.BackfillRunBudgetSeconds));
        failure.Should().Contain(nameof(WildberriesImportOptions.MaxInProcessWaitSeconds));
    }

    [Fact]
    public void Validate_NonPositiveBudget_FallsBackToDefaultBudget()
    {
        // A non-positive budget resolves to the default, so the validated total is the default
        // budget plus the configured wait - not the wait alone.
        WildberriesImportOptions options = new WildberriesImportOptions
        {
            BackfillRunBudgetSeconds = 0,
            MaxInProcessWaitSeconds = 30 * 60,
        };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_NegativeWait_FallsBackToDefaultWait()
    {
        WildberriesImportOptions options = new WildberriesImportOptions
        {
            BackfillRunBudgetSeconds = 30 * 60,
            MaxInProcessWaitSeconds = -1,
        };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_ZeroWaitIsAllowed_AsItMeansNoInProcessWait()
    {
        WildberriesImportOptions options = new WildberriesImportOptions
        {
            BackfillRunBudgetSeconds = 20 * 60,
            MaxInProcessWaitSeconds = 0,
        };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_DefaultBudgetPlusDefaultWait_LeavesWatchdogMargin()
    {
        // The validator boundary is the 30-minute OrdersImport watchdog timeout; the shipped
        // defaults must fit inside it with room to spare, otherwise every fresh deployment
        // would fail its own startup validation.
        TimeSpan defaults = TimeSpan.FromSeconds(
            WildberriesImportOptions.DefaultBackfillRunBudgetSeconds + WildberriesImportOptions.DefaultMaxInProcessWaitSeconds);

        defaults.Should().BeLessThan(WildberriesImportOptionsValidator.MaximumAllowedTotal);
    }
}
