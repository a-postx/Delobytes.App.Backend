using System;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Delobytes.App.Backend.Integrations.Tests.TestSupport;

/// <summary>
/// Builders for the Integrations persistence tests. Every repository suite used to declare its own
/// byte-identical BuildOptions/TenantMock/BuildTemplate/BuildConnection copies, which had already
/// drifted apart; the builders live here once so a change to the fixture is made in one place.
/// </summary>
internal static class IntegrationsTestData
{
    /// <summary>Builds options for a fresh in-memory database whose name is unique per call.</summary>
    internal static DbContextOptions<IntegrationsDbContext> Options(string prefix)
    {
        return new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseInMemoryDatabase(prefix + "-" + Guid.NewGuid())
            .Options;
    }

    internal static Mock<ITenantContext> Tenant(Guid? tenantId)
    {
        Mock<ITenantContext> mock = new Mock<ITenantContext>();

        mock.Setup(t => t.TenantId).Returns(tenantId);

        return mock;
    }

    internal static SystemChannelTemplate Template(
        string code = "wildberries",
        string displayName = "Wildberries",
        string apiBaseUrl = "https://suppliers-api.wildberries.ru")
    {
        return new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = code,
            DisplayName = displayName,
            ApiBaseUrl = apiBaseUrl,
            ApiVersion = "v3",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    internal static Connection Connection(Guid templateId, bool isActive = true)
    {
        return new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            SystemChannelTemplateId = templateId,
            Name = "Test connection",
            ApiKey = "test-key",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    internal static JobRun JobRun(
        Guid connectionId,
        JobType jobType = JobType.ProductsImport,
        JobStatus status = JobStatus.Pending,
        DateTimeOffset? createdAt = null)
    {
        return new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            JobType = jobType,
            Status = status,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };
    }
}
