using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// Tests for the Ozon accruals page fetch: the request body, the cursor propagation and the end of
/// data signalled by an empty <c>last_id</c>.
/// </summary>
public class OzonApiClientFinanceAccrualsTests
{
    private const string AccrualsUrl = "https://api-seller.ozon.ru/v1/finance/accrual/by-day";

    [Fact]
    public async Task GetEntriesAsync_SendsTheDateAndCursorAndReturnsTheNextOne()
    {
        string? capturedBody = null;

        string json = """
        {
          "accruals": [
            {
              "accrual_id": 1, "date": "2026-11-16", "total_amount": { "amount": "-3.34", "currency": "RUB" },
              "unit_number": "14512295-0639", "accrued_category": "ITEM",
              "item_fees": { "fees": [ { "sku": 5, "fees": [ { "type_id": 1, "accrued": { "amount": "-3.34", "currency": "RUB" } } ] } ] }
            }
          ],
          "last_id": "next-page"
        }
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, AccrualsUrl)
            .With(req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", json);

        OzonApiClient client = BuildClient(mockHttp);

        FinanceEntriesPage page = await client.GetFinanceEntriesAsync(
            Guid.NewGuid(),
            new FinanceEntriesRequest { From = new DateOnly(2026, 11, 16), To = new DateOnly(2026, 11, 16) },
            CancellationToken.None);

        capturedBody.Should().Contain("\"date\":\"2026-11-16\"");
        capturedBody.Should().Contain("\"last_id\":\"\"");

        // The cursor carries the day it belongs to: the endpoint reads one day per request, so a
        // bare page id would lose the position inside a multi-day window.
        page.NextCursor.Should().Be("2026-11-16|next-page");
        page.IsComplete.Should().BeFalse();
        page.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetEntriesAsync_FirstDayExhausted_ContinuesWithTheNextDayOfTheWindow()
    {
        List<string> capturedBodies = new();

        string dayJson = """
        {
          "accruals": [
            {
              "accrual_id": 1, "date": "2026-11-16", "total_amount": { "amount": "-3.34", "currency": "RUB" },
              "unit_number": "14512295-0639", "accrued_category": "ITEM",
              "item_fees": { "fees": [ { "sku": 5, "fees": [ { "type_id": 1, "accrued": { "amount": "-3.34", "currency": "RUB" } } ] } ] }
            }
          ],
          "last_id": ""
        }
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, AccrualsUrl)
            .With(req =>
            {
                capturedBodies.Add(req.Content!.ReadAsStringAsync().Result);
                return true;
            })
            .Respond("application/json", dayJson)
            .Respond("application/json", dayJson);

        OzonApiClient client = BuildClient(mockHttp);

        FinanceEntriesPage first = await client.GetFinanceEntriesAsync(
            Guid.NewGuid(),
            new FinanceEntriesRequest { From = new DateOnly(2026, 11, 16), To = new DateOnly(2026, 11, 18) },
            CancellationToken.None);

        first.IsComplete.Should().BeFalse();
        first.NextCursor.Should().Be("2026-11-17|");

        FinanceEntriesPage second = await client.GetFinanceEntriesAsync(
            Guid.NewGuid(),
            new FinanceEntriesRequest
            {
                From = new DateOnly(2026, 11, 16),
                To = new DateOnly(2026, 11, 18),
                Cursor = first.NextCursor,
            },
            CancellationToken.None);

        capturedBodies.Should().HaveCount(2);
        capturedBodies[1].Should().Contain("\"date\":\"2026-11-17\"");
        capturedBodies[1].Should().Contain("\"last_id\":\"\"");
        second.NextCursor.Should().Be("2026-11-18|");
        second.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GetEntriesAsync_LastDayExhausted_IsTheEndOfTheWindow()
    {
        string dayJson = """
        {
          "accruals": [
            {
              "accrual_id": 1, "date": "2026-11-18", "total_amount": { "amount": "-3.34", "currency": "RUB" },
              "unit_number": "14512295-0639", "accrued_category": "ITEM",
              "item_fees": { "fees": [ { "sku": 5, "fees": [ { "type_id": 1, "accrued": { "amount": "-3.34", "currency": "RUB" } } ] } ] }
            }
          ],
          "last_id": ""
        }
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, AccrualsUrl).Respond("application/json", dayJson);

        OzonApiClient client = BuildClient(mockHttp);

        FinanceEntriesPage page = await client.GetFinanceEntriesAsync(
            Guid.NewGuid(),
            new FinanceEntriesRequest
            {
                From = new DateOnly(2026, 11, 18),
                To = new DateOnly(2026, 11, 18),
                Cursor = "2026-11-18|",
            },
            CancellationToken.None);

        page.IsComplete.Should().BeTrue();
        page.NextCursor.Should().BeNull();
        page.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetEntriesAsync_EmptyLastId_IsTheEndOfData()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, AccrualsUrl)
            .Respond("application/json", """{ "accruals": [], "last_id": "" }""");

        OzonApiClient client = BuildClient(mockHttp);

        FinanceEntriesPage page = await client.GetFinanceEntriesAsync(
            Guid.NewGuid(),
            new FinanceEntriesRequest { From = new DateOnly(2026, 11, 16), To = new DateOnly(2026, 11, 16) },
            CancellationToken.None);

        page.IsComplete.Should().BeTrue();
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GetEntriesAsync_RepeatedLastId_StopsTheLoop()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, AccrualsUrl)
            .Respond("application/json", """{ "accruals": [], "last_id": "same" }""");

        OzonApiClient client = BuildClient(mockHttp);

        FinanceEntriesPage page = await client.GetFinanceEntriesAsync(
            Guid.NewGuid(),
            new FinanceEntriesRequest
            {
                From = new DateOnly(2026, 11, 16),
                To = new DateOnly(2026, 11, 16),
                Cursor = "same",
            },
            CancellationToken.None);

        page.NextCursor.Should().BeNull();
        page.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task GetEntriesAsync_ServerError_IsNotReportedToTheLimiterAsASuccess()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, AccrualsUrl).Respond(HttpStatusCode.InternalServerError);

        RecordingRateLimiter limiter = new RecordingRateLimiter();
        OzonApiClient client = BuildClient(mockHttp, limiter);

        Func<Task> act = () => client.GetFinanceEntriesAsync(
            Guid.NewGuid(),
            new FinanceEntriesRequest { From = new DateOnly(2026, 11, 16), To = new DateOnly(2026, 11, 16) },
            CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();

        limiter.SuccessReports.Should().Be(
            0, "a 5xx must not feed the limiter the allowance of a response that failed");
    }

    [Fact]
    public async Task GetEntriesAsync_Success_IsReportedToTheLimiterOnce()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, AccrualsUrl)
            .Respond("application/json", """{ "accruals": [], "last_id": "" }""");

        RecordingRateLimiter limiter = new RecordingRateLimiter();
        OzonApiClient client = BuildClient(mockHttp, limiter);

        await client.GetFinanceEntriesAsync(
            Guid.NewGuid(),
            new FinanceEntriesRequest { From = new DateOnly(2026, 11, 16), To = new DateOnly(2026, 11, 16) },
            CancellationToken.None);

        limiter.SuccessReports.Should().Be(1);
    }

    private static OzonApiClient BuildClient(
        MockHttpMessageHandler mockHttp, IChannelRateLimiter? rateLimiter = null)
    {
        OzonApiClient client = new OzonApiClient(
            mockHttp.ToHttpClient(),
            Mock.Of<IHttpClientFactory>(),
            new FixedConnectionResolver(),
            rateLimiter ?? new AlwaysAllowRateLimiter(),
            NullLogger<OzonApiClient>.Instance,
            TimeProvider.System);

        client.SetTemplate(new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "ozon",
            DisplayName = "Ozon",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        return client;
    }
}
