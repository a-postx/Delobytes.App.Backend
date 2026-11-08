using System;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Validators;

public class OrdersImportTuningTests
{
    [Fact]
    public void Wildberries_Defaults_ResolveEverySettingToAnEffectiveValue()
    {
        OrdersImportTuning tuning = new WildberriesImportOptions().ToTuning(JobType.OrdersImport);

        tuning.RunBudget.Should().Be(TimeSpan.FromSeconds(WildberriesImportOptions.DefaultBackfillRunBudgetSeconds));
        tuning.MaxInProcessWait.Should().Be(TimeSpan.FromSeconds(WildberriesImportOptions.DefaultMaxInProcessWaitSeconds));
        tuning.MaxRequestsPerRun.Should().Be(WildberriesImportOptions.DefaultMaxRequestsPerRun);
        tuning.BatchSize.Should().Be(OrdersImportTuning.DefaultBatchSize);
        tuning.SafetyOverlap.Should().Be(WildberriesImportOptions.DefaultSafetyOverlap);
        tuning.BackfillWindow.Should().Be(WildberriesImportOptions.DefaultBackfillWindow);
        tuning.MinOrdersWindow.Should().Be(TimeSpan.FromHours(1));
        tuning.MethodName.Should().Be(OrdersRateLimiterMethods.WildberriesSupplierOrders);
    }

    [Fact]
    public void Wildberries_HasNoPageSize()
    {
        OrdersImportTuning tuning = new WildberriesImportOptions().ToTuning(JobType.OrdersImport);

        tuning.PageSize.Should().BeNull();
    }

    [Fact]
    public void Wildberries_ZeroMaxInProcessWait_StaysZero()
    {
        OrdersImportTuning tuning = new WildberriesImportOptions
        {
            MaxInProcessWaitSeconds = 0,
        }.ToTuning(JobType.OrdersImport);

        tuning.MaxInProcessWait.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Wildberries_NonPositiveBudget_FallsBackToTheDefault()
    {
        OrdersImportTuning tuning = new WildberriesImportOptions
        {
            BackfillRunBudgetSeconds = 0,
        }.ToTuning(JobType.OrdersImport);

        tuning.RunBudget.Should().Be(TimeSpan.FromSeconds(WildberriesImportOptions.DefaultBackfillRunBudgetSeconds));
    }

    [Fact]
    public void Ozon_Defaults_ResolveEverySettingToAnEffectiveValue()
    {
        OrdersImportTuning tuning = new OzonImportOptions().ToTuning(JobType.OrdersImportFbo);

        tuning.RunBudget.Should().Be(TimeSpan.FromSeconds(OzonImportOptions.DefaultBackfillRunBudgetSeconds));
        tuning.MaxInProcessWait.Should().Be(TimeSpan.FromSeconds(OzonImportOptions.DefaultMaxInProcessWaitSeconds));
        tuning.MaxRequestsPerRun.Should().Be(OzonImportOptions.DefaultMaxRequestsPerRun);
        tuning.BatchSize.Should().Be(OrdersImportTuning.DefaultBatchSize);
        tuning.SafetyOverlap.Should().Be(TimeSpan.FromMinutes(OzonImportOptions.DefaultSafetyOverlapMinutes));
        tuning.BackfillWindow.Should().Be(TimeSpan.FromDays(OzonImportOptions.DefaultBackfillWindowDays));
        tuning.PageSize.Should().Be(OzonImportOptions.DefaultPageSize);
    }

    [Fact]
    public void Ozon_ZeroMaxInProcessWait_StaysZero()
    {
        OrdersImportTuning tuning = new OzonImportOptions
        {
            MaxInProcessWaitSeconds = 0,
        }.ToTuning(JobType.OrdersImportFbo);

        tuning.MaxInProcessWait.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(JobType.OrdersImportFbo, OrdersRateLimiterMethods.OzonPostingFboList)]
    [InlineData(JobType.OrdersImportFbs, OrdersRateLimiterMethods.OzonPostingFbsList)]
    public void Ozon_MethodNameIsSelectedByJobType(JobType jobType, string expectedMethod)
    {
        OrdersImportTuning tuning = new OzonImportOptions().ToTuning(jobType);

        tuning.MethodName.Should().Be(expectedMethod);
    }

    [Fact]
    public void Ozon_UnsupportedJobType_Throws()
    {
        Action act = () => new OzonImportOptions().ToTuning(JobType.OrdersImport);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
