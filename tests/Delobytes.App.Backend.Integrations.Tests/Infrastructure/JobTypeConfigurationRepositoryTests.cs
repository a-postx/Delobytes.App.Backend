using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Infrastructure;

/// <summary>
/// Verifies JobTypeConfigurationRepository: the system-wide reference read the watchdog performs
/// once per JobType on every tick to obtain that type's timeout.
/// </summary>
public class JobTypeConfigurationRepositoryTests
{
    private static DbContextOptions<IntegrationsDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseInMemoryDatabase("job-type-config-repo-" + Guid.NewGuid())
            .Options;
    }

    private static Mock<ITenantContext> TenantMock(Guid tenantId)
    {
        Mock<ITenantContext> mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock;
    }

    private static JobTypeConfiguration BuildConfiguration(JobType jobType, int timeoutMinutes, int batchSize)
    {
        return new JobTypeConfiguration
        {
            JobType = jobType,
            TimeoutMinutes = timeoutMinutes,
            BatchSize = batchSize,
        };
    }

    [Fact]
    public async Task GetByTypeAsync_ReturnsConfigurationForRequestedType()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, TenantMock(Guid.NewGuid()).Object))
        {
            ctx.JobTypeConfigurations.AddRange(
                BuildConfiguration(JobType.ProductsImport, 60, 50),
                BuildConfiguration(JobType.OrdersImport, 30, 100));

            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, TenantMock(Guid.NewGuid()).Object))
        {
            JobTypeConfigurationRepository repository = new JobTypeConfigurationRepository(ctx);

            JobTypeConfiguration? configuration = await repository.GetByTypeAsync(JobType.OrdersImport, CancellationToken.None);

            configuration.Should().NotBeNull();
            configuration!.JobType.Should().Be(JobType.OrdersImport);
            configuration.TimeoutMinutes.Should().Be(30);
            configuration.BatchSize.Should().Be(100);
        }
    }

    [Fact]
    public async Task GetByTypeAsync_ReturnsProductsImportConfiguration_WithSeededValues()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, TenantMock(Guid.NewGuid()).Object))
        {
            ctx.JobTypeConfigurations.Add(BuildConfiguration(JobType.ProductsImport, 60, 50));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, TenantMock(Guid.NewGuid()).Object))
        {
            JobTypeConfigurationRepository repository = new JobTypeConfigurationRepository(ctx);

            JobTypeConfiguration? configuration = await repository.GetByTypeAsync(JobType.ProductsImport, CancellationToken.None);

            configuration.Should().NotBeNull();
            configuration!.TimeoutMinutes.Should().Be(60);
            configuration.BatchSize.Should().Be(50);
        }
    }

    [Fact]
    public async Task GetByTypeAsync_WhenTypeIsNotConfigured_ReturnsNull()
    {
        // The watchdog relies on null (not an exception) to mean "no timeout policy for this
        // type yet", so an unconfigured type is skipped instead of timing out the whole tick.
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, TenantMock(Guid.NewGuid()).Object))
        {
            ctx.JobTypeConfigurations.Add(BuildConfiguration(JobType.ProductsImport, 60, 50));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, TenantMock(Guid.NewGuid()).Object))
        {
            JobTypeConfigurationRepository repository = new JobTypeConfigurationRepository(ctx);

            JobTypeConfiguration? configuration = await repository.GetByTypeAsync(JobType.OrdersSync, CancellationToken.None);

            configuration.Should().BeNull();
        }
    }

    [Fact]
    public async Task GetByTypeAsync_WhenTableIsEmpty_ReturnsNull()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, TenantMock(Guid.NewGuid()).Object))
        {
            JobTypeConfigurationRepository repository = new JobTypeConfigurationRepository(ctx);

            JobTypeConfiguration? configuration = await repository.GetByTypeAsync(JobType.OrdersImport, CancellationToken.None);

            configuration.Should().BeNull();
        }
    }

    [Fact]
    public async Task GetByTypeAsync_RequiresNoAmbientTenant()
    {
        // JobTypeConfiguration is a system reference table, not tenant-scoped: the watchdog
        // reads it from a scope that has no tenant set.
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, TenantMock(Guid.NewGuid()).Object))
        {
            ctx.JobTypeConfigurations.Add(BuildConfiguration(JobType.OrdersImport, 30, 100));
            await ctx.SaveChangesAsync();
        }

        Mock<ITenantContext> noTenantMock = new Mock<ITenantContext>();
        noTenantMock.Setup(t => t.TenantId).Returns((Guid?)null);

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, noTenantMock.Object))
        {
            JobTypeConfigurationRepository repository = new JobTypeConfigurationRepository(ctx);

            JobTypeConfiguration? configuration = await repository.GetByTypeAsync(JobType.OrdersImport, CancellationToken.None);

            configuration.Should().NotBeNull("the reference table must stay readable without a tenant context");
        }
    }
}
