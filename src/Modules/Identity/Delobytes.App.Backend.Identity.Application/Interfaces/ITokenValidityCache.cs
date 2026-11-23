namespace Delobytes.App.Backend.Identity.Application.Interfaces;

/// <summary>
/// Short-lived cache of <c>TokensValidAfter</c> values, so token validation does not
/// hit the database on every authenticated request.
/// </summary>
public interface ITokenValidityCache
{
    /// <summary>
    /// Tries to get the cached revocation instant for a user.
    /// </summary>
    bool TryGet(Guid userId, out DateTimeOffset? tokensValidAfter);

    /// <summary>
    /// Stores the revocation instant for a user for the cache lifetime.
    /// </summary>
    void Set(Guid userId, DateTimeOffset? tokensValidAfter);

    /// <summary>
    /// Drops the cached value, forcing the next lookup to read the database.
    /// </summary>
    void Invalidate(Guid userId);
}
