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
/// Verifies SyncCheckpointRepository: one watermark per (ConnectionId, JobType), upsert
/// semantics, and tenant isolation (SyncCheckpoint implements ITenantScoped).
/// </summary>
public class SyncCheckpointRepositoryTests
{
    private static DbContextOptions<IntegrationsDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseInMemoryDatabase("sync-checkpoint-" + Guid.NewGuid())
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

    [Fact]
    public async Task UpsertAsync_InsertsNewCheckpoint_WhenNoneExistsForConnectionAndJobType()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connection);
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            SyncCheckpointRepository repository = new SyncCheckpointRepository(ctx);
            SyncCheckpoint checkpoint = new SyncCheckpoint
            {
                ConnectionId = connection.Id,
                JobType = JobType.OrdersSync,
                LastChangedAt = null,
                LastSuccessAt = DateTimeOffset.UtcNow.AddDays(-30),
            };

            await repository.UpsertAsync(checkpoint, CancellationToken.None);
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            SyncCheckpointRepository repository = new SyncCheckpointRepository(ctx);

            SyncCheckpoint? persisted = await repository.GetAsync(connection.Id, JobType.OrdersSync, CancellationToken.None);

            persisted.Should().NotBeNull();
            persisted!.LastChangedAt.Should().BeNull("first run has no observed watermark yet");
        }
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExistingCheckpoint_InsteadOfInsertingDuplicate()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);
        DateTimeOffset firstChangedAt = DateTimeOffset.UtcNow.AddHours(-2);
        DateTimeOffset secondChangedAt = DateTimeOffset.UtcNow;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connection);
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            SyncCheckpointRepository repository = new SyncCheckpointRepository(ctx);
            await repository.UpsertAsync(
                new SyncCheckpoint
                {
                    ConnectionId = connection.Id,
                    JobType = JobType.OrdersSync,
                    LastChangedAt = firstChangedAt,
                    LastSuccessAt = firstChangedAt,
                },
                CancellationToken.None);
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            SyncCheckpointRepository repository = new SyncCheckpointRepository(ctx);
            await repository.UpsertAsync(
                new SyncCheckpoint
                {
                    ConnectionId = connection.Id,
                    JobType = JobType.OrdersSync,
                    LastChangedAt = secondChangedAt,
                    LastSuccessAt = secondChangedAt,
                },
                CancellationToken.None);
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            SyncCheckpointRepository repository = new SyncCheckpointRepository(ctx);

            SyncCheckpoint? persisted = await repository.GetAsync(connection.Id, JobType.OrdersSync, CancellationToken.None);

            persisted.Should().NotBeNull();
            persisted!.LastChangedAt.Should().Be(secondChangedAt, "a second Upsert for the same (ConnectionId, JobType) must overwrite, not duplicate");

            int rowCount = await ctx.SyncCheckpoints.CountAsync(
                c => c.ConnectionId == connection.Id && c.JobType == JobType.OrdersSync);
            rowCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task GetAsync_DoesNotLeak_AcrossTenants()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantAId = Guid.NewGuid();
        Guid tenantBId = Guid.NewGuid();
        Mock<ITenantContext> tenantAMock = TenantMock(tenantAId);
        Mock<ITenantContext> tenantBMock = TenantMock(tenantBId);
        SystemChannelTemplate template = BuildTemplate();
        Connection connectionA = BuildConnection(template.Id);

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantAMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connectionA);
            await ctx.SaveChangesAsync();

            SyncCheckpointRepository repository = new SyncCheckpointRepository(ctx);
            await repository.UpsertAsync(
                new SyncCheckpoint
                {
                    ConnectionId = connectionA.Id,
                    JobType = JobType.OrdersSync,
                    LastSuccessAt = DateTimeOffset.UtcNow,
                },
                CancellationToken.None);
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantBMock.Object))
        {
            SyncCheckpointRepository repository = new SyncCheckpointRepository(ctx);

            // TenantB has no access to connectionA (different tenant's query filter scope);
            // the checkpoint must not be visible even though the row physically exists.
            SyncCheckpoint? result = await repository.GetAsync(connectionA.Id, JobType.OrdersSync, CancellationToken.None);

            result.Should().BeNull("a checkpoint stamped with TenantA's id must not leak to TenantB");
        }
    }
}
