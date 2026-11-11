using Delobytes.App.Backend.Identity.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Identity;

public class BcryptPasswordHasherTests
{
    private const string Password = "Secret123!";

    private readonly BcryptPasswordHasher _hasher = new BcryptPasswordHasher();

    [Fact]
    public void Hash_ReturnsBcryptHashInsteadOfPlainPassword()
    {
        // Act
        string hash = _hasher.Hash(Password);

        // Assert
        hash.Should().NotBeNullOrWhiteSpace();
        hash.Should().NotBe(Password);
        hash.Should().StartWith("$2");
    }

    [Fact]
    public void Verify_WhenPasswordMatchesHash_ReturnsTrue()
    {
        // Arrange
        string hash = _hasher.Hash(Password);

        // Act
        bool result = _hasher.Verify(Password, hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_WhenPasswordDoesNotMatchHash_ReturnsFalse()
    {
        // Arrange
        string hash = _hasher.Hash(Password);

        // Act
        bool result = _hasher.Verify("AnotherSecret123!", hash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Hash_WhenCalledTwice_ProducesDifferentSalts()
    {
        // Act
        string firstHash = _hasher.Hash(Password);
        string secondHash = _hasher.Hash(Password);

        // Assert
        firstHash.Should().NotBe(secondHash);
        _hasher.Verify(Password, firstHash).Should().BeTrue();
        _hasher.Verify(Password, secondHash).Should().BeTrue();
    }
}
