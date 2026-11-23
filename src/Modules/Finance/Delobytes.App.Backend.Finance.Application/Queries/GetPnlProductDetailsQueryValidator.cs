using FluentValidation;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>
/// Validates the product sheet request: one real product identifier and an ordered range.
/// </summary>
public class GetPnlProductDetailsQueryValidator : AbstractValidator<GetPnlProductDetailsQuery>
{
    public GetPnlProductDetailsQueryValidator()
    {
        RuleFor(query => query.ProductId)
            .NotEqual(Guid.Empty)
            .WithMessage("Идентификатор товара обязателен.");

        RuleFor(query => query.Filter.To)
            .Must((query, to) => to!.Value >= query.Filter.From!.Value)
            .WithMessage("Дата окончания периода не может быть раньше даты начала.")
            .When(query => query.Filter.From.HasValue && query.Filter.To.HasValue);
    }
}
