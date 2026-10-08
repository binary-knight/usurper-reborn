using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8 difficulty pass, DESIGN item 1 (rows D1 to D14): a monster ability rolls from the random it is
/// given, MonsterTactics leaves out wasted picks and adds tier-1 signature abilities from monster level 3,
/// and Predator and Skirmisher families weigh their targets by trait, all behind
/// GameConfig.MonsterTacticsEnabled (default on). Off, and in an Old God fight, every pick and every draw
/// is the one made before.
/// </summary>
[Collection("SharedGameSingletons")]
public class MonsterTactics128Tests
{
    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int Turns = 2000;

    /// <summary>A seeded Random that writes down every draw the engine asks of it.</summary>
    private sealed class RecordingRandom : Random
    {
        public readonly List<string> Calls = new();
        public RecordingRandom(int seed) : base(seed) { }
        public override int Next() { int v = base.Next(); Calls.Add($"Next()={v}"); return v; }
        public override int Next(int maxValue) { int v = base.Next(maxValue); Calls.Add($"Next({maxValue})={v}"); return v; }
        public override int Next(int minValue, int maxValue) { int v = base.Next(minValue, maxValue); Calls.Add($"Next({minValue},{maxValue})={v}"); return v; }
        public override double NextDouble() { double v = base.NextDouble(); Calls.Add($"NextDouble()={v:R}"); return v; }
    }

    private static T WithSwitch<T>(bool on, Func<T> body)
    {
        bool saved = GameConfig.MonsterTacticsEnabled;
        try { GameConfig.MonsterTacticsEnabled = on; return body(); }
        finally { GameConfig.MonsterTacticsEnabled = saved; }
    }

    private static Monster Mon(string name, string family, int level, params string[] abilities) => new Monster
    {
        Name = name, TierName = name, FamilyName = family, Level = level, HP = 100_000, MaxHP = 100_000,
        Strength = 60, WeapPow = 20, IsActive = true, SpecialAbilities = abilities.ToList(),
    };

    private static Character Fighter(string name, CharacterClass cls = CharacterClass.Warrior, long hp = 1_000_000, long maxHp = 1_000_000) => new Character
    {
        Name1 = name.ToLowerInvariant(), Name2 = name, Class = cls, Race = CharacterRace.Human, Level = 20,
        HP = hp, MaxHP = maxHp, ArmPow = 20, Defence = 10, CombatSpeed = CombatSpeed.Instant,
    };

    private sealed record Rig(CombatEngine Engine, Character Hero, MemoryStream Output, CombatResult Result);

    private static Rig NewRig(Random random, params Character[] teammates)
    {
        var output = new MemoryStream();
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), output));
        var hero = Fighter("Hero");
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, random);
        typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(engine, hero);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        var result = new CombatResult { Player = hero, Teammates = teammates.ToList(), CombatLog = new List<string>(), CurrentRound = 2 };
        return new Rig(engine, hero, output, result);
    }

    private static void SetRandom(CombatEngine engine, Random random) => typeof(CombatEngine).GetField("random", F)!.SetValue(engine, random);

    private static bool Special(Rig rig, Monster m) =>
        ((Task<bool>)typeof(CombatEngine).GetMethod("TryMonsterSpecialAbility", F)!.Invoke(rig.Engine, new object?[] { m, rig.Hero, rig.Result, null })!).GetAwaiter().GetResult();

    private static void HitCompanion(Rig rig, Monster m, Character companion) =>
        ((Task)typeof(CombatEngine).GetMethod("MonsterAttacksCompanion", F)!.Invoke(rig.Engine, new object?[] { m, companion, rig.Result, null })!).GetAwaiter().GetResult();

    /// <summary>The engine's target pick: null is the player, as in the engine.</summary>
    private static Character? Target(Rig rig, Monster m, List<Character> team, Random random) =>
        (Character?)typeof(CombatEngine).GetMethod("SelectMonsterTarget", F)!.Invoke(rig.Engine, new object?[] { rig.Hero, team, m, random });

    private static int Weight(Rig rig, Character c) => (int)typeof(CombatEngine).GetMethod("GetTargetWeight", F)!.Invoke(rig.Engine, new object[] { c })!;

    private static string Shown(Rig rig) => Encoding.UTF8.GetString(rig.Output.ToArray());

    /// <summary>The share of 10,000 seeded target picks that land on the teammate.</summary>
    private static double TeammateShare(Rig rig, Monster m, Character teammate, int seed = 128, int picks = 10_000)
    {
        var random = new Random(seed);
        var team = new List<Character> { teammate };
        int hits = 0;
        for (int i = 0; i < picks; i++)
            if (ReferenceEquals(Target(rig, m, team, random), teammate)) hits++;
        return hits / (double)picks;
    }

    // ---------------------------------------------------------------- D1

    private static string AbilityRolls(Random? rng)
    {
        var dragon = Mon("Drake", "Draconic", 30);
        var hero = Fighter("Hero");
        var sb = new StringBuilder();
        for (int i = 0; i < 40; i++)
        {
            var breath = MonsterAbilities.ExecuteAbility(MonsterAbilities.AbilityType.FireBreath, dragon, hero, rng);
            var multi = MonsterAbilities.ExecuteAbility(MonsterAbilities.AbilityType.Multiattack, dragon, hero, rng);
            var spell = MonsterAbilities.ExecuteAbility(MonsterAbilities.AbilityType.Spellcasting, dragon, hero, rng);
            sb.Append(breath.DirectDamage).Append(',').Append(multi.ExtraAttacks).Append(',').Append(spell.InflictStatus).Append(';');
        }
        return sb.ToString();
    }

    [Fact]
    public void D1_ExecuteAbility_RollsFromTheRandomItIsGiven()
    {
        string a = AbilityRolls(new Random(128)), b = AbilityRolls(new Random(128)), c = AbilityRolls(new Random(129));
        b.Should().Be(a, "the same seed gives the same rolls");
        c.Should().NotBe(a, "another seed gives other rolls");
        AbilityRolls(null).Should().NotBeNullOrEmpty("without a random it still rolls, from the static source");
    }

    [Fact]
    public void D1_TheEngineHandsItsRandomToTheAbility_OnlyWhileTacticsAreOn()
    {
        // LowRandom first so the ability chance passes; Multiattack's own roll is Next(1, 3)
        List<string> Draws(bool on) => WithSwitch(on, () =>
        {
            var random = new RecordingRandom(128);
            var rig = NewRig(random);
            var wolf = Mon("Dire Wolf", "Beast", 300, "Multiattack");   // chance 30 + 300 / 5 = 90
            for (int i = 0; i < 40; i++) Special(rig, wolf);
            return random.Calls;
        });
        Draws(true).Should().Contain(c => c.StartsWith("Next(1,3)="), "on, the ability's roll comes from the fight's random");
        Draws(false).Should().NotContain(c => c.StartsWith("Next(1,3)="), "off, it comes from the static source as before");
    }

    [Fact]
    public void D1_TheCompanionPath_HandsTheEngineRandomToTheAbility_OnlyWhileTacticsAreOn()
    {
        List<string> Draws(bool on) => WithSwitch(on, () =>
        {
            var random = new RecordingRandom(128);
            var companion = Fighter("Lyra");
            var rig = NewRig(random, companion);
            var wolf = Mon("Dire Wolf", "Beast", 300, "Multiattack");
            for (int i = 0; i < 40; i++) HitCompanion(rig, wolf, companion);
            return random.Calls;
        });
        Draws(true).Should().Contain(c => c.StartsWith("Next(1,3)="));
        Draws(false).Should().NotContain(c => c.StartsWith("Next(1,3)="));
    }

    // ---------------------------------------------------------------- D2

    private static int Count(string ability, Monster m, Character target, int seed = 128)
    {
        var rng = new Random(seed);
        int n = 0;
        for (int i = 0; i < Turns; i++)
            if (MonsterTactics.Choose(m, target, 2, null, rng) == ability) n++;
        return n;
    }

    [Fact]
    public void D2_SelfHeal_OnlyBelow40PercentHP()
    {
        var hero = Fighter("Hero");
        var angel = Mon("Angel", "Celestial", 25, "Heal", "Flight");
        angel.MaxHP = 1000;
        angel.HP = 900;
        Count("Heal", angel, hero).Should().Be(0, "an Angel at 90% HP does not heal");
        angel.HP = 400;
        Count("Heal", angel, hero).Should().Be(0, "40% is not below 40%");
        angel.HP = 300;
        Count("Heal", angel, hero).Should().BeInRange(900, 1100, "at 30% HP Heal is one of two picks");
        foreach (var heal in new[] { "Regeneration", "Phylactery", "SelfRepair", "Cocoon", "Rebirth", "Sanctuary" })
        {
            var m = Mon("Healer", "Construct", 25, heal);
            m.MaxHP = 1000;
            m.HP = 1000;
            MonsterTactics.Choose(m, hero, 2, null, new Random(1)).Should().BeNull($"{heal} at full HP is a normal attack");
            m.HP = 399;
            MonsterTactics.Choose(m, hero, 2, null, new Random(1)).Should().Be(heal);
        }
    }

    // ---------------------------------------------------------------- D3

    [Fact]
    public void D3_AStatusTheTargetCarries_IsNotAppliedAgain()
    {
        var spider = Mon("Giant Spider", "Insectoid", 6, "WebTrap");
        var hero = Fighter("Hero");
        Count("WebTrap", spider, hero).Should().Be(Turns, "a free player is webbed");
        hero.ApplyStatus(StatusEffect.Stunned, 2);
        Count("WebTrap", spider, hero).Should().Be(0, "a stunned player is not webbed again");
        MonsterTactics.Choose(spider, hero, 2, null, new Random(1)).Should().BeNull("nothing useful is left, so the spider attacks");

        var burning = Fighter("Burning");
        burning.ApplyStatus(StatusEffect.Burning, 3);
        var elemental = Mon("Fire Elemental", "Elemental", 30, "Burn", "Fireball");
        Count("Burn", elemental, burning).Should().Be(0);
        Count("Fireball", elemental, burning).Should().Be(Turns, "an ability with no status stays");
        var lich = Mon("Lich", "Undead", 80, "Spellcasting");
        var cursed = Fighter("Cursed");
        foreach (var s in new[] { StatusEffect.Cursed, StatusEffect.Weakened, StatusEffect.Silenced }) cursed.ApplyStatus(s, 3);
        Count("Spellcasting", lich, cursed).Should().Be(Turns, "Spellcasting rolls its status, so it is never left out");
    }

    [Fact]
    public void D3_ThePlayerPath_UsesTheTactic()
    {
        int Webs(bool on, bool stunned) => WithSwitch(on, () =>
        {
            var rig = NewRig(new Random(128));
            var spider = Mon("Giant Spider", "Insectoid", 300, "WebTrap");   // chance 90
            string web = MonsterAbilities.ExecuteAbility(MonsterAbilities.AbilityType.WebTrap, spider, rig.Hero, new Random(1)).Message;
            int webs = 0;
            for (int i = 0; i < 60; i++)
            {
                rig.Hero.ActiveStatuses.Clear();
                if (stunned) rig.Hero.ApplyStatus(StatusEffect.Stunned, 2);
                rig.Output.SetLength(0);
                bool used = Special(rig, spider);
                bool shown = Shown(rig).Contains(web);
                used.Should().Be(shown, "WebTrap takes the turn when it is used");
                if (shown) webs++;
            }
            return webs;
        });
        Webs(on: true, stunned: false).Should().BeGreaterThan(30, "a free player is webbed at the 90% ability chance");
        Webs(on: true, stunned: true).Should().Be(0, "a stunned player gets the spider's normal attack");
        Webs(on: false, stunned: true).Should().BeGreaterThan(30, "off, the pick is uniform as before");
    }

    /// <summary>The table MonsterTactics reads is the status each handler really sets.</summary>
    [Fact]
    public void D3_TheStatusTable_MatchesEveryAbilityHandler()
    {
        var rolled = new[] { MonsterAbilities.AbilityType.Spellcasting, MonsterAbilities.AbilityType.RealityBreak };
        foreach (var ability in Enum.GetValues<MonsterAbilities.AbilityType>())
        {
            if (rolled.Contains(ability)) { MonsterTactics.StatusOf.Should().NotContainKey(ability); continue; }
            var m = Mon("Any", "Beast", 30);
            m.HP = 1;   // low HP, so a conditional handler (Rebirth) takes its real branch
            var r = MonsterAbilities.ExecuteAbility(ability, m, Fighter("Hero"), new Random(1));
            var expected = MonsterTactics.StatusOf.TryGetValue(ability, out var s) ? s : StatusEffect.None;
            r.InflictStatus.Should().Be(expected, $"{ability}");
        }
    }

    // ---------------------------------------------------------------- D4

    [Fact]
    public void D4_AnEvasionBuff_IsNotCastAgainWhileActive()
    {
        var hero = Fighter("Hero");
        var pixie = Mon("Pixie", "Fey", 5, "Invisibility");
        Count("Invisibility", pixie, hero).Should().Be(Turns);
        pixie.EvasionRounds = 2;
        Count("Invisibility", pixie, hero).Should().Be(0, "the Pixie is already unseen");
        foreach (var buff in new[] { "Flight", "Incorporeal", "PhaseShift", "TreeMeld", "Teleport", "InkCloud" })
        {
            var m = Mon("Evader", "Fey", 30, buff);
            MonsterTactics.Choose(m, hero, 2, null, new Random(1)).Should().Be(buff);
            m.EvasionRounds = 1;
            MonsterTactics.Choose(m, hero, 2, null, new Random(1)).Should().BeNull($"{buff} is active");
        }
    }

    // ---------------------------------------------------------------- D5

    [Fact]
    public void D5_SpentOneShotBuffs_AreSkipped()
    {
        var hero = Fighter("Hero");
        var warlord = Mon("Goblin Warlord", "Goblinoid", 60, "CriticalStrike", "Rally");
        Count("Rally", warlord, hero).Should().BeInRange(900, 1100, "a fresh Warlord rallies half the time");
        MonsterAbilities.ExecuteAbility(MonsterAbilities.AbilityType.Rally, warlord, hero, new Random(1));
        warlord.HasEnraged.Should().BeTrue();
        Count("Rally", warlord, hero).Should().Be(0, "a goblin rallies once");
        Count("CriticalStrike", warlord, hero).Should().Be(Turns);

        var orc = Mon("Orc Warrior", "Orcish", 30, "Rage");
        MonsterTactics.Choose(orc, hero, 2, null, new Random(1)).Should().Be("Rage");
        orc.HasEnraged = true;
        MonsterTactics.Choose(orc, hero, 2, null, new Random(1)).Should().BeNull();

        var armor = Mon("Living Statue", "Construct", 30, "Stoneskin");
        MonsterTactics.Choose(armor, hero, 2, null, new Random(1)).Should().Be("Stoneskin");
        armor.HasHardenedArmor = true;
        MonsterTactics.Choose(armor, hero, 2, null, new Random(1)).Should().BeNull();
    }

    // ---------------------------------------------------------------- D6

    private static int WebsOnCompanion(bool stunned, int seed)
    {
        var companion = Fighter("Lyra");
        var rig = NewRig(new Random(seed), companion);
        var spider = Mon("Giant Spider", "Insectoid", 300, "WebTrap");   // chance 90
        string web = MonsterAbilities.ExecuteAbility(MonsterAbilities.AbilityType.WebTrap, spider, companion, new Random(1)).Message;
        web.Should().NotBeNullOrEmpty();
        int webs = 0;
        for (int i = 0; i < 60; i++)
        {
            companion.ActiveStatuses.Clear();
            if (stunned) companion.ApplyStatus(StatusEffect.Stunned, 2);
            rig.Output.SetLength(0);
            HitCompanion(rig, spider, companion);
            if (Shown(rig).Contains(web)) webs++;
        }
        return webs;
    }

    [Fact]
    public void D6_TheCompanionPath_UsesTheSameTactic()
    {
        WithSwitch(true, () => WebsOnCompanion(stunned: false, seed: 128)).Should().BeGreaterThan(30, "a free companion is webbed at the 90% ability chance");
        WithSwitch(true, () => WebsOnCompanion(stunned: true, seed: 128)).Should().Be(0, "a stunned companion is not webbed again");
        WithSwitch(false, () => WebsOnCompanion(stunned: true, seed: 128)).Should().BeGreaterThan(30, "off, the pick is uniform as before");
    }

    // ---------------------------------------------------------------- D7

    /// <summary>
    /// An Old God fight (BossContext): the boss on the player, an add with a wasted pick on the player and on
    /// a stunned companion, and a Predator add choosing between the player and a wounded companion. Returns
    /// every draw the engine made and what the add did.
    /// </summary>
    private static string OldGodFight(bool on) => WithSwitch(on, () =>
    {
        var random = new RecordingRandom(128);
        var companion = Fighter("Lyra", CharacterClass.Magician, hp: 100, maxHp: 1_000_000);
        var rig = NewRig(random, companion);
        rig.Engine.BossContext = new BossCombatContext { BossData = UsurperRemake.Data.OldGodsData.GetGodBossData(OldGodType.Maelketh) };
        var sb = new StringBuilder();
        try
        {
            var god = new Monster { Name = "Maelketh", Level = 28, HP = 49_500, MaxHP = 49_500, Strength = 100, WeapPow = 50, IsBoss = true, IsActive = true,
                SpecialAbilities = new List<string> { "Cleave", "War Cry", "Shield Bash" } };
            var add = Mon("Spectral Soldier", "Undead", 300, "Heal", "Flight");   // full HP: Heal is a wasted pick
            var webber = Mon("Giant Spider", "Insectoid", 300, "WebTrap");
            for (int i = 0; i < 40; i++)
            {
                int before = random.Calls.Count;
                Special(rig, god);
                sb.Append("god:").Append(string.Join(' ', random.Calls.Skip(before).Take(2))).Append(';');

                add.EvasionRounds = 0;
                before = random.Calls.Count;
                bool used = Special(rig, add);
                sb.Append("add:").Append(used).Append(add.EvasionRounds).Append(string.Join(' ', random.Calls.Skip(before).Take(2))).Append(';');

                companion.ActiveStatuses.Clear();
                companion.ApplyStatus(StatusEffect.Stunned, 2);
                companion.HP = 100;
                before = random.Calls.Count;
                HitCompanion(rig, webber, companion);
                sb.Append("web:").Append(string.Join(' ', random.Calls.Skip(before).Take(2))).Append(';');

                companion.HP = 100;
                var picked = Target(rig, add, new List<Character> { companion }, random);
                sb.Append("target:").Append(picked == null ? "player" : picked.Name2).Append(';');
            }
        }
        finally { rig.Engine.BossContext = null; }
        return sb.ToString();
    });

    [Fact]
    public void D7_AnOldGodFight_KeepsItsOwnPicker()
    {
        string on = OldGodFight(true), off = OldGodFight(false);
        on.Should().Be(off, "with BossContext set, the switch changes no pick and no draw");
        on.Should().Contain("god:Next(100)=").And.Contain(" Next(3)=", "the boss picks among its three phase 1 abilities (SelectBossAbility)");
        on.Should().Contain("add:True0Next(100)=", "the add still picks Heal at full HP, as before");
        on.Should().Contain(" Next(2)=", "the add picks uniformly from its two abilities");
        on.Should().Contain(" Next(1)=0", "the spider still webs a stunned companion");
    }

    // ---------------------------------------------------------------- D8

    [Fact]
    public void D8_APredator_Adds60ToATargetAt35PercentHPOrLess()
    {
        var wolf = Mon("Wolf", "Beast", 5, "PackTactics");
        var companion = Fighter("Lyra", hp: 350, maxHp: 1000);
        var rig = NewRig(new Random(1), companion);
        int wp = Weight(rig, rig.Hero), wc = Weight(rig, companion);
        double plain = wc / (double)(wp + wc), focused = (wc + 60) / (double)(wp + wc + 60);
        (focused - plain).Should().BeGreaterThan(0.05, "the two bands are apart");

        MonsterTactics.TraitBonus(wolf, companion).Should().Be(60, "35% is in");
        WithSwitch(true, () => TeammateShare(rig, wolf, companion)).Should().BeApproximately(focused, 0.02);
        companion.HP = 351;
        MonsterTactics.TraitBonus(wolf, companion).Should().Be(0, "above 35% is out");
        WithSwitch(true, () => TeammateShare(rig, wolf, companion)).Should().BeApproximately(plain, 0.02);
        foreach (var family in new[] { "Beast", "Insectoid", "Shadow", "Undead" })
        {
            companion.HP = 10;
            MonsterTactics.TraitBonus(Mon("Hunter", family, 30), companion).Should().Be(60, family);
        }
        // the player is a target like any other
        rig.Hero.HP = rig.Hero.MaxHP / 4;
        MonsterTactics.TraitBonus(wolf, rig.Hero).Should().Be(60);
    }

    // ---------------------------------------------------------------- D9

    [Fact]
    public void D9_ASkirmisher_Adds50ToCastersAndHealers()
    {
        var goblin = Mon("Goblin", "Goblinoid", 5);
        var mage = Fighter("Lyra", CharacterClass.Magician);
        var rig = NewRig(new Random(1), mage);
        int wp = Weight(rig, rig.Hero), wc = Weight(rig, mage);
        double plain = wc / (double)(wp + wc), focused = (wc + 50) / (double)(wp + wc + 50);
        (focused - plain).Should().BeGreaterThan(0.05);
        WithSwitch(true, () => TeammateShare(rig, goblin, mage)).Should().BeApproximately(focused, 0.02);
        WithSwitch(false, () => TeammateShare(rig, goblin, mage)).Should().BeApproximately(plain, 0.02);

        foreach (var cls in Enum.GetValues<CharacterClass>())
        {
            bool caster = cls is CharacterClass.Magician or CharacterClass.Sage or CharacterClass.Cleric or CharacterClass.Alchemist
                or CharacterClass.Wavecaller or CharacterClass.Abysswarden;
            foreach (var family in new[] { "Goblinoid", "Fey", "Demonic" })
                MonsterTactics.TraitBonus(Mon("Raider", family, 30), Fighter("T", cls)).Should().Be(caster ? 50 : 0, $"{family} against {cls}");
        }
        var wounded = Fighter("Wounded", hp: 1, maxHp: 1000);
        MonsterTactics.TraitBonus(goblin, wounded).Should().Be(0, "a Skirmisher does not hunt the wounded");
    }

    // ---------------------------------------------------------------- D10

    private static string Picks(Rig rig, Monster m, Character teammate, int picks = 10_000)
    {
        var random = new Random(128);
        var team = new List<Character> { teammate };
        var sb = new StringBuilder(picks);
        for (int i = 0; i < picks; i++) sb.Append(Target(rig, m, team, random) == null ? 'P' : 'T');
        return sb.ToString();
    }

    [Fact]
    public void D10_AFamilyWithoutATrait_WeighsAsBefore()
    {
        var ogre = Mon("Ogre", "Giant", 12);
        var mage = Fighter("Lyra", CharacterClass.Magician, hp: 10, maxHp: 1000);   // wounded and a caster: both traits would bite
        var rig = NewRig(new Random(1), mage);
        string on = WithSwitch(true, () => Picks(rig, ogre, mage)), off = WithSwitch(false, () => Picks(rig, ogre, mage));
        on.Should().Be(off, "an Ogre's 10,000 seeded picks are the ones made before the change");
        int wp = Weight(rig, rig.Hero), wc = Weight(rig, mage);
        (on.Count(c => c == 'T') / 10_000.0).Should().BeApproximately(wc / (double)(wp + wc), 0.02);
        foreach (var family in new[] { "Giant", "Orcish", "Draconic", "Elemental", "Construct", "Aquatic", "Celestial", "Aberration", "Ooze", "", "Summoned" })
            MonsterTactics.TraitBonus(Mon("Plain", family, 30), mage).Should().Be(0, family);
    }

    // ---------------------------------------------------------------- D11

    [Fact]
    public void D11_ATaunt_OverridesTheTraits()
    {
        var wolf = Mon("Wolf", "Beast", 5);
        var companion = Fighter("Lyra", hp: 10, maxHp: 1000);
        var rig = NewRig(new Random(1), companion);
        wolf.TauntedBy = rig.Hero.DisplayName;
        wolf.TauntRoundsLeft = 3;
        wolf.TauntStickChance = 100;
        WithSwitch(true, () => TeammateShare(rig, wolf, companion, picks: 1000)).Should().Be(0, "a taunted Wolf always attacks the taunter");

        var tank = Fighter("Aldric");
        var prey = Fighter("Prey", hp: 10, maxHp: 1000);
        wolf.TauntedBy = tank.DisplayName;
        var random = new Random(128);
        var team = new List<Character> { tank, prey };
        WithSwitch(true, () =>
        {
            for (int i = 0; i < 1000; i++) Target(rig, wolf, team, random).Should().BeSameAs(tank);
            return 0;
        });
    }

    // ---------------------------------------------------------------- D12

    [Fact]
    public void D12_AGroupedPlayer_IsEligibleUnderTheTraits()
    {
        var wolf = Mon("Wolf", "Beast", 5);
        var grouped = Fighter("Bran", hp: 10, maxHp: 1000);
        grouped.RemoteTerminal = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        grouped.IsGroupedPlayer.Should().BeTrue();
        var rig = NewRig(new Random(1), grouped);
        MonsterTactics.TraitBonus(wolf, grouped).Should().Be(60);
        int wp = Weight(rig, rig.Hero), wc = Weight(rig, grouped);
        WithSwitch(true, () => TeammateShare(rig, wolf, grouped)).Should().BeApproximately((wc + 60) / (double)(wp + wc + 60), 0.02);

        var groupedMage = Fighter("Mira", CharacterClass.Magician);
        groupedMage.RemoteTerminal = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        MonsterTactics.TraitBonus(Mon("Goblin", "Goblinoid", 5), groupedMage).Should().Be(50);
    }

    // ---------------------------------------------------------------- D13

    [Fact]
    public void D13_TierOneSignatures_OnlyFromMonsterLevel3()
    {
        var hero = Fighter("Hero");
        var goblin = Mon("Goblin", "Goblinoid", 2);
        goblin.SignatureAbilities = new List<string> { "Rally" };
        Count("Rally", goblin, hero).Should().Be(0, "a level 2 Goblin has no Rally");
        MonsterTactics.Choose(goblin, hero, 2, null, new Random(1)).Should().BeNull();
        goblin.Level = 3;
        Count("Rally", goblin, hero).Should().Be(Turns, "at level 3 Rally appears");

        // through the engine, every roll lowest: the level 3 Goblin rallies, the level 2 one attacks
        WithSwitch(true, () =>
        {
            var rig = NewRig(new LowRandom());
            var three = Mon("Goblin", "Goblinoid", 3); three.SignatureAbilities = new List<string> { "Rally" };
            var two = Mon("Goblin", "Goblinoid", 2); two.SignatureAbilities = new List<string> { "Rally" };
            Special(rig, three).Should().BeTrue();
            three.HasEnraged.Should().BeTrue("Rally was used");
            Special(rig, two).Should().BeFalse();
            two.HasEnraged.Should().BeFalse();
            return 0;
        });
    }

    [Fact]
    public void D13_TheFiveSignatures_AreData_AndReachGeneratedMonsters()
    {
        var expected = new Dictionary<string, string>
        {
            ["Goblin"] = "Rally", ["Zombie"] = "Enfeeble", ["Kobold"] = "Poison", ["Spark"] = "Burn", ["Animated Armor"] = "Stoneskin",
        };
        var tiers = MonsterFamilies.GetBuiltInFamilies().SelectMany(f => f.Tiers).ToList();
        foreach (var tier in tiers)
        {
            if (expected.TryGetValue(tier.Name, out var signature))
            {
                tier.SignatureAbilities.Should().Equal(signature);
                tier.SpecialAbilities.Should().BeEmpty($"{tier.Name} keeps an empty ability list, so nothing changes with the switch off");
                Enum.TryParse<MonsterAbilities.AbilityType>(signature, out _).Should().BeTrue();
            }
            else tier.SignatureAbilities.Should().BeEmpty($"{tier.Name} has no signature (the Orc and the Ogre among them)");
        }
        tiers.Select(t => t.Name).Should().Contain(expected.Keys).And.Contain(new[] { "Orc", "Ogre" });

        // both generator paths copy the tier's signature onto the monster
        var random = new Random(128);
        var single = Enumerable.Range(0, 400).Select(_ => MonsterGenerator.GenerateMonster(5, false, false, random)).ToList();
        var grouped = Enumerable.Range(0, 400).SelectMany(_ => MonsterGenerator.GenerateMonsterGroup(5, random)).ToList();
        foreach (var made in new[] { single, grouped })
        {
            made.Should().Contain(m => m.TierName == "Goblin").And.Contain(m => m.TierName == "Zombie");
            foreach (var m in made)
            {
                if (expected.TryGetValue(m.TierName, out var signature)) m.SignatureAbilities.Should().Equal(signature);
                else m.SignatureAbilities.Should().BeEmpty(m.TierName);
            }
        }
    }

    // ---------------------------------------------------------------- D14

    [Fact]
    public void D14_SwitchOff_TheUniformPick_WithTheDrawsMadeBefore()
    {
        WithSwitch(false, () =>
        {
            var rig = NewRig(new Random(1));
            var angel = Mon("Angel", "Celestial", 20, "Heal", "Flight");   // full HP: the tactic would never heal
            int heals = 0, flights = 0, attacks = 0;
            for (int turn = 0; turn < 400; turn++)
            {
                var random = new RecordingRandom(1000 + turn);
                var reference = new RecordingRandom(1000 + turn);
                SetRandom(rig.Engine, random);
                angel.EvasionRounds = 0;
                bool used = Special(rig, angel);
                // the code before 1.2.8: the chance roll, then one uniform pick
                bool expectUsed = reference.Next(100) < 30 + angel.Level / 5;
                string? pick = expectUsed ? angel.SpecialAbilities[reference.Next(angel.SpecialAbilities.Count)] : null;
                random.Calls.Take(reference.Calls.Count).Should().Equal(reference.Calls, $"turn {turn}");
                used.Should().Be(pick == "Heal", $"turn {turn}: Heal takes the turn, Flight is followed by the normal attack");
                (angel.EvasionRounds == 2).Should().Be(pick == "Flight", $"turn {turn}");
                if (pick == "Heal") heals++; else if (pick == "Flight") flights++; else attacks++;
            }
            heals.Should().BeGreaterThan(30, "off, an Angel at full HP still picks Heal");
            flights.Should().BeGreaterThan(30);
            attacks.Should().BeGreaterThan(150);
            return 0;
        });
    }

    [Fact]
    public void D14_SwitchOff_SignaturesAndTraitsDoNothing()
    {
        WithSwitch(false, () =>
        {
            var random = new RecordingRandom(128);
            var rig = NewRig(random);
            var goblin = Mon("Goblin", "Goblinoid", 8);
            goblin.SignatureAbilities = new List<string> { "Rally" };
            for (int i = 0; i < 50; i++) Special(rig, goblin).Should().BeFalse();
            random.Calls.Should().BeEmpty("off, a Goblin has no ability, so the engine does not even roll the chance");
            goblin.HasEnraged.Should().BeFalse();

            var wolf = Mon("Wolf", "Beast", 5);
            var companion = Fighter("Lyra", hp: 10, maxHp: 1000);
            int wp = Weight(rig, rig.Hero), wc = Weight(rig, companion);
            TeammateShare(rig, wolf, companion).Should().BeApproximately(wc / (double)(wp + wc), 0.02, "off, a Wolf weighs a wounded target as before");
            return 0;
        });
        WithSwitch(true, () =>
        {
            var random = new RecordingRandom(128);
            var rig = NewRig(random);
            var goblin = Mon("Goblin", "Goblinoid", 8);
            goblin.SignatureAbilities = new List<string> { "Rally" };
            Special(rig, goblin);
            random.Calls.Should().NotBeEmpty("on, the same Goblin rolls its ability chance");
            return 0;
        });
    }

    [Fact]
    public void TheSwitch_IsOnByDefault_AndNotASysopSetting()
    {
        var p = typeof(GameConfig).GetProperty(nameof(GameConfig.MonsterTacticsEnabled))!;
        GameConfig.MonsterTacticsEnabled.Should().BeTrue();
        p.SetMethod!.IsAssembly.Should().BeTrue("the setter is internal");
        string root = Leftovers1114BTests.RepoRoot();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (name is "GameConfig.cs" or "CombatEngine.cs" or "MonsterTactics.cs" or "Monster.cs" or "MonsterFamilies.cs") continue;
            File.ReadAllText(file).Should().NotContain("MonsterTacticsEnabled", $"{name} must not read or set the switch");
        }
    }
}
