using System;

namespace Delobytes.App.Backend.Tests.TestSupport;

/// <summary>
/// Pins the clock so time-dependent logic resolves deterministically. One copy for the whole test
/// project: several suites used to carry their own identical class, and a change had to be repeated.
/// </summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    public FixedTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow()
    {
        return _utcNow;
    }
}
