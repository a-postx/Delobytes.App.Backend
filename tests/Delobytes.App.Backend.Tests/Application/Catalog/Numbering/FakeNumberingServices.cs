using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Numbering;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Tests.Application.Catalog.Numbering;

/// <summary>
/// Stand-in for <see cref="INumberAllocator"/> that issues the standard product-code format from
/// an in-memory counter. The real allocator needs an ambient transaction and row locks, which the
/// InMemory provider cannot provide, so every test that only cares about the caller's use of the
/// issued value uses this one.
/// </summary>
internal sealed class FakeNumberAllocator : INumberAllocator
{
    private int _nextNumber;

    public FakeNumberAllocator(int startNumber = 1)
    {
        _nextNumber = startNumber;
    }

    public int CallCount { get; private set; }

    public Exception? ExceptionToThrow { get; set; }

    public Task<string> AllocateAsync(Guid schemeId, CancellationToken cancellationToken)
    {
        CallCount++;

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        string code = "P-" + _nextNumber.ToString("D6", CultureInfo.InvariantCulture);
        _nextNumber++;

        return Task.FromResult(code);
    }
}

/// <summary>
/// Stand-in for <see cref="INumberingSchemeProvider"/> that returns one fixed, valid scheme and
/// counts how often it was asked. The real provider persists its seed, which is covered by
/// <see cref="NumberingSchemeProviderTests"/>.
/// </summary>
internal sealed class FakeNumberingSchemeProvider : INumberingSchemeProvider
{
    public NumberingScheme Scheme { get; set; } = new NumberingScheme
    {
        Id = Guid.NewGuid(),
        Code = "PRODUCT",
        Name = "Product code",
        EntityKind = NumberingEntityKind.Product,
        IsDefault = true,
        IsActive = true,
    };

    public int CallCount { get; private set; }

    public Task<NumberingScheme> GetDefaultProductSchemeAsync(CancellationToken cancellationToken)
    {
        CallCount++;

        return Task.FromResult(Scheme);
    }
}

/// <summary>
/// Stand-in for <see cref="ICatalogTransactionFactory"/> that opens transactions recording their
/// own lifecycle. Call order relative to the repository and the allocator is asserted by the
/// transaction tests; everything else only needs a transaction that does not throw.
/// </summary>
internal sealed class FakeCatalogTransactionFactory : ICatalogTransactionFactory
{
    public List<string> CallLog { get; } = new ();

    public FakeCatalogTransaction Transaction { get; private set; } = default!;

    public Task<ICatalogTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        CallLog.Add("begin");
        Transaction = new FakeCatalogTransaction(CallLog);

        return Task.FromResult<ICatalogTransaction>(Transaction);
    }
}

internal sealed class FakeCatalogTransaction : ICatalogTransaction
{
    private readonly List<string> _callLog;

    public FakeCatalogTransaction(List<string> callLog)
    {
        _callLog = callLog;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        _callLog.Add("commit");

        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken cancellationToken)
    {
        _callLog.Add("rollback");

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
