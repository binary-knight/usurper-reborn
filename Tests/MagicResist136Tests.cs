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

/// <summary>Every draw the lowest it can be: each chance roll succeeds and each pick is the first.</summary>
internal sealed class MagicResist136FlatRandom : Random
{
    public override int Next() => 0;
    public override int Next(int maxValue) => 0;
    public override int Next(int minValue, int maxValue) => minValue;
    public override double NextDouble() => 0.0;
}

/// <summary>1.3.6 magic resistance: the rigs the tests share.</summary>
internal static class MagicResist136Rig
{
    internal const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static int _serial;

    /// <summary>The 23 abilities that roll the breath damage (the ten monster spells among them).</summary>
    internal static readonly string[] BreathRolls =
    {
        "FireBreath", "FrostBreath", "PoisonCloud", "Spellcasting", "AncientMagic", "Hellfire", "Boulder", "Lightning",
        "Earthquake", "Immolate", "Fireball", "Inferno", "Madness", "PoisonGas", "Overload", "RootEntangle", "Whirlpool",
        "TidalWave", "HolySmite", "DivineJudgment", "Terror", "Nightmare", "RealityBreak",
    };

    internal static string Plain(string s) => Regex.Replace(Regex.Replace(s, "\u001b\\[[0-9;]*m", ""), "\\s+", " ");

    /// <summary>An armour piece with magic resistance <paramref name="mr"/> (an item holds at most 100), a new item each call.</summary>
    internal static int Armour(EquipmentSlot slot, int armourClass, int mr)
    {
        int n = System.Threading.Interlocked.Increment(ref _serial);
        return EquipmentDatabase.RegisterDynamic(new Equipment
        {
            Name = $"MR136 {slot} {mr} {n}", Slot = slot, MinLevel = 1, Rarity = EquipmentRarity.Common,
            ArmorClass = armourClass, MagicResistance = mr,
        });
    }

    /// <summary>A body armour of armour class 10 and a helm of none, the magic resistance split between them
    /// (at most 100 each), so every wearer has the same armour whatever its resistance.</summary>
    internal static T Wear<T>(T c, int mr) where T : Character
    {
        c.EquippedItems[EquipmentSlot.Body] = Armour(EquipmentSlot.Body, 10, Math.Min(mr, 100));
        c.EquippedItems[EquipmentSlot.Head] = Armour(EquipmentSlot.Head, 0, Math.Max(0, mr - 100));
        c.RecalculateStats();
        c.HP = c.MaxHP;
        c.Mana = c.MaxMana;
        return c;
    }

    internal static Monster Caster(string ability, string family = "")
    {
        var m = G.Foe("Wolf", 50_000, 600, 40, 10);
        m.SpecialAbilities = new List<string> { ability };
        if (family.Length > 0) m.FamilyName = family;
        return m;
    }

    internal static G.Rig Rig(Character hero, List<Character>? team, Monster foe) =>
        G.NewRig(hero, new MagicResist136FlatRandom(), team, foe);

    internal static bool MonsterSpecial(G.Rig rig, Monster m, Character target) =>
        ((Task<bool>)typeof(CombatEngine).GetMethod("TryMonsterSpecialAbility", F)!
            .Invoke(rig.Engine, new object?[] { m, target, rig.Result, rig.Monsters })!).GetAwaiter().GetResult();

    internal static void MonsterOnCompanion(G.Rig rig, Monster m, Character companion) =>
        ((Task)typeof(CombatEngine).GetMethod("MonsterAttacksCompanion", F)!
            .Invoke(rig.Engine, new object?[] { m, companion, rig.Result, rig.Monsters })!).GetAwaiter().GetResult();

    /// <summary>What a monster's special ability takes from the player, through TryMonsterSpecialAbility.</summary>
    internal static long PlayerLoss(string ability, int mr, string family = "", int barracks = 0)
    {
        var hero = Wear(G.Hero(200_000, 60, 40), mr);
        if (barracks > 0) { hero.Team = "MR136 Team"; hero.HQLevelsTeam = hero.Team; hero.HQBarracksLevel = barracks; }
        var m = Caster(ability, family);
        var rig = Rig(hero, null, m);
        long before = hero.HP;
        MonsterSpecial(rig, m, hero).Should().BeTrue("{0} is used", ability);
        return before - hero.HP;
    }

    /// <summary>What a monster's special ability takes from a companion, through MonsterAttacksCompanion.</summary>
    internal static long CompanionLoss(string ability, int mr, string family = "")
    {
        var mate = Wear(G.Mate("Ally One", 200_000, 40, 60), mr);
        var m = Caster(ability, family);
        var rig = Rig(G.Hero(), new List<Character> { mate }, m);
        long before = mate.HP;
        MonsterOnCompanion(rig, m, mate);
        return before - mate.HP;
    }

    internal static string Source(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, relative)).Replace("\r\n", "\n");
    }
}

/// <summary>1.3.6: the share magic resistance takes of a magical hit.</summary>
[Collection("SharedGameSingletons")]
public class MagicResist136ShareTests
{
    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(-5, 0.0)]
    [InlineData(20, 1.0 / 6.0)]
    [InlineData(60, 60.0 / 160.0)]
    [InlineData(100, 0.5)]
    [InlineData(500, 0.5)]
    public void MagicResist136_Share_IsMrOverMrPlus100_CappedAtHalf(int mr, double share)
    {
        CombatEngine.MagicResistShare(mr).Should().BeApproximately(share, 1e-12, "MR {0}", mr);
    }

    [Theory]
    [InlineData(0, 1200, 1200)]
    [InlineData(20, 1200, 1000)]
    [InlineData(100, 1200, 600)]
    [InlineData(200, 1200, 600)]
    [InlineData(20, 1, 1)]
    [InlineData(200, 1, 1)]
    [InlineData(200, 0, 0)]
    [InlineData(20, 7, 5)]
    public void MagicResist136_Apply_TakesTheShare_FloorOne(int mr, long damage, long expected)
    {
        var c = MagicResist136Rig.Wear(G.Hero(), mr);
        c.GetEquipmentMagicResistance().Should().Be(mr);
        CombatEngine.ApplyMagicResist(c, damage).Should().Be(expected, "MR {0} on a hit of {1}", mr, damage);
    }
}

/// <summary>1.3.6: which monster abilities are magical, and that the mark draws nothing.</summary>
[Collection("SharedGameSingletons")]
public class MagicResist136MarkTests
{
    /// <summary>Each ability once on a fixed seed: its direct damage, the draws it made, and the next draw after it.
    /// Recorded at the start head 021090a9, before the rule (receipts rng-golden-021090a9.txt).</summary>
    private static readonly string[] StartHeadDraws =
    {
        "Multiattack 0 1 722257",
        "CrushingBlow 0 0 776085",
        "VenomousBite 0 0 776085",
        "BleedingWound 0 0 776085",
        "FireBreath 642 1 722257",
        "FrostBreath 516 1 722257",
        "PoisonCloud 348 1 722257",
        "LifeDrain 0 0 776085",
        "ManaDrain 0 1 722257",
        "Regeneration 0 0 776085",
        "Thorns 0 0 776085",
        "ArmorHarden 0 0 776085",
        "Vanish 0 0 776085",
        "Phase 0 1 722257",
        "PetrifyingGaze 0 0 776085",
        "HorrifyingScream 0 0 776085",
        "BlindingFlash 0 0 776085",
        "Curse 0 0 776085",
        "Silence 0 0 776085",
        "Enfeeble 0 0 776085",
        "Devour 0 0 776085",
        "Berserk 0 0 776085",
        "SummonMinions 0 1 722257",
        "Explosion 0 0 776085",
        "SoulReap 0 1 722257",
        "Backstab 0 0 776085",
        "Flee 0 0 776085",
        "CallForHelp 0 0 776085",
        "Enrage 0 0 776085",
        "Heal 0 0 776085",
        "CriticalStrike 0 0 776085",
        "Rally 0 0 776085",
        "CommandArmy 0 1 722257",
        "Paralyze 0 0 776085",
        "Incorporeal 0 1 722257",
        "Spellcasting 558 2 830389",
        "Phylactery 0 0 776085",
        "Rage 0 0 776085",
        "Frenzy 0 1 722257",
        "Warcry 0 0 776085",
        "Cleave 0 0 776085",
        "Flight 0 0 776085",
        "DragonFear 0 0 776085",
        "AncientMagic 852 1 722257",
        "Invisibility 0 0 776085",
        "Teleport 0 0 776085",
        "Hellfire 768 1 722257",
        "Corruption 0 0 776085",
        "Dominate 0 0 776085",
        "Boulder 642 1 722257",
        "Stoneskin 0 0 776085",
        "Lightning 726 1 722257",
        "Earthquake 600 1 722257",
        "PackTactics 0 0 776085",
        "Bite 0 0 776085",
        "Lycanthropy 0 0 776085",
        "Howl 0 0 776085",
        "Moonlight 0 0 776085",
        "Burn 0 0 776085",
        "Immolate 684 1 722257",
        "Fireball 642 1 722257",
        "Rebirth 0 0 776085",
        "Inferno 852 1 722257",
        "Corrosion 0 0 776085",
        "Split 0 0 776085",
        "Engulf 0 0 776085",
        "Absorb 0 0 776085",
        "ShapeShift 0 2 830389",
        "Madness 348 1 722257",
        "WebTrap 0 0 776085",
        "PhaseShift 0 1 722257",
        "Poison 0 0 776085",
        "SummonSpiders 0 1 722257",
        "DeadlyVenom 0 0 776085",
        "Swarm 0 1 722257",
        "Cocoon 0 0 776085",
        "ImmuneMagic 0 0 776085",
        "PoisonGas 390 1 722257",
        "Indestructible 0 0 776085",
        "SelfRepair 0 0 776085",
        "Overload 1062 1 722257",
        "Sleep 0 0 776085",
        "TreeMeld 0 1 722257",
        "Charm 0 0 776085",
        "AnimateTrees 0 1 722257",
        "RootEntangle 348 1 722257",
        "TimeStop 0 1 722257",
        "WildShape 0 0 776085",
        "TentacleGrab 0 1 722257",
        "InkCloud 0 0 776085",
        "Whirlpool 642 1 722257",
        "TidalWave 852 1 722257",
        "HolySmite 684 1 722257",
        "Purify 0 0 776085",
        "DivineJudgment 936 1 722257",
        "Sanctuary 0 0 776085",
        "Resurrection 0 0 776085",
        "StrengthDrain 0 0 776085",
        "Terror 432 1 722257",
        "Possess 30 0 776085",
        "Nightmare 558 1 722257",
        "DevourSoul 0 1 722257",
        "RealityBreak 768 2 830389",
    };

    private static List<string> Draws()
    {
        var lines = new List<string>();
        foreach (MonsterAbilities.AbilityType t in Enum.GetValues(typeof(MonsterAbilities.AbilityType)))
        {
            if (t == MonsterAbilities.AbilityType.None) continue;
            var m = G.Foe("Wolf", 50_000, 600, 40, 10);
            m.SpecialAbilities = new List<string> { t.ToString() };
            var rng = new G.RecordingRandom(136);
            var r = MonsterAbilities.ExecuteAbility(t, m, G.Hero(), rng);
            int next = rng.Next(1_000_000);
            lines.Add($"{t} {r.DirectDamage} {rng.Calls.Count - 1} {next}");
        }
        return lines;
    }

    [Fact]
    public void MagicResist136_Mark_DrawsAndDamage_SameAsTheStartHead()
    {
        string lang = GameConfig.Language;
        try
        {
            GameConfig.Language = "en";
            Draws().Should().Equal(StartHeadDraws, "the mark adds no draw and moves no roll");
        }
        finally { GameConfig.Language = lang; }
    }

    [Fact]
    public void MagicResist136_Mark_EveryBreathRollAndEverySpell_AndNothingElse()
    {
        var breath = new HashSet<string>(MagicResist136Rig.BreathRolls);
        foreach (MonsterAbilities.AbilityType t in Enum.GetValues(typeof(MonsterAbilities.AbilityType)))
        {
            if (t == MonsterAbilities.AbilityType.None) continue;
            var r = MonsterAbilities.ExecuteAbility(t, MagicResist136Rig.Caster(t.ToString()), G.Hero(), new Random(9));
            bool magical = breath.Contains(t.ToString()) || CombatEngine.MonsterSpellAbilities.Contains(t);
            r.IsMagical.Should().Be(magical, "{0} {1}", t, magical ? "rolls the breath damage or is a monster spell" : "is not magical");
        }
    }

    [Theory]
    [InlineData("Boulder")]
    [InlineData("Earthquake")]
    [InlineData("RootEntangle")]
    [InlineData("FireBreath")]
    [InlineData("PoisonGas")]
    public void MagicResist136_Mark_BreathRollsOutsideTheSpellList_AreMagical(string ability)
    {
        var t = Enum.Parse<MonsterAbilities.AbilityType>(ability);
        CombatEngine.MonsterSpellAbilities.Contains(t).Should().BeFalse("{0} is marked by its breath roll alone", ability);
        MonsterAbilities.ExecuteAbility(t, MagicResist136Rig.Caster(ability), G.Hero(), new Random(3)).IsMagical
            .Should().BeTrue("{0} rolls the breath damage, so it is magical", ability);
    }

    [Theory]
    [InlineData("CrushingBlow")]
    [InlineData("LifeDrain")]
    [InlineData("Absorb")]
    [InlineData("VenomousBite")]
    [InlineData("Engulf")]
    [InlineData("Multiattack")]
    [InlineData("Heal")]
    public void MagicResist136_Mark_NonBreathAbilities_AreNotMagical(string ability)
    {
        var t = Enum.Parse<MonsterAbilities.AbilityType>(ability);
        MonsterAbilities.ExecuteAbility(t, MagicResist136Rig.Caster(ability), G.Hero(), new Random(3)).IsMagical
            .Should().BeFalse("{0} rolls no breath damage and is no monster spell", ability);
    }

    [Fact]
    public void MagicResist136_Mark_EveryBreathRollGoesThroughTheMark()
    {
        string src = MagicResist136Rig.Source("Scripts/Systems/MonsterAbilities.cs");
        Regex.Matches(src, @"CalculateBreathDamage\(monster,").Count.Should().Be(1, "the only direct roll is inside the marking helper");
        Regex.Matches(src, @"result\.DirectDamage = MagicalBreath\(result, monster, ").Count.Should().Be(MagicResist136Rig.BreathRolls.Length);
        Regex.IsMatch(src, @"private static int MagicalBreath\(AbilityResult result, Monster monster, float multiplier, Random rng\)\s*\{\s*result\.IsMagical = true;\s*return CalculateBreathDamage\(monster, multiplier, rng\);")
            .Should().BeTrue("the helper marks the result and returns the roll unchanged");
    }

    [Fact]
    public void MagicResist136_Mark_MonsterSpellsAreMarkedByTheSpellList()
    {
        // Today every monster spell also rolls the breath damage, so this line is pinned by its source: a spell
        // added later without a breath roll is still magical.
        string src = MagicResist136Rig.Source("Scripts/Systems/MonsterAbilities.cs");
        Regex.IsMatch(src, @"if \(CombatEngine\.MonsterSpellAbilities\.Contains\(ability\)\) result\.IsMagical = true;\s*\n\s*return result;")
            .Should().BeTrue("ExecuteAbility marks every monster spell magical before it returns");
    }
}

/// <summary>1.3.6: magic resistance on a monster's magical ability against the player and a companion.</summary>
[Collection("SharedGameSingletons")]
public class MagicResist136PveTests
{
    private static long Ward(long hit) => Math.Max(1, (long)Math.Floor(hit * (1.0 - 20.0 / 120.0)));

    [Theory]
    [InlineData("Fireball")]
    [InlineData("FireBreath")]
    [InlineData("Lightning")]
    [InlineData("HolySmite")]
    [InlineData("Boulder")]
    public void MagicResist136_Player_DirectBranch_WardTakesASixth(string ability)
    {
        long none = MagicResist136Rig.PlayerLoss(ability, 0);
        none.Should().BeGreaterThan(100, "the hit lands");
        MagicResist136Rig.PlayerLoss(ability, 20).Should().Be(Ward(none), "Ward 20 takes a sixth of {0}", ability);
        MagicResist136Rig.PlayerLoss(ability, 200).Should().Be(Math.Max(1, none / 2), "the cap is a half (MR 200)");
    }

    [Fact]
    public void MagicResist136_Player_MrZero_SameAsNoArmourEnchant()
    {
        var bare = G.Hero(200_000, 60, 40);
        bare.EquippedItems[EquipmentSlot.Body] = EquipmentDatabase.RegisterDynamic(new Equipment
        {
            Name = "MR136 Plain Mail", Slot = EquipmentSlot.Body, MinLevel = 1, Rarity = EquipmentRarity.Common, ArmorClass = 10,
        });
        bare.RecalculateStats();
        bare.HP = bare.MaxHP;
        var m = MagicResist136Rig.Caster("Fireball");
        var rig = MagicResist136Rig.Rig(bare, null, m);
        long before = bare.HP;
        MagicResist136Rig.MonsterSpecial(rig, m, bare).Should().BeTrue();
        MagicResist136Rig.PlayerLoss("Fireball", 0).Should().Be(before - bare.HP, "MR 0 changes nothing");
    }

    [Theory]
    [InlineData("CrushingBlow")]
    [InlineData("LifeDrain")]
    [InlineData("Absorb")]
    public void MagicResist136_Player_NonMagical_Unchanged(string ability)
    {
        long none = MagicResist136Rig.PlayerLoss(ability, 0);
        none.Should().BeGreaterThan(0);
        MagicResist136Rig.PlayerLoss(ability, 200).Should().Be(none, "{0} is not magical", ability);
    }

    [Fact]
    public void MagicResist136_Player_OldGod_Excluded()
    {
        long none = MagicResist136Rig.PlayerLoss("Fireball", 0, "OldGod");
        none.Should().BeGreaterThan(100);
        MagicResist136Rig.PlayerLoss("Fireball", 20, "OldGod").Should().Be(none, "an Old God's attack is never reduced (N1)");
        MagicResist136Rig.PlayerLoss("Fireball", 200, "OldGod").Should().Be(none, "an Old God's attack is never reduced (N1)");
    }

    [Fact]
    public void MagicResist136_Player_AppliedLast_AfterTheBarracksAndTheMinimum()
    {
        // Barracks 20 halves the hit below the anti-tank minimum (0.25 percent of 200,000 = 500), so the minimum
        // holds at 500; the Ward's sixth comes after it and takes the hit below the minimum, to 416.
        MagicResist136Rig.PlayerLoss("Fireball", 0, barracks: 20).Should().Be(500, "the minimum holds after the Barracks");
        MagicResist136Rig.PlayerLoss("Fireball", 20, barracks: 20).Should().Be(416, "magic resistance comes last, after the Barracks and the minimum");
    }

    [Theory]
    [InlineData("Fireball")]
    [InlineData("AncientMagic")]
    [InlineData("Earthquake")]
    public void MagicResist136_Companion_DirectBranch_TakesTheShare(string ability)
    {
        long none = MagicResist136Rig.CompanionLoss(ability, 0);
        none.Should().BeGreaterThan(100, "the hit lands");
        MagicResist136Rig.CompanionLoss(ability, 20).Should().Be(Ward(none), "Ward 20 takes a sixth of {0} on a companion", ability);
        MagicResist136Rig.CompanionLoss(ability, 100).Should().Be(Math.Max(1, none / 2), "MR 100 takes a half");
    }

    [Theory]
    [InlineData("CrushingBlow")]
    [InlineData("LifeDrain")]
    public void MagicResist136_Companion_NonMagical_Unchanged(string ability)
    {
        long none = MagicResist136Rig.CompanionLoss(ability, 0);
        none.Should().BeGreaterThan(0);
        MagicResist136Rig.CompanionLoss(ability, 200).Should().Be(none, "{0} is not magical", ability);
    }

    [Fact]
    public void MagicResist136_Companion_OldGod_Excluded()
    {
        long none = MagicResist136Rig.CompanionLoss("Fireball", 0, "OldGod");
        none.Should().BeGreaterThan(100);
        MagicResist136Rig.CompanionLoss("Fireball", 20, "OldGod").Should().Be(none, "an Old God's attack on a companion is never reduced (N1)");
    }

    [Fact]
    public void MagicResist136_Placement_PlayerLifeStealBranch_LastBeforeTheHeal()
    {
        // No magical ability sets a life steal today, so this branch is pinned by its source.
        string src = MagicResist136Rig.Source("Scripts/Systems/CombatEngine.cs");
        Regex.IsMatch(src, @"TeamHQBonus\.ApplyDefense\(player, damage\)\);\n\s*if \(abilityResult\.IsMagical && !isOldGodFightAbility\) damage = ApplyMagicResist\(player, damage\);[^\n]*\n\s*\n\s*(?:damage = ShareLinkedHit\(player, damage, result, monKind\);[^\n]*\n\s*)?player\.HP -= damage;\n[^\n]*\n\s*long healAmount = damage \* abilityResult\.LifeStealPercent / 100;")
            .Should().BeTrue("the life steal branch takes the share after the Barracks and before the heal reads the damage");
    }

    [Fact]
    public void MagicResist136_Placement_PlayerMultiplierBranch_Last()
    {
        // A magical result always carries direct damage today, so this branch is pinned by its source.
        string src = MagicResist136Rig.Source("Scripts/Systems/CombatEngine.cs");
        Regex.IsMatch(src, @"GetMinIncomingDamage\(player, rawDamage\)\), TeamHQBonus\.ApplyDefense\(player, damage\)\);\n\s*if \(abilityResult\.IsMagical && !isOldGodFightAbility\) damage = ApplyMagicResist\(player, damage\);[^\n]*\n\s*\n\s*(?:damage = ShareLinkedHit\(player, damage, result, monKind\);[^\n]*\n\s*)?player\.HP -= damage;")
            .Should().BeTrue("the multiplier branch takes the share after the Barracks, just before the HP write");
    }

    [Fact]
    public void MagicResist136_Placement_CompanionMultiplierBranch_Last()
    {
        // A magical result always carries direct damage today, so this branch is pinned by its source.
        string src = MagicResist136Rig.Source("Scripts/Systems/CombatEngine.cs");
        Regex.IsMatch(src, @"dmg = MitigateCompanionAbilityHit\(monster, companion, dmg, result\);\n\s*if \(abilityResult\.IsMagical && monster\.FamilyName != ""OldGod""\) dmg = ApplyMagicResist\(companion, dmg\);[^\n]*\n\s*(?:dmg = ShareLinkedHit\(companion, dmg, result, MonsterAbilityEventKind\(abilityType\)\);[^\n]*\n\s*)?RecordAllyHit\(companion, dmg\);")
            .Should().BeTrue("the companion multiplier branch takes the share after the mitigation, never on an Old God's attack");
    }
}

/// <summary>1.3.6 (N2): magic resistance on a spell in PvP, the player's and the NPC's.</summary>
[Collection("SharedGameSingletons")]
public class MagicResist136PvpTests
{
    // SpellSystem rolls its fumble and its damage on the shared random, so a cast is tried again until one lands
    private static (long Raw, long Shown, long Lost) PvpSpell(bool npc, int mr, int barracks = 0)
    {
        string lang = GameConfig.Language;
        try
        {
            GameConfig.Language = "en";
            for (int attempt = 0; attempt < 100; attempt++)
            {
                var got = PvpSpellOnce(npc, mr, barracks);
                if (got.HasValue) return got.Value;
            }
        }
        finally { GameConfig.Language = lang; }
        throw new InvalidOperationException("no Magic Missile landed in 100 casts");
    }

    private static (long Raw, long Shown, long Lost)? PvpSpellOnce(bool npc, int mr, int barracks)
    {
        var caster = G.Hero(50_000, 60, 30, CharacterClass.Magician, name: "Caster");
        caster.EquippedItems[EquipmentSlot.MainHand] = G.Weapon(40, WeaponType.Staff, WeaponHandedness.TwoHanded);
        caster.RecalculateStats();
        caster.Spell = Enumerable.Range(0, 30).Select(i => new List<bool> { i == 0, false }).ToList();   // Magic Missile only
        caster.Intelligence = caster.BaseIntelligence = 200;
        caster.Mana = caster.MaxMana = 100_000;
        caster.HP = caster.MaxHP;
        caster.Healing = 0;
        caster.AI = npc ? CharacterAI.Computer : CharacterAI.Human;
        var target = MagicResist136Rig.Wear(G.Hero(500_000, 60, 30, name: "Target"), mr);
        target.AI = npc ? CharacterAI.Human : CharacterAI.Computer;
        if (barracks > 0) { target.Team = "MR136 Team"; target.HQLevelsTeam = target.Team; target.HQBarracksLevel = barracks; }

        _ = new GriefSystem();
        var output = new MemoryStream();
        var terminal = new TerminalEmulator(new MemoryStream(), output);
        var engine = new CombatEngine(terminal);
        typeof(CombatEngine).GetField("random", MagicResist136Rig.F)!.SetValue(engine, new MagicResist136FlatRandom());
        typeof(CombatEngine).GetField("currentPlayer", MagicResist136Rig.F)!.SetValue(engine, caster);
        var result = new CombatResult { Player = caster, Teammates = new List<Character>(), CombatLog = new List<string>(), CurrentRound = 2 };
        long before = target.HP;
        if (npc)
            ((Task)typeof(CombatEngine).GetMethod("ProcessComputerPlayerAction", MagicResist136Rig.F)!
                .Invoke(engine, new object[] { caster, target, result })!).GetAwaiter().GetResult();
        else
            ((Task)typeof(CombatEngine).GetMethod("ExecutePvPSpell", MagicResist136Rig.F)!.Invoke(engine,
                new object[] { caster, target, new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = 1 }, result })!).GetAwaiter().GetResult();
        terminal.StreamWriterInternal?.Flush();
        string shown = MagicResist136Rig.Plain(Encoding.UTF8.GetString(output.ToArray()));
        var raw = Regex.Match(shown, @"Magic missiles strike .*? for (\d+) damage");
        if (!raw.Success) return null;   // a fumble
        var hit = Regex.Match(shown, @"takes (\d+) magical damage");
        hit.Success.Should().BeTrue(shown);
        return (long.Parse(raw.Groups[1].Value), long.Parse(hit.Groups[1].Value), before - target.HP);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MagicResist136_PvpSpell_Mr100Halves_MrZeroUnchanged(bool npc)
    {
        var none = PvpSpell(npc, 0);
        none.Lost.Should().Be(none.Raw, "no resistance, the spell lands whole");
        none.Shown.Should().Be(none.Lost);
        var half = PvpSpell(npc, 100);
        half.Lost.Should().Be(Math.Max(1, half.Raw / 2), "MR 100 takes a half of a PvP spell (N2), {0}", npc ? "the NPC's" : "the player's");
        half.Shown.Should().Be(half.Lost, "the line shows what landed");
        var ward = PvpSpell(npc, 20);
        ward.Lost.Should().Be(Math.Max(1, (long)Math.Floor(ward.Raw * (1.0 - 20.0 / 120.0))), "Ward 20 takes a sixth");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MagicResist136_PvpSpell_AfterTheBarracks(bool npc)
    {
        // Barracks 20 divides by 2, rounded; magic resistance comes after it (N2)
        var hit = PvpSpell(npc, 100, barracks: 20);
        hit.Lost.Should().Be(Math.Max(1, (long)Math.Floor(Math.Round(hit.Raw / 2.0) * 0.5)), "the Barracks first, then the half");
    }
}
