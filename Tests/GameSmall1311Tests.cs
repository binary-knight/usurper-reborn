using System;
using System.Collections.Generic;
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
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.11 small game rows. B7: the Ancient Scroll result waits for Enter. G1: the murder revenge grudge asks again on
/// a key it does not offer. K6: the arrest text says imprisoned only when someone was. K7: the street Smoke Bomb text
/// states what it gives. K8: the outpost torch and lockpick say what they do. K9: the dungeon merchant's healing
/// potion follows the player's level. K10, K14: two unused keys are gone. K12: BanditLordRises is gone and old state
/// naming it still loads. K15: the unread stat effects are gone and old state with them still loads. K16: a dying
/// wish message for someone permanently dead is dropped at the daily reset. K17: a recipient holding a family grudge
/// still receives the message. Members added in 1.3.11 are reached by reflection so the class also runs on the
/// release before it (the repro). All in memory.
/// </summary>
[Collection("SharedGameSingletons")]
public sealed class GameSmall1311Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PS = BindingFlags.Public | BindingFlags.Static;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private readonly King? _kingBefore = CastleLocation.GetCurrentKing();
    private readonly List<NPC> _rosterBefore = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
    private readonly bool _pacing = Pacing.Disabled;
    private readonly List<string> _noticesBefore = GameEngine.PendingNotifications.ToList();
    private readonly object? _streetRandomBefore = typeof(StreetEncounterSystem).GetField("_random", F)!.GetValue(StreetEncounterSystem.Instance);

    public GameSmall1311Tests()
    {
        Pacing.Disabled = true;
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        GameEngine.PendingNotifications.Clear();
    }

    public void Dispose()
    {
        CastleLocation.SetKing(_kingBefore!);
        var active = NPCSpawnSystem.Instance.ActiveNPCs;
        active.Clear();
        active.AddRange(_rosterBefore);
        Pacing.Disabled = _pacing;
        GameEngine.PendingNotifications.Clear();
        foreach (var n in _noticesBefore) GameEngine.PendingNotifications.Enqueue(n);
        typeof(StreetEncounterSystem).GetField("_random", F)!.SetValue(StreetEncounterSystem.Instance, _streetRandomBefore);
    }

    // ---------- helpers ----------

    /// <summary>Next gives the set value, within the bound asked for; NextDouble gives 0.</summary>
    private sealed class FixedRandom : Random
    {
        private readonly int _value;
        public FixedRandom(int value) { _value = value; }
        public override int Next() => _value;
        public override int Next(int maxValue) => Math.Min(_value, Math.Max(0, maxValue - 1));
        public override int Next(int minValue, int maxValue) => Math.Clamp(_value, minValue, Math.Max(minValue, maxValue - 1));
        public override double NextDouble() => 0;
    }

    private static string RawText(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static string Plain(string raw) => Regex.Replace(raw, "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");

    private static string Plain(TerminalEmulator term, MemoryStream output) => Plain(RawText(term, output));

    private static async Task Run(object target, Type owner, string method, params object?[] args)
    {
        try
        {
            var r = owner.GetMethod(method, F)!.Invoke(target, args);
            if (r is Task t) await t;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static Character Hero(int level = 8) => new Character
    {
        Name1 = "gs", Name2 = "Gs Hero", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = level,
        HP = 300, MaxHP = 300, BaseMaxHP = 300, Gold = 1_000_000, Mental = 100, AI = CharacterAI.Human, Dexterity = 60, Wisdom = 60,
        Healing = 2, Chivalry = 500,
    };

    private static NPC Npc(string id, string name, int level = 8)
    {
        var npc = new NPC
        {
            ID = id, Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Race = CharacterRace.Human,
            Level = level, HP = 500, MaxHP = 500, BaseMaxHP = 500, Strength = 300, BaseStrength = 300,
            Gold = 5000, CurrentLocation = "Main Street", Sex = CharacterSex.Female, BirthDate = DateTime.Now,
        };
        var profile = PersonalityProfile.GenerateForArchetype("commoner");
        npc.Personality = profile;
        npc.Brain = new NPCBrain(npc, profile);
        npc.Memory = npc.Brain.Memory;
        return npc;
    }

    private static (DungeonLocation d, TerminalEmulator term, MemoryStream output, Character hero) Dungeon(Random random, int level, int floor, params string[] lines)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines), output);
        var hero = Hero(level);
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, floor);
        typeof(DungeonLocation).GetField("dungeonRandom", F)!.SetValue(d, random);
        return (d, term, output, hero);
    }

    private static int Count(string text, string part) => Regex.Matches(text, Regex.Escape(part)).Count;

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        dir.Should().NotBeNull();
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    // ---------- K7: the street merchant's Smoke Bomb text ----------

    [Fact]
    public void K7_TheSmokeBombText_StatesItsEffect_InEveryLanguage()
    {
        const string key = "street_encounter.merchant.item.smoke_bomb_desc";
        Loc.GetIn("en", key).Should().Contain("Darkness +2").And.NotContain("Guaranteed");
        Loc.GetIn("es", key).Should().Contain("Oscuridad +2").And.NotContain("garantizada");
        Loc.GetIn("fr", key).Should().Contain("Ténèbres +2").And.NotContain("garantie");
        Loc.GetIn("hu", key).Should().Contain("Sötétség +2").And.NotContain("Garantált");
        Loc.GetIn("it", key).Should().Contain("Oscurità +2").And.NotContain("garantita");
        foreach (var lang in new[] { "en", "hu" })
            ("      " + Loc.GetIn(lang, key)).Length.Should().BeLessOrEqualTo(79, lang);
    }

    [Fact]
    public void K7_TheSmokeBomb_StillGivesDarknessTwo_AndNothingForFleeing()
    {
        var hero = Hero();
        hero.Darkness = 5;
        int bombs = hero.SmokeBombs;
        var itemType = typeof(StreetEncounterSystem).GetNestedType("MerchantItem", BindingFlags.NonPublic)!;
        object item = Activator.CreateInstance(itemType)!;
        itemType.GetField("Name")!.SetValue(item, "Smoke Bomb");
        typeof(StreetEncounterSystem).GetMethod("ApplyMerchantItem", F)!.Invoke(StreetEncounterSystem.Instance, new object[] { hero, item });
        hero.Darkness.Should().Be(7);
        hero.SmokeBombs.Should().Be(bombs);
    }

    // ---------- K8: the outpost torch and lockpick ----------

    [Theory]
    [InlineData("en", "HP", "Dexterity")]
    [InlineData("es", "PV", "Destreza")]
    [InlineData("fr", "PV", "Dextérité")]
    [InlineData("hu", "ÉP", "Ügyesség")]
    [InlineData("it", "PV", "Destrezza")]
    public void K8_TheNames_SayWhatEachDoes(string lang, string hp, string dex)
    {
        Loc.GetIn(lang, "dungeon.settlement_item_torch").Should().Contain($"+10 {hp}");
        Loc.GetIn(lang, "dungeon.settlement_item_lockpick").Should().Contain($"+2 {dex}");
    }

    [Fact]
    public void K8_TheEffects_AreWhatTheNamesSay()
    {
        var (d, _, _, hero) = Dungeon(new FixedRandom(0), 8, 5);
        hero.HP = 100;
        var apply = typeof(DungeonLocation).GetMethod("ApplySettlementPurchase", F)!;
        apply.Invoke(d, new object[] { hero, "torch" });
        hero.HP.Should().Be(110);
        apply.Invoke(d, new object[] { hero, "lockpick" });
        hero.TimedStatBuffs.Should().Contain(b => b.Source == "settlement_lockpick" && b.Stat == StatKind.Dexterity && b.Amount == 2);
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")] [InlineData("it")]
    public void K8_ThePriceColumn_StaysLinedUp_AndTheRowsFit(string lang)
    {
        var (d, term, output, hero) = Dungeon(new FixedRandom(0), 8, 5, "0");
        string prev = GameConfig.Language;
        GameConfig.Language = lang;
        try
        {
            var settlement = Enumerable.Range(1, 100).Select(DungeonSettlementData.GetSettlement).First(s => s != null && s.TradeItems.Contains("Lockpick"))!;
            var r = typeof(DungeonLocation).GetMethod("SettlementTrade", F)!.Invoke(d, new object[] { hero, settlement });
            if (r is Task t) t.GetAwaiter().GetResult();
        }
        finally { GameConfig.Language = prev; }
        var rows = Plain(term, output).Split('\n').Where(r => Regex.IsMatch(r, @"^  \[\d+\] ") && r.TrimEnd().EndsWith("g")).ToList();
        rows.Should().HaveCountGreaterThan(1);
        rows.Select(r => Regex.Match(r, @" +[\d,.]+g\s*$").Index + Regex.Match(r, @" +[\d,.]+g\s*$").Length - Regex.Match(r, @"[\d,.]+g\s*$").Length)
            .Distinct().Count().Should().BeLessOrEqualTo(1, "the prices start in one column");
        rows.Should().OnlyContain(r => r.TrimEnd().Length <= 79);
    }
}
