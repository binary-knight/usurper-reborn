using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 G10 (CP12, Q10, Q18). Rule 1: the Mystic Shaman's weapon enchant riders and the Ancestral Guidance heal
/// run on every live swing of a party member, the player's and the NPC teammate's, from one helper, at the
/// swinger's own enchant, and the heal goes to the swinger and the rest of the party once each; a teammate's
/// enchant counts down its own rounds. In memory only.
/// </summary>
[Collection("SharedGameSingletons")]
public class Shaman136Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int WeaponPower = 100;
    private const int MatePower = 37;     // the teammate's rider: 100 x 37 / 100 = 37
    private const int LeaderPower = 90;   // the leader's own enchant, never the teammate's

    // the swing reads damage with fixed draws, so the to-hit roll is off
    private readonly bool _savedHitRoll = GameConfig.PlayerHitRollEnabled;
    public Shaman136Tests() { GameConfig.PlayerHitRollEnabled = false; }
    public void Dispose() { GameConfig.PlayerHitRollEnabled = _savedHitRoll; }

    // ---------- harness ----------

    private sealed class FixedRandom : Random
    {
        public override int Next() => 0;
        public override int Next(int maxValue) => maxValue == 100 ? 99 : (maxValue <= 0 ? 0 : maxValue - 1);
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0.5;
        protected override double Sample() => 0.5;
    }

    private sealed class Recorder : ICombatObserver
    {
        public readonly List<CombatEvent> Events = new();
        public void OnFightStart(CombatFightStart start) { }
        public void OnEvent(CombatEvent e) { if (!e.IsUse && !e.IsRoll && e.Amount > 0) Events.Add(e); }
        public void OnFightEnd(long playerHpEnd) { }
        public List<long> Hits(CombatSide actor) =>
            Events.Where(e => e.Actor == actor && e.Target == CombatSide.Monster).Select(e => e.Amount).ToList();
    }

    // one dynamic item, named in 13 characters, registered once
    private static readonly Lazy<int> AxeId = new(() => EquipmentDatabase.RegisterDynamic(new Equipment
    {
        Name = "Shaman136 Axe", Slot = EquipmentSlot.MainHand, WeaponType = WeaponType.Axe,
        Handedness = WeaponHandedness.OneHanded, WeaponPower = WeaponPower, MinLevel = 1, Rarity = EquipmentRarity.Common,
    }));

    private static Character Member(string name, CharacterClass cls, CharacterAI ai)
    {
        var c = new Character
        {
            Name1 = name, Name2 = name, AI = ai, Class = cls, Race = CharacterRace.Troll, Sex = CharacterSex.Male,
            Age = 25, Level = 60, Strength = 120, BaseStrength = 120, Dexterity = 20, BaseDexterity = 20,
            Agility = 10, BaseAgility = 10, Constitution = 30, BaseConstitution = 30, Intelligence = 40, BaseIntelligence = 40,
            Wisdom = 20, BaseWisdom = 20, Charisma = 20, BaseCharisma = 20, Stamina = 100, BaseStamina = 100, Mental = 80,
            CombatSpeed = CombatSpeed.Instant,
        };
        c.EquippedItems[EquipmentSlot.MainHand] = AxeId.Value;
        c.RecalculateStats();
        c.MaxHP = 100_000; c.HP = c.MaxHP;
        c.MaxMana = 200; c.Mana = 0; c.ManaPotions = 0; c.Healing = 0;
        return c;
    }

    private static Monster Dummy() => new Monster
    {
        Name = "Training Dummy", Level = 60, HP = 1_000_000_000L, MaxHP = 1_000_000_000L, Strength = 1, Defence = 0,
        ArmPow = 0, Experience = 1, Gold = 0, IsActive = true,
    };

    private sealed class Rig
    {
        public CombatEngine Engine = null!;
        public Character Leader = null!;
        public Character Mate = null!;
        public CombatResult Result = null!;
        public Recorder Rec = new();
        public Monster Target = null!;
    }

    private static Rig NewRig()
    {
        var rig = new Rig
        {
            Leader = Member("Leader", CharacterClass.MysticShaman, CharacterAI.Human),
            Mate = Member("Mate", CharacterClass.MysticShaman, CharacterAI.Computer),
            Target = Dummy(),
        };
        // the mate swings: no ability, no spell, no potion
        rig.Leader.TeammateDisabledAbilities[rig.Mate.GetSkillToggleKey()] =
            ClassAbilitySystem.GetAvailableAbilities(rig.Mate).Select(a => a.Id).ToList();
        var mates = new List<Character> { rig.Mate };
        rig.Engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(rig.Engine, new FixedRandom());
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(rig.Engine, rig.Leader);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(rig.Engine, mates);
        rig.Result = new CombatResult { Player = rig.Leader, Teammates = mates };
        typeof(CombatEngine).GetMethod("BeginFightEvents", F)!.Invoke(rig.Engine, new object[] { rig.Result });
        rig.Engine.Observer = rig.Rec;
        return rig;
    }

    private static void Enchant(Character c, int type, int power, int rounds = 5)
    {
        c.ShamanEnchantType = type; c.ShamanEnchantPower = power; c.ShamanEnchantRounds = rounds;
    }

    private static void Guidance(Character c) { c.ActiveTotemType = 6; c.ActiveTotemRounds = 4; c.ActiveTotemPower = 25; }

    private static async Task MateTurn(Rig rig) =>
        await (Task)typeof(CombatEngine).GetMethod("ProcessTeammateActionMultiMonster", F)!
            .Invoke(rig.Engine, new object[] { rig.Mate, new List<Monster> { rig.Target }, rig.Result })!;

    private static void PlayerOnHit(Rig rig, long damage) =>
        typeof(CombatEngine).GetMethod("ApplyPlayerSwingOnHitEffects", F)!
            .Invoke(rig.Engine, new object[] { rig.Leader, rig.Target, damage, false, rig.Result });

    private static void RoundEnd(Rig rig) =>
        typeof(CombatEngine).GetMethod("RegeneratePartyAtRoundEnd", F)!.Invoke(rig.Engine, new object[] { rig.Leader });

    // ---------- rule 1: the riders on a teammate's swing ----------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Shaman136_R1_ATeammateShaman_SetsOffItsOwnEnchantRider_OnItsOwnSwing(int type)
    {
        var rig = NewRig();
        Enchant(rig.Mate, type, MatePower);
        Enchant(rig.Leader, type, LeaderPower);
        long leaderMana = rig.Leader.Mana;

        await MateTurn(rig);

        var hits = rig.Rec.Hits(CombatSide.Team);
        hits.Should().NotBeEmpty("the teammate swings");
        long rider = WeaponPower * MatePower / 100;
        hits.Count(h => h == rider).Should().BeGreaterThan(0, "the teammate's rider fires at its own power");
        hits.Should().NotContain(WeaponPower * LeaderPower / 100, "never at the leader's power");
        rig.Rec.Hits(CombatSide.Player).Should().BeEmpty("the leader did not swing");
        rig.Leader.Mana.Should().Be(leaderMana);
        if (type == 2) rig.Target.IsSlowed.Should().BeTrue("Frostbrand slows the target");
        if (type == 4) rig.Mate.Mana.Should().BeGreaterThan(0, "Stormstrike restores the swinger's own mana");
    }

    [Fact]
    public async Task Shaman136_R1_ATeammateWithNoEnchant_HasNoRider()
    {
        var rig = NewRig();
        await MateTurn(rig);
        var hits = rig.Rec.Hits(CombatSide.Team);
        hits.Should().NotBeEmpty();
        hits.Should().NotContain(WeaponPower * MatePower / 100);
        rig.Target.IsSlowed.Should().BeFalse();
    }

    [Fact]
    public async Task Shaman136_R1_ATeammateShamanWithGuidance_HealsItselfOnce_AndThePlayer()
    {
        var rig = NewRig();
        Guidance(rig.Mate);
        rig.Mate.HP = rig.Mate.MaxHP - 50_000;
        rig.Leader.HP = rig.Leader.MaxHP - 50_000;
        long mateHp = rig.Mate.HP, leaderHp = rig.Leader.HP;

        await MateTurn(rig);

        var hits = rig.Rec.Hits(CombatSide.Team);
        hits.Should().NotBeEmpty();
        long expected = hits.Sum(h => (long)(h * 25 / 100.0));
        expected.Should().BeGreaterThan(0);
        (rig.Mate.HP - mateHp).Should().Be(expected, "the Shaman who dealt the hit is healed once");
        (rig.Leader.HP - leaderHp).Should().Be(expected, "an NPC Shaman's Guidance reaches the player");
    }

    [Fact]
    public void Shaman136_R1_ThePlayersSwing_IsUnchanged()
    {
        var rig = NewRig();
        Enchant(rig.Leader, 1, LeaderPower);
        PlayerOnHit(rig, 1000);
        rig.Rec.Hits(CombatSide.Player).Should().Equal(new long[] { WeaponPower * LeaderPower / 100 },
            "the player's rider is the player's own enchant");
        rig.Rec.Hits(CombatSide.Team).Should().BeEmpty();
        rig.Leader.ShamanEnchantRounds.Should().Be(5, "the player's rounds count down in the player's own round end");
    }

    [Fact]
    public async Task Shaman136_R1_ATeammatesEnchant_LastsItsOwnRounds()
    {
        var rig = NewRig();
        int rounds = GameConfig.ShamanEnchantDuration;
        Enchant(rig.Mate, 1, MatePower, rounds);
        Enchant(rig.Leader, 1, LeaderPower, rounds);
        for (int r = 1; r < rounds; r++) RoundEnd(rig);
        rig.Mate.ShamanEnchantRounds.Should().Be(1);
        rig.Mate.ShamanEnchantType.Should().Be(1);
        await MateTurn(rig);
        rig.Rec.Hits(CombatSide.Team).Should().Contain(WeaponPower * MatePower / 100, "the last round still fires");

        RoundEnd(rig);
        rig.Mate.ShamanEnchantRounds.Should().Be(0);
        rig.Mate.ShamanEnchantType.Should().Be(0, "the enchant ends at its own rounds");
        rig.Mate.ShamanEnchantPower.Should().Be(0);
        rig.Leader.ShamanEnchantRounds.Should().Be(rounds, "the party step does not touch the leader's own enchant");
        rig.Rec.Events.Clear();
        await MateTurn(rig);
        rig.Rec.Hits(CombatSide.Team).Should().NotBeEmpty();
        rig.Rec.Hits(CombatSide.Team).Should().NotContain(WeaponPower * MatePower / 100, "no rider after the enchant ends");
    }

    [Fact]
    public void Shaman136_R1_OneHelper_BothSwingPathsCallIt_TheDeadPathKeepsItsCopy()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        int Count(string what) { int n = 0, i = 0; while ((i = src.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; } return n; }
        Count("ApplyShamanSwingRiders(player, target, damage, isOffHandAttack, result);").Should().Be(1, "the player's swing");
        Count("ApplyShamanSwingRiders(teammate, target, tmDealt, isOffHandAttack, result);").Should().Be(1, "the teammate's swing");
        Count("combat.shaman_enchant_fire_hit").Should().Be(2, "the helper and the dead single monster path's own copy");
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }
}
