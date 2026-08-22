using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Options;
using Delobytes.App.Backend.Options.Validators;
using Delobytes.App.Backend.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// Unit tests for JWT token validation infrastructure.
/// All tests use a self-generated RSA key pair — no network calls to Auth0 are made.
/// </summary>
public sealed class JwtTokenValidationTests : IDisposable
{
    private const string TestIssuer = "https://test-tenant.auth0.com/";
    private const string TestAudience = "https://api.delobytes.io";
    private const string TestSubject = "auth0|user_abc123";

    private readonly RSA _rsa;

    // RsaSecurityKey holds both public and private parts of the key.
    // JwtSecurityTokenHandler uses the public part for verification automatically.
    private readonly RsaSecurityKey _rsaKey;
    private readonly SigningCredentials _signingCredentials;
    private readonly TokenValidationParameters _validationParameters;
    private readonly JwtTokenValidator _validator;
    private readonly JwtSecurityTokenHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="JwtTokenValidationTests"/> class.
    /// Generates a fresh RSA-2048 key pair for each test run.
    /// </summary>
    public JwtTokenValidationTests()
    {
        _rsa = RSA.Create(2048);
        _rsaKey = new RsaSecurityKey(_rsa);
        _signingCredentials = new SigningCredentials(_rsaKey, SecurityAlgorithms.RsaSha256);

        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = TestIssuer,
            ValidateAudience = true,
            ValidAudience = TestAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _rsaKey,
            ClockSkew = TimeSpan.Zero,
        };

        _validator = new JwtTokenValidator();
        _handler = new JwtSecurityTokenHandler();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _rsa.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // JwtTokenValidator.CanReadToken
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CanReadToken_WhenValidJwtString_ReturnsTrue()
    {
        // Arrange
        string token = CreateValidToken();

        // Act
        bool result = _validator.CanReadToken(token);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("only_one_segment")]
    [InlineData("two.segments")]
    [InlineData("four.dot.separated.parts")]
    public void CanReadToken_WhenInvalidInput_ReturnsFalse(string? input)
    {
        // Act
        bool result = _validator.CanReadToken(input);

        // Assert
        result.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // JwtTokenValidator.TryGetSubject
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryGetSubject_WhenTokenHasSubClaim_ReturnsSubject()
    {
        // Arrange
        string token = CreateValidToken(subject: TestSubject);

        // Act
        string? subject = _validator.TryGetSubject(token);

        // Assert
        subject.Should().Be(TestSubject);
    }

    [Fact]
    public void TryGetSubject_WhenTokenIsNull_ReturnsNull()
    {
        // Act
        string? subject = _validator.TryGetSubject(null);

        // Assert
        subject.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // JwtTokenValidator.ValidateToken — success scenarios
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateToken_WhenTokenIsValid_ReturnsPrincipalWithClaims()
    {
        // Arrange
        string token = CreateValidToken(subject: TestSubject);

        // Act
        ClaimsPrincipal principal = _validator.ValidateToken(token, _validationParameters);

        // Assert
        principal.Should().NotBeNull();
        principal.Identity!.IsAuthenticated.Should().BeTrue();

        // JwtSecurityTokenHandler maps "sub" to ClaimTypes.NameIdentifier by default.
        // We check both to be resilient to claim-type-map configurations.
        string? sub = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        sub.Should().Be(TestSubject);
    }

    [Fact]
    public void ValidateToken_WhenTokenContainsCustomClaims_ClaimsAreAccessible()
    {
        // Arrange
        List<Claim> extraClaims = new()
        {
            new Claim("tenant_id", "tenant-xyz"),
            new Claim("role", "Admin"),
        };

        string token = CreateValidToken(subject: TestSubject, additionalClaims: extraClaims);

        // Act
        ClaimsPrincipal principal = _validator.ValidateToken(token, _validationParameters);

        // Assert
        principal.FindFirst("tenant_id")?.Value.Should().Be("tenant-xyz");
        principal.FindFirst("role")?.Value.Should().Be("Admin");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // JwtTokenValidator.ValidateToken — failure scenarios
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateToken_WhenTokenIsExpired_ThrowsSecurityTokenExpiredException()
    {
        // Arrange
        string token = CreateValidToken(
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1));

        // Act
        Action act = () => _validator.ValidateToken(token, _validationParameters);

        // Assert
        act.Should().Throw<SecurityTokenExpiredException>();
    }

    [Fact]
    public void ValidateToken_WhenAudienceIsWrong_ThrowsSecurityTokenInvalidAudienceException()
    {
        // Arrange
        string token = CreateValidToken(audience: "https://wrong-audience.io");

        // Act
        Action act = () => _validator.ValidateToken(token, _validationParameters);

        // Assert
        act.Should().Throw<SecurityTokenInvalidAudienceException>();
    }

    [Fact]
    public void ValidateToken_WhenIssuerIsWrong_ThrowsSecurityTokenInvalidIssuerException()
    {
        // Arrange
        string token = CreateValidToken(issuer: "https://evil-tenant.auth0.com/");

        // Act
        Action act = () => _validator.ValidateToken(token, _validationParameters);

        // Assert
        act.Should().Throw<SecurityTokenInvalidIssuerException>();
    }

    [Fact]
    public void ValidateToken_WhenSignatureIsInvalid_ThrowsSecurityTokenSignatureKeyNotFoundException()
    {
        // Arrange — sign with a different (unknown) key
        using RSA otherRsa = RSA.Create(2048);
        SigningCredentials otherCredentials = new(
            new RsaSecurityKey(otherRsa),
            SecurityAlgorithms.RsaSha256);

        string token = CreateValidToken(credentials: otherCredentials);

        // Act
        Action act = () => _validator.ValidateToken(token, _validationParameters);

        // Assert
        act.Should().Throw<SecurityTokenSignatureKeyNotFoundException>();
    }

    [Fact]
    public void ValidateToken_WhenTokenIsNull_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => _validator.ValidateToken(null!, _validationParameters);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ValidateToken_WhenParametersAreNull_ThrowsArgumentNullException()
    {
        // Arrange
        string token = CreateValidToken();

        // Act
        Action act = () => _validator.ValidateToken(token, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Auth0Options validator
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Auth0OptionsValidator_WhenDomainAndAudienceSet_ReturnsSuccess()
    {
        // Arrange
        Auth0OptionsValidator validator = new();
        Auth0Options options = new()
        {
            Domain = "test.auth0.com",
            Audience = "https://api.test.io",
        };

        // Act
        ValidateOptionsResult result = validator.Validate(null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Auth0OptionsValidator_WhenDomainIsEmpty_ReturnsFailed()
    {
        // Arrange
        Auth0OptionsValidator validator = new();
        Auth0Options options = new()
        {
            Domain = string.Empty,
            Audience = "https://api.test.io",
        };

        // Act
        ValidateOptionsResult result = validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainMatch("*Domain*");
    }

    [Fact]
    public void Auth0OptionsValidator_WhenAudienceIsEmpty_ReturnsFailed()
    {
        // Arrange
        Auth0OptionsValidator validator = new();
        Auth0Options options = new()
        {
            Domain = "test.auth0.com",
            Audience = string.Empty,
        };

        // Act
        ValidateOptionsResult result = validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainMatch("*Audience*");
    }

    [Fact]
    public void Auth0Options_Authority_IsComposedCorrectly()
    {
        // Arrange
        Auth0Options options = new()
        {
            Domain = "my-tenant.auth0.com",
            Audience = "https://api.delobytes.io",
        };

        // Act / Assert
        options.Authority.Should().Be("https://my-tenant.auth0.com/");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private string CreateValidToken(
        string? subject = null,
        string? issuer = null,
        string? audience = null,
        DateTime? notBefore = null,
        DateTime? expires = null,
        SigningCredentials? credentials = null,
        IEnumerable<Claim>? additionalClaims = null)
    {
        List<Claim> claims = new()
        {
            new Claim(JwtRegisteredClaimNames.Sub, subject ?? TestSubject),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        if (additionalClaims != null)
        {
            claims.AddRange(additionalClaims);
        }

        JwtSecurityToken jwt = new(
            issuer: issuer ?? TestIssuer,
            audience: audience ?? TestAudience,
            claims: claims,
            notBefore: notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            expires: expires ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials ?? _signingCredentials);

        return _handler.WriteToken(jwt);
    }
}
