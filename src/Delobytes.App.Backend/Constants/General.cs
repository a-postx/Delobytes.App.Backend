namespace Delobytes.App.Backend.Constants;

public static class General
{
    /// <summary>
    /// UTC kind conversion exist in EF Core so refactoring is needed in case of value change.
    /// </summary>
    public const string DefaultSqlModelDateTimeFunction = "now() at time zone 'utc'";
}
