using Delobytes.App.Backend.Catalog.Domain.Entities;
using FluentValidation;

namespace Delobytes.App.Backend.Catalog.Application.Numbering;

public class NumberingSchemeValidator : AbstractValidator<NumberingScheme>
{
    public NumberingSchemeValidator()
    {
        RuleFor(scheme => scheme.Code)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(scheme => scheme.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(scheme => scheme.EntityKind)
            .IsInEnum();

        RuleFor(scheme => scheme.Segments)
            .NotEmpty();

        RuleFor(scheme => scheme.Segments)
            .Must(HaveUniqueOrders)
            .WithMessage("Position numbers must be unique inside a numbering scheme.")
            .When(scheme => scheme.Segments.Count > 0);

        RuleForEach(scheme => scheme.Segments)
            .SetValidator(new NumberingSegmentValidator());
    }

    private static bool HaveUniqueOrders(ICollection<NumberingSegment> segments)
    {
        return segments.Select(segment => segment.Order).Distinct().Count() == segments.Count;
    }
}
