using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods: victory deeds in a group fight. The leader and each living grouped human
/// follower record their own god's victory deeds (UndeadSlain, StrongerFoeBeaten) within their own
/// daily cap; NPC teammates, companions and pets never do. A follower's tier crossing leaves the
/// stat update to the follower's own session (GodBoonRecalcPending).
/// </summary>
[Collection("SharedGameSingletons")]
public class GodGroupDeeds1115Tests
{
    private static TerminalEmulator Term() =>
        new TerminalEmulator(new LineStream(Enumerable.Repeat("", 5)), new MemoryStream());

    private static Character Hero(string name, string god, GodSystem gods, int favor = 30)
    {
        var c = new Character { Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 5, HP = 50, MaxHP = 50, Class = CharacterClass.Warrior };
        GodRegistry.SetWorshippedGod(c, god, gods).Should().BeTrue();
        c.GodFavor = favor;
        FavorSystem.Bind(c, gods);
        c.GodFavor = favor;
        return c;
    }

    private static Character Follower(string name, string god, GodSystem gods, int favor = 30)
    {
        var c = Hero(name, god, gods, favor);
        c.RemoteTerminal = Term();
        return c;
    }

    private static List<Monster> Undead() => new() { new Monster { Name = "Skeleton", Level = 1 } };

    [Fact]
    public void Follower_OfSolarius_GainsUndeadSlain()
    {
        var gods = new GodSystem();
        var leader = Hero("GgdLeadA", "Valorian", gods);
        var follower = Follower("GgdFolA", "Solarius", gods);
        follower.IsGroupedPlayer.Should().BeTrue();

        GodDeedSystem.RecordGroupVictory(leader, Undead(), new List<Character> { follower }, Term(), gods);

        follower.GodFavor.Should().Be(31, "a grouped follower records their own god's victory deed");
        FavorSystem.GainedToday(follower, FavorSource.Deed).Should().Be(1);
        leader.GodFavor.Should().Be(30, "undead are not a War deed");
    }

    [Fact]
    public void Follower_StrongerFoe_ByTheirOwnLevel()
    {
        var gods = new GodSystem();
        var leader = Hero("GgdLeadB", "Valorian", gods);
        var follower = Follower("GgdFolB", "Valorian", gods);
        follower.Level = 3;

        GodDeedSystem.RecordGroupVictory(leader, new List<Monster> { new Monster { Name = "Goblin", Level = 4 } }, new List<Character> { follower }, Term(), gods);

        follower.GodFavor.Should().Be(31, "the goblin is above the follower's level");
        leader.GodFavor.Should().Be(30, "the goblin is not above the leader's level");
    }

    [Fact]
    public void NpcTeammates_CompanionsAndDeadFollowers_NeverRecord()
    {
        var gods = new GodSystem();
        var leader = Hero("GgdLeadC", "Valorian", gods);
        var npc = Hero("GgdNpcC", "Solarius", gods);
        npc.AI = CharacterAI.Computer;
        var companion = Hero("GgdCompC", "Solarius", gods);
        companion.IsCompanion = true;
        var dead = Follower("GgdDeadC", "Solarius", gods);
        dead.HP = 0;

        GodDeedSystem.RecordGroupVictory(leader, Undead(), new List<Character> { npc, companion, dead }, Term(), gods);

        npc.GodFavor.Should().Be(30);
        companion.GodFavor.Should().Be(30, "a teammate that is not a grouped player never records");
        dead.GodFavor.Should().Be(30, "a fallen follower does not record");
    }

    [Fact]
    public void Leader_RecordsOnce_EvenWhenListedAsTeammate()
    {
        var gods = new GodSystem();
        var leader = Hero("GgdLeadD", "Solarius", gods);
        leader.RemoteTerminal = Term();
        var follower = Follower("GgdFolD", "Solarius", gods);

        GodDeedSystem.RecordGroupVictory(leader, Undead(), new List<Character> { leader, follower, follower }, Term(), gods);

        leader.GodFavor.Should().Be(31, "the leader's deed records once");
        follower.GodFavor.Should().Be(31, "a follower's deed records once");
    }

    [Fact]
    public void DailyCap_IsEachPlayersOwn()
    {
        var gods = new GodSystem();
        var leader = Hero("GgdLeadE", "Solarius", gods);
        leader.GodFavorDayGains[FavorSource.Deed.ToString()] = GameConfig.GodFavorDeedDailyCap;
        var follower = Follower("GgdFolE", "Solarius", gods);
        var capped = Follower("GgdCapE", "Solarius", gods);
        capped.GodFavorDayGains[FavorSource.Deed.ToString()] = GameConfig.GodFavorDeedDailyCap;

        GodDeedSystem.RecordGroupVictory(leader, Undead(), new List<Character> { follower, capped }, Term(), gods);

        leader.GodFavor.Should().Be(30, "the leader's cap is spent");
        follower.GodFavor.Should().Be(31, "the follower's cap is their own");
        capped.GodFavor.Should().Be(30, "this follower's own cap is spent");
    }

    [Fact]
    public void Follower_TierCrossing_IsLeftToTheirOwnSession()
    {
        var gods = new GodSystem();
        var leader = Hero("GgdLeadF", "Solarius", gods, GameConfig.GodFavorTierDevoutMin - 1);
        var follower = Follower("GgdFolF", "Solarius", gods, GameConfig.GodFavorTierDevoutMin - 1);

        GodDeedSystem.RecordGroupVictory(leader, Undead(), new List<Character> { follower }, Term(), gods);

        follower.GodFavor.Should().Be(GameConfig.GodFavorTierDevoutMin);
        follower.GodBoonRecalcPending.Should().BeTrue("the follower's own session applies the boon update");
        leader.GodFavor.Should().Be(GameConfig.GodFavorTierDevoutMin);
        leader.GodBoonRecalcPending.Should().BeFalse("the leader's own session updates at once");
    }
}
