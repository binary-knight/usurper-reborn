using Xunit;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperReborn.Tests;

/// <summary>
/// v0.64.0 Brain v2 Slice 2: utility-scorer tests.
///
/// Goal: lock the three scoring layers (goal alignment, need satisfaction,
/// recency penalty) so that future tuning passes don't silently regress them.
/// Tests use ConstructTestNPC to build a minimal NPC + Brain + Personality;
/// the scorer is a pure function over those + the candidate list.
/// </summary>
public class BrainV2ScorerTests
{
    private static Random TestRng() => new Random(42);

    private static NPC ConstructTestNPC(
        long hp = 100,
        long maxHp = 100,
        long gold = 1000,
        int level = 10,
        long experience = 0,
        long baseWeapPow = -1,  // -1 = default to Level*12 (well-geared) per v0.64.0 Slice 6 scorer
        CharacterClass charClass = CharacterClass.Warrior,
        float impulsiveness = 0.3f,
        Goal? topGoal = null)
    {
        // v0.64.0 Slice 6: helper defaults to "well-geared" NPC so tests that
        // don't explicitly exercise the gear-gap path don't accidentally trip
        // the shop boost from missing-armor. Tests that DO want under-geared
        // can pass an explicit baseWeapPow.
        long weapPow = baseWeapPow >= 0 ? baseWeapPow : level * 12;
        long armPow = level * 10;
        var npc = new NPC
        {
            Name1 = "Test",
            Name2 = "Test",
            Class = charClass,
            Race = CharacterRace.Human,
            Level = level,
            HP = hp,
            MaxHP = maxHp,
            BaseMaxHP = maxHp,
            Gold = gold,
            Experience = experience,
            BaseWeapPow = weapPow,
            WeapPow = weapPow,
            BaseArmPow = armPow,
            ArmPow = armPow,
        };
        var profile = PersonalityProfile.GenerateForArchetype("commoner");
        profile.Impulsiveness = impulsiveness;
        npc.Personality = profile;
        npc.Brain = new NPCBrain(npc, profile);
        if (topGoal != null) npc.Brain.Goals.AddGoal(topGoal);
        return npc;
    }

    private static List<(string action, double weight)> SimpleCandidates() => new()
    {
        ("shop", 1.0),
        ("train", 1.0),
        ("inn", 1.0),
        ("heal", 1.0),
        ("dungeon", 1.0),
        ("move", 1.0),
        ("levelup", 1.0),
        ("team_dungeon", 1.0),
        ("dark_alley", 1.0),
        ("bank", 1.0),
    };

    [Fact]
    public void PickAction_EmptyCandidates_ReturnsMoveAsDefault()
    {
        var npc = ConstructTestNPC();
        var pick = BrainV2Scorer.PickAction(npc, new List<(string, double)>(), TestRng());
        pick.Should().Be("move", "scorer must always return a safe default");
    }

    [Fact]
    public void PickAction_LowHP_PrioritizesHeal()
    {
        // HP at 20% (urgent band). Heal should dominate even if other candidates
        // have equal base weight.
        var npc = ConstructTestNPC(hp: 20, maxHp: 100);
        var pick = BrainV2Scorer.PickAction(npc, SimpleCandidates(), TestRng());
        pick.Should().Be("heal", "urgent low HP must promote heal over all else");
    }

    [Fact]
    public void PickAction_LowHP_RefusesDungeon()
    {
        // Even if dungeon has a high base weight, low HP should suppress it.
        var npc = ConstructTestNPC(hp: 20, maxHp: 100);
        var skewedCandidates = new List<(string, double)>
        {
            ("dungeon", 10.0),     // very high base
            ("team_dungeon", 10.0),
            ("heal", 1.0),
        };
        var pick = BrainV2Scorer.PickAction(npc, skewedCandidates, TestRng());
        new[] { "dungeon", "team_dungeon" }.Should().NotContain(pick,
            "NPC at 20% HP must not pick combat verbs even when base weight is high");
    }

    [Fact]
    public void PickAction_LevelupReady_DominatesScore()
    {
        // NPC has enough XP for next level. Levelup should crush other options.
        long xpNeeded = GameConfig.GetExperienceForLevel(11);
        var npc = ConstructTestNPC(level: 10, experience: xpNeeded);
        var pick = BrainV2Scorer.PickAction(npc, SimpleCandidates(), TestRng());
        pick.Should().Be("levelup", "ready-to-level NPC should max levelup priority");
    }

    [Fact]
    public void PickAction_LevelupNotReady_DemotesLevelup()
    {
        // NPC has zero XP. Levelup should be heavily penalized.
        var npc = ConstructTestNPC(level: 10, experience: 0);
        var candidates = new List<(string, double)>
        {
            ("levelup", 10.0),  // high base
            ("train", 1.0),
        };
        var pick = BrainV2Scorer.PickAction(npc, candidates, TestRng());
        pick.Should().Be("train", "non-eligible levelup must not win even with high base weight");
    }

    [Fact]
    public void PickAction_EconomicGoal_PromotesShop()
    {
        // NPC with an Economic top goal should favor shop over equal-weighted alternatives.
        var goal = new Goal("Find Better Weapons", GoalType.Economic, 0.9f);
        var npc = ConstructTestNPC(topGoal: goal);
        var candidates = new List<(string, double)>
        {
            ("shop", 1.0),
            ("inn", 1.0),
            ("move", 1.0),
        };
        var pick = BrainV2Scorer.PickAction(npc, candidates, TestRng());
        pick.Should().Be("shop", "Economic goal + matching name should promote shop");
    }

    [Fact]
    public void PickAction_SocialGoal_PromotesLovestreetForPartnerSeeker()
    {
        // Social goal "Find Life Partner" should promote love_street over inn.
        var goal = new Goal("Find Life Partner", GoalType.Social, 0.9f);
        var npc = ConstructTestNPC(topGoal: goal);
        var candidates = new List<(string, double)>
        {
            ("love_street", 1.0),
            ("inn", 1.0),
            ("dungeon", 1.0),
        };
        var pick = BrainV2Scorer.PickAction(npc, candidates, TestRng());
        pick.Should().Be("love_street", "Life Partner social goal should pick love_street");
    }

    [Fact]
    public void PickAction_CombatGoal_PromotesDungeon()
    {
        // Combat goal should promote dungeon when HP is healthy.
        var goal = new Goal("Defend Territory", GoalType.Combat, 0.9f);
        var npc = ConstructTestNPC(hp: 100, maxHp: 100, topGoal: goal);
        var candidates = new List<(string, double)>
        {
            ("dungeon", 1.0),
            ("inn", 1.0),
            ("shop", 1.0),
        };
        var pick = BrainV2Scorer.PickAction(npc, candidates, TestRng());
        pick.Should().Be("dungeon", "Combat goal with full HP should pick dungeon");
    }

    [Fact]
    public void PickAction_GearGap_PromotesShopping()
    {
        // v0.64.0 Slice 6: scorer reads live WeapPow (gear-influenced after
        // RecalculateStats), expected baseline is Level*12 (well-geared).
        // Lv 20 NPC at WeapPow 30 is < 40% of expected 240 = critically
        // under-geared. Should pick shop.
        var npc = ConstructTestNPC(level: 20, baseWeapPow: 30, gold: 500);
        var candidates = new List<(string, double)>
        {
            ("shop", 1.0),
            ("inn", 1.0),
            ("move", 1.0),
        };
        var pick = BrainV2Scorer.PickAction(npc, candidates, TestRng());
        pick.Should().Be("shop", "critical gear gap + spending money should pick shop");
    }

    [Fact]
    public void PickAction_WellGeared_DoesNotPrioritizeShopping()
    {
        // v0.64.0 Slice 6: closed-loop check. Lv 20 NPC at WeapPow 250
        // (above expected 240) is well-geared. Shop should NOT win over an
        // equally-weighted inn -- the gear-gap boost stops firing once gear
        // catches up. Recency penalty on shop (NPC just shopped earlier)
        // additionally suppresses it.
        var npc = ConstructTestNPC(level: 20, baseWeapPow: 250, gold: 1000);
        npc.Brain!.MarkActivity("shop");  // simulate "just bought gear"
        var candidates = new List<(string, double)>
        {
            ("shop", 1.0),
            ("inn", 1.0),
            ("train", 1.0),
        };
        var pick = BrainV2Scorer.PickAction(npc, candidates, TestRng());
        pick.Should().NotBe("shop", "well-geared NPC + recency penalty should stop shopping");
    }

    [Fact]
    public void PickAction_ArmorGap_PromotesShoppingToo()
    {
        // v0.64.0 Slice 6: armor gap mirrors weapon gap. Lv 20 NPC with weapon
        // OK but very low ArmPow should still pick shop.
        var npc = ConstructTestNPC(level: 20, baseWeapPow: 200, gold: 500);
        npc.ArmPow = 30;  // critical gap: 30 vs expected 200, < 40%
        var candidates = new List<(string, double)>
        {
            ("shop", 1.0),
            ("inn", 1.0),
            ("move", 1.0),
        };
        var pick = BrainV2Scorer.PickAction(npc, candidates, TestRng());
        pick.Should().Be("shop", "armor gap alone should still trigger shop boost");
    }

    [Fact]
    public void PickAction_BankWithdrawal_BeatsAlternativesWhenBroke()
    {
        // NPC broke but has bank gold. bank should beat inn.
        var npc = ConstructTestNPC(gold: 50);
        npc.BankGold = 5000;
        var candidates = new List<(string, double)>
        {
            ("bank", 1.0),
            ("inn", 1.0),
            ("move", 1.0),
        };
        var pick = BrainV2Scorer.PickAction(npc, candidates, TestRng());
        pick.Should().Be("bank", "broke NPC with bank deposits should head to bank");
    }

    [Fact]
    public void PickAction_RecencyPenalty_AvoidsJustDoneVerb()
    {
        // NPC just did shop. With equal base weights, recency should flip the
        // pick toward the not-recently-done verb. (Recency penalty is 0.3x
        // within 5 minutes, so it beats equal base; it deliberately does NOT
        // override a 5x base-weight advantage on the other side -- recency is
        // a tiebreaker, not a veto.)
        var npc = ConstructTestNPC();
        npc.Brain!.MarkActivity("shop");  // recent
        var candidates = new List<(string, double)>
        {
            ("shop", 1.0),
            ("train", 1.0),
        };
        var pick = BrainV2Scorer.PickAction(npc, candidates, TestRng());
        pick.Should().NotBe("shop", "recency penalty should flip pick away from just-done verb when alternatives are competitive");
    }

    [Fact]
    public void PickAction_NoGoal_FallsBackToBaseWeights()
    {
        // NPC with no goals at all. Highest base weight should win.
        var npc = ConstructTestNPC();
        npc.Brain!.Goals.AllGoals.Clear();
        var candidates = new List<(string, double)>
        {
            ("shop", 1.0),
            ("train", 5.0),   // highest
            ("inn", 1.0),
        };
        var pick = BrainV2Scorer.PickAction(npc, candidates, TestRng());
        pick.Should().Be("train", "no goals + highest base should win");
    }

    [Fact]
    public void PickAction_DeterministicForSameInputs()
    {
        // Same NPC state + same RNG seed should produce same pick. Lock this
        // because the A/B telemetry comparison depends on reproducibility for
        // diagnosing surprising outcomes.
        var npc = ConstructTestNPC();
        var candidates = SimpleCandidates();
        var pickA = BrainV2Scorer.PickAction(npc, candidates, new Random(7));
        var pickB = BrainV2Scorer.PickAction(npc, candidates, new Random(7));
        pickA.Should().Be(pickB, "scorer must be deterministic for identical inputs");
    }
}
