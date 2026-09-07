using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using FluentAssertions;
using RichardSzalay.MockHttp;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

public class WildberriesApiKeyValidatorTests
{
    private const string TestUrl = "https://content-api.wildberries.ru/content/v2/get/cards/list";

    private static WildberriesApiKeyValidator BuildValidator(MockHttpMessageHandler handler)
    {
        HttpClient client = handler.ToHttpClient();
        return new WildberriesApiKeyValidator(client);
    }

    [Fact]
    public async Task ValidateAsync_Returns200_ReturnsSuccess()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(TestUrl).Respond(HttpStatusCode.OK);

        WildberriesApiKeyValidator validator = BuildValidator(mockHttp);

        ApiKeyValidationResult result = await validator.ValidateAsync(
            "valid-key", null, null, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_Returns401_ReturnsFailureWithMessage()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(TestUrl).Respond(HttpStatusCode.Unauthorized);

        WildberriesApiKeyValidator validator = BuildValidator(mockHttp);

        ApiKeyValidationResult result = await validator.ValidateAsync(
            "bad-key", null, null, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Неверный API-ключ Wildberries");
    }

    [Fact]
    public async Task ValidateAsync_Returns500_ReturnsFailureWithStatusCode()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(TestUrl).Respond(HttpStatusCode.InternalServerError);

        WildberriesApiKeyValidator validator = BuildValidator(mockHttp);

        ApiKeyValidationResult result = await validator.ValidateAsync(
            "some-key", null, null, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("500");
    }

    [Fact]
    public async Task ValidateAsync_HttpRequestException_ReturnsConnectionFailure()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(TestUrl).Throw(new HttpRequestException("Network error"));

        WildberriesApiKeyValidator validator = BuildValidator(mockHttp);

        ApiKeyValidationResult result = await validator.ValidateAsync(
            "some-key", null, null, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Не удалось подключиться к Wildberries");
    }

    [Fact]
    public async Task ValidateAsync_TaskCanceledException_ReturnsConnectionFailure()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(TestUrl).Throw(new TaskCanceledException("Timeout"));

        WildberriesApiKeyValidator validator = BuildValidator(mockHttp);

        ApiKeyValidationResult result = await validator.ValidateAsync(
            "some-key", null, null, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Не удалось подключиться к Wildberries");
    }

    [Fact]
    public async Task ValidateAsync_SendsCorrectAuthorizationHeader()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();

        mockHttp
            .When(TestUrl)
            .WithHeaders("Authorization", "Bearer my-secret-key")
            .Respond(HttpStatusCode.OK);

        // Любой другой запрос (без правильного заголовка) → 401
        mockHttp.When(TestUrl).Respond(HttpStatusCode.Unauthorized);

        WildberriesApiKeyValidator validator = BuildValidator(mockHttp);

        ApiKeyValidationResult result = await validator.ValidateAsync(
            "my-secret-key", null, null, CancellationToken.None);

        result.IsValid.Should().BeTrue();
    }
}
