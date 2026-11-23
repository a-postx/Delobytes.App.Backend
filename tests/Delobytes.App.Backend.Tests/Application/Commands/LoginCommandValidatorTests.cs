using Delobytes.App.Backend.Identity.Application.Commands;
using FluentValidation.Results;

namespace Delobytes.App.Backend.Identity.Tests.Commands;

/// <summary>
/// Tests for LoginCommandValidator.
/// </summary>
public class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCredentials_Passes()
    {
        // Arrange
        LoginCommand command = new LoginCommand
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
    public void Validate_EmptyEmail_FailsOnEmailField()
    {
        // Arrange
        LoginCommand command = new LoginCommand
        {
            Email = string.Empty,
            Password = "password123",
        };

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(LoginCommand.Email));
    }

    [Fact]
    public void Validate_InvalidEmail_FailsOnEmailField()
    {
        // Arrange
        LoginCommand command = new LoginCommand
        {
            Email = "not-an-email",
            Password = "password123",
        };

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(LoginCommand.Email));
    }

    [Fact]
    public void Validate_EmailLongerThanColumn_FailsOnEmailField()
    {
        // Arrange
        LoginCommand command = new LoginCommand
        {
            Email = $"{new string('a', 250)}@example.com",
            Password = "password123",
        };

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(LoginCommand.Email));
    }

    [Fact]
    public void Validate_EmptyPassword_FailsOnPasswordField()
    {
        // Arrange
        LoginCommand command = new LoginCommand
        {
            Email = "user@example.com",
            Password = string.Empty,
        };

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(LoginCommand.Password));
    }
}
