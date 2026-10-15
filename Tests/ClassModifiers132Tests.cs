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
using UsurperRemake.Systems;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.2: the class combat modifiers of Character.GetClassCombatModifiers are read on the live combat path.
/// Warrior AttackBonus on the player's and a teammate's swing; Assassin PoisonChance on a landed melee hit and
/// BackstabMultiplier in [K] Backstab, which only a class with a multiplier above 1 may press and which the
/// Assassin's three dungeon menus show; Barbarian and Mystic Shaman DamageReduction on a monster's basic hit;
/// RageAvailable as the [G] test, [G] kept out of the menus; Paladin AuraBonus on the armour class a monster
/// rolls against and as Defence for teammates, and the [Z] Smite key that spends a daily charge; Ranger
/// RangedBonus on a bow swing, a bow ability and the ranged attack. Every check compares two characters that
/// differ only in class (or in the one number the modifier stands for), on the same seeded draws.
/// </summary>
[Collection("SharedGameSingletons")]
public class ClassModifiers132Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    // ---------------------------------------------------------------- harness

    /// <summary>Every draw is its lowest value: Next(max) is 0, Next(min, max) is min.</summary>
    private sealed class LowRandom : Random
    {
        public override int Next() => 0;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0.0;
        protected override double Sample() => 0.0;
    }

    /// <summary>Next(max) returns the face given; every other draw is its lowest value.</summary>
    private sealed class PercentRandom : Random
    {
        private readonly int _face;
        public PercentRandom(int face) { _face = face; }
        public override int Next() => 0;
        public override int Next(int maxValue) => Math.Min(_face, Math.Max(0, maxValue - 1));
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0.0;
        protected override double Sample() => 0.0;
    }

    private static T Off<T>(Func<T> body)
    {
        bool saved = GameConfig.PlayerHitRollEnabled;
        string lang = GameConfig.Language;
        try { GameConfig.PlayerHitRollEnabled = false; GameConfig.Language = "en"; return body(); }
        finally { GameConfig.PlayerHitRollEnabled = saved; GameConfig.Language = lang; }
    }

    private static async Task<T> OffAsync<T>(Func<Task<T>> body)
    {
        bool saved = GameConfig.PlayerHitRollEnabled;
        string lang = GameConfig.Language;
        try { GameConfig.PlayerHitRollEnabled = false; GameConfig.Language = "en"; return await body(); }
        finally { GameConfig.PlayerHitRollEnabled = saved; GameConfig.Language = lang; }
    }

    private static async Task<T> InLanguage<T>(string lang, Func<Task<T>> body)
    {
        string saved = GameConfig.Language;
        try { GameConfig.Language = lang; return await body(); }
        finally { GameConfig.Language = saved; }
    }

    private static Character Hero(CharacterClass cls, int level = 50, long strength = 300, long dexterity = 30, int? main = null,
        WeaponType mainType = WeaponType.Sword, string name = "Tester")
    {
        var c = G.Hero(50_000, strength, level, cls, dexterity, main, null, mainType, name);
        c.Mana = 0; c.MaxMana = 0;
        return c;
    }

    private static object? Call(CombatEngine engine, string method, params object?[] args)
    {
        try { return typeof(CombatEngine).GetMethod(method, F)!.Invoke(engine, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static async Task CallAsync(CombatEngine engine, string method, params object?[] args) => await (Task)Call(engine, method, args)!;

    private static string Plain(string transcript) => Regex.Replace(transcript, "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
    private static List<string> Rows(string transcript) => Plain(transcript).Split('\n').Select(r => r.TrimEnd()).Where(r => r.Length > 0).ToList();

    private static Monster Foe(int level = 50, long strength = 1, int defence = 0) => new Monster
    {
        Name = "Wolf", TierName = "Wolf", Level = level, HP = 1_000_000_000L, MaxHP = 1_000_000_000L, Strength = strength,
        Defence = defence, Experience = 1, Gold = 0, IsActive = true, CanSpeak = false,
    };

    // ---------------------------------------------------------------- 1. Warrior AttackBonus

    /// <summary>One basic swing's damage, before the monster's armour, on a seeded engine.</summary>
    private static long Swing(Character hero, int seed = 132) => Off(() =>
    {
        var rig = G.NewRig(hero, new Random(seed), null, Foe());
        return (long)Call(rig.Engine, "ComputePlayerSwingDamageRolled", hero, rig.Monsters[0], false, 0, 1, 0)!;
    });

    [Fact]
    public void Warrior_AttackBonus_AddsLevelOverFive_ToThePlayersSwing_AndNotForAnotherClass()
    {
        var warrior = Hero(CharacterClass.Warrior);
        warrior.GetClassCombatModifiers().AttackBonus.Should().Be(10, "level 50 / 5");
        var cleric = Hero(CharacterClass.Cleric);
        // a Cleric holding a temporary attack bonus of 10 swings as the Warrior does with none
        var clericPlusTen = Hero(CharacterClass.Cleric);
        clericPlusTen.TempAttackBonus = 10;
        Swing(Hero(CharacterClass.Magician)).Should().Be(Swing(cleric), "pin: two classes with no attack bonus swing alike");
        Swing(warrior).Should().Be(Swing(clericPlusTen), "the Warrior's Level / 5 is added where the temporary bonus is");
        Swing(warrior).Should().BeGreaterThan(Swing(cleric));
    }

    /// <summary>A teammate's turn with no stamina: it swings once (level 9, no extra attack) at the one foe.</summary>
    private static long TeammateSwing(Character mate) => Off(() =>
    {
        var rig = G.NewRig(Hero(CharacterClass.Cleric), new Random(132), new List<Character> { mate }, Foe(9));
        mate.CurrentCombatStamina = 0;
        long start = rig.Monsters[0].HP;
        G.TeammateTurn(rig, mate);
        return start - rig.Monsters[0].HP;
    });

    private static Character Mate(CharacterClass cls, long strength = 40, int? bow = null)
    {
        var m = G.Mate("Ally One", level: 9, strength: strength);
        m.Class = cls;
        m.Mana = 0; m.MaxMana = 0;
        if (bow.HasValue)
        {
            m.EquippedItems[EquipmentSlot.MainHand] = G.Weapon(bow.Value, WeaponType.Bow, WeaponHandedness.TwoHanded);
            m.RecalculateStats();
            m.HP = m.MaxHP;
        }
        return m;
    }

    [Fact]
    public void Warrior_AttackBonus_AddsToATeammatesSwing()
    {
        Mate(CharacterClass.Warrior).GetClassCombatModifiers().AttackBonus.Should().Be(1, "level 9 / 5");
        TeammateSwing(Mate(CharacterClass.Barbarian)).Should().Be(TeammateSwing(Mate(CharacterClass.Cleric)), "pin: no attack bonus");
        // the teammate swing is Strength + weapon + a random term + the temporary bonus: one more Strength is one more point
        TeammateSwing(Mate(CharacterClass.Warrior)).Should().Be(TeammateSwing(Mate(CharacterClass.Cleric, strength: 41)));
    }

    // ---------------------------------------------------------------- 2. Assassin PoisonChance

    [Fact]
    public void Assassin_PoisonChance_Is5()
    {
        Hero(CharacterClass.Assassin).GetClassCombatModifiers().PoisonChance.Should().Be(5);
        Hero(CharacterClass.Warrior).GetClassCombatModifiers().PoisonChance.Should().Be(0);
    }

    /// <summary>One landed hit's on-hit effects with the percent die showing `face`.</summary>
    private static (bool poisoned, int rounds, string shown) OnHit(Character attacker, int face, bool notMelee = false, bool spell = false)
    {
        var rig = G.NewRig(attacker, new PercentRandom(face), null, Foe());
        var foe = rig.Monsters[0];
        Call(rig.Engine, "ApplyPostHitEnchantments", attacker, foe, 100L, rig.Result, spell, EquipmentSlot.MainHand, notMelee);
        return (foe.Poisoned, foe.PoisonRounds, Plain(rig.Shown()));
    }

    [Fact]
    public void Assassin_PoisonChance_PoisonsALandedMeleeHit_ForThreeRounds_AtFivePercent()
    {
        var hit = OnHit(Hero(CharacterClass.Assassin), 4);
        hit.poisoned.Should().BeTrue("a roll of 4 is under 5");
        hit.rounds.Should().Be(3);
        hit.shown.Should().Contain(Loc.Get("combat.ability_poisoned", "Wolf"));
        OnHit(Hero(CharacterClass.Assassin), 5).poisoned.Should().BeFalse("5 is not under 5");
        OnHit(Hero(CharacterClass.Warrior), 0).poisoned.Should().BeFalse("pin: no other class poisons");
        OnHit(Hero(CharacterClass.Assassin), 0, notMelee: true).poisoned.Should().BeFalse("a ranged hit does not");
        OnHit(Hero(CharacterClass.Assassin), 0, spell: true).poisoned.Should().BeFalse("a spell does not");
        OnHit(Hero(CharacterClass.Assassin, main: 30, mainType: WeaponType.Bow), 0).poisoned.Should().BeFalse("a bow hand does not");
    }

    [Fact]
    public void Assassin_PoisonChance_RidesTheBasicAttack_OnTheLivePath()
    {
        var run = Off(() => G.Repeat(Hero(CharacterClass.Assassin), G.A(CombatActionType.Attack), times: 40, foe: Foe()));
        run.Error.Should().BeNull();
        Plain(run.Transcript).Should().Contain(Loc.Get("combat.ability_poisoned", "Wolf"));
        var other = Off(() => G.Repeat(Hero(CharacterClass.Warrior), G.A(CombatActionType.Attack), times: 40, foe: Foe()));
        Plain(other.Transcript).Should().NotContain(Loc.Get("combat.ability_poisoned", "Wolf"));
    }

    // ---------------------------------------------------------------- 2. Assassin BackstabMultiplier and [K]

    /// <summary>The player's action from the dungeon prompt, typed as `script`, against one foe.</summary>
    private static async Task<(CombatAction action, string shown)> Prompt(Character hero, string script, string lang = "en")
    {
        return await InLanguage(lang, async () =>
        {
            var output = new MemoryStream();
            var term = new TerminalEmulator(new G.ScriptedStream(script), output);
            var engine = new CombatEngine(term);
            var monsters = new List<Monster> { Foe() };
            typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
            typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(engine, hero);
            typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character>());
            var result = new CombatResult { Player = hero, Teammates = new List<Character>(), CombatLog = new List<string>(), CurrentRound = 1 };
            var task = (Task<(CombatAction action, bool enableAutoCombat)>)Call(engine, "GetPlayerActionMultiMonster", hero, monsters, result)!;
            var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(20)));
            done.Should().Be(task, "the prompt must take the scripted keys and return");
            var (action, _) = await task;
            term.StreamWriterInternal?.Flush();
            return (action, Plain(Encoding.UTF8.GetString(output.ToArray())));
        });
    }

    [Fact]
    public async Task Backstab_K_IsTheAssassins_AnotherClassIsAskedAgain()
    {
        var hero = Hero(CharacterClass.Assassin);
        hero.MKills = 5;
        var (action, _) = await Prompt(hero, "K\nA\n");
        action.Type.Should().Be(CombatActionType.Backstab);

        var warrior = Hero(CharacterClass.Warrior);
        warrior.MKills = 5;
        var (other, shown) = await Prompt(warrior, "K\nA\n");
        other.Type.Should().Be(CombatActionType.Attack, "the K is refused and the A that follows is taken");
        shown.Should().Contain(Loc.Get("combat.invalid_action"));
    }

    /// <summary>Backstab's damage on a foe with no armour, every draw low (the success check passes, the die is 1).</summary>
    private static async Task<long> BackstabDamage(Character hero)
    {
        return await OffAsync(async () =>
        {
            var rig = G.NewRig(hero, new LowRandom(), null, Foe());
            long start = rig.Monsters[0].HP;
            await CallAsync(rig.Engine, "ExecuteBackstabMultiMonster", hero, rig.Monsters, (int?)0, rig.Result);
            return start - rig.Monsters[0].HP;
        });
    }

    [Fact]
    public async Task Backstab_ReadsTheClassMultiplier()
    {
        var assassin = Hero(CharacterClass.Assassin);
        assassin.GetClassCombatModifiers().BackstabMultiplier.Should().Be(3.0f);
        Hero(CharacterClass.Warrior).GetClassCombatModifiers().BackstabMultiplier.Should().Be(1.0f);
        long a = await BackstabDamage(assassin);
        long w = await BackstabDamage(Hero(CharacterClass.Warrior));
        a.Should().BeGreaterThan(0);
        // (Strength + weapon power) times the multiplier, plus the die's 1: 3 for the Assassin, 1 for a class without one
        (a - 1).Should().Be(3 * (w - 1), "the Assassin's 3.0 against the default 1.0");
    }

    // ---------------------------------------------------------------- the three dungeon menus

    private static string Menu(Character hero, string kind, bool isFollower = false, string lang = "en")
    {
        string saved = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var output = new MemoryStream();
            var term = new TerminalEmulator(new MemoryStream(), output);
            var engine = new CombatEngine(term);
            var classInfo = new List<(string key, string name, bool available)>();
            Call(engine, "ShowDungeonCombatMenu" + kind, hero, false, false, classInfo, isFollower, GodDomain.None);
            term.StreamWriterInternal?.Flush();
            return Plain(Encoding.UTF8.GetString(output.ToArray()));
        }
        finally { GameConfig.Language = saved; }
    }

    private static readonly string[] Kinds = { "BBS", "ScreenReader", "Standard" };

    /// <summary>The menu shows the key: "[K]" in the boxed and compact menus, "K - " in the screen reader one.</summary>
    private static bool Shows(string menu, string kind, char key) =>
        kind == "ScreenReader" ? Rows(menu).Any(r => r.TrimStart().StartsWith(key + " - ")) : menu.Contains("[" + key + "]");

    [Fact]
    public void Backstab_K_IsShown_InTheAssassinsThreeMenus_AndNoOtherClasss()
    {
        foreach (var kind in Kinds)
        {
            Shows(Menu(Hero(CharacterClass.Assassin), kind), kind, 'K').Should().BeTrue("{0} menu, Assassin", kind);
            foreach (var cls in new[] { CharacterClass.Warrior, CharacterClass.Paladin, CharacterClass.Barbarian, CharacterClass.Ranger })
                Shows(Menu(Hero(cls), kind), kind, 'K').Should().BeFalse("{0} menu, {1}", kind, cls);
            // a group follower's input has no K: the follower's menu does not offer it
            Shows(Menu(Hero(CharacterClass.Assassin), kind, isFollower: true), kind, 'K').Should().BeFalse("{0} menu, follower", kind);
        }
    }

    [Fact]
    public void Rage_G_StaysOutOfTheMenus()
    {
        foreach (var kind in Kinds)
            Shows(Menu(Hero(CharacterClass.Barbarian), kind), kind, 'G').Should().BeFalse("{0} menu", kind);
    }

    [Fact]
    public void Smite_Z_IsShown_ForAPaladinWithACharge_Only()
    {
        foreach (var kind in Kinds)
        {
            var paladin = Hero(CharacterClass.Paladin);
            paladin.SmiteChargesRemaining = 2;
            Shows(Menu(paladin, kind), kind, 'Z').Should().BeTrue("{0} menu, a charge left", kind);
            Menu(paladin, kind).Should().Contain("2", "{0} menu shows the charges", kind);
            paladin.SmiteChargesRemaining = 0;
            Shows(Menu(paladin, kind), kind, 'Z').Should().BeFalse("{0} menu, no charge", kind);
            var warrior = Hero(CharacterClass.Warrior);
            warrior.SmiteChargesRemaining = 2;
            Shows(Menu(warrior, kind), kind, 'Z').Should().BeFalse("{0} menu, Warrior", kind);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    public void TheNewMenuRows_FitTheScreen(string lang)
    {
        foreach (bool full in new[] { false, true })
        {
            var assassin = Hero(CharacterClass.Assassin);
            var paladin = Hero(CharacterClass.Paladin);
            paladin.SmiteChargesRemaining = 12;
            if (full)
                foreach (var h in new[] { assassin, paladin }) { h.PoisonVials = 12; h.Healing = 13; h.ManaPotions = 14; h.HerbHealing = 11; }
            foreach (var kind in Kinds)
                foreach (var (hero, key) in new[] { (assassin, 'K'), (paladin, 'Z') })
                {
                    string menu = Menu(hero, kind, lang: lang);
                    var rows = Rows(menu).Where(r => kind == "ScreenReader" ? r.TrimStart().StartsWith(key + " - ") : r.Contains("[" + key + "]")).ToList();
                    rows.Should().NotBeEmpty("{0} {1} menu shows [{2}]", lang, kind, key);
                    foreach (var row in rows)
                    {
                        row.Length.Should().BeLessOrEqualTo(79, "{0} {1} row \"{2}\" (full {3})", lang, kind, row, full);
                        if (kind == "Standard") row.Length.Should().Be(41, "{0} boxed row \"{1}\" keeps the box", lang, row);
                    }
                }
        }
    }

    // ---------------------------------------------------------------- 3. RageAvailable and [G]

    [Fact]
    public async Task Rage_G_IsTheBarbarians_AnotherClassIsAskedAgain()
    {
        var barbarian = Hero(CharacterClass.Barbarian);
        barbarian.MKills = 5;
        barbarian.GetClassCombatModifiers().RageAvailable.Should().BeTrue();
        (await Prompt(barbarian, "G\nA\n")).action.Type.Should().Be(CombatActionType.Rage);
        var warrior = Hero(CharacterClass.Warrior);
        warrior.MKills = 5;
        warrior.GetClassCombatModifiers().RageAvailable.Should().BeFalse();
        (await Prompt(warrior, "G\nA\n")).action.Type.Should().Be(CombatActionType.Attack);
    }

    [Fact]
    public void Rage_G_ReadsRageAvailable_NotTheClassName()
    {
        string body = GodMiracles1115Tests.Body("Scripts/Systems/CombatEngine.cs",
            "private async Task<(CombatAction action, bool enableAutoCombat)> GetPlayerActionMultiMonster(");
        int g = body.IndexOf("case \"G\":", StringComparison.Ordinal);
        g.Should().BeGreaterThan(0);
        string gCase = body.Substring(g, body.IndexOf("continue;", g, StringComparison.Ordinal) - g);
        gCase.Should().Contain("RageAvailable");
        gCase.Should().NotContain("CharacterClass.Barbarian");
    }

    // ---------------------------------------------------------------- 3. DamageReduction

    /// <summary>The HP one monster basic hit takes from the player, on a seeded engine (no special ability).</summary>
    private static async Task<long> TakenByPlayer(Character hero, List<Character>? team = null, int seed = 132)
    {
        return await OffAsync(async () =>
        {
            var rig = G.NewRig(hero, new Random(seed), team, Foe(50, strength: 400));
            long start = hero.HP;
            await CallAsync(rig.Engine, "ProcessMonsterAction", rig.Monsters[0], hero, rig.Result, rig.Monsters);
            return start - hero.HP;
        });
    }

    private static Character Tank(CharacterClass cls, int tempDefence = 0)
    {
        var c = Hero(cls, dexterity: 1);
        c.Defence = 20; c.ArmPow = 0; c.Agility = 1;
        c.HP = c.MaxHP = 20_000;
        c.TempDefenseBonus = tempDefence;
        return c;
    }

    /// <summary>The first seeds on which the monster lands a hit on a Warrior (so the damage step runs).</summary>
    private static async Task<List<int>> HitSeeds()
    {
        var seeds = new List<int>();
        for (int s = 1; s < 400 && seeds.Count < 6; s++)
            if (await TakenByPlayer(Tank(CharacterClass.Warrior), seed: s) > 0) seeds.Add(s);
        seeds.Should().NotBeEmpty();
        return seeds;
    }

    [Fact]
    public async Task DamageReduction_TakesTwoOffAMonstersHit_OnABarbarianAndAMysticShaman()
    {
        foreach (int s in await HitSeeds())
        {
            long warrior = await TakenByPlayer(Tank(CharacterClass.Warrior), seed: s);
            (await TakenByPlayer(Tank(CharacterClass.Cleric), seed: s)).Should().Be(warrior, "pin: no reduction, seed {0}", s);
            long shielded = await TakenByPlayer(Tank(CharacterClass.Warrior, tempDefence: 2), seed: s);
            shielded.Should().BeLessThan(warrior, "two more Defence takes two off, seed {0}", s);
            (await TakenByPlayer(Tank(CharacterClass.Barbarian), seed: s)).Should().Be(shielded, "Barbarian, seed {0}", s);
            (await TakenByPlayer(Tank(CharacterClass.MysticShaman), seed: s)).Should().Be(shielded, "Mystic Shaman, seed {0}", s);
        }
    }

    /// <summary>The HP one monster basic hit takes from a teammate, the player standing by.</summary>
    private static async Task<long> TakenByTeammate(Character player, Character mate, int seed)
    {
        return await OffAsync(async () =>
        {
            var rig = G.NewRig(player, new Random(seed), new List<Character> { mate }, Foe(9, strength: 300));
            long start = mate.HP;
            await CallAsync(rig.Engine, "MonsterAttacksCompanion", rig.Monsters[0], mate, rig.Result, rig.Monsters);
            return start - mate.HP;
        });
    }

    private static Character Guard(CharacterClass cls, int tempDefence = 0)
    {
        var m = Mate(cls);
        m.HP = m.MaxHP = 20_000;
        m.Defence = 10; m.Agility = 1; m.Dexterity = 1;
        m.TempDefenseBonus = tempDefence;
        return m;
    }

    [Fact]
    public async Task DamageReduction_AndTheAura_ReachTeammates()
    {
        int found = 0;
        for (int s = 1; s < 400 && found < 6; s++)
        {
            long warrior = await TakenByTeammate(Hero(CharacterClass.Cleric), Guard(CharacterClass.Warrior), s);
            if (warrior <= 0) continue;
            found++;
            long shielded = await TakenByTeammate(Hero(CharacterClass.Cleric), Guard(CharacterClass.Warrior, tempDefence: 2), s);
            shielded.Should().BeLessThan(warrior, "seed {0}", s);
            (await TakenByTeammate(Hero(CharacterClass.Cleric), Guard(CharacterClass.Cleric), s)).Should().Be(warrior, "pin, seed {0}", s);
            (await TakenByTeammate(Hero(CharacterClass.Cleric), Guard(CharacterClass.Barbarian), s)).Should().Be(shielded, "Barbarian teammate, seed {0}", s);
            // a Paladin player's aura is two more Defence for a teammate
            (await TakenByTeammate(Hero(CharacterClass.Paladin), Guard(CharacterClass.Warrior), s)).Should().Be(shielded, "Paladin aura, seed {0}", s);
        }
        found.Should().BeGreaterThan(0);
    }

    // ---------------------------------------------------------------- 4. Paladin AuraBonus on the monster's roll

    /// <summary>The DC a monster's roll is shown against, for one basic attack on the player. With a teammate the
    /// monster may turn on the teammate, so the first seed on which it rolls against the player is taken.</summary>
    private static async Task<int> MonsterDC(Func<Character> hero, Func<List<Character>>? team = null)
    {
        return await OffAsync(async () =>
        {
            for (int seed = 132; seed < 432; seed++)
            {
                var player = hero();
                var rig = G.NewRig(player, new Random(seed), team?.Invoke(), Foe(46, strength: 10));
                await CallAsync(rig.Engine, "ProcessMonsterAction", rig.Monsters[0], player, rig.Result, rig.Monsters);
                var m = Regex.Match(Plain(rig.Shown()), @"vs DC (\d+)\]");
                if (m.Success) return int.Parse(m.Groups[1].Value);
            }
            throw new InvalidOperationException("the monster never rolled against the player");
        });
    }

    /// <summary>A level 50 target with Dexterity 70: armour class 10 + 20 + 5 = 35 against the test foe's modifier of 23.</summary>
    private static Character Agile(CharacterClass cls)
    {
        var c = Tank(cls);
        c.Dexterity = 70;
        return c;
    }

    [Fact]
    public async Task Aura_RaisesTheArmourClassAMonsterRollsAgainst_ByTwo()
    {
        int needed = TrainingSystem.CalculatePlayerHitRollAC(Agile(CharacterClass.Warrior)) - (46 / 2 + (10 - 10) / 3);
        needed.Should().BeInRange(3, 14, "the test foe's roll sits inside the at-level window, so +2 is not clamped away");
        int baseDC = await MonsterDC(() => Agile(CharacterClass.Warrior));
        (await MonsterDC(() => Agile(CharacterClass.Cleric))).Should().Be(baseDC, "pin: no aura");
        (await MonsterDC(() => Agile(CharacterClass.Paladin))).Should().Be(baseDC + 2, "a Paladin player");
        (await MonsterDC(() => Agile(CharacterClass.Warrior), () => new List<Character> { Guard(CharacterClass.Paladin) })).Should().Be(baseDC + 2, "a living Paladin teammate");
        Character Fallen() { var f = Guard(CharacterClass.Paladin); f.HP = 0; return f; }
        (await MonsterDC(() => Agile(CharacterClass.Warrior), () => new List<Character> { Fallen() })).Should().Be(baseDC, "a fallen Paladin gives none");
        (await MonsterDC(() => Agile(CharacterClass.Paladin), () => new List<Character> { Guard(CharacterClass.Paladin) })).Should().Be(baseDC + 2, "two Paladins do not stack");
    }

    // ---------------------------------------------------------------- 4. the Smite key and its charges

    [Fact]
    public async Task Smite_Z_IsThePaladinsWithACharge()
    {
        var paladin = Hero(CharacterClass.Paladin);
        paladin.MKills = 5;
        paladin.SmiteChargesRemaining = 1;
        (await Prompt(paladin, "Z\nA\n")).action.Type.Should().Be(CombatActionType.Smite);

        paladin.SmiteChargesRemaining = 0;
        var (spent, shown) = await Prompt(paladin, "Z\nA\n");
        spent.Type.Should().Be(CombatActionType.Attack, "no charge: asked again");
        shown.Should().Contain(Loc.Get("combat.out_of_smites"));

        var warrior = Hero(CharacterClass.Warrior);
        warrior.MKills = 5;
        warrior.SmiteChargesRemaining = 1;
        (await Prompt(warrior, "Z\nA\n")).action.Type.Should().Be(CombatActionType.Attack, "a Warrior has no Smite");
    }

    private static async Task<(long damage, string shown)> SmiteOnce(Character hero)
    {
        return await OffAsync(async () =>
        {
            var rig = G.NewRig(hero, new Random(132), null, Foe());
            long start = rig.Monsters[0].HP;
            await CallAsync(rig.Engine, "ExecuteSmiteMultiMonster", hero, rig.Monsters, (int?)0, rig.Result);
            return (start - rig.Monsters[0].HP, Plain(rig.Shown()));
        });
    }

    [Fact]
    public async Task Smite_SpendsACharge_AndRefusesAtNone()
    {
        var paladin = Hero(CharacterClass.Paladin);
        paladin.SmiteChargesRemaining = 1;
        var first = await SmiteOnce(paladin);
        first.damage.Should().BeGreaterThan(0);
        paladin.SmiteChargesRemaining.Should().Be(0, "the smite spent its charge");
        var second = await SmiteOnce(paladin);
        second.damage.Should().Be(0, "no charge left");
        second.shown.Should().Contain(Loc.Get("combat.out_of_smites"));
        paladin.SmiteChargesRemaining.Should().Be(0);
    }

    // ---------------------------------------------------------------- 5. Ranger RangedBonus

    [Fact]
    public void Ranged_AddsFour_ToABowSwing_OnlyForTheRanger()
    {
        var ranger = Hero(CharacterClass.Ranger, main: 30, mainType: WeaponType.Bow);
        ranger.GetClassCombatModifiers().RangedBonus.Should().Be(4);
        var cleric = Hero(CharacterClass.Cleric, main: 30, mainType: WeaponType.Bow);
        var clericPlusFour = Hero(CharacterClass.Cleric, main: 30, mainType: WeaponType.Bow);
        clericPlusFour.TempAttackBonus = 4;
        Swing(Hero(CharacterClass.Magician, main: 30, mainType: WeaponType.Bow)).Should().Be(Swing(cleric), "pin");
        Swing(ranger).Should().Be(Swing(clericPlusFour), "a Ranger's bow swing gets +4 where the temporary bonus is");
        // no bow, no bonus
        Swing(Hero(CharacterClass.Ranger, main: 30)).Should().Be(Swing(Hero(CharacterClass.Cleric, main: 30)));
    }

    [Fact]
    public void Ranged_AddsFour_ToATeammateRangersBowSwing()
    {
        TeammateSwing(Mate(CharacterClass.Ranger, bow: 30)).Should().Be(TeammateSwing(Mate(CharacterClass.Cleric, strength: 44, bow: 30)));
        TeammateSwing(Mate(CharacterClass.Ranger)).Should().Be(TeammateSwing(Mate(CharacterClass.Cleric)), "no bow, no bonus");
    }

    private static async Task<long> AbilityDamage(Character hero, string abilityId, int damage)
    {
        return await OffAsync(async () =>
        {
            var rig = G.NewRig(hero, new Random(132), null, Foe());
            long start = rig.Monsters[0].HP;
            var used = new ClassAbilityResult { Success = true, AbilityUsed = ClassAbilitySystem.GetAbility(abilityId)!, Damage = damage, SpecialEffect = "" };
            await CallAsync(rig.Engine, "ApplyAbilityEffectsMultiMonster", hero, rig.Monsters[0], rig.Monsters, used, rig.Result);
            return start - rig.Monsters[0].HP;
        });
    }

    [Fact]
    public async Task Ranged_AddsFour_ToABowAbility_OnlyForTheRanger()
    {
        var bowRanger = Hero(CharacterClass.Ranger, main: 30, mainType: WeaponType.Bow);
        var bowCleric = Hero(CharacterClass.Cleric, main: 30, mainType: WeaponType.Bow);
        (await AbilityDamage(bowRanger, "precise_shot", 500)).Should().Be(await AbilityDamage(bowCleric, "precise_shot", 504));
        (await AbilityDamage(Hero(CharacterClass.Ranger), "power_strike", 500)).Should().Be(await AbilityDamage(Hero(CharacterClass.Cleric), "power_strike", 500), "not a bow ability");
    }

    private static async Task<long> RangedShot(Character hero)
    {
        return await OffAsync(async () =>
        {
            var rig = G.NewRig(hero, new Random(132), null, Foe());
            long start = rig.Monsters[0].HP;
            await CallAsync(rig.Engine, "ExecuteRangedAttackMultiMonster", hero, rig.Monsters, (int?)0, rig.Result);
            return start - rig.Monsters[0].HP;
        });
    }

    [Fact]
    public async Task Ranged_AddsFour_ToTheRangedAttack()
    {
        // +4 after the bow's two-handed factor, before the monster's Defence (none here)
        long cleric = await RangedShot(Hero(CharacterClass.Cleric, dexterity: 200, main: 30, mainType: WeaponType.Bow));
        (await RangedShot(Hero(CharacterClass.Magician, dexterity: 200, main: 30, mainType: WeaponType.Bow))).Should().Be(cleric, "pin");
        (await RangedShot(Hero(CharacterClass.Ranger, dexterity: 200, main: 30, mainType: WeaponType.Bow))).Should().Be(cleric + 4);
    }
}
