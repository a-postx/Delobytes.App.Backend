using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// Tests for <see cref="OzonPhotoUrl"/>: the CDN renders WebP only from a path size segment, so the
/// rewrite has to insert exactly one and leave every other URL untouched.
/// </summary>
public class OzonPhotoUrlTests
{
    private const string OzonHost = "https://ir.ozone.ru";

    [Theory]
    [InlineData("https://ir.ozone.ru/s3/multimedia-1-z/7696694447.jpg", 328, "https://ir.ozone.ru/s3/multimedia-1-z/wc328/7696694447.jpg")]
    [InlineData("https://ir.ozone.ru/s3/multimedia-1-z/7696694447.jpg", 688, "https://ir.ozone.ru/s3/multimedia-1-z/wc688/7696694447.jpg")]
    [InlineData("https://ir.ozone.ru/s3/multimedia-7/6300677551.jpg", 328, "https://ir.ozone.ru/s3/multimedia-7/wc328/6300677551.jpg")]
    public void ToWebP_ForBareUrl_InsertsTheSizeSegment(string url, int maxSide, string expected)
    {
        OzonPhotoUrl.ToWebP(url, maxSide).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://ir-3.ozone.ru/s3/multimedia-7/6300677551.jpg", "https://ir-3.ozone.ru/s3/multimedia-7/wc328/6300677551.jpg")]
    [InlineData("https://cdn1.ozone.ru/s3/multimedia-1-m/6943754938.jpg", "https://cdn1.ozone.ru/s3/multimedia-1-m/wc328/6943754938.jpg")]
    public void ToWebP_KeepsTheHostItWasGiven(string url, string expected)
    {
        OzonPhotoUrl.ToWebP(url, OzonPhotoUrl.ThumbnailMaxSide).Should().Be(expected);
    }

    [Fact]
    public void ToWebP_ForUrlThatAlreadyCarriesASizeSegment_ReplacesIt()
    {
        OzonPhotoUrl
            .ToWebP($"{OzonHost}/s3/multimedia-1-z/wc1000/7696694447.jpg", OzonPhotoUrl.ThumbnailMaxSide)
            .Should().Be($"{OzonHost}/s3/multimedia-1-z/wc328/7696694447.jpg");
    }

    [Fact]
    public void ToWebP_ForUrlWithAJpegResizeSegment_ReplacesItWithTheWebPOne()
    {
        OzonPhotoUrl
            .ToWebP($"{OzonHost}/s3/multimedia-1-z/ww1000/7696694447.jpg", OzonPhotoUrl.LargeMaxSide)
            .Should().Be($"{OzonHost}/s3/multimedia-1-z/wc688/7696694447.jpg");
    }

    [Theory]
    [InlineData(328)]
    [InlineData(688)]
    public void ToWebP_IsIdempotent(int maxSide)
    {
        string once = OzonPhotoUrl.ToWebP($"{OzonHost}/s3/multimedia-1-z/7696694447.jpg", maxSide);

        OzonPhotoUrl.ToWebP(once, maxSide).Should().Be(once);
    }

    [Fact]
    public void ToWebP_KeepsTheQueryString()
    {
        OzonPhotoUrl
            .ToWebP($"{OzonHost}/s3/multimedia-1-z/7696694447.jpg?v=2", OzonPhotoUrl.ThumbnailMaxSide)
            .Should().Be($"{OzonHost}/s3/multimedia-1-z/wc328/7696694447.jpg?v=2");
    }

    [Fact]
    public void ToWebP_ForUrlWithoutABucket_ReturnsItUnchanged()
    {
        const string Url = "https://cdn.example/primary.jpg";

        OzonPhotoUrl.ToWebP(Url, OzonPhotoUrl.ThumbnailMaxSide).Should().Be(Url);
    }

    [Fact]
    public void ToWebP_ForANonOzonHost_ReturnsItUnchanged()
    {
        const string Url = "https://cdn.example/s3/multimedia-1-z/7696694447.jpg";

        OzonPhotoUrl.ToWebP(Url, OzonPhotoUrl.ThumbnailMaxSide).Should().Be(Url);
    }

    [Fact]
    public void ToWebP_ForBucketWithoutAnObject_ReturnsItUnchanged()
    {
        const string Url = "https://ir.ozone.ru/s3/multimedia-1-z";

        OzonPhotoUrl.ToWebP(Url, OzonPhotoUrl.ThumbnailMaxSide).Should().Be(Url);
    }

    [Fact]
    public void ToWebP_ForSizeSegmentWithoutAnObject_ReturnsItUnchanged()
    {
        const string Url = "https://ir.ozone.ru/s3/multimedia-1-z/wc1000/";

        OzonPhotoUrl.ToWebP(Url, OzonPhotoUrl.ThumbnailMaxSide).Should().Be(Url);
    }

    [Theory]
    [InlineData(49)]
    [InlineData(10000)]
    [InlineData(0)]
    public void ToWebP_ForOutOfRangeMaxSide_ReturnsTheUrlUnchanged(int maxSide)
    {
        const string Url = "https://ir.ozone.ru/s3/multimedia-1-z/7696694447.jpg";

        OzonPhotoUrl.ToWebP(Url, maxSide).Should().Be(Url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/s3/multimedia-1-z/7696694447.jpg")]
    public void ToWebP_ForUsableShapesItCannotRewrite_ReturnsTheUrlUnchanged(string url)
    {
        OzonPhotoUrl.ToWebP(url, OzonPhotoUrl.ThumbnailMaxSide).Should().Be(url);
    }
}
