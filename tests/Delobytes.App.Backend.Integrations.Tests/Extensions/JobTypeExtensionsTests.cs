using System;
using Delobytes.App.Backend.Integrations.Application.Extensions;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Extensions;

public class JobTypeExtensionsTests
{
    [Theory]
    [InlineData(JobType.ProductsImport, "products-import")]
    [InlineData(JobType.OrdersImport, "orders-import")]
    [InlineData(JobType.OrdersSync, "orders-sync")]
    [InlineData(JobType.OrdersImportFbo, "orders-import-fbo")]
    [InlineData(JobType.OrdersImportFbs, "orders-import-fbs")]
    public void ToStringCamelCase_ReturnsStableApiToken(JobType jobType, string expected)
    {
        // The token is the public route segment: it must not follow a rename of the enum member.
        jobType.ToStringCamelCase().Should().Be(expected);
    }

    [Fact]
    public void ToStringCamelCase_ThrowsForUndefinedEnumValue()
    {
        Func<string> act = () => ((JobType)999).ToStringCamelCase();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("products-import", JobType.ProductsImport)]
    [InlineData("orders-import", JobType.OrdersImport)]
    [InlineData("orders-sync", JobType.OrdersSync)]
    [InlineData("orders-import-fbo", JobType.OrdersImportFbo)]
    [InlineData("orders-import-fbs", JobType.OrdersImportFbs)]
    [InlineData("Products-Import", JobType.ProductsImport)]
    [InlineData("ORDERS-IMPORT", JobType.OrdersImport)]
    [InlineData("  products-import  ", JobType.ProductsImport)]
    public void TryFromString_AcceptsKnownTokens(string token, JobType expected)
    {
        JobTypeExtensions.TryFromString(token, out JobType jobType).Should().BeTrue();
        jobType.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("productsimport")]
    [InlineData("products_import")]
    [InlineData("ProductsImport")]
    [InlineData("999")]
    public void TryFromString_RejectsUnknownTokens(string? token)
    {
        JobTypeExtensions.TryFromString(token, out JobType jobType).Should().BeFalse();
        jobType.Should().Be(default(JobType));
    }

    [Theory]
    [InlineData(JobType.ProductsImport, true)]
    [InlineData(JobType.OrdersImport, false)]
    [InlineData(JobType.OrdersSync, false)]
    [InlineData(JobType.OrdersImportFbo, false)]
    [InlineData(JobType.OrdersImportFbs, false)]
    public void IsCancellable_OnlyForUserInitiatedJobs(JobType jobType, bool expected)
    {
        jobType.IsCancellable().Should().Be(expected);
    }

    [Theory]
    [InlineData(JobType.ProductsImport, true)]
    [InlineData(JobType.OrdersImport, false)]
    [InlineData(JobType.OrdersSync, false)]
    [InlineData(JobType.OrdersImportFbo, false)]
    [InlineData(JobType.OrdersImportFbs, false)]
    public void IsManuallyStartable_OnlyForUserInitiatedJobs(JobType jobType, bool expected)
    {
        jobType.IsManuallyStartable().Should().Be(expected);
    }

    [Fact]
    public void ToStringCamelCase_RoundTripsThroughFromString_ForEveryDefinedType()
    {
        foreach (JobType jobType in Enum.GetValues<JobType>())
        {
            JobTypeExtensions.TryFromString(jobType.ToStringCamelCase(), out JobType parsed).Should().BeTrue();
            parsed.Should().Be(jobType);
        }
    }
}
