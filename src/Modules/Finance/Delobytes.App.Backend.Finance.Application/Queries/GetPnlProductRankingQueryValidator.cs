using FluentValidation;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>
/// Validates the parts of the ranking request that do not depend on the stored data: the order of
/// the range, the size of the slice and the optional revenue floor. The sort key, direction and
/// coverage are parsed by the controller, so an unknown value never reaches the query.
/// </summary>
public class GetPnlProductRankingQueryValidator : AbstractValidator<GetPnlProductRankingQuery>
{
    public GetPnlProductRankingQueryValidator()
    {
        RuleFor(query => query.Filter.To)
            .Must((query, to) => to!.Value >= query.Filter.From!.Value)
            .WithMessage("Дата окончания периода не может быть раньше даты начала.")
            .When(query => query.Filter.From.HasValue && query.Filter.To.HasValue);

        RuleFor(query => query.Take)
            .InclusiveBetween(1, GetPnlProductRankingQuery.MaxTake)
            .WithMessage($"Количество строк рейтинга должно быть от 1 до {GetPnlProductRankingQuery.MaxTake}.");

        RuleFor(query => query.MinRevenue)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Минимальная выручка не может быть отрицательной.")
            .When(query => query.MinRevenue.HasValue);
    }
}
