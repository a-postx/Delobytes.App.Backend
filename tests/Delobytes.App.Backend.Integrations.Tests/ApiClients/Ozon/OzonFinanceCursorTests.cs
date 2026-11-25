using System;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients.Ozon;

/// <summary>
/// Tests the finance walk position. A cursor that loses its day makes a multi-day window import only
/// its first day while the orchestrator marks the whole window read, so the format is a correctness
/// boundary rather than a convenience.
/// </summary>
public class OzonFinanceCursorTests
{
    private static readonly DateOnly WindowFrom = new DateOnly(2026, 11, 16);
    private static readonly DateOnly SecondDay = new DateOnly(2026, 11, 17);

    [Fact]
    public void Parse_EmptyCursor_StartsAtTheWindow()
    {
        OzonFinanceCursor position = OzonFinanceCursor.Parse(null, WindowFrom);

        position.Day.Should().Be(WindowFrom);
        position.LastId.Should().BeEmpty();
    }

    [Fact]
    public void Parse_DatedCursor_ReturnsItsDayAndPageId()
    {
        OzonFinanceCursor position = OzonFinanceCursor.Parse("2026-11-17|next-page", WindowFrom);

        position.Day.Should().Be(SecondDay);
        position.LastId.Should().Be("next-page");
    }

    [Fact]
    public void Parse_BarePageId_IsReadAsTheFirstDayOfTheWindow()
    {
        OzonFinanceCursor position = OzonFinanceCursor.Parse("bare-page-id", WindowFrom);

        position.Day.Should().Be(WindowFrom);
        position.LastId.Should().Be("bare-page-id");
    }

    [Fact]
    public void Parse_DayBeforeTheWindow_IsClampedToTheWindowStart()
    {
        OzonFinanceCursor position = OzonFinanceCursor.Parse("2026-11-15|page", WindowFrom);

        position.Day.Should().Be(WindowFrom);
        position.LastId.Should().Be("page");
    }

    [Fact]
    public void ToString_EmptyPageId_KeepsTheDayAndTheSeparator()
    {
        OzonFinanceCursor position = new OzonFinanceCursor(SecondDay, string.Empty);

        position.ToString().Should().Be("2026-11-17|");
    }

    [Fact]
    public void ToString_RoundTripsThroughParse()
    {
        OzonFinanceCursor position = new OzonFinanceCursor(SecondDay, "page-2");

        OzonFinanceCursor parsed = OzonFinanceCursor.Parse(position.ToString(), WindowFrom);

        parsed.Day.Should().Be(SecondDay);
        parsed.LastId.Should().Be("page-2");
    }
}
