namespace Delobytes.App.Backend.Contracts.Errors;

/// <summary>
/// Типизированный машиночитаемый код ошибки.
/// Wire-значение задано явно и не связано с именем члена,
/// поэтому переименование в C# не меняет контракт API.
/// </summary>
public readonly record struct ErrorCode
{
    internal ErrorCode(string value, int status, string defaultMessage)
    {
        Value = value;
        Status = status;
        DefaultMessage = defaultMessage;
    }

    public string Value { get; }

    public int Status { get; }

    public string DefaultMessage { get; }

    public override string ToString()
    {
        return Value;
    }
}
