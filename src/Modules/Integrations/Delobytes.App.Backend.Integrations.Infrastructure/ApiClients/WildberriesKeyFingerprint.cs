using System.Security.Cryptography;
using System.Text;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// Derives the stored identity of a Wildberries API key. The raw key never leaves the request
/// path: pacing state, log lines and diagnostics address a key only through this fingerprint.
/// </summary>
/// <remarks>
/// A single implementation is required, not merely convenient. <see cref="WildberriesApiClient"/>
/// acquires a pacing slot for the key it is about to send with, while
/// <see cref="Services.WildberriesRateLimiter"/> reports the headers of that same response; if the
/// two hashed differently they would read and write different throttle rows and the gate would
/// never see its own reports. The format is the full 64-character uppercase SHA-256 digest, which
/// is what existing rows already hold, so the fingerprint is not truncated here.
/// </remarks>
internal static class WildberriesKeyFingerprint
{
    /// <summary>
    /// Computes the fingerprint of <paramref name="apiKey"/>.
    /// </summary>
    /// <param name="apiKey">Raw API key as sent in the <c>Authorization</c> header.</param>
    /// <returns>Uppercase hexadecimal SHA-256 digest.</returns>
    public static string Compute(string apiKey)
    {
        ArgumentNullException.ThrowIfNull(apiKey);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));

        return Convert.ToHexString(hash);
    }
}
