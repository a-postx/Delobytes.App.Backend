using System;
using System.Collections.Generic;
using System.Linq;
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
/// Verifies JobScheduleRepository: the cross-tenant "due schedules" read the scheduler uses
/// on every tick, and the advance (LastRunAt/NextRunAt) it writes back afterwards.
/// </summary>
public class JobScheduleRepositoryTests
{
    private static DbContextOptions<IntegrationsDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseInMemoryDatabase("job-schedule-repo-" + Guid.NewGuid())
            .Options;
    }

    private static Mock<ITenantContext> TenantMock(Guid tenantId)
    {
        Mock<ITenantContext> mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock;
    }

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

    private static Connection BuildConnection(Guid templateId)
    {
        return new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            SystemChannelTemplateId = templateId,
            Name = "Test connection",
            ApiKey = "test-key",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static JobSchedule BuildSchedule(
        Guid connectionId,
        DateTimeOffset? nextRunAt,
        bool isEnabled = true,
        JobType jobType = JobType.OrdersImport,
        DateTimeOffset? lastRunAt = null)
    {
        return new JobSchedule
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            JobType = jobType,
            CronExpression = "0 * * * *",
            IsEnabled = isEnabled,
            LastRunAt = lastRunAt,
            NextRunAt = nextRunAt,
        };
    }

    private static async Task<Connection> SeedConnectionAsync(
        IntegrationsDbContext ctx,
        SystemChannelTemplate template)
    {
        if (ctx.SystemChannelTemplates.Local.All(t => t.Id != template.Id))
        {
            ctx.SystemChannelTemplates.Add(template);
        }

        Connection connection = BuildConnection(template.Id);
        ctx.Connections.Add(connection);
        await ctx.SaveChangesAsync();
        return connection;
    }

    [Fact]
    public async Task GetDueSchedulesAsync_ReturnsOnlyEnabledSchedulesThatAreDue()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantMock = TenantMock(tenantId);
        SystemChannelTemplate template = BuildTemplate();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Guid dueId;
        Guid futureId;
        Guid disabledId;
        Guid neverScheduledId;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            Connection connection = await SeedConnectionAsync(ctx, template);

            JobSchedule due = BuildSchedule(connection.Id, now.AddMinutes(-1));
            JobSchedule future = BuildSchedule(
                connection.Id,
                now.AddMinutes(30),
                jobType: JobType.ProductsImport);
            JobSchedule disabled = BuildSchedule(connection.Id, now.AddHours(-5), isEnabled: false);
            JobSchedule neverScheduled = BuildSchedule(
                connection.Id,
                nextRunAt: null,
                jobType: JobType.OrdersSync);

            ctx.JobSchedules.AddRange(due, future, disabled, neverScheduled);
            await ctx.SaveChangesAsync();

            dueId = due.Id;
            futureId = future.Id;
            disabledId = disabled.Id;
            neverScheduledId = neverScheduled.Id;
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobScheduleRepository repository = new JobScheduleRepository(ctx);

            List<JobSchedule> dueSchedules = await repository.GetDueSchedulesAsync(now, CancellationToken.None);

            dueSchedules.Should().HaveCount(1);
            dueSchedules[0].Id.Should().Be(dueId);
            dueSchedules.Select(s => s.Id).Should().NotContain(new[] { futureId, disabledId, neverScheduledId });
        }
    }

    [Fact]
    public async Task GetDueSchedulesAsync_WhenNextRunAtEqualsNow_IsTreatedAsDue()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantMock = TenantMock(tenantId);
        SystemChannelTemplate template = BuildTemplate();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            Connection connection = await SeedConnectionAsync(ctx, template);
            ctx.JobSchedules.Add(BuildSchedule(connection.Id, now));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobScheduleRepository repository = new JobScheduleRepository(ctx);

            List<JobSchedule> dueSchedules = await repository.GetDueSchedulesAsync(now, CancellationToken.None);

            dueSchedules.Should().HaveCount(1, "NextRunAt <= now is the boundary the scheduler relies on");
        }
    }

    [Fact]
    public async Task GetDueSchedulesAsync_ReturnsDueSchedulesAcrossAllTenants()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantAId = Guid.NewGuid();
        Guid tenantBId = Guid.NewGuid();
        Mock<ITenantContext> tenantAMock = TenantMock(tenantAId);
        Mock<ITenantContext> tenantBMock = TenantMock(tenantBId);
        SystemChannelTemplate template = BuildTemplate();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantAMock.Object))
        {
            Connection connection = await SeedConnectionAsync(ctx, template);
            ctx.JobSchedules.Add(BuildSchedule(connection.Id, now.AddMinutes(-10)));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantBMock.Object))
        {
            Connection connection = SeedConnectionWithoutTemplate(ctx, template);
            ctx.JobSchedules.Add(BuildSchedule(connection.Id, now.AddMinutes(-20)));
            await ctx.SaveChangesAsync();
        }

        // The scheduler ticks with no ambient tenant set; the query filter would otherwise
        // hide every row and the whole fan-out would silently do nothing.
        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantAMock.Object))
        {
            JobScheduleRepository repository = new JobScheduleRepository(ctx);

            List<JobSchedule> dueSchedules = await repository.GetDueSchedulesAsync(now, CancellationToken.None);

            dueSchedules.Should().HaveCount(2);
            dueSchedules.Select(s => s.TenantId).Should().BeEquivalentTo(new[] { tenantAId, tenantBId });
        }
    }

    [Fact]
    public async Task UpdateAsync_PersistsAdvancedLastRunAtAndNextRunAt()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantMock = TenantMock(tenantId);
        SystemChannelTemplate template = BuildTemplate();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid scheduleId;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            Connection connection = await SeedConnectionAsync(ctx, template);
            JobSchedule schedule = BuildSchedule(connection.Id, now.AddMinutes(-1));
            ctx.JobSchedules.Add(schedule);
            await ctx.SaveChangesAsync();

            scheduleId = schedule.Id;
        }

        DateTimeOffset expectedLastRunAt = now;
        DateTimeOffset expectedNextRunAt = now.AddHours(1);

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobScheduleRepository repository = new JobScheduleRepository(ctx);
            JobSchedule schedule = await ctx.JobSchedules.SingleAsync(s => s.Id == scheduleId);

            schedule.LastRunAt = expectedLastRunAt;
            schedule.NextRunAt = expectedNextRunAt;

            await repository.UpdateAsync(schedule, CancellationToken.None);
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobSchedule persisted = await ctx.JobSchedules.SingleAsync(s => s.Id == scheduleId);

            persisted.LastRunAt.Should().Be(expectedLastRunAt);
            persisted.NextRunAt.Should().Be(expectedNextRunAt);
        }
    }

    [Fact]
    public async Task SaveChanges_StampsTenantId_OnNewlyAddedSchedule()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantMock = TenantMock(tenantId);
        SystemChannelTemplate template = BuildTemplate();
        Guid scheduleId;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            Connection connection = await SeedConnectionAsync(ctx, template);
            JobSchedule schedule = BuildSchedule(connection.Id, DateTimeOffset.UtcNow.AddHours(1));
            ctx.JobSchedules.Add(schedule);
            await ctx.SaveChangesAsync();

            scheduleId = schedule.Id;
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            Guid stampedTenantId = await ctx.JobSchedules
                .IgnoreQueryFilters()
                .Where(s => s.Id == scheduleId)
                .Select(s => s.TenantId)
                .SingleAsync();

            stampedTenantId.Should().Be(
                tenantId,
                "JobSchedule declares a real TenantId, so the SetTenantId guard must stamp it like any shadow property");
        }
    }

    /// <summary>
    /// Seeds a connection for a second tenant without re-adding the template (already
    /// persisted under another tenant's context).
    /// </summary>
    private static Connection SeedConnectionWithoutTemplate(IntegrationsDbContext ctx, SystemChannelTemplate template)
    {
        Connection connection = BuildConnection(template.Id);
        ctx.Connections.Add(connection);
        return connection;
    }
}
