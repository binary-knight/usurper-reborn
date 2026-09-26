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

namespace UsurperReborn.Tests;

/// <summary>v1.1.15 Sage piece 3: party wards, Ocean's Memory, Veloura's Embrace, and the duel mapping.</summary>
[Collection("SharedGameSingletons")]
public class SageWards1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SF = BindingFlags.NonPublic | BindingFlags.Static;

    private static (CombatEngine engine, MemoryStream output) Engine(Character player, List<Character> teammates, Random? rng = null)
    {
        var output = new MemoryStream();
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), output));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, rng ?? new Random(7));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, player);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, teammates);
        return (engine, output);
    }

    private static Character Sage(int level = 95) => new Character
    {
        Name1 = "Sage", Name2 = "Sage", Class = CharacterClass.Sage, Level = level, Wisdom = 50, Intelligence = 50,
        HP = 100_000, MaxHP = 100_000, Mana = 100_000, MaxMana = 100_000, CombatSpeed = CombatSpeed.Instant,
    };

    private static Character Ally(string name, CharacterClass cls) => new Character
    {
        Name1 = name, Name2 = name, Class = cls, Level = 40, Wisdom = 30, HP = 5_000, MaxHP = 5_000,
        Mana = 5_000, MaxMana = 5_000, CombatSpeed = CombatSpeed.Instant,
    };

    /// <summary>The Sage spell's own result, from the real spell code.</summary>
    private static SpellSystem.SpellResult Cast(Character sage, int slot)
    {
        var r = new SpellSystem.SpellResult { Success = true };
        typeof(SpellSystem).GetMethod("ExecuteSageSpell", SF)!
            .Invoke(null, new object?[] { sage, slot, null, null, r, new Random(3), 1.0f });
        return r;
    }

    private static string Text(CombatEngine engine, MemoryStream output)
    {
        var term = (TerminalEmulator)typeof(CombatEngine).GetField("terminal", F)!.GetValue(engine)!;
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    /// <summary>A Sage, two teammates, and a leader who is not in the teammate list (a follower's cast).</summary>
    private static (Character sage, Character a, Character b, Character leader, CombatEngine engine, MemoryStream output, CombatResult result) Party()
    {
        var sage = Sage();
        var a = Ally("Tank", CharacterClass.Warrior);
        var b = Ally("Healer", CharacterClass.Cleric);
        var leader = Ally("Leader", CharacterClass.Ranger);
        var (engine, output) = Engine(sage, new List<Character> { sage, a, b });
        return (sage, a, b, leader, engine, output, new CombatResult { Player = leader });
    }

    // ---- the kit ----

    [Fact]
    public void ThePartySpells_AreAreaSpells()
    {
        foreach (int slot in new[] { 1, 13, 16, 20, 22, 24 })
            SpellSystem.GetSpellInfo(CharacterClass.Sage, slot).IsMultiTarget.Should().BeTrue($"slot {slot}");
        SpellSystem.GetSpellInfo(CharacterClass.Sage, 24).SpellType.Should().Be("Heal");
        string src = File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        src.Should().Contain("else if (spellInfo.IsMultiTarget && spellInfo.SpellType == \"Buff\" && player.Class == CharacterClass.Sage)");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(13)]
    [InlineData(16)]
    [InlineData(20)]
    public void EachWard_ReachesEveryAlly(int slot)
    {
        var (sage, a, b, leader, engine, _, result) = Party();
        var r = Cast(sage, slot);
        r.ProtectionBonus.Should().BeGreaterThan(0);
        engine.ApplySagePartyWard(sage, r, result);
        foreach (var c in new[] { sage, a, b, leader })
        {
            c.MagicACBonus.Should().Be(r.ProtectionBonus, c.Name2);
            c.HasStatus(StatusEffect.Blessed).Should().BeTrue(c.Name2);
        }
    }

    [Fact]
    public void AFallenAlly_GetsNoWard()
    {
        var (sage, a, b, leader, engine, _, result) = Party();
        b.HP = 0;
        engine.ApplySagePartyWard(sage, Cast(sage, 20), result);
        b.MagicACBonus.Should().Be(0);
        a.MagicACBonus.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(20)]
    public void ShadowCloak_AndNocturasVeil_BlurEveryAlly(int slot)
    {
        var (sage, a, b, leader, engine, _, result) = Party();
        engine.ApplySagePartyWard(sage, Cast(sage, slot), result);
        foreach (var c in new[] { sage, a, b, leader })
            c.HasStatus(StatusEffect.Blur).Should().BeTrue(c.Name2);
    }

    [Fact]
    public void FogOfWar_IsSmaller_AndBlursNoOne()
    {
        var (sage, a, b, leader, engine, _, result) = Party();
        var fog = Cast(sage, 1);
        fog.ProtectionBonus.Should().BeLessThan(Cast(sage, 13).ProtectionBonus);
        string src = File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts", "Systems", "SpellSystem.cs"));
        src.Should().Contain("ScaleProtectionEffect(5 + (caster.Level / 15), caster, profMult)");
        engine.ApplySagePartyWard(sage, fog, result);
        foreach (var c in new[] { sage, a, b, leader })
            c.HasStatus(StatusEffect.Blur).Should().BeFalse(c.Name2);
    }

    [Fact]
    public void MindBlank_GuardsEveryAlly()
    {
        var (sage, a, b, leader, engine, _, result) = Party();
        engine.ApplySagePartyWard(sage, Cast(sage, 16), result);
        foreach (var c in new[] { sage, a, b, leader })
        {
            c.HasStatusImmunity.Should().BeTrue(c.Name2);
            c.StatusImmunityDuration.Should().BeGreaterThan(0, c.Name2);
        }
    }

    // ---- highest wins ----

    [Fact]
    public void AStrongerClericWard_Stays_AndAStrongerSageWard_Replaces()
    {
        var (sage, a, b, leader, engine, output, result) = Party();
        CombatEngine.ApplyWardHighestWins(a, 200, 5).Should().BeTrue("a Cleric's Divine Intervention on the tank");
        var fog = Cast(sage, 1);
        fog.ProtectionBonus.Should().BeLessThan(200);
        engine.ApplySagePartyWard(sage, fog, result);
        a.MagicACBonus.Should().Be(200, "the stronger ward holds");
        b.MagicACBonus.Should().Be(fog.ProtectionBonus, "no ward was up");
        Text(engine, output).Should().Contain("Tank keeps a stronger ward.");

        var veil = Cast(sage, 20);
        veil.ProtectionBonus.Should().BeGreaterThan(200);
        engine.ApplySagePartyWard(sage, veil, result);
        a.MagicACBonus.Should().Be(veil.ProtectionBonus, "the stronger ward wins");
    }

    [Fact]
    public void SageWards_DoNotStack_OrStepDown()
    {
        var (sage, a, b, leader, engine, _, result) = Party();
        var veil = Cast(sage, 20);
        engine.ApplySagePartyWard(sage, veil, result);
        engine.ApplySagePartyWard(sage, Cast(sage, 1), result);
        foreach (var c in new[] { sage, a, b, leader })
            c.MagicACBonus.Should().Be(veil.ProtectionBonus, c.Name2);
    }

    [Fact]
    public void AWeakerSelfWard_DoesNotReplaceTheSagesWard()
    {
        var (sage, a, b, leader, engine, _, result) = Party();
        var veil = Cast(sage, 20);
        engine.ApplySagePartyWard(sage, veil, result);
        // the Cleric casts a weaker ward on herself, through the caster path
        var armor = new SpellSystem.SpellResult { Success = true, ProtectionBonus = 28, Duration = 999 };
        typeof(CombatEngine).GetMethod("ApplySpellEffects", F)!.Invoke(engine, new object?[] { b, null, armor, null });
        b.MagicACBonus.Should().Be(veil.ProtectionBonus);
    }

    // ---- Ocean's Memory ----

    [Fact]
    public void OceansMemory_HalvesAnAllysManaCost()
    {
        var (sage, a, b, leader, engine, _, result) = Party();
        var cure = SpellSystem.GetSpellInfo(CharacterClass.Cleric, 5);
        int before = SpellSystem.CalculateManaCost(cure, b);
        engine.ApplySagePartyWard(sage, Cast(sage, 22), result);
        b.HasOceanMemory.Should().BeTrue();
        leader.HasOceanMemory.Should().BeTrue();
        SpellSystem.CalculateManaCost(cure, b).Should().Be(Math.Max(1, before / 2));
    }

    [Fact]
    public void OceansMemory_EndsWithTheFight()
    {
        var c = Ally("Healer", CharacterClass.Cleric);
        c.HasOceanMemory = true;
        c.ApplyStatus(StatusEffect.Blur, 999);
        typeof(CombatEngine).GetMethod("ScrubTransientCombatState", SF)!.Invoke(null, new object[] { c });
        c.HasOceanMemory.Should().BeFalse();
        c.HasStatus(StatusEffect.Blur).Should().BeFalse();
        string src = File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        src.Should().Contain("player.HasOceanMemory = false; // v1.1.15: Ocean's Memory lasts one fight");
        src.Should().Contain("teammate.HasOceanMemory = false; // v1.1.15: Ocean's Memory lasts one fight");
        src.Should().Contain("player.HasOceanMemory = false; // v1.1.15: it never ended before");
        src.Should().Contain("teammate.HasOceanMemory = false; // v1.1.15: the Sage's party spells end with the fight");
    }

    // ---- Veloura's Embrace ----

    [Fact]
    public async Task VelourasEmbrace_HealsThePartyAndWardsIt()
    {
        var (sage, a, b, leader, engine, _, result) = Party();
        var staff = EquipmentDatabase.GetAll().First(e => e.WeaponType == WeaponType.Staff);
        sage.EquippedItems[EquipmentSlot.MainHand] = staff.Id;
        foreach (var c in new[] { sage, a, b, leader }) c.HP = c.MaxHP / 4;
        long sageBefore = sage.HP;
        var action = new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = 24 };
        await (Task)typeof(CombatEngine).GetMethod("ExecuteSpellMultiMonster", F)!
            .Invoke(engine, new object[] { sage, new List<Monster>(), action, result })!;
        foreach (var c in new[] { a, b, leader })
        {
            c.HP.Should().BeGreaterThan(c.MaxHP / 4, c.Name2);
            c.MagicACBonus.Should().BeGreaterThan(0, c.Name2);
        }
        sage.HP.Should().BeGreaterThan(sageBefore);
    }

    // ---- Blur on a teammate ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Blur_MakesAMonsterMissATeammate(bool blurred)
    {
        var tank = Ally("Tank", CharacterClass.Warrior);
        if (blurred) tank.ApplyStatus(StatusEffect.Blur, 999);
        var (engine, _) = Engine(Sage(), new List<Character> { tank }, new LowRandom());
        var ogre = new Monster { Name = "Ogre", Level = 30, HP = 5000, MaxHP = 5000, Strength = 200, Defence = 20 };
        await (Task)typeof(CombatEngine).GetMethod("MonsterAttacksCompanion", F)!
            .Invoke(engine, new object?[] { ogre, tank, new CombatResult { Player = Sage() }, null })!;
        if (blurred) tank.HP.Should().Be(tank.MaxHP, "the blow strikes only an image");
        else tank.HP.Should().BeLessThan(tank.MaxHP);
    }

    // ---- text ----

    [Fact]
    public void TheNewLines_AreInEveryLanguage()
    {
        string dir = Path.Combine(Leftovers1114BTests.RepoRoot(), "Localization");
        var keys = new List<string> { "combat.ward_stronger_holds", "combat.sage_ocean_memory_ally" };
        foreach (int slot in new[] { 1, 13, 16, 20, 22, 24 }) keys.Add($"spell.sage.{slot}.desc");
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            string json = File.ReadAllText(Path.Combine(dir, lang + ".json"));
            foreach (var k in keys) json.Should().Contain($"\"{k}\":", $"{lang} {k}");
        }
    }
}
