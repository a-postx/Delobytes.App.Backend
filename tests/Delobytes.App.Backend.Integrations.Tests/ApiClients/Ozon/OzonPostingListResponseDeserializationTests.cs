using System.Text.Json;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients.Ozon;

/// <summary>
/// Tests that one whole Ozon <c>posting/fbo/list</c> page deserializes whatever numeric shape the
/// endpoint sends. The shapes disagree inside a single response: <c>products[].price.amount</c> is a
/// numeric string, <c>financial_data.products[].*</c> are bare numbers. Reading only one of them
/// aborted the entire import, so this file drives the real page contract end to end.
/// </summary>
public class OzonPostingListResponseDeserializationTests
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    /// <summary>
    /// The captured response that failed in production, with <c>financial_data</c> filled: numeric
    /// <c>payout</c>, <c>price</c>, <c>old_price</c>, <c>commission.*</c> and <c>customer_price.amount</c>
    /// next to string <c>products[].price.amount</c>. Byte position 801 of the failing payload falls
    /// inside this block.
    /// </summary>
    private const string FinancialDataResponse = """
    {
        "has_next": true,
        "cursor": "9tyWrg0AAAAAAICpk4ABAAAAAACQs8PB+AIAAAAA",
        "postings": [
            {
                "posting_number": "0208203071-0097-1",
                "order_id": 40519223391,
                "order_number": "0208203071-0097",
                "status": "delivering",
                "substatus": "posting_in_pickup_point",
                "cancellation": null,
                "products": [
                    {
                        "is_marketplace_buyout": false,
                        "offer_id": "свч-77",
                        "name": "Свечи восковые натуральные цветные набор 77",
                        "sku": 5918369809,
                        "quantity": 1,
                        "price": {
                            "amount": "880",
                            "currency": "RUB"
                        },
                        "digital_codes": []
                    }
                ],
                "analytics_data": null,
                "financial_data": {
                    "posting_services": null,
                    "products": [
                        {
                            "product_id": 5918369809,
                            "old_price": 1200.50,
                            "price": 880.00,
                            "payout": 792.32,
                            "commission": {
                                "amount": 87.68,
                                "percent": 9.96,
                                "currency": "RUB"
                            },
                            "customer_price": {
                                "amount": 880.00,
                                "currency": "RUB"
                            },
                            "unknown_settlement_field": 1.00
                        }
                    ]
                },
                "legal_info": null,
                "external_order": {
                    "is_external": false,
                    "platform_name": ""
                },
                "cancel_reason_id": 0,
                "created_at": "2026-10-29T01:54:31.722576Z",
                "in_process_at": "2026-10-29T01:54:44.241023Z",
                "additional_data": [],
                "is_extra_packed": false,
                "is_sealed": false,
                "storage_type": "warehouse"
            }
        ]
    }
    """;

    /// <summary>
    /// The shape the user's own request returned: <c>financial_data</c> null. Guards that the fix does
    /// not depend on the block being present.
    /// </summary>
    private const string NullFinancialDataResponse = """
    {
        "has_next": false,
        "cursor": "",
        "postings": [
            {
                "posting_number": "45274296-0617-1",
                "order_id": 40192045304,
                "order_number": "45274296-0617",
                "status": "delivered",
                "substatus": "posting_received",
                "cancellation": null,
                "products": [
                    {
                        "is_marketplace_buyout": false,
                        "offer_id": "свч-74",
                        "name": "Свечи восковые натуральные цветные набор 74",
                        "sku": 5022694666,
                        "quantity": 1,
                        "price": {
                            "amount": "900",
                            "currency": "RUB"
                        },
                        "digital_codes": []
                    }
                ],
                "analytics_data": null,
                "financial_data": null,
                "legal_info": null,
                "cancel_reason_id": 0,
                "created_at": "2026-10-29T05:18:33.587560Z",
                "in_process_at": "2026-10-29T05:18:51.134376Z"
            }
        ]
    }
    """;

    [Fact]
    public void Deserialize_NumericFinancialDataWithStringProductAmount_ReadsEveryAmount()
    {
        OzonPostingListResponse? page = JsonSerializer.Deserialize<OzonPostingListResponse>(
            FinancialDataResponse, Options);

        page.Should().NotBeNull();
        page!.HasNext.Should().BeTrue();
        page.Cursor.Should().Be("9tyWrg0AAAAAAICpk4ABAAAAAACQs8PB+AIAAAAA");
        page.Postings.Should().HaveCount(1);

        OzonPosting posting = page.Postings![0];
        posting.PostingNumber.Should().Be("0208203071-0097-1");
        posting.OrderId.Should().Be(40519223391);

        OzonPostingProduct product = posting.Products![0];
        product.Sku.Should().Be(5918369809);
        product.Price!.Amount.Should().Be(880m);
        product.Price.Currency.Should().Be("RUB");

        OzonPostingFinancialProduct financial = posting.FinancialData!.Products![0];
        financial.ProductId.Should().Be(5918369809);
        financial.OldPrice.Should().Be(1200.50m);
        financial.Price.Should().Be(880.00m);
        financial.Payout.Should().Be(792.32m);
        financial.Commission!.Amount.Should().Be(87.68m);
        financial.Commission.Percent.Should().Be(9.96m);
        financial.Commission.Currency.Should().Be("RUB");
        financial.CustomerPrice!.Amount.Should().Be(880.00m);
    }

    [Fact]
    public void Deserialize_NullFinancialData_LeavesTheBlockNullAndTheRestIntact()
    {
        OzonPostingListResponse? page = JsonSerializer.Deserialize<OzonPostingListResponse>(
            NullFinancialDataResponse, Options);

        OzonPosting posting = page!.Postings![0];
        posting.FinancialData.Should().BeNull();
        posting.Products![0].Price!.Amount.Should().Be(900m);
        posting.Products[0].Price.Currency.Should().Be("RUB");
    }

    [Fact]
    public void Deserialize_EmptyPostingsArray_YieldsNoPostings()
    {
        const string body = """{ "has_next": false, "cursor": "", "postings": [] }""";

        OzonPostingListResponse? page = JsonSerializer.Deserialize<OzonPostingListResponse>(body, Options);

        page!.Postings.Should().BeEmpty();
    }

    [Fact]
    public void Deserialize_MissingPayout_LeavesPayoutNullRatherThanZero()
    {
        const string body = """
        {
            "has_next": false,
            "postings": [
                {
                    "posting_number": "p-1",
                    "order_number": "order-1",
                    "products": [ { "sku": 111, "quantity": 1 } ],
                    "financial_data": { "products": [ { "product_id": 111, "price": 100.00 } ] },
                    "created_at": "2026-10-29T01:54:31.722576Z"
                }
            ]
        }
        """;

        OzonPostingListResponse? page = JsonSerializer.Deserialize<OzonPostingListResponse>(body, Options);

        OzonPostingFinancialProduct financial = page!.Postings![0].FinancialData!.Products![0];
        financial.Payout.Should().BeNull();
        financial.Price.Should().Be(100.00m);
        financial.Commission.Should().BeNull();
    }
}
