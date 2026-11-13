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
    [InlineData(JobType.WbOrdersImport, "wb-orders-import")]
    [InlineData(JobType.OzonOrdersImportFbo, "ozon-orders-import-fbo")]
    [InlineData(JobType.OzonOrdersImportFbs, "ozon-orders-import-fbs")]
    [InlineData(JobType.OzonProductsImport, "ozon-products-import")]
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
    [InlineData("wb-orders-import", JobType.WbOrdersImport)]
    [InlineData("ozon-orders-import-fbo", JobType.OzonOrdersImportFbo)]
    [InlineData("ozon-orders-import-fbs", JobType.OzonOrdersImportFbs)]
    [InlineData("ozon-products-import", JobType.OzonProductsImport)]
    [InlineData("Products-Import", JobType.ProductsImport)]
    [InlineData("WB-ORDERS-IMPORT", JobType.WbOrdersImport)]
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
    [InlineData(JobType.OzonProductsImport, true)]
    [InlineData(JobType.WbOrdersImport, false)]
    [InlineData(JobType.OzonOrdersImportFbo, false)]
    [InlineData(JobType.OzonOrdersImportFbs, false)]
    public void IsCancellable_OnlyForUserInitiatedJobs(JobType jobType, bool expected)
    {
        jobType.IsCancellable().Should().Be(expected);
    }

    [Theory]
    [InlineData(JobType.ProductsImport, true)]
    [InlineData(JobType.OzonProductsImport, true)]
    [InlineData(JobType.WbOrdersImport, false)]
    [InlineData(JobType.OzonOrdersImportFbo, false)]
    [InlineData(JobType.OzonOrdersImportFbs, false)]
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
