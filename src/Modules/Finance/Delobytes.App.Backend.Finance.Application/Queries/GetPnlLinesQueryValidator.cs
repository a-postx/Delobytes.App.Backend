using FluentValidation;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>
/// Validates the product selection of the line query. The list is capped so the identifiers fit in
/// the query string, and the two forms are mutually exclusive so a request never leaves it to the
/// server to guess whether it meant "this product" or "these products".
/// </summary>
public class GetPnlLinesQueryValidator : AbstractValidator<GetPnlLinesQuery>
{
    public GetPnlLinesQueryValidator()
    {
        RuleFor(query => query.Filter.ProductIds)
            .Must(ids => ids == null || ids.Length <= PnlQueryFilter.MaxProductIds)
            .WithMessage($"За один запрос можно запросить не более {PnlQueryFilter.MaxProductIds} товаров.");

        RuleFor(query => query.Filter)
            .Must(filter => !(filter.ProductId.HasValue && filter.ProductIds is { Length: > 0 }))
            .WithMessage("Укажите либо productId, либо productIds, но не оба параметра.");
    }
}
