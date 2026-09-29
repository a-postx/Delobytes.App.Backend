namespace Delobytes.App.Backend.Catalog.Infrastructure.Storage;

/// <summary>
/// Content sniffing for the only image format the pipeline accepts.
/// The marketplace URL suffix is not trusted: the signature inside the payload decides.
/// </summary>
internal static class WebPValidator
{
    /// <summary>Smallest possible WebP header: "RIFF" + 4-byte length + "WEBP".</summary>
    internal const int HeaderLength = 12;

    private const int RiffSignatureOffset = 0;
    private const int WebPFormatOffset = 8;

    /// <summary>
    /// Checks the 12-byte RIFF/WEBP signature at the start of the payload.
    /// The header is read from the beginning of the stream, and on a seekable stream the
    /// position is put back to 0 afterwards, so the same stream can be uploaded right away.
    /// A non-seekable stream cannot be rewound: its first <see cref="HeaderLength"/> bytes
    /// are consumed by the check.
    /// </summary>
    /// <param name="stream">Stream to inspect.</param>
    /// <returns><c>true</c> when the payload starts with a valid WebP header.</returns>
    internal static bool HasValidHeader(Stream stream)
    {
        if (stream is null)
        {
            return false;
        }

        if (stream.CanSeek)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }

        byte[] header = new byte[HeaderLength];
        int read = ReadExactly(stream, header, HeaderLength);

        if (stream.CanSeek)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }

        return read == HeaderLength
            && HasRiffSignature(header)
            && HasWebPFormat(header);
    }

    // The RIFF signature is "RIFF"; bytes 4..7 hold the little-endian chunk size and are
    // deliberately not validated, because a streaming producer may not have filled them in yet.
    private static bool HasRiffSignature(byte[] header)
    {
        return header[RiffSignatureOffset] == (byte)'R'
            && header[RiffSignatureOffset + 1] == (byte)'I'
            && header[RiffSignatureOffset + 2] == (byte)'F'
            && header[RiffSignatureOffset + 3] == (byte)'F';
    }

    private static bool HasWebPFormat(byte[] header)
    {
        return header[WebPFormatOffset] == (byte)'W'
            && header[WebPFormatOffset + 1] == (byte)'E'
            && header[WebPFormatOffset + 2] == (byte)'B'
            && header[WebPFormatOffset + 3] == (byte)'P';
    }

    // Stream.Read may return fewer bytes than requested even before the end of the stream,
    // so a single call is not enough to decide whether the file is long enough.
    private static int ReadExactly(Stream stream, byte[] buffer, int count)
    {
        int total = 0;

        while (total < count)
        {
            int read = stream.Read(buffer, total, count - total);

            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
