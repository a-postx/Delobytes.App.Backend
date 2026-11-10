using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries;
using FluentAssertions;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

public class WildberriesApiKeyValidatorTests
{
    private const string TestUrl = "https://content-api.wildberries.ru/content/v2/get/cards/list";

    private static WildberriesApiKeyValidator BuildValidator(
        MockHttpMessageHandler handler, IChannelRateLimiter? rateLimiter = null)
    {
        HttpClient client = handler.ToHttpClient();
        return new WildberriesApiKeyValidator(client, rateLimiter ?? new AlwaysAllowRateLimiter());
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

    private static Mock<IChannelRateLimiter> BuildAcquiringLimiter()
    {
        Mock<IChannelRateLimiter> limiter = new Mock<IChannelRateLimiter>();

        limiter
            .Setup(l => l.TryAcquireAsync(It.IsAny<RateLimitScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitAcquireResult
            {
                Acquired = true,
                NextRequestAllowedAt = DateTimeOffset.UtcNow,
            });

        return limiter;
    }

    [Fact]
    public async Task ValidateAsync_SlotRefused_ReturnsFailureWithoutIssuingTheRequest()
    {
        int httpCallCount = 0;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(TestUrl).Respond(_ =>
        {
            httpCallCount++;

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        Mock<IChannelRateLimiter> limiter = new Mock<IChannelRateLimiter>();
        limiter
            .Setup(l => l.TryAcquireAsync(It.IsAny<RateLimitScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitAcquireResult
            {
                Acquired = false,
                NextRequestAllowedAt = DateTimeOffset.UtcNow.AddMinutes(5),
            });

        ApiKeyValidationResult result = await BuildValidator(mockHttp, limiter.Object).ValidateAsync(
            "some-key", null, null, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Валидация отложена");
        httpCallCount.Should().Be(0, "validation must not spend a request the key's budget has not released");
    }

    [Fact]
    public async Task ValidateAsync_Returns429_ReportsTheThrottleOnTheCardsBucket()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(TestUrl).Respond(HttpStatusCode.TooManyRequests);

        Mock<IChannelRateLimiter> limiter = BuildAcquiringLimiter();

        limiter
            .Setup(l => l.ReportRateLimitedAsync(
                It.IsAny<RateLimitScope>(), It.IsAny<RateLimitSignal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTimeOffset.UtcNow.AddMinutes(1));

        ApiKeyValidationResult result = await BuildValidator(mockHttp, limiter.Object).ValidateAsync(
            "some-key", null, null, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("временно ограничил");

        limiter.Verify(
            l => l.ReportRateLimitedAsync(
                It.Is<RateLimitScope>(s =>
                    s.Method == WildberriesApiMethodRegistry.ContentCardsList
                    && s.ApiKey == "some-key"),
                It.IsAny<RateLimitSignal>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ValidateAsync_Returns200_ReportsSuccessOnTheCardsBucket()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(TestUrl).Respond(HttpStatusCode.OK);

        Mock<IChannelRateLimiter> limiter = BuildAcquiringLimiter();

        ApiKeyValidationResult result = await BuildValidator(mockHttp, limiter.Object).ValidateAsync(
            "some-key", null, null, CancellationToken.None);

        result.IsValid.Should().BeTrue();

        limiter.Verify(
            l => l.ReportSuccessAsync(
                It.Is<RateLimitScope>(s => s.Method == WildberriesApiMethodRegistry.ContentCardsList),
                It.IsAny<RateLimitSignal>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
