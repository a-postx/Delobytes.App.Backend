namespace Delobytes.App.Backend.Contracts.Errors;

/// <summary>
/// Normalizes validation field names to the camelCase form the client sends, so a client can
/// map errors back onto form fields.
/// </summary>
public static class ErrorFieldNames
{
    public static string ToCamelCase(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName) || char.IsLower(fieldName[0]))
        {
            return fieldName;
        }

        return char.ToLowerInvariant(fieldName[0]) + fieldName[1..];
    }
}
