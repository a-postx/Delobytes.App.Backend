using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Delobytes.App.Backend.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// Tests DataSeeder.SeedJobSchedulesAsync: every active connection must get exactly one
/// OrdersImport schedule (idempotently), so orders import starts running for connections
/// that existed before Stage 11 shipped.
///
/// The DbContext and the MessageTenantContext are always the same instance here, exactly as
/// production wires them (ITenantContext is built on top of MessageTenantContext), because the
/// seeder stamps the new row through that shared context.
/// </summary>
public class DataSeederSeedJobSchedulesTests
{
    private static DbContextOptions<IntegrationsDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseInMemoryDatabase("seed-schedules-" + Guid.NewGuid())
            .Options;
    }

    private static MessageTenantContext BuildTenantContext() => new MessageTenantContext();

    private static SystemChannelTemplate BuildTemplate()
    {
        return new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "wildberries",
            DisplayName = "Wildberries",
            ApiBaseUrl = "https://suppliers-api.wildberries.ru",
            ApiVersion = "v3",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static Connection BuildConnection(Guid templateId, bool isActive = true)
    {
        return new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            SystemChannelTemplateId = templateId,
            Name = "Test connection",
            ApiKey = "test-key",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Seeds one template plus one connection for the given tenant, mirroring the production
    /// requirement that a tenant-scoped row cannot be written without an ambient TenantId.
    /// </summary>
    private static async Task SeedConnectionAsync(
        DbContextOptions<IntegrationsDbContext> options,
        Guid tenantId,
        Connection connection,
        SystemChannelTemplate? template)
    {
        MessageTenantContext tenantContext = BuildTenantContext();
        using IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantContext);
        tenantContext.SetTenantId(tenantId);

        if (template != null)
        {
            ctx.SystemChannelTemplates.Add(template);
        }

        ctx.Connections.Add(connection);
        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Runs the seeder the way startup does: a fresh context with a fresh (empty) tenant
    /// context, so the seeder has to establish the tenant for every connection itself.
    /// </summary>
    private static async Task RunSeederAsync(
        DbContextOptions<IntegrationsDbContext> options,
        BackgroundJobsOptions? backgroundJobsOptions = null)
    {
        MessageTenantContext tenantContext = BuildTenantContext();
        using IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantContext);

        await DataSeeder.SeedJobSchedulesAsync(
            ctx,
            tenantContext,
            backgroundJobsOptions ?? new BackgroundJobsOptions(),
            NullLogger.Instance);
    }

    private static async Task<List<JobSchedule>> ReadSchedulesAsync(DbContextOptions<IntegrationsDbContext> options)
    {
        using IntegrationsDbContext ctx = new IntegrationsDbContext(options, BuildTenantContext());

        return await ctx.JobSchedules.IgnoreQueryFilters().ToListAsync();
    }

    [Fact]
    public async Task SeedJobSchedulesAsync_CreatesOneEnabledHourlyOrdersSchedulePerActiveConnection()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, connection, template);

        await RunSeederAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(1);
        JobSchedule schedule = schedules[0];
        schedule.ConnectionId.Should().Be(connection.Id);
        schedule.JobType.Should().Be(JobType.OrdersImport);
        schedule.CronExpression.Should().Be("0 * * * *");
        schedule.IsEnabled.Should().BeTrue();
        schedule.LastRunAt.Should().BeNull();
        schedule.NextRunAt.Should().NotBeNull();
        schedule.NextRunAt!.Value.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-1));
        schedule.NextRunAt!.Value.Offset.Should().Be(TimeSpan.Zero, "NextRunAt is persisted in UTC");
        schedule.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task SeedJobSchedulesAsync_SkipsInactiveConnections()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate();
        Connection inactive = BuildConnection(template.Id, isActive: false);

        await SeedConnectionAsync(options, tenantId, inactive, template);

        await RunSeederAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().BeEmpty("an inactive connection must not get scheduled work");
    }

    [Fact]
    public async Task SeedJobSchedulesAsync_RunTwice_DoesNotDuplicateSchedules()
    {
        // Startup seeding runs on every boot; the second run must be a no-op.
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, connection, template);

        await RunSeederAsync(options);
        await RunSeederAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(1);
    }

    [Fact]
    public async Task SeedJobSchedulesAsync_SecondRun_KeepsExistingNextRunAtAndLastRunAt()
    {
        // A schedule that already fired must keep its watermark; reseeding it back to
        // "next hour" would silently re-run the same window.
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);
        DateTimeOffset preservedNextRunAt = DateTimeOffset.UtcNow.AddMinutes(17);
        DateTimeOffset preservedLastRunAt = DateTimeOffset.UtcNow.AddHours(-1);

        await SeedConnectionAsync(options, tenantId, connection, template);

        MessageTenantContext seedTenantContext = BuildTenantContext();
        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, seedTenantContext))
        {
            seedTenantContext.SetTenantId(tenantId);
            ctx.JobSchedules.Add(new JobSchedule
            {
                Id = Guid.NewGuid(),
                ConnectionId = connection.Id,
                JobType = JobType.OrdersImport,
                CronExpression = "0 * * * *",
                IsEnabled = true,
                LastRunAt = preservedLastRunAt,
                NextRunAt = preservedNextRunAt,
            });
            await ctx.SaveChangesAsync();
        }

        await RunSeederAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(1);
        schedules[0].NextRunAt.Should().Be(preservedNextRunAt);
        schedules[0].LastRunAt.Should().Be(preservedLastRunAt);
    }

    [Fact]
    public async Task SeedJobSchedulesAsync_DisabledExistingSchedule_IsNotReplacedOrDuplicated()
    {
        // Idempotency is keyed on (ConnectionId, OrdersImport), not on IsEnabled: an operator
        // who disabled a schedule must not have it silently re-enabled on the next boot.
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, connection, template);

        MessageTenantContext seedTenantContext = BuildTenantContext();
        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, seedTenantContext))
        {
            seedTenantContext.SetTenantId(tenantId);
            ctx.JobSchedules.Add(new JobSchedule
            {
                Id = Guid.NewGuid(),
                ConnectionId = connection.Id,
                JobType = JobType.OrdersImport,
                CronExpression = "0 * * * *",
                IsEnabled = false,
                NextRunAt = DateTimeOffset.UtcNow.AddHours(1),
            });
            await ctx.SaveChangesAsync();
        }

        await RunSeederAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(1);
        schedules[0].IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task SeedJobSchedulesAsync_SeedsEveryConnectionThatLacksASchedule()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate();
        Connection first = BuildConnection(template.Id);
        Connection second = BuildConnection(template.Id);
        Connection third = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, first, template);

        MessageTenantContext seedTenantContext = BuildTenantContext();
        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, seedTenantContext))
        {
            seedTenantContext.SetTenantId(tenantId);
            ctx.Connections.AddRange(second, third);
            ctx.JobSchedules.Add(new JobSchedule
            {
                Id = Guid.NewGuid(),
                ConnectionId = first.Id,
                JobType = JobType.OrdersImport,
                CronExpression = "0 * * * *",
                IsEnabled = true,
                NextRunAt = DateTimeOffset.UtcNow.AddMinutes(30),
            });
            await ctx.SaveChangesAsync();
        }

        await RunSeederAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(3);
        schedules.Select(s => s.ConnectionId).Should().BeEquivalentTo(new[] { first.Id, second.Id, third.Id });
    }

    [Fact]
    public async Task SeedJobSchedulesAsync_MultipleConnectionsAcrossTenants_SeedsEachUnderItsOwnTenant()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantAId = Guid.NewGuid();
        Guid tenantBId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate();
        Connection connectionA = BuildConnection(template.Id);
        Connection connectionB = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantAId, connectionA, template);
        await SeedConnectionAsync(options, tenantBId, connectionB, null);

        await RunSeederAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(2);
        schedules.Single(s => s.ConnectionId == connectionA.Id).TenantId.Should().Be(tenantAId);
        schedules.Single(s => s.ConnectionId == connectionB.Id).TenantId.Should().Be(tenantBId);
    }

    [Fact]
    public async Task SeedJobSchedulesAsync_UsesConfiguredCronExpression()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);
        BackgroundJobsOptions backgroundJobsOptions = new BackgroundJobsOptions
        {
            DefaultOrdersImportCronExpression = "*/15 * * * *",
        };

        await SeedConnectionAsync(options, tenantId, connection, template);

        await RunSeederAsync(options, backgroundJobsOptions);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(1);
        schedules[0].CronExpression.Should().Be("*/15 * * * *");
        schedules[0].NextRunAt.Should().NotBeNull();

        // Every 15 minutes: the first run lands within the coming quarter hour.
        schedules[0].NextRunAt!.Value.Should().BeBefore(DateTimeOffset.UtcNow.AddMinutes(16));
    }

    [Fact]
    public async Task SeedJobSchedulesAsync_WhenNoConnectionsExist_SeedsNothingAndDoesNotThrow()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        Func<Task> act = () => RunSeederAsync(options);

        await act.Should().NotThrowAsync();

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);
        schedules.Should().BeEmpty();
    }
}
