using Delobytes.App.Backend.Contracts.Identity;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Delobytes.App.Backend.Persistence.ValueGeneration;

/// <summary>
/// Generates UUIDv7 identifiers for <see cref="Guid"/> keys instead of the default random v4, so a
/// new row sorts after the rows already stored.
/// </summary>
public sealed class SequentialGuidValueGenerator : ValueGenerator<Guid>
{
    /// <inheritdoc/>
    public override bool GeneratesTemporaryValues => false;

    /// <inheritdoc/>
    public override Guid Next(EntityEntry entry)
    {
        return EntityId.New();
    }
}
