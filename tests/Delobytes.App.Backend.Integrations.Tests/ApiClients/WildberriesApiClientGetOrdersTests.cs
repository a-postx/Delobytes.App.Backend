using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Application.Services.MarketplaceClients;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

public class WildberriesApiClientGetOrdersTests
{
    private const string StatisticsBaseUrl = "https://statistics-api.wildberries.ru";

    private static readonly Guid TestTemplateId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly SystemChannelTemplate TestTemplate = new SystemChannelTemplate
    {
        Id = TestTemplateId,
        Code = "wildberries",
        DisplayName = "Wildberries",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static WildberriesApiClient BuildClient(MockHttpMessageHandler mockHttp, int? maxOrdersPerResponse = null)
    {
        HttpClient typedClient = mockHttp.ToHttpClient();

        Mock<IEndpointResolver> endpointResolver = new Mock<IEndpointResolver>();
        endpointResolver
            .Setup(r => r.GetEndpointUrlAsync(
                TestTemplateId,
                ChannelEndpointType.Statistics,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(StatisticsBaseUrl);

        WildberriesImportOptions options = new WildberriesImportOptions();

        if (maxOrdersPerResponse.HasValue)
        {
            options.MaxOrdersPerResponse = maxOrdersPerResponse.Value;
        }

        WildberriesApiClient client = new WildberriesApiClient(
            typedClient,
            endpointResolver.Object,
            Options.Create(options),
            NullLogger<WildberriesApiClient>.Instance);

        client.SetTemplate(TestTemplate);

        return client;
    }

    private static WildberriesApiClient BuildClientRespondingWith(string json)
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{StatisticsBaseUrl}/api/v1/supplier/orders")
            .Respond("application/json", json);

        return BuildClient(mockHttp);
    }

    [Fact]
    public async Task GetOrdersAsync_ValidResponse_MapsFieldsToSnapshot()
    {
        string json = """
        [
            {
                "date": "2024-01-10T08:00:00",
                "lastChangeDate": "2024-01-10T09:30:00",
                "warehouseName": "Подольск",
                "warehouseType": "Склад WB",
                "regionName": "Московская",
                "supplierArticle": "ART-1",
                "nmId": 123456,
                "subject": "Кресло",
                "techSize": "0",
                "totalPrice": 1200,
                "priceWithDisc": 1000,
                "finishedPrice": 950,
                "isCancel": false,
                "orderType": "Клиентский",
                "gNumber": "G-1",
                "srid": "SRID-1"
            }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersRequest request = new OrdersRequest
        {
            DateFrom = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DateTo = new DateTimeOffset(2024, 1, 31, 0, 0, 0, TimeSpan.Zero),
            Flag = 0,
            Limit = 100
        };

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), request, CancellationToken.None);

        page.Should().NotBeNull();
        page.HasMore.Should().BeFalse();
        page.Orders.Should().HaveCount(1);

        ChannelOrderSnapshot order = page.Orders.Single();

        // The snapshot is a cart, so its identity is gNumber; srid is the identity of the unit.
        order.ExternalOrderId.Should().Be("G-1");
        order.ExternalOrderNumber.Should().Be("G-1");
        order.ExternalStatus.Should().Be("active");
        order.CancelReason.Should().BeNull();
        order.Currency.Should().Be("RUB");
        order.WarehouseName.Should().Be("Подольск");
        order.IsSellerWarehouse.Should().BeFalse();
        order.DeliveryRegion.Should().Be("Московская");
        order.ChannelReportedTotal.Should().Be(1200);

        order.Lines.Should().HaveCount(1);
        ChannelOrderLineSnapshot line = order.Lines.Single();
        line.LineKey.Should().Be("SRID-1");
        line.ExternalLineId.Should().Be("SRID-1");
        line.ExternalProductId.Should().Be("123456");
        line.OfferId.Should().Be("ART-1");
        line.Quantity.Should().Be(1);
        line.UnitPrice.Should().Be(1000);
        line.BuyerUnitPrice.Should().Be(950);
        line.UnitPriceBeforeDiscount.Should().Be(1200);
        line.Currency.Should().Be("RUB");
    }

    [Fact]
    public async Task GetOrdersAsync_CancelledOrder_MapsCancelledStatus()
    {
        string json = """
        [
            {
                "date": "2024-01-10T08:00:00",
                "lastChangeDate": "2024-01-10T09:30:00",
                "nmId": 1,
                "isCancel": true,
                "gNumber": "G-1",
                "srid": "SRID-2"
            }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        ChannelOrderSnapshot order = page.Orders.Single();
        order.ExternalStatus.Should().Be("cancelled");
        order.CancelReason.Should().Be("cancel");
    }

    [Fact]
    public async Task GetOrdersAsync_CartWithOneCancelledUnitAndOneLiveUnit_StaysActive()
    {
        string json = """
        [
            { "date": "2024-01-10T08:00:00", "lastChangeDate": "2024-01-10T09:30:00", "nmId": 1, "isCancel": true, "gNumber": "G-1", "srid": "SRID-1" },
            { "date": "2024-01-10T08:00:00", "lastChangeDate": "2024-01-10T09:30:00", "nmId": 2, "isCancel": false, "gNumber": "G-1", "srid": "SRID-2" }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        // OrderStatus.Cancelled is projected from ExternalStatus, so a single cancelled unit must
        // not be allowed to mark the still-live cart as cancelled.
        ChannelOrderSnapshot order = page.Orders.Single();
        order.ExternalStatus.Should().Be("active");
        order.CancelReason.Should().BeNull();
        order.Lines.Should().HaveCount(2);

        // The per-unit flags the cart status is reduced from: without them the header could not be
        // recomputed once the cart is assembled from more than one report.
        order.Lines.Single(l => l.LineKey == "SRID-1").IsCancelled.Should().BeTrue();
        order.Lines.Single(l => l.LineKey == "SRID-2").IsCancelled.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrdersAsync_CartWithEveryUnitCancelled_IsCancelled()
    {
        string json = """
        [
            { "date": "2024-01-10T08:00:00", "lastChangeDate": "2024-01-10T09:30:00", "nmId": 1, "isCancel": true, "gNumber": "G-1", "srid": "SRID-1" },
            { "date": "2024-01-10T08:00:00", "lastChangeDate": "2024-01-10T09:30:00", "nmId": 2, "isCancel": true, "gNumber": "G-1", "srid": "SRID-2" }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        ChannelOrderSnapshot order = page.Orders.Single();
        order.ExternalStatus.Should().Be("cancelled");
        order.CancelReason.Should().Be("cancel");
        order.Lines.Should().OnlyContain(l => l.IsCancelled);
    }

    [Fact]
    public async Task GetOrdersAsync_EachLine_CarriesItsOwnStatusChangedAt()
    {
        string json = """
        [
            { "date": "2024-01-10T08:00:00", "lastChangeDate": "2024-01-10T09:30:00", "nmId": 1, "isCancel": false, "gNumber": "G-1", "srid": "SRID-1" },
            { "date": "2024-01-10T08:00:00", "lastChangeDate": "2024-01-10T14:15:00", "nmId": 2, "isCancel": true, "gNumber": "G-1", "srid": "SRID-2" }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        // Each unit's lastChangeDate is a reported fact of that unit. The cart-level instant is a
        // reduction over these, so the line has to keep its own or the reduction cannot be redone
        // after the cart is merged.
        ChannelOrderSnapshot order = page.Orders.Single();
        order.Lines.Single(l => l.LineKey == "SRID-1").StatusChangedAt
            .Should().Be(new DateTimeOffset(2024, 1, 10, 6, 30, 0, TimeSpan.Zero));
        order.Lines.Single(l => l.LineKey == "SRID-2").StatusChangedAt
            .Should().Be(new DateTimeOffset(2024, 1, 10, 11, 15, 0, TimeSpan.Zero));
        order.StatusChangedAt.Should().Be(new DateTimeOffset(2024, 1, 10, 11, 15, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetOrdersAsync_MultiUnitCart_ReducesOrderDateToEarliestAndStatusChangedAtToLatest()
    {
        string json = """
        [
            { "date": "2024-01-10T12:00:00", "lastChangeDate": "2024-01-10T13:00:00", "nmId": 1, "isCancel": false, "gNumber": "G-1", "srid": "SRID-1" },
            { "date": "2024-01-10T09:00:00", "lastChangeDate": "2024-01-10T15:00:00", "nmId": 2, "isCancel": false, "gNumber": "G-1", "srid": "SRID-2" }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        // The watermark is derived from StatusChangedAt, so the cart must report its newest unit's
        // instant; the placement time is the earliest instant any unit of the cart was placed.
        ChannelOrderSnapshot order = page.Orders.Single();
        order.OrderDate.Should().Be(new DateTimeOffset(2024, 1, 10, 6, 0, 0, TimeSpan.Zero));
        order.StatusChangedAt.Should().Be(new DateTimeOffset(2024, 1, 10, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetOrdersAsync_TwoDifferentArticlesInOneCart_ProduceOneOrderWithTwoLines()
    {
        string json = """
        [
            {
                "date": "2024-01-10T08:00:00",
                "lastChangeDate": "2024-01-10T09:30:00",
                "supplierArticle": "ART-1",
                "nmId": 111,
                "techSize": "0",
                "totalPrice": 1200,
                "priceWithDisc": 1000,
                "finishedPrice": 950,
                "isCancel": false,
                "gNumber": "G-1",
                "srid": "SRID-1"
            },
            {
                "date": "2024-01-10T08:00:00",
                "lastChangeDate": "2024-01-10T09:30:00",
                "supplierArticle": "ART-2",
                "nmId": 222,
                "techSize": "L",
                "totalPrice": 2400,
                "priceWithDisc": 2000,
                "finishedPrice": 1900,
                "isCancel": false,
                "gNumber": "G-1",
                "srid": "SRID-2"
            }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        page.Orders.Should().HaveCount(1);
        page.RowsWithoutIdentity.Should().Be(0);

        ChannelOrderSnapshot order = page.Orders.Single();
        order.ExternalOrderId.Should().Be("G-1");
        order.Lines.Should().HaveCount(2);
        order.Lines.Select(l => l.LineKey).Should().BeEquivalentTo(new[] { "SRID-1", "SRID-2" });

        ChannelOrderLineSnapshot firstLine = order.Lines.Single(l => l.LineKey == "SRID-1");
        firstLine.ExternalProductId.Should().Be("111");
        firstLine.OfferId.Should().Be("ART-1");
        firstLine.ExternalSizeId.Should().Be("0");
        firstLine.UnitPrice.Should().Be(1000);
        firstLine.BuyerUnitPrice.Should().Be(950);
        firstLine.UnitPriceBeforeDiscount.Should().Be(1200);

        ChannelOrderLineSnapshot secondLine = order.Lines.Single(l => l.LineKey == "SRID-2");
        secondLine.ExternalProductId.Should().Be("222");
        secondLine.OfferId.Should().Be("ART-2");
        secondLine.ExternalSizeId.Should().Be("L");
        secondLine.UnitPrice.Should().Be(2000);
        secondLine.BuyerUnitPrice.Should().Be(1900);
        secondLine.UnitPriceBeforeDiscount.Should().Be(2400);
    }

    [Fact]
    public async Task GetOrdersAsync_TwoIdenticalUnitsInOneCart_ProduceTwoLinesOfQuantityOne()
    {
        // The case the old mapper collapsed: two units of the same article and size share nmId,
        // techSize, gNumber, prices and (at second precision) both timestamps, and differ only by
        // srid. They are two lines of one unit each, not one line of two.
        string json = """
        [
            {
                "date": "2024-01-10T08:00:00",
                "lastChangeDate": "2024-01-10T09:30:00",
                "supplierArticle": "ART-1",
                "nmId": 111,
                "techSize": "0",
                "totalPrice": 1200,
                "priceWithDisc": 1000,
                "finishedPrice": 950,
                "isCancel": false,
                "gNumber": "G-1",
                "srid": "SRID-1"
            },
            {
                "date": "2024-01-10T08:00:00",
                "lastChangeDate": "2024-01-10T09:30:00",
                "supplierArticle": "ART-1",
                "nmId": 111,
                "techSize": "0",
                "totalPrice": 1200,
                "priceWithDisc": 1000,
                "finishedPrice": 950,
                "isCancel": false,
                "gNumber": "G-1",
                "srid": "SRID-2"
            }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        page.Orders.Should().HaveCount(1);

        ChannelOrderSnapshot order = page.Orders.Single();
        order.ExternalOrderId.Should().Be("G-1");
        order.Lines.Should().HaveCount(2);
        order.Lines.Select(l => l.LineKey).Should().BeEquivalentTo(new[] { "SRID-1", "SRID-2" });
        order.Lines.Should().OnlyContain(l => l.Quantity == 1);
        order.Lines.Should().OnlyContain(l => l.ExternalProductId == "111");
    }

    [Fact]
    public async Task GetOrdersAsync_MultiLineCart_ReportsNoChannelTotal()
    {
        string json = """
        [
            { "date": "2024-01-10T08:00:00", "lastChangeDate": "2024-01-10T09:30:00", "nmId": 1, "totalPrice": 1200, "isCancel": false, "gNumber": "G-1", "srid": "SRID-1" },
            { "date": "2024-01-10T08:00:00", "lastChangeDate": "2024-01-10T09:30:00", "nmId": 2, "totalPrice": 700, "isCancel": false, "gNumber": "G-1", "srid": "SRID-2" }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        // Wildberries reports totalPrice per unit, so a multi-unit cart has no reportable order
        // total: summing the units would be a derived value in a reported-facts field.
        page.Orders.Single().ChannelReportedTotal.Should().BeNull();
    }

    [Fact]
    public async Task GetOrdersAsync_SingleLineCart_KeepsReportedTotal()
    {
        string json = """
        [
            { "date": "2024-01-10T08:00:00", "lastChangeDate": "2024-01-10T09:30:00", "nmId": 1, "totalPrice": 1200, "isCancel": false, "gNumber": "G-1", "srid": "SRID-1" }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        page.Orders.Single().ChannelReportedTotal.Should().Be(1200);
    }

    [Fact]
    public async Task GetOrdersAsync_MissingGNumber_CountsTheRowAndProducesNoOrder()
    {
        // The row carries a usable line identity and no order identity. Falling back to the line
        // identity would key the cart on a value that changes the moment gNumber starts being
        // reported, and the second key would satisfy every constraint just as the first did.
        string json = """
        [
            {
                "date": "2024-01-10T08:00:00",
                "lastChangeDate": "2024-01-10T09:30:00",
                "nmId": 123456,
                "srid": "SRID-1"
            }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        page.Orders.Should().BeEmpty();
        page.RowsWithoutIdentity.Should().Be(1);
    }

    [Fact]
    public async Task GetOrdersAsync_CartWithNoParseableDate_FailsInsteadOfSubstitutingUtcNow()
    {
        string json = """
        [
            { "date": "not-a-date", "lastChangeDate": "2024-01-10T09:30:00", "nmId": 1, "isCancel": false, "gNumber": "G-1", "srid": "SRID-1" },
            { "date": "", "lastChangeDate": "2024-01-10T09:35:00", "nmId": 2, "isCancel": false, "gNumber": "G-1", "srid": "SRID-2" }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        Func<Task> act = async () =>
            await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        // The wall clock would make every refetch of unchanged data differ, which the consumer sees
        // as an update, so the run would never take the Skipped path.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*parseable date*");
    }

    [Fact]
    public async Task GetOrdersAsync_RefetchingUnchangedData_ReportsTheSameHeaderInstants()
    {
        string json = """
        [
            { "date": "2024-01-10T08:00:00", "lastChangeDate": "2024-01-10T09:30:00", "nmId": 1, "isCancel": false, "gNumber": "G-1", "srid": "SRID-1" }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersRequest request = BuildWindowRequest();

        OrdersPage first = await client.GetOrdersAsync(Guid.NewGuid(), request, CancellationToken.None);
        OrdersPage second = await client.GetOrdersAsync(Guid.NewGuid(), request, CancellationToken.None);

        // Idempotency at the mapper level: nothing about an unchanged cart may differ between runs.
        ChannelOrderSnapshot firstOrder = first.Orders.Single();
        ChannelOrderSnapshot secondOrder = second.Orders.Single();

        secondOrder.OrderDate.Should().Be(firstOrder.OrderDate);
        secondOrder.StatusChangedAt.Should().Be(firstOrder.StatusChangedAt);
        secondOrder.ExternalStatus.Should().Be(firstOrder.ExternalStatus);
        secondOrder.ChannelReportedTotal.Should().Be(firstOrder.ChannelReportedTotal);
        secondOrder.OrderDate.Should().Be(new DateTimeOffset(2024, 1, 10, 5, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetOrdersAsync_OrdersAfterDateTo_AreFilteredOutClientSide()
    {
        string json = """
        [
            {
                "date": "2024-01-10T08:00:00",
                "lastChangeDate": "2024-01-10T09:30:00",
                "nmId": 1,
                "isCancel": false,
                "gNumber": "G-IN-WINDOW",
                "srid": "IN-WINDOW"
            },
            {
                "date": "2024-02-15T08:00:00",
                "lastChangeDate": "2024-02-15T09:30:00",
                "nmId": 2,
                "isCancel": false,
                "gNumber": "G-OUT-OF-WINDOW",
                "srid": "OUT-OF-WINDOW"
            }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        page.Orders.Should().ContainSingle(o => o.ExternalOrderId == "G-IN-WINDOW");
    }

    [Fact]
    public async Task GetOrdersAsync_RequestLimitLowerThanResultCount_ReturnsEveryRow()
    {
        string json = """
        [
            { "date": "2024-01-10T08:00:00", "lastChangeDate": "2024-01-10T09:30:00", "nmId": 1, "isCancel": false, "gNumber": "G-1", "srid": "A" },
            { "date": "2024-01-11T08:00:00", "lastChangeDate": "2024-01-11T09:30:00", "nmId": 2, "isCancel": false, "gNumber": "G-2", "srid": "B" }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersRequest request = new OrdersRequest
        {
            DateFrom = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DateTo = new DateTimeOffset(2024, 1, 31, 0, 0, 0, TimeSpan.Zero),
            Flag = 0,
            Limit = 1
        };

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), request, CancellationToken.None);

        page.Orders.Should().HaveCount(2);
        page.Orders.Select(o => o.ExternalOrderId).Should().BeEquivalentTo(new[] { "G-1", "G-2" });
        page.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrdersAsync_SendsDateFromAndFlagAsQueryParameters()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{StatisticsBaseUrl}/api/v1/supplier/orders*")
            .With(req => req.RequestUri!.Query.Contains("flag=1") &&
                         req.RequestUri!.Query.Contains("dateFrom="))
            .Respond("application/json", "[]");

        WildberriesApiClient client = BuildClient(mockHttp);

        OrdersRequest request = new OrdersRequest
        {
            DateFrom = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DateTo = new DateTimeOffset(2024, 1, 31, 0, 0, 0, TimeSpan.Zero),
            Flag = 1,
            Limit = 100
        };

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), request, CancellationToken.None);

        page.Orders.Should().BeEmpty();
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetOrdersAsync_NonSuccessStatusCode_ThrowsHttpRequestException()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{StatisticsBaseUrl}/api/v1/supplier/orders*")
            .Respond(HttpStatusCode.Unauthorized);

        WildberriesApiClient client = BuildClient(mockHttp);

        OrdersRequest request = new OrdersRequest
        {
            DateFrom = DateTimeOffset.UtcNow.AddDays(-1),
            DateTo = DateTimeOffset.UtcNow,
            Flag = 0,
            Limit = 100
        };

        Func<Task> act = async () => await client.GetOrdersAsync(Guid.NewGuid(), request, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetOrdersAsync_TemplateNotBound_ThrowsInvalidOperationException()
    {
        HttpClient typedClient = new MockHttpMessageHandler().ToHttpClient();
        Mock<IEndpointResolver> endpointResolver = new Mock<IEndpointResolver>();

        WildberriesApiClient client = new WildberriesApiClient(
            typedClient,
            endpointResolver.Object,
            Options.Create(new WildberriesImportOptions()),
            NullLogger<WildberriesApiClient>.Instance);

        IOrdersSource ordersSource = client;

        Func<Task> act = async () => await ordersSource.GetOrdersAsync(
            Guid.NewGuid(),
            new OrdersRequest { DateFrom = DateTimeOffset.UtcNow, DateTo = DateTimeOffset.UtcNow, Flag = 0, Limit = 10 },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetOrdersAsync_StickerWithoutGNumber_CountsTheRowAndProducesNoOrder()
    {
        string json = """
        [
            {
                "date": "2024-01-10T08:00:00",
                "lastChangeDate": "2024-01-10T09:30:00",
                "nmId": 123456,
                "techSize": "0",
                "sticker": "STK-A"
            }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        // A sticker addresses a unit inside a cart, so it stays a legitimate line identity, but it
        // cannot stand in for the cart: the cart would be keyed by whichever unit arrived first.
        page.Orders.Should().BeEmpty();
        page.RowsWithoutIdentity.Should().Be(1);
    }

    [Fact]
    public async Task GetOrdersAsync_StickerWithGNumber_KeysOrderByGNumberAndLineBySticker()
    {
        string json = """
        [
            {
                "date": "2024-01-10T08:00:00",
                "lastChangeDate": "2024-01-10T09:30:00",
                "nmId": 123456,
                "techSize": "0",
                "gNumber": "G-1",
                "sticker": "STK-A"
            }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        ChannelOrderSnapshot order = page.Orders.Single();
        order.ExternalOrderId.Should().Be("G-1");
        order.ExternalOrderNumber.Should().Be("G-1");
        order.Lines.Single().LineKey.Should().Be("wb-stk:STK-A");
        order.Lines.Single().ExternalLineId.Should().BeNull();
    }

    [Fact]
    public async Task GetOrdersAsync_SridPresent_TakesPrecedenceOverSticker()
    {
        string json = """
        [
            {
                "date": "2024-01-10T08:00:00",
                "lastChangeDate": "2024-01-10T09:30:00",
                "nmId": 123456,
                "gNumber": "G-1",
                "srid": "SRID-1",
                "sticker": "STK-A"
            }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        // srid is the documented unit identity and is never rewritten into the derived sticker form.
        page.Orders.Single().ExternalOrderId.Should().Be("G-1");
        page.Orders.Single().Lines.Single().LineKey.Should().Be("SRID-1");
        page.RowsWithoutIdentity.Should().Be(0);
    }

    [Fact]
    public async Task GetOrdersAsync_TwoStickerOnlyRowsWithoutGNumber_AreBothCountedAndProduceNoOrders()
    {
        // Two units of one cart share nmId, techSize and (at second precision) date, and differ only
        // by sticker. Giving each its own order keyed on the sticker is what split a multi-unit cart
        // into N single-line orders; without a gNumber neither row has an order identity at all, so
        // the page reports both as unmappable instead of inventing two carts.
        string json = """
        [
            {
                "date": "2024-01-10T08:00:00",
                "lastChangeDate": "2024-01-10T09:30:00",
                "nmId": 123456,
                "techSize": "0",
                "sticker": "STK-A"
            },
            {
                "date": "2024-01-10T08:00:00",
                "lastChangeDate": "2024-01-10T09:30:00",
                "nmId": 123456,
                "techSize": "0",
                "sticker": "STK-B"
            }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        page.Orders.Should().BeEmpty();
        page.RowsWithoutIdentity.Should().Be(2);
    }

    [Fact]
    public async Task GetOrdersAsync_TwoStickerOnlyRowsWithSameGNumber_BecomeOneOrderWithTwoLines()
    {
        string json = """
        [
            {
                "date": "2024-01-10T08:00:00",
                "lastChangeDate": "2024-01-10T09:30:00",
                "nmId": 123456,
                "techSize": "0",
                "gNumber": "G-1",
                "sticker": "STK-A"
            },
            {
                "date": "2024-01-10T08:00:00",
                "lastChangeDate": "2024-01-10T09:30:00",
                "nmId": 123456,
                "techSize": "0",
                "gNumber": "G-1",
                "sticker": "STK-B"
            }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        ChannelOrderSnapshot order = page.Orders.Single();
        order.ExternalOrderId.Should().Be("G-1");
        order.Lines.Select(l => l.LineKey).Should().BeEquivalentTo(new[] { "wb-stk:STK-A", "wb-stk:STK-B" });
    }

    [Fact]
    public async Task GetOrdersAsync_RowWithoutSridAndSticker_IsSkippedAndCounted()
    {
        string json = """
        [
            {
                "date": "2024-01-10T08:00:00",
                "lastChangeDate": "2024-01-10T09:30:00",
                "nmId": 123456,
                "techSize": "0",
                "gNumber": "G-1",
                "srid": "SRID-1"
            },
            {
                "date": "2024-01-10T08:05:00",
                "lastChangeDate": "2024-01-10T09:35:00",
                "nmId": 654321,
                "techSize": "1",
                "gNumber": "G-2"
            }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        page.Orders.Should().HaveCount(1);
        page.Orders.Single().ExternalOrderId.Should().Be("G-1");
        page.Orders.Single().Lines.Single().LineKey.Should().Be("SRID-1");

        // No composite of nmId/techSize/gNumber/date is proven unique per order, so the row is
        // dropped and reported rather than given a key that could merge it with another order.
        page.RowsWithoutIdentity.Should().Be(1);
    }

    [Fact]
    public async Task GetOrdersAsync_DuplicateSridWithinOneOrder_Throws()
    {
        string json = """
        [
            { "date": "2024-01-10T08:00:00", "nmId": 1, "gNumber": "G-1", "srid": "SRID-1" },
            { "date": "2024-01-10T08:00:00", "nmId": 2, "gNumber": "G-1", "srid": "SRID-1" }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        Func<Task> act = async () =>
            await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        // One srid describes one ordered unit, so two rows claiming it are two versions of that
        // unit rather than two units. Keeping either would silently decide which one is believed.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*'SRID-1'*'G-1'*");
    }

    [Fact]
    public async Task GetOrdersAsync_SameSridInTwoDifferentOrders_IsNotADuplicate()
    {
        string json = """
        [
            { "date": "2024-01-10T08:00:00", "nmId": 1, "gNumber": "G-1", "srid": "SRID-1" },
            { "date": "2024-01-10T08:00:00", "nmId": 1, "gNumber": "G-2", "srid": "SRID-1" }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        // Line keys are unique within an order, not globally.
        page.Orders.Should().HaveCount(2);
        page.Orders.Select(o => o.ExternalOrderId).Should().BeEquivalentTo(new[] { "G-1", "G-2" });
    }

    [Fact]
    public async Task GetOrdersAsync_GNumberLongerThanPersistableLength_Throws()
    {
        string overlongGNumber = new string('G', 101);

        string json = $$"""
        [
            { "date": "2024-01-10T08:00:00", "nmId": 1, "gNumber": "{{overlongGNumber}}", "srid": "SRID-1" }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        Func<Task> act = async () =>
            await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        // Order.ExternalOrderId is HasMaxLength(100); failing here beats a silent truncation that
        // would create a second order row on the next import.
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetOrdersAsync_SridLongerThanPersistableLength_Throws()
    {
        string overlongSrid = new string('S', 101);

        string json = $$"""
        [
            { "date": "2024-01-10T08:00:00", "nmId": 1, "gNumber": "G-1", "srid": "{{overlongSrid}}" }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        Func<Task> act = async () =>
            await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetOrdersAsync_StickerFallbackLongerThanPersistableLength_Throws()
    {
        string overlongSticker = new string('K', 100);

        string json = $$"""
        [
            { "date": "2024-01-10T08:00:00", "nmId": 1, "sticker": "{{overlongSticker}}" }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        Func<Task> act = async () =>
            await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        // 100 characters plus the wb-stk: prefix exceeds the column width.
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetOrdersAsync_OverlongKey_ThrowsOnIdentityLengthNotOnMissingIdentity()
    {
        string overlongGNumber = new string('G', 101);

        string json = $$"""
        [
            { "date": "2024-01-10T08:00:00", "nmId": 1, "gNumber": "{{overlongGNumber}}", "srid": "SRID-1" },
            { "date": "2024-01-10T08:05:00", "nmId": 2, "gNumber": "G-2", "srid": "SRID-2" }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);

        Func<Task> act = async () =>
            await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        // The row has an identity; it is only too long to persist. Throwing on the length keeps it
        // out of RowsWithoutIdentity, which would report the failure as data-quality loss.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exceeds the maximum persistable length*");
    }

    [Fact]
    public async Task GetOrdersAsync_RowsReachingResponseCap_ReportsHasMore()
    {
        string json = """
        [
            { "date": "2024-01-10T08:00:00", "nmId": 1, "gNumber": "G-1", "srid": "A" },
            { "date": "2024-01-10T08:00:00", "nmId": 2, "gNumber": "G-2", "srid": "B" }
        ]
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{StatisticsBaseUrl}/api/v1/supplier/orders")
            .Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp, maxOrdersPerResponse: 2);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        page.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrdersAsync_CappedResponse_ReportsCoverageAtLatestSelectionInstant()
    {
        string json = """
        [
            { "date": "2024-01-10T08:00:00", "lastChangeDate": "2024-01-10T09:30:00", "nmId": 1, "gNumber": "G-1", "srid": "A" },
            { "date": "2024-01-12T08:00:00", "lastChangeDate": "2024-01-20T09:30:00", "nmId": 2, "gNumber": "G-2", "srid": "B" }
        ]
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{StatisticsBaseUrl}/api/v1/supplier/orders")
            .Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp, maxOrdersPerResponse: 2);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        page.HasMore.Should().BeTrue();

        // WC time is UTC+3, so 2024-01-20T09:30 local is 06:30Z. The delta axis is lastChangeDate,
        // never the order date: a cart's position in the delta window is where its last change sits.
        page.CoveredUntil.Should().Be(new DateTimeOffset(2024, 1, 20, 6, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetOrdersAsync_CappedResponseWithRowsOutOfOrder_ReportsNoCoverage()
    {
        string json = """
        [
            { "date": "2024-01-10T08:00:00", "lastChangeDate": "2024-01-20T09:30:00", "nmId": 1, "gNumber": "G-1", "srid": "A" },
            { "date": "2024-01-12T08:00:00", "lastChangeDate": "2024-01-10T09:30:00", "nmId": 2, "gNumber": "G-2", "srid": "B" }
        ]
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{StatisticsBaseUrl}/api/v1/supplier/orders")
            .Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp, maxOrdersPerResponse: 2);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        page.HasMore.Should().BeTrue();

        // The rows do not ascend by the delta axis, so the largest instant does not bound a served
        // prefix: unseen rows may sit below it. Claiming coverage there would let the caller resume
        // past them, so the page reports that coverage is undeterminable.
        page.CoveredUntil.Should().BeNull();
    }

    [Fact]
    public async Task GetOrdersAsync_ResponseBelowCap_ReportsCoverageOfWholeWindow()
    {
        string json = """
        [
            { "date": "2024-01-10T08:00:00", "lastChangeDate": "2024-01-10T09:30:00", "nmId": 1, "gNumber": "G-1", "srid": "A" }
        ]
        """;

        WildberriesApiClient client = BuildClientRespondingWith(json);
        OrdersRequest request = BuildWindowRequest();

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), request, CancellationToken.None);

        page.HasMore.Should().BeFalse();

        // Nothing was cut off, so the page covers exactly what was asked for.
        page.CoveredUntil.Should().Be(request.DateTo);
    }

    [Fact]
    public async Task GetOrdersAsync_CappedResponse_RowPastWindowEndStillBoundsCoverage()
    {
        string json = """
        [
            { "date": "2024-01-10T08:00:00", "lastChangeDate": "2024-01-10T09:30:00", "nmId": 1, "gNumber": "G-1", "srid": "A" },
            { "date": "2024-02-15T08:00:00", "lastChangeDate": "2024-02-15T09:30:00", "nmId": 2, "gNumber": "G-2", "srid": "B" }
        ]
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{StatisticsBaseUrl}/api/v1/supplier/orders")
            .Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp, maxOrdersPerResponse: 2);

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), BuildWindowRequest(), CancellationToken.None);

        // The row past the window end is filtered out of the page, but it is what consumed the cap,
        // so it still bounds how far the response reached.
        page.Orders.Should().ContainSingle(o => o.ExternalOrderId == "G-1");
        page.HasMore.Should().BeTrue();
        page.CoveredUntil.Should().Be(new DateTimeOffset(2024, 2, 15, 6, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetOrdersAsync_CappedBackfillResponse_ReportsCoverageOnOrderDateAxis()
    {
        string json = """
        [
            { "date": "2024-01-10T08:00:00", "lastChangeDate": "2024-01-25T09:30:00", "nmId": 1, "gNumber": "G-1", "srid": "A" },
            { "date": "2024-01-20T08:00:00", "lastChangeDate": "2024-01-11T09:30:00", "nmId": 2, "gNumber": "G-2", "srid": "B" }
        ]
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{StatisticsBaseUrl}/api/v1/supplier/orders")
            .Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp, maxOrdersPerResponse: 2);

        OrdersRequest request = new OrdersRequest
        {
            DateFrom = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DateTo = new DateTimeOffset(2024, 1, 31, 0, 0, 0, TimeSpan.Zero),
            Flag = 1,
            Limit = 100
        };

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), request, CancellationToken.None);

        page.HasMore.Should().BeTrue();

        // flag=1 selects by order date, so that axis bounds the served prefix even though
        // lastChangeDate runs the other way.
        page.CoveredUntil.Should().Be(new DateTimeOffset(2024, 1, 20, 5, 0, 0, TimeSpan.Zero));
    }

    private static OrdersRequest BuildWindowRequest()
    {
        return new OrdersRequest
        {
            DateFrom = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DateTo = new DateTimeOffset(2024, 1, 31, 0, 0, 0, TimeSpan.Zero),
            Flag = 0,
            Limit = 100
        };
    }
}
