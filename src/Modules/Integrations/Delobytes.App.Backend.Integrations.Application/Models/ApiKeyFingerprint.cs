using System.Security.Cryptography;
using System.Text;

namespace Delobytes.App.Backend.Integrations.Application.Models;

/// <summary>
/// Derives the stored identity of a channel API key. The raw key never leaves the request path:
/// pacing state, log lines and diagnostics address a key only through this fingerprint.
/// </summary>
/// <remarks>
/// A single implementation is required, not merely convenient. A channel client acquires a pacing
/// slot for the key it is about to send with, while its rate limiter reports the headers of that
/// same response; if the two hashed differently they would read and write different throttle rows
/// and the gate would never see its own reports. The format is the full 64-character uppercase
/// SHA-256 digest, which is what existing rows already hold, so the fingerprint is not truncated.
/// </remarks>
public static class ApiKeyFingerprint
{
    public static string Compute(string apiKey)
    {
        ArgumentNullException.ThrowIfNull(apiKey);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));

        return Convert.ToHexString(hash);
    }
}
