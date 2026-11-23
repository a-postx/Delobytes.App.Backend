using Delobytes.App.Backend.Identity.Application.Commands;
using FluentValidation.Results;

namespace Delobytes.App.Backend.Identity.Tests.Commands;

/// <summary>
/// Tests for RegisterCommandValidator.
/// </summary>
public class RegisterCommandValidatorTests
{
    private readonly RegisterCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCredentials_Passes()
    {
        // Arrange
        RegisterCommand command = new RegisterCommand
        {
            Email = "user@example.com",
            Password = "password123",
        };

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyPassword_FailsOnPasswordField()
    {
        // Arrange
        RegisterCommand command = new RegisterCommand
        {
            Email = "user@example.com",
            Password = string.Empty,
        };

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(RegisterCommand.Password));
    }

    [Fact]
    public void Validate_InvalidEmail_FailsOnEmailField()
    {
        // Arrange
        RegisterCommand command = new RegisterCommand
        {
            Email = "not-an-email",
            Password = "password123",
        };

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(RegisterCommand.Email));
    }
}
