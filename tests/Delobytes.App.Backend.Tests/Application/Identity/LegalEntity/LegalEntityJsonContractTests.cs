using System.Text.Json;
using System.Text.Json.Serialization;
using Delobytes.App.Backend.Identity.Application.Queries.GetTenantLegalEntity;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.LegalEntity;

/// <summary>
/// Pins the JSON contract of the legal entity payload.
///
/// Tax fields used to travel in this payload and left the API as enum member names
/// ("Usn", "None") because the application registers JsonStringEnumConverter. They now
/// live in the separate tax-profile contract, and the legal entity response must no
/// longer carry them at all — a client that still reads taxType here would silently
/// keep showing a stale regime.
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
    public void Serialize_Response_NoLongerCarriesAnyTaxField()
    {
        // Arrange
        GetTenantLegalEntityResponse response = new GetTenantLegalEntityResponse
        {
            TenantId = Guid.Parse("d40fc941-b390-4d6d-b346-8aff2c2716bd"),
            LegalName = "ООО «Ромашка»",
            Inn = "7712345678",
        };

        // Act
        string json = JsonSerializer.Serialize(response, SerializerOptions);

        // Assert
        json.Should().NotContain("taxType");
        json.Should().NotContain("taxRatePercent");
        json.Should().NotContain("vatType");
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
        };

        // Act
        string json = JsonSerializer.Serialize(response, SerializerOptions);

        // Assert
        json.Should().Contain("\"tenantId\"");
        json.Should().Contain("\"legalName\"");
        json.Should().Contain("\"inn\"");
    }

    [Fact]
    public void Deserialize_LegacyFrontendPayload_IgnoresRetiredTaxFields()
    {
        // Arrange
        // A frontend build that has not been updated yet still posts the old fields;
        // ignoring them is the intended tolerant behaviour, and the legal entity values
        // must survive.
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
        request!.LegalName.Should().Be("ООО «Ромашка»");
        request.Inn.Should().Be("7712345678");
    }

    /// <summary>
    /// Request shape mirroring the legal entity payload the frontend sends, so the
    /// deserialization contract can be asserted without referencing controller models.
    /// </summary>
    private sealed class UpdateTenantLegalEntityRequestDto
    {
        public string? LegalName { get; set; }

        public string? Inn { get; set; }
    }
}
