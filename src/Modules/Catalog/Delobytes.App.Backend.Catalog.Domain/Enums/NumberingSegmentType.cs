namespace Delobytes.App.Backend.Catalog.Domain.Enums;

/// <summary>
/// Defines what a numbering segment contributes to the composed identifier.
/// </summary>
public enum NumberingSegmentType
{
    /// <summary>
    /// A literal string, taken from <see cref="Entities.NumberingSegment.ConstantValue"/>.
    /// </summary>
    Constant = 1,

    /// <summary>
    /// A value taken from a <see cref="Entities.NumberingSequence"/> counter.
    /// </summary>
    AutoNumber = 2,
}
