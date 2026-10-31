using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// Tests DataSeeder.SeedJobTypeConfigurationsAsync: the reference rows that give the watchdog its
/// per-type timeout. They must appear exactly once, carry the agreed values, and never clobber a
/// value an operator has tuned by hand.
/// </summary>
public class DataSeederSeedJobTypeConfigurationsTests
{
    private static DbContextOptions<IntegrationsDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseInMemoryDatabase("seed-job-type-config-" + Guid.NewGuid())
            .Options;
    }

    private static Mock<ITenantContext> NoTenantMock()
    {
        Mock<ITenantContext> mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns((Guid?)null);
        return mock;
    }

    private static async Task RunSeederAsync(DbContextOptions<IntegrationsDbContext> options)
    {
        using IntegrationsDbContext ctx = new IntegrationsDbContext(options, NoTenantMock().Object);

        await DataSeeder.SeedJobTypeConfigurationsAsync(ctx, NullLogger.Instance);
    }

    private static async Task<List<JobTypeConfiguration>> ReadConfigurationsAsync(
        DbContextOptions<IntegrationsDbContext> options)
    {
        using IntegrationsDbContext ctx = new IntegrationsDbContext(options, NoTenantMock().Object);

        return await ctx.JobTypeConfigurations.AsNoTracking().ToListAsync();
    }

    [Fact]
    public async Task SeedJobTypeConfigurationsAsync_CreatesExactlyTheConfiguredTypes()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        await RunSeederAsync(options);

        List<JobTypeConfiguration> configurations = await ReadConfigurationsAsync(options);

        configurations.Should().HaveCount(4);
        configurations.Select(c => c.JobType)
            .Should().BeEquivalentTo(new[]
            {
                JobType.ProductsImport,
                JobType.OrdersImport,
                JobType.OrdersImportFbo,
                JobType.OrdersImportFbs,
            });
    }

    [Theory]
    [InlineData(JobType.OrdersImportFbo)]
    [InlineData(JobType.OrdersImportFbs)]
    public async Task SeedJobTypeConfigurationsAsync_OrdersSchemaTypes_UseThirtyMinuteTimeoutAndBatchOfOneHundred(
        JobType jobType)
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        await RunSeederAsync(options);

        JobTypeConfiguration configuration = (await ReadConfigurationsAsync(options))
            .Single(c => c.JobType == jobType);

        configuration.TimeoutMinutes.Should().Be(30);
        configuration.BatchSize.Should().Be(100);
    }

    [Fact]
    public async Task SeedJobTypeConfigurationsAsync_ProductsImport_UsesSixtyMinuteTimeoutAndBatchOfFifty()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        await RunSeederAsync(options);

        JobTypeConfiguration configuration = (await ReadConfigurationsAsync(options))
            .Single(c => c.JobType == JobType.ProductsImport);

        configuration.TimeoutMinutes.Should().Be(60);
        configuration.BatchSize.Should().Be(50);
    }

    [Fact]
    public async Task SeedJobTypeConfigurationsAsync_OrdersImport_UsesThirtyMinuteTimeoutAndBatchOfOneHundred()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        await RunSeederAsync(options);

        JobTypeConfiguration configuration = (await ReadConfigurationsAsync(options))
            .Single(c => c.JobType == JobType.OrdersImport);

        configuration.TimeoutMinutes.Should().Be(30);
        configuration.BatchSize.Should().Be(100);
    }

    [Fact]
    public async Task SeedJobTypeConfigurationsAsync_DoesNotSeedTypesWithoutATimeoutPolicy()
    {
        // OrdersSync is reserved for the future reconciliation task type; seeding it now would
        // silently give the watchdog authority to fail jobs of a type nobody has sized yet.
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        await RunSeederAsync(options);

        List<JobTypeConfiguration> configurations = await ReadConfigurationsAsync(options);

        configurations.Should().NotContain(c => c.JobType == JobType.OrdersSync);
    }

    [Fact]
    public async Task SeedJobTypeConfigurationsAsync_RunTwice_DoesNotDuplicateRows()
    {
        // Startup seeding runs on every boot; the second run must be a no-op.
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        await RunSeederAsync(options);
        await RunSeederAsync(options);

        List<JobTypeConfiguration> configurations = await ReadConfigurationsAsync(options);

        configurations.Should().HaveCount(4);
    }

    [Fact]
    public async Task SeedJobTypeConfigurationsAsync_SecondRun_KeepsOperatorAdjustedValues()
    {
        // A tuned timeout (an operator raising the products import ceiling) must survive a
        // restart; reseeding it back to the default would silently re-introduce the timeouts
        // the operator was working around.
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, NoTenantMock().Object))
        {
            ctx.JobTypeConfigurations.Add(new JobTypeConfiguration
            {
                JobType = JobType.ProductsImport,
                TimeoutMinutes = 45,
                BatchSize = 25,
            });

            await ctx.SaveChangesAsync();
        }

        await RunSeederAsync(options);

        List<JobTypeConfiguration> configurations = await ReadConfigurationsAsync(options);

        configurations.Should().HaveCount(4);

        JobTypeConfiguration products = configurations.Single(c => c.JobType == JobType.ProductsImport);
        products.TimeoutMinutes.Should().Be(45);
        products.BatchSize.Should().Be(25);
    }

    [Fact]
    public async Task SeedJobTypeConfigurationsAsync_WhenOneTypeAlreadyExists_SeedsOnlyTheMissingOne()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, NoTenantMock().Object))
        {
            ctx.JobTypeConfigurations.Add(new JobTypeConfiguration
            {
                JobType = JobType.OrdersImport,
                TimeoutMinutes = 30,
                BatchSize = 100,
            });

            await ctx.SaveChangesAsync();
        }

        await RunSeederAsync(options);

        List<JobTypeConfiguration> configurations = await ReadConfigurationsAsync(options);

        configurations.Should().HaveCount(4);
        configurations.Single(c => c.JobType == JobType.ProductsImport).TimeoutMinutes.Should().Be(60);
    }
}
