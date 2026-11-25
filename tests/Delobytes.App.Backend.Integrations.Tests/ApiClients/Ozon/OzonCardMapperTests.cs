using System.Collections.Generic;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients.Ozon;

/// <summary>
/// Tests the pure projection of an Ozon attribute item onto the channel-neutral card. Unit conversion
/// and photo de-duplication are the two places where a mapping mistake is silent: the import stores a
/// wrong size or a repeated gallery and nothing fails.
/// </summary>
public class OzonCardMapperTests
{
    private const string FirstImage = "https://cdn.example/card.jpg";
    private const string SecondImage = "https://cdn.example/second.jpg";

    [Fact]
    public void Map_MillimetreAndGramUnits_AreConvertedToCentimetresAndKilograms()
    {
        OzonProductAttributesItem item = BuildItem(
            depth: 100m,
            width: 200m,
            height: 300m,
            dimensionUnit: "mm",
            weight: 500m,
            weightUnit: "g");

        ChannelProductCard card = OzonCardMapper.Map(item);

        card.LengthCm.Should().Be(10m);
        card.WidthCm.Should().Be(20m);
        card.HeightCm.Should().Be(30m);
        card.WeightKg.Should().Be(0.5m);
    }

    [Fact]
    public void Map_CentimetreAndKilogramUnits_AreTakenAsReported()
    {
        OzonProductAttributesItem item = BuildItem(
            depth: 10m,
            dimensionUnit: "cm",
            weight: 0.5m,
            weightUnit: "kg");

        ChannelProductCard card = OzonCardMapper.Map(item);

        card.LengthCm.Should().Be(10m);
        card.WeightKg.Should().Be(0.5m);
    }

    [Fact]
    public void Map_IdentityAndChannelMarkers_ComeFromTheSkuAndTheChannel()
    {
        OzonProductAttributesItem item = BuildItem(sku: 5918369809, offerId: "offer-1");

        ChannelProductCard card = OzonCardMapper.Map(item);

        card.ExternalProductId.Should().Be("5918369809");
        card.OfferId.Should().Be("offer-1");
        card.ChannelCode.Should().Be("ozon");
        card.BarcodeType.Should().Be("ozon");
    }

    [Fact]
    public void Map_PrimaryImageRepeatedInTheGallery_YieldsNoDuplicateEntry()
    {
        OzonProductAttributesItem item = BuildItem(
            primaryImage: FirstImage,
            images: new List<string> { FirstImage, SecondImage });

        ChannelProductCard card = OzonCardMapper.Map(item);

        card.Photos.Should().HaveCount(4);
        card.Photos.Select(photo => photo.Url).Should().Equal(
            FirstImage, FirstImage, SecondImage, SecondImage);
        card.Photos.Select(photo => photo.SizeVariant).Should().Equal(
            "large", "thumbnail", "large", "thumbnail");
        card.Photos.Select(photo => photo.DisplayOrder).Should().Equal(1, 1, 2, 2);
    }

    [Fact]
    public void Map_BarcodeList_IsPreferredOverTheSingleBarcodeField()
    {
        OzonProductAttributesItem item = BuildItem(
            barcode: "999",
            barcodes: new List<string> { "111", " ", "222" });

        ChannelProductCard card = OzonCardMapper.Map(item);

        card.Barcodes.Should().Equal("111", "222");
    }

    [Fact]
    public void Map_DescriptionAttribute_IsReadIntoTheDescription()
    {
        OzonProductAttributesItem item = BuildItem(
            attributes: new List<OzonProductAttribute>
            {
                new OzonProductAttribute
                {
                    Id = 4191,
                    Values = new List<OzonProductAttributeValue>
                    {
                        new OzonProductAttributeValue { Value = " " },
                        new OzonProductAttributeValue { Value = "Описание карточки" },
                    },
                },
            });

        ChannelProductCard card = OzonCardMapper.Map(item);

        card.Description.Should().Be("Описание карточки");
    }

    private static OzonProductAttributesItem BuildItem(
        long sku = 555,
        string offerId = "offer-1",
        string? barcode = null,
        List<string>? barcodes = null,
        decimal depth = 0m,
        decimal width = 0m,
        decimal height = 0m,
        string? dimensionUnit = "cm",
        decimal weight = 0m,
        string? weightUnit = "kg",
        string? primaryImage = null,
        List<string>? images = null,
        List<OzonProductAttribute>? attributes = null)
    {
        return new OzonProductAttributesItem
        {
            Id = 11,
            Sku = sku,
            OfferId = offerId,
            Name = "Карточка",
            Barcode = barcode,
            Barcodes = barcodes ?? new List<string>(),
            Depth = depth,
            Width = width,
            Height = height,
            DimensionUnit = dimensionUnit,
            Weight = weight,
            WeightUnit = weightUnit,
            PrimaryImage = primaryImage,
            Images = images ?? new List<string>(),
            Attributes = attributes ?? new List<OzonProductAttribute>(),
        };
    }
}
