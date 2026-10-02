using Delobytes.App.Backend.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence;

/// <summary>
/// Turns a database unique-constraint violation into the domain error it actually represents.
/// Without this, SaveChangesAsync surfaces a raw DbUpdateException, which the exception
/// middleware can only map to 500 — the caller then learns nothing about what collided.
/// </summary>
internal static class UniqueConstraintTranslator
{
    private const string UniqueViolationSqlState = "23505";

    /// <summary>Index name to the error code that explains it.</summary>
    private static readonly Dictionary<string, ErrorCode> KnownConstraints = new Dictionary<string, ErrorCode>
    {
        ["IX_Products_TenantId_Sku"] = ErrorCodes.Catalog.ProductSkuConflict,
        ["IX_ProductBarcodes_TenantId_Value"] = ErrorCodes.Catalog.ProductBarcodeConflict,
    };

    /// <summary>
    /// Maps <paramref name="exception"/> to an <see cref="AppException"/> when it is a unique
    /// violation. Returns false for everything else — including exceptions raised by
    /// non-relational providers, which carry no SqlState — so the caller rethrows the original.
    /// </summary>
    /// <param name="exception">Exception thrown by SaveChanges.</param>
    /// <param name="translated">Domain exception when the method returns true.</param>
    public static bool TryTranslate(DbUpdateException exception, out AppException? translated)
    {
        translated = null;

        PostgresException? postgres = FindPostgresException(exception);

        if (postgres == null || postgres.SqlState != UniqueViolationSqlState)
        {
            return false;
        }

        string? constraintName = postgres.ConstraintName;

        ErrorCode code = constraintName != null && KnownConstraints.TryGetValue(constraintName, out ErrorCode known)
            ? known
            : ErrorCodes.Common.Conflict;

        // The provider message is deliberately dropped: it quotes index and column names, which
        // are schema details. The error code alone carries the meaning the client needs.
        translated = new AppException(code, null, exception);
        return true;
    }

    private static PostgresException? FindPostgresException(Exception? exception)
    {
        // EF wraps the provider exception, and a multi-statement batch can nest it a level deeper.
        for (int depth = 0; exception != null && depth < 5; depth++)
        {
            if (exception is PostgresException postgres)
            {
                return postgres;
            }

            exception = exception.InnerException;
        }

        return null;
    }
}
