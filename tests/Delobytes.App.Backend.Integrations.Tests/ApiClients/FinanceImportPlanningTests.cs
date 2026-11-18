using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Finance;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// Tests for finance window planning and batch splitting. Both are pure rules: the overlap absorbs
/// a re-issued report, the backfill floor bounds the first run and a batch never exceeds the host's
/// message budget.
/// </summary>
public class FinanceImportPlanningTests
{
    [Fact]
    public void Plan_Wildberries_UsesFourteenDayOverlap()
    {
        (DateOnly From, DateOnly To)? window = FinanceImportWindowPlanner.Plan(
            "wildberries",
            new DateOnly(2026, 11, 10),
            new DateOnly(2026, 11, 16),
            initialBackfillDays: 180,
            wbOverlapDays: 14,
            ozonOverlapDays: 7);

        window.Should().Be((new DateOnly(2026, 10, 27), new DateOnly(2026, 11, 16)));
    }

    [Fact]
    public void Plan_Ozon_EndsYesterdayAndUsesSevenDayOverlap()
    {
        (DateOnly From, DateOnly To)? window = FinanceImportWindowPlanner.Plan(
            "ozon",
            new DateOnly(2026, 11, 12),
            new DateOnly(2026, 11, 16),
            initialBackfillDays: 180,
            wbOverlapDays: 14,
            ozonOverlapDays: 7);

        window.Should().Be((new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 15)));
    }

    [Fact]
    public void Plan_FirstRun_IsBoundedByTheBackfillDepth()
    {
        (DateOnly From, DateOnly To)? window = FinanceImportWindowPlanner.Plan(
            "ozon",
            new DateOnly(2020, 1, 1),
            new DateOnly(2026, 11, 16),
            initialBackfillDays: 180,
            wbOverlapDays: 14,
            ozonOverlapDays: 7);

        window.Should().Be((new DateOnly(2026, 5, 20), new DateOnly(2026, 11, 15)));
    }

    [Fact]
    public void Plan_OzonWithTodaysWatermark_StillCoversTheOverlap()
    {
        (DateOnly From, DateOnly To)? window = FinanceImportWindowPlanner.Plan(
            "ozon",
            new DateOnly(2026, 11, 16),
            new DateOnly(2026, 11, 16),
            initialBackfillDays: 180,
            wbOverlapDays: 14,
            ozonOverlapDays: 7);

        window.Should().Be((new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 15)));
    }

    [Fact]
    public void Split_UsesTheConfiguredBoundaryAndKeepsOrder()
    {
        List<int> items = new();

        for (int index = 0; index < 2500; index++)
        {
            items.Add(index);
        }

        IReadOnlyList<IReadOnlyList<int>> batches = FinanceBatchSplitter.Split(items, 1000);

        batches.Should().HaveCount(3);
        batches[0].Should().HaveCount(1000);
        batches[1].Should().HaveCount(1000);
        batches[2].Should().HaveCount(500);
        batches[0][0].Should().Be(0);
        batches[2][499].Should().Be(2499);
    }

    [Fact]
    public void Split_EmptyInput_ProducesNoBatches()
    {
        FinanceBatchSplitter.Split(Array.Empty<int>(), 1000).Should().BeEmpty();
    }
}

