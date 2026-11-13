using Delobytes.App.Backend.Contracts.Errors;

namespace Delobytes.App.Backend.Catalog.Application.Exceptions;

public class NumberingExhaustedException : AppException
{
    public NumberingExhaustedException(Guid sequenceId, string sequenceName, int endNumber)
        : base(
            ErrorCodes.Catalog.NumberingExhausted,
            $"Numbering sequence '{sequenceName}' ({sequenceId}) is exhausted: its next number would exceed {endNumber}.")
    {
        SequenceId = sequenceId;
        SequenceName = sequenceName;
        EndNumber = endNumber;
    }

    public Guid SequenceId { get; }

    public string SequenceName { get; }

    public int EndNumber { get; }
}
