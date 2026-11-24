using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Delobytes.App.Backend.Persistence.Conventions;

/// <summary>
/// Replaces the default random GUID generator on every single-column <see cref="Guid"/> primary key
/// with <see cref="ValueGeneration.SequentialGuidValueGenerator"/>. The key stays
/// <c>ValueGeneratedOnAdd</c>, so the schema and the migration snapshots are unaffected.
/// </summary>
public sealed class SequentialGuidKeyConvention : IKeyAddedConvention
{
    /// <inheritdoc/>
    public void ProcessKeyAdded(
        IConventionKeyBuilder keyBuilder,
        IConventionContext<IConventionKeyBuilder> context)
    {
        IConventionKey key = keyBuilder.Metadata;

        if (key.Properties.Count != 1 || key.Properties[0].ClrType != typeof(Guid))
        {
            return;
        }

        key.Properties[0].Builder.HasValueGenerator(typeof(ValueGeneration.SequentialGuidValueGenerator));
    }
}
