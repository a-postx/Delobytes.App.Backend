using System;
using System.Collections.Generic;
using System.Linq;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.SalesChannels;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Infrastructure;

/// <summary>
/// The registry validates descriptors against modules at construction. Since both channels now
/// serve products import under distinct job types, a missing or duplicated declaration would fail
/// startup; this test pins the Ozon half of that contract.
/// </summary>
public class ChannelJobTypeRegistryTests
{
    [Fact]
    public void Constructor_WithBothChannelModules_Succeeds()
    {
        Action act = () => _ = new ChannelJobTypeRegistry(
            new ChannelModule[] { new WildberriesChannelModule(), new OzonChannelModule() });

        act.Should().NotThrow();
    }

    [Fact]
    public void Ozon_DeclaresProductsImportAndServesAJobTypeOfThatKind()
    {
        ChannelJobTypeRegistry registry = new ChannelJobTypeRegistry(
            new ChannelModule[] { new WildberriesChannelModule(), new OzonChannelModule() });

        IReadOnlyList<JobType> productsTypes = registry.GetJobTypes("ozon", ChannelJobKind.ProductsImport);

        productsTypes.Should().ContainSingle().Which.Should().Be(JobType.OzonProductsImport);
        registry.ResolveKind(JobType.OzonProductsImport).Should().Be(ChannelJobKind.ProductsImport);
    }

    [Fact]
    public void ProductsJobTypes_AreDistinctPerChannel()
    {
        ChannelJobTypeRegistry registry = new ChannelJobTypeRegistry(
            new ChannelModule[] { new WildberriesChannelModule(), new OzonChannelModule() });

        registry.GetJobTypes("wildberries", ChannelJobKind.ProductsImport)
            .Should().ContainSingle().Which.Should().Be(JobType.ProductsImport);

        registry.GetJobTypes("ozon", ChannelJobKind.ProductsImport)
            .Should().ContainSingle().Which.Should().Be(JobType.OzonProductsImport);
    }
}
