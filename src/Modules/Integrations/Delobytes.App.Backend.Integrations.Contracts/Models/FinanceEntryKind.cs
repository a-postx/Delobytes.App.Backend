namespace Delobytes.App.Backend.Integrations.Contracts.Models;

/// <summary>
/// What a finance entry represents in the arithmetic of a line. Mirrors the Finance journal kind.
/// </summary>
public enum FinanceEntryKind
{
    /// <summary>A component summed into the line result.</summary>
    Component = 0,

    /// <summary>The channel's own total for the document, used only as a check.</summary>
    ControlTotal = 1,
}
