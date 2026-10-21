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
                ConnectionId = connection.Id, OldestDayInScope = now.AddDays(-90), NextDayToFetch = now,
                MaxObservedLastChangeDate = now.AddDays(-1),
            };
            await repository.UpsertAsync(state, default);
            context.ChangeTracker.Clear();
            OrdersBackfillState initial = (await repository.GetAsync(connection.Id, default))!;
            initial.CatchUpNextDay.Should().BeNull();
            initial.HandoverWatermark.Should().BeNull();
            initial.CatchUpNextDay = now;
            initial.HandoverWatermark = now.AddMinutes(-30);
            initial.IsCompleted = true;
            initial.CompletedAt = now;
            await repository.UpsertAsync(initial, default);
            context.ChangeTracker.Clear();
            OrdersBackfillState completed = (await repository.GetAsync(connection.Id, default))!;
            completed.CatchUpNextDay.Should().Be(now);
            completed.HandoverWatermark.Should().Be(now.AddMinutes(-30));
            completed.IsCompleted.Should().BeTrue();
            completed.CompletedAt.Should().Be(now);
            completed.MaxObservedLastChangeDate.Should().Be(now.AddDays(-1));
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
            context.Model.FindEntityType(typeof(OrdersBackfillState))!.FindProperty(nameof(OrdersBackfillState.MaxObservedLastChangeDate)).Should().NotBeNull();
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
}
