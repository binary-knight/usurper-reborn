using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 7 leftovers: Aurelion's fight moved to the Temple's Deep Temple, and
/// floor 85 now points back to it. Player-visible text that still named floor 85 for Aurelion
/// (the Main Street Old Gods list, the Inn's bartender rumor, the save-quest return line and the
/// oracle's floor list) is updated to name the Deep Temple instead, in all 5 languages.
/// </summary>
[Collection("SharedGameSingletons")]
public class TempleLeftovers1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };
    private static readonly Regex Floor85 = new(@"\b85\b");

    private static string LocDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Localization");
            if (File.Exists(Path.Combine(candidate, "en.json"))) return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Localization directory not found above " + AppContext.BaseDirectory);
    }

    private static Dictionary<string, string> LoadLang(string lang)
    {
        string path = Path.Combine(LocDir(), lang + ".json");
        var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path))!;
        return doc.Where(kv => kv.Value.ValueKind == JsonValueKind.String)
                  .ToDictionary(kv => kv.Key, kv => kv.Value.GetString() ?? "");
    }

    /// <summary>Sets Old God statuses for one test and restores them afterwards.</summary>
    private sealed class StoryScope : IDisposable
    {
        private readonly Dictionary<OldGodType, GodStatus> _statusBefore;
        private readonly Dictionary<OldGodType, bool> _encounteredBefore;
        public StoryScope(params (OldGodType God, GodStatus Status, bool Encountered)[] states)
        {
            var story = StoryProgressionSystem.Instance;
            _statusBefore = story.OldGodStates.ToDictionary(kv => kv.Key, kv => kv.Value.Status);
            _encounteredBefore = story.OldGodStates.ToDictionary(kv => kv.Key, kv => kv.Value.HasBeenEncountered);
            foreach (var god in story.OldGodStates.Keys.ToList())
            {
                story.OldGodStates[god].Status = GodStatus.Imprisoned;
                story.OldGodStates[god].HasBeenEncountered = false;
            }
            foreach (var (god, status, encountered) in states)
            {
                if (!story.OldGodStates.TryGetValue(god, out var s))
                    story.OldGodStates[god] = s = new OldGodState { Name = god.ToString(), CanBeSaved = true };
                s.Status = status;
                s.HasBeenEncountered = encountered;
            }
        }
        public void Dispose()
        {
            var story = StoryProgressionSystem.Instance;
            foreach (var god in story.OldGodStates.Keys.ToList())
            {
                if (_statusBefore.TryGetValue(god, out var st)) story.OldGodStates[god].Status = st;
                if (_encounteredBefore.TryGetValue(god, out var enc)) story.OldGodStates[god].HasBeenEncountered = enc;
            }
        }
    }

    // ---------------- Loc scan: no player-visible Aurelion text names floor 85 ----------------

    [Fact]
    public void NoLanguage_NamesFloor85_ForAurelion()
    {
        foreach (var lang in Langs)
        {
            var d = LoadLang(lang);
            var offenders = d.Where(kv => kv.Key.Contains("aurelion", StringComparison.OrdinalIgnoreCase)
                                        || kv.Value.Contains("Aurelion", StringComparison.OrdinalIgnoreCase))
                              .Where(kv => Floor85.IsMatch(kv.Value))
                              .Select(kv => kv.Key).ToList();
            offenders.Should().BeEmpty($"{lang}.json still sends players to floor 85 for Aurelion: {string.Join(", ", offenders)}");
        }
    }

    [Fact]
    public void OracleHint_NoLongerNamesFloor85()
    {
        foreach (var lang in Langs)
        {
            var d = LoadLang(lang);
            Floor85.IsMatch(d["settlement.oracle_hint_2"]).Should().BeFalse($"{lang}.json oracle hint still names floor 85");
        }
    }

    [Fact]
    public void SunforgedReturn_PointsToTheTemple_InEveryLanguage()
    {
        foreach (var lang in Langs)
        {
            var d = LoadLang(lang);
            string v = d["dungeon.sunforged_return"];
            Floor85.IsMatch(v).Should().BeFalse($"{lang}.json sunforged_return still names floor 85");
        }
    }

    // ---------------- Main Street: the Old Gods list names the Deep Temple for Aurelion ----------------

    private static (MainStreetLocation loc, TerminalEmulator term, MemoryStream output) StreetRig(Character hero, params string[] lines)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 10))), output);
        var street = new MainStreetLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
        return (street, term, output);
    }

    private static string Text(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
    }

    [Fact]
    public async Task MainStreet_OldGodsList_ShowsTheDeepTemple_ForAurelion()
    {
        var hero = StatRewards1115Tests.Fresh("MsGods");
        using var _ = new StoryScope(
            (OldGodType.Maelketh, GodStatus.Defeated, true),
            (OldGodType.Aurelion, GodStatus.Hostile, true));
        var (street, term, output) = StreetRig(hero, "");
        await (Task)typeof(MainStreetLocation).GetMethod("ShowStoryProgress", F)!.Invoke(street, null)!;
        string text = Text(term, output);

        text.Should().NotContain("Fl.85", "Aurelion's row must not point to the old dungeon floor");
        text.Should().Contain(Loc.Get("temple.room.deep"), "Aurelion's row must name the Deep Temple");
        text.Should().Contain("Fl.25", "other Old Gods keep their floor");
    }

    // ---------------- Inn: the bartender rumor points to the Temple for Aurelion ----------------

    private static InnLocation InnRig(Character hero)
    {
        var inn = new InnLocation();
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(inn, hero);
        return inn;
    }

    private static string BartenderRumor(InnLocation inn)
    {
        var m = typeof(InnLocation).GetMethod("GetBartenderRumor", F);
        m.Should().NotBeNull();
        try { return (string)m!.Invoke(inn, null)!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    [Fact]
    public void BartenderRumor_NamesTheTemple_ForAurelion_NotFloor85()
    {
        var hero = StatRewards1115Tests.Fresh("InnRumor");
        using var _ = new StoryScope(
            (OldGodType.Maelketh, GodStatus.Defeated, true),
            (OldGodType.Veloura, GodStatus.Defeated, true),
            (OldGodType.Thorgrim, GodStatus.Defeated, true),
            (OldGodType.Noctura, GodStatus.Defeated, true),
            (OldGodType.Aurelion, GodStatus.Imprisoned, false));
        var inn = InnRig(hero);
        string rumor = BartenderRumor(inn);

        rumor.Should().NotContain("85");
        rumor.Should().Contain("Aurelion");
        rumor.Should().Be(Loc.Get("inn.bartender_rumor_next_god_temple"));
    }
}
