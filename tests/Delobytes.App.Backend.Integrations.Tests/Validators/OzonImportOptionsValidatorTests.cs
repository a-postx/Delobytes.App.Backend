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
    public void Validate_DefaultBudgetPlusDefaultWait_LeavesWatchdogMargin()
    {
        TimeSpan defaults = TimeSpan.FromSeconds(
            OzonImportOptions.DefaultBackfillRunBudgetSeconds + OzonImportOptions.DefaultMaxInProcessWaitSeconds);

        defaults.Should().BeLessThan(OzonImportOptionsValidator.MaximumAllowedTotal);
    }
}
