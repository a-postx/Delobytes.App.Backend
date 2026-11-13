using System.Globalization;
using System.Text;
using Delobytes.App.Backend.Catalog.Application.Exceptions;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentValidation;

namespace Delobytes.App.Backend.Catalog.Application.Numbering;

/// <summary>
/// Composes identifiers out of a scheme's ordered segments and advances the auto-number counters.
/// </summary>
/// <remarks>
/// Allocation requires the caller's ambient transaction: the row locks taken through
/// <see cref="INumberingSequenceStore"/> are released when that transaction commits, not when a
/// single statement or savepoint completes. Manual product creation therefore holds the lock for
/// the duration of one insert, while a batch import holds it until the whole batch commits —
/// concurrent batches queue on that row instead of interleaving numbers.
/// </remarks>
public sealed class NumberAllocator : INumberAllocator
{
    private readonly INumberingSequenceStore _store;
    private readonly IValidator<NumberingScheme> _validator;

    public NumberAllocator(INumberingSequenceStore store, IValidator<NumberingScheme> validator)
    {
        _store = store;
        _validator = validator;
    }

    /// <inheritdoc/>
    public async Task<string> AllocateAsync(Guid schemeId, CancellationToken cancellationToken)
    {
        NumberingScheme? scheme = await _store.GetSchemeAsync(schemeId, cancellationToken)
            ?? throw new InvalidOperationException($"Numbering scheme '{schemeId}' was not found.");

        await _validator.ValidateAndThrowAsync(scheme, cancellationToken);

        if (!scheme.IsActive)
        {
            throw new InvalidOperationException(
                $"Numbering scheme '{scheme.Code}' ({scheme.Id}) is inactive and cannot issue numbers.");
        }

        List<NumberingSegment> segments = scheme.Segments
            .OrderBy(segment => segment.Order)
            .ToList();

        Guid[] sequenceIds = segments
            .Where(segment => segment.Type == NumberingSegmentType.AutoNumber)
            .Select(ResolveSequenceId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        await _store.LockSequencesAsync(sequenceIds, cancellationToken);
        await _store.RefreshLockedSequencesAsync(sequenceIds, cancellationToken);

        int[] issuedNumbers = new int[segments.Count];
        Dictionary<Guid, int> cursors = ProjectNumbers(segments, issuedNumbers);

        string composed = Compose(segments, issuedNumbers);

        // Counters are written only after every segment of the scheme has been projected, so an
        // exhausted or malformed scheme cannot leave a half-advanced set of sequences behind.
        AdvanceCounters(segments, cursors);

        return composed;
    }

    private static Guid ResolveSequenceId(NumberingSegment segment)
    {
        return segment.NumberingSequenceId
            ?? throw new InvalidOperationException(
                $"Segment {segment.Order} of the numbering scheme is an auto number without a sequence.");
    }

    private static NumberingSequence ResolveSequence(NumberingSegment segment)
    {
        return segment.NumberingSequence
            ?? throw new InvalidOperationException(
                $"Segment {segment.Order} of the numbering scheme references sequence " +
                $"'{segment.NumberingSequenceId}' which was not loaded.");
    }

    private static Dictionary<Guid, int> ProjectNumbers(List<NumberingSegment> segments, int[] issuedNumbers)
    {
        Dictionary<Guid, int> cursors = new ();

        for (int index = 0; index < segments.Count; index++)
        {
            NumberingSegment segment = segments[index];

            if (segment.Type != NumberingSegmentType.AutoNumber)
            {
                continue;
            }

            NumberingSequence sequence = ResolveSequence(segment);

            if (!sequence.IsActive)
            {
                throw new InvalidOperationException(
                    $"Numbering sequence '{sequence.Name}' ({sequence.Id}) is inactive and cannot issue numbers.");
            }

            // A freshly inserted row carries the counter at its CLR default, and StartNumber is the
            // lowest number the sequence may ever issue, so the two are reconciled here rather than
            // in every seeding path.
            int cursor = cursors.TryGetValue(sequence.Id, out int current)
                ? current
                : Math.Max(sequence.NextNumber, sequence.StartNumber);

            if (sequence.EndNumber.HasValue && cursor > sequence.EndNumber.Value && sequence.WarnOnClose)
            {
                throw new NumberingExhaustedException(sequence.Id, sequence.Name, sequence.EndNumber.Value);
            }

            // The counter is an int: a number it can no longer advance past is the hard end of the
            // range, whatever EndNumber says.
            if (cursor > int.MaxValue - sequence.Step)
            {
                throw new NumberingExhaustedException(sequence.Id, sequence.Name, int.MaxValue);
            }

            issuedNumbers[index] = cursor;
            cursors[sequence.Id] = cursor + sequence.Step;
        }

        return cursors;
    }

    private static string Compose(List<NumberingSegment> segments, int[] issuedNumbers)
    {
        StringBuilder builder = new ();

        for (int index = 0; index < segments.Count; index++)
        {
            NumberingSegment segment = segments[index];

            if (segment.Type == NumberingSegmentType.Constant)
            {
                builder.Append(segment.ConstantValue);
            }
            else
            {
                builder.Append(FormatAutoNumber(issuedNumbers[index], segment.Length));
            }

            if (segment.SeparatorAfter is not null)
            {
                builder.Append(segment.SeparatorAfter);
            }
        }

        return builder.ToString();
    }

    private static string FormatAutoNumber(int number, int? length)
    {
        string value = number.ToString(CultureInfo.InvariantCulture);

        return length.HasValue && length.Value > value.Length
            ? value.PadLeft(length.Value, '0')
            : value;
    }

    private static void AdvanceCounters(List<NumberingSegment> segments, Dictionary<Guid, int> cursors)
    {
        foreach (NumberingSegment segment in segments)
        {
            if (segment.Type != NumberingSegmentType.AutoNumber)
            {
                continue;
            }

            ResolveSequence(segment).NextNumber = cursors[ResolveSequenceId(segment)];
        }
    }
}
