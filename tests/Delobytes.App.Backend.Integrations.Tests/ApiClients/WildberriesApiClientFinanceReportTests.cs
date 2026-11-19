using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// Tests for the Wildberries finance report page fetch: the request shape, the cursor propagation,
/// the end-of-data signal and the two failures the run must report clearly.
/// </summary>
public class WildberriesApiClientFinanceReportTests
{
    private const string FinanceBaseUrl = "https://finance-api.wildberries.ru";
    private const string ReportUrl = FinanceBaseUrl + "/api/finance/v1/sales-reports/detailed";

    private static readonly Guid TestTemplateId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly SystemChannelTemplate TestTemplate = new SystemChannelTemplate
    {
        Id = TestTemplateId,
        Code = "wildberries",
        DisplayName = "Wildberries",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task GetEntriesAsync_FirstPage_SendsWeeklyPeriodFieldsAndZeroCursor()
    {
        string? capturedBody = null;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, ReportUrl)
            .With(req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", "[]");

        WildberriesApiClient client = BuildClient(mockHttp);

        FinanceEntriesPage page = await client.GetFinanceEntriesAsync(
            Guid.NewGuid(),
            new FinanceEntriesRequest
            {
                From = new DateOnly(2026, 7, 27),
                To = new DateOnly(2026, 8, 2),
            },
            CancellationToken.None);

        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("\"period\":\"weekly\"");
        capturedBody.Should().Contain("\"rrdId\":0");
        capturedBody.Should().Contain("\"dateFrom\":\"2026-07-27\"");
        capturedBody.Should().Contain("\"dateTo\":\"2026-08-02\"");
        capturedBody.Should().Contain("\"retailAmount\"");
        capturedBody.Should().Contain("\"deliveryService\"");

        // The seller's price and the commission rate are what let the mapper book the sale at the
        // price the seller actually receives instead of the buyer's payment.
        capturedBody.Should().Contain("\"retailPriceWithDisc\"");
        capturedBody.Should().Contain("\"commissionPercent\"");

        page.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GetEntriesAsync_NextPage_PropagatesTheCursorAndReturnsTheLastRowIdentity()
    {
        string? capturedBody = null;

        string json = """
        [
          { "reportId": 1, "createDate": "2026-08-03", "dateFrom": "2026-07-27", "dateTo": "2026-08-02", "currency": "RUB", "rrdId": 42, "nmId": 5, "srid": "s1", "sellerOperName": "Продажа", "rrDate": "2026-08-01", "retailAmount": "100", "forPay": "100" },
          { "reportId": 1, "createDate": "2026-08-03", "dateFrom": "2026-07-27", "dateTo": "2026-08-02", "currency": "RUB", "rrdId": 43, "nmId": 5, "srid": "s1", "sellerOperName": "Продажа", "rrDate": "2026-08-01", "retailAmount": "50", "forPay": "50" }
        ]
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, ReportUrl)
            .With(req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp);

        FinanceEntriesPage page = await client.GetFinanceEntriesAsync(
            Guid.NewGuid(),
            new FinanceEntriesRequest
            {
                From = new DateOnly(2026, 7, 27),
                To = new DateOnly(2026, 8, 2),
                Cursor = "42",
            },
            CancellationToken.None);

        capturedBody.Should().Contain("\"rrdId\":42");
        page.NextCursor.Should().Be("43");
    }

    [Fact]
    public async Task GetEntriesAsync_NoContent_IsTheEndOfData()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, ReportUrl).Respond(HttpStatusCode.NoContent);

        WildberriesApiClient client = BuildClient(mockHttp);

        FinanceEntriesPage page = await client.GetFinanceEntriesAsync(
            Guid.NewGuid(),
            new FinanceEntriesRequest { From = new DateOnly(2026, 7, 27), To = new DateOnly(2026, 8, 2) },
            CancellationToken.None);

        page.IsComplete.Should().BeTrue();
        page.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEntriesAsync_Unauthorized_TellsTheOperatorToEnableTheFinanceCategory()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, ReportUrl).Respond(HttpStatusCode.Unauthorized);

        WildberriesApiClient client = BuildClient(mockHttp);

        Func<Task> act = () => client.GetFinanceEntriesAsync(
            Guid.NewGuid(),
            new FinanceEntriesRequest { From = new DateOnly(2026, 7, 27), To = new DateOnly(2026, 8, 2) },
            CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Финансы*");
    }

    [Fact]
    public async Task GetEntriesAsync_Forbidden_TellsTheOperatorToEnableTheFinanceCategory()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, ReportUrl).Respond(HttpStatusCode.Forbidden);

        WildberriesApiClient client = BuildClient(mockHttp);

        Func<Task> act = () => client.GetFinanceEntriesAsync(
            Guid.NewGuid(),
            new FinanceEntriesRequest { From = new DateOnly(2026, 7, 27), To = new DateOnly(2026, 8, 2) },
            CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task GetEntriesAsync_Throttled_ReportsTheNextAttemptInsteadOfThrowing()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, ReportUrl).Respond(HttpStatusCode.TooManyRequests);

        WildberriesApiClient client = BuildClient(mockHttp);

        FinanceEntriesPage page = await client.GetFinanceEntriesAsync(
            Guid.NewGuid(),
            new FinanceEntriesRequest { From = new DateOnly(2026, 7, 27), To = new DateOnly(2026, 8, 2) },
            CancellationToken.None);

        page.Throttled.Should().BeTrue();
        page.RetryAt.Should().NotBeNull();
    }

    private static WildberriesApiClient BuildClient(MockHttpMessageHandler mockHttp)
    {
        HttpClient typedClient = mockHttp.ToHttpClient();

        Mock<IEndpointResolver> endpointResolver = new Mock<IEndpointResolver>();
        endpointResolver
            .Setup(r => r.GetEndpointUrlAsync(
                TestTemplateId,
                ChannelEndpointType.Finance,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(FinanceBaseUrl);

        WildberriesApiClient client = new WildberriesApiClient(
            typedClient,
            endpointResolver.Object,
            new FixedConnectionResolver(),
            new AlwaysAllowRateLimiter(),
            Options.Create(new WildberriesImportOptions()),
            NullLogger<WildberriesApiClient>.Instance,
            TimeProvider.System);

        client.SetTemplate(TestTemplate);

        return client;
    }
}
