using System;
using System.Collections.Generic;
using System.Linq;
using Delobytes.App.Backend.Catalog.Infrastructure.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Delobytes.App.Backend.Tests.Persistence;

/// <summary>
/// Guards the photo-split backfill. The suite runs on the EF Core InMemory provider only, which
/// cannot execute migration SQL, so the migration is inspected verbatim and the grouping rule it
/// implements is asserted separately. The rule is the one thing that cannot be corrected after
/// release, hence the explicit cases even though they mirror the SQL rather than run it.
/// </summary>
public class ProductPhotoBackfillTests
{
    [Fact]
    public void SplitMigration_BackfillsChannelsBeforeDroppingTheChannelColumn()
    {
        List<MigrationOperation> operations = CollectUpOperations();

        int createTable = IndexOf<CreateTableOperation>(operations, o => o.Name == "ChannelProductPhotos");
        List<int> inserts = IndicesOf<SqlOperation>(
            operations,
            o => o.Sql.Contains("INSERT INTO catalog.", StringComparison.Ordinal));
        List<int> deletes = IndicesOf<SqlOperation>(
            operations,
            o => o.Sql.Contains("DELETE FROM catalog.", StringComparison.Ordinal));
        int dropColumn = IndexOf<DropColumnOperation>(operations, o => o.Name == "ChannelId");

        createTable.Should().BeGreaterThanOrEqualTo(0);
        inserts.Should().ContainSingle();
        deletes.Should().ContainSingle();
        dropColumn.Should().BeGreaterThanOrEqualTo(0);

        createTable.Should().BeLessThan(inserts[0], "the target table must exist before the copy");
        inserts[0].Should().BeLessThan(deletes[0], "a row must be copied before its source is deleted");
        deletes[0].Should().BeLessThan(dropColumn, "the source column is read by the backfill");
    }

    [Fact]
    public void SplitMigration_GroupsBySlotAndPrefersTheCanonicalRowOrTheOldestOne()
    {
        SqlOperation insert = CollectUpOperations()
            .OfType<SqlOperation>()
            .Single(o => o.Sql.Contains("INSERT INTO catalog.", StringComparison.Ordinal));

        insert.Sql.Should().Contain(
            "PARTITION BY p.\"TenantId\", p.\"ProductId\", p.\"DisplayOrder\", p.\"SizeVariant\"");
        insert.Sql.Should().Contain(
            "CASE WHEN p.\"ChannelId\" = '00000000-0000-0000-0000-000000000000' THEN 0 ELSE 1 END",
            "a legacy canonical row outranks any channel row");
        insert.Sql.Should().Contain("ORDER BY", "the earliest row is the one closest to creation");
        insert.Sql.Should().Contain("p.\"CreatedAt\"");
        insert.Sql.Should().Contain("p.\"Id\"");
        insert.Sql.Should().Contain(
            "AND r.\"ChannelId\" <> '00000000-0000-0000-0000-000000000000'",
            "a legacy canonical row is never copied to a channel");
        insert.Sql.Should().Contain(
            "JOIN catalog.\"ChannelProducts\" cp",
            "a row whose channel has no link is an orphan and is not copied");
    }

    [Fact]
    public void BackfillRule_KeepsTheCanonicalRowAndTurnsOtherChannelsIntoTheirOwnCopies()
    {
        // The cases the SQL must produce, expressed on the same inputs. Used as documentation of
        // the rule: the SQL above is the executable form, this pins what it is meant to decide.
        Row canonical = new Row(Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow.AddDays(-3), 1, "thumbnail");
        Row wildberries = new Row(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-5), 1, "thumbnail");
        Row ozon = new Row(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), 1, "thumbnail");

        IReadOnlyList<Row> kept = ApplyRule(new[] { canonical, wildberries, ozon });

        kept.Should().ContainSingle().Which.Should().Be(canonical,
            "a Guid.Empty row is canonical by definition, whatever its CreatedAt");

        IReadOnlyList<Row> withoutCanonical = ApplyRule(new[] { wildberries, ozon });

        withoutCanonical.Should().ContainSingle().Which.Should().Be(wildberries,
            "without a legacy canonical row the earliest row stays canonical");
    }

    private static IReadOnlyList<Row> ApplyRule(IEnumerable<Row> rows)
    {
        return rows
            .GroupBy(r => (r.DisplayOrder, r.SizeVariant))
            .Select(g => g
                .OrderBy(r => r.ChannelId == Guid.Empty ? 0 : 1)
                .ThenBy(r => r.CreatedAt)
                .ThenBy(r => r.Id)
                .First())
            .ToList();
    }

    private static List<MigrationOperation> CollectUpOperations()
    {
        MigrationBuilder builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        new ExposedMigration().ApplyUp(builder);

        return builder.Operations.ToList();
    }

    private static int IndexOf<T>(List<MigrationOperation> operations, Func<T, bool> predicate)
        where T : MigrationOperation
    {
        for (int i = 0; i < operations.Count; i++)
        {
            if (operations[i] is T typed && predicate(typed))
            {
                return i;
            }
        }

        return -1;
    }

    private static List<int> IndicesOf<T>(List<MigrationOperation> operations, Func<T, bool> predicate)
        where T : MigrationOperation
    {
        List<int> indices = new List<int>();

        for (int i = 0; i < operations.Count; i++)
        {
            if (operations[i] is T typed && predicate(typed))
            {
                indices.Add(i);
            }
        }

        return indices;
    }

    private sealed record Row(Guid Id, Guid ChannelId, DateTimeOffset CreatedAt, int DisplayOrder, string SizeVariant);

    // Up is protected; deriving exposes it without changing the migration's production shape.
    private sealed class ExposedMigration : split_channel_photos
    {
        public void ApplyUp(MigrationBuilder builder)
        {
            Up(builder);
        }
    }
}
