using System;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentAssertions;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

/// <summary>
/// Product lifecycle transitions. The status and its timestamps are one state: the tests pin the
/// pairs that must never diverge, in particular a repeated archive, which used to overwrite the
/// original <see cref="Product.ArchivedAt"/>.
/// </summary>
public class ProductStateMachineTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryArchive_Repeated_KeepsTheFirstTimestamp()
    {
        Product product = BuildProduct();

        product.TryArchive(Now).Should().BeTrue();
        product.TryArchive(Now.AddDays(1)).Should().BeFalse();

        product.Status.Should().Be(ProductStatus.Archived);
        product.ArchivedAt.Should().Be(Now);
    }

    [Fact]
    public void TryArchive_Deleted_IsRejected()
    {
        Product product = BuildProduct();
        product.TryCompleteDeletion(Now);

        product.TryArchive(Now.AddDays(1)).Should().BeFalse();

        product.Status.Should().Be(ProductStatus.Deleted);
        product.ArchivedAt.Should().BeNull();
    }

    [Fact]
    public void TryArchive_PendingDeletion_ClearsThePendingMarker()
    {
        Product product = BuildProduct();
        product.TryRequestDeletion(Now);

        product.TryArchive(Now.AddDays(1)).Should().BeTrue();

        product.Status.Should().Be(ProductStatus.Archived);
        product.DeletionRequestedAt.Should().BeNull();
        product.ArchivedAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void TryRestore_FromArchived_ClearsArchiveState()
    {
        Product product = BuildProduct();
        product.TryArchive(Now);

        product.TryRestore().Should().BeTrue();

        product.Status.Should().Be(ProductStatus.Active);
        product.ArchivedAt.Should().BeNull();
    }

    [Fact]
    public void TryRestore_FromDeletionFailed_ClearsTheRequest()
    {
        Product product = BuildProduct();
        product.TryRequestDeletion(Now);
        product.TryFailDeletion();

        product.TryRestore().Should().BeTrue();

        product.Status.Should().Be(ProductStatus.Active);
        product.DeletionRequestedAt.Should().BeNull();
    }

    [Fact]
    public void TryRestore_Deleted_IsRejected()
    {
        Product product = BuildProduct();
        product.TryCompleteDeletion(Now);

        product.TryRestore().Should().BeFalse();

        product.Status.Should().Be(ProductStatus.Deleted);
        product.DeletedAt.Should().Be(Now);
    }

    [Fact]
    public void TryRequestDeletion_Repeated_IsRejected()
    {
        Product product = BuildProduct();
        product.TryRequestDeletion(Now).Should().BeTrue();

        product.TryRequestDeletion(Now.AddMinutes(1)).Should().BeFalse();

        product.DeletionRequestedAt.Should().Be(Now);
    }

    [Fact]
    public void TryCompleteDeletion_KeepsTheRequestAsAnAuditTrail()
    {
        Product product = BuildProduct();
        product.TryRequestDeletion(Now);

        product.TryCompleteDeletion(Now.AddHours(1)).Should().BeTrue();

        product.Status.Should().Be(ProductStatus.Deleted);
        product.DeletedAt.Should().Be(Now.AddHours(1));
        product.DeletionRequestedAt.Should().Be(Now);
    }

    [Fact]
    public void TryFailDeletion_OutsidePending_IsRejected()
    {
        Product product = BuildProduct();

        product.TryFailDeletion().Should().BeFalse();

        product.Status.Should().Be(ProductStatus.Active);
    }

    private static Product BuildProduct()
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            Code = "C-1",
            Sku = "SKU-1",
            Name = "Test product",
            CreatedAt = Now.AddDays(-10),
        };
    }
}
