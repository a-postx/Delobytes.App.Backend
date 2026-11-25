namespace Delobytes.App.Backend.Catalog.Application.Products;

/// <summary>
/// How one card ended up in the catalogue. A card that fails is not reported through this value:
/// the failure is an exception, and the batch records it as an error.
/// </summary>
public enum ImportStatus
{
    /// <summary>The card became a new product.</summary>
    Created,

    /// <summary>The card was written onto an existing product or link.</summary>
    Updated,

    /// <summary>Nothing was created or changed; the card was deliberately left alone.</summary>
    Skipped,
}

/// <summary>
/// Counters of one batch, as reported in <c>ProductBatchImportCompletedEvent</c>. A failed card is
/// recorded with its error as well, because the run's status is derived from
/// <see cref="Failed"/> and from a non-empty <see cref="ErrorMessage"/>.
/// </summary>
public sealed class ProductImportCounters
{
    private readonly List<string> _errors = new List<string>();

    /// <summary>Cards the batch went through, including the ones that failed.</summary>
    public int Processed { get; private set; }

    /// <summary>Cards that became new products.</summary>
    public int Created { get; private set; }

    /// <summary>Cards that changed an existing product or link.</summary>
    public int Updated { get; private set; }

    /// <summary>Cards that required no write, or could not be resolved to one product.</summary>
    public int Skipped { get; private set; }

    /// <summary>Cards that failed.</summary>
    public int Failed { get; private set; }

    /// <summary>Errors of the failed cards, or <c>null</c> when there were none.</summary>
    public string? ErrorMessage => _errors.Count == 0 ? null : string.Join("; ", _errors);

    /// <summary>Records the outcome of one card.</summary>
    /// <param name="status">Outcome the card reported.</param>
    public void Record(ImportStatus status)
    {
        Processed++;

        switch (status)
        {
            case ImportStatus.Created:
                Created++;
                break;
            case ImportStatus.Updated:
                Updated++;
                break;
            case ImportStatus.Skipped:
                Skipped++;
                break;
            default:
                throw new InvalidOperationException($"Unhandled import status {status}.");
        }
    }

    /// <summary>Records a card that could not be imported.</summary>
    /// <param name="externalProductId">Card identifier, for the operator.</param>
    /// <param name="errorMessage">What went wrong.</param>
    public void RecordCardFailure(string externalProductId, string errorMessage)
    {
        Processed++;
        Failed++;
        _errors.Add($"NmId={externalProductId}: {errorMessage}");
    }

    /// <summary>
    /// Records a failure of the batch itself, before any card could be counted.
    /// </summary>
    /// <param name="errorMessage">What went wrong.</param>
    /// <param name="cardsCount">Cards the batch carried; all of them failed.</param>
    public void RecordBatchFailure(string errorMessage, int cardsCount)
    {
        Failed += cardsCount;
        _errors.Add(errorMessage);
    }
}
