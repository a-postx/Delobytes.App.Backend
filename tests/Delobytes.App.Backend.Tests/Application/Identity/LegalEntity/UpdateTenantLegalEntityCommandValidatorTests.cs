using Delobytes.App.Backend.Identity.Application.Commands.UpdateTenantLegalEntity;
using FluentAssertions;
using FluentValidation.Results;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.LegalEntity;

/// <summary>
/// Tests for UpdateTenantLegalEntityCommandValidator.
///
/// The validator used to be the last line of defence against a tenant being saved with a
/// tax regime the user never chose. That responsibility moved to the tax profile, so the
/// tax rules are gone from here and only the legal entity text fields remain.
/// </summary>
public class UpdateTenantLegalEntityCommandValidatorTests
{
    private readonly UpdateTenantLegalEntityCommandValidator _validator = new UpdateTenantLegalEntityCommandValidator();

    private static UpdateTenantLegalEntityCommand BuildValidCommand() => new UpdateTenantLegalEntityCommand
    {
        TenantId = Guid.NewGuid(),
        LegalName = "ООО «Ромашка»",
        Inn = "7712345678",
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

    [Fact]
    public void Validate_CommandWithOnlyTenantId_IsValidBecauseTextFieldsAreOptional()
    {
        // Arrange
        // Without the tax fields there is nothing left that a client could omit and
        // still produce an invalid command, so this shape must now pass.
        UpdateTenantLegalEntityCommand command = new UpdateTenantLegalEntityCommand
        {
            TenantId = Guid.NewGuid(),
        };

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
