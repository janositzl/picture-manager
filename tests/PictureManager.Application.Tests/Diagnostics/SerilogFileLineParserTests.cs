using FluentAssertions;
using PictureManager.Application.Diagnostics;

namespace PictureManager.Application.Tests.Diagnostics;

public class SerilogFileLineParserTests
{
    [Theory]
    [InlineData("VRB", LogSeverity.Verbose)]
    [InlineData("DBG", LogSeverity.Debug)]
    [InlineData("INF", LogSeverity.Information)]
    [InlineData("WRN", LogSeverity.Warning)]
    [InlineData("ERR", LogSeverity.Error)]
    [InlineData("FTL", LogSeverity.Fatal)]
    public void Parse_MapsEveryLevelCode(string code, LogSeverity expected)
    {
        var entries = SerilogFileLineParser.Parse([$"2026-10-14 10:32:01.123 +02:00 [{code}] Hello"]);

        entries.Should().ContainSingle();
        entries[0].Level.Should().Be(expected);
        entries[0].Timestamp.Should().Be("2026-10-14 10:32:01.123 +02:00");
        entries[0].Message.Should().Be("Hello");
        entries[0].Exception.Should().BeNull();
    }

    [Fact]
    public void Parse_AttachesContinuationLinesToThePreviousEntry()
    {
        var entries = SerilogFileLineParser.Parse(
        [
            "2026-10-14 10:00:00.000 +00:00 [INF] first",
            "2026-10-14 10:00:01.000 +00:00 [ERR] boom",
            "System.InvalidOperationException: bad",
            "   at Foo.Bar()",
            "2026-10-14 10:00:02.000 +00:00 [INF] last"
        ]);

        entries.Should().HaveCount(3);
        entries[1].Message.Should().Be("boom");
        entries[1].Exception.Should().Be("System.InvalidOperationException: bad\n   at Foo.Bar()");
        entries[2].Exception.Should().BeNull();
    }

    [Fact]
    public void Parse_LeadingOrphanLine_BecomesEntryWithoutLevel()
    {
        var entries = SerilogFileLineParser.Parse(
        [
            "   at Foo.Bar()",
            "2026-10-14 10:00:00.000 +00:00 [WRN] next"
        ]);

        entries.Should().HaveCount(2);
        entries[0].Level.Should().BeNull();
        entries[0].Message.Should().Be("   at Foo.Bar()");
        entries[1].Level.Should().Be(LogSeverity.Warning);
    }

    [Fact]
    public void LogEntry_SerializesTheLevelAsAString()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new LogEntry("t", LogSeverity.Warning, "m", null),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        json.Should().Contain("\"level\":\"Warning\"");
    }

    [Fact]
    public void Parse_NoLines_ReturnsEmpty() =>
        SerilogFileLineParser.Parse([]).Should().BeEmpty();
}
