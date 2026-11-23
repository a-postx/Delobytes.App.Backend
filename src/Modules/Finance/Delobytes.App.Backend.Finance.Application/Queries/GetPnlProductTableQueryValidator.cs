using FluentValidation;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>
/// Validates the parts of the product table request that do not depend on the stored data. The
/// segment and the sort key are parsed by the controller, so an unknown value never reaches it.
/// </summary>
public class GetPnlProductTableQueryValidator : AbstractValidator<GetPnlProductTableQuery>
{
    public GetPnlProductTableQueryValidator()
    {
        RuleFor(query => query.Filter.To)
            .Must((query, to) => to!.Value >= query.Filter.From!.Value)
            .WithMessage("Дата окончания периода не может быть раньше даты начала.")
            .When(query => query.Filter.From.HasValue && query.Filter.To.HasValue);

        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Номер страницы должен быть не меньше 1.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, GetPnlProductTableQuery.MaxPageSize)
            .WithMessage($"Размер страницы должен быть от 1 до {GetPnlProductTableQuery.MaxPageSize}.");

        RuleFor(query => query.Search)
            .MaximumLength(GetPnlProductTableQuery.MaxSearchLength)
            .WithMessage($"Строка поиска не может быть длиннее {GetPnlProductTableQuery.MaxSearchLength} символов.")
            .When(query => query.Search != null);
    }
}
