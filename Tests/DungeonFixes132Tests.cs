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
using UsurperRemake.Data;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.2: four dungeon and outskirts faults.
/// 1. The Sellsword contract "Bandits on the Trade Road" named a monster "Bandit" that does not exist. The bandits
///    met are the wilderness Bandit Scout and Mountain Bandit; both now count for it. It needs 3 kills at any level
///    and lasts 14 days. A contract claimed before (target "Bandit") counts the same kills.
/// 2. The Outskirts Prison buff halved only riddle and puzzle failures. It now halves room trap and chest trap
///    damage too, before the trap's stat check; gold and experience losses are not damage and stay.
/// 3. The Herbalist Hut gave nothing when its random herb was at its limit and could be tried again. It now refuses
///    only when every herb is at its limit and otherwise gives one of the herbs below their limit. The Mystic
///    Circle no longer spends its day when mana (or HP) is already full.
/// 4. A failed riddle charged its own damage and then that damage x floor / 5 more; fleeing charged the second part.
///    It now charges its own damage once (the Prison buff halves it), and fleeing charges nothing.
/// All in memory.
/// </summary>
[Collection("SharedGameSingletons")]
public class DungeonFixes132Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags S = BindingFlags.NonPublic | BindingFlags.Static;

    private readonly bool _pacingWas = Pacing.Disabled;
    private readonly bool _srWas = GameConfig.ScreenReaderMode;
    private readonly List<Quest> _quests = new();

    public DungeonFixes132Tests()
    {
        Pacing.Disabled = true;
        GameConfig.ScreenReaderMode = false;
    }

    public void Dispose()
    {
        Pacing.Disabled = _pacingWas;
        GameConfig.ScreenReaderMode = _srWas;
        foreach (var q in _quests) q.Deleted = true;
    }

    // ---------- helpers ----------

    private sealed class Screen
    {
        public TerminalEmulator Term = null!;
        public MemoryStream Output = new();
        public string Text
        {
            get
            {
                Term.StreamWriterInternal?.Flush();
                return Regex.Replace(Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
            }
        }
    }

    private static Screen At(BaseLocation loc, Character hero, params string[] lines)
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 30))), s.Output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, hero);
        return s;
    }

    private static async Task Run(object target, string method, params object?[] args)
    {
        var m = target.GetType().GetMethod(method, F);
        m.Should().NotBeNull(method);
        try
        {
            var r = m!.Invoke(target, args);
            if (r is Task t)
            {
                (await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(60)))).Should().BeSameAs(t, "{0} must return", method);
                await t;
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static Character Hero(string name, CharacterClass cls = CharacterClass.Warrior) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Class = cls, Race = CharacterRace.Human, Level = 30,
        HP = 100000, MaxHP = 100000, Gold = 100000, Experience = 1000000,
        Agility = 1, Dexterity = 1, Constitution = 1, Wisdom = 1, Mental = 50, MentalHintShown = true,
    };

    private static void GivePrisonBuff(Character hero)
    {
        hero.SettlementBuffType = (int)SettlementBuffType.TrapResist;
        hero.SettlementBuffCombats = GameConfig.SettlementBuffDuration;
        hero.SettlementBuffValue = GameConfig.SettlementTrapResist;
    }

    private static (DungeonLocation d, Screen s) Dungeon(Character hero, int floor, int seed, params string[] lines)
    {
        var d = new DungeonLocation();
        var s = At(d, hero, lines);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, floor);
        typeof(DungeonLocation).GetField("dungeonRandom", F)!.SetValue(d, new Random(seed));
        return (d, s);
    }

    // ---------- 1. the bandit contract ----------

    private Quest MercContract(string id, Character hero)
    {
        var templates = (Array)typeof(QuestSystem).GetField("MercTemplatesSlice1", S)!.GetValue(null)!;
        var tpl = templates.Cast<object>().Single(t => (string)t.GetType().GetField("Id")!.GetValue(t)! == id);
        var quest = (Quest)typeof(QuestSystem).GetMethod("CreateMercContract", S)!.Invoke(null, new[] { tpl, (object)hero.Level })!;
        _quests.Add(quest);
        quest.Occupier = hero.Name2;
        return quest;
    }

    private static void Kill(Character hero, string name) => QuestSystem.OnMonsterKilled(hero, name, false, name);

    [Fact]
    public void Both_wilderness_bandits_count_and_the_contract_completes_at_three_kills()
    {
        var hero = Hero("Contract Taker");
        var quest = MercContract("crown_bandit_purge", hero);
        var o = quest.Objectives.Single();
        o.TargetId.Should().Be("Bandit", "the stored target stays, so contracts in saves keep working");
        o.RequiredProgress.Should().Be(3, "3 kills at any level");
        quest.DaysToComplete.Should().Be(14);

        Kill(hero, "Bandit Scout");
        Kill(hero, "Mountain Bandit");
        o.CurrentProgress.Should().Be(2, "each bandit kill counts once");
        Kill(hero, "Bandit Scout");
        o.IsComplete.Should().BeTrue();
        ((bool)typeof(QuestSystem).GetMethod("ValidateQuestCompletion", S)!.Invoke(null, new object[] { hero, quest })!).Should().BeTrue("the contract can be turned in");
    }

    [Fact]
    public void Other_monsters_do_not_count_for_the_bandit_contract()
    {
        var hero = Hero("Contract Taker Two");
        var quest = MercContract("crown_bandit_purge", hero);
        foreach (var name in new[] { "Timber Wolf", "Goblin", "Orc", "Zombie", "Forest Troll" }) Kill(hero, name);
        quest.Objectives.Single().CurrentProgress.Should().Be(0);
    }

    [Fact]
    public void A_bandit_contract_claimed_before_the_fix_counts_the_bandits()
    {
        var hero = Hero("Old Contract");
        // as CreateMercContract made it before: target "Bandit", 3 + level / 5, 7 days
        var quest = new Quest
        {
            Id = "merc_crown_bandit_purge_old", Initiator = QuestSystem.MERC_INITIATOR_CROWN, Occupier = hero.Name2,
            Date = DateTime.Now, DaysToComplete = 7, IsMercContract = true, IssuingFaction = Faction.TheCrown, MercContractTier = 1,
        };
        quest.Objectives.Add(new QuestObjective { Id = "merc_obj_crown_bandit_purge", ObjectiveType = QuestObjectiveType.KillSpecificMonster, TargetId = "Bandit", TargetName = "Bandit", RequiredProgress = 9 });
        QuestSystem.AddQuestToDatabase(quest);
        _quests.Add(quest);

        Kill(hero, "Bandit Scout");
        Kill(hero, "Mountain Bandit");
        quest.Objectives[0].CurrentProgress.Should().Be(2);
    }

    [Fact]
    public void A_bandit_kill_ticks_an_any_monster_objective_once()
    {
        var hero = Hero("Any Hunter");
        var quest = MercContract("faith_escort_pilgrim", hero);
        Kill(hero, "Bandit Scout");
        quest.Objectives.Single().CurrentProgress.Should().Be(1);
    }

    [Theory]
    [InlineData("faith_purge_undead", 30, 9, 7)]
    [InlineData("crown_warband_breaker", 30, 11, 7)]
    [InlineData("crown_guard_relief", 12, 12, 7)]
    public void Other_contracts_keep_their_count_and_days(string id, int level, int required, int days)
    {
        var hero = Hero("Pin " + id);
        hero.Level = level;
        var quest = MercContract(id, hero);
        quest.Objectives.Single().RequiredProgress.Should().Be(required, "base count + level / 5");
        quest.DaysToComplete.Should().Be(days);
    }

    [Fact]
    public void The_bandit_contract_text_sends_the_player_to_the_wilderness_in_every_language()
    {
        foreach (var lang in new[] { "en", "es", "fr", "it", "hu" })
        {
            string place = Loc.GetIn(lang, "location.wilderness");
            foreach (var key in new[] { "merc.contract.crown_bandit_purge.comment", "merc.contract.crown_bandit_purge.objective" })
            {
                string text = Loc.GetIn(lang, key, 3);
                text.Should().NotBe(key);
                text.ToLowerInvariant().Should().Contain(lang == "hu" ? "vadon" : place.ToLowerInvariant().Replace("the ", "").Replace("la ", "").Replace("le ", ""),
                    $"{lang} {key} names the wilderness");
            }
        }
        foreach (var lang in new[] { "en", "hu" })
        {
            ("  " + Loc.GetIn(lang, "merc.contract.crown_bandit_purge.comment", 3)).Length.Should().BeLessThanOrEqualTo(79, lang);
            ("    " + Loc.GetIn(lang, "merc.contract.crown_bandit_purge.objective", 3)).Length.Should().BeLessThanOrEqualTo(79, lang);
        }
    }

    // ---------- 2. the Prison buff on room and chest traps ----------

    private static async Task<(long hp, long gold, long xp, int charges)> RoomTrap(int seed, bool buff)
    {
        var hero = Hero("Trap " + seed);
        if (buff) GivePrisonBuff(hero);
        var (d, _) = Dungeon(hero, 20, seed);
        await Run(d, "TriggerTrap", new DungeonRoom { Id = "room_1", HasTrap = true });
        return (100000 - hero.HP, 100000 - hero.Gold, 1000000 - hero.Experience, hero.SettlementBuffCombats);
    }

    private static async Task<(long hp, long gold, long xp, int charges)> ChestTrap(int seed, bool buff)
    {
        var hero = Hero("Chest " + seed);
        if (buff) GivePrisonBuff(hero);
        var (d, _) = Dungeon(hero, 20, seed, "O");
        await Run(d, "TreasureChestEncounter");
        return (100000 - hero.HP, 100000 - hero.Gold, 1000000 - hero.Experience, hero.SettlementBuffCombats);
    }

    /// <summary>Seeds whose first draw (the chest roll) is a trap, 7 or 8.</summary>
    private static IEnumerable<int> ChestTrapSeeds() => Enumerable.Range(1, 400).Where(s => new Random(s).Next(10) is 7 or 8).Take(16);

    [Fact]
    public async Task Room_trap_damage_is_halved_with_the_Prison_buff()
    {
        int damaged = 0;
        for (int seed = 1; seed <= 40; seed++)
        {
            var plain = await RoomTrap(seed, false);
            var held = await RoomTrap(seed, true);
            held.hp.Should().Be(plain.hp / 2, $"seed {seed}: the buff halves the damage");
            held.gold.Should().Be(plain.gold, $"seed {seed}: gold is not damage");
            held.xp.Should().Be(plain.xp, $"seed {seed}: experience is not damage");
            held.charges.Should().Be(GameConfig.SettlementBuffDuration - (plain.hp > 0 ? 1 : 0), $"seed {seed}: a halved trap spends one use");
            if (plain.hp > 0) damaged++;
        }
        damaged.Should().BeGreaterThan(5, "the seeds reach the damaging traps");
    }

    [Fact]
    public async Task Chest_trap_damage_is_halved_with_the_Prison_buff()
    {
        int damaged = 0;
        foreach (int seed in ChestTrapSeeds())
        {
            var plain = await ChestTrap(seed, false);
            var held = await ChestTrap(seed, true);
            held.hp.Should().Be(plain.hp / 2, $"seed {seed}: the buff halves the damage");
            held.gold.Should().Be(plain.gold, $"seed {seed}: the acid takes gold, not HP");
            held.charges.Should().Be(GameConfig.SettlementBuffDuration - (plain.hp > 0 ? 1 : 0), $"seed {seed}");
            if (plain.hp > 0) damaged++;
        }
        damaged.Should().BeGreaterThan(3, "the seeds reach the damaging chest traps");
    }

    // HP, gold and experience lost on floor 20 without the buff, as the code at 9e3eac82 gives them
    private static readonly string RoomTrapPin =
        "1:64/0/0 2:81/0/0 3:0/0/742 4:0/-400/0 5:42/0/0 6:0/3000/0 7:0/-400/0 8:63/0/0 9:80/0/0 10:0/0/742 11:64/0/0 12:81/0/0 13:0/3000/0 14:0/0/0 15:42/0/0 16:0/3000/0 17:0/0/742 18:63/0/0 19:80/0/0 20:0/0/742 21:0/-400/0 22:41/0/0 23:0/3000/0 24:0/-400/0 25:62/0/0 26:91/0/0 27:0/0/742 28:63/0/0 29:40/0/0 30:0/3000/0 31:0/-400/0 32:41/0/0 33:0/3000/0 34:0/0/742 35:0/0/0 36:91/0/0 37:0/0/742 38:0/-400/0 39:40/0/0 40:0/3000/0";
    private static readonly string ChestTrapPin =
        "2:100/0/0 4:160/0/0 6:0/10000/0 23:0/10000/0 25:100/0/0 27:160/0/0 29:100/0/0 44:160/0/0 46:0/10000/0 48:100/0/0 50:0/10000/0 52:100/0/0 67:160/0/0 69:0/10000/0 71:160/0/0 73:0/10000/0";

    [Fact]
    public async Task Trap_losses_without_the_buff_are_unchanged()
    {
        var room = new List<string>();
        for (int seed = 1; seed <= 40; seed++) { var r = await RoomTrap(seed, false); room.Add($"{seed}:{r.hp}/{r.gold}/{r.xp}"); }
        var chest = new List<string>();
        foreach (int seed in ChestTrapSeeds()) { var r = await ChestTrap(seed, false); chest.Add($"{seed}:{r.hp}/{r.gold}/{r.xp}"); }
        string.Join(" ", room).Should().Be(RoomTrapPin);
        string.Join(" ", chest).Should().Be(ChestTrapPin);
    }

    // ---------- 3. Herbalist Hut and Mystic Circle ----------

    private static readonly HerbType[] Herbs = { HerbType.HealingHerb, HerbType.IronbarkRoot, HerbType.FirebloomPetal, HerbType.Swiftthistle, HerbType.StarbloomEssence };

    private static void SetHerbs(Character hero, Func<HerbType, int> count)
    {
        hero.HerbHealing = count(HerbType.HealingHerb);
        hero.HerbIronbark = count(HerbType.IronbarkRoot);
        hero.HerbFirebloom = count(HerbType.FirebloomPetal);
        hero.HerbSwiftthistle = count(HerbType.Swiftthistle);
        hero.HerbStarbloom = count(HerbType.StarbloomEssence);
    }

    private static int Max(HerbType h) => GameConfig.HerbMaxCarry[(int)h];

    [Fact]
    public async Task The_herbalist_refuses_without_spending_the_day_when_every_herb_is_at_its_limit()
    {
        var hero = Hero("Full Pouch");
        SetHerbs(hero, Max);
        await Run(Settlement(hero), "UseHerbalistHutService");
        hero.SettlementHerbClaimedToday.Should().BeFalse("nothing was given");
        Herbs.All(h => hero.GetHerbCount(h) == Max(h)).Should().BeTrue();
        _lastScreen!.Text.Should().Contain(Loc.Get("settlement.herbalist_all_full"));
    }

    private Screen? _lastScreen;
    private SettlementLocation Settlement(Character hero)
    {
        var loc = new SettlementLocation();
        _lastScreen = At(loc, hero);
        return loc;
    }

    [Fact]
    public async Task The_herbalist_gives_a_herb_below_its_limit_on_the_first_try()
    {
        for (int i = 0; i < 25; i++)
        {
            var hero = Hero("One Gap " + i);
            SetHerbs(hero, h => h == HerbType.Swiftthistle ? Max(h) - 1 : Max(h));
            await Run(Settlement(hero), "UseHerbalistHutService");
            hero.GetHerbCount(HerbType.Swiftthistle).Should().Be(Max(HerbType.Swiftthistle), $"try {i}: the only herb that fits is given");
            hero.SettlementHerbClaimedToday.Should().BeTrue($"try {i}: the day is spent");
        }
    }

    [Fact]
    public async Task The_herbalist_still_gives_one_herb_a_day_and_refuses_a_second()
    {
        var hero = Hero("Empty Pouch");
        SetHerbs(hero, _ => 0);
        await Run(Settlement(hero), "UseHerbalistHutService");
        Herbs.Sum(h => hero.GetHerbCount(h)).Should().Be(1);
        hero.SettlementHerbClaimedToday.Should().BeTrue();

        await Run(Settlement(hero), "UseHerbalistHutService");
        Herbs.Sum(h => hero.GetHerbCount(h)).Should().Be(1, "once a day");
        _lastScreen!.Text.Should().Contain(Loc.Get("settlement.herbalist_claimed"));
    }

    [Theory]
    [InlineData(CharacterClass.Cleric)]
    [InlineData(CharacterClass.Warrior)]
    public async Task The_mystic_circle_refuses_without_spending_the_day_when_full(CharacterClass cls)
    {
        var hero = Hero("Full " + cls, cls);
        hero.MaxMana = 200; hero.Mana = 200;
        await Run(Settlement(hero), "UseMysticCircleService");
        hero.SettlementCircleUsedToday.Should().BeFalse("nothing was restored");
        hero.Mana.Should().Be(200);
        hero.HP.Should().Be(hero.MaxHP);
        _lastScreen!.Text.Should().Contain(Loc.Get(cls == CharacterClass.Cleric ? "settlement.mana_already_full" : "settlement.already_full_hp"));
    }

    [Fact]
    public async Task The_mystic_circle_restores_and_spends_the_day_when_not_full()
    {
        var hero = Hero("Low Mana", CharacterClass.Cleric);
        hero.MaxMana = 200; hero.Mana = 10;
        await Run(Settlement(hero), "UseMysticCircleService");
        hero.SettlementCircleUsedToday.Should().BeTrue();
        hero.Mana.Should().Be(10 + (long)(200 * GameConfig.SettlementManaRestorePercent));

        await Run(Settlement(hero), "UseMysticCircleService");
        _lastScreen!.Text.Should().Contain(Loc.Get("settlement.mystic_depleted"));
    }

    [Fact]
    public void The_herbalist_refusal_exists_in_every_language_and_fits_79_columns()
    {
        foreach (var lang in new[] { "en", "es", "fr", "it", "hu" })
            Loc.GetIn(lang, "settlement.herbalist_all_full").Should().NotBe("settlement.herbalist_all_full", lang);
        foreach (var lang in new[] { "en", "hu" })
            Loc.GetIn(lang, "settlement.herbalist_all_full").Length.Should().BeLessThanOrEqualTo(79, lang);
    }

    // ---------- 4. riddles ----------

    private static readonly FieldInfo RiddleInstance = typeof(RiddleDatabase).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Run a dungeon riddle on the floor with a seeded riddle draw; returns the riddle drawn and the HP lost.</summary>
    private static async Task<(Riddle riddle, long lost, int charges, string text)> RiddleRun(int seed, int floor, bool buff, params string[] answers)
    {
        var saved = RiddleInstance.GetValue(null);
        try
        {
            int difficulty = Math.Min(5, 1 + floor / 20);
            var probe = new RiddleDatabase();
            typeof(RiddleDatabase).GetField("random", F)!.SetValue(probe, new Random(seed));
            var riddle = probe.GetRandomRiddle(difficulty, null);

            var db = new RiddleDatabase();
            typeof(RiddleDatabase).GetField("random", F)!.SetValue(db, new Random(seed));

            var hero = Hero("Riddler " + seed);
            if (buff) GivePrisonBuff(hero);
            var (d, s) = Dungeon(hero, floor, seed, answers);
            await Run(d, "RiddleEncounter", hero);
            return (riddle, 100000 - hero.HP, hero.SettlementBuffCombats, s.Text);
        }
        finally { RiddleInstance.SetValue(null, saved); }
    }

    private static readonly string[] Wrong = { "no such answer", "no such answer", "no such answer" };

    [Theory]
    [InlineData(1, 50)]
    [InlineData(2, 50)]
    [InlineData(3, 90)]
    [InlineData(4, 10)]
    public async Task A_failed_riddle_charges_its_own_damage_once(int seed, int floor)
    {
        var (riddle, lost, _, text) = await RiddleRun(seed, floor, false, Wrong);
        text.Should().Contain(Loc.Get("riddle.failure_wit"), "the riddle was failed");
        lost.Should().Be(riddle.FailureDamage, "one charge, the one the failure line names");
        text.Should().NotContain(Loc.Get("dungeon.trap_activates", riddle.FailureDamage * floor / 5));
    }

    [Theory]
    [InlineData(1, 50)]
    [InlineData(5, 30)]
    public async Task The_Prison_buff_halves_the_riddle_charge(int seed, int floor)
    {
        var (riddle, lost, charges, _) = await RiddleRun(seed, floor, true, Wrong);
        lost.Should().Be((int)(riddle.FailureDamage * (1f - GameConfig.SettlementTrapResist)));
        charges.Should().Be(GameConfig.SettlementBuffDuration - 1);
    }

    [Theory]
    [InlineData(1, 50)]
    [InlineData(6, 80)]
    public async Task Fleeing_a_riddle_costs_nothing(int seed, int floor)
    {
        var (riddle, lost, _, text) = await RiddleRun(seed, floor, false, "flee");
        riddle.FleeAllowed.Should().BeTrue();
        text.Should().Contain(Loc.Get("riddle.flee_allowed"));
        lost.Should().Be(0, "a riddle that allows fleeing lets you go");
    }

    [Fact]
    public async Task A_solved_riddle_costs_nothing()
    {
        var saved = RiddleInstance.GetValue(null);
        Riddle riddle;
        try
        {
            var probe = new RiddleDatabase();
            typeof(RiddleDatabase).GetField("random", F)!.SetValue(probe, new Random(7));
            riddle = probe.GetRandomRiddle(3, null);
        }
        finally { RiddleInstance.SetValue(null, saved); }
        var (_, lost, _, _) = await RiddleRun(7, 50, false, riddle.Answer);
        lost.Should().Be(0);
    }
}
