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
/// Verifies JobRunRepository's handling of the type-specific ProductsImportJob/OrdersImportJob
/// tables introduced to keep per-job-type fields out of the shared JobRun table.
/// </summary>
public class JobRunRepositoryTypeSpecificTests
{
    private static DbContextOptions<IntegrationsDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseInMemoryDatabase("jobrun-type-specific-" + Guid.NewGuid())
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

    private static JobRun BuildJobRun(Guid connectionId, JobType jobType)
    {
        return new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            JobType = jobType,
            Status = JobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public async Task CreateProductsImportJobAsync_PersistsRow_LinkedToJobRun()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);
        JobRun jobRun = BuildJobRun(connection.Id, JobType.ProductsImport);

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
            ProductsImportJob job = new ProductsImportJob { JobRunId = jobRun.Id, TotalImportBatches = null };

            await repository.CreateProductsImportJobAsync(job, CancellationToken.None);
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);

            ProductsImportJob? persisted = await repository.GetProductsImportJobAsync(jobRun.Id, CancellationToken.None);

            persisted.Should().NotBeNull();
            persisted!.JobRunId.Should().Be(jobRun.Id);
            persisted.TotalImportBatches.Should().BeNull();
        }
    }

    [Fact]
    public async Task CreateOrdersImportJobAsync_PersistsRow_LinkedToJobRun()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());
        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template.Id);
        JobRun jobRun = BuildJobRun(connection.Id, JobType.OrdersSync);

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
            OrdersImportJob job = new OrdersImportJob { JobRunId = jobRun.Id };

            await repository.CreateOrdersImportJobAsync(job, CancellationToken.None);
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            JobRunRepository repository = new JobRunRepository(ctx);

            OrdersImportJob? persisted = await repository.GetOrdersImportJobAsync(jobRun.Id, CancellationToken.None);

            persisted.Should().NotBeNull();
            persisted!.JobRunId.Should().Be(jobRun.Id);
        }
    }

    [Fact]
    public async Task GetProductsImportJobAsync_ReturnsNull_WhenNoRowExists()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());

        using IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object);
        JobRunRepository repository = new JobRunRepository(ctx);

        ProductsImportJob? result = await repository.GetProductsImportJobAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }
}
