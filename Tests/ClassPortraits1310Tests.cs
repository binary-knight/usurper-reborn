using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.10: the class preview shows a portrait for all 17 classes. xbit's four hand drawn portraits (Warrior,
/// Paladin, Magician, Assassin) stay as they were; the other 13 are converted from the class sprites by
/// tools/class-portraits/class_portraits.py into Scripts/UI/ClassPortraitArt.cs, in 256 colours, or in 16 colours
/// in a BBS session. Screen reader mode and the art off setting show none.
/// </summary>
[Collection("SharedGameSingletons")]
public class ClassPortraits1310Tests
{
    private static readonly Regex Csi = new("\u001b\\[[0-9;]*[A-Za-z]");
    private const string Reset = "\u001b[0m";

    private static readonly CharacterClass[] Xbit =
        { CharacterClass.Warrior, CharacterClass.Paladin, CharacterClass.Magician, CharacterClass.Assassin };

    private static IEnumerable<CharacterClass> AllClasses() => Enum.GetValues<CharacterClass>();
    private static IEnumerable<CharacterClass> Converted() => AllClasses().Where(c => !Xbit.Contains(c));

    private static int Visible(string row) => Csi.Replace(row, "").Length;

    // ---------------------------------------------------------------------------------------------
    // The portraits themselves
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void EveryClass_HasA38By18Portrait_InBothSets()
    {
        AllClasses().Count().Should().Be(17);
        foreach (var sixteen in new[] { false, true })
            foreach (var c in AllClasses())
            {
                var rows = RacePortraits.GetCroppedClassPortrait(c, 38, sixteen);
                rows.Should().NotBeNull($"{c} has a portrait (sixteen {sixteen})");
                rows!.Should().HaveCount(18, $"{c} has 18 rows");
                foreach (var r in rows)
                    Visible(r).Should().Be(38, $"each {c} row is 38 columns (sixteen {sixteen})");
            }
    }

    [Fact]
    public void ConvertedData_IsExactly38By18_WithAResetAtBothEnds()
    {
        foreach (var set in new[] { ClassPortraitArt.Xterm256, ClassPortraitArt.Ansi16 })
        {
            set.Keys.Should().BeEquivalentTo(Converted(), "the 13 classes without a hand drawn portrait, no more");
            foreach (var (c, rows) in set)
            {
                rows.Should().HaveCount(18);
                foreach (var r in rows)
                {
                    Visible(r).Should().Be(38, $"{c}");
                    r.Should().StartWith(Reset).And.EndWith(Reset, $"{c} rows reset at both ends");
                }
            }
        }
    }

    [Fact]
    public void EveryPortraitRow_EndsWithAColourReset()
    {
        foreach (var sixteen in new[] { false, true })
            foreach (var c in AllClasses())
                foreach (var r in RacePortraits.GetCroppedClassPortrait(c, 38, sixteen)!)
                    r.Should().EndWith(Reset, $"no {c} colour may smear into the frame or the next line");
    }

    [Fact]
    public void SixteenColourSet_HasNo256ColourSequence_And256SetHasThem()
    {
        foreach (var c in AllClasses())
        {
            var text = string.Join("\n", RacePortraits.GetCroppedClassPortrait(c, 38, sixteenColours: true)!);
            text.Should().NotContain("38;5;").And.NotContain("48;5;", $"{c} in a BBS session uses 16 colours only");
            foreach (Match m in Csi.Matches(text))
                m.Value.Should().MatchRegex("^\u001b\\[[0-9;]*m$", "only colour sequences");
            foreach (Match m in Regex.Matches(text, "\u001b\\[([0-9;]*)m"))
                foreach (var p in m.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse))
                    (p == 0 || p == 1 || (p >= 30 && p <= 37) || (p >= 40 && p <= 47))
                        .Should().BeTrue($"{c} 16 colour set uses SGR 0, 1, 30 to 37 and 40 to 47 only, not {p}");
        }
        foreach (var c in Converted())
            string.Join("\n", RacePortraits.GetCroppedClassPortrait(c, 38)!).Should().Contain("38;5;", $"{c} is in 256 colours");
    }

    // origin/main 56ec9d9e: sha256 of each "XPortrait = new string[] { ... };" block of RacePortraits.cs
    private static readonly Dictionary<string, string> XbitHashes = new()
    {
        ["Warrior"] = "8e971bea973a95bfff438983da86972efe37c6621185db440d6e3d995871171f",
        ["Paladin"] = "72f0864d00465ecff62d34f985ef472c3de4676a029b4acb44011755b8afd8f3",
        ["Magician"] = "575207a9ee50c3c4faad8d9b1f7f430811f5fb04fa469d37d5516ccc208cd253",
        ["Assassin"] = "9024745fd6734dfec67e1fe97649eacfc917bf50a0cea664120073962639de7e",
    };

    [Fact]
    public void XbitsFour_AreByteIdenticalToMain_AndIgnoreTheColourSet()
    {
        var src = File.ReadAllText(Path.Combine(FloorAndStun1113Tests.RepoRoot(), "Scripts/UI/RacePortraits.cs")).Replace("\r\n", "\n");
        foreach (var (name, hash) in XbitHashes)
        {
            var m = Regex.Match(src, name + @"Portrait = new string\[\]\s*\{.*?\};", RegexOptions.Singleline);
            m.Success.Should().BeTrue(name);
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(m.Value))).ToLowerInvariant()
                .Should().Be(hash, $"xbit's {name} portrait stays exactly as it is");
        }
        foreach (var c in Xbit)
            RacePortraits.GetCroppedClassPortrait(c, 38, sixteenColours: true)
                .Should().Equal(RacePortraits.GetCroppedClassPortrait(c, 38), $"{c} is the same drawing in every session");
    }

    // ---------------------------------------------------------------------------------------------
    // Which set a session gets
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, "Local", true)]
    [InlineData(false, "BBS", true)]
    [InlineData(false, null, false)]
    [InlineData(false, "Web", false)]
    [InlineData(false, "SSH", false)]
    [InlineData(false, "MUD", false)]
    [InlineData(false, "Steam", false)]
    [InlineData(false, "Local", false)]
    public void SixteenColours_InADoorOrABbsSession_Only(bool door, string? connectionType, bool expected)
    {
        CharacterCreationSystem.ClassPortraitSixteenColours(door, connectionType).Should().Be(expected);
    }

    [Fact]
    public void ThePreview_PassesTheDoorFlagAndTheConnectionType()
    {
        var src = File.ReadAllText(Path.Combine(FloorAndStun1113Tests.RepoRoot(), "Scripts/Systems/CharacterCreationSystem.cs"));
        Regex.IsMatch(src, @"RacePortraits\.GetCroppedClassPortrait\(characterClass, 38,\s*ClassPortraitSixteenColours\(UsurperRemake\.BBS\.DoorMode\.IsInDoorMode, UsurperRemake\.Server\.SessionContext\.Current\?\.ConnectionType\)\)")
            .Should().BeTrue("a door session reads DoorMode, a door player on the online server reads the connection type");
    }

    // ---------------------------------------------------------------------------------------------
    // The preview screen as a session receives it
    // ---------------------------------------------------------------------------------------------

    private static string Preview(CharacterClass c, string connectionType, string lang = "en", bool screenReader = false, bool artOff = false)
    {
        var saved = SessionContext.Current;
        var output = new MemoryStream();
        var ctx = new SessionContext { Username = "portrait", CharacterKey = "portrait", OutputStream = output, ConnectionType = connectionType };
        try
        {
            SessionContext.Current = ctx;
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = screenReader;
            ctx.DisableCharacterMonsterArt = artOff;
            var term = new TerminalEmulator(new LineStream(new[] { "N" }), output);
            var ccs = new CharacterCreationSystem(term);
            var mi = typeof(CharacterCreationSystem).GetMethod("ShowClassPreview", BindingFlags.NonPublic | BindingFlags.Instance)!;
            ((Task<bool>)mi.Invoke(ccs, new object[] { c, CharacterRace.Human })!).GetAwaiter().GetResult();
            term.StreamWriterInternal?.Flush();
            return Encoding.UTF8.GetString(output.ToArray());
        }
        finally
        {
            GameConfig.ScreenReaderMode = false;   // still inside the session: clears only this flow's value
            SessionContext.Current = saved;
        }
    }

    [Fact]
    public void AWebSession_Gets256Colours_ABbsSession16_ForEveryConvertedClass()
    {
        foreach (var c in Converted())
        {
            var web = Preview(c, "Web");
            web.Should().Contain("\u001b[38;5;", $"{c} on the web is drawn in 256 colours");
            web.Should().Contain(ClassPortraitArt.Xterm256[c][9], $"{c} shows its own portrait");
            var bbs = Preview(c, "BBS");
            bbs.Should().NotContain("38;5;").And.NotContain("48;5;", $"{c} for a BBS door player uses 16 colours");
            bbs.Should().Contain(ClassPortraitArt.Ansi16[c][9], $"{c} shows its 16 colour portrait");
        }
    }

    [Fact]
    public void XbitsFour_ShowTheirOwnPortrait_InEverySession()
    {
        foreach (var c in Xbit)
        {
            var row = RacePortraits.GetCroppedClassPortrait(c, 38)![9];
            Preview(c, "Web").Should().Contain(row);
            Preview(c, "BBS").Should().Contain(row);
        }
    }

    [Fact]
    public void ScreenReaderAndArtOff_ShowNoPortrait()
    {
        foreach (var c in AllClasses())
            foreach (var (sr, off) in new[] { (true, false), (false, true) })
            {
                var text = Preview(c, "Web", screenReader: sr, artOff: off);
                text.Should().NotContain("38;5;", $"{c} screen reader {sr} art off {off}");
                foreach (var row in RacePortraits.GetCroppedClassPortrait(c, 38)!.Concat(RacePortraits.GetCroppedClassPortrait(c, 38, true)!)
                             .Where(r => Csi.Replace(r, "").Trim().Length > 0))
                    text.Should().NotContain(row.Substring(0, row.Length - Reset.Length), $"{c} shows no portrait (screen reader {sr} art off {off})");
            }
    }

    [Fact]
    public void TheSideBySidePreview_Fits79Columns_ForEveryClass_InEveryLanguage()
    {
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
            foreach (var c in AllClasses())
                foreach (var conn in new[] { "Web", "BBS" })
                {
                    var text = Preview(c, conn, lang);
                    var lines = Csi.Replace(text, "").Split('\n').Select(l => l.TrimEnd('\r')).ToList();
                    lines.Max(l => l.Length).Should().BeLessThanOrEqualTo(79, $"{c} {lang} {conn}");
                    lines.Count(l => l.StartsWith("║") && l.Length == 79 && l.IndexOf('║', 1) == 39)
                        .Should().BeGreaterThanOrEqualTo(18, $"{c} {lang} {conn} shows the 18 portrait rows beside the stats");
                }
    }
}
