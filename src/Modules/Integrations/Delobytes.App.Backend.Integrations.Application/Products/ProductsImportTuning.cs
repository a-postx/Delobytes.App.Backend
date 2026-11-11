namespace Delobytes.App.Backend.Integrations.Application.Products;

/// <summary>
/// Channel-provided knobs of a products import run. Kept separate from any one marketplace's
/// options so the orchestrator never reads a channel-specific settings class.
/// </summary>
/// <param name="BatchSize">How many cards one published batch carries.</param>
public sealed record ProductsImportTuning(int BatchSize)
{
    /// <summary>
    /// Batch size used when a channel does not configure one.
    /// </summary>
    public const int DefaultBatchSize = 50;

    /// <summary>
    /// Gets the tuning a channel that does not register its own falls back to.
    /// </summary>
    public static ProductsImportTuning Default { get; } = new ProductsImportTuning(DefaultBatchSize);
}

/// <summary>
/// Resolves the products import tuning of the channel a run belongs to.
/// </summary>
public interface IProductsImportTuningResolver
{
    /// <summary>
    /// Returns the channel's tuning, or the shared default when the channel configures none.
    /// </summary>
    /// <param name="channelCode">Code of the channel template the run reads from.</param>
    public ProductsImportTuning Resolve(string channelCode);
}
