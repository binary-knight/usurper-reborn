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
