using Delobytes.App.Backend.Contracts.Errors;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// The model-state response factory and the exception middleware share one field-name normalizer,
/// so nested and indexed FluentValidation paths must map onto the keys the client sends.
/// </summary>
public class ErrorFieldNamesTests
{
    [Theory]
    [InlineData("Sku", "sku")]
    [InlineData("sku", "sku")]
    [InlineData("Barcodes[0].Value", "barcodes[0].Value")]
    [InlineData("", "")]
    public void ToCamelCase_LowercasesOnlyTheFirstCharacter(string input, string expected)
    {
        ErrorFieldNames.ToCamelCase(input).Should().Be(expected);
    }
}
