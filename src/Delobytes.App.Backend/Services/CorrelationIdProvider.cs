using System.Text.RegularExpressions;

namespace Delobytes.App.Backend.Services;

/// <summary>
/// Generates and validates correlation identifiers.
/// The validator is deliberately strict: an incoming header value is attacker-controlled input
/// that ends up in logs, in support tickets and in the response, so only a bounded character set
/// of a bounded length is accepted. Anything else is replaced by a server-generated identifier.
/// </summary>
public static partial class CorrelationIdProvider
{
    /// <summary>
    /// Key under which the current correlation identifier is stored in <see cref="HttpContext.Items"/>.
    /// </summary>
    public const string HttpContextItemKey = "Delobytes.CorrelationId";

    private const int MaxLength = 64;
    private const int GeneratedLength = 32;

    /// <summary>
    /// Gets a value indicating whether the supplied value is safe to reuse as a correlation identifier.
    /// </summary>
    /// <param name="value">Candidate value from an incoming request or message header.</param>
    /// <returns>True when the value is well formed and within the allowed length.</returns>
    public static bool IsWellFormed(string? value)
    {
        return !string.IsNullOrEmpty(value)
            && value.Length <= MaxLength
            && CorrelationIdPattern().IsMatch(value);
    }

    /// <summary>
    /// Generates a new correlation identifier.
    /// </summary>
    /// <returns>A 32-character lowercase hexadecimal identifier without separators.</returns>
    public static string Create()
    {
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Returns the supplied value when it is well formed, otherwise a newly generated identifier.
    /// </summary>
    /// <param name="value">Candidate value from an incoming request or message header.</param>
    /// <returns>A usable correlation identifier.</returns>
    public static string Resolve(string? value)
    {
        return IsWellFormed(value) ? value! : Create();
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();
}
