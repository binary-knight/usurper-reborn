using Xunit;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperReborn.Tests;

/// <summary>
/// v0.64.0 Brain v2 Slice 3: life-event goal promotion + completion detection +
/// scorer combat-outcome feedback. Locks the v0.63.0 family memory types
/// (KilledMyParent / KilledMyFamily / LostFamilyMember / FamilyMemberBorn)
/// flowing into the goal stack and the scorer reading recent attacked
/// memories to bias away from combat verbs.
/// </summary>
public class GoalSystemSliceThreeTests
{
    private static NPC ConstructTestNPC(
        long hp = 100,
        long maxHp = 100,
        long gold = 1000,
        int level = 10,
        long baseWeapPow = 50,
        float vengefulness = 0.7f,
        float loyalty = 0.5f,
        bool married = false)
    {
        var npc = new NPC
        {
            Name1 = "Test",
            Name2 = "TestNPC",
            Class = CharacterClass.Warrior,
            Race = CharacterRace.Human,
            Level = level,
            HP = hp,
            MaxHP = maxHp,
            BaseMaxHP = maxHp,
            Gold = gold,
            BaseWeapPow = baseWeapPow,
            WeapPow = baseWeapPow,
            Married = married,
            IsMarried = married,
        };
        var profile = PersonalityProfile.GenerateForArchetype("commoner");
        profile.Vengefulness = vengefulness;
        profile.Loyalty = loyalty;
        npc.Personality = profile;
        npc.Brain = new NPCBrain(npc, profile);
        // NPC.EmotionalState is a separate instance from Brain.Emotions in this
        // codebase (pre-existing structural quirk -- both exist as parallel fields).
        // Unify here so test assertions on either accessor see the same writes.
        npc.EmotionalState = npc.Brain.Emotions;
        return npc;
    }

    private static WorldState EmptyWorldState() => new WorldState(new List<NPC>());

    // ----- Family-memory goal promotion -----

    [Fact]
    public void GenerateNewGoals_KilledMyParentMemory_PromotesAvengeGoal()
    {
        var npc = ConstructTestNPC(vengefulness: 0.8f);
        npc.Brain!.Memory!.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.KilledMyParent,
            Description = "Killer killed my parent Dad.",
            InvolvedCharacter = "Killer Bob",
            Importance = 0.95f,
            Timestamp = DateTime.Now,
        });

        npc.Brain.Goals!.UpdateGoals(npc, EmptyWorldState(), npc.Brain.Memory, npc.Brain.Emotions);

        var avenge = npc.Brain.Goals.AllGoals.FirstOrDefault(g => g.Name == "Avenge Killer Bob");
        avenge.Should().NotBeNull("KilledMyParent memory must promote a family-revenge goal");
        avenge!.Type.Should().Be(GoalType.Combat);
        avenge.TargetCharacter.Should().Be("Killer Bob");
        avenge.Priority.Should().BeGreaterThan(0.7f, "vengeful NPC should have high-priority family revenge");
    }

    [Fact]
    public void GenerateNewGoals_KilledMyFamilyMemory_PromotesAvengeGoal()
    {
        // Sibling / aunt / cousin death by named killer also promotes Avenge.
        var npc = ConstructTestNPC();
        npc.Brain!.Memory!.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.KilledMyFamily,
            InvolvedCharacter = "Sister Killer",
            Importance = 0.9f,
            Timestamp = DateTime.Now,
        });

        npc.Brain.Goals!.UpdateGoals(npc, EmptyWorldState(), npc.Brain.Memory, npc.Brain.Emotions);

        var avenge = npc.Brain.Goals.AllGoals.FirstOrDefault(g => g.Name == "Avenge Sister Killer");
        avenge.Should().NotBeNull();
        avenge!.Type.Should().Be(GoalType.Combat);
    }

    [Fact]
    public void GenerateNewGoals_FamilyMemberBornMemory_PromotesProtectFamily()
    {
        var npc = ConstructTestNPC(loyalty: 0.9f);
        npc.Brain!.Memory!.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.FamilyMemberBorn,
            InvolvedCharacter = "Newborn Niece",
            Importance = 0.85f,
            Timestamp = DateTime.Now,
        });

        npc.Brain.Goals!.UpdateGoals(npc, EmptyWorldState(), npc.Brain.Memory, npc.Brain.Emotions);

        var protect = npc.Brain.Goals.AllGoals.FirstOrDefault(g => g.Name == "Protect Family");
        protect.Should().NotBeNull("birth in the family should promote Protect Family");
        protect!.Type.Should().Be(GoalType.Social);
    }

    [Fact]
    public void GenerateNewGoals_LostFamilyMember_PromotesMourn_AndSadness()
    {
        var npc = ConstructTestNPC();
        npc.Brain!.Memory!.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.LostFamilyMember,
            InvolvedCharacter = "Grandfather",
            Importance = 0.85f,
            Timestamp = DateTime.Now,
        });

        npc.Brain.Goals!.UpdateGoals(npc, EmptyWorldState(), npc.Brain.Memory, npc.Brain.Emotions);

        var mourn = npc.Brain.Goals.AllGoals.FirstOrDefault(g => g.Name == "Mourn the Dead");
        mourn.Should().NotBeNull("family loss should promote a Personal mourning goal");
        mourn!.Type.Should().Be(GoalType.Personal);
        // EmotionalState should have picked up Sadness too.
        npc.Brain.Emotions!.HasEmotion(EmotionType.Sadness).Should().BeTrue();
    }

    [Fact]
    public void GenerateNewGoals_NoFamilyMemories_DoesNotPromoteFamilyGoals()
    {
        var npc = ConstructTestNPC();
        npc.Brain.Goals!.UpdateGoals(npc, EmptyWorldState(), npc.Brain.Memory, npc.Brain.Emotions);

        npc.Brain.Goals.AllGoals.Should().NotContain(g => g.Name.StartsWith("Avenge"));
        npc.Brain.Goals.AllGoals.Should().NotContain(g => g.Name == "Protect Family");
        npc.Brain.Goals.AllGoals.Should().NotContain(g => g.Name == "Mourn the Dead");
    }

    [Fact]
    public void GenerateNewGoals_DuplicateMemory_OnlyPromotesOneGoal()
    {
        // AddGoal dedups by name; a single killer producing N memories should
        // result in exactly one Avenge goal, not N.
        var npc = ConstructTestNPC();
        for (int i = 0; i < 3; i++)
        {
            npc.Brain!.Memory!.RecordEvent(new MemoryEvent
            {
                Type = MemoryType.KilledMyParent,
                InvolvedCharacter = "Repeat Killer",
                Importance = 0.9f,
                Timestamp = DateTime.Now,
            });
        }
        npc.Brain!.Goals!.UpdateGoals(npc, EmptyWorldState(), npc.Brain.Memory, npc.Brain.Emotions);

        var avengeCount = npc.Brain.Goals.AllGoals.Count(g => g.Name == "Avenge Repeat Killer");
        avengeCount.Should().Be(1, "AddGoal dedup must prevent duplicate Avenge entries");
    }

    // ----- Goal completion detection -----

    [Fact]
    public void IsGoalCompleted_FindBetterWeapons_CompletesWhenWeapPowMet()
    {
        // Goal name contains "Weapon"; should complete when BaseWeapPow >= level*8.
        // Lv 10 needs BaseWeapPow >= 80.
        // Note: GoalSystem.UpdateGoals prunes-already-completed BEFORE detecting
        // new completions, so a goal marked complete this tick is pruned on the
        // NEXT tick. We check IsCompleted after one tick (the semantic test) and
        // confirm pruning after two ticks (the lifecycle test).
        var npc = ConstructTestNPC(level: 10, baseWeapPow: 90);  // exceeds threshold
        npc.Brain!.Goals!.AddGoal(new Goal("Find Better Weapons", GoalType.Economic, 0.8f));

        npc.Brain.Goals.UpdateGoals(npc, EmptyWorldState(), npc.Brain.Memory, npc.Brain.Emotions);

        var goal = npc.Brain.Goals.AllGoals.FirstOrDefault(g => g.Name == "Find Better Weapons");
        goal.Should().NotBeNull();
        goal!.IsCompleted.Should().BeTrue("weapon goal must complete when gear baseline is met");

        // Second tick prunes the marked-complete goal.
        npc.Brain.Goals.UpdateGoals(npc, EmptyWorldState(), npc.Brain.Memory, npc.Brain.Emotions);
        npc.Brain.Goals.AllGoals.Should().NotContain(g => g.Name == "Find Better Weapons");
    }

    [Fact]
    public void IsGoalCompleted_FindBetterWeapons_DoesNotCompleteWhenUndergeared()
    {
        var npc = ConstructTestNPC(level: 10, baseWeapPow: 30);  // way under
        npc.Brain!.Goals!.AddGoal(new Goal("Find Better Weapons", GoalType.Economic, 0.8f));

        npc.Brain.Goals.UpdateGoals(npc, EmptyWorldState(), npc.Brain.Memory, npc.Brain.Emotions);

        npc.Brain.Goals.AllGoals.Should().Contain(g => g.Name == "Find Better Weapons",
            "weapon goal must persist while under-geared");
    }

    [Fact]
    public void IsGoalCompleted_LifePartner_CompletesWhenMarried()
    {
        var npc = ConstructTestNPC(married: true);
        npc.Brain!.Goals!.AddGoal(new Goal("Find Life Partner", GoalType.Social, 0.7f));

        npc.Brain.Goals.UpdateGoals(npc, EmptyWorldState(), npc.Brain.Memory, npc.Brain.Emotions);

        var goal = npc.Brain.Goals.AllGoals.FirstOrDefault(g => g.Name == "Find Life Partner");
        goal.Should().NotBeNull();
        goal!.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void IsGoalCompleted_MaintainHealth_CompletesAtFullHP()
    {
        var npc = ConstructTestNPC(hp: 100, maxHp: 100);
        npc.Brain!.Goals!.AddGoal(new Goal("Maintain Health", GoalType.Personal, 0.8f));

        npc.Brain.Goals.UpdateGoals(npc, EmptyWorldState(), npc.Brain.Memory, npc.Brain.Emotions);

        var goal = npc.Brain.Goals.AllGoals.FirstOrDefault(g => g.Name == "Maintain Health");
        goal.Should().NotBeNull();
        goal!.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void IsGoalCompleted_MaintainHealth_DoesNotCompleteWhileHurt()
    {
        var npc = ConstructTestNPC(hp: 50, maxHp: 100);
        npc.Brain!.Goals!.AddGoal(new Goal("Maintain Health", GoalType.Personal, 0.8f));

        npc.Brain.Goals.UpdateGoals(npc, EmptyWorldState(), npc.Brain.Memory, npc.Brain.Emotions);

        var goal = npc.Brain.Goals.AllGoals.FirstOrDefault(g => g.Name == "Maintain Health");
        goal.Should().NotBeNull();
        goal!.IsCompleted.Should().BeFalse("hurt NPC's health goal must persist");
    }

    [Fact]
    public void IsGoalCompleted_EarnMoney_CompletesAt1000Gold()
    {
        var npc = ConstructTestNPC(gold: 1500);
        npc.Brain!.Goals!.AddGoal(new Goal("Earn Money", GoalType.Economic, 0.7f));

        npc.Brain.Goals.UpdateGoals(npc, EmptyWorldState(), npc.Brain.Memory, npc.Brain.Emotions);

        var goal = npc.Brain.Goals.AllGoals.FirstOrDefault(g => g.Name == "Earn Money");
        goal.Should().NotBeNull();
        goal!.IsCompleted.Should().BeTrue();
    }

    // ----- Scorer combat-outcome feedback -----

    [Fact]
    public void Scorer_RecentAttackedMemory_PenalizesCombatVerbs()
    {
        // NPC was attacked recently. Combat verbs (dungeon, team_dungeon) should
        // be penalized; non-combat verbs (inn, train) should NOT be penalized.
        var npc = ConstructTestNPC();
        npc.Brain!.Memory!.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.Attacked,
            InvolvedCharacter = "Some Bandit",
            Importance = 0.9f,
            Timestamp = DateTime.Now,
        });

        var candidates = new List<(string, double)>
        {
            ("dungeon", 1.0),
            ("inn", 1.0),
        };
        var pick = BrainV2Scorer.PickAction(npc, candidates, new Random(42));
        pick.Should().Be("inn", "recent attacked memory must penalize dungeon over inn");
    }

    [Fact]
    public void Scorer_NoRecentAttackMemory_DoesNotPenalizeCombat()
    {
        // No attacked memory. Combat verbs are scored normally.
        var goal = new Goal("Defend Territory", GoalType.Combat, 0.9f);
        var npc = ConstructTestNPC(hp: 100, maxHp: 100);
        npc.Brain!.Goals!.AddGoal(goal);

        var candidates = new List<(string, double)>
        {
            ("dungeon", 1.0),
            ("inn", 1.0),
        };
        var pick = BrainV2Scorer.PickAction(npc, candidates, new Random(42));
        pick.Should().Be("dungeon", "combat goal without recent attack should still pick combat verb");
    }
}
