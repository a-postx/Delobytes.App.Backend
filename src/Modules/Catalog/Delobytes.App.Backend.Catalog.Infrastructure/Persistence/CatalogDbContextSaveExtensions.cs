using Delobytes.App.Backend.Contracts.Errors;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence;

/// <summary>
/// Single save path for Catalog repositories: concurrency and unique-constraint failures are
/// translated into <see cref="AppException"/> so the API answers 409 with a machine-readable
/// code instead of 500 from a raw <see cref="DbUpdateException"/>.
/// </summary>
internal static class CatalogDbContextSaveExtensions
{
    public static async Task<int> SaveChangesWithConflictTranslationAsync(this CatalogDbContext context, CancellationToken ct)
    {
        try
        {
            return await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new AppException(ErrorCodes.Common.Conflict, ex.Message);
        }
        catch (DbUpdateException ex)
        {
            // DbUpdateConcurrencyException derives from DbUpdateException, so it must be caught
            // above this block; here only genuine write conflicts remain.
            if (UniqueConstraintTranslator.TryTranslate(ex, out AppException? translated))
            {
                throw translated!;
            }

            throw;
        }
    }
}
