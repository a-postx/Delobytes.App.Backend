using Delobytes.App.Backend.Services;
using FluentAssertions;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// Tests for <see cref="CorrelationIdProvider"/>.
/// </summary>
public class CorrelationIdProviderTests
{
    [Fact]
    public void Create_ReturnsNonEmptyGuid()
    {
        Guid id = CorrelationIdProvider.Create();

        id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Create_CalledRepeatedly_ReturnsDistinctValues()
    {
        HashSet<Guid> ids = new HashSet<Guid>();

        for (int i = 0; i < 100; i++)
        {
            ids.Add(CorrelationIdProvider.Create());
        }

        ids.Should().HaveCount(100);
    }

    [Fact]
    public void Resolve_Guid_WithValue_ReturnsThatValue()
    {
        Guid existing = Guid.NewGuid();

        CorrelationIdProvider.Resolve((Guid?)existing).Should().Be(existing);
    }

    [Fact]
    public void Resolve_Guid_WithNull_GeneratesSubstitute()
    {
        Guid resolved = CorrelationIdProvider.Resolve((Guid?)null);

        resolved.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Resolve_Guid_WithEmptyGuid_GeneratesSubstitute()
    {
        // Guid.Empty cannot have been produced by Create(), so treating it the same as "absent"
        // protects against an uninitialised default(Guid) being mistaken for a real identifier.
        Guid resolved = CorrelationIdProvider.Resolve((Guid?)Guid.Empty);

        resolved.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Resolve_String_WithWellFormedGuid_ParsesIt()
    {
        Guid source = Guid.NewGuid();

        CorrelationIdProvider.Resolve(source.ToString()).Should().Be(source);
    }

    [Fact]
    public void Resolve_String_AcceptsGuidWithoutDashes()
    {
        Guid source = Guid.NewGuid();

        CorrelationIdProvider.Resolve(source.ToString("N")).Should().Be(source);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a valid id")]
    [InlineData("client-supplied-id")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Resolve_String_WithUnparsableOrEmptyValue_GeneratesSubstitute(string? value)
    {
        Guid resolved = CorrelationIdProvider.Resolve(value);

        resolved.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void GeneratedValuesAreAlwaysAcceptedByResolve()
    {
        // Guards against the generator and the resolver drifting apart.
        for (int i = 0; i < 50; i++)
        {
            Guid id = CorrelationIdProvider.Create();

            CorrelationIdProvider.Resolve(id.ToString()).Should().Be(id);
            CorrelationIdProvider.Resolve((Guid?)id).Should().Be(id);
        }
    }
}
