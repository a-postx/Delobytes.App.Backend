using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Exceptions;
using Delobytes.App.Backend.Catalog.Application.Numbering;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Tests.Application.Catalog.Numbering;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog.Numbering;

public class NumberingAllocatorTests
{
    private static NumberAllocator BuildAllocator(NumberingScheme scheme, out FakeNumberingSequenceStore store)
    {
        store = new FakeNumberingSequenceStore().Add(scheme);

        return new NumberAllocator(store, new NumberingSchemeValidator());
    }

    private static NumberingScheme BuildProductScheme(NumberingSequence sequence, string constant = "P-")
    {
        return NumberingTestData.BuildScheme(
            segments: new[]
            {
                NumberingTestData.Constant(1, constant),
                NumberingTestData.AutoNumber(2, sequence, 6),
            });
    }

    [Fact]
    public async Task AllocateAsync_SingleAutoSegmentWithLengthSix_ReturnsPaddedSequentialCodes()
    {
        NumberingSequence sequence = NumberingTestData.BuildSequence();
        NumberingScheme scheme = BuildProductScheme(sequence);
        NumberAllocator allocator = BuildAllocator(scheme, out _);

        string first = await allocator.AllocateAsync(scheme.Id, default);
        string second = await allocator.AllocateAsync(scheme.Id, default);

        first.Should().Be("P-000001");
        second.Should().Be("P-000002");
    }

    [Fact]
    public async Task AllocateAsync_StepTen_ReturnsEveryTenthNumber()
    {
        NumberingSequence sequence = NumberingTestData.BuildSequence(startNumber: 10, step: 10);
        NumberingScheme scheme = BuildProductScheme(sequence);
        NumberAllocator allocator = BuildAllocator(scheme, out _);

        string first = await allocator.AllocateAsync(scheme.Id, default);
        string second = await allocator.AllocateAsync(scheme.Id, default);

        first.Should().Be("P-000010");
        second.Should().Be("P-000020");
    }

    [Fact]
    public async Task AllocateAsync_TwoAutoSegments_AdvanceTheirOwnSequences()
    {
        NumberingSequence first = NumberingTestData.BuildSequence();
        NumberingSequence second = NumberingTestData.BuildSequence();

        NumberingScheme scheme = NumberingTestData.BuildScheme(
            segments: new[]
            {
                NumberingTestData.Constant(1, "A-"),
                NumberingTestData.AutoNumber(2, first, 2, "/"),
                NumberingTestData.Constant(3, "B-"),
                NumberingTestData.AutoNumber(4, second, 4),
            });

        NumberAllocator allocator = BuildAllocator(scheme, out _);

        string composed = await allocator.AllocateAsync(scheme.Id, default);

        composed.Should().Be("A-01/B-0001");
        first.NextNumber.Should().Be(2);
        second.NextNumber.Should().Be(2);
    }

    [Fact]
    public async Task AllocateAsync_RepeatedAllocations_ProgressWithoutAnyClock()
    {
        NumberingSequence sequence = NumberingTestData.BuildSequence();
        NumberingScheme scheme = BuildProductScheme(sequence);
        NumberAllocator allocator = BuildAllocator(scheme, out _);

        string first = await allocator.AllocateAsync(scheme.Id, default);
        string second = await allocator.AllocateAsync(scheme.Id, default);
        string third = await allocator.AllocateAsync(scheme.Id, default);

        new[] { first, second, third }.Should().Equal("P-000001", "P-000002", "P-000003");
    }

    [Fact]
    public void ProductNumbering_ExposesNoCalendarResetPath()
    {
        // Product numbering never resets: there is no reset period, no reset timestamp, and the
        // allocator takes no clock, so no code path can make a number depend on the current date.
        typeof(NumberingSequence).GetProperty("ResetPeriod").Should().BeNull();
        typeof(NumberingSequence).GetProperty("LastResetAt").Should().BeNull();

        IEnumerable<string> constructorDependencies = typeof(NumberAllocator)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.Name);

        constructorDependencies.Should().NotContain(new[]
        {
            nameof(TimeProvider),
            nameof(DateTime),
            nameof(DateTimeOffset),
            "ITenantTimeService",
        });

        typeof(NumberAllocator)
            .GetMethods()
            .Select(method => method.Name)
            .Should()
            .NotContain(name => name.Contains("Reset", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AllocateAsync_EndNumberExceededWithWarnOnClose_ThrowsAndLeavesCounterUnchanged()
    {
        NumberingSequence sequence = NumberingTestData.BuildSequence(nextNumber: 5, endNumber: 5, warnOnClose: true);
        NumberingScheme scheme = BuildProductScheme(sequence);
        NumberAllocator allocator = BuildAllocator(scheme, out _);

        string lastNumberInRange = await allocator.AllocateAsync(scheme.Id, default);

        Func<Task> act = () => allocator.AllocateAsync(scheme.Id, default);

        lastNumberInRange.Should().Be("P-000005");
        await act.Should().ThrowAsync<NumberingExhaustedException>();
        sequence.NextNumber.Should().Be(6);
    }

    [Fact]
    public async Task AllocateAsync_EndNumberExceededWithoutWarnOnClose_KeepsCounting()
    {
        NumberingSequence sequence = NumberingTestData.BuildSequence(nextNumber: 5, endNumber: 5, warnOnClose: false);
        NumberingScheme scheme = BuildProductScheme(sequence);
        NumberAllocator allocator = BuildAllocator(scheme, out _);

        await allocator.AllocateAsync(scheme.Id, default);
        string beyondEnd = await allocator.AllocateAsync(scheme.Id, default);

        beyondEnd.Should().Be("P-000006");
    }

    [Fact]
    public async Task AllocateAsync_ConstantOnlyScheme_ReturnsSameValueAndAdvancesNothing()
    {
        NumberingScheme scheme = NumberingTestData.BuildScheme(
            segments: new[]
            {
                NumberingTestData.Constant(1, "P-"),
                NumberingTestData.Constant(2, "FIXED"),
            });

        NumberAllocator allocator = BuildAllocator(scheme, out FakeNumberingSequenceStore store);

        string first = await allocator.AllocateAsync(scheme.Id, default);
        string second = await allocator.AllocateAsync(scheme.Id, default);

        first.Should().Be("P-FIXED");
        second.Should().Be("P-FIXED");
        store.RequestedSequenceIds.Should().BeEmpty();
    }

    [Fact]
    public async Task AllocateAsync_TwoAutoSegmentsWithDifferentSteps_AdvanceIndependently()
    {
        NumberingSequence perUnit = NumberingTestData.BuildSequence();
        NumberingSequence perBatch = NumberingTestData.BuildSequence(startNumber: 100, step: 5);

        NumberingScheme scheme = NumberingTestData.BuildScheme(
            segments: new[]
            {
                NumberingTestData.Constant(1, "A-"),
                NumberingTestData.AutoNumber(2, perUnit, 2, "/"),
                NumberingTestData.Constant(3, "B-"),
                NumberingTestData.AutoNumber(4, perBatch, 4),
            });

        NumberAllocator allocator = BuildAllocator(scheme, out _);

        string first = await allocator.AllocateAsync(scheme.Id, default);
        string second = await allocator.AllocateAsync(scheme.Id, default);

        first.Should().Be("A-01/B-0100");
        second.Should().Be("A-02/B-0105");
        perUnit.NextNumber.Should().Be(3);
        perBatch.NextNumber.Should().Be(110);
    }

    [Fact]
    public async Task AllocateAsync_InactiveScheme_Throws()
    {
        NumberingSequence sequence = NumberingTestData.BuildSequence();
        NumberingScheme scheme = NumberingTestData.BuildScheme(
            isActive: false,
            segments: new[]
            {
                NumberingTestData.Constant(1, "P-"),
                NumberingTestData.AutoNumber(2, sequence, 6),
            });

        NumberAllocator allocator = BuildAllocator(scheme, out _);

        Func<Task> act = () => allocator.AllocateAsync(scheme.Id, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{scheme.Code}*is inactive*");
    }
}
