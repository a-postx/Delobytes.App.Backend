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

/// <summary>
/// Verifies JobRunRepository query and persistence behaviour: tenant-scoped reads for
/// the worker, cross-tenant reads for the watchdog, and the active-job check used to
/// enforce one Pending/Running job per (ConnectionId, JobType).
/// </summary>
public class JobRunRepositoryTests
{
    private static DbContextOptions<IntegrationsDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseInMemoryDatabase("jobrun-repo-" + Guid.NewGuid())
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

    private static JobRun BuildJobRun(Guid connectionId, JobType jobType, JobStatus status, DateTimeOffset createdAt)
    {
        return new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            JobType = jobType,
            Status = status,
            CreatedAt = createdAt,
        };
    }

    [Fact]
    public async Task CreateAsync_PersistsJobRun_AndReturnsIt()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantMock = TenantMock(tenantId);
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);

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
            JobRun jobRun = BuildJobRun(connection.Id, JobType.ProductsImport, JobStatus.Pending, DateTimeOffset.UtcNow);

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
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantMock = TenantMock(tenantId);
        SystemChannelTemplate template = BuildTemplate();
        Connection connectionWithActiveJob = BuildConnection(template.Id);
        Connection connectionWithTerminalJob = BuildConnection(template.Id);

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.AddRange(connectionWithActiveJob, connectionWithTerminalJob);
            ctx.JobRuns.Add(BuildJobRun(connectionWithActiveJob.Id, JobType.ProductsImport, JobStatus.Running, DateTimeOffset.UtcNow));
            ctx.JobRuns.Add(BuildJobRun(connectionWithTerminalJob.Id, JobType.ProductsImport, JobStatus.Success, DateTimeOffset.UtcNow));
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
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantAId = Guid.NewGuid();
        Guid tenantBId = Guid.NewGuid();
        Mock<ITenantContext> tenantAMock = TenantMock(tenantAId);
        Mock<ITenantContext> tenantBMock = TenantMock(tenantBId);
        SystemChannelTemplate template = BuildTemplate();

        Connection connectionA = BuildConnection(template.Id);
        Connection connectionA2 = BuildConnection(template.Id);
        Connection connectionB = BuildConnection(template.Id);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantAMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.AddRange(connectionA, connectionA2);
            // Older Pending job for tenant A, created first.
            ctx.JobRuns.Add(BuildJobRun(connectionA.Id, JobType.ProductsImport, JobStatus.Pending, now.AddMinutes(-10)));
            // Newer Pending job for tenant A.
            ctx.JobRuns.Add(BuildJobRun(connectionA2.Id, JobType.ProductsImport, JobStatus.Pending, now.AddMinutes(-5)));
            // Running job for tenant A must be excluded.
            ctx.JobRuns.Add(BuildJobRun(connectionA.Id, JobType.ProductsImport, JobStatus.Running, now.AddMinutes(-1)));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantBMock.Object))
        {
            ctx.Connections.Add(connectionB);
            // Pending job for tenant B must never leak into tenant A's results.
            ctx.JobRuns.Add(BuildJobRun(connectionB.Id, JobType.ProductsImport, JobStatus.Pending, now.AddMinutes(-20)));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantAMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);

            List<JobRun> pending = await repository.GetPendingJobsAsync(tenantAId, limit: 10, CancellationToken.None);

            pending.Should().HaveCount(2);
            pending[0].ConnectionId.Should().Be(connectionA.Id, "oldest Pending job must come first");
            pending[1].ConnectionId.Should().Be(connectionA2.Id);

            List<JobRun> limited = await repository.GetPendingJobsAsync(tenantAId, limit: 1, CancellationToken.None);
            limited.Should().HaveCount(1);
            limited[0].ConnectionId.Should().Be(connectionA.Id);
        }
    }

    [Fact]
    public async Task GetRunningJobsOlderThanAsync_ReturnsStuckJobs_AcrossAllTenants_IgnoringQueryFilter()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantAId = Guid.NewGuid();
        Guid tenantBId = Guid.NewGuid();
        Mock<ITenantContext> tenantAMock = TenantMock(tenantAId);
        Mock<ITenantContext> tenantBMock = TenantMock(tenantBId);
        SystemChannelTemplate template = BuildTemplate();

        Connection connectionA = BuildConnection(template.Id);
        Connection connectionB = BuildConnection(template.Id);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset threshold = now.AddHours(-1);

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantAMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connectionA);
            // Stuck job for tenant A: started well before the threshold.
            JobRun stuckJob = BuildJobRun(connectionA.Id, JobType.ProductsImport, JobStatus.Running, now.AddHours(-3));
            stuckJob.StartedAt = now.AddHours(-3);
            ctx.JobRuns.Add(stuckJob);

            // Fresh Running job, still within timeout, must be excluded.
            JobRun freshJob = BuildJobRun(connectionA.Id, JobType.OrdersSync, JobStatus.Running, now.AddMinutes(-5));
            freshJob.StartedAt = now.AddMinutes(-5);
            ctx.JobRuns.Add(freshJob);

            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantBMock.Object))
        {
            ctx.Connections.Add(connectionB);
            // Stuck job belonging to a different tenant must still be found by the watchdog.
            JobRun stuckJobTenantB = BuildJobRun(connectionB.Id, JobType.ProductsImport, JobStatus.Running, now.AddHours(-2));
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
    public async Task UpdateAsync_PersistsStatusTransition()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantMock = TenantMock(tenantId);
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);
        JobRun jobRun = BuildJobRun(connection.Id, JobType.ProductsImport, JobStatus.Pending, DateTimeOffset.UtcNow);

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
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantMock = TenantMock(tenantId);
        Mock<ITenantContext> otherTenantMock = TenantMock(Guid.NewGuid());
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connection);
            ctx.JobRuns.Add(BuildJobRun(connection.Id, JobType.ProductsImport, JobStatus.Success, now.AddHours(-3)));
            ctx.JobRuns.Add(BuildJobRun(connection.Id, JobType.OrdersImport, JobStatus.Failed, now.AddHours(-1)));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, otherTenantMock.Object))
        {
            ctx.JobRuns.Add(BuildJobRun(connection.Id, JobType.ProductsImport, JobStatus.Success, now));
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
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connection);
            ctx.JobRuns.Add(BuildJobRun(connection.Id, JobType.ProductsImport, JobStatus.Success, now.AddMinutes(-10)));
            ctx.JobRuns.Add(BuildJobRun(connection.Id, JobType.OrdersImport, JobStatus.Success, now.AddMinutes(-5)));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);

            (int totalCount, List<JobRun> items) = await repository.GetPagedAsync(
                JobType.OrdersImport, null, null, null, null, CancellationToken.None);

            totalCount.Should().Be(1);
            items.Should().ContainSingle().Which.JobType.Should().Be(JobType.OrdersImport);
        }
    }

    [Fact]
    public async Task GetPagedAsync_ConnectionFilter_ExcludesOtherConnections()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());
        SystemChannelTemplate template = BuildTemplate();
        Connection first = BuildConnection(template.Id);
        Connection second = BuildConnection(template.Id);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.AddRange(first, second);
            ctx.JobRuns.Add(BuildJobRun(first.Id, JobType.ProductsImport, JobStatus.Success, now.AddMinutes(-10)));
            ctx.JobRuns.Add(BuildJobRun(second.Id, JobType.ProductsImport, JobStatus.Success, now.AddMinutes(-5)));
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
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connection);
            ctx.JobRuns.Add(BuildJobRun(connection.Id, JobType.ProductsImport, JobStatus.Pending, now.AddMinutes(-30)));
            ctx.JobRuns.Add(BuildJobRun(connection.Id, JobType.OrdersImport, JobStatus.Running, now.AddMinutes(-20)));
            ctx.JobRuns.Add(BuildJobRun(connection.Id, JobType.ProductsImport, JobStatus.Success, now.AddMinutes(-10)));
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
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connection);
            ctx.JobRuns.Add(BuildJobRun(connection.Id, JobType.ProductsImport, JobStatus.Success, now.AddMinutes(-10)));
            ctx.JobRuns.Add(BuildJobRun(connection.Id, JobType.OrdersImport, JobStatus.Failed, now.AddMinutes(-5)));
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
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connection);

            for (int i = 0; i < 5; i++)
            {
                ctx.JobRuns.Add(BuildJobRun(
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
