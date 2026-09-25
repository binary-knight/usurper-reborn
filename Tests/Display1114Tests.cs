using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.14: status sheet separators, the dungeon danger label, room text wrapping.</summary>
[Collection("SharedGameSingletons")]
public class StatusSheetSeparators1114Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    internal static string Plain(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    /// <summary>Writes a render to USURPER_EVIDENCE_DIR when set, for a by-eye check.</summary>
    internal static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    private static Character Hero() => new Character
    {
        Name1 = "Aldric", Name2 = "Aldric", Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        Sex = CharacterSex.Male, Age = 20, Height = 184, Weight = 102, Level = 12, HP = 390, MaxHP = 390,
        AI = CharacterAI.Human,
    };

    private static async Task<string> RenderStatus()
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 20)), output);
        var loc = new MainStreetLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, Hero());
        await (Task)typeof(BaseLocation).GetMethod("ShowStatus", F)!.Invoke(loc, Array.Empty<object>())!;
        return Plain(term, output);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task StatusSheet_HasNoDoubledSeparator(string lang)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var text = await RenderStatus();
            text.Should().Contain(Loc.Get("base.stat_class").TrimEnd() + " " + Hero().ClassName);
            text.Should().NotContain("|    |");
            text.Should().NotMatchRegex(@"\|\s+\|");
            foreach (var row in text.Split('\n').Where(l => l.Contains(" | ")))
                row.Should().NotMatchRegex(@":  +\S", "one space after each label on a separated row");
        }
        finally { GameConfig.Language = prev; }
    }

    [Fact]
    public async Task StatusSheet_English_ReadsAsIntended()
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = "en";
            var text = await RenderStatus();
            Capture("status-en.txt", text);
            text.Should().Contain("Class: Warrior  |  Race: Human  |  Sex: Male");
            text.Should().Contain("Age: 20  |  Height: 184cm  |  Weight: 102kg");
        }
        finally { GameConfig.Language = prev; }
    }
}

/// <summary>v1.1.14: the dungeon room's danger line prints its label once.</summary>
[Collection("SharedGameSingletons")]
public class DungeonDangerLabel1114Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    internal static (DungeonLocation d, TerminalEmulator term, MemoryStream output) Rig(DungeonRoom room)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Array.Empty<string>()), output);
        var hero = new Character { Name1 = "dng", Name2 = "Dng", Class = CharacterClass.Warrior, Level = 12, HP = 390, MaxHP = 390, AI = CharacterAI.Human };
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, 6);
        var floor = new DungeonFloor { Level = 6, Theme = DungeonTheme.Catacombs, CurrentRoomId = room.Id };
        floor.Rooms.Add(room);
        typeof(DungeonLocation).GetField("currentFloor", F)!.SetValue(d, floor);
        return (d, term, output);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void DangerLine_LabelOnce_NoDoubleColon(string lang)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = false;
            var room = new DungeonRoom { Id = "r1", Name = "Hall of the Ancestors", DangerRating = 1, HasMonsters = true };
            var (d, term, output) = Rig(room);
            typeof(DungeonLocation).GetMethod("ShowDangerIndicators", F)!.Invoke(d, new object[] { room });
            var text = StatusSheetSeparators1114Tests.Plain(term, output);
            StatusSheetSeparators1114Tests.Capture($"danger-{lang}.txt", text);
            text.Should().NotContain(": :");
            text.Should().NotMatchRegex(@":\s*:");
            text.Should().Contain(Loc.Get("dungeon.bbs_danger") + "*..");
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; }
    }
}

/// <summary>v1.1.14: room descriptions and ambient lines wrap at word boundaries.</summary>
[Collection("SharedGameSingletons")]
public class RoomTextWrap1114Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static string Desc => Loc.GetIn("en", "dg.cat.hall.d");
    private static string Ambient => Loc.GetIn("en", "dg.cat.hall.a");

    [Theory]
    [InlineData(79)]
    [InlineData(40)]
    [InlineData(20)]
    public void WordWrap_BreaksOnlyAtSpaces_AndFitsTheWidth(int width)
    {
        var lines = UsurperRemake.UI.UIHelper.WordWrap(Desc, width);
        lines.Count.Should().BeGreaterThan(1);
        lines.Should().OnlyContain(l => l.Length <= width);
        lines.Should().OnlyContain(l => l.Length > 0 && l == l.Trim());
        string.Join(" ", lines).Should().Be(Desc, "joining at spaces gives the original text back");
    }

    [Fact]
    public void WordWrap_KeepsAnsiIntact_AndDoesNotCountIt()
    {
        string red = "\u001b[31m", reset = "\u001b[0m";
        string text = $"{red}Candles{reset} that should have {red}burned out{reset} centuries ago still flicker with pale blue flame.";
        var lines = UsurperRemake.UI.UIHelper.WordWrap(text, 30);
        string.Join(" ", lines).Should().Be(text);
        lines.Should().OnlyContain(l => UsurperRemake.UI.UIHelper.VisibleLength(l) <= 30);
        UsurperRemake.UI.UIHelper.WordWrap($"{red}{new string('x', 29)}{reset} y", 30).Should().HaveCount(2, "escapes are not counted, so the first word fits");
    }

    [Fact]
    public void WordWrap_OffsetAndNewlines()
    {
        UsurperRemake.UI.UIHelper.WordWrap("aaa bbb ccc", 10, 4).Should().Equal("aaa", "bbb ccc");
        UsurperRemake.UI.UIHelper.WordWrap("one\ntwo", 79).Should().Equal("one", "two");
        UsurperRemake.UI.UIHelper.WordWrap("", 79).Should().Equal("");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoomView_DescriptionAndAmbient_WrapAtWords(bool screenReader)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = "en";
            GameConfig.ScreenReaderMode = screenReader;
            var room = new DungeonRoom { Id = "r1", Name = "Hall of the Ancestors", Description = Desc, AtmosphereText = Ambient, DangerRating = 1, HasMonsters = true };
            var (d, term, output) = DungeonDangerLabel1114Tests.Rig(room);
            typeof(DungeonLocation).GetMethod("DisplayRoomView", F)!.Invoke(d, new object[] { room });
            var text = StatusSheetSeparators1114Tests.Plain(term, output);
            StatusSheetSeparators1114Tests.Capture($"room-{(screenReader ? "sr" : "tty")}.txt", text);
            var lines = text.Replace("\r", "").Split('\n');
            lines.Should().OnlyContain(l => l.Length <= 80, "no line may exceed the terminal width");
            lines.Should().Contain(l => l.EndsWith("their"), "the 80-column break falls inside \"empty\"; the wrap moves it whole");
            var tokens = text.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            foreach (var word in (Desc + " " + Ambient).Split(' '))
                tokens.Should().Contain(word, "each word of the description is printed whole");
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; }
    }
}
