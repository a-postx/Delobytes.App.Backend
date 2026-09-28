using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

public class ChannelApiClientFactoryTests
{
    private static SystemChannelTemplate BuildTemplate(string code)
    {
        return new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = code,
            DisplayName = code,
            ApiBaseUrl = "https://example.com",
            ApiVersion = "v1",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Собирает контейнер с реальными клиентами и заглушкой HTTP,
    /// обращений к сети при этом не происходит.
    /// </summary>
    private static ServiceProvider BuildProvider(MockHttpMessageHandler mockHttp)
    {
        HttpClient httpClient = mockHttp.ToHttpClient();

        Mock<IHttpClientFactory> factory = new Mock<IHttpClientFactory>();
        factory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        Mock<IEndpointResolver> resolver = new Mock<IEndpointResolver>();
        resolver
            .Setup(r => r.GetEndpointUrlAsync(
                It.IsAny<Guid>(),
                It.IsAny<ChannelEndpointType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://common-api.wildberries.ru");

        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(factory.Object);
        services.AddSingleton(resolver.Object);

        services.AddTransient(sp => new WildberriesApiClient(
            httpClient,
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IEndpointResolver>(),
            NullLogger<WildberriesApiClient>.Instance));

        services.AddTransient(sp => new OzonApiClient(
            httpClient,
            sp.GetRequiredService<IHttpClientFactory>(),
            NullLogger<OzonApiClient>.Instance));

        services.AddTransient(sp => new YandexKitApiClient(
            httpClient,
            sp.GetRequiredService<IHttpClientFactory>(),
            NullLogger<YandexKitApiClient>.Instance));

        services.AddTransient<IChannelApiClientFactory, ChannelApiClientFactory>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Create_WildberriesTemplate_ReturnsClientBoundToThatTemplate()
    {
        SystemChannelTemplate template = BuildTemplate("wildberries");
        string sellerInfoUrl = "https://common-api.wildberries.ru/api/v1/seller-info";
        string json = """{ "name": "ООО Ромашка", "tin": "7701234567", "trademark": "Ромашка Маркет" }""";

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, sellerInfoUrl).Respond("application/json", json);

        Mock<IEndpointResolver> resolver = new Mock<IEndpointResolver>();
        resolver
            .Setup(r => r.GetEndpointUrlAsync(
                template.Id,
                ChannelEndpointType.Common,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://common-api.wildberries.ru");

        HttpClient httpClient = mockHttp.ToHttpClient();

        Mock<IHttpClientFactory> httpFactory = new Mock<IHttpClientFactory>();
        httpFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(httpFactory.Object);
        services.AddSingleton(resolver.Object);
        services.AddTransient(sp => new WildberriesApiClient(
            httpClient,
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IEndpointResolver>(),
            NullLogger<WildberriesApiClient>.Instance));
        services.AddTransient<IChannelApiClientFactory, ChannelApiClientFactory>();

        using ServiceProvider provider = services.BuildServiceProvider();
        IChannelApiClientFactory factory = provider.GetRequiredService<IChannelApiClientFactory>();

        IChannelApiClient client = factory.Create(template);

        client.Should().BeOfType<WildberriesApiClient>();

        // Главное утверждение: клиент из фабрики уже привязан к шаблону
        // и обращение к API не падает с требованием предварительной настройки.
        AccountInfo? accountInfo = await client.GetAccountInfoAsync(
            "test-key", null, null, CancellationToken.None);

        accountInfo.Should().NotBeNull();
        accountInfo!.CustomerName.Should().Be("Ромашка Маркет");
        accountInfo.Inn.Should().Be("7701234567");

        resolver.Verify(
            r => r.GetEndpointUrlAsync(
                template.Id,
                ChannelEndpointType.Common,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void Create_WildberriesTemplate_CodeIsCaseInsensitive()
    {
        SystemChannelTemplate template = BuildTemplate("WildBerries");

        using ServiceProvider provider = BuildProvider(new MockHttpMessageHandler());
        IChannelApiClientFactory factory = provider.GetRequiredService<IChannelApiClientFactory>();

        IChannelApiClient client = factory.Create(template);

        client.Should().BeOfType<WildberriesApiClient>();
    }

    [Fact]
    public void Create_OzonTemplate_ReturnsOzonClient()
    {
        SystemChannelTemplate template = BuildTemplate("ozon");

        using ServiceProvider provider = BuildProvider(new MockHttpMessageHandler());
        IChannelApiClientFactory factory = provider.GetRequiredService<IChannelApiClientFactory>();

        IChannelApiClient client = factory.Create(template);

        client.Should().BeOfType<OzonApiClient>();
    }

    [Fact]
    public void Create_YandexKitTemplate_ReturnsYandexKitClient()
    {
        SystemChannelTemplate template = BuildTemplate("yandex.kit");

        using ServiceProvider provider = BuildProvider(new MockHttpMessageHandler());
        IChannelApiClientFactory factory = provider.GetRequiredService<IChannelApiClientFactory>();

        IChannelApiClient client = factory.Create(template);

        client.Should().BeOfType<YandexKitApiClient>();
    }

    [Fact]
    public void Create_UnsupportedChannel_ThrowsNotSupportedException()
    {
        SystemChannelTemplate template = BuildTemplate("unknown-marketplace");

        using ServiceProvider provider = BuildProvider(new MockHttpMessageHandler());
        IChannelApiClientFactory factory = provider.GetRequiredService<IChannelApiClientFactory>();

        Action act = () => factory.Create(template);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*unknown-marketplace*");
    }

    [Fact]
    public void Create_NullTemplate_ThrowsArgumentNullException()
    {
        using ServiceProvider provider = BuildProvider(new MockHttpMessageHandler());
        IChannelApiClientFactory factory = provider.GetRequiredService<IChannelApiClientFactory>();

        Action act = () => factory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
