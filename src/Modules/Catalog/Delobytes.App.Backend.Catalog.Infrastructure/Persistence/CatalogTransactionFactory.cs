using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ICatalogTransactionFactory"/>. Keeps
/// <see cref="IDbContextTransaction"/> out of the application layer, the same way
/// <see cref="ProductImportStore.BeginBatchAsync"/> does for the product import.
/// </summary>
public class CatalogTransactionFactory : ICatalogTransactionFactory
{
    private readonly CatalogDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogTransactionFactory"/> class.
    /// </summary>
    /// <param name="context">Catalog database context.</param>
    public CatalogTransactionFactory(CatalogDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<ICatalogTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        return new CatalogTransaction(transaction);
    }

    private sealed class CatalogTransaction : ICatalogTransaction
    {
        private readonly IDbContextTransaction _transaction;

        public CatalogTransaction(IDbContextTransaction transaction)
        {
            _transaction = transaction;
        }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            return _transaction.CommitAsync(cancellationToken);
        }

        public async Task RollbackAsync(CancellationToken cancellationToken)
        {
            // A failure after commit is not a failed rollback: the catch block that calls this
            // must not mask the original exception with "transaction already completed".
            if (_transaction.GetDbTransaction().Connection is null)
            {
                return;
            }

            try
            {
                await _transaction.RollbackAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Already committed or already rolled back by the provider.
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _transaction.DisposeAsync();
        }
    }
}
