using Delobytes.App.Backend.Identity.Application.Commands.UpdateTenantLegalEntity;
using Delobytes.App.Backend.Identity.Domain.Enums;
using FluentAssertions;
using FluentValidation.Results;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.LegalEntity;

/// <summary>
/// Tests for UpdateTenantLegalEntityCommandValidator (stage 10).
/// The validator is the contract that keeps a tenant from being saved with a tax
/// regime that the user never chose.
/// </summary>
public class UpdateTenantLegalEntityCommandValidatorTests
{
    private readonly UpdateTenantLegalEntityCommandValidator _validator = new UpdateTenantLegalEntityCommandValidator();

    private static UpdateTenantLegalEntityCommand BuildValidCommand() => new UpdateTenantLegalEntityCommand
    {
        TenantId = Guid.NewGuid(),
        LegalName = "ООО «Ромашка»",
        Inn = "7712345678",
        TaxType = TaxType.Usn,
        TaxRatePercent = 6m,
        VatType = VatType.None,
    };

    private static bool HasErrorFor(ValidationResult result, string propertyName) =>
        result.Errors.Exists(failure => failure.PropertyName == propertyName);

    [Fact]
    public void Validate_FullyPopulatedCommand_IsValid()
    {
        // Arrange
        UpdateTenantLegalEntityCommand command = BuildValidCommand();

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyTenantId_IsInvalid()
    {
        // Arrange
        UpdateTenantLegalEntityCommand command = BuildValidCommand();
        command.TenantId = Guid.Empty;

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        HasErrorFor(result, nameof(UpdateTenantLegalEntityCommand.TenantId)).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(99)]
    [InlineData(-1)]
    public void Validate_TaxTypeOutsideDeclaredEnum_IsInvalid(int rawTaxType)
    {
        // Arrange
        UpdateTenantLegalEntityCommand command = BuildValidCommand();
        command.TaxType = (TaxType)rawTaxType;

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        HasErrorFor(result, nameof(UpdateTenantLegalEntityCommand.TaxType)).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(99)]
    [InlineData(-1)]
    public void Validate_VatTypeOutsideDeclaredEnum_IsInvalid(int rawVatType)
    {
        // Arrange
        UpdateTenantLegalEntityCommand command = BuildValidCommand();
        command.VatType = (VatType)rawVatType;

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        HasErrorFor(result, nameof(UpdateTenantLegalEntityCommand.VatType)).Should().BeTrue();
    }

    [Fact]
    public void Validate_DefaultCommand_IsInvalidBecauseTaxRegimeWasNeverChosen()
    {
        // Arrange
        // A command that carries only the tenant id is exactly what a client would
        // send when the user submits the form without picking a tax regime.
        UpdateTenantLegalEntityCommand command = new UpdateTenantLegalEntityCommand
        {
            TenantId = Guid.NewGuid(),
        };

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        HasErrorFor(result, nameof(UpdateTenantLegalEntityCommand.TaxType)).Should().BeTrue();
        HasErrorFor(result, nameof(UpdateTenantLegalEntityCommand.VatType)).Should().BeTrue();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    [InlineData(-1)]
    [InlineData(1000)]
    public void Validate_TaxRateOutsideAllowedRange_IsInvalid(decimal taxRatePercent)
    {
        // Arrange
        UpdateTenantLegalEntityCommand command = BuildValidCommand();
        command.TaxRatePercent = taxRatePercent;

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        HasErrorFor(result, nameof(UpdateTenantLegalEntityCommand.TaxRatePercent)).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(15.5)]
    [InlineData(100)]
    public void Validate_TaxRateWithinAllowedRange_IsValid(decimal taxRatePercent)
    {
        // Arrange
        UpdateTenantLegalEntityCommand command = BuildValidCommand();
        command.TaxRatePercent = taxRatePercent;

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("7712345678")]
    [InlineData("771234567890")]
    public void Validate_InnWithinMaximumLength_IsValid(string inn)
    {
        // Arrange
        UpdateTenantLegalEntityCommand command = BuildValidCommand();
        command.Inn = inn;

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_InnLongerThanTwelveCharacters_IsInvalid()
    {
        // Arrange
        UpdateTenantLegalEntityCommand command = BuildValidCommand();
        command.Inn = new string('7', 13);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        HasErrorFor(result, nameof(UpdateTenantLegalEntityCommand.Inn)).Should().BeTrue();
    }

    [Fact]
    public void Validate_NullInnAndLegalName_IsValidBecauseTheyAreOptional()
    {
        // Arrange
        UpdateTenantLegalEntityCommand command = BuildValidCommand();
        command.Inn = null;
        command.LegalName = null;

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_LegalNameLongerThanFiveHundredCharacters_IsInvalid()
    {
        // Arrange
        UpdateTenantLegalEntityCommand command = BuildValidCommand();
        command.LegalName = new string('а', 501);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        HasErrorFor(result, nameof(UpdateTenantLegalEntityCommand.LegalName)).Should().BeTrue();
    }

    [Fact]
    public void Validate_LegalNameOfExactlyFiveHundredCharacters_IsValid()
    {
        // Arrange
        UpdateTenantLegalEntityCommand command = BuildValidCommand();
        command.LegalName = new string('а', 500);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}
