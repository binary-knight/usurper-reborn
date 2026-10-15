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
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8 difficulty pass, dual-wield per hand (user: "Per hand (Recommended)", "Apply in PvP too
/// (Recommended)"). WeapPow sums both hand items; each swing now reads only its own hand's weapon
/// plus the shared power, abilities read the main hand plus half the off hand. Live paths only.
/// "Equals" rows compare two characters whose hands differ only where the rule says a swing must
/// not look, on engines with the same seed, so every draw lines up.
/// </summary>
[Collection("SharedGameSingletons")]
public class DualWieldPerHand128Tests : IDisposable
{
    // 1.2.8 (the player to-hit roll): these rows read the damage of each hand with fixed draws, so the roll is
    // switched off for them (a fixed lowest draw would be a natural 1 on every swing)
    private readonly bool _savedHitRoll = GameConfig.PlayerHitRollEnabled;
    public DualWieldPerHand128Tests() { GameConfig.PlayerHitRollEnabled = false; }
    public void Dispose() { GameConfig.PlayerHitRollEnabled = _savedHitRoll; }

    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const long Shared = 20;   // BonusWeapPow: not a hand item, so both hands keep it

    // ---------- harness ----------

    private sealed class Recorder : ICombatObserver
    {
        public readonly List<CombatEvent> Events = new();
        public void OnFightStart(CombatFightStart start) { }
        public void OnEvent(CombatEvent e) { if (!e.IsUse && e.Amount > 0) Events.Add(e); }
        public void OnFightEnd(long playerHpEnd) { }
    }

    /// <summary>Every draw is fixed: Next(min, max) is min, Next(max) is 0 (low) or max - 1.</summary>
    private sealed class FixedRandom : Random
    {
        private readonly bool _low;
        public FixedRandom(bool low) { _low = low; }
        public override int Next() => 0;
        public override int Next(int maxValue) => _low || maxValue <= 0 ? 0 : maxValue - 1;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0.5;
        protected override double Sample() => 0.5;
    }

    private static int Weapon(int power, WeaponType type = WeaponType.Sword, WeaponHandedness hand = WeaponHandedness.OneHanded) =>
        EquipmentDatabase.RegisterDynamic(new Equipment
        {
            Name = $"Test {type} {power}", Slot = EquipmentSlot.MainHand, WeaponType = type, Handedness = hand,
            WeaponPower = power, MinLevel = 1, Rarity = EquipmentRarity.Common,
        });

    private static int Shield(int weaponPower = 0) => EquipmentDatabase.RegisterDynamic(new Equipment
    {
        Name = $"Test Kite Shield {weaponPower}", Slot = EquipmentSlot.OffHand, WeaponType = WeaponType.Shield,
        Handedness = WeaponHandedness.OffHandOnly, ShieldBonus = 4, BlockChance = 10, WeaponPower = weaponPower,
        MinLevel = 1, Rarity = EquipmentRarity.Common,
    });

    private static Character Hero(int? main, int? off, CharacterClass cls = CharacterClass.Assassin, int level = 30,
        long str = 116, bool shield = false, CharacterAI ai = CharacterAI.Human, string name = "Tester", int shieldPower = 0)
    {
        var c = new Character
        {
            Name1 = name, Name2 = name, AI = ai, Class = cls, Race = CharacterRace.Human, Sex = CharacterSex.Male,
            Age = 25, Level = level, HP = 5000, MaxHP = 5000, BaseMaxHP = 5000,
            Strength = str, BaseStrength = str, Charisma = str, BaseCharisma = str,
            Dexterity = 20, BaseDexterity = 20, Agility = 10, BaseAgility = 10,
            Constitution = 30, BaseConstitution = 30, Intelligence = 20, BaseIntelligence = 20,
            Wisdom = 20, BaseWisdom = 20, Stamina = 100, BaseStamina = 100, Mental = 80,
            BonusWeapPow = Shared, CombatSpeed = CombatSpeed.Instant,
        };
        if (main.HasValue) c.EquippedItems[EquipmentSlot.MainHand] = Weapon(main.Value);
        if (off.HasValue) c.EquippedItems[EquipmentSlot.OffHand] = Weapon(off.Value);
        if (shield) c.EquippedItems[EquipmentSlot.OffHand] = Shield(shieldPower);
        c.RecalculateStats();
        c.HP = c.MaxHP;
        return c;
    }

    private static Monster Dummy() => new Monster
    {
        Name = "Training Dummy", Level = 30, HP = 1_000_000_000L, MaxHP = 1_000_000_000L, Strength = 1, Defence = 0,
        ArmPow = 0, Experience = 1, Gold = 0, IsActive = true,
    };

    private sealed class Rig
    {
        public CombatEngine Engine = null!;
        public Recorder Rec = new();
        public CombatResult Result = null!;
        public List<Monster> Monsters = new() { Dummy() };
        public long Hits(CombatSide side) => Rec.Events.Where(e => e.Actor == side && e.Target == CombatSide.Monster).Sum(e => e.Amount);
        public List<long> Amounts(CombatSide side) => Rec.Events.Where(e => e.Actor == side && e.Target == CombatSide.Monster).Select(e => e.Amount).ToList();
    }

    private static Rig NewRig(Character player, Random rng, List<Character>? teammates = null)
    {
        var r = new Rig { Engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream())) };
        typeof(CombatEngine).GetField("random", F)!.SetValue(r.Engine, rng);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(r.Engine, player);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(r.Engine, teammates ?? new List<Character>());
        r.Result = new CombatResult { Player = player, Teammates = teammates ?? new List<Character>() };
        typeof(CombatEngine).GetMethod("BeginFightEvents", F)!.Invoke(r.Engine, new object[] { r.Result });
        r.Engine.Observer = r.Rec;
        return r;
    }

    private static Task Call(CombatEngine engine, string method, params object?[] args) =>
        (Task)typeof(CombatEngine).GetMethod(method, F)!.Invoke(engine, args)!;

    private static long SwingDamage(CombatEngine engine, Character player, bool offHand) =>
        (long)typeof(CombatEngine).GetMethod("ComputePlayerSwingDamage", F)!.Invoke(engine, new object[] { player, Dummy(), offHand, 0, 1 })!;

    // ---------- B1-B3: the helpers ----------

    [Fact]
    public void B1_MainHandSwing_ReadsSharedPlusTheMainItem()
    {
        var c = Hero(140, 133);
        c.IsDualWielding.Should().BeTrue();
        c.WeapPow.Should().Be(Shared + 140 + 133);
        c.SharedWeapPow.Should().Be(Shared);
        c.HandWeapPow(EquipmentSlot.MainHand).Should().Be(140);
        c.SwingWeapPow(offHand: false).Should().Be(Shared + 140);
    }

    [Fact]
    public void B2_OffHandSwing_ReadsSharedPlusTheOffItem()
    {
        var c = Hero(140, 133);
        c.HandWeapPow(EquipmentSlot.OffHand).Should().Be(133);
        c.SwingWeapPow(offHand: true).Should().Be(Shared + 133);
    }

    [Fact]
    public void B3_NotDualWielding_EveryHelperReturnsWeapPow()
    {
        var setups = new (string what, Character c)[]
        {
            ("sword and a shield with weapon power", Hero(140, null, shield: true, shieldPower: 7)),
            ("sword alone", Hero(140, null)),
            ("unarmed", Hero(null, null)),
        };
        var two = Hero(null, null);
        two.EquippedItems[EquipmentSlot.MainHand] = Weapon(150, WeaponType.Greatsword, WeaponHandedness.TwoHanded);
        two.RecalculateStats();
        var bow = Hero(null, null);
        bow.EquippedItems[EquipmentSlot.MainHand] = Weapon(90, WeaponType.Bow, WeaponHandedness.TwoHanded);
        bow.RecalculateStats();
        foreach (var (what, c) in setups.Append(("two-hander", two)).Append(("bow", bow)))
        {
            c.IsDualWielding.Should().BeFalse(what);
            c.SharedWeapPow.Should().Be(c.WeapPow, what);
            c.SwingWeapPow(false).Should().Be(c.WeapPow, what);
            c.SwingWeapPow(true).Should().Be(c.WeapPow, what);
            c.AbilityWeapPow.Should().Be(c.WeapPow, what);
        }
        setups[0].c.WeapPow.Should().Be(Shared + 140 + 7, "the shield's weapon power stays in");
        two.WeapPow.Should().Be(Shared + 150);
    }

    [Fact]
    public void DisarmedDualWielder_EachHandLosesTheSameShare()
    {
        // A PvP disarm halves WeapPow itself (CombatEngine, restored at the end of the fight).
        var c = Hero(140, 140);
        c.WeapPow = c.WeapPow / 2;   // 300 -> 150
        c.SwingWeapPow(false).Should().Be(75);
        c.SwingWeapPow(true).Should().Be(75);
        c.AbilityWeapPow.Should().Be(150 * 210 / 280);
    }

    // ---------- B4: the ability weapon bonus ----------

    [Fact]
    public void B4_AbilityWeaponBonus_IsSharedPlusMainPlusHalfTheOff()
    {
        var dual = Hero(140, 133, CharacterClass.Warrior);
        dual.AbilityWeapPow.Should().Be(Shared + 140 + 133 / 2);
        var same = Hero(140 + 133 / 2, null, CharacterClass.Warrior);   // one sword of exactly that power
        same.WeapPow.Should().Be(dual.AbilityWeapPow);
        for (int seed = 1; seed <= 20; seed++)
        {
            var a = ClassAbilitySystem.UseAbility(dual, "power_strike", new Random(seed));
            var b = ClassAbilitySystem.UseAbility(same, "power_strike", new Random(seed));
            a.Damage.Should().BeGreaterThan(0);
            a.Damage.Should().Be(b.Damage, $"seed {seed}");
        }
    }

    // ---------- B5: the player's ability off-hand follow-up ----------

    [Fact]
    public async Task B5_AbilityOffHandFollowUp_IsTheOffHandSwing()
    {
        var hero = Hero(1000, 10, CharacterClass.Warrior);
        var rig = NewRig(hero, new FixedRandom(low: false));
        var ability = ClassAbilitySystem.UseAbility(hero, "power_strike", new FixedRandom(low: false));
        await Call(rig.Engine, "ApplyAbilityEffectsMultiMonster", hero, rig.Monsters[0], rig.Monsters, ability, rig.Result);
        var hits = rig.Amounts(CombatSide.Player);
        hits.Should().HaveCount(2, "the ability hit, then the off-hand follow-up");
        long offSwing = SwingDamage(NewRig(hero, new FixedRandom(low: false)).Engine, hero, offHand: true);
        long mainAtHalf = SwingDamage(NewRig(hero, new FixedRandom(low: false)).Engine, Hero(1000, 1000, CharacterClass.Warrior), offHand: true);
        hits[1].Should().Be(offSwing);
        hits[1].Should().BeLessThan(mainAtHalf / 2, "a 10-power off hand is far below a 1000-power one");
    }

    [Fact]
    public async Task TeammateAbilityOffHandFollowUp_ReadsTheOffHand()
    {
        async Task<List<long>> Hits(int main, int off)
        {
            var leader = Hero(10, null, name: "Leader");
            var mate = Hero(main, off, CharacterClass.Warrior, ai: CharacterAI.Computer, name: "Mate");
            var rig = NewRig(leader, new FixedRandom(low: false), new List<Character> { mate });
            var ability = ClassAbilitySystem.UseAbility(mate, "power_strike", new FixedRandom(low: false));
            await Call(rig.Engine, "ApplyAbilityEffectsMultiMonster", mate, rig.Monsters[0], rig.Monsters, ability, rig.Result);
            return rig.Amounts(CombatSide.Team);
        }
        var both = await Hits(140, 133);
        both.Should().HaveCount(2, "the ability hit, then the off-hand follow-up");
        (await Hits(50, 133))[1].Should().Be(both[1], "the follow-up does not read the main hand");
        (await Hits(140, 10))[1].Should().BeLessThan(both[1], "the follow-up reads the off hand");
    }

    // ---------- B6: teammate swings ----------

    private static async Task<List<long>> TeammateSwings(int main, int off)
    {
        var leader = Hero(10, null, name: "Leader");
        var mate = Hero(main, off, CharacterClass.Warrior, level: 9, ai: CharacterAI.Computer, name: "Mate");
        leader.TeammateDisabledAbilities[mate.GetSkillToggleKey()] =
            ClassAbilitySystem.GetAvailableAbilities(mate).Select(a => a.Id).ToList();
        var rig = NewRig(leader, new Random(1281), new List<Character> { mate });
        await Call(rig.Engine, "ProcessTeammateActionMultiMonster", mate, rig.Monsters, rig.Result);
        return rig.Amounts(CombatSide.Team);
    }

    [Fact]
    public async Task B6_TeammateDualWield_SwingsPerHand()
    {
        var both = await TeammateSwings(140, 133);
        both.Should().HaveCount(2, "a level 9 Warrior swings once, plus the off hand");
        (await TeammateSwings(140, 50))[0].Should().Be(both[0], "the main-hand swing does not read the off hand");
        (await TeammateSwings(50, 133))[1].Should().Be(both[1], "the off-hand swing does not read the main hand");
        both[1].Should().BeLessThan(both[0]);
    }

    // ---------- B7: Power Attack, Backstab, Smite, Precise Strike ----------

    [Theory]
    [InlineData("ExecutePowerAttackMultiMonster", CharacterClass.Warrior)]
    [InlineData("ExecuteBackstabMultiMonster", CharacterClass.Assassin)]
    [InlineData("ExecuteSmiteMultiMonster", CharacterClass.Paladin)]
    [InlineData("ExecutePreciseStrikeMultiMonster", CharacterClass.Ranger)]
    public async Task B7_SpecialAttacks_ReadMainPlusHalfTheOff(string method, CharacterClass cls)
    {
        async Task<long> FirstHit(Character c)
        {
            var rig = NewRig(c, new FixedRandom(low: true));
            c.SmiteChargesRemaining = 1;   // 1.3.2: a Smite spends a daily charge
            await Call(rig.Engine, method, c, rig.Monsters, (int?)0, rig.Result);
            var hits = rig.Amounts(CombatSide.Player);
            hits.Should().NotBeEmpty(method);
            return hits[0];
        }
        var dual = Hero(140, 133, cls);
        var same = Hero(140 + 133 / 2, null, cls);
        same.WeapPow.Should().Be(dual.AbilityWeapPow);
        (await FirstHit(dual)).Should().Be(await FirstHit(same), method);
        (await FirstHit(dual)).Should().BeLessThan(await FirstHit(Hero(140 + 133, null, cls)), "not the summed power");
    }

    // ---------- B8 and the PvP row ----------

    private static async Task<long> PvPHit(Character attacker, bool offHand)
    {
        var defender = Hero(10, null, name: "Defender");
        defender.HP = defender.MaxHP = 1_000_000;
        defender.Defence = 0;
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        engine.SeedRandomForTests(1282);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, attacker);
        await Call(engine, "ExecutePvPSingleHit", attacker, defender, new CombatResult { Player = attacker }, offHand);
        return 1_000_000 - defender.HP;
    }

    [Fact]
    public async Task B8_PvPSwings_ReadTheirOwnHand()
    {
        long main = await PvPHit(Hero(140, 133), offHand: false);
        long off = await PvPHit(Hero(140, 133), offHand: true);
        (await PvPHit(Hero(140, 50), offHand: false)).Should().Be(main, "the main-hand swing does not read the off hand");
        (await PvPHit(Hero(50, 133), offHand: true)).Should().Be(off, "the off-hand swing does not read the main hand");
        off.Should().BeLessThan(main);
    }

    [Fact]
    public async Task PvP_ComputerOpponentDefaultSwing_ReadsTheMainHand()
    {
        async Task<long> Swing(int main, int off)
        {
            var computer = Hero(main, off, CharacterClass.Warrior, ai: CharacterAI.Computer, name: "Duelist");
            computer.CurrentCombatStamina = 0;   // no ability is affordable, so it swings
            var human = Hero(10, null, name: "Human");
            human.HP = human.MaxHP = 1_000_000;
            human.Defence = 0;
            var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
            engine.SeedRandomForTests(1283);
            typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, human);
            await Call(engine, "ProcessComputerPlayerAction", computer, human, new CombatResult { Player = human });
            return 1_000_000 - human.HP;
        }
        long dual = await Swing(140, 133);
        dual.Should().BeGreaterThan(0);
        (await Swing(140, 1)).Should().Be(dual, "an arena or PvP opponent's swing reads only its main hand");
    }

    // ---------- B9: Warrior level 30, four main swings plus the off hand ----------

    internal static async Task<long> BasicAttackRounds(Character player, int rounds, int seed)
    {
        var rig = NewRig(player, new Random(seed));
        for (int i = 0; i < rounds; i++)
            await Call(rig.Engine, "ProcessPlayerActionMultiMonster",
                new CombatAction { Type = CombatActionType.Attack, TargetIndex = 0 }, player, rig.Monsters, rig.Result);
        return rig.Hits(CombatSide.Player);
    }

    [Fact]
    public async Task B9_WarriorLevel30_DualWieldIsAbout112PercentOfSwordAndShield()
    {
        var dual = Hero(140, 140, CharacterClass.Warrior);
        var board = Hero(140, null, CharacterClass.Warrior, shield: true);
        dual.GetClassCombatModifiers().ExtraAttacks.Should().Be(3);
        double ratio = (double)await BasicAttackRounds(dual, 2000, 1289) / await BasicAttackRounds(board, 2000, 1289);
        StatusSheetSeparators1114Tests.Capture("b9_ratio.txt", $"warrior L30 140+140 vs 140+shield: {ratio:F4}\n");
        ratio.Should().BeInRange(1.08, 1.16);
    }

    // ---------- B10: the character sheet ----------

    private static string Sheet(Character hero)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 20)), output);
        var loc = new MainStreetLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, hero);
        ((Task)typeof(BaseLocation).GetMethod("ShowStatus", F)!.Invoke(loc, Array.Empty<object>())!).GetAwaiter().GetResult();
        return StatusSheetSeparators1114Tests.Plain(term, output);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void B10_Sheet_ShowsEachHandOnlyWhenDualWielding(string lang)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var dual = Hero(4321, 8765);
            string line = Loc.Get("base.style_dual_wield_hands", dual.SwingWeapPow(false), dual.SwingWeapPow(true));
            line.Should().NotContain("base.style_dual_wield_hands", "the key exists in " + lang);
            var text = Sheet(dual);
            StatusSheetSeparators1114Tests.Capture($"b10_sheet_{lang}.txt", text);
            var rows = text.Split('\n').Select(r => r.TrimEnd('\r')).Where(r => r.Contains(line)).ToList();
            rows.Should().ContainSingle();
            rows[0].Length.Should().BeLessThanOrEqualTo(79, rows[0]);
            string marker = Loc.Get("base.style_dual_wield_hands", "\u0001", "\u0002").Split('\u0001')[0];   // the text before {0}
            Sheet(Hero(4321, null, shield: true)).Should().NotContain(marker, "sword and shield");
            Sheet(Hero(4321, null)).Should().NotContain(marker, "one sword");
        }
        finally { GameConfig.Language = prev; }
    }
}
