using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Orders;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;
using Delobytes.App.Backend.Sales.Application.Interfaces;
using Delobytes.App.Backend.Sales.Application.Orders;
using Delobytes.App.Backend.Sales.Application.Services;
using Delobytes.App.Backend.Sales.Domain.Entities;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Products;

/// <summary>
/// Pins the identifier chain that joins the products import and the orders import: Ozon's card
/// <c>sku</c> becomes <c>ChannelProduct.ExternalProductId</c>, a link event carries it into Sales'
/// <c>ChannelProductRef</c>, and an order line is linked by that value alone. Using the internal
/// marketplace <c>id</c> anywhere in the chain breaks the join silently — orders keep importing and
/// simply never link — which is what these tests exist to prevent.
/// </summary>
public class OzonProductOrderIdentifierCompatibilityTests
{
    private const long Sku = 1088660899;
    private const string CardId = "1088660899";

    [Fact]
    public async Task CardAndOrderMappers_ProduceTheSameExternalProductId()
    {
        string cardId = await ReadCardExternalProductIdAsync();
        string orderLineId = await ReadOrderLineExternalProductIdAsync();

        cardId.Should().Be(CardId);
        orderLineId.Should().Be(cardId);
        orderLineId.Should().NotBe("577954855", "the internal marketplace id must never become the join key");
    }

    [Fact]
    public async Task Identifier_IsCultureInvariant()
    {
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            // ru-RU groups numbers with a non-breaking space; a culture-sensitive ToString would
            // emit "1 088 660 899" and break every later join.
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");

            string cardId = await ReadCardExternalProductIdAsync();
            string orderLineId = await ReadOrderLineExternalProductIdAsync();

            cardId.Should().Be(CardId);
            orderLineId.Should().Be(CardId);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task LinkEvent_FeedsTheSalesProjectionUsedByOrderLines()
    {
        Guid channelProductId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();

        string databaseName = Guid.NewGuid().ToString();
        Guid tenantId = Guid.NewGuid();

        SalesDbContext context = BuildSalesContext(databaseName, tenantId);

        ChannelProductRefRepository repository = new ChannelProductRefRepository(context);
        ChannelProductLinker linker = new ChannelProductLinker(
            repository,
            NullLogger<ChannelProductLinker>.Instance,
            TimeProvider.System);

        await linker.UpdateChannelProductRefAsync(
            new ChannelProductLinkedEvent
            {
                ChannelProductId = channelProductId,
                ProductId = productId,
                ChannelId = channelId,
                ExternalProductId = CardId,
                ExternalSku = "квр-10",
                Sku = "SKU-1",
                ProductName = "Product",
            },
            CancellationToken.None);

        OrdersImportStore store = new OrdersImportStore(
            BuildSalesContext(databaseName, tenantId),
            NullLogger<OrdersImportStore>.Instance);

        Dictionary<string, Guid> ids = await store.LoadChannelProductIdsAsync(
            channelId,
            new[] { CardId },
            CancellationToken.None);

        ids.Should().ContainKey(CardId);
        ids[CardId].Should().Be(channelProductId);
    }

    [Fact]
    public async Task OrderLineWithoutProjectionRow_StaysUnlinkedAndIsStillPersisted()
    {
        RecordingOrdersImportStore store = new RecordingOrdersImportStore();

        Mock<Delobytes.App.Backend.Sales.Application.Interfaces.IEventPublisher> eventPublisher =
            new Mock<Delobytes.App.Backend.Sales.Application.Interfaces.IEventPublisher>();
        eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<OrdersBatchImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<ITenantContext> tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns(Guid.NewGuid());

        OrdersBatchImporter importer = new OrdersBatchImporter(
            store,
            eventPublisher.Object,
            tenantContext.Object,
            NullLogger<OrdersBatchImporter>.Instance,
            TimeProvider.System);

        ChannelOrderSnapshot snapshot = new ChannelOrderSnapshot
        {
            ExternalOrderId = "order-1",
            OrderDate = DateTimeOffset.UtcNow.AddDays(-1),
            ExternalStatus = "delivered",
            Currency = "RUB",
            Lines = new List<ChannelOrderLineSnapshot>
            {
                new ChannelOrderLineSnapshot
                {
                    LineKey = "line-1",
                    ExternalProductId = CardId,
                    OfferId = "квр-10",
                    Quantity = 1,
                    Currency = "RUB",
                },
            },
        };

        await importer.ProcessAsync(
            new OrdersBatchImportRequestedEvent
            {
                JobRunId = Guid.NewGuid(),
                ConnectionId = Guid.NewGuid(),
                ChannelId = Guid.NewGuid(),
                BatchId = Guid.NewGuid(),
                Orders = new List<ChannelOrderSnapshot> { snapshot },
            },
            CancellationToken.None);

        store.AddedLines.Should().ContainSingle("the line is persisted even when it cannot be linked");
        store.AddedLines[0].ExternalProductId.Should().Be(CardId);
        store.AddedLines[0].ChannelProductId.Should().BeNull("there is no fallback match through OfferId");
    }

    private static async Task<string> ReadCardExternalProductIdAsync()
    {
        Mock<IConnectionResolver> resolver = new Mock<IConnectionResolver>();
        Connection connection = BuildConnection();
        resolver
            .Setup(r => r.ResolveByIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        OzonApiClient client = BuildClient(resolver.Object, connection, BuildAttributesPayload());

        ApiResponse<ProductCardsData> response = await client.GetProductCardsAsync(
            connection.Id,
            cursor: null,
            limit: 100,
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Cards.Should().ContainSingle();

        return response.Data.Cards.Single().ExternalProductId!;
    }

    private static Task<string> ReadOrderLineExternalProductIdAsync()
    {
        // The mapper itself, not an HTTP round trip: this test is about the identifier it emits,
        // and a JSON fixture would only add a second place for the value to be wrong.
        OzonMappingResult result = OzonOrderMapper.Map(
            new List<OzonPosting>
            {
                new OzonPosting
                {
                    PostingNumber = "posting-1",
                    OrderId = 123,
                    OrderNumber = "123",
                    Status = "delivered",
                    CreatedAt = new DateTimeOffset(2024, 3, 1, 10, 0, 0, TimeSpan.Zero),
                    Products = new List<OzonPostingProduct>
                    {
                        new OzonPostingProduct
                        {
                            Sku = Sku,
                            OfferId = "квр-10",
                            Name = "Product",
                            Quantity = 1,
                            Price = new OzonMoney { Amount = 880m, Currency = "RUB" },
                        },
                    },
                },
            },
            OrdersSchema.Fbo,
            logger: null);

        result.Orders.Should().ContainSingle();
        result.Orders[0].Lines.Should().ContainSingle();

        return Task.FromResult(result.Orders[0].Lines[0].ExternalProductId);
    }

    private static OzonApiClient BuildClient(
        IConnectionResolver resolver,
        Connection connection,
        string responsePayload)
    {
        StubHandler handler = new StubHandler(responsePayload);

        Mock<IChannelRateLimiter> rateLimiter = new Mock<IChannelRateLimiter>();
        rateLimiter
            .Setup(l => l.TryAcquireAsync(It.IsAny<RateLimitScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitAcquireResult { Acquired = true, NextRequestAllowedAt = DateTimeOffset.UtcNow });
        rateLimiter
            .Setup(l => l.ReportSuccessAsync(It.IsAny<RateLimitScope>(), It.IsAny<RateLimitSignal>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        OzonApiClient client = new OzonApiClient(
            new HttpClient(handler),
            Mock.Of<IHttpClientFactory>(),
            resolver,
            rateLimiter.Object,
            NullLogger<OzonApiClient>.Instance,
            TimeProvider.System);

        client.SetTemplate(new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "ozon",
            DisplayName = "Ozon",
            ApiBaseUrl = "https://api-seller.ozon.ru",
            ApiVersion = "v1",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        return client;
    }

    private static Connection BuildConnection()
    {
        return new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            Name = "Ozon",
            ApiKey = "test-key",
            Settings = JsonSerializer.Serialize(new { sellerId = "12345" }),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static SalesDbContext BuildSalesContext(string databaseName, Guid tenantId)
    {
        DbContextOptions<SalesDbContext> options = new DbContextOptionsBuilder<SalesDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Mock<ITenantContext> tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns(tenantId);

        return new SalesDbContext(options, tenantContext.Object);
    }

    private static string BuildAttributesPayload()
    {
        return $$"""
        {
          "result": [
            {
              "id": 577954855,
              "sku": {{Sku}},
              "offer_id": "квр-10",
              "name": "Product",
              "barcode": "4600000000001",
              "barcodes": ["4600000000001"],
              "depth": 570,
              "width": 240,
              "height": 40,
              "dimension_unit": "mm",
              "weight": 450,
              "weight_unit": "g",
              "primary_image": null,
              "images": [],
              "attributes": []
            }
          ],
          "total": 1,
          "last_id": ""
        }
        """;
    }

    private static string BuildPostingsPayload()
    {
        return $$"""
        {
          "result": {
            "postings": [
              {
                "posting_number": "posting-1",
                "order_id": 123,
                "order_number": "123",
                "status": "delivered",
                "created_at": "2024-03-01T10:00:00Z",
                "products": [
                  {
                    "sku": {{Sku}},
                    "offer_id": "квр-10",
                    "name": "Product",
                    "quantity": 1,
                    "price": { "amount": "880", "currency": "RUB" }
                  }
                ],
                "analytics_data": { "currency": "RUB" }
              }
            ]
          }
        }
        """;
    }

    /// <summary>
    /// Minimal store double: it records the staged lines and answers "nothing is linked yet", which
    /// is the state these tests need to observe the importer's own linking decision.
    /// </summary>
    private sealed class RecordingOrdersImportStore : IOrdersImportStore
    {
        public List<OrderLine> AddedLines { get; } = new();

        public Task<Dictionary<string, Guid>> LoadChannelProductIdsAsync(
            Guid channelId,
            IReadOnlyCollection<string> externalProductIds,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new Dictionary<string, Guid>());
        }

        public Task<Order?> FindOrderAsync(
            Guid channelId,
            string externalOrderId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<Order?>(null);
        }

        public void AddOrder(Order order)
        {
        }

        public void AddOrderLine(OrderLine line)
        {
            AddedLines.Add(line);
        }

        public void DetectChanges()
        {
        }

        public bool HasMeaningfulOrderChanges(Order order)
        {
            return false;
        }

        public bool HasMeaningfulLineChanges(Order order)
        {
            return false;
        }

        public Task<TResult> ExecuteWithSaveAsync<TResult>(
            Func<Task<TResult>> operation,
            string externalOrderId,
            CancellationToken cancellationToken)
        {
            return operation();
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _responsePayload;

        public StubHandler(string responsePayload)
        {
            _responsePayload = responsePayload;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_responsePayload, Encoding.UTF8, "application/json"),
            });
        }
    }
}
