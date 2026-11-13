using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentValidation;

namespace Delobytes.App.Backend.Catalog.Application.Numbering;

public class NumberingSegmentValidator : AbstractValidator<NumberingSegment>
{
    public NumberingSegmentValidator()
    {
        RuleFor(segment => segment.Order)
            .GreaterThan(0);

        RuleFor(segment => segment.Type)
            .IsInEnum();

        RuleFor(segment => segment.ConstantValue)
            .NotEmpty()
            .MaximumLength(50)
            .When(segment => segment.Type == NumberingSegmentType.Constant);

        RuleFor(segment => segment.ConstantValue)
            .MaximumLength(50)
            .When(segment => segment.Type != NumberingSegmentType.Constant);

        RuleFor(segment => segment.NumberingSequenceId)
            .NotNull()
            .When(segment => segment.Type == NumberingSegmentType.AutoNumber);

        RuleFor(segment => segment.Length)
            .GreaterThan(0)
            .When(segment => segment.Length.HasValue);

        RuleFor(segment => segment.SeparatorAfter)
            .MaximumLength(5);

        RuleFor(segment => segment.NumberingSequence)
            .SetValidator(new NumberingSequenceValidator())
            .When(segment => segment.NumberingSequence is not null);
    }
}
