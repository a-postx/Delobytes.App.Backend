using System;
using System.Collections.Generic;
using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Orders;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients.Ozon;

/// <summary>
/// Tests the request bodies handed to Ozon. The window format and the page-size envelope are contract
/// details: Ozon rejects the offset form the serializer would emit, and a limit outside 1..100 fails the
/// whole page rather than one row.
/// </summary>
public class OzonRequestFactoryTests
{
    private static readonly DateTimeOffset WindowFrom =
        new DateTimeOffset(2026, 11, 16, 12, 30, 0, TimeSpan.FromHours(3));

    private static readonly DateTimeOffset WindowTo =
        new DateTimeOffset(2026, 11, 17, 12, 30, 0, TimeSpan.FromHours(3));

    [Fact]
    public void BuildFboBody_FormatsTheWindowAsUtcWithZ()
    {
        OzonPostingListRequest body = OzonRequestFactory.BuildFboBody(BuildRequest(), 50);

        body.Filter.Since.Should().Be("2026-11-16T09:30:00Z");
        body.Filter.To.Should().Be("2026-11-17T09:30:00Z");
    }

    [Fact]
    public void BuildFboBody_WithoutStatuses_UsesTheOzonFboSet()
    {
        OzonPostingListRequest body = OzonRequestFactory.BuildFboBody(BuildRequest(), 50);

        body.Filter.Statuses.Should().Equal(OzonPostingStatuses.FboStatuses);
        body.Limit.Should().Be(50);
    }

    [Fact]
    public void BuildFboBody_WithStatuses_SendsTheCallerSet()
    {
        OrdersRequest request = BuildRequest();
        request.Statuses = new List<string> { "delivering" };

        OzonPostingListRequest body = OzonRequestFactory.BuildFboBody(request, 50);

        body.Filter.Statuses.Should().Equal("delivering");
    }

    [Fact]
    public void BuildFbsBody_WithoutStatuses_UsesTheOzonFbsSet()
    {
        OzonFbsPostingListRequest body = OzonRequestFactory.BuildFbsBody(BuildRequest(), 50);

        body.Filter.Since.Should().Be("2026-11-16T09:30:00Z");
        body.Filter.Statuses.Should().Equal(OzonPostingStatuses.FbsStatuses);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-5, 100)]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(5000, 100)]
    public void ClampPostingLimit_KeepsThePageInsideTheEndpointEnvelope(int requested, int expected)
    {
        OzonRequestFactory.ClampPostingLimit(requested).Should().Be(expected);
    }

    [Fact]
    public void BuildProductAttributesBody_EmptyCursor_OmitsLastIdFromThePayload()
    {
        OzonProductAttributesRequest body = OzonRequestFactory.BuildProductAttributesBody(200, null);

        string json = JsonSerializer.Serialize(body, OzonJson.Options);

        body.LastId.Should().BeNull();
        json.Should().NotContain("last_id");
        body.Limit.Should().Be(200);
    }

    [Fact]
    public void BuildProductAttributesBody_PresentCursor_SendsIt()
    {
        OzonProductAttributesRequest body = OzonRequestFactory.BuildProductAttributesBody(200, "cursor-1");

        string json = JsonSerializer.Serialize(body, OzonJson.Options);

        json.Should().Contain("\"last_id\":\"cursor-1\"");
    }

    private static OrdersRequest BuildRequest()
    {
        return new OrdersRequest
        {
            DateFrom = WindowFrom,
            DateTo = WindowTo,
            Limit = 50,
        };
    }
}
