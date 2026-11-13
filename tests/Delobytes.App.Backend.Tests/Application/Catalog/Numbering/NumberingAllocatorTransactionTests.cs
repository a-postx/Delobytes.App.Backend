using System;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Numbering;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Tests.Application.Catalog.Numbering;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog.Numbering;

/// <summary>
/// Allocation must never compose a value from a counter read before its row was locked. The real
/// <c>FOR UPDATE</c> statement and its "no ambient transaction" guard need PostgreSQL and a second
/// connection, so these tests model the store contract with a fake instead of pretending that an
/// in-memory provider proves serialization.
/// </summary>
public class NumberingAllocatorTransactionTests
{
    private static NumberingScheme BuildScheme(NumberingSequence sequence)
    {
        return NumberingTestData.BuildScheme(
            segments: new[]
            {
                NumberingTestData.Constant(1, "P-"),
                NumberingTestData.AutoNumber(2, sequence, 6),
            });
    }

    [Fact]
    public async Task AllocateAsync_WhenLockingFails_PropagatesAndLeavesCounterUnchanged()
    {
        NumberingSequence sequence = NumberingTestData.BuildSequence();
        NumberingScheme scheme = BuildScheme(sequence);

        FakeNumberingSequenceStore store = new FakeNumberingSequenceStore()
            .Add(scheme)
            .SetNumberAfterLock(sequence.Id, 99);

        store.LockException = new InvalidOperationException("Numbering sequences can only be locked inside an ambient transaction.");

        NumberAllocator allocator = new NumberAllocator(store, new NumberingSchemeValidator());

        Func<Task> act = () => allocator.AllocateAsync(scheme.Id, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ambient transaction*");
        sequence.NextNumber.Should().Be(1);
        store.CallOrder.Should().Equal("lock");
    }

    [Fact]
    public async Task AllocateAsync_LocksBeforeRefreshing_AndIssuesThePostLockValue()
    {
        // 7 is the value that was visible before the lock; 42 is what another committed allocation
        // left in the row. Issuing 7 would hand out a number that already exists.
        NumberingSequence sequence = NumberingTestData.BuildSequence(nextNumber: 7);
        NumberingScheme scheme = BuildScheme(sequence);

        FakeNumberingSequenceStore store = new FakeNumberingSequenceStore()
            .Add(scheme)
            .SetNumberAfterLock(sequence.Id, 42);

        NumberAllocator allocator = new NumberAllocator(store, new NumberingSchemeValidator());

        string allocated = await allocator.AllocateAsync(scheme.Id, default);

        allocated.Should().Be("P-000042");
        store.CallOrder.Should().Equal("lock", "refresh");
        store.RequestedSequenceIds.Should().Equal(sequence.Id);
        sequence.NextNumber.Should().Be(43);
    }
}
