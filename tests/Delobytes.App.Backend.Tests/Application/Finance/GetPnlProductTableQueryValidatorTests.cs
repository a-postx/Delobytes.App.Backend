using System;
using Delobytes.App.Backend.Finance.Application.Queries;
using FluentAssertions;
using FluentValidation.Results;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// The product table request must be rejected before the fold when its shape would make the
/// answer unbounded or meaningless.
/// </summary>
public class GetPnlProductTableQueryValidatorTests
{
    private readonly GetPnlProductTableQueryValidator _validator = new();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PageBelowOne_IsRejected(int page)
    {
        GetPnlProductTableQuery query = Query();
        query.Page = page;

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Fact]
    public void PageOne_IsAllowed()
    {
        _validator.Validate(Query()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void PageSizeOutOfRange_IsRejected(int pageSize)
    {
        GetPnlProductTableQuery query = Query();
        query.PageSize = pageSize;

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Fact]
    public void PageSizeAtBothBounds_IsAllowed()
    {
        GetPnlProductTableQuery smallest = Query();
        smallest.PageSize = 1;

        GetPnlProductTableQuery largest = Query();
        largest.PageSize = GetPnlProductTableQuery.MaxPageSize;

        _validator.Validate(smallest).IsValid.Should().BeTrue();
        _validator.Validate(largest).IsValid.Should().BeTrue();
    }

    [Fact]
    public void SearchAtTheMaximumLength_IsAllowed()
    {
        GetPnlProductTableQuery query = Query();
        query.Search = new string('a', GetPnlProductTableQuery.MaxSearchLength);

        _validator.Validate(query).IsValid.Should().BeTrue();
    }

    [Fact]
    public void SearchOverTheMaximumLength_IsRejected()
    {
        GetPnlProductTableQuery query = Query();
        query.Search = new string('a', GetPnlProductTableQuery.MaxSearchLength + 1);

        ValidationResult result = _validator.Validate(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.ErrorMessage.Contains("200", StringComparison.Ordinal));
    }

    [Fact]
    public void ToBeforeFrom_IsRejected()
    {
        GetPnlProductTableQuery query = Query();
        query.Filter.From = new DateOnly(2026, 2, 1);
        query.Filter.To = new DateOnly(2026, 1, 1);

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    private static GetPnlProductTableQuery Query()
    {
        return new GetPnlProductTableQuery
        {
            Filter = new PnlQueryFilter
            {
                From = new DateOnly(2026, 1, 1),
                To = new DateOnly(2026, 1, 31),
            },
        };
    }
}
