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

public class WildberriesApiClientProductsSourceTests
{
    private const string ContentBaseUrl = "https://content-api.wildberries.ru";
    private const string GetCardsListUrl = "https://content-api.wildberries.ru/content/v2/get/cards/list";

    private static readonly Guid TestTemplateId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly SystemChannelTemplate TestTemplate = new SystemChannelTemplate
    {
        Id = TestTemplateId,
        Code = "wildberries",
        DisplayName = "Wildberries",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static WildberriesApiClient BuildClient(MockHttpMessageHandler mockHttp)
    {
        HttpClient typedClient = mockHttp.ToHttpClient();

        Mock<IEndpointResolver> endpointResolver = new Mock<IEndpointResolver>();
        endpointResolver
            .Setup(r => r.GetEndpointUrlAsync(
                TestTemplateId,
                ChannelEndpointType.Content,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentBaseUrl);

        WildberriesApiClient client = new WildberriesApiClient(
            typedClient,
            endpointResolver.Object,
            Options.Create(new WildberriesImportOptions()),
            NullLogger<WildberriesApiClient>.Instance);

        client.SetTemplate(TestTemplate);

        return client;
    }

    [Fact]
    public async Task GetProductCardsAsync_ViaIProductsSource_ReturnsPageWithCursor()
    {
        string json = """
        {
            "cards": [
                { "nmID": 111, "vendorCode": "V-1", "title": "Товар" }
            ],
            "cursor": {
                "updatedAt": "2024-01-15T12:30:00Z",
                "nmID": 111,
                "total": 1
            }
        }
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp);
        IProductsSource productsSource = client;

        ProductCardsPage page = await productsSource.GetProductCardsAsync(
            Guid.NewGuid(),
            new ProductCardsPageRequest { Cursor = null, Limit = 50 },
            CancellationToken.None);

        page.Should().NotBeNull();
        page.Cards.Should().HaveCount(1);
        page.Cards.Single().NmId.Should().Be(111);
        page.TotalCount.Should().Be(1);
        page.NextCursor.Should().NotBeNull();
        page.NextCursor!.ProductId.Should().Be(111);
    }

    [Fact]
    public async Task GetProductCardsAsync_ViaIProductsSource_OnAuthFailure_Throws()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond(HttpStatusCode.Unauthorized);

        WildberriesApiClient client = BuildClient(mockHttp);
        IProductsSource productsSource = client;

        Func<Task> act = async () => await productsSource.GetProductCardsAsync(
            Guid.NewGuid(),
            new ProductCardsPageRequest { Cursor = null, Limit = 50 },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetProductCardsAsync_ViaIProductsSource_NullRequest_Throws()
    {
        WildberriesApiClient client = BuildClient(new MockHttpMessageHandler());
        IProductsSource productsSource = client;

        Func<Task> act = async () => await productsSource.GetProductCardsAsync(
            Guid.NewGuid(), null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
