namespace Delobytes.App.Backend.Contracts.Identity;

/// <summary>
/// Single source of entity identifiers.
/// </summary>
/// <remarks>
/// UUIDv7 stores the 48-bit Unix millisecond timestamp in octets 0..5, written big-endian, so the
/// RFC 9562 byte order is also chronological order. PostgreSQL compares <c>uuid</c> bytewise, and
/// Npgsql writes <see cref="Guid"/> in that same RFC order, so sequential inserts land next to each
/// other in the primary-key btree instead of scattering over the whole index.
/// </remarks>
public static class EntityId
{
    /// <summary>
    /// Creates a new UUIDv7 identifier.
    /// </summary>
    /// <returns>The identifier with a big-endian millisecond timestamp in its high 48 bits.</returns>
    public static Guid New()
    {
        return Guid.CreateVersion7();
    }
}
