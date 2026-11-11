using Delobytes.App.Backend.Identity.Application.Commands;
using FluentValidation.Results;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.Settings;

/// <summary>
/// Tests for UpdateTenantSettingsCommandValidator.
/// </summary>
public class UpdateTenantSettingsCommandValidatorTests
{
    private readonly UpdateTenantSettingsCommandValidator _validator = new();

    private static UpdateTenantSettingsCommand BuildCommand(string timeZone) => new UpdateTenantSettingsCommand
    {
        TenantId = Guid.NewGuid(),
        TimeZone = timeZone,
    };

    [Theory]
    [InlineData("Europe/Moscow")]
    [InlineData("Europe/Samara")]
    [InlineData("Asia/Vladivostok")]
    [InlineData("America/New_York")]
    public void Validate_KnownIanaZone_Passes(string timeZone)
    {
        // Arrange
        UpdateTenantSettingsCommand command = BuildCommand(timeZone);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_UnknownZone_FailsOnTimeZoneField()
    {
        // Arrange
        UpdateTenantSettingsCommand command = BuildCommand("Not/AZone");

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateTenantSettingsCommand.TimeZone));
    }

    [Fact]
    public void Validate_EmptyZone_FailsOnTimeZoneField()
    {
        // Arrange
        UpdateTenantSettingsCommand command = BuildCommand(string.Empty);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateTenantSettingsCommand.TimeZone));
    }

    [Fact]
    public void Validate_ZoneLongerThanColumn_FailsOnTimeZoneField()
    {
        // Arrange
        UpdateTenantSettingsCommand command = BuildCommand(new string('a', 65));

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateTenantSettingsCommand.TimeZone));
    }

    [Fact]
    public void Validate_EmptyTenantId_FailsOnTenantIdField()
    {
        // Arrange
        UpdateTenantSettingsCommand command = new UpdateTenantSettingsCommand
        {
            TenantId = Guid.Empty,
            TimeZone = "Europe/Samara",
        };

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateTenantSettingsCommand.TenantId));
    }
}
