using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Numbering;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Tests.Application.Catalog.Numbering;

/// <summary>
/// In-memory stand-in for <see cref="INumberingSequenceStore"/>. It hands the allocator the same
/// tracked entity graph the real store would and records the lock/refresh call order, so the
/// allocator's composition logic can be tested without a database. The <c>FOR UPDATE</c> SQL and
/// the "no ambient transaction" guard inside the real store cannot be proven here: they need
/// PostgreSQL and a second connection, and a fake would pass regardless.
/// </summary>
internal sealed class FakeNumberingSequenceStore : INumberingSequenceStore
{
    private readonly Dictionary<Guid, NumberingScheme> _schemes = new ();
    private readonly Dictionary<Guid, int> _numbersAfterLock = new ();

    public List<string> CallOrder { get; } = new ();

    public List<Guid> RequestedSequenceIds { get; } = new ();

    public Exception? LockException { get; set; }

    public FakeNumberingSequenceStore Add(NumberingScheme scheme)
    {
        _schemes[scheme.Id] = scheme;

        return this;
    }

    public FakeNumberingSequenceStore SetNumberAfterLock(Guid sequenceId, int nextNumber)
    {
        _numbersAfterLock[sequenceId] = nextNumber;

        return this;
    }

    public Task<NumberingScheme?> GetSchemeAsync(Guid schemeId, CancellationToken cancellationToken)
    {
        _schemes.TryGetValue(schemeId, out NumberingScheme? scheme);

        return Task.FromResult(scheme);
    }

    public Task LockSequencesAsync(IReadOnlyCollection<Guid> sequenceIds, CancellationToken cancellationToken)
    {
        CallOrder.Add("lock");
        RequestedSequenceIds.AddRange(sequenceIds);

        if (LockException is not null)
        {
            throw LockException;
        }

        return Task.CompletedTask;
    }

    public Task RefreshLockedSequencesAsync(
        IReadOnlyCollection<Guid> sequenceIds,
        CancellationToken cancellationToken)
    {
        CallOrder.Add("refresh");

        foreach (Guid sequenceId in sequenceIds)
        {
            if (!_numbersAfterLock.TryGetValue(sequenceId, out int persisted))
            {
                continue;
            }

            NumberingSequence? sequence = _schemes.Values
                .SelectMany(scheme => scheme.Segments)
                .Select(segment => segment.NumberingSequence)
                .FirstOrDefault(candidate => candidate is not null && candidate.Id == sequenceId);

            if (sequence is not null)
            {
                sequence.NextNumber = persisted;
            }
        }

        return Task.CompletedTask;
    }
}
