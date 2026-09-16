using System.Text.Json;
using System.Text.Json.Serialization;
using Delobytes.App.Backend.Identity.Application.Queries.GetTenantLegalEntity;
using Delobytes.App.Backend.Identity.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.LegalEntity;

/// <summary>
/// Pins the JSON contract of the legal entity payload (stage 10).
///
/// The application registers JsonStringEnumConverter, so TaxType and VatType leave the
/// API as member names ("Usn", "None"). A client that treats them as numbers silently
/// fails to match the value it receives, which is exactly what happened before, so the
/// wire format is asserted here rather than left implicit.
/// </summary>
public class LegalEntityJsonContractTests
{
    /// <summary>
    /// Mirrors the serializer configuration applied to controllers in Program.cs and
    /// WebApplicationBuilderExtensions (string enums plus camelCase properties).
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;

        return options;
    }

    [Fact]
    public void Serialize_Response_WritesTaxTypeAndVatTypeAsMemberNames()
    {
        // Arrange
        GetTenantLegalEntityResponse response = new GetTenantLegalEntityResponse
        {
            TenantId = Guid.Parse("d40fc941-b390-4d6d-b346-8aff2c2716bd"),
            LegalName = "ООО «Ромашка»",
            Inn = "7712345678",
            TaxType = TaxType.Usn,
            TaxRatePercent = 6m,
            VatType = VatType.None,
        };

        // Act
        string json = JsonSerializer.Serialize(response, SerializerOptions);

        // Assert
        json.Should().Contain("\"taxType\":\"Usn\"");
        json.Should().Contain("\"vatType\":\"None\"");
    }

    [Fact]
    public void Serialize_Response_UsesCamelCasePropertyNames()
    {
        // Arrange
        GetTenantLegalEntityResponse response = new GetTenantLegalEntityResponse
        {
            TenantId = Guid.NewGuid(),
            LegalName = "ООО «Ромашка»",
            Inn = "7712345678",
            TaxType = TaxType.Usn,
            TaxRatePercent = 6m,
            VatType = VatType.None,
        };

        // Act
        string json = JsonSerializer.Serialize(response, SerializerOptions);

        // Assert
        json.Should().Contain("\"tenantId\"");
        json.Should().Contain("\"legalName\"");
        json.Should().Contain("\"inn\"");
        json.Should().Contain("\"taxRatePercent\"");
    }

    [Theory]
    [InlineData(TaxType.Usn, "Usn")]
    [InlineData(TaxType.Osno, "Osno")]
    [InlineData(TaxType.Npd, "Npd")]
    public void Serialize_EveryTaxType_WritesItsDeclaredName(TaxType taxType, string expectedName)
    {
        // Arrange
        GetTenantLegalEntityResponse response = new GetTenantLegalEntityResponse
        {
            TenantId = Guid.NewGuid(),
            TaxType = taxType,
            VatType = VatType.None,
        };

        // Act
        JsonElement root = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(response, SerializerOptions));

        // Assert
        root.GetProperty("taxType").GetString().Should().Be(expectedName);
    }

    [Theory]
    [InlineData(VatType.None, "None")]
    [InlineData(VatType.Five, "Five")]
    [InlineData(VatType.Seven, "Seven")]
    [InlineData(VatType.TwentyTwo, "TwentyTwo")]
    public void Serialize_EveryVatType_WritesItsDeclaredName(VatType vatType, string expectedName)
    {
        // Arrange
        GetTenantLegalEntityResponse response = new GetTenantLegalEntityResponse
        {
            TenantId = Guid.NewGuid(),
            TaxType = TaxType.Usn,
            VatType = vatType,
        };

        // Act
        JsonElement root = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(response, SerializerOptions));

        // Assert
        root.GetProperty("vatType").GetString().Should().Be(expectedName);
    }

    [Fact]
    public void Deserialize_FrontendPayload_BindsStringEnumNamesToEnumMembers()
    {
        // Arrange
        const string FrontendPayload = """
            {
              "legalName": "ООО «Ромашка»",
              "inn": "7712345678",
              "taxType": "Osno",
              "taxRatePercent": 20.5,
              "vatType": "Seven"
            }
            """;

        // Act
        UpdateTenantLegalEntityRequestDto? request =
            JsonSerializer.Deserialize<UpdateTenantLegalEntityRequestDto>(FrontendPayload, SerializerOptions);

        // Assert
        request.Should().NotBeNull();
        request!.TaxType.Should().Be(TaxType.Osno);
        request.TaxRatePercent.Should().Be(20.5m);
        request.VatType.Should().Be(VatType.Seven);
    }

    [Fact]
    public void Deserialize_CommandResponse_PreservesDecimalPrecisionOfTheTaxRate()
    {
        // Arrange
        GetTenantLegalEntityResponse response = new GetTenantLegalEntityResponse
        {
            TenantId = Guid.NewGuid(),
            TaxType = TaxType.Usn,
            TaxRatePercent = 6.5m,
            VatType = VatType.None,
        };

        // Act
        string json = JsonSerializer.Serialize(response, SerializerOptions);
        GetTenantLegalEntityResponse? roundTripped =
            JsonSerializer.Deserialize<GetTenantLegalEntityResponse>(json, SerializerOptions);

        // Assert
        roundTripped!.TaxRatePercent.Should().Be(6.5m);
    }

    /// <summary>
    /// Request shape mirroring the legal entity payload the frontend sends, so the
    /// deserialization contract can be asserted without referencing controller models.
    /// </summary>
    private sealed class UpdateTenantLegalEntityRequestDto
    {
        public string? LegalName { get; set; }

        public string? Inn { get; set; }

        public TaxType TaxType { get; set; }

        public decimal TaxRatePercent { get; set; }

        public VatType VatType { get; set; }
    }
}
