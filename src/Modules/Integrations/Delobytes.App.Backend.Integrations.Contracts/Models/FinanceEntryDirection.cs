namespace Delobytes.App.Backend.Integrations.Contracts.Models;

/// <summary>
/// Which way a finance amount moves the line result. Mirrors the Finance journal direction.
/// </summary>
public enum FinanceEntryDirection
{
    /// <summary>Money the seller receives.</summary>
    Income = 0,

    /// <summary>Money the seller is charged.</summary>
    Expense = 1,
}
