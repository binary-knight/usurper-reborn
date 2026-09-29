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

    // ---------------- Removed keys: the confession, old flat menu and status keys piece 7 left behind ----------------

    /// <summary>
    /// The Temple Confession, the flat pre-rooms menu and its screen reader labels, and the old
    /// status counters no code reads any more. Kept: temple.sr_ask_join, temple.sr_leave_prayers and
    /// temple.sr_talk_past, still read by the Faith room's Cloister prompt.
    /// </summary>
    private static readonly string[] RemovedKeys =
    {
        "temple.believers_count", "temple.confess_cancelled", "temple.confess_cap", "temple.confess_current",
        "temple.confess_daily_limit", "temple.confess_flavor", "temple.confess_header", "temple.confess_intro",
        "temple.confess_no_sins", "temple.confess_prompt", "temple.confess_rate", "temple.confess_success",
        "temple.confess_too_poor", "temple.menu_altars", "temple.menu_confess", "temple.menu_contribute",
        "temple.menu_deep_temple", "temple.menu_desecrate", "temple.menu_examine_stones", "temple.menu_faith_member",
        "temple.menu_faith_seek", "temple.menu_faith_serve_another", "temple.menu_following", "temple.menu_god_ranking",
        "temple.menu_hall_fallen", "temple.menu_holy_news", "temple.menu_inner_sanctum", "temple.menu_inner_sanctum_cost",
        "temple.menu_item_sacrifice", "temple.menu_join_flock", "temple.menu_leave_faith", "temple.menu_meditated_today",
        "temple.menu_meditation_chapel", "temple.menu_meditation_hint", "temple.menu_pray", "temple.menu_prayed_today",
        "temple.menu_prophecies", "temple.menu_return", "temple.menu_rite", "temple.menu_sacrifice_gold",
        "temple.menu_sacrifice_gold_hint", "temple.menu_status", "temple.menu_the_faith", "temple.menu_unaffiliated",
        "temple.menu_worship", "temple.sr_altars", "temple.sr_ascended_gods", "temple.sr_confess",
        "temple.sr_contribute", "temple.sr_deep_temple", "temple.sr_desecrate", "temple.sr_examine_stones",
        "temple.sr_faith_member", "temple.sr_faith_seek", "temple.sr_faith_serve_another", "temple.sr_god_ranking",
        "temple.sr_hall_fallen", "temple.sr_holy_news", "temple.sr_inner_sanctum_cost", "temple.sr_inner_sanctum_meditated",
        "temple.sr_item_sacrifice", "temple.sr_join_immortal_following", "temple.sr_join_immortal_unaffiliated", "temple.sr_leave_immortal",
        "temple.sr_meditation_chapel", "temple.sr_pray", "temple.sr_prayed_today", "temple.sr_prophecies",
        "temple.sr_return", "temple.sr_rite", "temple.sr_sacrifice_gold", "temple.sr_status",
        "temple.sr_worship", "temple.your_status",
    };

    private static readonly string[] KeptSrKeys = { "temple.sr_ask_join", "temple.sr_leave_prayers", "temple.sr_talk_past" };

    [Fact]
    public void RemovedTempleKeys_AreGone_FromEveryLanguage()
    {
        foreach (var lang in Langs)
        {
            var d = LoadLang(lang);
            var stillThere = RemovedKeys.Where(k => d.ContainsKey(k)).ToList();
            stillThere.Should().BeEmpty($"{lang}.json still has keys no code reads: {string.Join(", ", stillThere)}");
        }
    }

    [Fact]
    public void KeptScreenReaderKeys_StillRead_ByTheFaithRoom()
    {
        string src = File.ReadAllText(Path.Combine(SourceRoot(), "Scripts/Locations/TempleLocation.cs"));
        foreach (var key in KeptSrKeys)
            src.Should().Contain($"\"{key}\"", $"{key} is still read and must not be removed");
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Scripts/Locations/TempleLocation.cs"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root not found above " + AppContext.BaseDirectory);
    }

    // ---------------- (a) K and V: the memory pointer only while the Halls of Memory are open ----------------

    /// <summary>
    /// The permanent Founder statues at the Pantheon keep HallsOfMemoryOpen() true for every account
    /// in practice, so this is a source guard, not a reachable runtime state; it protects the day the
    /// seed data changes.
    /// </summary>
    [Fact]
    public void MemoryPointer_ForKAndV_IsGatedOnHallsOpen()
    {
        string src = File.ReadAllText(Path.Combine(SourceRoot(), "Scripts/Locations/TempleLocation.cs"));
        int marker = src.IndexOf("string? pointer = key == \"H\"", StringComparison.Ordinal);
        marker.Should().BeGreaterThan(0);
        string tail = src.Substring(marker, 300);
        tail.Should().Contain("pointer == \"temple.moved.memory\"").And.Contain("HallsOfMemoryOpen()").And.Contain("pointer = null;");
    }

    // ---------------- (b) An offering's kind menu: an unhandled key is not silent ----------------

    private static (TempleLocation Temple, TerminalEmulator Term, MemoryStream Output, Character Hero) OfferRig(string name, params string[] lines)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 20))), output);
        var temple = new TempleLocation(term, null!, UsurperRemake.GodSystemSingleton.Instance);
        var hero = StatRewards1115Tests.Fresh(name);
        hero.Gold = 100_000;
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(temple, hero);
        return (temple, term, output, hero);
    }

    private static async Task RunOffering(TempleLocation temple, TempleLocation.TempleRoom room)
    {
        var m = typeof(TempleLocation).GetMethod("ProcessOffering", F);
        m.Should().NotBeNull();
        try { await (Task)m!.Invoke(temple, new object[] { room })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    [Fact]
    public async Task Offering_UnhandledKey_PrintsInvalidChoice()
    {
        var (temple, term, output, _) = OfferRig("TlOfferBadKind", "Solarius", "Z");
        await RunOffering(temple, TempleLocation.TempleRoom.Nave);
        term.StreamWriterInternal?.Flush();
        Encoding.UTF8.GetString(output.ToArray()).Should().Contain(Loc.Get("temple.invalid_choice"));
    }

    [Fact]
    public async Task Offering_R_LeavesQuietly()
    {
        var (temple, term, output, _) = OfferRig("TlOfferBack", "Solarius", "R");
        await RunOffering(temple, TempleLocation.TempleRoom.Nave);
        term.StreamWriterInternal?.Flush();
        Encoding.UTF8.GetString(output.ToArray()).Should().NotContain(Loc.Get("temple.invalid_choice"));
    }

    // ---------------- (c) Floor 85: sealed until the player can actually enter the Deep Temple ----------------

    [Fact]
    public void Floor85_ExplainsTheSealedDoor_WhenNotReadyForAurelion()
    {
        string src = File.ReadAllText(Path.Combine(SourceRoot(), "Scripts/Locations/DungeonLocation.cs"));
        int check = src.IndexOf("if (currentDungeonLevel == 85 && AurelionAwaitsAtTemple())", StringComparison.Ordinal);
        int fight = src.IndexOf("await TryOldGodBossEncounter(player!, room);", StringComparison.Ordinal);
        check.Should().BeGreaterThan(0);
        fight.Should().BeGreaterThan(check);
        string branch = src.Substring(check, fight - check);
        branch.Should().Contain("CanEncounterBoss(player!, OldGodType.Aurelion)");
        branch.Should().Contain("Loc.Get(\"dungeon.aurelion_at_temple_hint\")");
        branch.Should().Contain("Loc.Get(\"temple.deep_temple_sealed\")");
        branch.Should().Contain("Loc.Get(\"temple.deep_temple_prove\")");
    }

    // ---------------- (d) The Nave exempts an Evil player's own good god ----------------

    private static bool NaveRefuses(Character hero, string ownGod, TempleLocation.AltarPick pick)
    {
        var temple = new TempleLocation(new TerminalEmulator(new LineStream(Enumerable.Repeat("", 5)), new MemoryStream()), null!, UsurperRemake.GodSystemSingleton.Instance);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(temple, hero);
        if (!string.IsNullOrEmpty(ownGod)) GodRegistry.SetWorshippedGod(hero, ownGod, UsurperRemake.GodSystemSingleton.Instance);
        var m = typeof(TempleLocation).GetMethod("NaveRefusesOffering", F);
        try { return (bool)m!.Invoke(temple, new object[] { TempleLocation.TempleRoom.Nave, pick })!; }
        finally { GodRegistry.SetWorshippedGod(hero, null, UsurperRemake.GodSystemSingleton.Instance); }
    }

    [Fact]
    public void Nave_AcceptsAnEvilFollowers_OwnGoodGod_ButRefusesAnotherOne()
    {
        var hero = StatRewards1115Tests.Fresh("TlNaveEvil");
        hero.Chivalry = 0;
        hero.Darkness = 900;
        var solarius = new TempleLocation.AltarPick("Solarius", new God { Name = "Solarius", Goodness = 10000, Darkness = 0 }, null);
        var amara = new TempleLocation.AltarPick("Amara", new God { Name = "Amara", Goodness = 8000, Darkness = 1000 }, null);

        NaveRefuses(hero, "Solarius", solarius).Should().BeFalse("an Evil follower may still offer to their own good god");
        NaveRefuses(hero, "Solarius", amara).Should().BeTrue("another good god is still refused");
    }
}
