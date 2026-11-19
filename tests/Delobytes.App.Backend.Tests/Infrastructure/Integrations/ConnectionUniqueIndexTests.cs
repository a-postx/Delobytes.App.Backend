using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Integrations;

/// <summary>
/// Guards the database-level invariant behind <c>HasActiveConnectionForChannelAsync</c>: at most one
/// active connection per channel. The handler check is a race — two concurrent creates both see no
/// active row — so the guarantee has to exist as a filtered unique index.
/// </summary>
public class ConnectionUniqueIndexTests
{
    private const string IndexName = "IX_Connections_TenantId_ChannelId_Active";

    [Fact]
    public void ActiveConnectionPerChannel_IsUniqueAndFilteredByIsActive()
    {
        // Arrange
        using IntegrationsDbContext context = CreateContext();
        IEntityType entity = context.Model.FindEntityType(typeof(Connection))!;

        // Act
        IIndex? index = entity.GetIndexes()
            .SingleOrDefault(candidate => candidate.GetDatabaseName() == IndexName);

        // Assert
        index.Should().NotBeNull();
        index!.IsUnique.Should().BeTrue();
        index.Properties.Select(property => property.Name).Should().Equal("TenantId", "ChannelId");
        index.GetFilter().Should().Be("\"IsActive\"");
    }

    private static IntegrationsDbContext CreateContext()
    {
        DbContextOptions<IntegrationsDbContext> options = new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseNpgsql("Host=localhost;Database=integrations;Username=test;Password=test")
            .Options;

        Mock<ITenantContext> tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(Guid.Parse("33333333-3333-3333-3333-333333333333"));

        return new IntegrationsDbContext(options, tenant.Object);
    }
}
