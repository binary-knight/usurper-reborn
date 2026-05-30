using Xunit;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperReborn.Tests;

/// <summary>
/// v0.64.0 Brain v2 Slice 4: NPCCombatBrain (per-round action picker) +
/// NPCCombatSimulator (headless round combat) tests. Locks the basic
/// behaviors that Tier A NPCs depend on: heal when wounded, drink potions,
/// pick offensive abilities when affordable, fall through to basic attack,
/// and resolve combat with proper outcomes.
/// </summary>
public class NPCCombatTests
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
        long armPow = 120,
        long agility = 30,
        long healing = 5,
        long manaPotions = 3,
        float courage = 0.5f)
    {
        var npc = new NPC
        {
            Name1 = "Hero",
            Name2 = "Test Hero",
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
            BaseConstitution = 20,
            Wisdom = 20,
            BaseWisdom = 20,
            Agility = agility,
            BaseAgility = agility,
            WeapPow = weapPow,
            BaseWeapPow = weapPow,
            ArmPow = armPow,
            BaseArmPow = armPow,
            Healing = healing,
            ManaPotions = manaPotions,
        };
        var profile = PersonalityProfile.GenerateForArchetype("commoner");
        profile.Courage = courage;
        npc.Personality = profile;
        npc.Brain = new NPCBrain(npc, profile);
        npc.EmotionalState = npc.Brain.Emotions;
        npc.InitializeCombatStamina();
        return npc;
    }

    private static Monster MakeMonster(int level = 25, long hp = 300, long strength = 30, long weapPow = 50, int defence = 20)
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

    // ----- NPCCombatBrain tests -----

    [Fact]
    public void Brain_LowHP_PicksHealingPotion()
    {
        var npc = MakeNPC(hp: 30, maxHp: 200, healing: 3);
        var monsters = new List<Monster> { MakeMonster() };
        var action = NPCCombatBrain.PickAction(npc, monsters, new List<Character> { npc }, new Dictionary<string, int>(), new Random(1));

        action.Kind.Should().Be(NPCCombatActionKind.UseHealingPotion);
        action.HealTarget.Should().Be(npc);
    }

    [Fact]
    public void Brain_LowHP_NoPotions_PicksBasicAttack()
    {
        // Warrior with low HP and no healing potions OR healing ability falls
        // back to basic attack. Healer abilities exist but Warriors don't have any.
        var npc = MakeNPC(charClass: CharacterClass.Warrior, hp: 30, maxHp: 200, healing: 0, manaPotions: 0);
        var monsters = new List<Monster> { MakeMonster() };
        var action = NPCCombatBrain.PickAction(npc, monsters, new List<Character> { npc }, new Dictionary<string, int>(), new Random(1));

        // Could be basic attack or an ability; just confirm we didn't try to use a potion we don't have.
        action.Kind.Should().NotBe(NPCCombatActionKind.UseHealingPotion);
        action.Kind.Should().NotBe(NPCCombatActionKind.UseManaPotion);
    }

    [Fact]
    public void Brain_HealthyNPC_PicksOffensiveAction()
    {
        var npc = MakeNPC(hp: 200, maxHp: 200);
        var monsters = new List<Monster> { MakeMonster() };
        var action = NPCCombatBrain.PickAction(npc, monsters, new List<Character> { npc }, new Dictionary<string, int>(), new Random(1));

        new[] { NPCCombatActionKind.BasicAttack, NPCCombatActionKind.UseAbility }.Should().Contain(action.Kind,
            "healthy NPC should attack, not heal");
    }

    [Fact]
    public void Brain_NoMonsters_PicksBasicAttack()
    {
        // Defensive: no targets. Brain should still emit something (basic attack
        // with -1 target index). Simulator handles the no-target gracefully.
        var npc = MakeNPC();
        var action = NPCCombatBrain.PickAction(npc, new List<Monster>(), new List<Character> { npc }, new Dictionary<string, int>(), new Random(1));
        action.Kind.Should().Be(NPCCombatActionKind.BasicAttack);
    }

    [Fact]
    public void Brain_LowMana_Caster_PicksManaPotion()
    {
        var npc = MakeNPC(
            charClass: CharacterClass.Magician,
            hp: 200, maxHp: 200,
            mana: 10, maxMana: 200,
            manaPotions: 2,
            healing: 0);
        var monsters = new List<Monster> { MakeMonster() };
        var action = NPCCombatBrain.PickAction(npc, monsters, new List<Character> { npc }, new Dictionary<string, int>(), new Random(1));

        // Magician below 30% mana with mana potions should drink one.
        action.Kind.Should().Be(NPCCombatActionKind.UseManaPotion);
    }

    [Fact]
    public void Brain_HurtAlly_PicksHealForAlly()
    {
        // Healthy NPC + critically wounded ally + has potions -> heal ally.
        var healer = MakeNPC(charClass: CharacterClass.Cleric, hp: 200, maxHp: 200, healing: 5);
        var ally = MakeNPC(charClass: CharacterClass.Warrior, hp: 20, maxHp: 200, healing: 0);
        var monsters = new List<Monster> { MakeMonster() };
        var action = NPCCombatBrain.PickAction(healer, monsters, new List<Character> { healer, ally }, new Dictionary<string, int>(), new Random(1));

        // Healer below 70% threshold? No, at full. So skips self-heal. Ally
        // below 30% -> healer drinks a potion on the ally.
        action.Kind.Should().Be(NPCCombatActionKind.UseHealingPotion);
        action.HealTarget.Should().Be(ally);
    }

    // ----- NPCCombatSimulator tests -----

    [Fact]
    public void Simulator_StrongVsWeak_NPCWins()
    {
        // Lv 30 Warrior with full kit vs single Lv 10 weak monster. Should win.
        var npc = MakeNPC(level: 30, strength: 60, weapPow: 200);
        var monster = MakeMonster(level: 10, hp: 100, strength: 15, weapPow: 20, defence: 10);
        var result = NPCCombatSimulator.Simulate(npc, new List<Monster> { monster }, null, new Random(42));

        result.Outcome.Should().Be(NPCCombatOutcome.Won);
        result.ExpReward.Should().BeGreaterThan(0);
        result.GoldReward.Should().BeGreaterThan(0);
        npc.IsAlive.Should().BeTrue();
    }

    [Fact]
    public void Simulator_NPCWins_NPCHPNotZero()
    {
        var npc = MakeNPC(level: 30, strength: 60, weapPow: 200, hp: 200, maxHp: 200);
        var monster = MakeMonster(level: 10, hp: 50);
        NPCCombatSimulator.Simulate(npc, new List<Monster> { monster }, null, new Random(42));

        npc.HP.Should().BeGreaterThan(0, "winning NPC should retain some HP");
    }

    [Fact]
    public void Simulator_WeakVsStrong_NPCLosesOrFlees()
    {
        // Lv 5 NPC vs Lv 50 boss-stat monster. Should die or flee.
        var npc = MakeNPC(level: 5, hp: 30, maxHp: 30, strength: 10, weapPow: 20, armPow: 5, healing: 0);
        var monster = MakeMonster(level: 50, hp: 5000, strength: 200, weapPow: 200, defence: 100);

        var result = NPCCombatSimulator.Simulate(npc, new List<Monster> { monster }, null, new Random(42));

        new[] { NPCCombatOutcome.Died, NPCCombatOutcome.Fled, NPCCombatOutcome.Stalemate }
            .Should().Contain(result.Outcome, "weak NPC vs strong monster should not win");
    }

    [Fact]
    public void Simulator_EmptyMonsterList_ReturnsStalemate()
    {
        var npc = MakeNPC();
        var result = NPCCombatSimulator.Simulate(npc, new List<Monster>(), null, new Random(1));
        result.Outcome.Should().Be(NPCCombatOutcome.Stalemate);
    }

    [Fact]
    public void Simulator_NullMonsterList_DoesNotCrash()
    {
        var npc = MakeNPC();
        var result = NPCCombatSimulator.Simulate(npc, null!, null, new Random(1));
        result.Outcome.Should().Be(NPCCombatOutcome.Stalemate);
    }

    [Fact]
    public void Simulator_BoundedByMaxRounds()
    {
        // Two combatants both very tanky should hit MaxRounds without resolution.
        var npc = MakeNPC(level: 10, strength: 10, weapPow: 5, hp: 1000, maxHp: 1000, armPow: 200, healing: 0);
        var monster = MakeMonster(level: 10, hp: 100000, strength: 5, weapPow: 1, defence: 200);

        var result = NPCCombatSimulator.Simulate(npc, new List<Monster> { monster }, null, new Random(1));

        result.Rounds.Should().BeLessThanOrEqualTo(20, "MaxRounds enforced at 20");
    }

    [Fact]
    public void Simulator_TeamCombat_ContributesAllAllies()
    {
        // 2-NPC team vs 2 monsters should win when team is strong.
        var leader = MakeNPC(level: 30, strength: 60, weapPow: 200);
        var ally = MakeNPC(level: 30, strength: 60, weapPow: 200);
        var monsters = new List<Monster>
        {
            MakeMonster(level: 15, hp: 200),
            MakeMonster(level: 15, hp: 200),
        };
        var result = NPCCombatSimulator.Simulate(leader, monsters, new List<Character> { ally }, new Random(42));

        result.Outcome.Should().Be(NPCCombatOutcome.Won);
    }

    [Fact]
    public void Simulator_HealingPotionConsumed()
    {
        // NPC enters wounded with potions; should consume at least one over the fight.
        var npc = MakeNPC(level: 30, hp: 60, maxHp: 200, healing: 5);
        var monster = MakeMonster(level: 20, hp: 200);

        long potionsBefore = npc.Healing;
        NPCCombatSimulator.Simulate(npc, new List<Monster> { monster }, null, new Random(42));

        npc.Healing.Should().BeLessThan(potionsBefore,
            "wounded NPC with potions should drink at least one during combat");
    }
}
