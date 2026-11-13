using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Numbering;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog.Numbering;

public class NumberingSchemeProviderTests
{
    private static DbContextOptions<CatalogDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("numbering-scheme-provider-" + Guid.NewGuid())
            .Options;
    }

    private static Mock<ITenantContext> TenantMock(Guid? tenantId)
    {
        Mock<ITenantContext> mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock;
    }

    private static CatalogDbContext BuildContext(
        DbContextOptions<CatalogDbContext> options,
        Guid tenantId)
    {
        return new CatalogDbContext(options, TenantMock(tenantId).Object);
    }

    private static NumberingSchemeProvider BuildProvider(CatalogDbContext context, Guid tenantId)
    {
        return new NumberingSchemeProvider(
            context,
            TenantMock(tenantId).Object,
            new NumberingSchemeValidator());
    }

    private static Guid ReadTenantId(CatalogDbContext context, NumberingScheme scheme)
    {
        return context.Entry(scheme).Property<Guid>("TenantId").CurrentValue;
    }

    private static NumberingScheme BuildSeededScheme(NumberingSequence sequence, bool isActive = true)
    {
        return NumberingTestData.BuildScheme(
            isActive: isActive,
            segments: new[]
            {
                NumberingTestData.Constant(1, "P-"),
                NumberingTestData.AutoNumber(2, sequence, 6),
            });
    }

    [Fact]
    public async Task GetDefaultProductSchemeAsync_WhenNoDefaultExists_CreatesTheStandardSeedOnce()
    {
        DbContextOptions<CatalogDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();

        using CatalogDbContext ctx = BuildContext(options, tenantId);
        NumberingSchemeProvider provider = BuildProvider(ctx, tenantId);

        NumberingScheme created = await provider.GetDefaultProductSchemeAsync(default);

        created.Code.Should().Be("PRODUCT");
        created.Name.Should().Be("Product code");
        created.EntityKind.Should().Be(NumberingEntityKind.Product);
        created.IsDefault.Should().BeTrue();
        created.IsActive.Should().BeTrue();
        ReadTenantId(ctx, created).Should().Be(tenantId);

        List<NumberingSegment> segments = created.Segments.OrderBy(segment => segment.Order).ToList();
        segments.Should().HaveCount(2);
        segments[0].Type.Should().Be(NumberingSegmentType.Constant);
        segments[0].ConstantValue.Should().Be("P-");
        segments[1].Type.Should().Be(NumberingSegmentType.AutoNumber);
        segments[1].Length.Should().Be(6);
        segments[1].NumberingSequence.Should().NotBeNull();
        segments[1].NumberingSequence!.NextNumber.Should().Be(1);
        segments[1].NumberingSequence!.Step.Should().Be(1);

        (await ctx.NumberingSchemes.CountAsync()).Should().Be(1);
        (await ctx.NumberingSequences.CountAsync()).Should().Be(1);
        (await ctx.NumberingSegments.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task GetDefaultProductSchemeAsync_SecondCall_ReturnsTheExistingRowWithoutInserting()
    {
        DbContextOptions<CatalogDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();

        using CatalogDbContext ctx = BuildContext(options, tenantId);
        NumberingSchemeProvider provider = BuildProvider(ctx, tenantId);

        NumberingScheme first = await provider.GetDefaultProductSchemeAsync(default);
        NumberingScheme second = await provider.GetDefaultProductSchemeAsync(default);

        second.Id.Should().Be(first.Id);

        using CatalogDbContext verification = BuildContext(options, tenantId);
        (await verification.NumberingSchemes.CountAsync()).Should().Be(1);
        (await verification.NumberingSequences.CountAsync()).Should().Be(1);
        (await verification.NumberingSegments.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task GetDefaultProductSchemeAsync_SchemeOfAnotherTenant_IsNeverReturned()
    {
        DbContextOptions<CatalogDbContext> options = BuildOptions();
        Guid otherTenantId = Guid.NewGuid();
        Guid currentTenantId = Guid.NewGuid();

        NumberingSequence otherSequence = NumberingTestData.BuildSequence(nextNumber: 500);
        NumberingScheme otherScheme = BuildSeededScheme(otherSequence);

        using (CatalogDbContext otherTenant = BuildContext(options, otherTenantId))
        {
            otherTenant.NumberingSchemes.Add(otherScheme);
            await otherTenant.SaveChangesAsync();
        }

        using CatalogDbContext ctx = BuildContext(options, currentTenantId);
        NumberingSchemeProvider provider = new NumberingSchemeProvider(
            ctx,
            TenantMock(currentTenantId).Object,
            new NumberingSchemeValidator());

        NumberingScheme created = await provider.GetDefaultProductSchemeAsync(default);

        created.Id.Should().NotBe(otherScheme.Id);
        ReadTenantId(ctx, created).Should().Be(currentTenantId);

        using CatalogDbContext verification = BuildContext(options, currentTenantId);
        (await verification.NumberingSchemes.IgnoreQueryFilters().CountAsync()).Should().Be(2);
        (await verification.NumberingSchemes.CountAsync()).Should().Be(1);
        (await verification.NumberingSchemes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetDefaultProductSchemeAsync_InactiveDefault_ThrowsAndDoesNotCreateOrReactivate()
    {
        DbContextOptions<CatalogDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();

        NumberingSequence sequence = NumberingTestData.BuildSequence(nextNumber: 12);
        NumberingScheme inactiveDefault = BuildSeededScheme(sequence, isActive: false);

        using CatalogDbContext ctx = BuildContext(options, tenantId);
        ctx.NumberingSchemes.Add(inactiveDefault);
        await ctx.SaveChangesAsync();

        NumberingSchemeProvider provider = BuildProvider(ctx, tenantId);

        Func<Task> act = () => provider.GetDefaultProductSchemeAsync(default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*PRODUCT*{inactiveDefault.Id}*{tenantId}*inactive*");

        inactiveDefault.IsActive.Should().BeFalse();
        (await ctx.NumberingSchemes.CountAsync()).Should().Be(1);
        (await ctx.NumberingSequences.CountAsync()).Should().Be(1);

        // The unique slot is filtered on IsDefault alone: an inactive default still occupies it,
        // so a second default cannot be inserted behind the operator's back.
        IEntityType schemeType = ctx.Model.FindEntityType(typeof(NumberingScheme))!;
        IIndex index = schemeType.GetIndexes()
            .Single(candidate => candidate.GetDatabaseName() == "IX_NumberingSchemes_TenantId_Kind_Default");
        index.IsUnique.Should().BeTrue();
        index.GetFilter().Should().Be("\"IsDefault\"");
    }

    [Fact]
    public async Task GetDefaultProductSchemeAsync_ExistingSchemeWithAdvancedCounter_KeepsIt()
    {
        DbContextOptions<CatalogDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();

        NumberingSequence sequence = NumberingTestData.BuildSequence(nextNumber: 47);
        NumberingScheme existing = BuildSeededScheme(sequence);

        using CatalogDbContext ctx = BuildContext(options, tenantId);
        ctx.NumberingSchemes.Add(existing);
        await ctx.SaveChangesAsync();

        NumberingSchemeProvider provider = BuildProvider(ctx, tenantId);

        NumberingScheme returned = await provider.GetDefaultProductSchemeAsync(default);

        returned.Id.Should().Be(existing.Id);

        using CatalogDbContext verification = BuildContext(options, tenantId);
        (await verification.NumberingSequences.CountAsync()).Should().Be(1);

        NumberingSequence stored = await verification.NumberingSequences.SingleAsync();
        stored.NextNumber.Should().Be(47);
        stored.StartNumber.Should().Be(1);
    }
}
