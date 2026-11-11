using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Orders;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Resolves the real orders graph from the real registrations, so a missing keyed registration is
/// caught here instead of at the first scheduled run.
/// </summary>
public class OrdersFetcherContainerTests
{
    [Theory]
    [InlineData("wildberries", JobType.WbOrdersImport)]
    [InlineData("ozon", JobType.OzonOrdersImportFbo)]
    [InlineData("ozon", JobType.OzonOrdersImportFbs)]
    public void Resolve_SupportedPair_ResolvesTheFetcherSet(string channelCode, JobType jobType)
    {
        using ServiceProvider provider = BuildProvider();

        using IServiceScope scope = provider.CreateScope();
        IOrdersFetcherResolver resolver = scope.ServiceProvider.GetRequiredService<IOrdersFetcherResolver>();

        OrdersFetchersResolution.Supported supported = resolver
            .Resolve(channelCode, jobType)
            .Should().BeOfType<OrdersFetchersResolution.Supported>().Subject;

        supported.Fetchers.Backfill.Should().NotBeNull();
        supported.Fetchers.Delta.Should().NotBeNull();
        supported.Fetchers.Tuning.MaxRequestsPerRun.Should().BePositive();
    }

    [Fact]
    public void Resolve_UnsupportedPair_ReturnsUnsupported()
    {
        using ServiceProvider provider = BuildProvider();

        using IServiceScope scope = provider.CreateScope();
        IOrdersFetcherResolver resolver = scope.ServiceProvider.GetRequiredService<IOrdersFetcherResolver>();

        resolver.Resolve("wildberries", JobType.OzonOrdersImportFbo)
            .Should().BeOfType<OrdersFetchersResolution.Unsupported>();
    }

    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => Mock.Of<ITenantContext>());
        services.AddScoped(_ => Mock.Of<IPublishEndpoint>());
        services.AddIntegrationsInfrastructure(
            "Host=localhost;Database=test;Username=test;Password=test");

        return services.BuildServiceProvider(validateScopes: true);
    }
}
