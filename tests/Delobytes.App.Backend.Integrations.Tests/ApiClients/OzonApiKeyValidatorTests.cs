using System.Collections.Generic;
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

public class OzonApiKeyValidatorTests
{
    private const string TestUrl = "https://api-seller.ozon.ru/v3/product/list";

    private static OzonApiKeyValidator BuildValidator(MockHttpMessageHandler handler)
    {
        HttpClient client = handler.ToHttpClient();
        return new OzonApiKeyValidator(client);
    }

    private static Dictionary<string, string> ValidSettings()
    {
        return new Dictionary<string, string> { ["sellerId"] = "12345" };
    }

    [Fact]
    public async Task ValidateAsync_NullSettings_ReturnsFailure()
    {
        OzonApiKeyValidator validator = BuildValidator(new MockHttpMessageHandler());

        ApiKeyValidationResult result = await validator.ValidateAsync(
            "some-key", null, null, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Client ID");
    }

    [Fact]
    public async Task ValidateAsync_MissingSellerIdKey_ReturnsFailure()
    {
        OzonApiKeyValidator validator = BuildValidator(new MockHttpMessageHandler());

        Dictionary<string, string> settings = new Dictionary<string, string>
        {
            ["otherKey"] = "value",
        };

        ApiKeyValidationResult result = await validator.ValidateAsync(
            "some-key", null, settings, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Client ID");
    }

    [Fact]
    public async Task ValidateAsync_Returns200_ReturnsSuccess()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TestUrl).Respond(HttpStatusCode.OK);

        OzonApiKeyValidator validator = BuildValidator(mockHttp);

        ApiKeyValidationResult result = await validator.ValidateAsync(
            "valid-key", null, ValidSettings(), CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_Returns403_ReturnsFailure()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TestUrl).Respond(HttpStatusCode.Forbidden);

        OzonApiKeyValidator validator = BuildValidator(mockHttp);

        ApiKeyValidationResult result = await validator.ValidateAsync(
            "bad-key", null, ValidSettings(), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Неверный API-ключ или Client ID Ozon");
    }

    [Fact]
    public async Task ValidateAsync_Returns500_ReturnsUnexpectedResponseFailure()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TestUrl).Respond(HttpStatusCode.InternalServerError);

        OzonApiKeyValidator validator = BuildValidator(mockHttp);

        ApiKeyValidationResult result = await validator.ValidateAsync(
            "key", null, ValidSettings(), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("500");
    }

    [Fact]
    public async Task ValidateAsync_HttpRequestException_ReturnsConnectionFailure()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TestUrl).Throw(new HttpRequestException("timeout"));

        OzonApiKeyValidator validator = BuildValidator(mockHttp);

        ApiKeyValidationResult result = await validator.ValidateAsync(
            "key", null, ValidSettings(), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Не удалось подключиться к Ozon");
    }

    [Fact]
    public async Task ValidateAsync_SendsCorrectHeaders()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();

        mockHttp
            .When(HttpMethod.Post, TestUrl)
            .WithHeaders("Client-Id", "12345")
            .WithHeaders("Api-Key", "my-api-key")
            .Respond(HttpStatusCode.OK);

        mockHttp.When(HttpMethod.Post, TestUrl).Respond(HttpStatusCode.Forbidden);

        OzonApiKeyValidator validator = BuildValidator(mockHttp);

        ApiKeyValidationResult result = await validator.ValidateAsync(
            "my-api-key", null, ValidSettings(), CancellationToken.None);

        result.IsValid.Should().BeTrue();
    }
}
