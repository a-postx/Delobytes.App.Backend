namespace Delobytes.App.Backend.Identity.Domain;

/// <summary>
/// Produces the canonical form used to compare email addresses.
/// </summary>
public static class EmailNormalizer
{
    /// <summary>
    /// Trims the address and lowercases it with the invariant culture.
    /// </summary>
    public static string Normalize(string? email)
    {
        return (email ?? string.Empty).Trim().ToLowerInvariant();
    }
}
