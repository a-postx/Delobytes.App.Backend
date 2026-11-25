using System;
using System.Threading;
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
/// Verifies JobRunRepository's handling of the type-specific ProductsImportJob table introduced
/// to keep per-job-type fields out of the shared JobRun table.
/// </summary>
public class JobRunRepositoryTypeSpecificTests
{

    [Fact]
    public async Task CreateProductsImportJobAsync_PersistsRow_LinkedToJobRun()
    {
        DbContextOptions<IntegrationsDbContext> options = IntegrationsTestData.Options("jobrun-type-specific");
        Mock<ITenantContext> tenantMock = IntegrationsTestData.Tenant(Guid.NewGuid());
        SystemChannelTemplate template = IntegrationsTestData.Template();
        Connection connection = IntegrationsTestData.Connection(template.Id);
        JobRun jobRun = IntegrationsTestData.JobRun(connection.Id, JobType.ProductsImport);

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
    public async Task GetProductsImportJobAsync_ReturnsNull_WhenNoRowExists()
    {
        DbContextOptions<IntegrationsDbContext> options = IntegrationsTestData.Options("jobrun-type-specific");
        Mock<ITenantContext> tenantMock = IntegrationsTestData.Tenant(Guid.NewGuid());

        using IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object);
        JobRunRepository repository = new JobRunRepository(ctx);

        ProductsImportJob? result = await repository.GetProductsImportJobAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }
}
