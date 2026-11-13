namespace Delobytes.App.Backend.Catalog.Application.Interfaces;

/// <summary>
/// Opens the transactions a catalog command runs its allocation and persistence inside.
/// </summary>
public interface ICatalogTransactionFactory
{
    /// <summary>Opens a new transaction on the catalog database context of the current scope.</summary>
    public Task<ICatalogTransaction> BeginAsync(CancellationToken cancellationToken);
}
