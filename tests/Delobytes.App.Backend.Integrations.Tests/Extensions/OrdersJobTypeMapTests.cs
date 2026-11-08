using System.Collections.Generic;
using System.Linq;
using Delobytes.App.Backend.Integrations.Application.Extensions;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Extensions;

/// <summary>
/// Tests <see cref="OrdersJobTypeMap"/>: the single source of truth for which orders job types a
/// channel runs. After the removal of <c>OzonImport:EnableFbs</c> both Ozon schemas are always
/// mapped, and the scheduler - not a configuration flag - decides when each one actually runs.
/// </summary>
public class OrdersJobTypeMapTests
{
    [Fact]
    public void GetOrdersJobTypes_Ozon_AlwaysReturnsBothSchemas()
    {
        IReadOnlyList<JobType> jobTypes = OrdersJobTypeMap.GetOrdersJobTypes("ozon");

        jobTypes.Should().BeEquivalentTo(
            new[] { JobType.OzonOrdersImportFbo, JobType.OzonOrdersImportFbs },
            "the EnableFbs flag is gone, so sequencing is the only thing that decides which runs");
    }

    [Fact]
    public void GetOrdersJobTypes_Ozon_ReturnsFboBeforeFbs()
    {
        // The order matters on first contact: both schedules are seeded due immediately, so the
        // list decides which schema takes the connection first.
        IReadOnlyList<JobType> jobTypes = OrdersJobTypeMap.GetOrdersJobTypes("ozon");

        jobTypes.Should().ContainInOrder(JobType.OzonOrdersImportFbo, JobType.OzonOrdersImportFbs);
    }

    [Theory]
    [InlineData("ozon")]
    [InlineData("OZON")]
    [InlineData("Ozon")]
    public void GetOrdersJobTypes_OzonCodeIsCaseInsensitive(string channelCode)
    {
        OrdersJobTypeMap.GetOrdersJobTypes(channelCode).Should().HaveCount(2);
    }

    [Fact]
    public void GetOrdersJobTypes_Wildberries_ReturnsOrdersImportOnly()
    {
        OrdersJobTypeMap.GetOrdersJobTypes("wildberries").Should().BeEquivalentTo(new[] { JobType.WbOrdersImport });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("yandex.kit")]
    public void GetOrdersJobTypes_UnknownOrBlankChannel_ReturnsEmpty(string? channelCode)
    {
        OrdersJobTypeMap.GetOrdersJobTypes(channelCode).Should().BeEmpty(
            "a channel with no orders mapping must get no schedule at all");
    }

    [Fact]
    public void GetOrdersJobTypes_EveryReturnedTypeIsScheduled()
    {
        foreach (string channelCode in new[] { "ozon", "wildberries" })
        {
            OrdersJobTypeMap.GetOrdersJobTypes(channelCode)
                .Should().OnlyContain(jobType => OrdersJobTypeMap.IsScheduledOrdersJobType(jobType));
        }
    }

    [Theory]
    [InlineData(JobType.WbOrdersImport, true)]
    [InlineData(JobType.OzonOrdersImportFbo, true)]
    [InlineData(JobType.OzonOrdersImportFbs, true)]
    [InlineData(JobType.ProductsImport, false)]
    public void IsScheduledOrdersJobType_CoversExactlyTheThreeOrdersTypes(JobType jobType, bool expected)
    {
        OrdersJobTypeMap.IsScheduledOrdersJobType(jobType).Should().Be(expected);
    }

    [Theory]
    [InlineData("ozon", JobType.OzonOrdersImportFbo, OrdersRateLimiterMethods.OzonPostingFboList)]
    [InlineData("ozon", JobType.OzonOrdersImportFbs, OrdersRateLimiterMethods.OzonPostingFbsList)]
    [InlineData("wildberries", JobType.WbOrdersImport, OrdersRateLimiterMethods.WildberriesSupplierOrders)]
    public void GetRateLimitMethod_SupportedPair_ReturnsTheChannelMethod(
        string channelCode, JobType jobType, string expectedMethod)
    {
        OrdersJobTypeMap.GetRateLimitMethod(channelCode, jobType).Should().Be(expectedMethod);
    }

    [Theory]
    [InlineData("ozon", JobType.WbOrdersImport)]
    [InlineData("ozon", JobType.ProductsImport)]
    [InlineData("wildberries", JobType.OzonOrdersImportFbo)]
    [InlineData("wildberries", JobType.OzonOrdersImportFbs)]
    [InlineData("yandex.kit", JobType.WbOrdersImport)]
    [InlineData(null, JobType.WbOrdersImport)]
    public void GetRateLimitMethod_UnsupportedPair_ReturnsNull(string? channelCode, JobType jobType)
    {
        OrdersJobTypeMap.GetRateLimitMethod(channelCode, jobType).Should().BeNull(
            "a pair that never runs needs no limiter pre-check");
    }
}
