using System;
using System.Collections.Generic;
using System.Linq;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients.Ozon;

/// <summary>
/// Tests for <see cref="OzonOrderMapper"/>: posting JSON to <see cref="ChannelOrderSnapshot"/>,
/// specified in Stage 3 of the Ozon orders import specification.
/// </summary>
public class OzonOrderMapperTests
{
    private static readonly DateTimeOffset CreatedAt = new DateTimeOffset(2024, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private static OzonPostingProduct Product(
        long sku, string offerId = "offer-1", string name = "Product", int quantity = 1,
        decimal amount = 100.00m, string currency = "RUB")
    {
        return new OzonPostingProduct
        {
            Sku = sku,
            OfferId = offerId,
            Name = name,
            Quantity = quantity,
            Price = new OzonMoney { Amount = amount, Currency = currency }
        };
    }

    private static OzonPosting Posting(
        string postingNumber,
        string orderNumber,
        List<OzonPostingProduct> products,
        string status = "awaiting_packaging",
        string? substatus = null,
        DateTimeOffset? createdAt = null,
        OzonCancellation? cancellation = null,
        OzonPostingFinancialData? financialData = null,
        long? cancelReasonId = null)
    {
        return new OzonPosting
        {
            PostingNumber = postingNumber,
            OrderNumber = orderNumber,
            OrderId = 1,
            Status = status,
            Substatus = substatus,
            Products = products,
            CreatedAt = createdAt ?? CreatedAt,
            InProcessAt = null,
            Cancellation = cancellation,
            FinancialData = financialData,
            CancelReasonId = cancelReasonId
        };
    }

    [Fact]
    public void Map_SingleProductPosting_LineKeyIsPostingNumberAndEveryFieldMapped()
    {
        OzonPosting posting = Posting(
            "post-1", "order-1", new List<OzonPostingProduct> { Product(111, "off-1", "Name 1", 2, 150.50m, "RUB") });

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { posting }, out int rowsWithoutIdentity);

        rowsWithoutIdentity.Should().Be(0);
        result.Should().HaveCount(1);
        ChannelOrderSnapshot snapshot = result[0];
        snapshot.ExternalOrderId.Should().Be("order-1");
        snapshot.ExternalOrderNumber.Should().Be("order-1");
        snapshot.ReportsHeaderStatus.Should().BeTrue();
        snapshot.Lines.Should().HaveCount(1);

        ChannelOrderLineSnapshot line = snapshot.Lines[0];
        line.LineKey.Should().Be("post-1");
        line.ExternalLineId.Should().Be("post-1");
        line.ExternalProductId.Should().Be("111");
        line.OfferId.Should().Be("off-1");
        line.ProductName.Should().Be("Name 1");
        line.Quantity.Should().Be(2);
        line.UnitPrice.Should().Be(150.50m);
        line.Currency.Should().Be("RUB");
        line.IsCancelled.Should().BeFalse();
        line.ExternalSizeId.Should().BeNull();
        line.Vat.Should().BeNull();
        line.ChannelReportedTotal.Should().BeNull();
    }

    [Fact]
    public void Map_MultiProductDistinctSku_LineKeyIsPostingNumberColonSku()
    {
        OzonPosting posting = Posting(
            "post-1",
            "order-1",
            new List<OzonPostingProduct> { Product(111), Product(222) });

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { posting }, out int rowsWithoutIdentity);

        rowsWithoutIdentity.Should().Be(0);
        List<string> keys = result[0].Lines.Select(l => l.LineKey).ToList();
        keys.Should().BeEquivalentTo(new[] { "post-1:111", "post-1:222" });
    }

    [Fact]
    public void Map_MultiProductDuplicateSku_LineKeyHasOrdinalAndIsStableAcrossRefetch()
    {
        OzonPosting posting = Posting(
            "post-1",
            "order-1",
            new List<OzonPostingProduct> { Product(111), Product(111), Product(222) });

        List<ChannelOrderSnapshot> first = OzonOrderMapper.Map(new[] { posting }, out _);
        List<ChannelOrderSnapshot> second = OzonOrderMapper.Map(new[] { posting }, out _);

        List<string> firstKeys = first[0].Lines.Select(l => l.LineKey).ToList();
        List<string> secondKeys = second[0].Lines.Select(l => l.LineKey).ToList();

        firstKeys.Should().BeEquivalentTo(new[] { "post-1:111:1", "post-1:111:2", "post-1:222" }, options => options.WithStrictOrdering());
        secondKeys.Should().Equal(firstKeys);
    }

    [Fact]
    public void Map_TwoPostingsOfOneOrderNumber_YieldsOneSnapshotWithTwoLines()
    {
        OzonPosting postingA = Posting("post-1", "order-1", new List<OzonPostingProduct> { Product(111) });
        OzonPosting postingB = Posting("post-2", "order-1", new List<OzonPostingProduct> { Product(222) });

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { postingA, postingB }, out int rowsWithoutIdentity);

        rowsWithoutIdentity.Should().Be(0);
        result.Should().HaveCount(1);
        result[0].Lines.Should().HaveCount(2);
        result[0].Lines.Select(l => l.ExternalLineId).Should().BeEquivalentTo(new[] { "post-1", "post-2" });
    }

    [Fact]
    public void Map_OneCancelledAndOneLivePosting_HeaderStatusIsTheLiveOneAndOrderIsNotCancelled()
    {
        OzonPosting cancelled = Posting(
            "post-1", "order-1", new List<OzonPostingProduct> { Product(111) }, status: "cancelled");
        OzonPosting live = Posting(
            "post-2", "order-1", new List<OzonPostingProduct> { Product(222) }, status: "awaiting_deliver", substatus: "sub-1");

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { cancelled, live }, out _);

        ChannelOrderSnapshot snapshot = result[0];
        snapshot.ExternalStatus.Should().Be("awaiting_deliver");
        snapshot.ExternalSubstatus.Should().Be("sub-1");
        snapshot.ExternalStatus.Should().NotBe("cancelled");

        ChannelOrderLineSnapshot cancelledLine = snapshot.Lines.Single(l => l.ExternalLineId == "post-1");
        ChannelOrderLineSnapshot liveLine = snapshot.Lines.Single(l => l.ExternalLineId == "post-2");

        cancelledLine.IsCancelled.Should().BeTrue();
        liveLine.IsCancelled.Should().BeFalse();
    }

    [Fact]
    public void Map_AllPostingsCancelled_ExternalStatusIsCancelledAndCancelReasonFilled()
    {
        OzonPosting posting = Posting(
            "post-1",
            "order-1",
            new List<OzonPostingProduct> { Product(111) },
            status: "cancelled",
            cancellation: new OzonCancellation { CancelReason = "buyer_cancelled" });

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { posting }, out _);

        result[0].ExternalStatus.Should().Be("cancelled");
        result[0].CancelReason.Should().Be("buyer_cancelled");
    }

    [Fact]
    public void Map_AllPostingsCancelledWithoutCancelReason_FallsBackToCancel()
    {
        OzonPosting posting = Posting(
            "post-1", "order-1", new List<OzonPostingProduct> { Product(111) }, status: "cancelled");

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { posting }, out _);

        result[0].ExternalStatus.Should().Be("cancelled");
        result[0].CancelReason.Should().Be("cancel");
    }

    [Fact]
    public void Map_FinancialDataMatchedByProductId_PopulatesCommissionPayoutAndBuyerPrice()
    {
        OzonPostingFinancialData financialData = new OzonPostingFinancialData
        {
            Products = new List<OzonPostingFinancialProduct>
            {
                new OzonPostingFinancialProduct
                {
                    ProductId = 111,
                    OldPrice = 199.99m,
                    Price = 150.50m,
                    Payout = 90.00m,
                    Commission = new OzonCommission { Amount = 10.00m, Percent = 10m, Currency = "RUB" },
                    CustomerPrice = new OzonMoney { Amount = 150.50m, Currency = "RUB" }
                }
            }
        };

        OzonPosting posting = Posting(
            "post-1", "order-1", new List<OzonPostingProduct> { Product(111) }, financialData: financialData);

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { posting }, out _);

        ChannelOrderLineSnapshot line = result[0].Lines[0];
        line.UnitPriceBeforeDiscount.Should().Be(199.99m);
        line.ChannelReportedPayout.Should().Be(90.00m);
        line.ChannelReportedCommission.Should().Be(10.00m);
        line.BuyerUnitPrice.Should().Be(150.50m);
    }

    [Fact]
    public void Map_FinancialDataUnmatchedProduct_LeavesFinancialFieldsNull()
    {
        OzonPostingFinancialData financialData = new OzonPostingFinancialData
        {
            Products = new List<OzonPostingFinancialProduct>
            {
                new OzonPostingFinancialProduct { ProductId = 999, OldPrice = 199.99m }
            }
        };

        OzonPosting posting = Posting(
            "post-1", "order-1", new List<OzonPostingProduct> { Product(111) }, financialData: financialData);

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { posting }, out _);

        ChannelOrderLineSnapshot line = result[0].Lines[0];
        line.UnitPriceBeforeDiscount.Should().BeNull();
        line.ChannelReportedPayout.Should().BeNull();
        line.ChannelReportedCommission.Should().BeNull();
        line.BuyerUnitPrice.Should().BeNull();
    }

    [Fact]
    public void Map_BlankPostingNumber_IsCountedInRowsWithoutIdentityAndProducesNoSnapshot()
    {
        OzonPosting posting = Posting(string.Empty, "order-1", new List<OzonPostingProduct> { Product(111) });

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { posting }, out int rowsWithoutIdentity);

        rowsWithoutIdentity.Should().Be(1);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Map_EmptyProductsArray_IsCountedInRowsWithoutIdentityAndProducesNoSnapshot()
    {
        OzonPosting posting = Posting("post-1", "order-1", new List<OzonPostingProduct>());

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { posting }, out int rowsWithoutIdentity);

        rowsWithoutIdentity.Should().Be(1);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Map_BothOrderNumberAndOrderIdBlank_IsCountedInRowsWithoutIdentity()
    {
        OzonPosting posting = new OzonPosting
        {
            PostingNumber = "post-1",
            OrderNumber = null,
            OrderId = null,
            Status = "awaiting_packaging",
            Products = new List<OzonPostingProduct> { Product(111) },
            CreatedAt = CreatedAt
        };

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { posting }, out int rowsWithoutIdentity);

        rowsWithoutIdentity.Should().Be(1);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Map_LineKeyOverOneHundredCharacters_Throws()
    {
        string longPostingNumber = new string('9', 101);
        OzonPosting posting = Posting(longPostingNumber, "order-1", new List<OzonPostingProduct> { Product(111) });

        Action act = () => OzonOrderMapper.Map(new[] { posting }, out _);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Map_PageDeserializedFromRealOzonResponse_CarriesEverySettlementAmount()
    {
        const string body = """
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
                    "products": [
                        {
                            "offer_id": "свч-77",
                            "name": "Свечи восковые натуральные цветные набор 77",
                            "sku": 5918369809,
                            "quantity": 1,
                            "price": { "amount": "880", "currency": "RUB" }
                        }
                    ],
                    "financial_data": {
                        "products": [
                            {
                                "product_id": 5918369809,
                                "old_price": 1200.50,
                                "price": 880.00,
                                "payout": 792.32,
                                "commission": { "amount": 87.68, "percent": 9.96, "currency": "RUB" },
                                "customer_price": { "amount": 880.00, "currency": "RUB" }
                            }
                        ]
                    },
                    "created_at": "2026-10-29T01:54:31.722576Z",
                    "in_process_at": "2026-10-29T01:54:44.241023Z"
                }
            ]
        }
        """;

        OzonPostingListResponse? page = System.Text.Json.JsonSerializer.Deserialize<OzonPostingListResponse>(
            body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        page.Should().NotBeNull();

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(page!.Postings!, out int rowsWithoutIdentity);

        rowsWithoutIdentity.Should().Be(0);
        result.Should().HaveCount(1);

        ChannelOrderLineSnapshot line = result[0].Lines[0];
        line.LineKey.Should().Be("0208203071-0097-1");
        line.UnitPrice.Should().Be(880m);
        line.BuyerUnitPrice.Should().Be(880.00m);
        line.UnitPriceBeforeDiscount.Should().Be(1200.50m);
        line.ChannelReportedCommission.Should().Be(87.68m);
        line.ChannelReportedPayout.Should().Be(792.32m);
        line.Currency.Should().Be("RUB");
    }
}
