using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// Tests for <see cref="OzonApiClient.GetProductCardsAsync(Guid, ProductCardsCursor?, int, CancellationToken)"/>:
/// one attributes page per request, the paging contract, the card mapping (package dimensions, the
/// description attribute, photos) and the pacing that keeps a multi-page run alive.
/// </summary>
public class OzonApiClientGetProductCardsTests
{
    private const string ApiBaseUrl = "https://api-seller.ozon.ru";
    private const string AttributesUrl = ApiBaseUrl + "/v4/product/info/attributes";

    private readonly Guid _connectionId = Guid.NewGuid();
    private readonly Connection _connection;

    public OzonApiClientGetProductCardsTests()
    {
        _connection = new Connection
        {
            Id = _connectionId,
            ChannelId = Guid.NewGuid(),
            Name = "Ozon",
            ApiKey = "test-key",
            Settings = JsonSerializer.Serialize(new { sellerId = "12345" }),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public async Task GetProductCards_FirstPage_SendsVisibilityLimitAndSortWithoutLastId()
    {
        string? requestBody = null;

        (OzonApiClient client, MockHttpMessageHandler mockHttp) = BuildClient(_connection);
        mockHttp
            .When(HttpMethod.Post, AttributesUrl)
            .With(request =>
            {
                requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond("application/json", Payload());

        await client.GetProductCardsAsync(_connectionId, cursor: null, limit: 500, CancellationToken.None);

        using JsonDocument body = JsonDocument.Parse(requestBody!);
        body.RootElement.GetProperty("filter").GetProperty("visibility").GetString().Should().Be("VISIBLE");
        body.RootElement.GetProperty("limit").GetInt32().Should().Be(500);
        body.RootElement.GetProperty("sort_dir").GetString().Should().Be("ASC");
        body.RootElement.TryGetProperty("last_id", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetProductCards_LimitAboveCeiling_IsClampedToThousand()
    {
        string? requestBody = null;

        (OzonApiClient client, MockHttpMessageHandler mockHttp) = BuildClient(_connection);
        mockHttp
            .When(HttpMethod.Post, AttributesUrl)
            .With(request =>
            {
                requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond("application/json", Payload());

        await client.GetProductCardsAsync(_connectionId, cursor: null, limit: 5000, CancellationToken.None);

        using JsonDocument body = JsonDocument.Parse(requestBody!);
        body.RootElement.GetProperty("limit").GetInt32().Should().Be(1000);
    }

    [Fact]
    public async Task GetProductCards_CursorAndLastId_MapBothWays()
    {
        string? requestBody = null;

        (OzonApiClient client, MockHttpMessageHandler mockHttp) = BuildClient(_connection);
        mockHttp
            .When(HttpMethod.Post, AttributesUrl)
            .With(request =>
            {
                requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond("application/json", Payload(lastId: "next-token"));

        ApiResponse<ProductCardsData> response = await client.GetProductCardsAsync(
            _connectionId,
            new ProductCardsCursor { Token = "current-token" },
            100,
            CancellationToken.None);

        using JsonDocument body = JsonDocument.Parse(requestBody!);
        body.RootElement.GetProperty("last_id").GetString().Should().Be("current-token");

        response.Data!.NextCursor!.Token.Should().Be("next-token");

        // An empty last_id is the end of the catalogue, not a cursor.
        (OzonApiClient secondClient, MockHttpMessageHandler secondMock) = BuildClient(_connection);
        secondMock
            .When(HttpMethod.Post, AttributesUrl)
            .Respond("application/json", Payload(lastId: string.Empty));

        ApiResponse<ProductCardsData> endOfCatalogue = await secondClient.GetProductCardsAsync(
            _connectionId, cursor: null, limit: 100, CancellationToken.None);

        endOfCatalogue.Data!.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GetProductCards_MapsTheMarketplaceArticleNotTheInternalId()
    {
        ChannelProductCard card = await GetSingleCardAsync(Payload());

        card.ExternalProductId.Should().Be("1088660899");
        card.OfferId.Should().Be("квр-10");
    }

    [Fact]
    public async Task GetProductCards_NameComesFromTopLevelAndDescriptionFromAttribute4191()
    {
        ChannelProductCard card = await GetSingleCardAsync(Payload());

        card.Name.Should().Be("Top level name");
        card.Description.Should().Be("Right description");
        card.Description.Should().NotBe("Wrong description");
    }

    [Fact]
    public async Task GetProductCards_ConvertsPackageDimensionsAndWeight()
    {
        ChannelProductCard card = await GetSingleCardAsync(Payload());

        card.LengthCm.Should().Be(57m);
        card.WidthCm.Should().Be(24m);
        card.HeightCm.Should().Be(4m);
        card.WeightKg.Should().Be(0.45m);
    }

    [Fact]
    public async Task GetProductCards_IgnoresProductMeasurementAttributes()
    {
        // Attributes 8415/6433/6469/4082/4497/8962 carry the product's own measurements and would
        // break the assertions above if the mapper read them.
        ChannelProductCard card = await GetSingleCardAsync(Payload());

        card.LengthCm.Should().Be(57m);
        card.WidthCm.Should().Be(24m);
        card.HeightCm.Should().Be(4m);
    }

    [Fact]
    public async Task GetProductCards_PhotosStartWithPrimaryImageAndPairBothVariants()
    {
        ChannelProductCard card = await GetSingleCardAsync(Payload());

        card.Photos.Should().HaveCount(14);
        card.Photos[0].Url.Should().Be("https://cdn.example/primary.jpg");
        card.Photos.Count(photo => photo.Url == "https://cdn.example/primary.jpg").Should().Be(2);

        List<string> urls = card.Photos.Select(photo => photo.Url).Distinct().ToList();
        urls.Should().HaveCount(7);

        card.Photos.Select(photo => photo.DisplayOrder).Distinct().Should().Equal(1, 2, 3, 4, 5, 6, 7);

        foreach (string url in urls)
        {
            card.Photos.Where(photo => photo.Url == url).Select(photo => photo.SizeVariant)
                .Should().BeEquivalentTo(new[] { "large", "thumbnail" });
        }
    }

    [Fact]
    public async Task GetProductCards_PrimaryImageAlsoInImages_IsEmittedOnce()
    {
        string payload = Payload(images:
        [
            "https://cdn.example/primary.jpg",
            "https://cdn.example/1.jpg",
        ]);

        ChannelProductCard card = await GetSingleCardAsync(payload);

        card.Photos.Count(photo => photo.Url == "https://cdn.example/primary.jpg").Should().Be(2);
        card.Photos.Select(photo => photo.DisplayOrder).Distinct().Should().Equal(1, 2);
    }

    [Fact]
    public async Task GetProductCards_BarcodesFallBackToTheSingleBarcode()
    {
        ChannelProductCard withList = await GetSingleCardAsync(Payload(barcodes: ["111", "222"], barcode: "999"));
        withList.Barcodes.Should().Equal("111", "222");

        ChannelProductCard withSingle = await GetSingleCardAsync(Payload(barcodes: [], barcode: "999"));
        withSingle.Barcodes.Should().Equal("999");
    }

    [Fact]
    public async Task GetProductCards_CardWithoutSku_IsSkipped()
    {
        (OzonApiClient client, MockHttpMessageHandler mockHttp) = BuildClient(_connection);
        mockHttp
            .When(HttpMethod.Post, AttributesUrl)
            .Respond("application/json", Payload(sku: 0));

        ApiResponse<ProductCardsData> response = await client.GetProductCardsAsync(
            _connectionId, cursor: null, limit: 100, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Cards.Should().BeEmpty(
            "a card without a marketplace article cannot be keyed and would collide on \"0\"");
    }

    [Fact]
    public async Task GetProductCards_TooManyRequests_ReturnsFailureAndIssuesNoFurtherRequest()
    {
        int requests = 0;

        Mock<IChannelRateLimiter> rateLimiter = BuildAllowingLimiter();
        rateLimiter
            .Setup(l => l.ReportRateLimitedAsync(It.IsAny<RateLimitScope>(), It.IsAny<RateLimitSignal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTimeOffset.UtcNow.AddSeconds(2));

        (OzonApiClient client, MockHttpMessageHandler mockHttp) = BuildClient(_connection, rateLimiter);

        mockHttp
            .When(HttpMethod.Post, AttributesUrl)
            .With(_ =>
            {
                requests++;
                return true;
            })
            .Respond(HttpStatusCode.TooManyRequests, "application/json", "{}");

        ApiResponse<ProductCardsData> response = await client.GetProductCardsAsync(
            _connectionId, cursor: null, limit: 100, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.ErrorMessage.Should().Contain("Rate limit");
        requests.Should().Be(1);

        rateLimiter.Verify(
            l => l.ReportRateLimitedAsync(It.IsAny<RateLimitScope>(), It.IsAny<RateLimitSignal>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProductCards_StampsTheChannelCode()
    {
        ChannelProductCard card = await GetSingleCardAsync(Payload());

        card.ChannelCode.Should().Be("ozon");
        card.BarcodeType.Should().Be("ozon");
    }

    [Fact]
    public async Task GetProductCards_LimiterRefusesBriefly_WaitsAndIssuesOneRequest()
    {
        int requests = 0;
        int acquireCalls = 0;

        Mock<IChannelRateLimiter> rateLimiter = new Mock<IChannelRateLimiter>();
        rateLimiter
            .Setup(l => l.TryAcquireAsync(It.IsAny<RateLimitScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                acquireCalls++;

                return acquireCalls == 1
                    ? new RateLimitAcquireResult { Acquired = false, NextRequestAllowedAt = DateTimeOffset.UtcNow.AddMilliseconds(100) }
                    : new RateLimitAcquireResult { Acquired = true, NextRequestAllowedAt = DateTimeOffset.UtcNow };
            });
        rateLimiter
            .Setup(l => l.ReportSuccessAsync(It.IsAny<RateLimitScope>(), It.IsAny<RateLimitSignal>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        (OzonApiClient client, MockHttpMessageHandler mockHttp) = BuildClient(_connection, rateLimiter);
        mockHttp
            .When(HttpMethod.Post, AttributesUrl)
            .With(_ =>
            {
                requests++;
                return true;
            })
            .Respond("application/json", Payload());

        ApiResponse<ProductCardsData> response = await client.GetProductCardsAsync(
            _connectionId, cursor: null, limit: 100, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        requests.Should().Be(1);
        acquireCalls.Should().Be(2);
    }

    [Fact]
    public async Task GetProductCards_RefusalPastTolerance_ReturnsFailureWithoutRequesting()
    {
        DateTimeOffset unavailableUntil = DateTimeOffset.UtcNow.AddHours(1);
        int requests = 0;

        Mock<IChannelRateLimiter> rateLimiter = new Mock<IChannelRateLimiter>();
        rateLimiter
            .Setup(l => l.TryAcquireAsync(It.IsAny<RateLimitScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitAcquireResult { Acquired = false, NextRequestAllowedAt = unavailableUntil });

        (OzonApiClient client, MockHttpMessageHandler mockHttp) = BuildClient(_connection, rateLimiter);
        mockHttp
            .When(HttpMethod.Post, AttributesUrl)
            .With(_ =>
            {
                requests++;
                return true;
            })
            .Respond("application/json", Payload());

        ApiResponse<ProductCardsData> response = await client.GetProductCardsAsync(
            _connectionId, cursor: null, limit: 100, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.ErrorMessage.Should().Contain(unavailableUntil.ToString("O"));
        requests.Should().Be(0, "a refusal beyond the tolerance must not spend an HTTP request");
    }

    private async Task<ChannelProductCard?> GetSingleCardAsync(string payload)
    {
        (OzonApiClient client, MockHttpMessageHandler mockHttp) = BuildClient(_connection);
        mockHttp
            .When(HttpMethod.Post, AttributesUrl)
            .Respond("application/json", payload);

        ApiResponse<ProductCardsData> response = await client.GetProductCardsAsync(
            _connectionId, cursor: null, limit: 100, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Cards.Should().HaveCountLessOrEqualTo(1);

        return response.Data.Cards.SingleOrDefault();
    }

    private (OzonApiClient Client, MockHttpMessageHandler MockHttp) BuildClient(
        Connection connection,
        Mock<IChannelRateLimiter>? rateLimiter = null)
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();

        Mock<IConnectionResolver> resolver = new Mock<IConnectionResolver>();
        resolver
            .Setup(r => r.ResolveByIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        OzonAuthHandler authHandler = new OzonAuthHandler(
            resolver.Object, NullLogger<OzonAuthHandler>.Instance)
        {
            InnerHandler = mockHttp,
        };

        OzonApiClient client = new OzonApiClient(
            new HttpClient(authHandler),
            Mock.Of<IHttpClientFactory>(),
            resolver.Object,
            (rateLimiter ?? BuildAllowingLimiter()).Object,
            NullLogger<OzonApiClient>.Instance,
            TimeProvider.System);

        client.SetTemplate(new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "ozon",
            DisplayName = "Ozon",
            ApiBaseUrl = ApiBaseUrl,
            ApiVersion = "v1",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        return (client, mockHttp);
    }

    private static Mock<IChannelRateLimiter> BuildAllowingLimiter()
    {
        Mock<IChannelRateLimiter> rateLimiter = new Mock<IChannelRateLimiter>();
        rateLimiter
            .Setup(l => l.TryAcquireAsync(It.IsAny<RateLimitScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitAcquireResult { Acquired = true, NextRequestAllowedAt = DateTimeOffset.UtcNow });
        rateLimiter
            .Setup(l => l.ReportSuccessAsync(It.IsAny<RateLimitScope>(), It.IsAny<RateLimitSignal>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return rateLimiter;
    }

    private static string Payload(
        long sku = 1088660899,
        string? lastId = null,
        List<string>? barcodes = null,
        string barcode = "4600000000001",
        List<string>? images = null)
    {
        List<string> imageList = images ??
        [
            "https://cdn.example/1.jpg",
            "https://cdn.example/2.jpg",
            "https://cdn.example/3.jpg",
            "https://cdn.example/4.jpg",
            "https://cdn.example/5.jpg",
            "https://cdn.example/6.jpg",
        ];

        object payload = new
        {
            result = new[]
            {
                new
                {
                    id = 577954855,
                    sku,
                    offer_id = "квр-10",
                    name = "Top level name",
                    barcode,
                    barcodes = barcodes ?? ["4600000000001", "4600000000002"],
                    depth = 570,
                    width = 240,
                    height = 40,
                    dimension_unit = "mm",
                    weight = 450,
                    weight_unit = "g",
                    primary_image = "https://cdn.example/primary.jpg",
                    images = imageList,
                    attributes = new object[]
                    {
                        new
                        {
                            id = 4180,
                            complex_id = 0,
                            values = new[] { new { dictionary_value_id = 0, value = "Wrong description" } },
                        },
                        new
                        {
                            id = 4191,
                            complex_id = 0,
                            values = new[] { new { dictionary_value_id = 0, value = "Right description" } },
                        },
                        new { id = 8415, complex_id = 0, values = new[] { new { dictionary_value_id = 0, value = "55" } } },
                        new { id = 6433, complex_id = 0, values = new[] { new { dictionary_value_id = 0, value = "111" } } },
                        new { id = 6469, complex_id = 0, values = new[] { new { dictionary_value_id = 0, value = "222" } } },
                        new { id = 4082, complex_id = 0, values = new[] { new { dictionary_value_id = 0, value = "58х30х4" } } },
                        new { id = 4497, complex_id = 0, values = new[] { new { dictionary_value_id = 0, value = "333" } } },
                        new { id = 8962, complex_id = 0, values = new[] { new { dictionary_value_id = 0, value = "444" } } },
                    },
                },
            },
            total = 1,
            last_id = lastId ?? string.Empty,
        };

        return JsonSerializer.Serialize(payload);
    }
}
