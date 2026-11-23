using System;
using Delobytes.App.Backend.Finance.Application.Queries;
using FluentAssertions;
using FluentValidation.Results;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>The product sheet needs one real product identifier and an ordered range.</summary>
public class GetPnlProductDetailsQueryValidatorTests
{
    private readonly GetPnlProductDetailsQueryValidator _validator = new();

    [Fact]
    public void EmptyProductId_IsRejected()
    {
        GetPnlProductDetailsQuery query = Query();
        query.ProductId = Guid.Empty;

        ValidationResult result = _validator.Validate(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("Идентификатор товара обязателен.");
    }

    [Fact]
    public void RealProductId_IsAllowed()
    {
        _validator.Validate(Query()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ToBeforeFrom_IsRejected()
    {
        GetPnlProductDetailsQuery query = Query();
        query.Filter.From = new DateOnly(2026, 2, 1);
        query.Filter.To = new DateOnly(2026, 1, 1);

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    private static GetPnlProductDetailsQuery Query()
    {
        return new GetPnlProductDetailsQuery
        {
            ProductId = Guid.NewGuid(),
            Filter = new PnlQueryFilter
            {
                From = new DateOnly(2026, 1, 1),
                To = new DateOnly(2026, 1, 31),
            },
        };
    }
}
