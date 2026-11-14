using System;
using Delobytes.App.Backend.Catalog.Application.Services;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

/// <summary>
/// Tests for <see cref="WebPDimensions"/>: the three chunk layouts a still WebP can use, and the
/// guards that keep a truncated or foreign payload from yielding a bogus size.
/// </summary>
public class WebPDimensionsTests
{
    [Fact]
    public void TryRead_WithLossyChunk_ReadsWidthAndHeight()
    {
        byte[] payload = BuildLossy(750, 1000);

        WebPDimensions.TryRead(payload, out int width, out int height).Should().BeTrue();
        width.Should().Be(750);
        height.Should().Be(1000);
    }

    [Fact]
    public void TryRead_WithLosslessChunk_ReadsWidthAndHeight()
    {
        byte[] payload = BuildLossless(246, 328);

        WebPDimensions.TryRead(payload, out int width, out int height).Should().BeTrue();
        width.Should().Be(246);
        height.Should().Be(328);
    }

    [Fact]
    public void TryRead_WithExtendedChunk_ReadsCanvasSize()
    {
        byte[] payload = BuildExtended(516, 688);

        WebPDimensions.TryRead(payload, out int width, out int height).Should().BeTrue();
        width.Should().Be(516);
        height.Should().Be(688);
    }

    [Fact]
    public void TryRead_WithExtendedChunk_ReadsASizeAboveThe16BitRange()
    {
        byte[] payload = BuildExtended(1920, 2560);

        WebPDimensions.TryRead(payload, out int width, out int height).Should().BeTrue();
        width.Should().Be(1920);
        height.Should().Be(2560);
    }

    [Fact]
    public void TryRead_WithLandscapeDimensions_DoesNotSwapTheAxes()
    {
        byte[] payload = BuildExtended(328, 255);

        WebPDimensions.TryRead(payload, out int width, out int height).Should().BeTrue();
        width.Should().Be(328, "a 4:3 source becomes wider than tall, unlike a portrait");
        height.Should().Be(255);
    }

    [Fact]
    public void TryRead_WithUnknownFourCc_ReturnsFalse()
    {
        byte[] payload = BuildWithFourCc("ALPH"u8);

        WebPDimensions.TryRead(payload, out int width, out int height).Should().BeFalse();
        width.Should().Be(0);
        height.Should().Be(0);
    }

    [Fact]
    public void TryRead_WithPayloadShorterThanTheFirstChunk_ReturnsFalse()
    {
        byte[] payload = new byte[16];

        WebPDimensions.TryRead(payload, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryRead_WithEmptyPayload_ReturnsFalse()
    {
        WebPDimensions.TryRead(Array.Empty<byte>(), out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryRead_WithTruncatedLossyChunk_ReturnsFalse()
    {
        byte[] payload = BuildLossy(750, 1000);

        WebPDimensions.TryRead(payload.AsSpan(0, 28), out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryRead_WithLossyChunkMissingTheStartCode_ReturnsFalse()
    {
        byte[] payload = BuildLossy(750, 1000);
        payload[23] = 0x00;

        WebPDimensions.TryRead(payload, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryRead_WithLosslessChunkMissingItsSignature_ReturnsFalse()
    {
        byte[] payload = BuildLossless(246, 328);
        payload[20] = 0x00;

        WebPDimensions.TryRead(payload, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryRead_WithTruncatedExtendedChunk_ReturnsFalse()
    {
        byte[] payload = BuildExtended(516, 688);

        WebPDimensions.TryRead(payload.AsSpan(0, 29), out _, out _).Should().BeFalse();
    }

    private static byte[] BuildWithFourCc(ReadOnlySpan<byte> fourCc)
    {
        byte[] payload = new byte[64];
        "RIFF"u8.CopyTo(payload);
        "WEBP"u8.CopyTo(payload.AsSpan(8));
        fourCc.CopyTo(payload.AsSpan(12, 4));

        return payload;
    }

    private static byte[] BuildLossy(int width, int height)
    {
        byte[] payload = BuildWithFourCc("VP8 "u8);

        payload[23] = 0x9D;
        payload[24] = 0x01;
        payload[25] = 0x2A;
        payload[26] = (byte)(width & 0xFF);
        payload[27] = (byte)((width >> 8) & 0x3F);
        payload[28] = (byte)(height & 0xFF);
        payload[29] = (byte)((height >> 8) & 0x3F);

        return payload;
    }

    private static byte[] BuildLossless(int width, int height)
    {
        byte[] payload = BuildWithFourCc("VP8L"u8);

        payload[20] = 0x2F;

        uint bits = (uint)(width - 1) | ((uint)(height - 1) << 14);
        payload[21] = (byte)(bits & 0xFF);
        payload[22] = (byte)((bits >> 8) & 0xFF);
        payload[23] = (byte)((bits >> 16) & 0xFF);
        payload[24] = (byte)((bits >> 24) & 0xFF);

        return payload;
    }

    private static byte[] BuildExtended(int width, int height)
    {
        byte[] payload = BuildWithFourCc("VP8X"u8);

        int storedWidth = width - 1;
        int storedHeight = height - 1;

        payload[24] = (byte)(storedWidth & 0xFF);
        payload[25] = (byte)((storedWidth >> 8) & 0xFF);
        payload[26] = (byte)((storedWidth >> 16) & 0xFF);
        payload[27] = (byte)(storedHeight & 0xFF);
        payload[28] = (byte)((storedHeight >> 8) & 0xFF);
        payload[29] = (byte)((storedHeight >> 16) & 0xFF);

        return payload;
    }
}
