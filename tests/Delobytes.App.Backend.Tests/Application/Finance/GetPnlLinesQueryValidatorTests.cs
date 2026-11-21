using Delobytes.App.Backend.Finance.Application.Queries;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// The line query accepts one product or a capped list of products, never both: the two forms are
/// mutually exclusive and the list has to fit in the query string.
/// </summary>
public class GetPnlLinesQueryValidatorTests
{
    private readonly GetPnlLinesQueryValidator _validator = new();

    [Fact]
    public void ProductIdsAtTheCap_AreAllowed()
    {
        GetPnlLinesQuery query = new()
        {
            Filter = new PnlQueryFilter
            {
                ProductIds = Enumerable.Range(0, PnlQueryFilter.MaxProductIds)
                    .Select(_ => Guid.NewGuid())
                    .ToArray(),
            },
        };

        _validator.Validate(query).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ProductIdsOverTheCap_AreRejected()
    {
        GetPnlLinesQuery query = new()
        {
            Filter = new PnlQueryFilter
            {
                ProductIds = Enumerable.Range(0, PnlQueryFilter.MaxProductIds + 1)
                    .Select(_ => Guid.NewGuid())
                    .ToArray(),
            },
        };

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ProductIdAndProductIdsTogether_AreRejected()
    {
        GetPnlLinesQuery query = new()
        {
            Filter = new PnlQueryFilter
            {
                ProductId = Guid.NewGuid(),
                ProductIds = new[] { Guid.NewGuid() },
            },
        };

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Fact]
    public void NeitherForm_IsAllowed()
    {
        GetPnlLinesQuery query = new();

        _validator.Validate(query).IsValid.Should().BeTrue();
    }
}
