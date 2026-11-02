using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Extensions;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Delobytes.App.Backend.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// Tests DataSeeder.SeedJobSchedulesAsync: each active connection must get exactly one enabled
/// orders schedule per job type its channel maps to (idempotently) - one for Wildberries, FBO plus
/// FBS for Ozon - and a stale
/// <see cref="JobType.OrdersImport"/> schedule left on a non-Wildberries connection by earlier
/// versions of the seeder must be disabled rather than deleted.
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

    private static SystemChannelTemplate BuildTemplate(
        string code = "wildberries", string displayName = "Wildberries", string apiBaseUrl = "https://suppliers-api.wildberries.ru")
    {
        return new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = code,
            DisplayName = displayName,
            ApiBaseUrl = apiBaseUrl,
            ApiVersion = "v3",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static SystemChannelTemplate BuildOzonTemplate()
    {
        return BuildTemplate("ozon", "Ozon", "https://api-seller.ozon.ru");
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
        BackgroundJobsOptions? backgroundJobsOptions = null,
        ILogger? logger = null)
    {
        MessageTenantContext tenantContext = BuildTenantContext();
        using IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantContext);

        await DataSeeder.SeedJobSchedulesAsync(
            ctx,
            tenantContext,
            backgroundJobsOptions ?? new BackgroundJobsOptions(),
            logger ?? NullLogger.Instance);
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

    // ── Channel → orders job type ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedJobSchedulesAsync_OzonConnection_SeedsBothOzonOrdersTypes()
    {
        // Both schemas share one connection and are always scheduled; sequential execution is
        // enforced by the scheduler, not by a configuration flag deciding which exists.
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildOzonTemplate();
        Connection connection = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, connection, template);

        await RunSeederAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(2);
        schedules.Select(s => s.JobType).Should().BeEquivalentTo(
            new[] { JobType.OrdersImportFbo, JobType.OrdersImportFbs });
        schedules.Should().OnlyContain(s => s.IsEnabled);
        schedules.Should().OnlyContain(s => s.ConnectionId == connection.Id);
        schedules.Should().OnlyContain(s => s.CronExpression == "0 * * * *");
        schedules.Should().OnlyContain(s => s.TenantId == tenantId);
        schedules.Should().NotContain(s => s.JobType == JobType.OrdersImport);
    }

    [Fact]
    public async Task SeedJobSchedulesAsync_WildberriesConnection_SeedsOrdersImportOnly()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, connection, template);

        await RunSeederAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().ContainSingle();
        schedules[0].JobType.Should().Be(JobType.OrdersImport);
    }

    [Fact]
    public async Task SeedJobSchedulesAsync_UnknownChannelConnection_SeedsNoOrdersSchedule()
    {
        // A channel with no mapping is not an error at startup: it simply has no orders
        // pipeline yet, and the scheduler must not be handed a job it cannot dispatch.
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate("yandex.kit", "Yandex Kit", "https://api.kit.yandex.net");
        Connection connection = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, connection, template);

        await RunSeederAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().BeEmpty();
    }

    [Fact]
    public async Task SeedJobSchedulesAsync_MixedChannels_SeedsEachConnectionItsOwnType()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid wildberriesTenantId = Guid.NewGuid();
        Guid ozonTenantId = Guid.NewGuid();
        SystemChannelTemplate wildberriesTemplate = BuildTemplate();
        SystemChannelTemplate ozonTemplate = BuildOzonTemplate();
        Connection wildberriesConnection = BuildConnection(wildberriesTemplate.Id);
        Connection ozonConnection = BuildConnection(ozonTemplate.Id);

        await SeedConnectionAsync(options, wildberriesTenantId, wildberriesConnection, wildberriesTemplate);
        await SeedConnectionAsync(options, ozonTenantId, ozonConnection, ozonTemplate);

        await RunSeederAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        // One schedule per job type: Wildberries maps to OrdersImport alone, Ozon always maps to
        // both schemas, which the scheduler then serialises on the shared connection.
        schedules.Should().HaveCount(3);
        schedules.Where(s => s.ConnectionId == wildberriesConnection.Id)
            .Select(s => s.JobType)
            .Should().BeEquivalentTo(new[] { JobType.OrdersImport });
        schedules.Where(s => s.ConnectionId == ozonConnection.Id)
            .Select(s => s.JobType)
            .Should().BeEquivalentTo(new[] { JobType.OrdersImportFbo, JobType.OrdersImportFbs });
        schedules.Should().OnlyContain(s => s.IsEnabled);
    }

    [Fact]
    public async Task SeedJobSchedulesAsync_OzonConnectionRunTwice_IsIdempotent()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildOzonTemplate();
        Connection connection = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, connection, template);

        await RunSeederAsync(options);
        await RunSeederAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(2);
        schedules.Select(s => s.JobType).Should().BeEquivalentTo(
            new[] { JobType.OrdersImportFbo, JobType.OrdersImportFbs });
        schedules.Should().OnlyContain(s => s.IsEnabled);
    }

    // ── Cleanup of what earlier versions of the seeder created ──────────────────────────────────

    [Fact]
    public async Task SeedJobSchedulesAsync_StaleOrdersImportOnOzonConnection_IsDisabledAndLoggedAsWarning()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildOzonTemplate();
        Connection connection = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, connection, template);
        await AddScheduleAsync(options, tenantId, BuildSchedule(connection.Id, JobType.OrdersImport));

        RecordingLogger logger = new RecordingLogger();

        await RunSeederAsync(options, logger: logger);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        JobSchedule stale = schedules.Single(s => s.JobType == JobType.OrdersImport);
        stale.IsEnabled.Should().BeFalse("the Ozon connection runs OrdersImportFbo, not the Wildberries job");
        schedules.Should().Contain(s => s.JobType == JobType.OrdersImportFbo && s.IsEnabled);

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains(stale.Id.ToString())
            && e.Message.Contains(connection.Id.ToString()));
    }

    [Fact]
    public async Task SeedJobSchedulesAsync_SecondRunAfterStaleCleanup_DoesNotWarnAgain()
    {
        // The cleanup must be idempotent on its own: once the stale row is disabled it is out of
        // scope, so operators do not get the same warning on every boot.
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildOzonTemplate();
        Connection connection = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, connection, template);
        await AddScheduleAsync(options, tenantId, BuildSchedule(connection.Id, JobType.OrdersImport));

        await RunSeederAsync(options);

        RecordingLogger secondRunLogger = new RecordingLogger();
        await RunSeederAsync(options, logger: secondRunLogger);

        secondRunLogger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task SeedJobSchedulesAsync_OrdersImportOnWildberriesConnection_IsLeftEnabled()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, connection, template);
        await AddScheduleAsync(options, tenantId, BuildSchedule(connection.Id, JobType.OrdersImport));

        RecordingLogger logger = new RecordingLogger();

        await RunSeederAsync(options, logger: logger);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().ContainSingle();
        schedules[0].JobType.Should().Be(JobType.OrdersImport);
        schedules[0].IsEnabled.Should().BeTrue();
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    private static JobSchedule BuildSchedule(Guid connectionId, JobType jobType)
    {
        return new JobSchedule
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            JobType = jobType,
            CronExpression = "0 * * * *",
            IsEnabled = true,
            LastRunAt = null,
            NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        };
    }

    private static async Task AddScheduleAsync(
        DbContextOptions<IntegrationsDbContext> options, Guid tenantId, JobSchedule schedule)
    {
        MessageTenantContext tenantContext = BuildTenantContext();
        using IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantContext);
        tenantContext.SetTenantId(tenantId);

        ctx.JobSchedules.Add(schedule);
        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Keeps every formatted log entry so the cleanup can be asserted on the warning itself,
    /// not only on the resulting row.
    /// </summary>
    private sealed class RecordingLogger : ILogger
    {
        private readonly List<(LogLevel Level, string Message)> _entries = new List<(LogLevel Level, string Message)>();

        public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
