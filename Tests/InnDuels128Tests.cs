using System;
using System.Collections.Generic;
using System.Globalization;
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

/// <summary>
/// 1.2.8 piece P2. Seth Able fights at the level his challenge screen shows, and his win pays the
/// Inn's flat reward only, with no loot roll. Town honor duels and Inn duels share one daily cap
/// (InnDuelsToday, GameConfig.MaxNpcDuelsPerDay), counted on a win and cleared by the daily reset.
/// The Pit's reckless side bet label states the multiplier the code pays.
/// </summary>
[Collection("SharedGameSingletons")]
public class InnDuels128Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private static string Plain(MemoryStream output, TerminalEmulator term)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    private static Character Hero(int level = 18) => new Character
    {
        Name1 = "duel_tester", Name2 = "Duel Tester", Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        Level = level, AI = CharacterAI.Human, HP = 50000, MaxHP = 50000, BaseMaxHP = 50000,
        Strength = 5000, BaseStrength = 5000, Defence = 2000, BaseDefence = 2000, WeapPow = 5000,
        Dexterity = 200, BaseDexterity = 200, Agility = 200, BaseAgility = 200, Constitution = 200, BaseConstitution = 200,
        Stamina = 200, BaseStamina = 200, CombatSpeed = CombatSpeed.Instant,
        MKills = 1,   // not a first kill, so no First Blood bonus in the reward
    };

    private static T Place<T>(T loc, Character hero, TerminalEmulator term) where T : BaseLocation
    {
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, hero);
        return loc;
    }

    // ---------- (i) Seth Able ----------

    /// <summary>Runs the Inn's Seth challenge on a script (confirm, attack, then no to any offer after the fight), returns the screen.</summary>
    private static async Task<string> SethFight(Character hero)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "Y" }.Concat(Enumerable.Repeat("A", 10)).Concat(Enumerable.Repeat("N", 30))), output);   // N turns away a wandering monk
        var inn = Place(new InnLocation(), hero, term);
        try { await (Task)typeof(InnLocation).GetMethod("ChallengeSethAble", F)!.Invoke(inn, null)!; }
        catch (IOException) { }   // the scripted input ran out after the fight
        return Plain(output, term);
    }

    [Theory]
    [InlineData(18)]
    [InlineData(40)]
    public async Task Seth_FightsAtTheLevelHisChallengeScreenShows(int heroLevel)
    {
        var hero = Hero(heroLevel);
        string screen = await SethFight(hero);

        var shown = Regex.Match(screen, "Seth Able - Level (\\d+) - HP");
        shown.Success.Should().BeTrue("the challenge screen shows Seth's level");
        var fought = Regex.Match(screen, "Seth Able \\(Level (\\d+)\\)");
        fought.Success.Should().BeTrue("the fight banner shows the fighting monster's level");
        fought.Groups[1].Value.Should().Be(shown.Groups[1].Value, "Seth fights at the level the Inn shows");
        int.Parse(shown.Groups[1].Value).Should().Be(Math.Clamp(heroLevel + 3, 15, 80));

        var inn = Place(new InnLocation(), hero, new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        int sethLevel = (int)typeof(InnLocation).GetMethod("GetSethLevel", F)!.Invoke(inn, null)!;
        inn.CreateSethMonster().Level.Should().Be(sethLevel);
    }

    [Fact]
    public async Task Seth_Win_PaysTheFlatBonusOnly()
    {
        var hero = Hero(18);
        // every achievement already held, so no unlock reward lands in the fight's XP or gold. The list is
        // empty until some code initializes it, so initialize here: the test then reads the same in any order
        AchievementSystem.Initialize();
        foreach (var a in AchievementSystem.GetAllAchievements()) hero.Achievements.Unlock(a.Id);
        long xp0 = hero.Experience, gold0 = hero.Gold;
        string screen = await SethFight(hero);

        hero.SethDefeatsTotal.Should().Be(1, "the scripted hero beats Seth in one try: " + screen);
        long flatXp = TeamHQBonus.ApplyXP(hero, hero.Level * 200L);
        long flatGold = 50 + hero.Level * 5L;
        (hero.Experience - xp0).Should().Be(flatXp, "the Inn's flat bonus is the whole XP reward: " + screen);
        (hero.Gold - gold0).Should().Be(flatGold, "the Inn's flat bonus is the whole gold reward: " + screen);
        screen.Should().Contain(Loc.Get("inn.seth_earn_reward", $"{flatXp:N0}", $"{flatGold:N0}"));
        screen.Should().NotContain(Loc.Get("combat.xp_label", "0"), "the engine shows no reward of its own for Seth");
    }

    /// <summary>Runs the loot roll alone on one defeated monster with a seeded engine; true when an item dropped.</summary>
    private static async Task<bool> Drops(Monster monster, int seed)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Array.Empty<string>()), output);
        var engine = new CombatEngine(term);
        engine.SeedRandomForTests(seed);
        var result = new CombatResult { Player = Hero(18), Monster = monster, DefeatedMonsters = new List<Monster> { monster } };
        try { await (Task)typeof(CombatEngine).GetMethod("CheckForEquipmentDrop", F)!.Invoke(engine, new object[] { result })!; }
        catch (Exception) { return true; }   // the drop screen was reached (it stops on the bare test hero)
        return Plain(output, term).Trim().Length > 0;
    }

    [Fact]
    public async Task Seth_DropsNoLoot()
    {
        var hero = Hero(30);
        var inn = Place(new InnLocation(), hero, new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        var seeds = Enumerable.Range(1, 60).ToList();

        int controlDrops = 0;
        foreach (int s in seeds)
        {
            var control = inn.CreateSethMonster();
            control.CallerPaysReward = false;
            if (await Drops(control, s)) controlDrops++;
        }
        controlDrops.Should().BeGreaterThan(0, "the same monster without the flag rolls for loot on some of these seeds");

        foreach (int s in seeds)
            (await Drops(inn.CreateSethMonster(), s)).Should().BeFalse($"Seth is out of the loot roll (seed {s})");
    }

    // ---------- (ii) one shared daily duel cap ----------

    /// <summary>A location whose town duel acceptance and fight are fixed, so ChallengeNPC runs without dice or combat.</summary>
    private sealed class TownProbe : BaseLocation
    {
        public bool Accept = true;
        public bool Win = true;
        public int Fights;
        public TownProbe() : base(GameLocation.MainStreet, "Duel Probe") { }
        protected override bool ShouldNPCAcceptDuel(NPC npc) => Accept;
        protected override Task<EncounterResult> FightHonorDuel(NPC npc)
        {
            Fights++;
            return Task.FromResult(new EncounterResult { EncounterOccurred = true, Victory = Win });
        }
    }

    private static NPC Rival(int i, int darkness = 0) => new NPC
    {
        ID = $"npc_duel_rival_{i}", Name1 = $"Duel Rival {i}", Name2 = $"Duel Rival {i}", Level = 18,
        HP = 300, MaxHP = 300, Darkness = darkness, CurrentLocation = "Inn",
    };

    private static async Task<string> TownChallenge(TownProbe probe, Character hero, NPC npc)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Array.Empty<string>()), output);
        Place(probe, hero, term);
        await (Task)typeof(BaseLocation).GetMethod("ChallengeNPC", F)!.Invoke(probe, new object[] { npc })!;
        return Plain(output, term);
    }

    /// <summary>The Inn's duel menu path on a rival who always accepts; the player then declines to fight.</summary>
    private static async Task<string> InnChallenge(Character hero, NPC npc)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "N", "", "" }), output);
        var inn = Place(new InnLocation(), hero, term);
        try { await (Task)typeof(InnLocation).GetMethod("ChallengeNPC", F)!.Invoke(inn, new object[] { npc })!; }
        catch (IOException) { }
        return Plain(output, term);
    }

    [Fact]
    public async Task ThreeInnDuels_ThenATownChallenge_IsRefused()
    {
        var hero = Hero();
        hero.InnDuelsToday = GameConfig.MaxNpcDuelsPerDay;   // three Inn duels won today (the Inn counts each win)
        var probe = new TownProbe();

        string screen = await TownChallenge(probe, hero, Rival(1));

        screen.Should().Contain(Loc.Get("base.duel_daily_limit").Trim());
        screen.Should().NotContain(Loc.Get("base.duel_challenge", "Duel Rival 1").Trim(), "a capped player is not told the challenge went out");
        probe.Fights.Should().Be(0, "no duel is fought over the cap");
        hero.InnDuelsToday.Should().Be(GameConfig.MaxNpcDuelsPerDay);
    }

    [Fact]
    public async Task ThreeTownDuels_ThenAnInnDuel_IsRefused()
    {
        GameConfig.MaxNpcDuelsPerDay.Should().Be(3, "the user's ruling: one shared cap of 3");
        var hero = Hero();
        var probe = new TownProbe();

        for (int i = 1; i <= GameConfig.MaxNpcDuelsPerDay; i++)
        {
            string won = await TownChallenge(probe, hero, Rival(i));
            won.Should().NotContain(Loc.Get("base.duel_daily_limit").Trim(), $"town duel {i} is under the cap");
        }
        probe.Fights.Should().Be(GameConfig.MaxNpcDuelsPerDay);
        hero.InnDuelsToday.Should().Be(GameConfig.MaxNpcDuelsPerDay, "each town duel won counts, as an Inn duel won does");

        string inn = await InnChallenge(hero, Rival(9, darkness: 1000));
        inn.Should().Contain(Loc.Get("inn.too_many_duels"));
        inn.Should().NotContain(Loc.Get("inn.npc_accepts_challenge", "Duel Rival 9"));

        string fourth = await TownChallenge(probe, hero, Rival(4));
        fourth.Should().Contain(Loc.Get("base.duel_daily_limit").Trim());
        probe.Fights.Should().Be(GameConfig.MaxNpcDuelsPerDay);
    }

    [Fact]
    public async Task ALostTownDuel_DoesNotCount()
    {
        var hero = Hero();
        var probe = new TownProbe { Win = false };
        await TownChallenge(probe, hero, Rival(1));
        probe.Fights.Should().Be(1);
        hero.InnDuelsToday.Should().Be(0, "the Inn counts wins only, and the town matches it");
    }

    [Fact]
    public async Task TheDailyReset_ReopensBothDuelPaths()
    {
        var hero = Hero();
        hero.InnDuelsToday = GameConfig.MaxNpcDuelsPerDay;

        var engine = GameEngine.Instance;
        var old = engine.CurrentPlayer;
        engine.CurrentPlayer = hero;
        try
        {
            await (Task)typeof(DailySystemManager).GetMethod("RunBasicDailyReset", F)!.Invoke(DailySystemManager.Instance, null)!;
        }
        finally { engine.CurrentPlayer = old; }
        hero.InnDuelsToday.Should().Be(0);

        string inn = await InnChallenge(hero, Rival(9, darkness: 1000));
        inn.Should().NotContain(Loc.Get("inn.too_many_duels"));
        inn.Should().Contain(Loc.Get("inn.npc_accepts_challenge", "Duel Rival 9"), "the Inn duel is offered again");

        var probe = new TownProbe();
        string town = await TownChallenge(probe, hero, Rival(1));
        town.Should().NotContain(Loc.Get("base.duel_daily_limit").Trim());
        probe.Fights.Should().Be(1, "the town duel is fought again");
    }

    [Fact]
    public void TheTownRefusal_IsTranslated_AndFits79Columns()
    {
        string en = Loc.GetIn("en", "base.duel_daily_limit");
        foreach (var lang in Langs)
        {
            string text = Loc.GetIn(lang, "base.duel_daily_limit");
            text.Should().NotBe("base.duel_daily_limit", lang);
            if (lang != "en") text.Should().NotBe(en, $"{lang} has its own translation");
            text.Should().NotContainAny(new[] { "\u2013", "\u2014" }, lang);
        }
        foreach (var lang in new[] { "en", "hu" })
            Loc.GetIn(lang, "base.duel_daily_limit").Length.Should().BeLessThanOrEqualTo(79, lang);
    }

    // ---------- (iii) the Pit's side bet labels ----------

    private static double LabelMultiplier(string label)
    {
        var m = Regex.Match(label, "^(\\d+(?:[.,]\\d+)?)x ");
        m.Success.Should().BeTrue($"'{label}' starts with its multiplier");
        return double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
    }

    [Fact]
    public void EveryLanguage_PitBetLabels_StateTheMultiplierTheCodePays()
    {
        foreach (var lang in Langs)
        {
            LabelMultiplier(Loc.GetIn(lang, "dark_alley.bet_reckless")).Should().Be(DarkAlleyLocation.PitBetRecklessMultiplier, $"{lang} reckless");
            LabelMultiplier(Loc.GetIn(lang, "dark_alley.bet_risky")).Should().Be(DarkAlleyLocation.PitBetRiskyMultiplier, $"{lang} risky");
            LabelMultiplier(Loc.GetIn(lang, "dark_alley.bet_safe")).Should().Be(DarkAlleyLocation.PitBetSafeMultiplier, $"{lang} safe");
        }
        DarkAlleyLocation.PitBetRecklessMultiplier.Should().Be(2.5f, "the user's ruling: the label changes, the payout does not");
    }

    [Fact]
    public void ThePitBetRow_Fits79Columns()
    {
        foreach (var lang in new[] { "en", "hu" })
            ("  [3] " + Loc.GetIn(lang, "dark_alley.bet_reckless")).Length.Should().BeLessThanOrEqualTo(79, lang);
    }
}
