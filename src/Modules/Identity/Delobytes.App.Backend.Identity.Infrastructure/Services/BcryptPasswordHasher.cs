using Delobytes.App.Backend.Identity.Application.Interfaces;

namespace Delobytes.App.Backend.Identity.Infrastructure.Services;

/// <summary>
/// BCrypt-based implementation of IPasswordHasher.
/// </summary>
public class BcryptPasswordHasher : IPasswordHasher
{
    /// <inheritdoc/>
    public string Hash(string password)
        => BCrypt.Net.BCrypt.HashPassword(password);

    /// <inheritdoc/>
    public bool Verify(string password, string hash)
        => BCrypt.Net.BCrypt.Verify(password, hash);
}
