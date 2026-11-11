using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Identity.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Identity;

public class JwtTokenServiceTests
{
    private const string SecretKey = "integration-test-jwt-secret-key-0123456789abcdef";
    private const string Issuer = "https://delobytes.test";
    private const string Audience = "https://delobytes.test/frontend";

    private static readonly DateTimeOffset FixedNow = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private static IConfiguration BuildConfiguration(
        string? secretKey,
        string? issuer = null,
        string? audience = null,
        string? expirationMinutes = null)
    {
        Dictionary<string, string?> values = new Dictionary<string, string?>();

        if (secretKey != null)
        {
            values["AppSecrets:JwtSecretKey"] = secretKey;
        }

        if (issuer != null)
        {
            values["JwtSettings:Issuer"] = issuer;
        }

        if (audience != null)
        {
            values["JwtSettings:Audience"] = audience;
        }

        if (expirationMinutes != null)
        {
            values["JwtSettings:ExpirationMinutes"] = expirationMinutes;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static JwtTokenService BuildService(IConfiguration configuration)
    {
        return new JwtTokenService(configuration, new FixedTimeProvider(FixedNow));
    }

    private static JwtSecurityToken ReadToken(string token)
    {
        return new JwtSecurityTokenHandler().ReadJwtToken(token);
    }

    [Fact]
    public void GenerateToken_WithoutTenantOrRole_EmitsSubjectAndMetadataClaimsOnly()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        JwtTokenService service = BuildService(BuildConfiguration(SecretKey, Issuer, Audience, "60"));

        // Act
        string token = service.GenerateToken(userId, null, null);

        // Assert
        JwtSecurityToken jwt = ReadToken(token);

        jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value.Should().Be(userId.ToString());
        jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value.Should().NotBeNullOrWhiteSpace();
        jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Iat).Value
            .Should().Be(FixedNow.ToUnixTimeSeconds().ToString());
        jwt.Claims.Should().NotContain(c => c.Type == "tenantId");
        jwt.Claims.Should().NotContain(c => c.Type == "role");
    }

    [Fact]
    public void GenerateToken_WithTenantAndRole_EmitsTenantAndRoleClaims()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        JwtTokenService service = BuildService(BuildConfiguration(SecretKey, Issuer, Audience, "60"));

        // Act
        string token = service.GenerateToken(userId, tenantId, Role.Manager);

        // Assert
        JwtSecurityToken jwt = ReadToken(token);

        jwt.Claims.Single(c => c.Type == "tenantId").Value.Should().Be(tenantId.ToString());
        jwt.Claims.Single(c => c.Type == "role").Value.Should().Be("Manager");
    }

    [Fact]
    public void GenerateToken_UsesConfiguredIssuerAudienceAndExpiration()
    {
        // Arrange
        JwtTokenService service = BuildService(BuildConfiguration(SecretKey, Issuer, Audience, "45"));

        // Act
        string token = service.GenerateToken(Guid.NewGuid(), null, null);

        // Assert
        JwtSecurityToken jwt = ReadToken(token);

        jwt.Issuer.Should().Be(Issuer);
        jwt.Audiences.Should().ContainSingle().Which.Should().Be(Audience);
        jwt.ValidTo.Should().Be(FixedNow.UtcDateTime.AddMinutes(45));
    }

    [Fact]
    public void GenerateToken_WhenJwtSettingsMissing_AppliesDocumentedDefaults()
    {
        // Arrange
        JwtTokenService service = BuildService(BuildConfiguration(SecretKey));

        // Act
        string token = service.GenerateToken(Guid.NewGuid(), null, null);

        // Assert
        JwtSecurityToken jwt = ReadToken(token);

        jwt.Issuer.Should().Be("Delobytes.App.Backend");
        jwt.Audiences.Should().ContainSingle().Which.Should().Be("Delobytes.App.Frontend");
        jwt.ValidTo.Should().Be(FixedNow.UtcDateTime.AddMinutes(1440));
    }

    [Fact]
    public void GenerateToken_WhenSecretKeyMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        JwtTokenService service = BuildService(BuildConfiguration(secretKey: null, Issuer, Audience, "60"));

        // Act
        Action act = () => service.GenerateToken(Guid.NewGuid(), null, null);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("JwtSettings:SecretKey is not configured.");
    }

    [Fact]
    public async Task GenerateToken_ProducesTokenVerifiableWithConfiguredSecret()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        JwtTokenService service = BuildService(BuildConfiguration(SecretKey, Issuer, Audience, "60"));
        string token = service.GenerateToken(userId, Guid.NewGuid(), Role.Administrator);

        TokenValidationParameters validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateLifetime = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey)),
        };

        // Act
        TokenValidationResult result = await new JwtSecurityTokenHandler()
            .ValidateTokenAsync(token, validationParameters);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ClaimsIdentity.Claims.Select(c => c.Value).Should().Contain(userId.ToString());
    }
}
