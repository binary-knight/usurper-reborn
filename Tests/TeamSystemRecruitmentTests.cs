using Xunit;
using FluentAssertions;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// Unit tests for v0.57.11 relationship-aware recruitment helpers in TeamSystem.
/// The helpers under test are pure-ish (they read RelationshipSystem + RomanceTracker
/// singletons) so each test resets those systems to a known state. In single-player
/// test mode there's no SessionContext so the singletons just store in a fallback slot.
/// </summary>
public class TeamSystemRecruitmentTests
{
    private static Character MakePlayer(long level = 20)
    {
        var p = new Character
        {
            Name1 = "TestPlayer",
            Name2 = "TestPlayer",
            ID = "testplayer",
            Level = (int)level,
        };
        return p;
    }

    private static NPC MakeNPC(string id, string name, int level = 15, CharacterClass charClass = CharacterClass.Warrior)
    {
        var n = new NPC
        {
            ID = id,
            Name1 = name,
            Name2 = name,
            Level = level,
            Class = charClass,
            HP = 100,
            MaxHP = 100,
            Strength = 10,
            Defence = 5,
            Agility = 5,
            CurrentLocation = "Main Street",
        };
        return n;
    }

    /// <summary>
    /// Sets the relationship score between two characters directly by poking
    /// the internal record. The public API (<c>UpdateRelationship</c>) only
    /// steps the score by one tier at a time and is gated by daily caps, which
    /// makes it cumbersome for tests.
    /// </summary>
    private static void SetScore(Character a, Character b, int score)
    {
        var rec = RelationshipSystem.GetOrCreateRelationship(a, b);
        if (rec.Name1 == a.Name)
            rec.Relation1 = score;
        else
            rec.Relation2 = score;
    }

    private static void ResetWorld()
    {
        RelationshipSystem.Instance.Reset();
        RomanceTracker.Instance.Reset();
    }

    [Fact]
    public void GetRecruitmentBand_StrangerIsNeutral()
    {
        ResetWorld();
        var player = MakePlayer();
        var npc = MakeNPC("n1", "Aldric");

        var (band, mult) = TeamSystem.GetRecruitmentBand(player, npc);

        band.Should().Be(TeamSystem.RecruitmentBand.Neutral);
        mult.Should().Be(GameConfig.RecruitPriceNormal);
    }

    [Fact]
    public void GetRecruitmentBand_FriendshipTierGivesDiscount()
    {
        ResetWorld();
        var player = MakePlayer();
        var npc = MakeNPC("n2", "Friendly");
        SetScore(player, npc, GameConfig.RelationFriendship);

        var (band, mult) = TeamSystem.GetRecruitmentBand(player, npc);

        band.Should().Be(TeamSystem.RecruitmentBand.Friend);
        mult.Should().Be(GameConfig.RecruitPriceFriendship);
    }

    [Fact]
    public void GetRecruitmentBand_TrustTierGivesMidDiscount()
    {
        ResetWorld();
        var player = MakePlayer();
        var npc = MakeNPC("n3", "Trusted");
        SetScore(player, npc, GameConfig.RelationTrust);

        var (band, mult) = TeamSystem.GetRecruitmentBand(player, npc);

        band.Should().Be(TeamSystem.RecruitmentBand.Friend);
        mult.Should().Be(GameConfig.RecruitPriceTrust);
    }

    [Fact]
    public void GetRecruitmentBand_AngerTierAppliesSurcharge()
    {
        ResetWorld();
        var player = MakePlayer();
        var npc = MakeNPC("n4", "Angry");
        SetScore(player, npc, GameConfig.RelationAnger);

        var (band, mult) = TeamSystem.GetRecruitmentBand(player, npc);

        band.Should().Be(TeamSystem.RecruitmentBand.Rival);
        mult.Should().Be(GameConfig.RecruitPriceAnger);
    }

    [Fact]
    public void GetRecruitmentBand_EnemyTierRefuses()
    {
        ResetWorld();
        var player = MakePlayer();
        var npc = MakeNPC("n5", "Hostile");
        SetScore(player, npc, GameConfig.RelationEnemy);

        var (band, _) = TeamSystem.GetRecruitmentBand(player, npc);

        band.Should().Be(TeamSystem.RecruitmentBand.Refused);
    }

    [Fact]
    public void GetRecruitmentBand_HateTierRefuses()
    {
        ResetWorld();
        var player = MakePlayer();
        var npc = MakeNPC("n6", "Hated");
        SetScore(player, npc, GameConfig.RelationHate);

        var (band, _) = TeamSystem.GetRecruitmentBand(player, npc);

        band.Should().Be(TeamSystem.RecruitmentBand.Refused);
    }

    [Fact]
    public void GetRecruitmentBand_LoverIsHidden()
    {
        ResetWorld();
        var player = MakePlayer();
        var npc = MakeNPC("n7", "Lover");
        RomanceTracker.Instance.AddLover(npc.ID);

        var (band, _) = TeamSystem.GetRecruitmentBand(player, npc);

        band.Should().Be(TeamSystem.RecruitmentBand.Hidden);
    }

    [Fact]
    public void GetRecruitmentBand_SpouseIsHidden()
    {
        ResetWorld();
        var player = MakePlayer();
        var npc = MakeNPC("n8", "Spouse");
        RomanceTracker.Instance.AddSpouse(npc.ID);

        var (band, _) = TeamSystem.GetRecruitmentBand(player, npc);

        band.Should().Be(TeamSystem.RecruitmentBand.Hidden);
    }

    [Fact]
    public void GetRecruitmentBand_HiddenOverridesFriendshipScore()
    {
        // A lover who ALSO has RelationshipSystem score of Friendship (40)
        // should still be Hidden. The romance filter takes precedence over the
        // numeric score so partners stay off the payroll.
        ResetWorld();
        var player = MakePlayer();
        var npc = MakeNPC("n9", "Beloved");
        RomanceTracker.Instance.AddLover(npc.ID);
        SetScore(player, npc, GameConfig.RelationFriendship);

        var (band, _) = TeamSystem.GetRecruitmentBand(player, npc);

        band.Should().Be(TeamSystem.RecruitmentBand.Hidden);
    }

    [Fact]
    public void GetRecruitmentBaseCost_AppliesLevelGapDiscount()
    {
        var player = MakePlayer(30);
        var npc = MakeNPC("n10", "Junior", 15);

        long cost = TeamSystem.GetRecruitmentBaseCost(npc, player);

        // Under 0.7× discount: 15 * 2000 + 20 * 20 = 30400; ×0.7 = 21280
        cost.Should().BeLessThan(30400);
        cost.Should().BeGreaterThan(100);
    }

    [Fact]
    public void GetRecruitmentBaseCost_FloorsAtHundred()
    {
        var player = MakePlayer(100);
        var npc = MakeNPC("n11", "Tiny", 1);
        npc.Strength = 0;
        npc.Defence = 0;
        npc.Agility = 0;

        long cost = TeamSystem.GetRecruitmentBaseCost(npc, player);

        cost.Should().BeGreaterOrEqualTo(100);
    }

    [Fact]
    public void GetRecruitmentCost_ReturnsNegOneForRefused()
    {
        ResetWorld();
        var player = MakePlayer();
        var npc = MakeNPC("n12", "Foe");
        SetScore(player, npc, GameConfig.RelationHate);

        long cost = TeamSystem.GetRecruitmentCost(player, npc);

        cost.Should().Be(-1);
    }

    [Fact]
    public void IsRecruitable_FiltersSpecialNPCs()
    {
        ResetWorld();
        var player = MakePlayer();
        var npc = MakeNPC("n13", "Seth");
        npc.IsSpecialNPC = true;

        var recruitable = TeamSystem.IsRecruitable(player, npc, out _);

        recruitable.Should().BeFalse();
    }

    [Fact]
    public void IsRecruitable_FiltersPrisoners()
    {
        ResetWorld();
        var player = MakePlayer();
        var npc = MakeNPC("n14", "Convict");
        npc.DaysInPrison = 3;

        var recruitable = TeamSystem.IsRecruitable(player, npc, out _);

        recruitable.Should().BeFalse();
    }

    [Fact]
    public void IsRecruitable_FiltersNPCsAlreadyOnTeams()
    {
        ResetWorld();
        var player = MakePlayer();
        var npc = MakeNPC("n15", "Loyal");
        npc.Team = "Other Gang";

        var recruitable = TeamSystem.IsRecruitable(player, npc, out _);

        recruitable.Should().BeFalse();
    }

    [Fact]
    public void IsRecruitable_IncludesRefusedNPCsForNarrativeSignal()
    {
        // Hate-tier NPCs should pass IsRecruitable so the list can render them
        // with a "(won't join)" tag. This test confirms the documented design,
        // not a filter bug.
        ResetWorld();
        var player = MakePlayer();
        var npc = MakeNPC("n16", "Hater");
        SetScore(player, npc, GameConfig.RelationHate);

        var recruitable = TeamSystem.IsRecruitable(player, npc, out var band);

        recruitable.Should().BeTrue();
        band.Should().Be(TeamSystem.RecruitmentBand.Refused);
    }

    [Fact]
    public void IsRecruitable_FiltersLoversOut()
    {
        ResetWorld();
        var player = MakePlayer();
        var npc = MakeNPC("n17", "Partner");
        RomanceTracker.Instance.AddLover(npc.ID);

        var recruitable = TeamSystem.IsRecruitable(player, npc, out var band);

        recruitable.Should().BeFalse();
        band.Should().Be(TeamSystem.RecruitmentBand.Hidden);
    }

    [Fact]
    public void GetDefaultRolesForClass_Cleric_IncludesHealer()
    {
        var roles = UsurperRemake.Data.SpecializationData.GetDefaultRolesForClass(CharacterClass.Cleric);
        roles.Should().Contain(UsurperRemake.Data.SpecRole.Healer);
    }

    [Fact]
    public void GetDefaultRolesForClass_Warrior_IncludesTank()
    {
        var roles = UsurperRemake.Data.SpecializationData.GetDefaultRolesForClass(CharacterClass.Warrior);
        roles.Should().Contain(UsurperRemake.Data.SpecRole.Tank);
    }

    [Fact]
    public void GetDefaultRolesForClass_Voidreaver_FallsBackToDPS()
    {
        // Prestige class, no specs defined — should get the DPS fallback.
        var roles = UsurperRemake.Data.SpecializationData.GetDefaultRolesForClass(CharacterClass.Voidreaver);
        roles.Should().Contain(UsurperRemake.Data.SpecRole.DPS);
    }
}
