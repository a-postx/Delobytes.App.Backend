using FluentValidation;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>
/// Validates the parts of the summary request that do not depend on the stored data: the order of
/// the range and the number of periods it spans. An open range takes one of its edges from the data,
/// so its length is measured by the handler after the fold.
/// </summary>
public class GetPnlSummaryQueryValidator : AbstractValidator<GetPnlSummaryQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetPnlSummaryQueryValidator"/> class.
    /// </summary>
    public GetPnlSummaryQueryValidator()
    {
        // The trailing When() guards each chain, so Must() runs only with both dates present and
        // the null-forgiving operators below are safe.
        RuleFor(query => query.Filter.To)
            .Must((query, to) => to!.Value >= query.Filter.From!.Value)
            .WithMessage("Дата окончания периода не может быть раньше даты начала.")
            .When(query => query.Filter.From.HasValue && query.Filter.To.HasValue);

        // A total period folds the whole range into one row, so its length costs nothing.
        RuleFor(query => query.Filter.To)
            .Must((query, to) => PnlPeriodBuckets.Count(
                query.Filter.From!.Value,
                to!.Value,
                query.Granularity) <= PnlPeriodBuckets.MaxPeriods)
            .WithMessage(
                $"Диапазон не может превышать {PnlPeriodBuckets.MaxPeriods} периодов выбранной гранулярности.")
            .When(query => query.Filter.From.HasValue
                && query.Filter.To.HasValue
                && query.Granularity != PnlPeriodGranularity.Total);
    }
}
