using System;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Infrastructure;

/// <summary>
/// JobCheckpoint and ProductsImportJob are ITenantScoped, so the shared tenant convention attaches
/// a query filter to them like every other row of the Integrations module. Regression tests for
/// both the isolation itself and the no-tenant guard that comes with it.
/// </summary>
public class JobCheckpointTenantIsolationTests
{
    private static DbContextOptions<IntegrationsDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseInMemoryDatabase("job-detail-tenant-" + Guid.NewGuid())
            .Options;
    }

    private static Mock<ITenantContext> TenantMock(Guid? tenantId)
    {
        Mock<ITenantContext> mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock;
    }

    private static async Task<JobRun> SeedJobRunAsync(IntegrationsDbContext ctx)
    {
        SystemChannelTemplate template = new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "wildberries",
            DisplayName = "Wildberries",
            ApiBaseUrl = "https://suppliers-api.wildberries.ru",
            ApiVersion = "v3",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Connection connection = new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            SystemChannelTemplateId = template.Id,
            Name = "Test connection",
            ApiKey = "test-key",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            JobType = JobType.ProductsImport,
            Status = JobStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        ctx.SystemChannelTemplates.Add(template);
        ctx.Connections.Add(connection);
        ctx.JobRuns.Add(jobRun);
        await ctx.SaveChangesAsync();

        return jobRun;
    }

    [Fact]
    public async Task JobCheckpoint_OfAnotherTenant_IsNotVisible()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantA = TenantMock(Guid.NewGuid());
        Mock<ITenantContext> tenantB = TenantMock(Guid.NewGuid());
        Guid jobRunAId;
        Guid jobRunBId;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantA.Object))
        {
            JobRun jobRun = await SeedJobRunAsync(ctx);
            jobRunAId = jobRun.Id;

            await new JobCheckpointRepository(ctx).UpsertAsync(
                new JobCheckpoint
                {
                    JobRunId = jobRun.Id,
                    Payload = "{\"cursor\":\"tenant-a\"}",
                    UpdatedAt = DateTimeOffset.UtcNow,
                },
                CancellationToken.None);
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantB.Object))
        {
            JobRun jobRun = await SeedJobRunAsync(ctx);
            jobRunBId = jobRun.Id;
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantB.Object))
        {
            JobCheckpointRepository repository = new JobCheckpointRepository(ctx);

            JobCheckpoint? foreign = await repository.GetByJobRunIdAsync(jobRunAId, CancellationToken.None);

            foreign.Should().BeNull("another tenant's checkpoint must not be readable through the tenant filter");
            (await ctx.JobCheckpoints.CountAsync()).Should().Be(0);
            (await repository.GetByJobRunIdAsync(jobRunBId, CancellationToken.None)).Should().BeNull();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantA.Object))
        {
            (await ctx.JobCheckpoints.CountAsync()).Should().Be(1);
        }
    }

    [Fact]
    public async Task ProductsImportJob_OfAnotherTenant_IsNotVisible()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantA = TenantMock(Guid.NewGuid());
        Mock<ITenantContext> tenantB = TenantMock(Guid.NewGuid());
        Guid productsImportId;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantA.Object))
        {
            JobRun jobRun = await SeedJobRunAsync(ctx);
            productsImportId = jobRun.Id;

            await new JobRunRepository(ctx).CreateProductsImportJobAsync(
                new ProductsImportJob { JobRunId = jobRun.Id, TotalImportBatches = 3 },
                CancellationToken.None);
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantB.Object))
        {
            await SeedJobRunAsync(ctx);
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantB.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);

            ProductsImportJob? foreign = await repository.GetProductsImportJobAsync(productsImportId, CancellationToken.None);

            foreign.Should().BeNull("another tenant's type-specific job row must not be readable");
            (await ctx.ProductsImportJobs.CountAsync()).Should().Be(0);
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantA.Object))
        {
            (await ctx.ProductsImportJobs.CountAsync()).Should().Be(1);
        }
    }

    [Fact]
    public async Task JobCheckpoint_WrittenWithoutTenant_Throws()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantContext = TenantMock(Guid.NewGuid());
        Guid jobRunId;

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantContext.Object))
        {
            jobRunId = (await SeedJobRunAsync(ctx)).Id;
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, TenantMock(null).Object))
        {
            JobCheckpointRepository repository = new JobCheckpointRepository(ctx);

            Func<Task> act = () => repository.UpsertAsync(
                new JobCheckpoint
                {
                    JobRunId = jobRunId,
                    Payload = "{}",
                    UpdatedAt = DateTimeOffset.UtcNow,
                },
                CancellationToken.None);

            await act.Should().ThrowAsync<SecurityException>()
                .WithMessage("*without a resolved TenantId*");
        }
    }

    [Fact]
    public void Model_DeclaresTenantFilterAndShadowProperty_ForBothJobDetailTables()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        using IntegrationsDbContext ctx = new IntegrationsDbContext(options, TenantMock(Guid.NewGuid()).Object);

        IEntityType checkpoint = ctx.Model.FindEntityType(typeof(JobCheckpoint))!;
        IEntityType productsImport = ctx.Model.FindEntityType(typeof(ProductsImportJob))!;

        checkpoint.GetDeclaredQueryFilters().Should().NotBeEmpty();
        productsImport.GetDeclaredQueryFilters().Should().NotBeEmpty();
        checkpoint.FindProperty("TenantId").Should().NotBeNull();
        productsImport.FindProperty("TenantId").Should().NotBeNull();
    }
}
