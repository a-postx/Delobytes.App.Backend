using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// Tests for the Ozon accruals mapper. The fixture is a live response: it pins the sign convention
/// (Ozon reports charges as negative), the attribution keys and the rule that a multi-SKU posting
/// has no control total.
/// </summary>
public class OzonFinanceMapperTests
{
    private const string FixtureB = """
    {
      "accruals": [
        {
          "accrual_id": 69844499507,
          "date": "2026-11-16",
          "total_amount": { "amount": "-3.34", "currency": "RUB" },
          "unit_number": "14512295-0639",
          "accrued_category": "ITEM",
          "posting": null,
          "item_fees": {
            "fees": [
              { "sku": 5022694666, "fees": [ { "type_id": 1, "accrued": { "amount": "-3.34", "currency": "RUB" } } ], "quantity": 1 }
            ]
          },
          "non_item_fee": null,
          "container_fees": null
        },
        {
          "accrual_id": 69845968786,
          "date": "2026-11-16",
          "total_amount": { "amount": "-8.21", "currency": "RUB" },
          "unit_number": "41527690-0483",
          "accrued_category": "ITEM",
          "posting": null,
          "item_fees": {
            "fees": [
              { "sku": 5289788252, "fees": [ { "type_id": 1, "accrued": { "amount": "-8.21", "currency": "RUB" } } ], "quantity": 1 }
            ]
          },
          "non_item_fee": null,
          "container_fees": null
        },
        {
          "accrual_id": 69847746662,
          "date": "2026-11-16",
          "total_amount": { "amount": "-88", "currency": "RUB" },
          "unit_number": "37333414-0335-1",
          "accrued_category": "POSTING",
          "posting": {
            "delivery_schema": "Fbo",
            "products": [
              {
                "sku": 1418684813,
                "delivery": {
                  "total_accrued": { "amount": "-88", "currency": "RUB" },
                  "services": [ { "type_id": 32, "accrued": { "amount": "-88", "currency": "RUB" } } ]
                },
                "commission": null,
                "quantity": 1
              }
            ]
          },
          "item_fees": null,
          "non_item_fee": null,
          "container_fees": null
        },
        {
          "accrual_id": 69855196839,
          "date": "2026-11-16",
          "total_amount": { "amount": "601.22", "currency": "RUB" },
          "unit_number": "74691613-0254-1",
          "accrued_category": "POSTING",
          "posting": {
            "delivery_schema": "Fbo",
            "products": [
              {
                "sku": 1761926884,
                "delivery": {
                  "total_accrued": { "amount": "-159.1", "currency": "RUB" },
                  "services": [
                    { "type_id": 32, "accrued": { "amount": "-152", "currency": "RUB" } },
                    { "type_id": 29, "accrued": { "amount": "-7.1", "currency": "RUB" } }
                  ]
                },
                "commission": {
                  "seller_price": { "amount": "1584", "currency": "RUB" },
                  "sale_price": { "amount": "920.38", "currency": "RUB" },
                  "sale_commission": { "amount": "-823.68", "currency": "RUB" },
                  "commission": { "amount": "-823.68", "currency": "RUB" },
                  "commission_ratio": "value:\"0.520000\"",
                  "sale_amount": { "amount": "1584", "currency": "RUB" },
                  "coinvestment": { "amount": "9.2", "currency": "RUB" },
                  "bonus": { "amount": "654.42", "currency": "RUB" }
                },
                "quantity": 1
              }
            ]
          },
          "item_fees": null,
          "non_item_fee": null,
          "container_fees": null
        }
      ],
      "last_id": ""
    }
    """;

    [Fact]
    public void Map_FixtureB_ProducesTheExpectedEntries()
    {
        OzonFinanceMappingResult result = Map(FixtureB);

        result.Entries.Should().HaveCount(11);
        result.Entries.Should().OnlyContain(e => e.Source == FinanceEntrySource.OzonAccrual);
        result.Entries.Should().OnlyContain(e => e.ReportInstanceKey == string.Empty);
        result.Entries.Should().OnlyContain(e => e.Currency == "RUB");
        result.Entries.Should().OnlyContain(e => e.DocumentDate == new DateOnly(2026, 11, 16));
        result.Entries.Should().OnlyContain(e => e.PeriodFrom == new DateOnly(2026, 11, 16));
        result.Entries.Should().OnlyContain(e => e.PeriodTo == new DateOnly(2026, 11, 16));

        AssertEntry(result, "69844499507", FinanceEntryKind.Component, FinanceEntryType.Acquiring, 3.34m, FinanceEntryDirection.Expense);
        AssertEntry(result, "69844499507", FinanceEntryKind.ControlTotal, FinanceEntryType.Payout, 3.34m, FinanceEntryDirection.Expense);
        AssertEntry(result, "69845968786", FinanceEntryKind.Component, FinanceEntryType.Acquiring, 8.21m, FinanceEntryDirection.Expense);
        AssertEntry(result, "69845968786", FinanceEntryKind.ControlTotal, FinanceEntryType.Payout, 8.21m, FinanceEntryDirection.Expense);
        AssertEntry(result, "69847746662", FinanceEntryKind.Component, FinanceEntryType.Logistics, 88m, FinanceEntryDirection.Expense);
        AssertEntry(result, "69847746662", FinanceEntryKind.ControlTotal, FinanceEntryType.Payout, 88m, FinanceEntryDirection.Expense);
        AssertEntry(result, "69855196839", FinanceEntryKind.Component, FinanceEntryType.Sale, 920.38m, FinanceEntryDirection.Income);
        AssertEntry(result, "69855196839", FinanceEntryKind.Component, FinanceEntryType.Compensation, 663.62m, FinanceEntryDirection.Income);
        AssertEntry(result, "69855196839", FinanceEntryKind.Component, FinanceEntryType.Commission, 823.68m, FinanceEntryDirection.Expense);
        AssertEntry(result, "69855196839", FinanceEntryKind.Component, FinanceEntryType.Logistics, 159.1m, FinanceEntryDirection.Expense);
        AssertEntry(result, "69855196839", FinanceEntryKind.ControlTotal, FinanceEntryType.Payout, 601.22m, FinanceEntryDirection.Income);

        // The identity holds on the sample: 920.38 + 663.62 - 823.68 - 159.1 = 601.22.
        result.Diagnostics.SaleAmountMismatches.Should().Be(0);
    }

    [Fact]
    public void Map_MultiSkuPosting_EmitsNoControlTotalAndSumsDuplicatePairs()
    {
        string json = """
        {
          "accruals": [
            {
              "accrual_id": 1, "date": "2026-11-16", "total_amount": { "amount": "500", "currency": "RUB" },
              "unit_number": "posting-1", "accrued_category": "POSTING",
              "posting": { "products": [
                { "sku": 1, "commission": { "sale_price": { "amount": "300", "currency": "RUB" }, "commission": { "amount": "-30", "currency": "RUB" } } },
                { "sku": 2, "commission": { "sale_price": { "amount": "200", "currency": "RUB" }, "commission": { "amount": "-20", "currency": "RUB" } } },
                { "sku": 1, "commission": { "sale_price": { "amount": "300", "currency": "RUB" }, "commission": { "amount": "-30", "currency": "RUB" } } }
              ] },
              "item_fees": null
            }
          ],
          "last_id": ""
        }
        """;

        OzonFinanceMappingResult result = Map(json);

        result.Entries.Should().NotContain(e => e.Kind == FinanceEntryKind.ControlTotal);
        result.Diagnostics.AccrualsWithoutControlTotal.Should().Be(1);

        result.Entries.Where(e => e.ExternalProductId == "1" && e.Type == FinanceEntryType.Sale)
            .Should().ContainSingle().Which.Amount.Should().Be(600m);
    }

    [Fact]
    public void Map_NegativeSalePrice_EmitsAFullReversalWithControlTotal()
    {
        string json = """
        {
          "accruals": [
            {
              "accrual_id": 7, "date": "2026-11-16", "total_amount": { "amount": "-100", "currency": "RUB" },
              "unit_number": "posting-7", "accrued_category": "POSTING",
              "posting": { "products": [
                { "sku": 5, "commission": { "sale_price": { "amount": "-500", "currency": "RUB" }, "commission": { "amount": "-50", "currency": "RUB" }, "bonus": { "amount": "100", "currency": "RUB" } }, "delivery": { "total_accrued": { "amount": "-10", "currency": "RUB" } } }
              ] },
              "item_fees": null
            }
          ],
          "last_id": ""
        }
        """;

        OzonFinanceMappingResult result = Map(json);

        result.Diagnostics.Reversals.Should().Be(1);

        AssertEntry(result, "7", FinanceEntryKind.Component, FinanceEntryType.Sale, 500m, FinanceEntryDirection.Expense);
        AssertEntry(result, "7", FinanceEntryKind.Component, FinanceEntryType.Commission, 50m, FinanceEntryDirection.Income);
        AssertEntry(result, "7", FinanceEntryKind.Component, FinanceEntryType.Logistics, 10m, FinanceEntryDirection.Income);
        AssertEntry(result, "7", FinanceEntryKind.ControlTotal, FinanceEntryType.Payout, 100m, FinanceEntryDirection.Expense);
    }

    [Fact]
    public void Map_SkippedCategoriesAndFeeTypes_AreCounted()
    {
        string json = """
        {
          "accruals": [
            { "accrual_id": 1, "date": "2026-11-16", "total_amount": { "amount": "1", "currency": "RUB" }, "unit_number": "u1", "accrued_category": "STORAGE" },
            { "accrual_id": 2, "date": "2026-11-16", "total_amount": { "amount": "1", "currency": "RUB" }, "unit_number": "u2", "accrued_category": "ITEM",
              "item_fees": { "fees": [ { "sku": 9, "fees": [ { "type_id": 2, "accrued": { "amount": "-5", "currency": "RUB" } } ] } ] } }
          ],
          "last_id": ""
        }
        """;

        OzonFinanceMappingResult result = Map(json);

        result.Entries.Should().BeEmpty();
        result.Diagnostics.SkippedOperations.Should().Be(2);
    }

    [Fact]
    public void Map_ZeroCommissionWithSaleCommission_UsesTheFallbackAndCountsIt()
    {
        string json = """
        {
          "accruals": [
            {
              "accrual_id": 3, "date": "2026-11-16", "total_amount": { "amount": "100", "currency": "RUB" },
              "unit_number": "posting-3", "accrued_category": "POSTING",
              "posting": { "products": [
                { "sku": 5, "commission": { "sale_price": { "amount": "200", "currency": "RUB" }, "commission": { "amount": "0", "currency": "RUB" }, "sale_commission": { "amount": "-100", "currency": "RUB" } } }
              ] },
              "item_fees": null
            }
          ],
          "last_id": ""
        }
        """;

        OzonFinanceMappingResult result = Map(json);

        result.Diagnostics.CommissionFallbacks.Should().Be(1);
        AssertEntry(result, "3", FinanceEntryKind.Component, FinanceEntryType.Commission, 100m, FinanceEntryDirection.Expense);
    }

    private static void AssertEntry(
        OzonFinanceMappingResult result,
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

    private static OzonFinanceMappingResult Map(string json)
    {
        OzonAccrualsResponse response = JsonSerializer.Deserialize<OzonAccrualsResponse>(json)!;

        return OzonFinanceMapper.Map(response.Accruals ?? new List<OzonAccrual>());
    }
}
