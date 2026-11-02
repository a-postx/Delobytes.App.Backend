using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Infrastructure.Options.Validators;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Validators;

public class OzonImportOptionsValidatorTests
{
    private readonly OzonImportOptionsValidator _validator = new();

    [Fact]
    public void Validate_DefaultOptions_ReturnsSuccess()
    {
        ValidateOptionsResult result = _validator.Validate(null, new OzonImportOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_TotalBelowBoundary_ReturnsSuccess()
    {
        OzonImportOptions options = new OzonImportOptions
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
        OzonImportOptions options = new OzonImportOptions
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
        OzonImportOptions options = new OzonImportOptions
        {
            BackfillRunBudgetSeconds = 1800,
            MaxInProcessWaitSeconds = 600,
        };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        string failure = Assert.Single(result.Failures);
        failure.Should().Contain(nameof(OzonImportOptions.BackfillRunBudgetSeconds));
        failure.Should().Contain(nameof(OzonImportOptions.MaxInProcessWaitSeconds));
    }

    [Fact]
    public void Validate_NonPositiveBudget_FallsBackToDefaultBudget()
    {
        OzonImportOptions options = new OzonImportOptions
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
        OzonImportOptions options = new OzonImportOptions
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
        OzonImportOptions options = new OzonImportOptions
        {
            BackfillRunBudgetSeconds = 20 * 60,
            MaxInProcessWaitSeconds = 0,
        };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_MinimumRequestIntervalBelowMaxWait_ReturnsSuccess()
    {
        OzonImportOptions options = new OzonImportOptions
        {
            BackfillRunBudgetSeconds = 20 * 60,
            MaxInProcessWaitSeconds = 120,
            MinimumRequestIntervalSeconds = 15,
        };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Succeeded.Should().BeTrue("a 15s interval is well inside the 120s in-process wait tolerance");
    }

    [Fact]
    public void Validate_MinimumRequestIntervalEqualToMaxWait_ReturnsFailure()
    {
        // Every request would be throttled by the interval itself, so the run could never advance.
        OzonImportOptions options = new OzonImportOptions
        {
            BackfillRunBudgetSeconds = 10 * 60,
            MaxInProcessWaitSeconds = 60,
            MinimumRequestIntervalSeconds = 60,
        };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(OzonImportOptions.MinimumRequestIntervalSeconds));
        result.FailureMessage.Should().Contain(nameof(OzonImportOptions.MaxInProcessWaitSeconds));
    }

    [Fact]
    public void Validate_MinimumRequestIntervalAboveMaxWait_ReturnsFailure()
    {
        OzonImportOptions options = new OzonImportOptions
        {
            BackfillRunBudgetSeconds = 10 * 60,
            MaxInProcessWaitSeconds = 30,
            MinimumRequestIntervalSeconds = 120,
        };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_MinimumRequestIntervalWithNoInProcessWait_ReturnsSuccess()
    {
        // With MaxInProcessWaitSeconds = 0 the fetcher never waits in-process: it stops the run and
        // continues later, so a long minimum interval cannot stall it.
        OzonImportOptions options = new OzonImportOptions
        {
            BackfillRunBudgetSeconds = 20 * 60,
            MaxInProcessWaitSeconds = 0,
            MinimumRequestIntervalSeconds = 600,
        };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_ZeroMinimumRequestInterval_ReturnsSuccess()
    {
        OzonImportOptions options = new OzonImportOptions
        {
            BackfillRunBudgetSeconds = 20 * 60,
            MaxInProcessWaitSeconds = 60,
            MinimumRequestIntervalSeconds = 0,
        };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Succeeded.Should().BeTrue("zero disables the throttle");
    }

    [Fact]
    public void Validate_NegativeMinimumRequestInterval_FallsBackToTheDefault()
    {
        // A negative value is read as "use the default 15s", which still fits inside a 60s wait.
        OzonImportOptions options = new OzonImportOptions
        {
            BackfillRunBudgetSeconds = 20 * 60,
            MaxInProcessWaitSeconds = 60,
            MinimumRequestIntervalSeconds = -5,
        };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_DefaultMinimumRequestInterval_IsBelowTheDefaultWait()
    {
        OzonImportOptions defaults = new OzonImportOptions();

        defaults.MinimumRequestIntervalSeconds.Should().Be(OzonImportOptions.DefaultMinimumRequestIntervalSeconds);
        defaults.MinimumRequestIntervalSeconds.Should().BeLessThan(defaults.MaxInProcessWaitSeconds,
            "the shipped defaults must satisfy the validator they are checked against");
    }

    [Fact]
    public void Validate_DefaultBudgetPlusDefaultWait_LeavesWatchdogMargin()
    {
        TimeSpan defaults = TimeSpan.FromSeconds(
            OzonImportOptions.DefaultBackfillRunBudgetSeconds + OzonImportOptions.DefaultMaxInProcessWaitSeconds);

        defaults.Should().BeLessThan(OzonImportOptionsValidator.MaximumAllowedTotal);
    }
}
