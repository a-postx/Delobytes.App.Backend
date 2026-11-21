using System;
using System.Threading.Tasks;
using Delobytes.App.Backend.Finance.Application.Queries;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// The summary request must be rejected before the query runs when its shape makes the answer
/// unbounded: a reversed range or a range that spans more periods than the granularity allows.
/// </summary>
public class GetPnlSummaryQueryValidatorTests
{
    private readonly GetPnlSummaryQueryValidator _validator = new();

    [Fact]
    public void ToBeforeFrom_IsRejected()
    {
        GetPnlSummaryQuery query = Query(
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 1, 1),
            PnlPeriodGranularity.Day);

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ReversedRange_IsRejectedEvenForATotalPeriod()
    {
        GetPnlSummaryQuery query = Query(
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 1, 1),
            PnlPeriodGranularity.Total);

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Fact]
    public void DayRangeOf366Days_IsAllowed()
    {
        GetPnlSummaryQuery query = Query(
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 1, 1),
            PnlPeriodGranularity.Day);

        _validator.Validate(query).IsValid.Should().BeTrue();
    }

    [Fact]
    public void DayRangeOf367Days_IsRejected()
    {
        GetPnlSummaryQuery query = Query(
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 1, 2),
            PnlPeriodGranularity.Day);

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Fact]
    public void WeekRangeOf366Weeks_IsAllowedBut367IsNot()
    {
        DateOnly from = new(2026, 1, 5);

        _validator.Validate(Query(from, from.AddDays(365 * 7), PnlPeriodGranularity.Week))
            .IsValid.Should().BeTrue();

        _validator.Validate(Query(from, from.AddDays(366 * 7), PnlPeriodGranularity.Week))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void MonthRangeOfThirtyYears_IsAllowed()
    {
        GetPnlSummaryQuery query = Query(
            new DateOnly(2000, 1, 1),
            new DateOnly(2029, 12, 1),
            PnlPeriodGranularity.Month);

        _validator.Validate(query).IsValid.Should().BeTrue();
    }

    [Fact]
    public void MonthRangeLongerThan366Months_IsRejected()
    {
        GetPnlSummaryQuery query = Query(
            new DateOnly(2000, 1, 1),
            new DateOnly(2030, 12, 1),
            PnlPeriodGranularity.Month);

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Fact]
    public void TotalGranularity_IsNotLimitedByTheRangeLength()
    {
        GetPnlSummaryQuery query = Query(
            new DateOnly(1900, 1, 1),
            new DateOnly(2100, 1, 1),
            PnlPeriodGranularity.Total);

        _validator.Validate(query).IsValid.Should().BeTrue();
    }

    [Fact]
    public void OpenRange_IsLeftToTheHandlerThatKnowsTheData()
    {
        // One edge comes from the stored lines, so the validator cannot measure the span.
        _validator.Validate(new GetPnlSummaryQuery
        {
            Filter = new PnlQueryFilter { From = new DateOnly(2015, 1, 1) },
            Granularity = PnlPeriodGranularity.Day,
        }).IsValid.Should().BeTrue();

        _validator.Validate(new GetPnlSummaryQuery
        {
            Filter = new PnlQueryFilter { To = new DateOnly(2026, 1, 1) },
            Granularity = PnlPeriodGranularity.Day,
        }).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task RejectedRange_ReportsTheEndDateAsTheViolatingField()
    {
        GetPnlSummaryQuery query = Query(
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 1, 2),
            PnlPeriodGranularity.Day);

        ValidationResult result = await _validator.ValidateAsync(query);

        result.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be($"{nameof(GetPnlSummaryQuery.Filter)}.{nameof(PnlQueryFilter.To)}");
    }

    private static GetPnlSummaryQuery Query(
        DateOnly from,
        DateOnly to,
        PnlPeriodGranularity granularity)
    {
        return new GetPnlSummaryQuery
        {
            Filter = new PnlQueryFilter { From = from, To = to },
            Granularity = granularity,
        };
    }
}
