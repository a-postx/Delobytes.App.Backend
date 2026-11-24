using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;
using Delobytes.App.Backend.Integrations.Infrastructure.SalesChannels;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Persistence;

/// <summary>
/// Tests ConnectionSchedulesBackfill: each active connection created before the runtime provisioner
/// must get exactly one enabled schedule per job type its channel maps to (idempotently) - one
/// orders plus one finance row for Wildberries, FBO plus FBS plus finance for Ozon - and a row that
/// already exists must be left untouched, including a schedule an operator disabled.
///
/// The DbContext and the tenant context are always the same instance here, exactly as production
/// wires them (ILogger and ITenantContext live off the scoped context the repositories use),
/// because the backfill stamps each new row through that shared context.
/// </summary>
public class ConnectionSchedulesBackfillTests
{
    private static DbContextOptions<IntegrationsDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseInMemoryDatabase("schedules-backfill-" + Guid.NewGuid())
            .Options;
    }

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
        TestTenantContext tenantContext = new TestTenantContext();
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
    /// Runs the backfill the way the migrate step does: a fresh context with an empty tenant
    /// context, so the backfill has to establish the tenant for every connection itself.
    /// </summary>
    private static async Task RunBackfillAsync(
        DbContextOptions<IntegrationsDbContext> options,
        BackgroundJobsOptions? backgroundJobsOptions = null,
        ILogger? logger = null)
    {
        TestTenantContext tenantContext = new TestTenantContext();
        using IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantContext);
        JobScheduleRepository scheduleRepository = new JobScheduleRepository(ctx);

        ConnectionScheduleProvisioner provisioner = new ConnectionScheduleProvisioner(
            new Mock<IConnectionRepository>().Object,
            scheduleRepository,
            tenantContext,
            Options.Create(backgroundJobsOptions ?? new BackgroundJobsOptions()),
            NullLogger<ConnectionScheduleProvisioner>.Instance,
            TimeProvider.System,
            new ChannelJobTypeRegistry(new ChannelModule[] { new WildberriesChannelModule(), new OzonChannelModule() }));

        ConnectionSchedulesBackfill backfill = new ConnectionSchedulesBackfill(
            ctx,
            tenantContext,
            provisioner,
            logger as ILogger<ConnectionSchedulesBackfill> ?? NullLogger<ConnectionSchedulesBackfill>.Instance);

        await backfill.RunAsync();
    }

    private static async Task<List<JobSchedule>> ReadSchedulesAsync(DbContextOptions<IntegrationsDbContext> options)
    {
        using IntegrationsDbContext ctx = new IntegrationsDbContext(options, new TestTenantContext());

        return await ctx.JobSchedules.IgnoreQueryFilters().ToListAsync();
    }

    [Fact]
    public async Task Backfill_CreatesOneEnabledOrdersAndOneFinanceSchedulePerActiveConnection()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, connection, template);

        await RunBackfillAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(2);
        schedules.Should().ContainSingle(s => s.JobType == JobType.WbFinanceReportImport);
        JobSchedule schedule = schedules.Single(s => s.JobType == JobType.WbOrdersImport);
        schedule.ConnectionId.Should().Be(connection.Id);
        schedule.CronExpression.Should().Be("0 * * * *");
        schedule.IsEnabled.Should().BeTrue();
        schedule.LastRunAt.Should().BeNull();
        schedule.NextRunAt.Should().NotBeNull();
        schedule.NextRunAt!.Value.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-1));
        schedule.NextRunAt!.Value.Offset.Should().Be(TimeSpan.Zero, "NextRunAt is persisted in UTC");
        schedule.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task Backfill_SkipsInactiveConnections()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate();
        Connection inactive = BuildConnection(template.Id, isActive: false);

        await SeedConnectionAsync(options, tenantId, inactive, template);

        await RunBackfillAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().BeEmpty("an inactive connection must not get scheduled work");
    }

    [Fact]
    public async Task Backfill_RunTwice_DoesNotDuplicateSchedules()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, connection, template);

        await RunBackfillAsync(options);
        await RunBackfillAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(2);
    }

    [Fact]
    public async Task Backfill_SecondRun_KeepsExistingNextRunAtAndLastRunAt()
    {
        // A schedule that already fired must keep its watermark; rewriting it would silently
        // re-run the same window.
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);
        DateTimeOffset preservedNextRunAt = DateTimeOffset.UtcNow.AddMinutes(17);
        DateTimeOffset preservedLastRunAt = DateTimeOffset.UtcNow.AddHours(-1);

        await SeedConnectionAsync(options, tenantId, connection, template);

        JobSchedule existing = BuildSchedule(connection.Id, JobType.WbOrdersImport, enabled: true);
        existing.LastRunAt = preservedLastRunAt;
        existing.NextRunAt = preservedNextRunAt;
        await AddScheduleAsync(options, tenantId, existing);

        await RunBackfillAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(2);
        JobSchedule orders = schedules.Single(s => s.JobType == JobType.WbOrdersImport);
        orders.NextRunAt.Should().Be(preservedNextRunAt);
        orders.LastRunAt.Should().Be(preservedLastRunAt);
    }

    [Fact]
    public async Task Backfill_DisabledExistingSchedule_IsNotReplacedOrDuplicated()
    {
        // Idempotency is keyed on (ConnectionId, JobType), not on IsEnabled: an operator who
        // disabled a schedule must not have it silently re-enabled by the migrate step.
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, connection, template);
        await AddScheduleAsync(options, tenantId, BuildSchedule(connection.Id, JobType.WbOrdersImport, enabled: false));

        await RunBackfillAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(2);
        schedules.Single(s => s.JobType == JobType.WbOrdersImport).IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Backfill_SeedsEveryConnectionThatLacksASchedule()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate();
        Connection first = BuildConnection(template.Id);
        Connection second = BuildConnection(template.Id);
        Connection third = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, first, template);

        TestTenantContext tenantContext = new TestTenantContext();
        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantContext))
        {
            tenantContext.SetTenantId(tenantId);
            ctx.Connections.AddRange(second, third);
            ctx.JobSchedules.Add(BuildSchedule(first.Id, JobType.WbOrdersImport, enabled: true));
            await ctx.SaveChangesAsync();
        }

        await RunBackfillAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(6);
        schedules.Select(s => s.ConnectionId).Distinct().Should().BeEquivalentTo(new[] { first.Id, second.Id, third.Id });
    }

    [Fact]
    public async Task Backfill_MultipleConnectionsAcrossTenants_SeedsEachUnderItsOwnTenant()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantAId = Guid.NewGuid();
        Guid tenantBId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate();
        Connection connectionA = BuildConnection(template.Id);
        Connection connectionB = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantAId, connectionA, template);
        await SeedConnectionAsync(options, tenantBId, connectionB, null);

        await RunBackfillAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(4);
        schedules.First(s => s.ConnectionId == connectionA.Id).TenantId.Should().Be(tenantAId);
        schedules.First(s => s.ConnectionId == connectionB.Id).TenantId.Should().Be(tenantBId);
    }

    [Fact]
    public async Task Backfill_UsesConfiguredCronExpression()
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

        await RunBackfillAsync(options, backgroundJobsOptions);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(2);
        JobSchedule orders = schedules.Single(s => s.JobType == JobType.WbOrdersImport);
        orders.CronExpression.Should().Be("*/15 * * * *");
        orders.NextRunAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Backfill_WhenNoConnectionsExist_SeedsNothingAndDoesNotThrow()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        Func<Task> act = () => RunBackfillAsync(options);

        await act.Should().NotThrowAsync();

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);
        schedules.Should().BeEmpty();
    }

    // -- Channel > orders job type ----------------------------------------------------------------

    [Fact]
    public async Task Backfill_OzonConnection_SeedsBothOzonOrdersTypes()
    {
        // Both schemas share one connection and are always scheduled; sequential execution is
        // enforced by the scheduler, not by a configuration flag deciding which exists.
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildOzonTemplate();
        Connection connection = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, connection, template);

        await RunBackfillAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(3);
        schedules.Select(s => s.JobType).Should().BeEquivalentTo(
            new[] { JobType.OzonOrdersImportFbo, JobType.OzonOrdersImportFbs, JobType.OzonFinanceAccrualsImport });
        schedules.Should().OnlyContain(s => s.IsEnabled);
        schedules.Should().OnlyContain(s => s.ConnectionId == connection.Id);
        schedules.Where(s => s.JobType == JobType.OzonFinanceAccrualsImport)
            .Should().OnlyContain(s => s.CronExpression == "0 9 * * *");
        schedules.Where(s => s.JobType != JobType.OzonFinanceAccrualsImport)
            .Should().OnlyContain(s => s.CronExpression == "0 * * * *");
        schedules.Should().OnlyContain(s => s.TenantId == tenantId);
        schedules.Should().NotContain(s => s.JobType == JobType.WbOrdersImport);
    }

    [Fact]
    public async Task Backfill_WildberriesConnection_SeedsOrdersAndFinanceImportOnly()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, connection, template);

        await RunBackfillAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(2);
        schedules.Should().ContainSingle(s => s.JobType == JobType.WbFinanceReportImport);
        schedules.Single(s => s.JobType == JobType.WbOrdersImport).JobType.Should().Be(JobType.WbOrdersImport);
    }

    [Fact]
    public async Task Backfill_UnknownChannelConnection_SeedsNoOrdersSchedule()
    {
        // A channel with no mapping is not an error: it simply has no orders pipeline yet, and the
        // scheduler must not be handed a job it cannot dispatch.
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildTemplate("yandex.kit", "Yandex Kit", "https://api.kit.yandex.net");
        Connection connection = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, connection, template);

        await RunBackfillAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().BeEmpty();
    }

    [Fact]
    public async Task Backfill_MixedChannels_SeedsEachConnectionItsOwnType()
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

        await RunBackfillAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        // One schedule per job type: Wildberries maps to OrdersImport plus finance, Ozon always
        // maps to both orders schemas plus finance, which the scheduler then serialises on the
        // shared connection.
        schedules.Should().HaveCount(5);
        schedules.Where(s => s.ConnectionId == wildberriesConnection.Id)
            .Select(s => s.JobType)
            .Should().BeEquivalentTo(new[] { JobType.WbOrdersImport, JobType.WbFinanceReportImport });
        schedules.Where(s => s.ConnectionId == ozonConnection.Id)
            .Select(s => s.JobType)
            .Should().BeEquivalentTo(
                new[] { JobType.OzonOrdersImportFbo, JobType.OzonOrdersImportFbs, JobType.OzonFinanceAccrualsImport });
        schedules.Should().OnlyContain(s => s.IsEnabled);
    }

    [Fact]
    public async Task Backfill_OzonConnectionRunTwice_IsIdempotent()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        SystemChannelTemplate template = BuildOzonTemplate();
        Connection connection = BuildConnection(template.Id);

        await SeedConnectionAsync(options, tenantId, connection, template);

        await RunBackfillAsync(options);
        await RunBackfillAsync(options);

        List<JobSchedule> schedules = await ReadSchedulesAsync(options);

        schedules.Should().HaveCount(3);
        schedules.Select(s => s.JobType).Should().BeEquivalentTo(
            new[] { JobType.OzonOrdersImportFbo, JobType.OzonOrdersImportFbs, JobType.OzonFinanceAccrualsImport });
        schedules.Should().OnlyContain(s => s.IsEnabled);
    }

    private static JobSchedule BuildSchedule(Guid connectionId, JobType jobType, bool enabled)
    {
        return new JobSchedule
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            JobType = jobType,
            CronExpression = "0 * * * *",
            IsEnabled = enabled,
            LastRunAt = null,
            NextRunAt = DateTimeOffset.UtcNow.AddMinutes(30),
        };
    }

    private static async Task AddScheduleAsync(
        DbContextOptions<IntegrationsDbContext> options, Guid tenantId, JobSchedule schedule)
    {
        TestTenantContext tenantContext = new TestTenantContext();
        using IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantContext);
        tenantContext.SetTenantId(tenantId);

        ctx.JobSchedules.Add(schedule);
        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Tenant context of the migrate step: settable, with no query filter of its own.
    /// </summary>
    private sealed class TestTenantContext : ITenantContext, ITenantContextSetter
    {
        private Guid? _tenantId;

        public Guid? TenantId => _tenantId;

        public void SetTenantId(Guid? tenantId)
        {
            _tenantId = tenantId;
        }

        public void Clear()
        {
            _tenantId = null;
        }
    }
}
