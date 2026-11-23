using FluentValidation;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>
/// Validates the parts of the overview request that do not depend on the stored data: the order of
/// both ranges and the pairing of the comparison bounds. The overview folds the whole range into a
/// single period, so its length needs no limit.
/// </summary>
public class GetPnlOverviewQueryValidator : AbstractValidator<GetPnlOverviewQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetPnlOverviewQueryValidator"/> class.
    /// </summary>
    public GetPnlOverviewQueryValidator()
    {
        // The trailing When() guards each chain, so Must() runs only with both dates present and
        // the null-forgiving operator below is safe.
        RuleFor(query => query.Filter.To)
            .Must((query, to) => to!.Value >= query.Filter.From!.Value)
            .WithMessage("Дата окончания периода не может быть раньше даты начала.")
            .When(query => query.Filter.From.HasValue && query.Filter.To.HasValue);

        RuleFor(query => query)
            .Must(query => query.ComparisonFrom.HasValue == query.ComparisonTo.HasValue)
            .WithMessage("Укажите либо обе даты периода сравнения, либо ни одной.");

        RuleFor(query => query.ComparisonTo)
            .Must((query, to) => to!.Value >= query.ComparisonFrom!.Value)
            .WithMessage("Дата окончания периода сравнения не может быть раньше даты начала.")
            .When(query => query.ComparisonFrom.HasValue && query.ComparisonTo.HasValue);
    }
}
