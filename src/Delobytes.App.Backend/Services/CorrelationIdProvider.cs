namespace Delobytes.App.Backend.Services;

/// <summary>
/// Generates correlation identifiers and resolves them from untyped sources (HTTP context values,
/// message headers). A client-supplied value is never trusted as a chain identifier — the server
/// that starts the chain (the HTTP edge or a background service) always generates its own GUID, so
/// the identifier is guaranteed valid everywhere downstream without a separate format check.
/// </summary>
public static class CorrelationIdProvider
{
    /// <summary>
    /// Key under which the current correlation identifier is stored in <see cref="HttpContext.Items"/>.
    /// </summary>
    public const string HttpContextItemKey = "Delobytes.CorrelationId";

    /// <summary>
    /// Generates a new correlation identifier.
    /// </summary>
    /// <returns>A newly generated correlation identifier.</returns>
    public static Guid Create()
    {
        return Guid.NewGuid();
    }

    /// <summary>
    /// Returns the supplied value when present, otherwise a newly generated identifier.
    /// Used to fall back to a fresh id when the ambient context (HTTP request or consumed message)
    /// does not carry one yet — e.g. the very first publish of a chain started by a background service.
    /// </summary>
    /// <param name="value">Candidate value from the ambient correlation context.</param>
    /// <returns>A usable correlation identifier.</returns>
    public static Guid Resolve(Guid? value)
    {
        return value.HasValue && value.Value != Guid.Empty ? value.Value : Create();
    }

    /// <summary>
    /// Parses a message header value into a correlation identifier, falling back to a newly
    /// generated one when the header is missing or not a well-formed GUID.
    /// </summary>
    /// <param name="value">Candidate value from an incoming message header.</param>
    /// <returns>A usable correlation identifier.</returns>
    public static Guid Resolve(string? value)
    {
        return Guid.TryParse(value, out Guid parsed) && parsed != Guid.Empty ? parsed : Create();
    }
}
