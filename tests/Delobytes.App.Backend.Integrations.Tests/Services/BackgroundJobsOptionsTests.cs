using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Services;

/// <summary>
/// Verifies that a new connection gets an hourly schedule only for the operational imports and a
/// daily one for the finance reports, which change once a day and cost a rate-limited request each.
/// </summary>
public class BackgroundJobsOptionsTests
{
    [Theory]
    [InlineData(JobType.WbOrdersImport)]
    [InlineData(JobType.ProductsImport)]
    [InlineData(JobType.OzonOrdersImportFbo)]
    [InlineData(JobType.OzonOrdersImportFbs)]
    public void ResolveDefaultCronExpression_OperationalImport_StaysHourly(JobType jobType)
    {
        BackgroundJobsOptions options = new BackgroundJobsOptions();

        options.ResolveDefaultCronExpression(jobType).Should().Be("0 * * * *");
    }

    [Fact]
    public void ResolveDefaultCronExpression_WildberriesFinance_IsDaily()
    {
        BackgroundJobsOptions options = new BackgroundJobsOptions();

        options.ResolveDefaultCronExpression(JobType.WbFinanceReportImport).Should().Be("0 8 * * *");
    }

    [Fact]
    public void ResolveDefaultCronExpression_OzonFinance_IsDailyAndStaggeredFromWildberries()
    {
        BackgroundJobsOptions options = new BackgroundJobsOptions();

        options.ResolveDefaultCronExpression(JobType.OzonFinanceAccrualsImport)
            .Should().Be("0 9 * * *", "both finance jobs compete for the same tenant slot");
    }

    [Fact]
    public void ResolveDefaultCronExpression_HonoursConfiguredValues()
    {
        BackgroundJobsOptions options = new BackgroundJobsOptions
        {
            DefaultOrdersImportCronExpression = "*/15 * * * *",
            DefaultWbFinanceImportCronExpression = "30 6 * * *",
            DefaultOzonFinanceImportCronExpression = "15 7 * * *",
        };

        options.ResolveDefaultCronExpression(JobType.WbOrdersImport).Should().Be("*/15 * * * *");
        options.ResolveDefaultCronExpression(JobType.WbFinanceReportImport).Should().Be("30 6 * * *");
        options.ResolveDefaultCronExpression(JobType.OzonFinanceAccrualsImport).Should().Be("15 7 * * *");
    }
}
