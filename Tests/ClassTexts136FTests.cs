using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.ClassTexts136Support;
using D = UsurperReborn.Tests.ClassTexts136DTests;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 X-F: the group F class texts (Tidesworn, Wavecaller, Cyclebreaker, Abysswarden, Voidreaver) state the code: the
/// soft taunt's 75 percent stick a round with no boss rule, Weaken as -30 percent attack and -20 percent defence, Marked
/// as +30 percent, and each ability's numbers from the template, GameConfig and one cast at fixed stats.
/// </summary>
[Collection("SharedGameSingletons")]
public class ClassTexts136FTests
{
    private static readonly string[] Abilities =
    {
        "undertow_stance", "riptide_strike", "abyssal_anchor", "dissonant_wave", "oceans_voice", "entropy_cascade",
        "temporal_prison", "corrupting_touch", "abyssal_eruption", "void_shroud", "void_rupture", "umbral_step",
        "timeline_split", "causality_loop", "devour", "cycles_end",
    };

    internal static readonly string[] Keys = Abilities.Select(a => $"ability.{a}.desc").Concat(new[]
    {
        "spell.wavecaller.5.desc", "spell.abysswarden.2.desc", "combat.abyssal_anchor_weaken", "combat.abyssal_anchor_immovable",
        "combat.ability_borrowed_power", "combat.spell_nocturas_whisper_cast", "combat.ability_tidal_harmony_ally",
        "combat.ability_causality_loop",
    }).ToArray();

    private const string Weaken = "(-30% attack, -20% defense)";

    private static ClassAbilitySystem.ClassAbility Ab(string id) => ClassAbilitySystem.GetAbility(id)!;
    private static string Desc(string id) => En($"ability.{id}.desc");

    private static List<Monster> Foes(int n = 2) => Enumerable.Range(0, n).Select(_ => G.Dummy()).ToList();

    [Fact]
    public void ClassTexts136F_EveryChangedKey_HasFiveLanguagesWithEnsPlaceholdersAndNumbers()
    {
        FiveLanguages(Keys);
        foreach (var a in Abilities) { DescLength($"ability.{a}.desc"); AbilityDescIsCode(a); }
        DescLength("spell.wavecaller.5.desc"); SpellDescIsCode(CharacterClass.Wavecaller, 5);
        DescLength("spell.abysswarden.2.desc"); SpellDescIsCode(CharacterClass.Abysswarden, 2);
        Fits79("combat.abyssal_anchor_weaken");
        Fits79("combat.abyssal_anchor_immovable");
        Fits79("combat.ability_borrowed_power", 50);
        Fits79("combat.spell_nocturas_whisper_cast", "A Very Long Monster Name");
        Fits79("combat.ability_tidal_harmony_ally", "A Long Teammate Name");
        Fits79("combat.ability_causality_loop", "A Very Long Monster Name");
    }

    [Fact]
    public void ClassTexts136F_Weaken_IsThirtyOffAttackTwentyOffDefense()
    {
        var m = G.Dummy(); m.Strength = 1000; m.WeapPow = 0;
        long full = m.GetAttackPower();
        m.WeakenRounds = 1;
        m.GetAttackPower().Should().Be((long)(full * 0.70f), "-30% attack");
        m.WeakenDefenceCut(100).Should().Be(80, "-20% defense");
    }

    [Theory]
    [InlineData("undertow_stance", CharacterClass.Tidesworn)]
    [InlineData("abyssal_anchor", CharacterClass.Abysswarden)]
    public void ClassTexts136F_SoftTaunts_StickAndWeaken(string id, CharacterClass cls)
    {
        var ab = Ab(id);
        Desc(id).Should().Contain($"{ab.Duration} rounds").And.Contain($"{GameConfig.SoftTauntStickChance}% roll a round")
            .And.Contain(Weaken).And.Contain($"+{ab.DefenseBonus}").And.NotContain("forced");
        var foes = Foes(3); foes[2].IsBoss = true;
        Run(id, Hero(cls), foes);
        foreach (var m in foes)
        {
            m.TauntStickChance.Should().Be(GameConfig.SoftTauntStickChance, "a boss too: no boss rule");
            m.TauntRoundsLeft.Should().Be(ab.Duration);
            m.WeakenRounds.Should().Be(ab.Duration);
        }
        if (id == "abyssal_anchor")
        {
            En("combat.abyssal_anchor_weaken").Should().Contain(Weaken);
            En("combat.abyssal_anchor_immovable").Should().NotContain("forced");
        }
    }

    [Fact]
    public void ClassTexts136F_RiptideStrike_WeakensTwoRounds()
    {
        Desc("riptide_strike").Should().Contain($"Weakens the target for 2 rounds {Weaken}").And.Contain("+40% damage vs weakened foes").And.NotContain("25%");
        var foe = G.Dummy();
        Run("riptide_strike", Hero(CharacterClass.Tidesworn), new List<Monster> { foe });
        foe.WeakenRounds.Should().Be(2);
    }

    [Fact]
    public void ClassTexts136F_DissonantWave_WeakenMarkAndStunRoll()
    {
        var ab = Ab("dissonant_wave");
        Desc("dissonant_wave").Should().Contain($"All enemies for {ab.Duration} rounds: weakened {Weaken} and marked (+{GameConfig.MarkedBonusPercent}% damage taken)")
            .And.Contain("25% chance to be stunned for 1 round");
        var foes = Foes(3);
        Run("dissonant_wave", Hero(CharacterClass.Wavecaller), foes);
        foes.Should().OnlyContain(m => m.WeakenRounds == ab.Duration && m.IsMarked && m.MarkedDuration == ab.Duration);
        foes.Should().OnlyContain(m => !m.IsStunned || m.StunDuration <= 1, "a stun lasts 1 round");
    }

    [Fact]
    public void ClassTexts136F_OceansVoice_NoAllyCrit()
    {
        var ab = Ab("oceans_voice");
        Desc("oceans_voice").Should().Contain($"All allies: +{ab.AttackBonus} ATK, +{ab.DefenseBonus} DEF for {ab.Duration} rounds")
            .And.Contain($"your missed crits roll again at {Percent(GameConfig.WavecallerOceansVoiceCritBonus)}%");
    }

    [Fact]
    public void ClassTexts136F_EntropyCascade_MarksForThirty()
    {
        var ab = Ab("entropy_cascade");
        Desc("entropy_cascade").Should().Contain($"{ab.BaseDamage} AoE damage. Enemies are marked for {ab.Duration} rounds: +{GameConfig.MarkedBonusPercent}% damage taken");
        var foes = Foes(2);
        Run("entropy_cascade", Hero(CharacterClass.Cyclebreaker, level: 80), foes);
        foes.Should().OnlyContain(m => m.IsMarked && m.MarkedDuration >= ab.Duration);
    }

    [Fact]
    public void ClassTexts136F_TemporalPrison_HoldNotInvulnerable()
    {
        var ab = Ab("temporal_prison");
        Desc("temporal_prison").Should().Contain($"Cannot act for {ab.Duration} rounds (boss: {GameConfig.MaxStunDurationBoss} round, resisted half the time)")
            .And.NotContain("no damage");
        Percent(GameConfig.BossStunResistChance).Should().Be(50);
        var foe = G.Dummy();
        var c = Run("temporal_prison", Hero(CharacterClass.Cyclebreaker, level: 80), new List<Monster> { foe });
        foe.IsStunned.Should().BeTrue();
        foe.StunDuration.Should().Be(ab.Duration);
    }

    [Fact]
    public void ClassTexts136F_CorruptingTouch_HitNowThenTicks()
    {
        var ab = Ab("corrupting_touch");
        Desc("corrupting_touch").Should().Contain($"Hits for {ab.BaseDamage} now, then {ab.BaseDamage} damage/round for {ab.Duration} rounds")
            .And.Contain("heals you half its damage");
        var foe = G.Dummy();
        var c = Run("corrupting_touch", Hero(CharacterClass.Abysswarden, level: 80), new List<Monster> { foe });
        c.Lost[0].Should().BeGreaterThan(0, "the cast hits at once");
        foe.CorruptingDotRounds.Should().Be(ab.Duration);
        foe.CorruptingDotTickDamage.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ClassTexts136F_AbyssalEruption_PoisonNotAFlatTwenty()
    {
        var ab = Ab("abyssal_eruption");
        Desc("abyssal_eruption").Should().Contain($"{ab.BaseDamage} AoE damage. Poisons all enemies for 3 rounds (3-5% of their max HP a round)").And.NotContain("20 damage");
        var foes = Foes(2);
        Run("abyssal_eruption", Hero(CharacterClass.Abysswarden, level: 80), foes);
        foes.Should().OnlyContain(m => m.PoisonRounds >= 3);
    }

    [Fact]
    public void ClassTexts136F_VoidShroudAndVoidRupture()
    {
        var shroud = Ab("void_shroud");
        Desc("void_shroud").Should().StartWith($"{Percent(GameConfig.VoidreaverReflectionPercent)}% of damage taken reflected")
            .And.Contain($"+{shroud.DefenseBonus} defense for {shroud.Duration} rounds");
        var rupture = Ab("void_rupture");
        Desc("void_rupture").Should().Contain($"{rupture.BaseDamage} AoE damage. Each enemy killed makes the others take 60 or 15% of the damage, whichever is more");
    }

    [Fact]
    public void ClassTexts136F_Symphony_CostsHalfMaxHp()
    {
        Loc.GetIn("en", "spell.wavecaller.5.desc").Should().Contain("Costs half your max HP").And.NotContain("-50% max HP");
        var c = D.Caster(CharacterClass.Wavecaller);
        c.Charisma = 10; c.BaseCharisma = 10;   // a Wavecaller's ward reads Charisma: at 10 it is the base
        c.HP = c.MaxHP;
        long max = c.MaxHP;
        var r = D.Cast(CharacterClass.Wavecaller, 5, false, c);
        c.MaxHP.Should().Be(max, "max HP is not lowered");
        c.HP.Should().Be(Math.Max(1, max - (long)(max * 0.50)), "it costs half the max HP");
        Loc.GetIn("en", "spell.wavecaller.5.desc").Should().Contain($"+{r.AttackBonus} attack and +{r.ProtectionBonus} defense (grow with level; defense with CHA too)");
        var bright = D.Caster(CharacterClass.Wavecaller); bright.Charisma = 40; bright.BaseCharisma = 40;
        D.Cast(CharacterClass.Wavecaller, 5, false, bright).ProtectionBonus.Should().BeGreaterThan(r.ProtectionBonus, "Charisma raises the defense");
    }

    [Fact]
    public void ClassTexts136F_NocturasWhisper_IsWeaken()
    {
        Loc.GetIn("en", "spell.abysswarden.2.desc").Should().Contain("Target weakened: -30% attack, -20% defense. Duration: 3 rounds");
        En("combat.spell_nocturas_whisper_cast").Should().Contain(Weaken);
        Loc.GetIn("hu", "combat.spell_nocturas_whisper_cast").Should().Contain("-30%").And.Contain("-20%");
        var r = D.Cast(CharacterClass.Abysswarden, 2, false);
        r.SpecialEffect.Should().Be("weaken");
        r.Duration.Should().Be(3);
    }

    [Fact]
    public void ClassTexts136F_UmbralStep_OneDodgeAndAHiddenCrit()
    {
        Desc("umbral_step").Should().Contain("Dodge the next attack against you").And.NotContain("all attacks");
        var hero = Hero(CharacterClass.Abysswarden, level: 80);
        hero.RemoveStatus(StatusEffect.Hidden);
        Run("umbral_step", hero);
        hero.DodgeNextAttack.Should().BeTrue();
        hero.HasStatus(StatusEffect.Hidden).Should().BeTrue();
    }

    [Fact]
    public void ClassTexts136F_TidalHarmonyAllyLine_NoFixedAmount()
    {
        En("combat.ability_tidal_harmony_ally").Should().NotContain("200").And.StartWith("  {0}");
    }

    [Fact]
    public void ClassTexts136F_TimelineSplit_DoubledFullAttacks_Q15()
    {
        var ab = Ab("timeline_split");
        Desc("timeline_split").Should().Contain($"basic attacks are doubled, at full damage, for {ab.Duration} rounds").And.NotContain("clone").And.NotContain("50%");
        var hero = Hero(CharacterClass.Cyclebreaker, level: 80);
        hero.RemoveStatus(StatusEffect.Haste);
        Run("timeline_split", hero);
        hero.HasStatus(StatusEffect.Haste).Should().BeTrue("Haste doubles the basic attacks");
    }

    [Fact]
    public void ClassTexts136F_CausalityLoop_ConfuseAndWeaken_Q16()
    {
        var ab = Ab("causality_loop");
        Desc("causality_loop").Should().Contain($"for {ab.Duration} rounds: confused and weakened {Weaken}").And.NotContain("damage equal");
        En("combat.ability_causality_loop").Should().Contain("confused and weakened").And.NotContain("violence");
        var foe = G.Dummy();
        Run("causality_loop", Hero(CharacterClass.Cyclebreaker, level: 80), new List<Monster> { foe });
        foe.IsConfused.Should().BeTrue();
        foe.ConfusedDuration.Should().Be(ab.Duration);
        foe.WeakenRounds.Should().Be(ab.Duration);
    }

    [Fact]
    public void ClassTexts136F_Devour_TheCastersHp()
    {
        var ab = Ab("devour");
        Desc("devour").Should().Contain($"{ab.BaseDamage} damage. Heals 30% dealt. While your HP is below 30%: damage x1.5");
        var hurt = Hero(CharacterClass.Voidreaver, level: 80); hurt.HP = hurt.MaxHP / 5;
        var well = Hero(CharacterClass.Voidreaver, level: 80);
        var a = Run("devour", hurt, new List<Monster> { G.Dummy() }, seed: 21);
        var b = Run("devour", well, new List<Monster> { G.Dummy() }, seed: 21);
        a.Result.Damage.Should().Be(b.Result.Damage);
        ((double)a.Lost[0] / b.Lost[0]).Should().BeApproximately(1.5, 0.02, "x1.5 when your own HP is under 30%");
    }

    [Fact]
    public void ClassTexts136F_CyclesEnd_IgnoresAllDefense()
    {
        var ab = Ab("cycles_end");
        Desc("cycles_end").Should().Contain($"{ab.BaseDamage} damage + 50 per NG+ cycle (max +250). Ignores all defense").And.NotContain("50% defense");
        var soft = G.Dummy(defence: 0, armour: 0);
        var hard = G.Dummy(defence: 10000, armour: 10000);
        var a = Run("cycles_end", Hero(CharacterClass.Cyclebreaker, level: 90), new List<Monster> { soft }, seed: 31);
        var b = Run("cycles_end", Hero(CharacterClass.Cyclebreaker, level: 90), new List<Monster> { hard }, seed: 31);
        a.Lost[0].Should().BeGreaterThan(0);
        b.Lost[0].Should().Be(a.Lost[0], "the target's defense takes nothing off");
    }
}
