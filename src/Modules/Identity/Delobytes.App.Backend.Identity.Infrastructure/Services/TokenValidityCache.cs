using System.Collections.Concurrent;
using Delobytes.App.Backend.Identity.Application.Interfaces;

namespace Delobytes.App.Backend.Identity.Infrastructure.Services;

/// <summary>
/// In-memory cache with a one-minute lifetime.
/// Revocation is immediate on the instance that changed the password; other instances
/// may keep accepting old tokens until their cached entry expires.
/// </summary>
internal sealed class TokenValidityCache : ITokenValidityCache
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private readonly TimeProvider _timeProvider;

    public TokenValidityCache(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public bool TryGet(Guid userId, out DateTimeOffset? tokensValidAfter)
    {
        if (_entries.TryGetValue(userId, out Entry entry))
        {
            if (entry.ExpiresAt > _timeProvider.GetUtcNow())
            {
                tokensValidAfter = entry.TokensValidAfter;
                return true;
            }

            _entries.TryRemove(userId, out _);
        }

        tokensValidAfter = null;
        return false;
    }

    /// <inheritdoc/>
    public void Set(Guid userId, DateTimeOffset? tokensValidAfter)
    {
        _entries[userId] = new Entry(tokensValidAfter, _timeProvider.GetUtcNow().Add(Lifetime));
    }

    /// <inheritdoc/>
    public void Invalidate(Guid userId)
    {
        _entries.TryRemove(userId, out _);
    }

    private readonly record struct Entry(DateTimeOffset? TokensValidAfter, DateTimeOffset ExpiresAt);
}
