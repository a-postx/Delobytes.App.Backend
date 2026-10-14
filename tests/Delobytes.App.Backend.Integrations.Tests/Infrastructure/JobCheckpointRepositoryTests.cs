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
/// Verifies JobCheckpointRepository upsert semantics: a checkpoint is per-task internal state
/// (e.g. pagination cursor), one row per JobRun.
/// </summary>
public class JobCheckpointRepositoryTests
{
    private static DbContextOptions<IntegrationsDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseInMemoryDatabase("job-checkpoint-" + Guid.NewGuid())
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

    private static async Task<JobRun> SeedJobRunAsync(IntegrationsDbContext ctx, Guid connectionId)
    {
        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            JobType = JobType.ProductsImport,
            Status = JobStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        ctx.JobRuns.Add(jobRun);
        await ctx.SaveChangesAsync();
        return jobRun;
    }

    [Fact]
    public async Task UpsertAsync_InsertsNewCheckpoint_WhenNoneExistsForJobRun()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);
        DateTimeOffset updatedAt = DateTimeOffset.UtcNow;
        Guid jobRunId;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connection);
            JobRun jobRun = await SeedJobRunAsync(ctx, connection.Id);
            jobRunId = jobRun.Id;
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobCheckpointRepository repository = new JobCheckpointRepository(ctx);
            JobCheckpoint checkpoint = new JobCheckpoint
            {
                JobRunId = jobRunId,
                Payload = "{\"cursor\":\"page-1\"}",
                UpdatedAt = updatedAt,
            };

            await repository.UpsertAsync(checkpoint, CancellationToken.None);
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobCheckpointRepository repository = new JobCheckpointRepository(ctx);

            JobCheckpoint? persisted = await repository.GetByJobRunIdAsync(jobRunId, CancellationToken.None);

            persisted.Should().NotBeNull();
            persisted!.Payload.Should().Be("{\"cursor\":\"page-1\"}");
        }
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExistingCheckpoint_InsteadOfInsertingDuplicate()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);
        Guid jobRunId;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connection);
            JobRun jobRun = await SeedJobRunAsync(ctx, connection.Id);
            jobRunId = jobRun.Id;
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobCheckpointRepository repository = new JobCheckpointRepository(ctx);
            await repository.UpsertAsync(
                new JobCheckpoint { JobRunId = jobRunId, Payload = "{\"cursor\":\"page-1\"}", UpdatedAt = DateTimeOffset.UtcNow },
                CancellationToken.None);
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobCheckpointRepository repository = new JobCheckpointRepository(ctx);
            await repository.UpsertAsync(
                new JobCheckpoint { JobRunId = jobRunId, Payload = "{\"cursor\":\"page-2\"}", UpdatedAt = DateTimeOffset.UtcNow },
                CancellationToken.None);
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobCheckpointRepository repository = new JobCheckpointRepository(ctx);

            JobCheckpoint? persisted = await repository.GetByJobRunIdAsync(jobRunId, CancellationToken.None);

            persisted.Should().NotBeNull();
            persisted!.Payload.Should().Be("{\"cursor\":\"page-2\"}", "a second Upsert for the same JobRunId must overwrite, not duplicate");

            int rowCount = await ctx.JobCheckpoints.CountAsync(c => c.JobRunId == jobRunId);
            rowCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task GetByJobRunIdAsync_ReturnsNull_WhenNoCheckpointExists()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());

        using IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object);
        JobCheckpointRepository repository = new JobCheckpointRepository(ctx);

        JobCheckpoint? result = await repository.GetByJobRunIdAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }
}
