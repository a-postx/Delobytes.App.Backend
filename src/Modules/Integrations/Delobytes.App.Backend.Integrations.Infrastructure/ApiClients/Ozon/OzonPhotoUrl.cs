using System.Globalization;
using System.Text;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// Rewrites an Ozon image URL onto the CDN's WebP rendering endpoint.
/// </summary>
/// <remarks>
/// Ozon serves the bare URL (<c>/s3/{bucket}/{id}.jpg</c>) as the original JPEG and ignores
/// <c>Accept</c> entirely. Inserting a size segment (<c>/s3/{bucket}/wc{maxSide}/{id}.jpg</c>)
/// makes the CDN render that size as WebP, which is the only format the photo pipeline accepts.
/// The segment is drawn from the path rather than a query string, so the rewrite stays idempotent:
/// an URL that already carries a size segment keeps exactly one.
/// </remarks>
internal static class OzonPhotoUrl
{
    /// <summary>Longest side of the thumbnail variant; renders 246x328 for a 3:4 portrait.</summary>
    internal const int ThumbnailMaxSide = 328;

    /// <summary>Longest side of the large variant; renders 516x688 for a 3:4 portrait.</summary>
    internal const int LargeMaxSide = 688;

    // "w" selects WebP rendering and the second letter selects the fit mode; Ozon answers a
    // request without the leading "w" with the same resize in JPEG, so the prefix is mandatory.
    private const string SizeSegmentPrefix = "wc";

    private const string BucketMarker = "/s3/";

    // Only Ozon's own CDN has the rendering endpoint; a third-party URL that happens to contain
    // "/s3/" must not be rewritten, or the import would request an image that does not exist.
    private const string CdnHostSuffix = ".ozone.ru";

    // The CDN answers 403 outside this range, and an out-of-range value would turn every photo
    // into a failed download instead of a visible mapping error.
    private const int MaxSideMinimum = 50;
    private const int MaxSideMaximum = 9999;

    private const int MaxDigits = 5;
    private const int MaxModeLetters = 2;

    /// <summary>
    /// Returns the URL rewritten onto the WebP endpoint of the given size. Anything the shape
    /// <c>/s3/{bucket}/[size/]{object}</c> does not cover is returned unchanged, so an unexpected
    /// URL fails loudly in the photo pipeline instead of being rewritten by guesswork.
    /// </summary>
    /// <param name="url">Absolute URL as reported by the Ozon API.</param>
    /// <param name="maxSide">Longest side of the rendered image, 50 to 9999.</param>
    /// <returns>The rewritten URL, or <paramref name="url"/> when it cannot be rewritten.</returns>
    internal static string ToWebP(string url, int maxSide)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        if (maxSide < MaxSideMinimum || maxSide > MaxSideMaximum)
        {
            return url;
        }

        int queryStart = url.IndexOf('?', StringComparison.Ordinal);
        string path = queryStart < 0 ? url : url[..queryStart];
        string query = queryStart < 0 ? string.Empty : url[queryStart..];

        if (!IsOzonCdnUrl(path))
        {
            return url;
        }

        int bucketMarker = path.IndexOf(BucketMarker, StringComparison.Ordinal);

        if (bucketMarker <= 0)
        {
            return url;
        }

        int bucketStart = bucketMarker + BucketMarker.Length;
        int bucketEnd = path.IndexOf('/', bucketStart);

        if (bucketEnd < 0)
        {
            return url;
        }

        string objectPath = path[(bucketEnd + 1)..];

        if (objectPath.Length == 0)
        {
            return url;
        }

        int objectSeparator = objectPath.IndexOf('/', StringComparison.Ordinal);
        string firstSegment = objectSeparator < 0 ? objectPath : objectPath[..objectSeparator];
        string remainder = objectSeparator < 0 ? string.Empty : objectPath[(objectSeparator + 1)..];

        string target;

        if (objectSeparator < 0)
        {
            target = objectPath;
        }
        else if (IsSizeSegment(firstSegment))
        {
            // A size segment with nothing behind it names no object, so there is nothing to render.
            if (remainder.Length == 0)
            {
                return url;
            }

            target = remainder;
        }
        else
        {
            target = objectPath;
        }

        string segment = SizeSegmentPrefix + maxSide.ToString(CultureInfo.InvariantCulture);

        StringBuilder builder = new StringBuilder(path.Length + segment.Length + 1);
        builder.Append(path, 0, bucketEnd + 1);
        builder.Append(segment);
        builder.Append('/');
        builder.Append(target);
        builder.Append(query);

        return builder.ToString();
    }

    // Only Ozon's own CDN renders sizes, so the host is checked before the path is touched.
    // The bare "ozone.ru" is accepted as well: it is the same zone, one label up.
    private static bool IsOzonCdnUrl(string path)
    {
        int schemeEnd = path.IndexOf("://", StringComparison.Ordinal);

        if (schemeEnd < 0)
        {
            return false;
        }

        int hostStart = schemeEnd + 3;
        int hostEnd = path.IndexOf('/', hostStart);

        if (hostEnd < 0)
        {
            return false;
        }

        ReadOnlySpan<char> host = path.AsSpan(hostStart, hostEnd - hostStart);

        if (host.EndsWith(CdnHostSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return host.Equals(CdnHostSuffix.AsSpan(1), StringComparison.OrdinalIgnoreCase);
    }

    // Recognises the size segment Ozon puts directly after the bucket: one or two mode letters
    // followed by the pixel count. A file name such as "w1000.jpg" never matches, because the
    // extension is not a digit.
    private static bool IsSizeSegment(string segment)
    {
        int digitsStart = 0;

        while (digitsStart < segment.Length && digitsStart < MaxModeLetters)
        {
            char letter = segment[digitsStart];

            if (letter != 'w' && letter != 'W' && letter != 'c' && letter != 'C')
            {
                break;
            }

            digitsStart++;
        }

        if (digitsStart == 0 || digitsStart == segment.Length)
        {
            return false;
        }

        if (segment.Length - digitsStart > MaxDigits)
        {
            return false;
        }

        for (int index = digitsStart; index < segment.Length; index++)
        {
            if (!char.IsAsciiDigit(segment[index]))
            {
                return false;
            }
        }

        return true;
    }
}
