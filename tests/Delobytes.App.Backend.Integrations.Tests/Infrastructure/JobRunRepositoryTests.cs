using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;
using Delobytes.App.Backend.Integrations.Tests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Infrastructure;

/// <summary>
/// Verifies JobRunRepository query and persistence behaviour: tenant-scoped reads for
/// the worker, cross-tenant reads for the watchdog, and the active-job check used to
/// enforce one Pending/Running job per (ConnectionId, JobType).
/// </summary>
public partial class JobRunRepositoryTests
{

    [Fact]
    public async Task CreateAsync_PersistsJobRun_AndReturnsIt()
    {
        DbContextOptions<IntegrationsDbContext> options = IntegrationsTestData.Options("jobrun-repo");
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantMock = IntegrationsTestData.Tenant(tenantId);
        SystemChannelTemplate template = IntegrationsTestData.Template();
        Connection connection = IntegrationsTestData.Connection(template.Id);

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connection);
            await ctx.SaveChangesAsync();
        }

        Guid jobRunId;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);
            JobRun jobRun = IntegrationsTestData.JobRun(connection.Id, JobType.ProductsImport, JobStatus.Pending, DateTimeOffset.UtcNow);

            JobRun created = await repository.CreateAsync(jobRun, CancellationToken.None);

            created.Should().BeSameAs(jobRun);
            jobRunId = created.Id;
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);
            JobRun? persisted = await repository.GetByIdAsync(jobRunId, CancellationToken.None);

            persisted.Should().NotBeNull();
            persisted!.ConnectionId.Should().Be(connection.Id);
            persisted.Status.Should().Be(JobStatus.Pending);
        }
    }

    [Fact]
    public async Task HasActiveJobAsync_ReturnsTrue_ForPendingOrRunning_AndFalse_ForTerminalStatuses()
    {
        DbContextOptions<IntegrationsDbContext> options = IntegrationsTestData.Options("jobrun-repo");
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantMock = IntegrationsTestData.Tenant(tenantId);
        SystemChannelTemplate template = IntegrationsTestData.Template();
        Connection connectionWithActiveJob = IntegrationsTestData.Connection(template.Id);
        Connection connectionWithTerminalJob = IntegrationsTestData.Connection(template.Id);

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.AddRange(connectionWithActiveJob, connectionWithTerminalJob);
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(connectionWithActiveJob.Id, JobType.ProductsImport, JobStatus.Running, DateTimeOffset.UtcNow));
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(connectionWithTerminalJob.Id, JobType.ProductsImport, JobStatus.Success, DateTimeOffset.UtcNow));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);

            bool hasActive = await repository.HasActiveJobAsync(connectionWithActiveJob.Id, JobType.ProductsImport, CancellationToken.None);
            bool hasTerminalOnly = await repository.HasActiveJobAsync(connectionWithTerminalJob.Id, JobType.ProductsImport, CancellationToken.None);

            hasActive.Should().BeTrue("a Running job blocks new ones for the same connection/type");
            hasTerminalOnly.Should().BeFalse("a Success job must not block a new import");
        }
    }

    [Fact]
    public async Task GetPendingJobsAsync_ReturnsOnlyPendingJobs_ForGivenTenant_OrderedByCreatedAt_RespectingLimit()
    {
        DbContextOptions<IntegrationsDbContext> options = IntegrationsTestData.Options("jobrun-repo");
        Guid tenantAId = Guid.NewGuid();
        Guid tenantBId = Guid.NewGuid();
        Mock<ITenantContext> tenantAMock = IntegrationsTestData.Tenant(tenantAId);
        Mock<ITenantContext> tenantBMock = IntegrationsTestData.Tenant(tenantBId);
        SystemChannelTemplate template = IntegrationsTestData.Template();

        Connection connectionA = IntegrationsTestData.Connection(template.Id);
        Connection connectionA2 = IntegrationsTestData.Connection(template.Id);
        Connection connectionB = IntegrationsTestData.Connection(template.Id);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantAMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.AddRange(connectionA, connectionA2);
            // Older Pending job for tenant A, created first.
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(connectionA.Id, JobType.ProductsImport, JobStatus.Pending, now.AddMinutes(-10)));
            // Newer Pending job for tenant A.
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(connectionA2.Id, JobType.ProductsImport, JobStatus.Pending, now.AddMinutes(-5)));
            // Running job for tenant A must be excluded.
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(connectionA.Id, JobType.ProductsImport, JobStatus.Running, now.AddMinutes(-1)));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantBMock.Object))
        {
            ctx.Connections.Add(connectionB);
            // Pending job for tenant B must never leak into tenant A's results.
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(connectionB.Id, JobType.ProductsImport, JobStatus.Pending, now.AddMinutes(-20)));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantAMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);

            List<JobRun> pending = await repository.GetPendingJobsAsync(tenantAId, limit: 10, now, CancellationToken.None);

            pending.Should().HaveCount(2);
            pending[0].ConnectionId.Should().Be(connectionA.Id, "oldest Pending job must come first");
            pending[1].ConnectionId.Should().Be(connectionA2.Id);

            List<JobRun> limited = await repository.GetPendingJobsAsync(tenantAId, limit: 1, now, CancellationToken.None);
            limited.Should().HaveCount(1);
            limited[0].ConnectionId.Should().Be(connectionA.Id);
        }
    }

    [Fact]
    public async Task GetRunningJobsOlderThanAsync_ReturnsStuckJobs_AcrossAllTenants_IgnoringQueryFilter()
    {
        DbContextOptions<IntegrationsDbContext> options = IntegrationsTestData.Options("jobrun-repo");
        Guid tenantAId = Guid.NewGuid();
        Guid tenantBId = Guid.NewGuid();
        Mock<ITenantContext> tenantAMock = IntegrationsTestData.Tenant(tenantAId);
        Mock<ITenantContext> tenantBMock = IntegrationsTestData.Tenant(tenantBId);
        SystemChannelTemplate template = IntegrationsTestData.Template();

        Connection connectionA = IntegrationsTestData.Connection(template.Id);
        Connection connectionB = IntegrationsTestData.Connection(template.Id);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset threshold = now.AddHours(-1);

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantAMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connectionA);
            // Stuck job for tenant A: started well before the threshold.
            JobRun stuckJob = IntegrationsTestData.JobRun(connectionA.Id, JobType.ProductsImport, JobStatus.Running, now.AddHours(-3));
            stuckJob.StartedAt = now.AddHours(-3);
            ctx.JobRuns.Add(stuckJob);

            // Fresh Running job, still within timeout, must be excluded.
            JobRun freshJob = IntegrationsTestData.JobRun(connectionA.Id, JobType.OzonOrdersImportFbo, JobStatus.Running, now.AddMinutes(-5));
            freshJob.StartedAt = now.AddMinutes(-5);
            ctx.JobRuns.Add(freshJob);

            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantBMock.Object))
        {
            ctx.Connections.Add(connectionB);
            // Stuck job belonging to a different tenant must still be found by the watchdog.
            JobRun stuckJobTenantB = IntegrationsTestData.JobRun(connectionB.Id, JobType.ProductsImport, JobStatus.Running, now.AddHours(-2));
            stuckJobTenantB.StartedAt = now.AddHours(-2);
            ctx.JobRuns.Add(stuckJobTenantB);

            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantAMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);

            List<JobRun> stuckJobs = await repository.GetRunningJobsOlderThanAsync(threshold, CancellationToken.None);

            stuckJobs.Should().HaveCount(2, "the watchdog must see stuck Running jobs for every tenant");
            stuckJobs.Should().Contain(j => j.ConnectionId == connectionA.Id);
            stuckJobs.Should().Contain(j => j.ConnectionId == connectionB.Id);
        }
    }

    [Fact]
    public async Task GetPendingJobsOlderThanAsync_ReturnsOnlyStalePendingJobs_AcrossAllTenants()
    {
        DbContextOptions<IntegrationsDbContext> options = IntegrationsTestData.Options("jobrun-repo");
        Guid tenantAId = Guid.NewGuid();
        Guid tenantBId = Guid.NewGuid();
        Mock<ITenantContext> tenantAMock = IntegrationsTestData.Tenant(tenantAId);
        Mock<ITenantContext> tenantBMock = IntegrationsTestData.Tenant(tenantBId);
        SystemChannelTemplate template = IntegrationsTestData.Template();

        Connection connectionA = IntegrationsTestData.Connection(template.Id);
        Connection connectionB = IntegrationsTestData.Connection(template.Id);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset createdBefore = now.AddMinutes(-120);

        Guid staleJobId;
        Guid freshJobId;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantAMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connectionA);

            JobRun staleJob = IntegrationsTestData.JobRun(connectionA.Id, JobType.WbOrdersImport, JobStatus.Pending, now.AddHours(-11));
            ctx.JobRuns.Add(staleJob);

            JobRun freshJob = IntegrationsTestData.JobRun(connectionA.Id, JobType.ProductsImport, JobStatus.Pending, now.AddMinutes(-10));
            ctx.JobRuns.Add(freshJob);

            JobRun runningJob = IntegrationsTestData.JobRun(connectionA.Id, JobType.OzonOrdersImportFbo, JobStatus.Running, now.AddHours(-11));
            runningJob.StartedAt = now.AddHours(-11);
            ctx.JobRuns.Add(runningJob);

            ctx.JobRuns.Add(IntegrationsTestData.JobRun(connectionA.Id, JobType.WbFinanceReportImport, JobStatus.Success, now.AddHours(-11)));

            await ctx.SaveChangesAsync();

            staleJobId = staleJob.Id;
            freshJobId = freshJob.Id;
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantBMock.Object))
        {
            ctx.Connections.Add(connectionB);
            // A never-dispatched Pending row of another tenant is the case the watchdog exists for.
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(connectionB.Id, JobType.WbOrdersImport, JobStatus.Pending, now.AddHours(-12)));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantAMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);

            List<JobRun> stalePending = await repository.GetPendingJobsOlderThanAsync(
                createdBefore,
                now,
                CancellationToken.None);

            stalePending.Should().HaveCount(2, "the watchdog must see stale Pending rows for every tenant");
            stalePending.Should().Contain(j => j.Id == staleJobId);
            stalePending.Should().Contain(j => j.ConnectionId == connectionB.Id);
            stalePending.Should().OnlyContain(j => j.Status == JobStatus.Pending);
            stalePending.Select(j => j.Id).Should().NotContain(freshJobId);
        }
    }

    [Fact]
    public async Task GetPendingJobsOlderThanAsync_SkipsRowsDeferredToTheFuture_ButReturnsExpiredDeferrals()
    {
        DbContextOptions<IntegrationsDbContext> options = IntegrationsTestData.Options("jobrun-repo");
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantMock = IntegrationsTestData.Tenant(tenantId);
        SystemChannelTemplate template = IntegrationsTestData.Template();
        Connection connection = IntegrationsTestData.Connection(template.Id);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset createdBefore = now.AddMinutes(-120);

        Guid deferredJobId;
        Guid expiredDeferralJobId;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connection);

            JobRun deferredJob = IntegrationsTestData.JobRun(connection.Id, JobType.WbOrdersImport, JobStatus.Pending, now.AddHours(-5));
            deferredJob.NotBefore = now.AddMinutes(30);
            ctx.JobRuns.Add(deferredJob);

            JobRun expiredDeferralJob = IntegrationsTestData.JobRun(connection.Id, JobType.WbFinanceReportImport, JobStatus.Pending, now.AddHours(-5));
            expiredDeferralJob.NotBefore = now.AddHours(-3);
            ctx.JobRuns.Add(expiredDeferralJob);

            await ctx.SaveChangesAsync();

            deferredJobId = deferredJob.Id;
            expiredDeferralJobId = expiredDeferralJob.Id;
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);

            List<JobRun> stalePending = await repository.GetPendingJobsOlderThanAsync(
                createdBefore,
                now,
                CancellationToken.None);

            stalePending.Select(j => j.Id).Should().Contain(
                expiredDeferralJobId,
                "a deferral that has already passed no longer explains a Pending row");
            stalePending.Select(j => j.Id).Should().NotContain(
                deferredJobId,
                "a run waiting on the rate limiter is not stuck");
        }
    }

    [Fact]
    public async Task GetPendingJobsOlderThanAsync_MeasuresTheWaitingPeriodFromTheDeferral_NotFromCreation()
    {
        DbContextOptions<IntegrationsDbContext> options = IntegrationsTestData.Options("jobrun-repo");
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantMock = IntegrationsTestData.Tenant(tenantId);
        SystemChannelTemplate template = IntegrationsTestData.Template();
        Connection resumedConnection = IntegrationsTestData.Connection(template.Id);
        Connection abandonedConnection = IntegrationsTestData.Connection(template.Id);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset createdBefore = now.AddMinutes(-120);

        Guid resumedJobId;
        Guid abandonedJobId;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(resumedConnection);
            ctx.Connections.Add(abandonedConnection);

            // Parked by the rate limiter for twelve hours and resumed five minutes ago: the run is
            // healthy even though its CreatedAt is far beyond the pending timeout.
            JobRun resumedJob = IntegrationsTestData.JobRun(
                resumedConnection.Id, JobType.WbFinanceReportImport, JobStatus.Pending, now.AddHours(-12));
            resumedJob.NotBefore = now.AddMinutes(-5);
            ctx.JobRuns.Add(resumedJob);

            // Its deferral expired three hours ago and nothing dispatched it since: stale.
            JobRun abandonedJob = IntegrationsTestData.JobRun(
                abandonedConnection.Id, JobType.WbFinanceReportImport, JobStatus.Pending, now.AddHours(-12));
            abandonedJob.NotBefore = now.AddHours(-3);
            ctx.JobRuns.Add(abandonedJob);

            await ctx.SaveChangesAsync();

            resumedJobId = resumedJob.Id;
            abandonedJobId = abandonedJob.Id;
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);

            List<JobRun> stalePending = await repository.GetPendingJobsOlderThanAsync(
                createdBefore,
                now,
                CancellationToken.None);

            stalePending.Select(j => j.Id).Should().NotContain(
                resumedJobId,
                "the waiting period of a deferred run starts when its deferral ends, not when it was created");
            stalePending.Select(j => j.Id).Should().Contain(
                abandonedJobId,
                "an expired deferral stops explaining the Pending row");
        }
    }

    [Fact]
    public async Task UpdateAsync_PersistsStatusTransition()
    {
        DbContextOptions<IntegrationsDbContext> options = IntegrationsTestData.Options("jobrun-repo");
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantMock = IntegrationsTestData.Tenant(tenantId);
        SystemChannelTemplate template = IntegrationsTestData.Template();
        Connection connection = IntegrationsTestData.Connection(template.Id);
        JobRun jobRun = IntegrationsTestData.JobRun(connection.Id, JobType.ProductsImport, JobStatus.Pending, DateTimeOffset.UtcNow);

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connection);
            ctx.JobRuns.Add(jobRun);
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);
            JobRun? loaded = await repository.GetByIdAsync(jobRun.Id, CancellationToken.None);
            loaded.Should().NotBeNull();

            loaded!.Status = JobStatus.Running;
            loaded.StartedAt = DateTimeOffset.UtcNow;

            await repository.UpdateAsync(loaded, CancellationToken.None);
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobRun? reloaded = await ctx.JobRuns.FirstOrDefaultAsync(j => j.Id == jobRun.Id);

            reloaded.Should().NotBeNull();
            reloaded!.Status.Should().Be(JobStatus.Running);
            reloaded.StartedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task GetPagedAsync_NoFilters_ReturnsEveryJobOfCurrentTenant_NewestFirst()
    {
        DbContextOptions<IntegrationsDbContext> options = IntegrationsTestData.Options("jobrun-repo");
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantMock = IntegrationsTestData.Tenant(tenantId);
        Mock<ITenantContext> otherTenantMock = IntegrationsTestData.Tenant(Guid.NewGuid());
        SystemChannelTemplate template = IntegrationsTestData.Template();
        Connection connection = IntegrationsTestData.Connection(template.Id);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connection);
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(connection.Id, JobType.ProductsImport, JobStatus.Success, now.AddHours(-3)));
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(connection.Id, JobType.WbOrdersImport, JobStatus.Failed, now.AddHours(-1)));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, otherTenantMock.Object))
        {
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(connection.Id, JobType.ProductsImport, JobStatus.Success, now));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);

            (int totalCount, List<JobRun> items) = await repository.GetPagedAsync(
                null, null, null, null, null, CancellationToken.None);

            totalCount.Should().Be(2, "the global tenant filter must exclude other tenants' rows");
            items.Should().HaveCount(2);
            items[0].CreatedAt.Should().BeAfter(items[1].CreatedAt, "newest jobs come first");
        }
    }

    [Fact]
    public async Task GetPagedAsync_TypeFilter_ExcludesOtherJobTypes()
    {
        DbContextOptions<IntegrationsDbContext> options = IntegrationsTestData.Options("jobrun-repo");
        Mock<ITenantContext> tenantMock = IntegrationsTestData.Tenant(Guid.NewGuid());
        SystemChannelTemplate template = IntegrationsTestData.Template();
        Connection connection = IntegrationsTestData.Connection(template.Id);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connection);
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(connection.Id, JobType.ProductsImport, JobStatus.Success, now.AddMinutes(-10)));
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(connection.Id, JobType.WbOrdersImport, JobStatus.Success, now.AddMinutes(-5)));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);

            (int totalCount, List<JobRun> items) = await repository.GetPagedAsync(
                JobType.WbOrdersImport, null, null, null, null, CancellationToken.None);

            totalCount.Should().Be(1);
            items.Should().ContainSingle().Which.JobType.Should().Be(JobType.WbOrdersImport);
        }
    }

    [Fact]
    public async Task GetPagedAsync_ConnectionFilter_ExcludesOtherConnections()
    {
        DbContextOptions<IntegrationsDbContext> options = IntegrationsTestData.Options("jobrun-repo");
        Mock<ITenantContext> tenantMock = IntegrationsTestData.Tenant(Guid.NewGuid());
        SystemChannelTemplate template = IntegrationsTestData.Template();
        Connection first = IntegrationsTestData.Connection(template.Id);
        Connection second = IntegrationsTestData.Connection(template.Id);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.AddRange(first, second);
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(first.Id, JobType.ProductsImport, JobStatus.Success, now.AddMinutes(-10)));
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(second.Id, JobType.ProductsImport, JobStatus.Success, now.AddMinutes(-5)));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);

            (int totalCount, List<JobRun> items) = await repository.GetPagedAsync(
                null, second.Id, null, null, null, CancellationToken.None);

            totalCount.Should().Be(1);
            items.Should().ContainSingle().Which.ConnectionId.Should().Be(second.Id);
        }
    }

    [Fact]
    public async Task GetPagedAsync_StatusFilter_KeepsOnlyRequestedStatuses()
    {
        DbContextOptions<IntegrationsDbContext> options = IntegrationsTestData.Options("jobrun-repo");
        Mock<ITenantContext> tenantMock = IntegrationsTestData.Tenant(Guid.NewGuid());
        SystemChannelTemplate template = IntegrationsTestData.Template();
        Connection connection = IntegrationsTestData.Connection(template.Id);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connection);
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(connection.Id, JobType.ProductsImport, JobStatus.Pending, now.AddMinutes(-30)));
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(connection.Id, JobType.WbOrdersImport, JobStatus.Running, now.AddMinutes(-20)));
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(connection.Id, JobType.ProductsImport, JobStatus.Success, now.AddMinutes(-10)));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);

            (int totalCount, List<JobRun> items) = await repository.GetPagedAsync(
                null,
                null,
                new[] { JobStatus.Pending, JobStatus.Running },
                null,
                null,
                CancellationToken.None);

            totalCount.Should().Be(2);
            items.Should().OnlyContain(j => j.Status == JobStatus.Pending || j.Status == JobStatus.Running);
        }
    }

    [Fact]
    public async Task GetPagedAsync_EmptyStatusArray_IsTreatedAsNoFilter()
    {
        DbContextOptions<IntegrationsDbContext> options = IntegrationsTestData.Options("jobrun-repo");
        Mock<ITenantContext> tenantMock = IntegrationsTestData.Tenant(Guid.NewGuid());
        SystemChannelTemplate template = IntegrationsTestData.Template();
        Connection connection = IntegrationsTestData.Connection(template.Id);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connection);
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(connection.Id, JobType.ProductsImport, JobStatus.Success, now.AddMinutes(-10)));
            ctx.JobRuns.Add(IntegrationsTestData.JobRun(connection.Id, JobType.WbOrdersImport, JobStatus.Failed, now.AddMinutes(-5)));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);

            (int totalCount, List<JobRun> items) = await repository.GetPagedAsync(
                null, null, Array.Empty<JobStatus>(), null, null, CancellationToken.None);

            totalCount.Should().Be(2);
            items.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task GetPagedAsync_SkipAndTake_CountsBeforePaging()
    {
        DbContextOptions<IntegrationsDbContext> options = IntegrationsTestData.Options("jobrun-repo");
        Mock<ITenantContext> tenantMock = IntegrationsTestData.Tenant(Guid.NewGuid());
        SystemChannelTemplate template = IntegrationsTestData.Template();
        Connection connection = IntegrationsTestData.Connection(template.Id);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connection);

            for (int i = 0; i < 5; i++)
            {
                ctx.JobRuns.Add(IntegrationsTestData.JobRun(
                    connection.Id,
                    JobType.ProductsImport,
                    JobStatus.Success,
                    now.AddMinutes(-10 * (5 - i))));
            }

            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);

            (int totalCount, List<JobRun> page) = await repository.GetPagedAsync(
                null, null, null, 2, 2, CancellationToken.None);

            totalCount.Should().Be(5, "TotalCount ignores paging");
            page.Should().HaveCount(2);

            (int _, List<JobRun> firstPage) = await repository.GetPagedAsync(
                null, null, null, 0, 2, CancellationToken.None);

            page.Select(j => j.Id).Should().NotIntersectWith(firstPage.Select(j => j.Id));
            page[0].CreatedAt.Should().BeBefore(firstPage[0].CreatedAt);
        }
    }
}
