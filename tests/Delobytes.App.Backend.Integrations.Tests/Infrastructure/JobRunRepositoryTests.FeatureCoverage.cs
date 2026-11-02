using System;
using System.Collections.Generic;
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

public partial class JobRunRepositoryTests
{
    [Fact]
    public async Task GetPendingJobsAsync_FiltersBeforeLimit_AndIncludesNullAndExactDeadline()
    {
        Guid tenantId = Guid.NewGuid();
        DateTimeOffset now = new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
        using (IntegrationsDbContext context = new IntegrationsDbContext(BuildOptions(), TenantMock(tenantId).Object))
        {
            Connection connection = BuildConnection(Guid.NewGuid());
            context.Connections.Add(connection);
            JobRun future = BuildJobRun(connection.Id, JobType.OrdersImport, JobStatus.Pending, now.AddHours(-4));
            future.NotBefore = now.AddTicks(1);
            JobRun immediate = BuildJobRun(connection.Id, JobType.OrdersImport, JobStatus.Pending, now.AddHours(-3));
            JobRun due = BuildJobRun(connection.Id, JobType.OrdersImport, JobStatus.Pending, now.AddHours(-2));
            due.NotBefore = now;
            JobRun past = BuildJobRun(connection.Id, JobType.OrdersImport, JobStatus.Pending, now.AddHours(-1));
            past.NotBefore = now.AddTicks(-1);
            context.JobRuns.AddRange(future, immediate, due, past);
            context.JobRuns.Add(BuildJobRun(connection.Id, JobType.OrdersImport, JobStatus.Running, now.AddHours(-5)));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            JobRunRepository repository = new JobRunRepository(context);

            List<JobRun> limited = await repository.GetPendingJobsAsync(tenantId, 2, now, default);
            List<JobRun> all = await repository.GetPendingJobsAsync(tenantId, 10, now, default);

            limited.Should().HaveCount(2);
            limited[0].Id.Should().Be(immediate.Id);
            limited[1].Id.Should().Be(due.Id);
            all.Should().HaveCount(3);
            all.Should().NotContain(j => j.Id == future.Id);
            (await repository.HasActiveJobAsync(connection.Id, JobType.OrdersImport, default)).Should().BeTrue();
            (await repository.CountRunningJobsAsync(tenantId, default)).Should().Be(1);
        }
    }

    [Theory]
    [InlineData(JobStatus.Pending, true)]
    [InlineData(JobStatus.Running, false)]
    [InlineData(JobStatus.Success, false)]
    [InlineData(JobStatus.Failed, false)]
    [InlineData(JobStatus.Cancelled, false)]
    public async Task HasPendingJobAsync_FiltersStatusAndExcludesCurrent(JobStatus status, bool expected)
    {
        Guid tenantId = Guid.NewGuid();
        using (IntegrationsDbContext context = new IntegrationsDbContext(BuildOptions(), TenantMock(tenantId).Object))
        {
            Connection connection = BuildConnection(Guid.NewGuid());
            context.Connections.Add(connection);
            JobRun current = BuildJobRun(connection.Id, JobType.OrdersImport, JobStatus.Pending, DateTimeOffset.UtcNow);
            JobRun other = BuildJobRun(connection.Id, JobType.OrdersImport, status, DateTimeOffset.UtcNow);
            other.NotBefore = DateTimeOffset.UtcNow.AddHours(3);
            context.JobRuns.AddRange(current, other);
            await context.SaveChangesAsync();
            JobRunRepository repository = new JobRunRepository(context);

            (await repository.HasPendingJobAsync(connection.Id, JobType.OrdersImport, current.Id, default)).Should().Be(expected);
            (await repository.HasPendingJobAsync(Guid.NewGuid(), JobType.OrdersImport, current.Id, default)).Should().BeFalse();
            (await repository.HasPendingJobAsync(connection.Id, JobType.ProductsImport, current.Id, default)).Should().BeFalse();
            context.JobRuns.Remove(other);
            await context.SaveChangesAsync();
            (await repository.HasPendingJobAsync(connection.Id, JobType.OrdersImport, current.Id, default)).Should().BeFalse();
        }
    }
    [Fact]
    public async Task BackfillStateRepository_RoundTripsNullableCatchUpAndHandoverFields()
    {
        Guid tenantId = Guid.NewGuid();
        DateTimeOffset now = new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
        using (IntegrationsDbContext context = new IntegrationsDbContext(BuildOptions(), TenantMock(tenantId).Object))
        {
            Connection connection = BuildConnection(Guid.NewGuid());
            context.Connections.Add(connection);
            await context.SaveChangesAsync();
            OrdersBackfillStateRepository repository = new OrdersBackfillStateRepository(context);
            OrdersBackfillState state = new OrdersBackfillState
            {
                ConnectionId = connection.Id, JobType = JobType.OrdersImport,
                OldestDayInScope = now.AddDays(-90), NextDayToFetch = now,
            };
            await repository.UpsertAsync(state, default);
            context.ChangeTracker.Clear();
            OrdersBackfillState initial = (await repository.GetAsync(connection.Id, JobType.OrdersImport, default))!;
            initial.CatchUpNextDay.Should().BeNull();
            initial.HandoverWatermark.Should().BeNull();
            initial.CatchUpNextDay = now;
            initial.HandoverWatermark = now.AddMinutes(-30);
            initial.IsCompleted = true;
            initial.CompletedAt = now;
            await repository.UpsertAsync(initial, default);
            context.ChangeTracker.Clear();
            OrdersBackfillState completed = (await repository.GetAsync(connection.Id, JobType.OrdersImport, default))!;
            completed.CatchUpNextDay.Should().Be(now);
            completed.HandoverWatermark.Should().Be(now.AddMinutes(-30));
            completed.IsCompleted.Should().BeTrue();
            completed.CompletedAt.Should().Be(now);
            (await context.OrdersBackfillStates.CountAsync()).Should().Be(1);
        }
    }

    [Fact]
    public void Model_DeferredAndCatchUpProperties_AreNullableAndHaveNoConfiguredDefault()
    {
        using (IntegrationsDbContext context = new IntegrationsDbContext(BuildOptions(), TenantMock(Guid.NewGuid()).Object))
        {
            Microsoft.EntityFrameworkCore.Metadata.IProperty notBefore = context.Model.FindEntityType(typeof(JobRun))!.FindProperty(nameof(JobRun.NotBefore))!;
            Microsoft.EntityFrameworkCore.Metadata.IProperty catchUp = context.Model.FindEntityType(typeof(OrdersBackfillState))!.FindProperty(nameof(OrdersBackfillState.CatchUpNextDay))!;
            Microsoft.EntityFrameworkCore.Metadata.IProperty handover = context.Model.FindEntityType(typeof(OrdersBackfillState))!.FindProperty(nameof(OrdersBackfillState.HandoverWatermark))!;
            notBefore.IsNullable.Should().BeTrue();
            catchUp.IsNullable.Should().BeTrue();
            handover.IsNullable.Should().BeTrue();
            notBefore.FindAnnotation("Relational:DefaultValue").Should().BeNull();
            notBefore.FindAnnotation("Relational:DefaultValueSql").Should().BeNull();
        }
    }
    [Fact]
    public async Task DeferredPending_RoundTripsNotBefore_BlocksScheduleWithoutConsumingRunningSlot()
    {
        Guid tenantId = Guid.NewGuid();
        DateTimeOffset now = new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
        using (IntegrationsDbContext context = new IntegrationsDbContext(BuildOptions(), TenantMock(tenantId).Object))
        {
            Connection connection = BuildConnection(Guid.NewGuid());
            context.Connections.Add(connection);
            JobRun run = BuildJobRun(connection.Id, JobType.OrdersImport, JobStatus.Pending, now);
            run.NotBefore = now.AddHours(3);
            context.JobRuns.Add(run);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            JobRunRepository repository = new JobRunRepository(context);

            (await repository.GetByIdAsync(run.Id, default))!.NotBefore.Should().Be(now.AddHours(3));
            (await repository.HasActiveJobAsync(connection.Id, JobType.OrdersImport, default)).Should().BeTrue();
            (await repository.CountRunningJobsAsync(tenantId, default)).Should().Be(0);
            (await repository.GetPendingJobsAsync(tenantId, 1, now, default)).Should().BeEmpty();
            (await repository.GetPendingJobsAsync(tenantId, 1, now.AddHours(3), default)).Should().ContainSingle();
        }
    }

    // ── HasActiveOzonOrdersJobAsync: the cross-schema gate the scheduler serialises on ─────────

    [Theory]
    [InlineData(JobType.OrdersImportFbo, JobStatus.Running)]
    [InlineData(JobType.OrdersImportFbo, JobStatus.Pending)]
    [InlineData(JobType.OrdersImportFbs, JobStatus.Running)]
    [InlineData(JobType.OrdersImportFbs, JobStatus.Pending)]
    public async Task HasActiveOzonOrdersJobAsync_ReturnsTrue_ForEitherSchemaInEitherActiveStatus(
        JobType existingJobType, JobStatus existingStatus)
    {
        Guid tenantId = Guid.NewGuid();
        using (IntegrationsDbContext context = new IntegrationsDbContext(BuildOptions(), TenantMock(tenantId).Object))
        {
            Connection connection = BuildConnection(Guid.NewGuid());
            context.Connections.Add(connection);
            context.JobRuns.Add(BuildJobRun(connection.Id, existingJobType, existingStatus, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
            JobRunRepository repository = new JobRunRepository(context);

            (await repository.HasActiveOzonOrdersJobAsync(connection.Id, default)).Should().BeTrue(
                "an active FBO job must block FBS and vice versa");
        }
    }

    [Fact]
    public async Task HasActiveOzonOrdersJobAsync_ReturnsTrue_WhenFboIsRunningAndTheFbsScheduleIsChecked()
    {
        // The exact case the scheduler relies on: FBO occupies the connection, so the FBS schedule
        // has to be skipped rather than creating a second JobRun on the same rate-limiter bucket.
        Guid tenantId = Guid.NewGuid();
        using (IntegrationsDbContext context = new IntegrationsDbContext(BuildOptions(), TenantMock(tenantId).Object))
        {
            Connection connection = BuildConnection(Guid.NewGuid());
            Connection otherConnection = BuildConnection(Guid.NewGuid());
            context.Connections.AddRange(connection, otherConnection);
            context.JobRuns.Add(BuildJobRun(connection.Id, JobType.OrdersImportFbo, JobStatus.Running, DateTimeOffset.UtcNow));
            context.JobRuns.Add(BuildJobRun(otherConnection.Id, JobType.OrdersImportFbs, JobStatus.Running, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
            JobRunRepository repository = new JobRunRepository(context);

            (await repository.HasActiveOzonOrdersJobAsync(connection.Id, default)).Should().BeTrue();
            (await repository.HasActiveJobAsync(connection.Id, JobType.OrdersImportFbs, default)).Should().BeFalse(
                "the per-type check is what misses the collision; the cross-schema one is what catches it");
        }
    }

    [Theory]
    [InlineData(JobStatus.Success)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    public async Task HasActiveOzonOrdersJobAsync_ReturnsFalse_ForTerminalStatuses(JobStatus status)
    {
        Guid tenantId = Guid.NewGuid();
        using (IntegrationsDbContext context = new IntegrationsDbContext(BuildOptions(), TenantMock(tenantId).Object))
        {
            Connection connection = BuildConnection(Guid.NewGuid());
            context.Connections.Add(connection);
            context.JobRuns.Add(BuildJobRun(connection.Id, JobType.OrdersImportFbo, status, DateTimeOffset.UtcNow));
            context.JobRuns.Add(BuildJobRun(connection.Id, JobType.OrdersImportFbs, status, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
            JobRunRepository repository = new JobRunRepository(context);

            (await repository.HasActiveOzonOrdersJobAsync(connection.Id, default)).Should().BeFalse(
                "a finished run must not keep the connection locked");
        }
    }

    [Theory]
    [InlineData(JobType.OrdersImport)]
    [InlineData(JobType.ProductsImport)]
    [InlineData(JobType.OrdersSync)]
    public async Task HasActiveOzonOrdersJobAsync_IgnoresOtherJobTypes(JobType jobType)
    {
        Guid tenantId = Guid.NewGuid();
        using (IntegrationsDbContext context = new IntegrationsDbContext(BuildOptions(), TenantMock(tenantId).Object))
        {
            Connection connection = BuildConnection(Guid.NewGuid());
            context.Connections.Add(connection);
            context.JobRuns.Add(BuildJobRun(connection.Id, jobType, JobStatus.Running, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
            JobRunRepository repository = new JobRunRepository(context);

            (await repository.HasActiveOzonOrdersJobAsync(connection.Id, default)).Should().BeFalse(
                "only the two Ozon orders schemas share the FBO/FBS collision this gate exists for");
            (await repository.HasActiveJobAsync(connection.Id, jobType, default)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task HasActiveOzonOrdersJobAsync_IsScopedToOneConnection()
    {
        Guid tenantId = Guid.NewGuid();
        using (IntegrationsDbContext context = new IntegrationsDbContext(BuildOptions(), TenantMock(tenantId).Object))
        {
            Connection busy = BuildConnection(Guid.NewGuid());
            Connection idle = BuildConnection(Guid.NewGuid());
            context.Connections.AddRange(busy, idle);
            context.JobRuns.Add(BuildJobRun(busy.Id, JobType.OrdersImportFbs, JobStatus.Running, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
            JobRunRepository repository = new JobRunRepository(context);

            (await repository.HasActiveOzonOrdersJobAsync(idle.Id, default)).Should().BeFalse(
                "sequential execution is enforced per connection, not across a tenant's Ozon connections");
            (await repository.HasActiveOzonOrdersJobAsync(Guid.NewGuid(), default)).Should().BeFalse();
        }
    }
}
