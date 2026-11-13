using System;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Tests.Application.Catalog.Numbering;

internal static class NumberingTestData
{
    public static NumberingSequence BuildSequence(
        int startNumber = 1,
        int? nextNumber = null,
        int step = 1,
        int? endNumber = null,
        bool warnOnClose = false)
    {
        return new NumberingSequence
        {
            Id = Guid.NewGuid(),
            Name = "Product code",
            StartNumber = startNumber,
            NextNumber = nextNumber ?? startNumber,
            Step = step,
            EndNumber = endNumber,
            WarnOnClose = warnOnClose,
            IsActive = true,
        };
    }

    public static NumberingSegment Constant(int order, string value)
    {
        return new NumberingSegment
        {
            Id = Guid.NewGuid(),
            Order = order,
            Type = NumberingSegmentType.Constant,
            ConstantValue = value,
        };
    }

    public static NumberingSegment AutoNumber(
        int order,
        NumberingSequence sequence,
        int? length,
        string? separatorAfter = null)
    {
        return new NumberingSegment
        {
            Id = Guid.NewGuid(),
            Order = order,
            Type = NumberingSegmentType.AutoNumber,
            NumberingSequenceId = sequence.Id,
            NumberingSequence = sequence,
            Length = length,
            SeparatorAfter = separatorAfter,
        };
    }

    public static NumberingScheme BuildScheme(
        string code = "PRODUCT",
        bool isDefault = true,
        bool isActive = true,
        params NumberingSegment[] segments)
    {
        NumberingScheme scheme = new NumberingScheme
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = "Product code",
            EntityKind = NumberingEntityKind.Product,
            IsDefault = isDefault,
            IsActive = isActive,
        };

        foreach (NumberingSegment segment in segments)
        {
            segment.SchemeId = scheme.Id;
            scheme.Segments.Add(segment);
        }

        return scheme;
    }
}
