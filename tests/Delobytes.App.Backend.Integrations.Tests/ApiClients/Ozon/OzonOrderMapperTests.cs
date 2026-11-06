using System;
using System.Collections.Generic;
using System.Linq;
using Delobytes.App.Backend.Integrations.Application.Models;
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
                            "name": "Свечи восковые",
                            "sku": 9999999909,
                            "quantity": 1,
                            "price": { "amount": "880", "currency": "RUB" }
                        }
                    ],
                    "financial_data": {
                        "products": [
                            {
                                "product_id": 9999999909,
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

    // ── FBS postings ────────────────────────────────────────────────────────────────────────────

    private static OzonPosting FbsPosting(
        string postingNumber,
        string orderNumber,
        string? schema = "fbs",
        DateTimeOffset? createdAt = null,
        DateTimeOffset? inProcessAt = null,
        OzonPostingAnalyticsData? analyticsData = null,
        OzonPostingFinancialData? financialData = null,
        OzonDeliveryMethod? deliveryMethod = null,
        List<OzonPostingProduct>? products = null)
    {
        return new OzonPosting
        {
            PostingNumber = postingNumber,
            OrderNumber = orderNumber,
            OrderId = 1,
            Status = "awaiting_packaging",
            Products = products ?? new List<OzonPostingProduct> { Product(111) },
            CreatedAt = createdAt,
            InProcessAt = inProcessAt,
            AnalyticsData = analyticsData,
            FinancialData = financialData,
            DeliveryMethod = deliveryMethod,
            DeliverySchema = schema,
        };
    }

    [Fact]
    public void Map_FbsPostingWithoutCreatedAt_FallsBackToInProcessAtAsOrderDate()
    {
        // FBS responses may omit created_at entirely; the placement instant then has to come from
        // in_process_at rather than being invented or dropped.
        DateTimeOffset inProcessAt = CreatedAt.AddMinutes(7);
        OzonPosting posting = FbsPosting("post-1", "order-1", createdAt: null, inProcessAt: inProcessAt);

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { posting }, out int rowsWithoutIdentity);

        rowsWithoutIdentity.Should().Be(0);
        result.Should().HaveCount(1);
        result[0].OrderDate.Should().Be(inProcessAt);
        result[0].StatusChangedAt.Should().Be(inProcessAt);
    }

    [Fact]
    public void Map_PostingWithBothInstants_PrefersCreatedAtForTheOrderDate()
    {
        // Preferring created_at keeps an FBO-shaped response mapped exactly as before, even when the
        // endpoint also reports in_process_at.
        OzonPosting posting = FbsPosting(
            "post-1", "order-1", createdAt: CreatedAt, inProcessAt: CreatedAt.AddMinutes(7));

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { posting }, out _);

        result[0].OrderDate.Should().Be(CreatedAt);
        result[0].StatusChangedAt.Should().Be(CreatedAt.AddMinutes(7), "the change instant is in_process_at");
    }

    [Fact]
    public void Map_PostingWithNeitherCreatedAtNorInProcessAt_Throws()
    {
        OzonPosting posting = FbsPosting("post-1", "order-1", createdAt: null, inProcessAt: null);

        Action act = () => OzonOrderMapper.Map(new[] { posting }, out _);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no created_at or in_process_at*");
    }

    [Fact]
    public void Map_FbsPostingWithNullAnalyticsData_ReadsCityFromClusterToAndWarehouseFromDeliveryMethod()
    {
        OzonPostingFinancialData financialData = new OzonPostingFinancialData
        {
            ClusterFrom = "Хоругвино",
            ClusterTo = "Москва",
            Products = new List<OzonPostingFinancialProduct>(),
        };

        OzonPosting posting = FbsPosting(
            "post-1",
            "order-1",
            inProcessAt: CreatedAt,
            analyticsData: null,
            financialData: financialData,
            deliveryMethod: new OzonDeliveryMethod { Warehouse = "Склад продавца", WarehouseId = 42 });

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { posting }, out _);

        ChannelOrderSnapshot snapshot = result[0];
        snapshot.DeliveryCity.Should().Be("Москва");
        snapshot.DeliveryRegion.Should().Be("Хоругвино");
        snapshot.WarehouseName.Should().Be("Склад продавца");
        snapshot.IsB2b.Should().BeNull("a posting without analytics_data reports no legal-entity flag");
    }

    [Fact]
    public void Map_PostingWithAnalyticsData_PrefersItOverTheFinancialClusters()
    {
        OzonPosting posting = FbsPosting(
            "post-1",
            "order-1",
            inProcessAt: CreatedAt,
            analyticsData: new OzonPostingAnalyticsData
            {
                City = "Санкт-Петербург",
                Region = "Ленинградская область",
                WarehouseName = "Склад Ozon",
                IsLegal = true,
            },
            financialData: new OzonPostingFinancialData { ClusterTo = "Москва", ClusterFrom = "Хоругвино" },
            deliveryMethod: new OzonDeliveryMethod { Warehouse = "Склад продавца" });

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { posting }, out _);

        ChannelOrderSnapshot snapshot = result[0];
        snapshot.DeliveryCity.Should().Be("Санкт-Петербург");
        snapshot.DeliveryRegion.Should().Be("Ленинградская область");
        snapshot.WarehouseName.Should().Be("Склад Ozon");
        snapshot.IsB2b.Should().BeTrue();
    }

    [Theory]
    [InlineData("fbs", true)]
    [InlineData("FBS", true)]
    [InlineData("rfbs", true)]
    [InlineData("fbo", false)]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void Map_SinglePosting_ReducesDeliverySchemaToIsSellerWarehouse(string? schema, bool? expected)
    {
        OzonPosting posting = FbsPosting("post-1", "order-1", schema: schema, inProcessAt: CreatedAt);

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { posting }, out _);

        result[0].IsSellerWarehouse.Should().Be(expected);
        result[0].Lines[0].FulfillmentSchema.Should().Be(schema);
    }

    [Fact]
    public void Map_MixedFboAndFbsPostings_IsSellerWarehouseIsNullAndEachLineKeepsItsSchema()
    {
        // One order_number split into an FBO posting and an FBS posting is one order that is neither
        // wholly Ozon-fulfilled nor wholly seller-fulfilled.
        OzonPosting fboPosting = FbsPosting("post-1", "order-1", schema: "fbo", inProcessAt: CreatedAt);
        OzonPosting fbsPosting = FbsPosting(
            "post-2",
            "order-1",
            schema: "fbs",
            inProcessAt: CreatedAt,
            products: new List<OzonPostingProduct> { Product(222) });

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { fboPosting, fbsPosting }, out _);

        result.Should().HaveCount(1);
        ChannelOrderSnapshot snapshot = result[0];
        snapshot.IsSellerWarehouse.Should().BeNull();
        snapshot.Lines.Should().HaveCount(2);
        snapshot.Lines.Single(l => l.ExternalLineId == "post-1").FulfillmentSchema.Should().Be("fbo");
        snapshot.Lines.Single(l => l.ExternalLineId == "post-2").FulfillmentSchema.Should().Be("fbs");
    }

    [Fact]
    public void Map_AllFboPostings_IsSellerWarehouseIsFalse()
    {
        OzonPosting first = FbsPosting("post-1", "order-1", schema: "fbo", inProcessAt: CreatedAt);
        OzonPosting second = FbsPosting(
            "post-2",
            "order-1",
            schema: "fbo",
            inProcessAt: CreatedAt,
            products: new List<OzonPostingProduct> { Product(222) });

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { first, second }, out _);

        result[0].IsSellerWarehouse.Should().BeFalse();
    }

    [Fact]
    public void Map_FbsPageDeserializedFromRealOzonResponse_IsMappedFromInProcessAtAndSchema()
    {
        const string body = """
        {
            "has_next": false,
            "postings": [
                {
                    "posting_number": "93849103-0021-1",
                    "order_id": 5091287231,
                    "order_number": "93849103-0021",
                    "status": "awaiting_deliver",
                    "substatus": "posting_awaiting_passport_data",
                    "delivery_schema": "fbs",
                    "products": [
                        {
                            "offer_id": "свч-77",
                            "name": "Свечи восковые натуральные набор 77",
                            "sku": 5918369809,
                            "quantity": 1,
                            "price": { "amount": "880", "currency": "RUB" }
                        }
                    ],
                    "financial_data": {
                        "cluster_from": "Хоругвино",
                        "cluster_to": "Москва",
                        "products": []
                    },
                    "delivery_method": {
                        "id": 21321684811000,
                        "name": "Доставка продавца",
                        "warehouse_id": 21321684811000,
                        "warehouse": "Склад продавца",
                        "tpl_provider_id": 0,
                        "tpl_provider": ""
                    },
                    "analytics_data": null,
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

        ChannelOrderSnapshot snapshot = result[0];
        snapshot.ExternalOrderId.Should().Be("93849103-0021");
        snapshot.ExternalStatus.Should().Be("awaiting_deliver");
        snapshot.ExternalSubstatus.Should().Be("posting_awaiting_passport_data");
        snapshot.OrderDate.Should().Be(page.Postings![0].InProcessAt!.Value);
        snapshot.StatusChangedAt.Should().Be(page.Postings![0].InProcessAt!.Value);
        snapshot.DeliveryCity.Should().Be("Москва");
        snapshot.WarehouseName.Should().Be("Склад продавца");
        snapshot.IsSellerWarehouse.Should().BeTrue();

        ChannelOrderLineSnapshot line = snapshot.Lines[0];
        line.LineKey.Should().Be("93849103-0021-1");
        line.FulfillmentSchema.Should().Be("fbs");
        line.UnitPrice.Should().Be(880m);
        line.Currency.Should().Be("RUB");
    }

    // ── FBO endpoint defaulting ─────────────────────────────────────────────────────────────

    [Fact]
    public void Map_FboSourceSchema_PostingWithoutDeliverySchema_DefaultsFulfillmentSchemaToFbo()
    {
        // v3/posting/fbo/list never reports delivery_schema; the caller states the endpoint
        // instead, through sourceSchema, and the mapper must still resolve "fbo" from that alone.
        OzonPosting posting = FbsPosting("post-1", "order-1", schema: null, createdAt: CreatedAt);

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(
            new[] { posting }, OrdersSchema.Fbo, null, out int rowsWithoutIdentity);

        rowsWithoutIdentity.Should().Be(0);
        result[0].IsSellerWarehouse.Should().BeFalse();
        result[0].Lines[0].FulfillmentSchema.Should().Be("fbo");
    }

    [Fact]
    public void Map_FboSourceSchema_PostingReportingItsOwnSchema_KeepsReportedValue()
    {
        // A posting that does report delivery_schema (should Ozon ever add it to this endpoint)
        // must win over the endpoint default.
        OzonPosting posting = FbsPosting("post-1", "order-1", schema: "fbo", createdAt: CreatedAt);

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(
            new[] { posting }, OrdersSchema.Fbo, null, out _);

        result[0].Lines[0].FulfillmentSchema.Should().Be("fbo");
    }

    [Fact]
    public void Map_WithoutSourceSchema_PostingWithoutDeliverySchema_LeavesFulfillmentSchemaNull()
    {
        // The overloads that do not state a source schema (kept for backward compatibility) must
        // not guess: this is the behaviour every pre-fix caller relied on.
        OzonPosting posting = FbsPosting("post-1", "order-1", schema: null, createdAt: CreatedAt);

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(new[] { posting }, out _);

        result[0].IsSellerWarehouse.Should().BeNull();
        result[0].Lines[0].FulfillmentSchema.Should().BeNull();
    }

    [Fact]
    public void Map_FbsSourceSchema_PostingWithoutDeliverySchema_LeavesFulfillmentSchemaNull()
    {
        // Unlike FBO, v4/posting/fbs/list is documented to always report delivery_schema. A
        // posting that still omits it is an anomaly to tolerate, not a reason to default to "fbs" -
        // rFBS exists and must not be folded into it.
        OzonPosting posting = FbsPosting("post-1", "order-1", schema: null, createdAt: CreatedAt);

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(
            new[] { posting }, OrdersSchema.Fbs, null, out _);

        result[0].IsSellerWarehouse.Should().BeNull();
        result[0].Lines[0].FulfillmentSchema.Should().BeNull();
    }

    [Fact]
    public void Map_FboPageDeserializedFromRealOzonResponse_DefaultsFulfillmentSchemaToFbo()
    {
        // Mirrors the real v3/posting/fbo/list shape: no delivery_schema field anywhere in the
        // payload, exactly like Ozon's actual response for this endpoint.
        const string body = """
        {
            "has_next": false,
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
                            "offer_id": "свч-74",
                            "name": "Свечи восковые натуральные",
                            "sku": 5022694666,
                            "quantity": 1,
                            "price": { "amount": "900", "currency": "RUB" }
                        }
                    ],
                    "analytics_data": null,
                    "financial_data": null,
                    "created_at": "2026-10-29T05:18:33.587560Z",
                    "in_process_at": "2026-10-29T05:18:51.134376Z"
                }
            ]
        }
        """;

        OzonPostingListResponse? page = System.Text.Json.JsonSerializer.Deserialize<OzonPostingListResponse>(
            body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        page.Should().NotBeNull();

        List<ChannelOrderSnapshot> result = OzonOrderMapper.Map(
            page!.Postings!, OrdersSchema.Fbo, null, out int rowsWithoutIdentity);

        rowsWithoutIdentity.Should().Be(0);
        result.Should().HaveCount(1);

        ChannelOrderSnapshot snapshot = result[0];
        snapshot.ExternalOrderId.Should().Be("45274296-0617");
        snapshot.IsSellerWarehouse.Should().BeFalse();

        ChannelOrderLineSnapshot line = snapshot.Lines[0];
        line.LineKey.Should().Be("45274296-0617-1");
        line.FulfillmentSchema.Should().Be("fbo");
        line.UnitPrice.Should().Be(900m);
        line.Currency.Should().Be("RUB");
    }
}
