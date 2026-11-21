using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries.Dtos;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// Tests for the Wildberries finance mapper. The fixtures are live rows: the field names, the money
/// formats and the sign of the commission composite are all taken from a real report.
/// </summary>
public class WildberriesFinanceMapperTests
{
    private const string FixtureA = """
    [
      {
        "reportId": 799692300,
        "dateFrom": "2026-07-27",
        "dateTo": "2026-08-02",
        "createDate": "2026-08-03",
        "currency": "RUB",
        "rrdId": 3131711029842,
        "nmId": 174371959,
        "srid": "eBl.rbcae8cd5eb1f4ed7848aed79801a9d95.0.0",
        "sellerOperName": "Логистика",
        "saleDt": "2026-08-01T03:08:01Z",
        "rrDate": "2026-08-01",
        "retailAmount": "0",
        "retailPriceWithDisc": "0",
        "acquiringFee": "0",
        "vw": "0",
        "vwNds": "0",
        "ppvzReward": "0",
        "forPay": "0",
        "deliveryService": "53.84"
      },
      {
        "reportId": 799692300,
        "dateFrom": "2026-07-27",
        "dateTo": "2026-08-02",
        "createDate": "2026-08-03",
        "currency": "RUB",
        "rrdId": 3131711029843,
        "nmId": 174371959,
        "srid": "eBl.rbcae8cd5eb1f4ed7848aed79801a9d95.0.0",
        "sellerOperName": "Продажа",
        "saleDt": "2026-08-01T03:08:01Z",
        "rrDate": "2026-08-01",
        "retailAmount": "204",
        "retailPriceWithDisc": "340",
        "commissionPercent": 34.5,
        "acquiringFee": "8.16",
        "vw": "-21.0991803278688525",
        "vwNds": "-4.64",
        "ppvzReward": "7.041",
        "forPay": "214.54",
        "deliveryService": "0"
      }
    ]
    """;

    [Fact]
    public void Map_FixtureA_ProducesTheExpectedEntries()
    {
        WildberriesFinanceMappingResult result = Map(FixtureA);

        result.Diagnostics.SkippedOperations.Should().Be(0);
        result.Diagnostics.RowsWithoutSrid.Should().Be(0);
        result.Diagnostics.RowsWithoutRrDate.Should().Be(0);
        result.Diagnostics.Reversals.Should().Be(0);
        result.Diagnostics.SplitFallbacks.Should().Be(0);

        result.Entries.Should().HaveCount(7);

        result.Entries.Should().OnlyContain(e => e.Source == FinanceEntrySource.WbReport);
        result.Entries.Should().OnlyContain(e => e.ReportInstanceKey == "799692300:2026-08-03");
        result.Entries.Should().OnlyContain(e => e.ExternalLineId == "eBl.rbcae8cd5eb1f4ed7848aed79801a9d95.0.0");
        result.Entries.Should().OnlyContain(e => e.ExternalProductId == "174371959");
        result.Entries.Should().OnlyContain(e => e.Currency == "RUB");
        result.Entries.Should().OnlyContain(e => e.DocumentDate == new DateOnly(2026, 8, 1));
        result.Entries.Should().OnlyContain(e => e.PeriodFrom == new DateOnly(2026, 7, 27));
        result.Entries.Should().OnlyContain(e => e.PeriodTo == new DateOnly(2026, 8, 2));

        AssertEntry(result, "3131711029842", FinanceEntryKind.Component, FinanceEntryType.Logistics, 53.84m, FinanceEntryDirection.Expense);
        AssertEntry(result, "3131711029842", FinanceEntryKind.ControlTotal, FinanceEntryType.Payout, 53.84m, FinanceEntryDirection.Expense);

        // The buyer paid 204 of the seller's price 340: Wildberries financed the 40 % difference,
        // and the commission is 34.5 % of 340. 204 + 136 - 117.3 - 8.16 = 214.54, which is forPay.
        AssertEntry(result, "3131711029843", FinanceEntryKind.Component, FinanceEntryType.Sale, 204m, FinanceEntryDirection.Income);
        AssertEntry(result, "3131711029843", FinanceEntryKind.Component, FinanceEntryType.Compensation, 136m, FinanceEntryDirection.Income);
        AssertEntry(result, "3131711029843", FinanceEntryKind.Component, FinanceEntryType.Commission, 117.3m, FinanceEntryDirection.Expense);
        AssertEntry(result, "3131711029843", FinanceEntryKind.Component, FinanceEntryType.Acquiring, 8.16m, FinanceEntryDirection.Expense);
        AssertEntry(result, "3131711029843", FinanceEntryKind.ControlTotal, FinanceEntryType.Payout, 214.54m, FinanceEntryDirection.Income);

        decimal netRevenue = result.Entries
            .Where(e => e.Kind == FinanceEntryKind.Component)
            .Sum(e => e.Direction == FinanceEntryDirection.Income ? e.Amount : -e.Amount);

        decimal controlTotal = result.Entries
            .Where(e => e.Kind == FinanceEntryKind.ControlTotal)
            .Sum(e => e.Direction == FinanceEntryDirection.Income ? e.Amount : -e.Amount);

        netRevenue.Should().Be(160.70m);
        controlTotal.Should().Be(160.70m);
        Math.Abs(netRevenue - controlTotal).Should().BeLessThan(0.01m);
    }

    [Fact]
    public void Fields_RequestTheSellerPriceAndTheCommissionRate()
    {
        WildberriesFinanceMapper.Fields.Should().Contain("retailPriceWithDisc");
        WildberriesFinanceMapper.Fields.Should().Contain("commissionPercent");
    }

    [Fact]
    public void Map_SaleWithoutCommissionRate_KeepsTheReportedComposite()
    {
        string json = """
        [
          {
            "reportId": 1, "createDate": "2026-08-03", "dateFrom": "2026-07-27", "dateTo": "2026-08-02",
            "currency": "RUB", "rrdId": 12, "nmId": 5, "srid": "srid-1", "sellerOperName": "Продажа",
            "rrDate": "2026-08-01", "retailAmount": "204", "retailPriceWithDisc": "340",
            "acquiringFee": "8.16", "vw": "-21.0991803278688525", "vwNds": "-4.64", "ppvzReward": "7.041",
            "forPay": "214.54", "deliveryService": "0"
          }
        ]
        """;

        WildberriesFinanceMappingResult result = Map(json);

        result.Diagnostics.SplitFallbacks.Should().Be(1);
        AssertEntry(result, "12", FinanceEntryKind.Component, FinanceEntryType.Sale, 204m, FinanceEntryDirection.Income);
        AssertEntry(result, "12", FinanceEntryKind.Component, FinanceEntryType.Commission, 18.6981803278688525m, FinanceEntryDirection.Income);
        result.Entries.Should().NotContain(e => e.Type == FinanceEntryType.Compensation);
    }

    [Fact]
    public void Map_SplitThatDoesNotReproduceThePayout_IsRejectedAndCounted()
    {
        string json = """
        [
          {
            "reportId": 1, "createDate": "2026-08-03", "dateFrom": "2026-07-27", "dateTo": "2026-08-02",
            "currency": "RUB", "rrdId": 13, "nmId": 5, "srid": "srid-1", "sellerOperName": "Продажа",
            "rrDate": "2026-08-01", "retailAmount": "204", "retailPriceWithDisc": "340",
            "commissionPercent": 34.5, "acquiringFee": "8.16", "vw": "-21.0991803278688525",
            "vwNds": "-4.64", "ppvzReward": "7.041", "forPay": "100", "deliveryService": "0"
          }
        ]
        """;

        WildberriesFinanceMappingResult result = Map(json);

        result.Diagnostics.SplitFallbacks.Should().Be(1);
        AssertEntry(result, "13", FinanceEntryKind.Component, FinanceEntryType.Commission, 18.6981803278688525m, FinanceEntryDirection.Income);
        result.Entries.Should().NotContain(e => e.Type == FinanceEntryType.Compensation);
    }

    [Fact]
    public void Map_NegativeSaleWithSellerPrice_ReversesEveryComponent()
    {
        string json = """
        [
          {
            "reportId": 1, "createDate": "2026-08-03", "dateFrom": "2026-07-27", "dateTo": "2026-08-02",
            "currency": "RUB", "rrdId": 14, "nmId": 5, "srid": "srid-1", "sellerOperName": "Продажа",
            "rrDate": "2026-08-01", "retailAmount": "-204", "retailPriceWithDisc": "-340",
            "commissionPercent": 34.5, "acquiringFee": "8.16", "vw": "-21.0991803278688525",
            "vwNds": "-4.64", "ppvzReward": "7.041", "forPay": "-214.54", "deliveryService": "0"
          }
        ]
        """;

        WildberriesFinanceMappingResult result = Map(json);

        result.Diagnostics.Reversals.Should().Be(1);
        result.Diagnostics.SplitFallbacks.Should().Be(0);

        AssertEntry(result, "14", FinanceEntryKind.Component, FinanceEntryType.Sale, 204m, FinanceEntryDirection.Expense);
        AssertEntry(result, "14", FinanceEntryKind.Component, FinanceEntryType.Compensation, 136m, FinanceEntryDirection.Expense);
        AssertEntry(result, "14", FinanceEntryKind.Component, FinanceEntryType.Commission, 117.3m, FinanceEntryDirection.Income);
        AssertEntry(result, "14", FinanceEntryKind.Component, FinanceEntryType.Acquiring, 8.16m, FinanceEntryDirection.Income);
        AssertEntry(result, "14", FinanceEntryKind.ControlTotal, FinanceEntryType.Payout, 214.54m, FinanceEntryDirection.Expense);

        decimal netRevenue = result.Entries
            .Where(e => e.Kind == FinanceEntryKind.Component)
            .Sum(e => e.Direction == FinanceEntryDirection.Income ? e.Amount : -e.Amount);

        netRevenue.Should().Be(-214.54m);
    }

    [Fact]
    public void Map_ReturnRow_MirrorsTheSaleItReverses()
    {
        string json = """
        [
          {
            "reportId": 806538296, "dateFrom": "2026-08-03", "dateTo": "2026-08-09",
            "createDate": "2026-08-10", "currency": "RUB", "rrdId": 3132248693513,
            "nmId": 1119407553, "srid": "eBC.ra85fcc0ac12b4a539a118c6706c410f7.0.0",
            "sellerOperName": "Возврат", "saleDt": "2026-08-08T14:58:52Z", "rrDate": "2026-08-08",
            "retailAmount": "481", "retailPriceWithDisc": "602", "commissionPercent": 38.5,
            "acquiringFee": "19.24", "vw": "80.8729508196721311", "vwNds": "17.79",
            "ppvzReward": "12.105", "forPay": "350.99", "deliveryService": "0"
          }
        ]
        """;

        WildberriesFinanceMappingResult result = Map(json);

        result.Diagnostics.SkippedOperations.Should().Be(0);
        result.Diagnostics.Returns.Should().Be(1);
        result.Diagnostics.Reversals.Should().Be(1);
        result.Diagnostics.SplitFallbacks.Should().Be(0);

        // The buyer paid 481 of the seller's price 602 and Wildberries financed the 121 difference;
        // the commission is 38.5 % of 602. The return takes that payout back: 481 + 121 - 231.77 -
        // 19.24 = 350.99, the same figure the row reports as forPay, so every component flips.
        AssertEntry(result, "3132248693513", FinanceEntryKind.Component, FinanceEntryType.Sale, 481m, FinanceEntryDirection.Expense);
        AssertEntry(result, "3132248693513", FinanceEntryKind.Component, FinanceEntryType.Compensation, 121m, FinanceEntryDirection.Expense);
        AssertEntry(result, "3132248693513", FinanceEntryKind.Component, FinanceEntryType.Commission, 231.77m, FinanceEntryDirection.Income);
        AssertEntry(result, "3132248693513", FinanceEntryKind.Component, FinanceEntryType.Acquiring, 19.24m, FinanceEntryDirection.Income);
        AssertEntry(result, "3132248693513", FinanceEntryKind.ControlTotal, FinanceEntryType.Payout, 350.99m, FinanceEntryDirection.Expense);

        decimal netRevenue = result.Entries
            .Where(e => e.Kind == FinanceEntryKind.Component)
            .Sum(e => e.Direction == FinanceEntryDirection.Income ? e.Amount : -e.Amount);

        decimal controlTotal = result.Entries
            .Where(e => e.Kind == FinanceEntryKind.ControlTotal)
            .Sum(e => e.Direction == FinanceEntryDirection.Income ? e.Amount : -e.Amount);

        netRevenue.Should().Be(-350.99m);
        controlTotal.Should().Be(-350.99m);
    }

    [Fact]
    public void Map_ReturnWithoutCommissionRate_KeepsTheReportedCompositeAndFlipsItsDirection()
    {
        string json = """
        [
          {
            "reportId": 1, "createDate": "2026-08-10", "dateFrom": "2026-08-03", "dateTo": "2026-08-09",
            "currency": "RUB", "rrdId": 15, "nmId": 5, "srid": "srid-1", "sellerOperName": "Возврат",
            "rrDate": "2026-08-08", "retailAmount": "204", "retailPriceWithDisc": "340",
            "acquiringFee": "8.16", "vw": "21.0991803278688525", "vwNds": "4.64",
            "ppvzReward": "-7.041", "forPay": "214.54", "deliveryService": "0"
          }
        ]
        """;

        WildberriesFinanceMappingResult result = Map(json);

        result.Diagnostics.Returns.Should().Be(1);
        result.Diagnostics.SplitFallbacks.Should().Be(1);

        AssertEntry(result, "15", FinanceEntryKind.Component, FinanceEntryType.Sale, 204m, FinanceEntryDirection.Expense);
        AssertEntry(result, "15", FinanceEntryKind.Component, FinanceEntryType.Acquiring, 8.16m, FinanceEntryDirection.Income);
        AssertEntry(result, "15", FinanceEntryKind.Component, FinanceEntryType.Commission, 18.6981803278688525m, FinanceEntryDirection.Income);
        AssertEntry(result, "15", FinanceEntryKind.ControlTotal, FinanceEntryType.Payout, 214.54m, FinanceEntryDirection.Expense);
        result.Entries.Should().NotContain(e => e.Type == FinanceEntryType.Compensation);
    }

    [Fact]
    public void Map_RrDateWinsEvenWhenSaleDateFallsOnAnotherCalendarDay()
    {
        string json = """
        [
          {
            "reportId": 1,
            "dateFrom": "2026-07-27",
            "dateTo": "2026-08-02",
            "createDate": "2026-08-03",
            "currency": "RUB",
            "rrdId": 10,
            "nmId": 5,
            "srid": "srid-1",
            "sellerOperName": "Продажа",
            "saleDt": "2026-07-31T22:30:00Z",
            "rrDate": "2026-08-01",
            "retailAmount": "100",
            "acquiringFee": "0",
            "vw": "0",
            "vwNds": "0",
            "ppvzReward": "0",
            "forPay": "100",
            "deliveryService": "0"
          }
        ]
        """;

        WildberriesFinanceMappingResult result = Map(json);

        result.Entries.Should().OnlyContain(e => e.DocumentDate == new DateOnly(2026, 8, 1));
        result.Diagnostics.RowsWithoutRrDate.Should().Be(0);
    }

    [Fact]
    public void Map_MissingRrDate_FallsBackToSaleDateAndCountsIt()
    {
        string json = """
        [
          {
            "reportId": 1,
            "dateFrom": "2026-07-27",
            "dateTo": "2026-08-02",
            "createDate": "2026-08-03",
            "currency": "RUB",
            "rrdId": 10,
            "nmId": 5,
            "srid": "srid-1",
            "sellerOperName": "Продажа",
            "saleDt": "2026-08-02T10:00:00Z",
            "rrDate": "",
            "retailAmount": "100",
            "acquiringFee": "0",
            "vw": "0",
            "vwNds": "0",
            "ppvzReward": "0",
            "forPay": "100",
            "deliveryService": "0"
          }
        ]
        """;

        WildberriesFinanceMappingResult result = Map(json);

        result.Entries.Should().OnlyContain(e => e.DocumentDate == new DateOnly(2026, 8, 2));
        result.Diagnostics.RowsWithoutRrDate.Should().Be(1);
    }

    [Fact]
    public void Map_OutOfScopeOperationsAndRowsWithoutSrid_AreCounted()
    {
        string json = """
        [
          { "reportId": 1, "createDate": "2026-08-03", "dateFrom": "2026-07-27", "dateTo": "2026-08-02", "currency": "RUB", "rrdId": 1, "nmId": 5, "srid": "s1", "sellerOperName": "Штраф", "rrDate": "2026-08-01", "retailAmount": "10" },
          { "reportId": 1, "createDate": "2026-08-03", "dateFrom": "2026-07-27", "dateTo": "2026-08-02", "currency": "RUB", "rrdId": 2, "nmId": 5, "srid": "", "sellerOperName": "Продажа", "rrDate": "2026-08-01", "retailAmount": "10" }
        ]
        """;

        WildberriesFinanceMappingResult result = Map(json);

        result.Entries.Should().BeEmpty();
        result.Diagnostics.SkippedOperations.Should().Be(1);
        result.Diagnostics.RowsWithoutSrid.Should().Be(1);
    }

    [Fact]
    public void Map_ZeroAmounts_AreNotEmitted()
    {
        string json = """
        [
          {
            "reportId": 1, "createDate": "2026-08-03", "dateFrom": "2026-07-27", "dateTo": "2026-08-02",
            "currency": "RUB", "rrdId": 10, "nmId": 5, "srid": "srid-1", "sellerOperName": "Продажа",
            "rrDate": "2026-08-01", "retailAmount": "0", "acquiringFee": "0", "vw": "0", "vwNds": "0",
            "ppvzReward": "0", "forPay": "0", "deliveryService": "0"
          }
        ]
        """;

        WildberriesFinanceMappingResult result = Map(json);

        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Map_NegativeSale_EmitsAFullReversalAndCountsIt()
    {
        string json = """
        [
          {
            "reportId": 1, "createDate": "2026-08-03", "dateFrom": "2026-07-27", "dateTo": "2026-08-02",
            "currency": "RUB", "rrdId": 10, "nmId": 5, "srid": "srid-1", "sellerOperName": "Продажа",
            "rrDate": "2026-08-01", "retailAmount": "-204", "acquiringFee": "8.16", "vw": "-21.0991803278688525",
            "vwNds": "-4.64", "ppvzReward": "7.041", "forPay": "-214.54", "deliveryService": "0"
          }
        ]
        """;

        WildberriesFinanceMappingResult result = Map(json);

        result.Diagnostics.Reversals.Should().Be(1);

        AssertEntry(result, "10", FinanceEntryKind.Component, FinanceEntryType.Sale, 204m, FinanceEntryDirection.Expense);
        AssertEntry(result, "10", FinanceEntryKind.Component, FinanceEntryType.Acquiring, 8.16m, FinanceEntryDirection.Expense);
        AssertEntry(result, "10", FinanceEntryKind.Component, FinanceEntryType.Commission, 18.6981803278688525m, FinanceEntryDirection.Income);
        AssertEntry(result, "10", FinanceEntryKind.ControlTotal, FinanceEntryType.Payout, 214.54m, FinanceEntryDirection.Expense);
    }

    private static void AssertEntry(
        WildberriesFinanceMappingResult result,
        string externalEntryId,
        FinanceEntryKind kind,
        FinanceEntryType type,
        decimal amount,
        FinanceEntryDirection direction)
    {
        ChannelFinanceEntrySnapshot entry = result.Entries
            .Should().ContainSingle(e =>
                e.ExternalEntryId == externalEntryId && e.Kind == kind && e.Type == type)
            .Subject;

        entry.Amount.Should().Be(amount);
        entry.Direction.Should().Be(direction);
    }

    private static WildberriesFinanceMappingResult Map(string json)
    {
        List<WildberriesFinanceReportRow> rows =
            JsonSerializer.Deserialize<List<WildberriesFinanceReportRow>>(json)!;

        return WildberriesFinanceMapper.Map(rows);
    }
}
