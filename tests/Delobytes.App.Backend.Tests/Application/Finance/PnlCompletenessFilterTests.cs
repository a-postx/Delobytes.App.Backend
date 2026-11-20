using Delobytes.App.Backend.Finance.Application.Queries;
using Delobytes.App.Backend.Finance.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// Tests for the completeness selection: the default must keep recorded exceptions visible while
/// excluding provisional figures, and <c>all</c> must remove the restriction instead of silently
/// narrowing to one state.
/// </summary>
public class PnlCompletenessFilterTests
{
    [Fact]
    public void Parse_MissingValue_SelectsTheFinalStates()
    {
        IReadOnlyList<PnlLineCompleteness>? states = PnlCompletenessFilter.Parse(null);

        states.Should().BeEquivalentTo(
            new[] { PnlLineCompleteness.Complete, PnlLineCompleteness.ClosedWithException });
    }

    [Fact]
    public void Parse_All_RemovesTheRestriction()
    {
        IReadOnlyList<PnlLineCompleteness>? states = PnlCompletenessFilter.Parse("all");

        states.Should().BeNull();
    }

    [Fact]
    public void Parse_Provisional_SelectsTheOpenStates()
    {
        IReadOnlyList<PnlLineCompleteness>? states = PnlCompletenessFilter.Parse("provisional");

        states.Should().BeEquivalentTo(
            new[] { PnlLineCompleteness.Provisional, PnlLineCompleteness.Overdue });
    }

    [Fact]
    public void Parse_Final_SelectsTheNonProvisionalStates()
    {
        IReadOnlyList<PnlLineCompleteness>? states = PnlCompletenessFilter.Parse("final");

        states.Should().BeEquivalentTo(
            new[] { PnlLineCompleteness.Complete, PnlLineCompleteness.ClosedWithException });
    }

    [Theory]
    [InlineData("complete", PnlLineCompleteness.Complete)]
    [InlineData("overdue", PnlLineCompleteness.Overdue)]
    [InlineData("closedWithException", PnlLineCompleteness.ClosedWithException)]
    public void Parse_ExactState_SelectsOnlyThatState(string raw, PnlLineCompleteness expected)
    {
        IReadOnlyList<PnlLineCompleteness>? states = PnlCompletenessFilter.Parse(raw);

        states.Should().ContainSingle().Which.Should().Be(expected);
    }

    [Fact]
    public void Parse_IsCaseInsensitive()
    {
        IReadOnlyList<PnlLineCompleteness>? states = PnlCompletenessFilter.Parse("  CLOSEDWITHEXCEPTION  ");

        states.Should().ContainSingle().Which.Should().Be(PnlLineCompleteness.ClosedWithException);
    }

    [Fact]
    public void TryParse_UnknownValue_ReturnsFalseInsteadOfNarrowing()
    {
        bool parsed = PnlCompletenessFilter.TryParse("everything", out IReadOnlyList<PnlLineCompleteness>? states);

        parsed.Should().BeFalse();
        states.Should().BeNull();
    }

    [Fact]
    public void Parse_UnknownValue_Throws()
    {
        Action act = () => PnlCompletenessFilter.Parse("everything");

        act.Should().Throw<ArgumentException>();
    }
}
