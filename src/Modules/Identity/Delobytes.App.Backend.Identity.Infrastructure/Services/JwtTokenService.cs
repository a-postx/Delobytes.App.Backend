using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Delobytes.App.Backend.Identity.Infrastructure.Services;

/// <summary>
/// Implementation of JWT token service.
/// </summary>
public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="JwtTokenService"/> class.
    /// </summary>
    /// <param name="configuration">Application configuration.</param>
    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <inheritdoc/>
    public string GenerateToken(Guid userId, Guid tenantId, Role role)
    {
        IConfigurationSection secrets = _configuration.GetSection("AppSecrets");
        string secretKey = secrets["JwtSecretKey"] ?? throw new InvalidOperationException("JwtSettings:SecretKey is not configured.");
        IConfigurationSection jwtSettings = _configuration.GetSection("JwtSettings");
        string issuer = jwtSettings["Issuer"] ?? "Delobytes.App.Backend";
        string audience = jwtSettings["Audience"] ?? "Delobytes.App.Frontend";
        int expirationMinutes = int.Parse(jwtSettings["ExpirationMinutes"] ?? "1440"); // Default: 24 hours

        SymmetricSecurityKey securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        SigningCredentials credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        Claim[] claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("userId", userId.ToString()),
            new Claim("tenantId", tenantId.ToString()),
            new Claim("role", role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };

        JwtSecurityToken token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <inheritdoc/>
    public string GenerateTokenWithoutTenant(Guid userId)
    {
        IConfigurationSection secrets = _configuration.GetSection("AppSecrets");
        string secretKey = secrets["JwtSecretKey"] ?? throw new InvalidOperationException("JwtSettings:SecretKey is not configured.");
        IConfigurationSection jwtSettings = _configuration.GetSection("JwtSettings");
        string issuer = jwtSettings["Issuer"] ?? "Delobytes.App.Backend";
        string audience = jwtSettings["Audience"] ?? "Delobytes.App.Frontend";
        int expirationMinutes = int.Parse(jwtSettings["ExpirationMinutes"] ?? "1440"); // Default: 24 hours

        SymmetricSecurityKey securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        SigningCredentials credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        Claim[] claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("userId", userId.ToString()),
            new Claim("needsTenantSetup", "true"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };

        JwtSecurityToken token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
