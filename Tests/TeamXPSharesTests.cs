using System.Collections.Generic;
using System.Linq;
using Xunit;
using FluentAssertions;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.0.4 regression tests for the party XP split. Reported live: "the game keeps
/// resetting the exp distribution"; a player who chose an even split found their
/// character at 100% ten rooms later. Combat used to overwrite the stored split
/// (a fight with no teammates zeroed every slot, a dead teammate's share was moved
/// onto the player for good, and [E] stored fixed numbers for that day's party).
/// ResolveTeamXPShares is now a pure function of the preference and the party present.
/// </summary>
public class TeamXPSharesTests
{
    private static Character MakePlayer(int[] stored, bool explicitSplit, bool evenSplit, bool redistribute)
        => new Character
        {
            Name1 = "Mandragor", Name2 = "Mandragor", ID = "mandragor", Level = 20,
            TeamXPPercent = (int[])stored.Clone(),
            TeamXPIsExplicit = explicitSplit,
            TeamXPEvenSplit = evenSplit,
            AutoRedistributeXP = redistribute,
        };

    private static NPC Ally(string name, bool alive = true, int level = 15)
        => new NPC { ID = $"npc_{name.ToLower()}", Name1 = name, Name2 = name, Level = level, MaxHP = 100, HP = alive ? 100 : 0 };

    private static List<Character> Party(params Character[] members) => members.ToList();

    private static void AssertStoredUntouched(Character player, int[] before)
        => player.TeamXPPercent.Should().Equal(before);

    [Fact]
    public void SoloFight_PaysPlayerEverything_AndLeavesPreferenceAlone()
    {
        var stored = new[] { 34, 33, 33, 0, 0 };
        var player = MakePlayer(stored, explicitSplit: true, evenSplit: false, redistribute: true);

        var shares = CombatEngine.ResolveTeamXPShares(player, null);

        shares.Should().Equal(100, 0, 0, 0, 0);
        AssertStoredUntouched(player, stored);
    }

    [Fact]
    public void EvenMode_FollowsPartySize()
    {
        var stored = new[] { 100, 0, 0, 0, 0 };
        var player = MakePlayer(stored, explicitSplit: true, evenSplit: true, redistribute: true);

        CombatEngine.ResolveTeamXPShares(player, Party(Ally("Aldric"), Ally("Mira")))
            .Should().Equal(34, 33, 33, 0, 0);
        CombatEngine.ResolveTeamXPShares(player, Party(Ally("Aldric"), Ally("Mira"), Ally("Vex")))
            .Should().Equal(25, 25, 25, 25, 0);
        CombatEngine.ResolveTeamXPShares(player, Party(Ally("Aldric")))
            .Should().Equal(50, 50, 0, 0, 0);
        AssertStoredUntouched(player, stored);
    }

    [Fact]
    public void EvenMode_SkipsDeadTeammate_ThenPaysThemAgainWhenRevived()
    {
        var stored = new[] { 100, 0, 0, 0, 0 };
        var player = MakePlayer(stored, explicitSplit: true, evenSplit: true, redistribute: true);
        var mira = Ally("Mira", alive: false);

        CombatEngine.ResolveTeamXPShares(player, Party(Ally("Aldric"), mira)).Should().Equal(50, 50, 0, 0, 0);

        mira.HP = mira.MaxHP;
        CombatEngine.ResolveTeamXPShares(player, Party(Ally("Aldric"), mira)).Should().Equal(34, 33, 33, 0, 0);
        AssertStoredUntouched(player, stored);
    }

    [Fact]
    public void NeverSetASplit_DefaultsToEven()
    {
        var stored = new[] { 100, 0, 0, 0, 0 };
        var player = MakePlayer(stored, explicitSplit: false, evenSplit: false, redistribute: true);

        CombatEngine.ResolveTeamXPShares(player, Party(Ally("Aldric"))).Should().Equal(50, 50, 0, 0, 0);
        AssertStoredUntouched(player, stored);
    }

    [Fact]
    public void CustomMode_DeadSlotGoesToPlayer_WhenRedistributeOff()
    {
        var stored = new[] { 60, 20, 20, 0, 0 };
        var player = MakePlayer(stored, explicitSplit: true, evenSplit: false, redistribute: false);

        CombatEngine.ResolveTeamXPShares(player, Party(Ally("Aldric"), Ally("Mira", alive: false)))
            .Should().Equal(80, 20, 0, 0, 0);
        AssertStoredUntouched(player, stored);
    }

    [Fact]
    public void CustomMode_DeadSlotIsSplit_WhenRedistributeOn()
    {
        var stored = new[] { 60, 20, 20, 0, 0 };
        var player = MakePlayer(stored, explicitSplit: true, evenSplit: false, redistribute: true);

        CombatEngine.ResolveTeamXPShares(player, Party(Ally("Aldric"), Ally("Mira", alive: false)))
            .Should().Equal(70, 30, 0, 0, 0);
        AssertStoredUntouched(player, stored);
    }

    [Fact]
    public void CustomMode_KeepAllXP_IsHonoredWithTeammates()
    {
        // v0.57.2: a deliberate 100/0 must not be "helpfully" spread over the party
        var stored = new[] { 100, 0, 0, 0, 0 };
        var player = MakePlayer(stored, explicitSplit: true, evenSplit: false, redistribute: true);

        CombatEngine.ResolveTeamXPShares(player, Party(Ally("Aldric"), Ally("Mira"))).Should().Equal(100, 0, 0, 0, 0);
        AssertStoredUntouched(player, stored);
    }

    [Fact]
    public void CustomMode_UnallocatedStaysUnallocated()
    {
        var stored = new[] { 50, 30, 0, 0, 0 };
        var player = MakePlayer(stored, explicitSplit: true, evenSplit: false, redistribute: true);

        CombatEngine.ResolveTeamXPShares(player, Party(Ally("Aldric"))).Should().Equal(50, 30, 0, 0, 0);
        AssertStoredUntouched(player, stored);
    }

    [Fact]
    public void LeavingEvenMode_SeedsFromThePartySplit_SoADeadTeammateIsPaidWhenRevived()
    {
        var player = MakePlayer(new[] { 100, 0, 0, 0, 0 }, explicitSplit: true, evenSplit: true, redistribute: true);
        var aldric = Ally("Aldric");
        var mira = Ally("Mira", alive: false);

        // What the menu seeds custom numbers from: the split over the PARTY, not the living
        var seed = CombatEngine.PartyEvenXPSplit(3);
        seed.Should().Equal(34, 33, 33, 0, 0);
        System.Array.Copy(seed, player.TeamXPPercent, seed.Length);
        player.TeamXPEvenSplit = false;
        player.TeamXPPercent[0] = 40; // the edit the player made

        // Mira is dead this fight: her stored share is redistributed, not lost
        CombatEngine.ResolveTeamXPShares(player, Party(aldric, mira)).Should().Equal(57, 49, 0, 0, 0);
        player.TeamXPPercent.Should().Equal(new[] { 40, 33, 33, 0, 0 });

        mira.HP = mira.MaxHP;
        CombatEngine.ResolveTeamXPShares(player, Party(aldric, mira)).Should().Equal(40, 33, 33, 0, 0);
    }

    [Fact]
    public void PartyEvenSplit_PlayerTakesRemainder()
    {
        CombatEngine.PartyEvenXPSplit(1).Should().Equal(100, 0, 0, 0, 0);
        CombatEngine.PartyEvenXPSplit(2).Should().Equal(50, 50, 0, 0, 0);
        CombatEngine.PartyEvenXPSplit(4).Should().Equal(25, 25, 25, 25, 0);
        CombatEngine.PartyEvenXPSplit(5).Should().Equal(20, 20, 20, 20, 20);
    }
}
