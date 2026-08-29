namespace Delobytes.App.Backend.Identity.Application.Interfaces;

/// <summary>
/// Abstraction over password hashing — keeps Application layer free of BCrypt dependency.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Hashes a plain-text password.
    /// </summary>
    public string Hash(string password);

    /// <summary>
    /// Verifies a plain-text password against a stored hash.
    /// </summary>
    public bool Verify(string password, string hash);
}
