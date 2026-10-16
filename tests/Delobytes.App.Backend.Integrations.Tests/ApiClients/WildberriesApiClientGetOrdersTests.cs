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

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{StatisticsBaseUrl}/api/v1/supplier/orders")
            .Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp);

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
        order.ExternalOrderId.Should().Be("SRID-1");
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
                "srid": "SRID-2"
            }
        ]
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{StatisticsBaseUrl}/api/v1/supplier/orders")
            .Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp);

        OrdersRequest request = new OrdersRequest
        {
            DateFrom = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DateTo = new DateTimeOffset(2024, 1, 31, 0, 0, 0, TimeSpan.Zero),
            Flag = 0,
            Limit = 100
        };

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), request, CancellationToken.None);

        ChannelOrderSnapshot order = page.Orders.Single();
        order.ExternalStatus.Should().Be("cancelled");
        order.CancelReason.Should().Be("cancel");
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
                "srid": "IN-WINDOW"
            },
            {
                "date": "2024-02-15T08:00:00",
                "lastChangeDate": "2024-02-15T09:30:00",
                "nmId": 2,
                "isCancel": false,
                "srid": "OUT-OF-WINDOW"
            }
        ]
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{StatisticsBaseUrl}/api/v1/supplier/orders")
            .Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp);

        OrdersRequest request = new OrdersRequest
        {
            DateFrom = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DateTo = new DateTimeOffset(2024, 1, 31, 0, 0, 0, TimeSpan.Zero),
            Flag = 0,
            Limit = 100
        };

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), request, CancellationToken.None);

        page.Orders.Should().ContainSingle(o => o.ExternalOrderId == "IN-WINDOW");
    }

    [Fact]
    public async Task GetOrdersAsync_RequestLimitLowerThanResultCount_ReturnsEveryRow()
    {
        string json = """
        [
            { "date": "2024-01-10T08:00:00", "lastChangeDate": "2024-01-10T09:30:00", "nmId": 1, "isCancel": false, "srid": "A" },
            { "date": "2024-01-11T08:00:00", "lastChangeDate": "2024-01-11T09:30:00", "nmId": 2, "isCancel": false, "srid": "B" }
        ]
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{StatisticsBaseUrl}/api/v1/supplier/orders")
            .Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp);

        OrdersRequest request = new OrdersRequest
        {
            DateFrom = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DateTo = new DateTimeOffset(2024, 1, 31, 0, 0, 0, TimeSpan.Zero),
            Flag = 0,
            Limit = 1
        };

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), request, CancellationToken.None);

        page.Orders.Should().HaveCount(2);
        page.Orders.Select(o => o.ExternalOrderId).Should().BeEquivalentTo(new[] { "A", "B" });
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
}
