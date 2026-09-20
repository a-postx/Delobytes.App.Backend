namespace Delobytes.App.Backend.Contracts.Errors;

public sealed class FieldError
{
    public FieldError(string field, string message)
    {
        Field = field;
        Message = message;
    }

    public string Field { get; }

    public string Message { get; }
}
