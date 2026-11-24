using System;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Identity;
using Delobytes.App.Backend.Contracts.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Delobytes.App.Backend.Tests.Infrastructure.Persistence;

/// <summary>
/// Pins the identity rule: UUIDv7 with a big-endian 48-bit millisecond timestamp. PostgreSQL
/// compares <c>uuid</c> bytewise, so this layout is what keeps a new row's key next to the previous
/// one in the primary-key btree.
/// </summary>
public class SequentialGuidGenerationTests
{
    [Fact]
    public void New_ProducesVersion7()
    {
        Guid id = EntityId.New();

        byte[] bytes = WriteBigEndian(id);

        (bytes[6] >> 4).Should().Be(7, "UUIDv7 is the only layout with the timestamp in the high bits");
    }

    [Fact]
    public void New_EncodesTheTimestampBigEndianInTheFirstSixBytes()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;

        Guid id = EntityId.New();

        DateTimeOffset after = DateTimeOffset.UtcNow;

        byte[] bytes = WriteBigEndian(id);
        long timestampMs = 0;

        for (int i = 0; i < 6; i++)
        {
            timestampMs = (timestampMs << 8) | bytes[i];
        }

        DateTimeOffset decoded = DateTimeOffset.FromUnixTimeMilliseconds(timestampMs);

        decoded.Should().BeOnOrAfter(before.AddSeconds(-1)).And.BeOnOrBefore(after.AddSeconds(1));
    }

    [Fact]
    public void New_IdsCreatedLaterSortAfterIdsCreatedEarlier()
    {
        Guid earlier = EntityId.New();

        System.Threading.Thread.Sleep(5);

        Guid later = EntityId.New();

        byte[] earlierBytes = WriteBigEndian(earlier);
        byte[] laterBytes = WriteBigEndian(later);

        CompareTimestampBytes(laterBytes, earlierBytes).Should().BePositive(
            "the 48-bit timestamp is the leading field, so its byte order is the key order");
    }

    [Fact]
    public async Task SaveChanges_NewEntity_AssignsVersion7Id()
    {
        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.TenantId).Returns(Guid.NewGuid());

        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("sequential-guid-" + Guid.NewGuid())
            .Options;

        using CatalogDbContext context = new CatalogDbContext(options, tenantContextMock.Object);

        Product product = new Product
        {
            Code = "C-1",
            Sku = "SKU-1",
            Name = "Test product",
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        product.Id.Should().NotBeEmpty();
        (WriteBigEndian(product.Id)[6] >> 4).Should().Be(7);
    }

    private static byte[] WriteBigEndian(Guid id)
    {
        byte[] bytes = new byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);
        return bytes;
    }

    private static int CompareTimestampBytes(byte[] left, byte[] right)
    {
        for (int i = 0; i < 6; i++)
        {
            int comparison = left[i].CompareTo(right[i]);

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }
}
