namespace Delobytes.App.Backend.Contracts.Storage;

/// <summary>
/// Wraps provider-specific storage failures so no caller ever needs to catch
/// AmazonS3Exception or reference the AWS SDK.
/// </summary>
public class ObjectStorageException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectStorageException"/> class.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="innerException">Original provider-specific exception, if any.</param>
    public ObjectStorageException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
