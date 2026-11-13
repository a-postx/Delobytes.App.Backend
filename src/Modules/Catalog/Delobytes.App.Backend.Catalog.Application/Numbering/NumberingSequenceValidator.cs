using Delobytes.App.Backend.Catalog.Domain.Entities;
using FluentValidation;

namespace Delobytes.App.Backend.Catalog.Application.Numbering;

public class NumberingSequenceValidator : AbstractValidator<NumberingSequence>
{
    public NumberingSequenceValidator()
    {
        RuleFor(sequence => sequence.Name)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(sequence => sequence.StartNumber)
            .GreaterThanOrEqualTo(0);

        RuleFor(sequence => sequence.Step)
            .GreaterThan(0);

        RuleFor(sequence => sequence.EndNumber)
            .GreaterThanOrEqualTo(sequence => sequence.StartNumber)
            .When(sequence => sequence.EndNumber.HasValue);
    }
}
