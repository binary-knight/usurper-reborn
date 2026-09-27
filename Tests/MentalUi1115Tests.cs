using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15 Mental piece 3 of 7: the status sheet line and afflictions line, the dungeon/combat
/// band tags (shown in both modes, unlike the fatigue tag they sit next to), the band-change
/// announcement and first-time hint helper (MentalUi.AnnounceMentalChange, not yet called from
/// anywhere), and the GMCP mental/maxMental fields on Char.Vitals.
/// </summary>
[Collection("SharedGameSingletons")]
public class MentalUi1115Tests
{
    private static Character Hero(int mental = 100, int addict = 0, bool screenReader = false) => new Character
    {
        Name1 = "hero", Name2 = "Hero", AI = CharacterAI.Human,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        Mental = mental, Addict = addict, ScreenReaderMode = screenReader,
        Level = 10, HP = 100, MaxHP = 100,
    };

    // ─── Character.GetMentalTier ───

    [Theory]
    [InlineData(100, "status.mental_stable", "bright_green")]
    [InlineData(75, "status.mental_stable", "bright_green")]
    [InlineData(74, "status.mental_strained", "yellow")]
    [InlineData(50, "status.mental_strained", "yellow")]
    [InlineData(49, "status.mental_shaken", "bright_yellow")]
    [InlineData(25, "status.mental_shaken", "bright_yellow")]
    [InlineData(24, "status.mental_breaking", "red")]
    [InlineData(1, "status.mental_breaking", "red")]
    [InlineData(0, "status.mental_broken", "bright_red")]
    public void GetMentalTier_MapsEveryBandToItsLocLabelAndColor(int mental, string key, string color)
    {
        var (label, actualColor) = Hero(mental).GetMentalTier();
        label.Should().Be(Loc.Get(key));
        actualColor.Should().Be(color);
    }

    // ─── MentalUi.GetMentalTag ───

    [Fact]
    public void GetMentalTag_IsEmptyAtStable_ButMatchesTheTierBelowIt()
    {
        MentalUi.GetMentalTag(Hero(100)).label.Should().BeEmpty();
        MentalUi.GetMentalTag(Hero(75)).label.Should().BeEmpty();
        MentalUi.GetMentalTag(Hero(60)).Should().Be(Hero(60).GetMentalTier());
        MentalUi.GetMentalTag(Hero(10)).Should().Be(Hero(10).GetMentalTier());
    }

    [Fact]
    public void GetMentalTag_DoesNotDependOnOnlineMode()
    {
        // Mental runs in both modes; the tag helper must not itself branch on IsOnlineMode the
        // way GetFatigueTier's callers do. Toggle the flag around the call and confirm no change.
        var f = typeof(UsurperRemake.BBS.DoorMode).GetField("_onlineMode", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool was = (bool)f.GetValue(null)!;
        try
        {
            f.SetValue(null, false);
            var offline = MentalUi.GetMentalTag(Hero(40));
            f.SetValue(null, true);
            var online = MentalUi.GetMentalTag(Hero(40));
            online.Should().Be(offline);
            online.label.Should().NotBeEmpty();
        }
        finally { f.SetValue(null, was); }
    }

    [Fact]
    public void GetMentalTag_NullPlayer_ReturnsEmpty()
    {
        MentalUi.GetMentalTag(null).Should().Be(("", ""));
    }

    // ─── MentalUi.AnnounceMentalChange ───

    private static (TerminalEmulator term, MemoryStream output) NewTerminal()
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 20)), output);
        return (term, output);
    }

    private static string Rendered(MemoryStream output, TerminalEmulator term)
    {
        term.StreamWriterInternal!.Flush();
        return Encoding.UTF8.GetString(output.ToArray());
    }

    [Fact]
    public void AnnounceMentalChange_NoBandChange_PrintsNothing()
    {
        var (term, output) = NewTerminal();
        var hero = Hero(90); // Stable before and after
        MentalUi.AnnounceMentalChange(term, hero, 95);
        Rendered(output, term).Should().BeEmpty();
    }

    [Fact]
    public void AnnounceMentalChange_WorseBand_PrintsTheWorseLine()
    {
        var (term, output) = NewTerminal();
        var hero = Hero(60); // Strained now, was Stable
        MentalUi.AnnounceMentalChange(term, hero, 80);
        Rendered(output, term).Should().Contain(Loc.Get("mental.band_worse", Loc.Get("status.mental_strained")));
    }

    [Fact]
    public void AnnounceMentalChange_BetterBand_PrintsTheBetterLine()
    {
        var (term, output) = NewTerminal();
        var hero = Hero(80); // Stable now, was Strained
        MentalUi.AnnounceMentalChange(term, hero, 60);
        Rendered(output, term).Should().Contain(Loc.Get("mental.band_better", Loc.Get("status.mental_stable")));
    }

    [Fact]
    public void AnnounceMentalChange_FirstDropBelowStable_ShowsTheHintOnceAndSetsTheFlag()
    {
        var (term, output) = NewTerminal();
        var hero = Hero(60);
        hero.MentalHintShown.Should().BeFalse();
        MentalUi.AnnounceMentalChange(term, hero, 80);
        Rendered(output, term).Should().Contain(Loc.Get("mental.first_hint"));
        hero.MentalHintShown.Should().BeTrue();

        // Second drop, still below Stable: the hint does not repeat.
        var (term2, output2) = NewTerminal();
        hero.Mental = 40;
        MentalUi.AnnounceMentalChange(term2, hero, 60);
        Rendered(output2, term2).Should().NotContain(Loc.Get("mental.first_hint"));
    }

    [Fact]
    public void AnnounceMentalChange_SkipsNpcs()
    {
        var (term, output) = NewTerminal();
        var npc = Hero(60);
        npc.AI = CharacterAI.Computer;
        MentalUi.AnnounceMentalChange(term, npc, 80);
        Rendered(output, term).Should().BeEmpty();
        npc.MentalHintShown.Should().BeFalse();
    }

    // ─── Status sheet rendering (BaseLocation.ShowStatus) ───

    private static string RenderShowStatus(Character hero)
    {
        var loc = new InnLocation();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 20)), output);
        const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, hero);
        ((System.Threading.Tasks.Task)typeof(BaseLocation).GetMethod("ShowStatus", F)!.Invoke(loc, null)!).GetAwaiter().GetResult();
        term.StreamWriterInternal!.Flush();
        return Encoding.UTF8.GetString(output.ToArray());
    }

    [Fact]
    public void StatusSheet_ShowsTheMentalLine_WithBandAndDenominator()
    {
        var shown = RenderShowStatus(Hero(62));
        shown.Should().Contain($"62/{GameConfig.MaxMentalStability}");
        shown.Should().Contain(Loc.Get("status.mental_strained"));
    }

    [Fact]
    public void StatusSheet_AppendsACapSuffix_WhenAddictionLowersTheCap()
    {
        var hero = Hero(mental: 40, addict: 40); // cap = 100 - 40/2 = 80
        int cap = MentalSystem.GetCap(hero);
        cap.Should().Be(80);
        var shown = RenderShowStatus(hero);
        shown.Should().Contain(Loc.Get("status.mental_cap_suffix", cap));
    }

    [Fact]
    public void StatusSheet_OmitsTheCapSuffix_WhenNotCapped()
    {
        var hero = Hero(mental: 90, addict: 0);
        var shown = RenderShowStatus(hero);
        shown.Should().NotContain(Loc.Get("status.mental_cap_suffix", 100));
    }

    [Theory]
    [InlineData(90, 0)]   // Stable: no afflictions line
    [InlineData(60, 0)]   // Strained: no afflictions line
    [InlineData(40, 5)]   // Shaken
    [InlineData(10, 10)]  // Breaking
    [InlineData(0, 10)]   // Broken
    public void StatusSheet_AfflictionsLine_MatchesGetCombatPenaltyExactly(int mental, int expectedPct)
    {
        var shown = RenderShowStatus(Hero(mental));
        float penalty = MentalSystem.GetCombatPenalty(mental);
        ((int)Math.Round(penalty * 100)).Should().Be(expectedPct, "the test table must track MentalSystem.GetCombatPenalty");
        if (expectedPct > 0)
            shown.Should().Contain(Loc.Get("base.mental_afflictions_pct", expectedPct));
        else
            shown.Should().NotContain(Loc.Get("base.mental_afflictions_label"));
    }

    [Fact]
    public void StatusSheet_ReadsBandFirst_UnderScreenReaderMode()
    {
        var hero = Hero(mental: 62, screenReader: true);
        var shown = RenderShowStatus(hero);
        shown.Should().Contain(Loc.Get("status.mental_sr_line", Loc.Get("status.mental_strained"), 62, GameConfig.MaxMentalStability));
    }

    [Fact]
    public void StatusSheet_RendersTheMentalLine_InOnlineMode_UnlikeFatigue()
    {
        var f = typeof(UsurperRemake.BBS.DoorMode).GetField("_onlineMode", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool was = (bool)f.GetValue(null)!;
        try
        {
            f.SetValue(null, true);
            var hero = Hero(40); // Shaken: expect both the Mental line and the afflictions line
            var shown = RenderShowStatus(hero);
            shown.Should().Contain(Loc.Get("status.mental_shaken"));
            shown.Should().Contain(Loc.Get("base.mental_afflictions_pct", 5));
        }
        finally { f.SetValue(null, was); }
    }

    // ─── GMCP Char.Vitals: mental / maxMental ───

    [Fact]
    public void EmitVitalsIfChanged_IncludesMentalAndMaxMental_AndMentalAloneTriggersAnEmit()
    {
        var saved = SessionContext.Current;
        var stream = new MemoryStream();
        SessionContext.Current = new SessionContext { OutputStream = stream, GmcpEnabled = true };
        try
        {
            var hero = Hero(100);
            hero.HP = 50; hero.MaxHP = 50; hero.Mana = 0; hero.MaxMana = 0; hero.Stamina = 10;
            GmcpBridge.EmitVitalsIfChanged(hero); // baseline emit
            stream.SetLength(0);

            hero.Mental = 62; // only Mental changes
            GmcpBridge.EmitVitalsIfChanged(hero);
            string text = Encoding.UTF8.GetString(stream.ToArray());
            text.Should().Contain("\"mental\":62");
            text.Should().Contain($"\"maxMental\":{GameConfig.MaxMentalStability}");

            stream.SetLength(0);
            GmcpBridge.EmitVitalsIfChanged(hero); // nothing changed now
            stream.Length.Should().Be(0, "Mental unchanged and every other tracked stat unchanged should not re-emit");
        }
        finally { SessionContext.Current = saved; }
    }

    // ─── Loc presence: every new key exists and is non-empty in all five languages ───

    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private static readonly string[] NewKeys =
    {
        "status.mental_stable",
        "status.mental_strained",
        "status.mental_shaken",
        "status.mental_breaking",
        "status.mental_broken",
        "status.mental_cap_suffix",
        "status.mental_sr_line",
        "base.mental_afflictions_label",
        "base.mental_afflictions_pct",
        "combat.mental_tag",
        "mental.band_worse",
        "mental.band_better",
        "mental.first_hint",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        if (dir == null) throw new InvalidOperationException("repo root not found above " + AppContext.BaseDirectory);
        return dir.FullName;
    }

    [Fact]
    public void EveryNewMentalUiLocKey_ExistsAndIsNonEmpty_InAllFiveLanguages()
    {
        var missing = new List<string>();
        foreach (var lang in Langs)
        {
            string path = Path.Combine(RepoRoot(), "Localization", lang + ".json");
            var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path))!;
            foreach (var key in NewKeys)
            {
                if (!doc.TryGetValue(key, out var v) || v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()))
                    missing.Add($"{lang}:{key}");
            }
        }
        missing.Should().BeEmpty($"every new Mental UI key must be present and non-empty in all 5 files, missing: {string.Join(", ", missing)}");
    }
}
