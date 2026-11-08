using System;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Application.Orders;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Tests <see cref="OrdersFetcherResolver"/>: each (channel, job type) pair resolves to the fetcher
/// set registered under its key, Ozon FBO and FBS resolve to different sets, and every other
/// combination is refused rather than silently served the wrong schema.
/// </summary>
public class OrdersFetcherResolverTests
{
    [Fact]
    public void Resolve_WildberriesOrdersImport_ReturnsTheWildberriesSet()
    {
        (OrdersFetcherResolver resolver, IOrdersFetchers wildberries, _, _) = BuildResolver();

        OrdersFetchersResolution.Supported supported = resolver
            .Resolve("wildberries", JobType.WbOrdersImport)
            .Should().BeOfType<OrdersFetchersResolution.Supported>().Subject;

        supported.Fetchers.Should().BeSameAs(wildberries);
    }

    [Fact]
    public void Resolve_OzonFbo_ReturnsTheFboSet()
    {
        (OrdersFetcherResolver resolver, _, IOrdersFetchers ozonFbo, _) = BuildResolver();

        OrdersFetchersResolution.Supported supported = resolver
            .Resolve("ozon", JobType.OzonOrdersImportFbo)
            .Should().BeOfType<OrdersFetchersResolution.Supported>().Subject;

        supported.Fetchers.Should().BeSameAs(ozonFbo);
    }

    [Fact]
    public void Resolve_OzonFbs_ReturnsTheFbsSet()
    {
        (OrdersFetcherResolver resolver, _, _, IOrdersFetchers ozonFbs) = BuildResolver();

        OrdersFetchersResolution.Supported supported = resolver
            .Resolve("ozon", JobType.OzonOrdersImportFbs)
            .Should().BeOfType<OrdersFetchersResolution.Supported>().Subject;

        supported.Fetchers.Should().BeSameAs(ozonFbs);
    }

    [Fact]
    public void Resolve_OzonFboAndFbs_ResolveToDifferentInstances()
    {
        (OrdersFetcherResolver resolver, _, IOrdersFetchers fbo, IOrdersFetchers fbs) = BuildResolver();

        IOrdersFetchers resolvedFbo = ((OrdersFetchersResolution.Supported)
            resolver.Resolve("ozon", JobType.OzonOrdersImportFbo)).Fetchers;
        IOrdersFetchers resolvedFbs = ((OrdersFetchersResolution.Supported)
            resolver.Resolve("ozon", JobType.OzonOrdersImportFbs)).Fetchers;

        resolvedFbo.Should().NotBeSameAs(resolvedFbs,
            "sharing one set would let the FBS job read the FBO endpoint and its statuses");
        resolvedFbo.Should().BeSameAs(fbo);
        resolvedFbs.Should().BeSameAs(fbs);
    }

    [Theory]
    [InlineData("ozon", JobType.WbOrdersImport)]
    [InlineData("ozon", JobType.ProductsImport)]
    [InlineData("wildberries", JobType.OzonOrdersImportFbo)]
    [InlineData("wildberries", JobType.OzonOrdersImportFbs)]
    [InlineData("wildberries", JobType.ProductsImport)]
    [InlineData("yandex.kit", JobType.WbOrdersImport)]
    [InlineData("", JobType.WbOrdersImport)]
    public void Resolve_UnsupportedCombination_ReturnsTheReason(string channelCode, JobType jobType)
    {
        (OrdersFetcherResolver resolver, _, _, _) = BuildResolver();

        OrdersFetchersResolution.Unsupported unsupported = resolver
            .Resolve(channelCode, jobType)
            .Should().BeOfType<OrdersFetchersResolution.Unsupported>().Subject;

        unsupported.Reason.Should().Be(
            $"Channel '{channelCode}' does not support orders import for job type '{jobType}'");
    }

    [Theory]
    [InlineData("OZON")]
    [InlineData("Ozon")]
    [InlineData("ozon")]
    public void Resolve_ChannelCodeIsCaseInsensitive(string channelCode)
    {
        (OrdersFetcherResolver resolver, _, _, IOrdersFetchers ozonFbs) = BuildResolver();

        OrdersFetchersResolution.Supported supported = resolver
            .Resolve(channelCode, JobType.OzonOrdersImportFbs)
            .Should().BeOfType<OrdersFetchersResolution.Supported>().Subject;

        supported.Fetchers.Should().BeSameAs(ozonFbs);
    }

    private static (OrdersFetcherResolver Resolver, IOrdersFetchers Wildberries, IOrdersFetchers OzonFbo, IOrdersFetchers OzonFbs)
        BuildResolver()
    {
        IOrdersFetchers wildberries = Mock.Of<IOrdersFetchers>();
        IOrdersFetchers ozonFbo = Mock.Of<IOrdersFetchers>();
        IOrdersFetchers ozonFbs = Mock.Of<IOrdersFetchers>();

        ServiceCollection services = new ServiceCollection();
        services.AddKeyedSingleton(
            OrdersFetcherKeys.Fetchers("wildberries", JobType.WbOrdersImport), wildberries);
        services.AddKeyedSingleton(
            OrdersFetcherKeys.Fetchers("ozon", JobType.OzonOrdersImportFbo), ozonFbo);
        services.AddKeyedSingleton(
            OrdersFetcherKeys.Fetchers("ozon", JobType.OzonOrdersImportFbs), ozonFbs);

        ServiceProvider provider = services.BuildServiceProvider();

        return (new OrdersFetcherResolver(provider), wildberries, ozonFbo, ozonFbs);
    }
}
