using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.YandexKit;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// Channels that serve orders but not product cards. Ozon used to be one of them and is not any
/// more, so only its Yandex Kit counterpart remains.
/// </summary>
public class UnsupportedProductsSourceTests
{
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
