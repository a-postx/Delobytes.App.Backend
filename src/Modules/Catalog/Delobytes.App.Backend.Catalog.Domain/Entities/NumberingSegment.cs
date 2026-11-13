using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// One position in a <see cref="NumberingScheme"/>: either a literal string or a value taken from
/// a <see cref="NumberingSequence"/> counter, optionally zero-padded and followed by a separator.
/// </summary>
public class NumberingSegment : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid SchemeId { get; set; }

    /// <summary>1-based position in the composed identifier; unique per scheme.</summary>
    public int Order { get; set; }

    public NumberingSegmentType Type { get; set; }

    /// <summary>Required when <see cref="Type"/> is <see cref="NumberingSegmentType.Constant"/>.</summary>
    public string? ConstantValue { get; set; }

    /// <summary>Required when <see cref="Type"/> is <see cref="NumberingSegmentType.AutoNumber"/>.</summary>
    public Guid? NumberingSequenceId { get; set; }

    /// <summary>Zero-pad width of the auto number; null or zero leaves the number unpadded.</summary>
    public int? Length { get; set; }

    /// <summary>Appended after this segment, for example a hyphen.</summary>
    public string? SeparatorAfter { get; set; }

    public NumberingScheme Scheme { get; set; } = default!;

    public NumberingSequence? NumberingSequence { get; set; }
}
