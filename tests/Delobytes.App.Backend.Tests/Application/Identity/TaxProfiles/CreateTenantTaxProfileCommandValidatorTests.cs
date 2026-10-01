using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Identity.Application.Commands.TaxProfiles.CreateTenantTaxProfile;
using FluentAssertions;
using FluentValidation.Results;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.TaxProfiles;

/// <summary>
/// Tests for CreateTenantTaxProfileCommandValidator.
///
/// RatePercent is a percentage (0–100), not a fraction. The old Tenant.TaxRatePercent
/// column was numeric(8,6), which could not hold 100 at all, so the boundary is pinned
/// here as well as in the persistence test.
/// </summary>
public class CreateTenantTaxProfileCommandValidatorTests
{
    private readonly CreateTenantTaxProfileCommandValidator _validator = new CreateTenantTaxProfileCommandValidator();

    private static CreateTenantTaxProfileCommand BuildValidCommand() => new CreateTenantTaxProfileCommand
    {
        TenantId = Guid.NewGuid(),
        Regime = TaxRegime.UsnIncome,
        RatePercent = 6m,
        Vat = VatType.None,
        ValidFrom = new DateOnly(2026, 1, 1),
    };

    private static bool HasErrorFor(ValidationResult result, string propertyName) =>
        result.Errors.Exists(failure => failure.PropertyName == propertyName);

    [Fact]
    public void Validate_FullyPopulatedCommand_IsValid()
    {
        // Arrange
        CreateTenantTaxProfileCommand command = BuildValidCommand();

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyTenantId_IsInvalid()
    {
        // Arrange
        CreateTenantTaxProfileCommand command = BuildValidCommand();
        command.TenantId = Guid.Empty;

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        HasErrorFor(result, nameof(CreateTenantTaxProfileCommand.TenantId)).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(100)]
    public void Validate_RatePercentWithinInclusiveRange_IsValid(decimal ratePercent)
    {
        // Arrange
        CreateTenantTaxProfileCommand command = BuildValidCommand();
        command.RatePercent = ratePercent;

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    [InlineData(1000)]
    public void Validate_RatePercentOutsideInclusiveRange_IsInvalid(decimal ratePercent)
    {
        // Arrange
        CreateTenantTaxProfileCommand command = BuildValidCommand();
        command.RatePercent = ratePercent;

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        HasErrorFor(result, nameof(CreateTenantTaxProfileCommand.RatePercent)).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(-1)]
    public void Validate_RegimeOutsideDeclaredEnum_IsInvalid(int rawRegime)
    {
        // Arrange
        CreateTenantTaxProfileCommand command = BuildValidCommand();
        command.Regime = (TaxRegime)rawRegime;

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        HasErrorFor(result, nameof(CreateTenantTaxProfileCommand.Regime)).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void Validate_VatOutsideDeclaredEnum_IsInvalid(int rawVat)
    {
        // Arrange
        CreateTenantTaxProfileCommand command = BuildValidCommand();
        command.Vat = (VatType)rawVat;

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        HasErrorFor(result, nameof(CreateTenantTaxProfileCommand.Vat)).Should().BeTrue();
    }

    [Fact]
    public void Validate_DefaultCommand_IsInvalidBecauseRegimeAndVatWereNeverChosen()
    {
        // Arrange
        // A default-constructed command carries TaxRegime 0 and VatType 0, neither of
        // which is a declared member — this is exactly the "form submitted without
        // picking a regime" case.
        CreateTenantTaxProfileCommand command = new CreateTenantTaxProfileCommand
        {
            TenantId = Guid.NewGuid(),
        };

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        HasErrorFor(result, nameof(CreateTenantTaxProfileCommand.Regime)).Should().BeTrue();
        HasErrorFor(result, nameof(CreateTenantTaxProfileCommand.Vat)).Should().BeTrue();
    }

    [Fact]
    public void Validate_UsnIncome_IsTheOnlyAcceptedRegime()
    {
        // Arrange
        CreateTenantTaxProfileCommand command = BuildValidCommand();
        command.Regime = TaxRegime.UsnIncome;

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
        Enum.GetValues<TaxRegime>().Should().ContainSingle();
    }
}
