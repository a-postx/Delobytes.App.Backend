using System.Text.RegularExpressions;
using Delobytes.App.Backend.Services;
using FluentAssertions;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// Tests for <see cref="CorrelationIdProvider"/>, which guards the boundary between
/// attacker-controlled header input and values that end up in logs and responses.
/// </summary>
public class CorrelationIdProviderTests
{
    [Fact]
    public void Create_ReturnsLowercaseHexOfExpectedLength()
    {
        string id = CorrelationIdProvider.Create();

        id.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public void Create_CalledRepeatedly_ReturnsDistinctValues()
    {
        HashSet<string> ids = new HashSet<string>();

        for (int i = 0; i < 100; i++)
        {
            ids.Add(CorrelationIdProvider.Create());
        }

        ids.Should().HaveCount(100);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("has.dot")]
    [InlineData("has:colon")]
    [InlineData("newline\ninjection")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("юникод")]
    public void IsWellFormed_RejectsUnsafeValues(string? value)
    {
        CorrelationIdProvider.IsWellFormed(value).Should().BeFalse();
    }

    [Fact]
    public void IsWellFormed_RejectsValueLongerThan64Characters()
    {
        string tooLong = new string('a', 65);

        CorrelationIdProvider.IsWellFormed(tooLong).Should().BeFalse();
    }

    [Fact]
    public void IsWellFormed_AcceptsValueOfExactly64Characters()
    {
        string boundary = new string('a', 64);

        CorrelationIdProvider.IsWellFormed(boundary).Should().BeTrue();
    }

    [Theory]
    [InlineData("4bf92f3577b34da6a3ce929d0e0e4736")]
    [InlineData("5c1e0a3d-1b2e-4d5f-8a9b-0c1d2e3f4a5b")]
    [InlineData("trace_id-with_underscores")]
    [InlineData("ABC123")]
    public void IsWellFormed_AcceptsOrdinaryIdentifierValues(string value)
    {
        CorrelationIdProvider.IsWellFormed(value).Should().BeTrue();
    }

    [Fact]
    public void Resolve_WithWellFormedValue_ReturnsThatValue()
    {
        string incoming = "client-supplied-id";

        CorrelationIdProvider.Resolve(incoming).Should().Be(incoming);
    }

    [Fact]
    public void Resolve_WithMalformedValue_GeneratesSubstitute()
    {
        string resolved = CorrelationIdProvider.Resolve("not a valid id");

        resolved.Should().NotBe("not a valid id");
        CorrelationIdProvider.IsWellFormed(resolved).Should().BeTrue();
    }

    [Fact]
    public void Resolve_WithNull_GeneratesSubstitute()
    {
        CorrelationIdProvider.Resolve(null).Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public void Resolve_DoesNotEchoValueThatDiffersOnlyByLength()
    {
        string tooLong = new string('a', 200);

        CorrelationIdProvider.Resolve(tooLong).Should().NotBe(tooLong);
        CorrelationIdProvider.Resolve(tooLong).Length.Should().Be(32);
    }

    [Fact]
    public void GeneratedValuesAreAlwaysAcceptedByTheValidator()
    {
        // Guards against the generator and the validator drifting apart.
        for (int i = 0; i < 50; i++)
        {
            string id = CorrelationIdProvider.Create();

            CorrelationIdProvider.IsWellFormed(id).Should().BeTrue();
            Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,64}$").Should().BeTrue();
        }
    }
}
