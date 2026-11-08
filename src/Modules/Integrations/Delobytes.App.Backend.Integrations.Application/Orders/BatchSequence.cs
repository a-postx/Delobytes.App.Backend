namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// The batch numbering of one run. Publishing takes a number from here, so the publisher never
/// holds a captured mutable counter and a redelivery continues where the previous attempt stopped.
/// </summary>
public sealed class BatchSequence
{
    public BatchSequence(int nextIndex)
    {
        NextIndex = nextIndex;
    }

    public int NextIndex { get; private set; }

    public int TakeNext()
    {
        int current = NextIndex;
        NextIndex++;
        return current;
    }
}
