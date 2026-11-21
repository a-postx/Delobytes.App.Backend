using FluentValidation;
using FluentValidation.Results;

namespace Delobytes.App.Backend.Finance.Api.Controllers;

/// <summary>
/// Validation of the period an endpoint reports on. Query-string dates bind to <c>default</c> when
/// omitted, so a bound <c>0001-01-01</c> is treated as a missing parameter rather than a period.
/// </summary>
internal static class PnlPeriodQuery
{
    /// <summary>
    /// Returns the required period or throws a validation error the client can map back onto the
    /// <c>from</c> and <c>to</c> fields.
    /// </summary>
    public static (DateOnly From, DateOnly To) Require(DateOnly? from, DateOnly? to)
    {
        List<ValidationFailure> failures = new List<ValidationFailure>();

        if (from is null || from.Value == default)
        {
            failures.Add(new ValidationFailure("from", "Начало периода обязательно."));
        }

        if (to is null || to.Value == default)
        {
            failures.Add(new ValidationFailure("to", "Конец периода обязателен."));
        }

        if (failures.Count == 0 && from!.Value > to!.Value)
        {
            failures.Add(new ValidationFailure("from", "Начало периода не может быть позже его конца."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return (from!.Value, to!.Value);
    }
}
