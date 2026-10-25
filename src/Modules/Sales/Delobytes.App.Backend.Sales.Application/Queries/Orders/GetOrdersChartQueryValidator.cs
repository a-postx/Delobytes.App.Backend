using FluentValidation;

namespace Delobytes.App.Backend.Sales.Application.Queries.Orders;

/// <summary>
/// Validates only the checks that are independent of unresolved defaults. Range length and the
/// "to before from" check against the resolved pair are enforced in the query handler, after
/// <see cref="GetOrdersChartQuery.From"/>/<see cref="GetOrdersChartQuery.To"/> defaults are applied.
/// </summary>
public class GetOrdersChartQueryValidator : AbstractValidator<GetOrdersChartQuery>
{
    public GetOrdersChartQueryValidator()
    {
        // The trailing When() guards this whole RuleFor chain, so Must() only runs once both
        // values are present - the null-forgiving operators below are safe.
        RuleFor(x => x.To)
            .Must((query, to) => to!.Value >= query.From!.Value)
            .WithMessage("Дата окончания периода не может быть раньше даты начала.")
            .When(x => x.From.HasValue && x.To.HasValue);
    }
}
