using System.Buffers.Binary;

namespace Delobytes.App.Backend.Catalog.Application.Services;

/// <summary>
/// Reads the pixel dimensions out of a WebP header without decoding the image.
/// </summary>
/// <remarks>
/// The pipeline stores the size of the bytes it actually receives, and the marketplaces do not
/// report it reliably: Wildberries names its variants by box rather than by rendered size, and
/// Ozon reports none at all. The container states the real size in its first chunk, so the header
/// is enough. Nothing here decodes pixels, allocates, or trusts a chunk length to bound a read;
/// every read is guarded by the payload length.
/// </remarks>
internal static class WebPDimensions
{
    /// <summary>RIFF header: "RIFF", a length and "WEBP" precede the first chunk.</summary>
    private const int ChunkPayloadOffset = 20;

    private const int ChunkFourCcOffset = 12;
    private const int ChunkFourCcLength = 4;

    private static ReadOnlySpan<byte> Vp8StartCode => [0x9D, 0x01, 0x2A];

    // Lossy: a 3-byte frame tag, then the start code that identifies a key frame, then 16-bit
    // width and height, each carrying 14 significant bits.
    private const int Vp8StartCodeOffset = 3;
    private const int Vp8StartCodeLength = 3;
    private const int Vp8WidthOffset = 6;
    private const int Vp8HeightOffset = 8;
    private const int Vp8PayloadLength = 10;
    private const int Vp8DimensionMask = 0x3FFF;

    // Lossless: a 1-byte signature, then 14 bits of width-1 and 14 bits of height-1.
    private const int Vp8lSignature = 0x2F;
    private const int Vp8lPayloadLength = 5;
    private const int Vp8lDimensionMask = 0x3FFF;
    private const int Vp8lHeightShift = 14;

    // Extended: a flags byte and 3 reserved bytes, then 24-bit canvas width-1 and height-1.
    private const int Vp8xWidthOffset = 4;
    private const int Vp8xHeightOffset = 7;
    private const int Vp8xPayloadLength = 10;
    private const int Vp8xDimensionBytes = 3;

    /// <summary>
    /// Tries to read the dimensions of a WebP image.
    /// </summary>
    /// <param name="payload">Complete WebP file, already verified by <see cref="WebPValidator"/>.</param>
    /// <param name="width">Width in pixels, or 0 when the header is not understood.</param>
    /// <param name="height">Height in pixels, or 0 when the header is not understood.</param>
    /// <returns><c>true</c> when both dimensions were read.</returns>
    internal static bool TryRead(ReadOnlySpan<byte> payload, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (payload.Length < ChunkPayloadOffset)
        {
            return false;
        }

        ReadOnlySpan<byte> fourCc = payload.Slice(ChunkFourCcOffset, ChunkFourCcLength);

        if (fourCc.SequenceEqual("VP8 "u8))
        {
            return TryReadLossy(payload, out width, out height);
        }

        if (fourCc.SequenceEqual("VP8L"u8))
        {
            return TryReadLossless(payload, out width, out height);
        }

        if (fourCc.SequenceEqual("VP8X"u8))
        {
            return TryReadExtended(payload, out width, out height);
        }

        return false;
    }

    private static bool TryReadLossy(ReadOnlySpan<byte> payload, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (payload.Length < ChunkPayloadOffset + Vp8PayloadLength)
        {
            return false;
        }

        ReadOnlySpan<byte> startCode = payload.Slice(ChunkPayloadOffset + Vp8StartCodeOffset, Vp8StartCodeLength);

        if (!startCode.SequenceEqual(Vp8StartCode))
        {
            return false;
        }

        width = BinaryPrimitives.ReadUInt16LittleEndian(payload[(ChunkPayloadOffset + Vp8WidthOffset)..]) & Vp8DimensionMask;
        height = BinaryPrimitives.ReadUInt16LittleEndian(payload[(ChunkPayloadOffset + Vp8HeightOffset)..]) & Vp8DimensionMask;

        return true;
    }

    private static bool TryReadLossless(ReadOnlySpan<byte> payload, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (payload.Length < ChunkPayloadOffset + Vp8lPayloadLength)
        {
            return false;
        }

        if (payload[ChunkPayloadOffset] != Vp8lSignature)
        {
            return false;
        }

        // The two fields are packed into one 32-bit word; the top 4 bits are unused.
        uint bits = BinaryPrimitives.ReadUInt32LittleEndian(payload[(ChunkPayloadOffset + 1)..]);

        width = (int)(bits & Vp8lDimensionMask) + 1;
        height = (int)((bits >> Vp8lHeightShift) & Vp8lDimensionMask) + 1;

        return true;
    }

    private static bool TryReadExtended(ReadOnlySpan<byte> payload, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (payload.Length < ChunkPayloadOffset + Vp8xPayloadLength)
        {
            return false;
        }

        width = ReadUInt24LittleEndian(payload.Slice(ChunkPayloadOffset + Vp8xWidthOffset, Vp8xDimensionBytes)) + 1;
        height = ReadUInt24LittleEndian(payload.Slice(ChunkPayloadOffset + Vp8xHeightOffset, Vp8xDimensionBytes)) + 1;

        return true;
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> source)
    {
        return source[0] | (source[1] << 8) | (source[2] << 16);
    }
}
