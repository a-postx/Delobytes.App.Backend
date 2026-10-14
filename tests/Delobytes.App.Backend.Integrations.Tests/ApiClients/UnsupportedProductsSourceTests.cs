using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Services.MarketplaceClients;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

public class UnsupportedProductsSourceTests
{
    [Fact]
    public async Task OzonApiClient_GetProductCardsAsync_ViaIProductsSource_ThrowsNotSupported()
    {
        Mock<IHttpClientFactory> httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new MockHttpMessageHandler().ToHttpClient());

        OzonApiClient client = new OzonApiClient(
            new HttpClient(), httpClientFactory.Object, NullLogger<OzonApiClient>.Instance);

        IProductsSource productsSource = client;

        Func<Task> act = async () => await productsSource.GetProductCardsAsync(
            Guid.NewGuid(), new ProductCardsPageRequest { Limit = 50 }, CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task YandexKitApiClient_GetProductCardsAsync_ViaIProductsSource_ThrowsNotSupported()
    {
        Mock<IHttpClientFactory> httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new MockHttpMessageHandler().ToHttpClient());

        YandexKitApiClient client = new YandexKitApiClient(
            new HttpClient(), httpClientFactory.Object, NullLogger<YandexKitApiClient>.Instance);

        IProductsSource productsSource = client;

        Func<Task> act = async () => await productsSource.GetProductCardsAsync(
            Guid.NewGuid(), new ProductCardsPageRequest { Limit = 50 }, CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
