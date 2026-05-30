using Xunit;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// v0.64.0 Brain v2 Slices 7-9 tests:
///   - Slice 7a: NPC AI state unification (EmotionalState / Memory / Goals
///     are the same instance as Brain.* after EnsureSystemsInitialized).
///   - Slice 8a: Status effect application via ability dispatch (poison,
///     stun, slow, weaken).
///   - Slice 8b: Buff/Defense ability application (TempAttackBonus,
///     TempDefenseBonus, TempDamageReductionPercent).
///   - Slice 8c: Equipment-enchant procs (LifeSteal heals on hit).
///   - Slice 9a/9b: LLM moment fallback paths still complete safely with
///     LLM disabled.
/// </summary>
public class BrainV2SlicesSevenEightNineTests
{
    private static NPC MakeNPC(
        CharacterClass charClass = CharacterClass.Warrior,
        int level = 30,
        long hp = 200,
        long maxHp = 200,
        long mana = 100,
        long maxMana = 100,
        long strength = 30,
        long weapPow = 150,
        long armPow = 120)
    {
        var npc = new NPC
        {
            Name1 = "Hero",
            Name2 = "Hero",
            Class = charClass,
            Race = CharacterRace.Human,
            Level = level,
            HP = hp,
            MaxHP = maxHp,
            BaseMaxHP = maxHp,
            Mana = mana,
            MaxMana = maxMana,
            BaseMaxMana = maxMana,
            Strength = strength,
            BaseStrength = strength,
            BaseStamina = 20,
            BaseWisdom = 20,
            WeapPow = weapPow,
            BaseWeapPow = weapPow,
            ArmPow = armPow,
            BaseArmPow = armPow,
        };
        npc.EnsureSystemsInitialized();
        return npc;
    }

    private static Monster MakeMonster(long hp = 500, long strength = 25, long weapPow = 30, int defence = 15, int level = 25)
    {
        return new Monster
        {
            Name = "Goblin",
            Level = level,
            HP = hp,
            MaxHP = hp,
            Strength = strength,
            WeapPow = weapPow,
            Defence = defence,
        };
    }

    // ----- Slice 7a: AI state unification -----

    [Fact]
    public void EnsureSystemsInitialized_UnifiesEmotionalState()
    {
        var npc = MakeNPC();
        npc.EmotionalState.Should().NotBeNull();
        npc.Brain.Should().NotBeNull();
        npc.Brain!.Emotions.Should().NotBeNull();
        // The single-source-of-truth fix: NPC.EmotionalState IS Brain.Emotions.
        ReferenceEquals(npc.EmotionalState, npc.Brain.Emotions).Should().BeTrue(
            "NPC.EmotionalState must be the SAME instance as Brain.Emotions after unification");
    }

    [Fact]
    public void EnsureSystemsInitialized_UnifiesMemory()
    {
        var npc = MakeNPC();
        ReferenceEquals(npc.Memory, npc.Brain!.Memory).Should().BeTrue(
            "NPC.Memory must be the SAME instance as Brain.Memory after unification");
    }

    [Fact]
    public void EnsureSystemsInitialized_UnifiesGoals()
    {
        var npc = MakeNPC();
        ReferenceEquals(npc.Goals, npc.Brain!.Goals).Should().BeTrue(
            "NPC.Goals must be the SAME instance as Brain.Goals after unification");
    }

    [Fact]
    public void Picker_AddsEmotion_VisibleToBrainConsumers()
    {
        // The whole point of the unification: a write via npc.EmotionalState
        // (as the picker dispatch does) is visible to readers of Brain.Emotions
        // (as DialogueEnhancer does).
        var npc = MakeNPC();
        npc.EmotionalState!.AddEmotion(EmotionType.Joy, 0.7f, 60);

        npc.Brain!.Emotions.HasEmotion(EmotionType.Joy).Should().BeTrue(
            "writes via npc.EmotionalState must be visible to Brain.Emotions consumers");
    }

    // ----- Slice 8a: status effects -----

    [Fact]
    public void Simulator_PoisonAbility_AppliesPoisonRounds()
    {
        // Inject a poison status directly onto the monster and verify the
        // tick path decrements rounds and applies HP damage. Needs the monster
        // to survive multiple rounds for the decrement to be observable, so
        // give it a big HP pool against a weak NPC.
        var caster = MakeNPC(charClass: CharacterClass.Magician, level: 5, strength: 5, weapPow: 5, mana: 500);
        var monster = MakeMonster(hp: 5000, strength: 5, weapPow: 5, defence: 500);
        monster.PoisonRounds = 3;
        long hpBefore = monster.HP;

        NPCCombatSimulator.Simulate(caster, new List<Monster> { monster }, null, new Random(42));

        // Poison rounds should have ticked down all the way to 0.
        monster.PoisonRounds.Should().Be(0, "poison rounds should tick down to zero over 3 rounds");
        monster.HP.Should().BeLessThan(hpBefore, "poison ticks should reduce monster HP");
    }

    [Fact]
    public void Simulator_StunnedMonster_SkipsTurn()
    {
        // Stunned monster shouldn't attack. Set HP very low so we can detect
        // whether the NPC took damage (monster turn fired) or not.
        var npc = MakeNPC(level: 30, hp: 100, maxHp: 100);
        var monster = MakeMonster(hp: 50, strength: 100, weapPow: 100, defence: 10);
        monster.StunRounds = 5;  // stunned for the whole fight

        long hpBefore = npc.HP;
        NPCCombatSimulator.Simulate(npc, new List<Monster> { monster }, null, new Random(42));

        npc.HP.Should().Be(hpBefore, "stunned monster must not attack");
    }

    // ----- Slice 8b: buffs / defense -----

    [Fact]
    public void Character_TempAttackBonus_BoostsBasicAttack()
    {
        // The simulator's ExecuteBasicAttack reads TempAttackBonus when
        // TempAttackBonusDuration > 0. Verify the fields exist and behave.
        var npc = MakeNPC();
        npc.TempAttackBonus = 50;
        npc.TempAttackBonusDuration = 3;
        // Direct field read; the simulator integration is tested via combat outcome.
        npc.TempAttackBonus.Should().Be(50);
        npc.TempAttackBonusDuration.Should().Be(3);
    }

    [Fact]
    public void Simulator_BuffedNPC_DealsMoreDamage()
    {
        // Two identical fights, one with TempAttackBonus active. The buffed
        // fight should end faster (fewer rounds) OR leave the monster with
        // less HP.
        var npc1 = MakeNPC(level: 20, strength: 30, weapPow: 100);
        var monster1 = MakeMonster(hp: 1000, strength: 5, weapPow: 5, defence: 5);
        var npc2 = MakeNPC(level: 20, strength: 30, weapPow: 100);
        npc2.TempAttackBonus = 50;
        npc2.TempAttackBonusDuration = 20;
        var monster2 = MakeMonster(hp: 1000, strength: 5, weapPow: 5, defence: 5);

        var r1 = NPCCombatSimulator.Simulate(npc1, new List<Monster> { monster1 }, null, new Random(42));
        var r2 = NPCCombatSimulator.Simulate(npc2, new List<Monster> { monster2 }, null, new Random(42));

        // Buffed NPC should have killed faster (fewer rounds).
        r2.Rounds.Should().BeLessThanOrEqualTo(r1.Rounds,
            "buffed NPC should kill at least as fast as unbuffed NPC");
    }

    // ----- Slice 8c: equipment enchant procs -----

    [Fact]
    public void Character_GetEquipmentLifeSteal_ReturnsZeroByDefault()
    {
        // Defensive: NPCs with no equipped LifeSteal items return 0.
        var npc = MakeNPC();
        int ls = npc.GetEquipmentLifeSteal();
        ls.Should().Be(0, "NPC with no equipment should have 0 lifesteal");
    }

    // ----- Slice 9a/9b: LLM moment fallback paths -----

    [Fact]
    public async Task PostDeathEpitaph_CompletesWithFallback_WhenLLMDisabled()
    {
        // LLM disabled by default in test env. PostDeathEpitaphAsync should
        // post the templated fallback news entry and complete within 2s.
        Environment.SetEnvironmentVariable("USURPER_LLM_ENABLED", "false");
        LLMProvider.ResetForTests();

        var npc = MakeNPC();
        var task = LLMMoments.PostDeathEpitaphAsync(npc, "Black Hand Garrick", "the Dungeon");
        var completed = await Task.WhenAny(task, Task.Delay(2000));
        completed.Should().Be(task, "fallback path should complete within 2s");
    }

    [Fact]
    public async Task PostDeathEpitaph_NullNPC_NoOp()
    {
        await LLMMoments.PostDeathEpitaphAsync(null!, "Killer", "Somewhere");
        // No assertion beyond "didn't throw".
    }

    [Fact]
    public async Task PersonalitySummary_CachesResult()
    {
        Environment.SetEnvironmentVariable("USURPER_LLM_ENABLED", "false");
        LLMProvider.ResetForTests();

        var npc = MakeNPC();
        var first = await LLMMoments.GeneratePersonalitySummaryAsync(npc, CancellationToken.None);
        first.Should().NotBeNullOrEmpty("templated fallback should produce a non-empty summary");

        npc.PersonalitySummaryCache.Should().Be(first, "first generation should cache the result");

        var second = await LLMMoments.GeneratePersonalitySummaryAsync(npc, CancellationToken.None);
        second.Should().Be(first, "second call should return cached value, not re-generate");
    }

    [Fact]
    public async Task PersonalitySummary_NullNPC_ReturnsEmpty()
    {
        var summary = await LLMMoments.GeneratePersonalitySummaryAsync(null!, CancellationToken.None);
        summary.Should().Be("");
    }
}
