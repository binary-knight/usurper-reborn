using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.MentalRecovery1115Tests;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15: a secret boss win pays RewardXP and RewardGold once (the boss monster pays nothing on the
/// kill), and every grouped player who fought and is standing at the end gets the chamber cleared in
/// their own floor state and the same reward once. Grouped players not in the fight get nothing.
/// </summary>
[Collection("SharedGameSingletons")]
public class SecretBossGroup1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags S = BindingFlags.NonPublic | BindingFlags.Static;
    private const int EchoFloor = 75;
    private const string RoomId = "vault";

    private static DungeonLocation Dungeon(Character hero, MemoryStream output, int keys)
    {
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", keys).ToArray()), output);
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, EchoFloor);
        return d;
    }

    private static DungeonRoom BossRoom() => new DungeonRoom
    {
        Id = RoomId, HasEvent = true, HasSecretBoss = true,
        EventType = DungeonEventType.SecretBoss, SecretBossType = SecretBossType.EchoOfSelf,
    };

    private static Character Follower(string name)
    {
        var f = Hero(name);
        f.RemoteTerminal = Term(new MemoryStream());
        f.AutoLevelUp = false;
        f.Gold = 0;
        f.Experience = 0;
        return f;
    }

    private static List<Character> Teammates(DungeonLocation d) =>
        (List<Character>)typeof(DungeonLocation).GetField("teammates", F)!.GetValue(d)!;

    private static async Task Win(DungeonLocation d, DungeonRoom room, Character hero, List<Character> groupedAtStart)
    {
        var boss = new Monster { Name = "Echo", HP = 0, MaxHP = 500 };
        await (Task)typeof(DungeonLocation).GetMethod("FinishSecretBoss", F)!
            .Invoke(d, new object[] { room, SecretBossType.EchoOfSelf, hero, boss, new CombatResult { Outcome = CombatOutcome.Victory }, groupedAtStart })!;
    }

    private static bool Marked(Character c) =>
        c.DungeonFloorStates.TryGetValue(EchoFloor, out var fs)
        && fs.RoomStates.TryGetValue(RoomId, out var rs)
        && rs.EventCompleted && rs.SecretBossDefeated;

    private static SecretBossData Echo => SecretBossManager.Instance.GetBoss(SecretBossType.EchoOfSelf)!;

    [Fact]
    public void The_boss_monster_pays_nothing_on_the_kill()
    {
        foreach (SecretBossType type in Enum.GetValues(typeof(SecretBossType)))
        {
            if (SecretBossManager.Instance.GetBoss(type) == null) continue;
            var m = SecretBossManager.Instance.CreateBossMonster(type, 50);
            m.Experience.Should().Be(0, $"{type}: the victory screen pays the XP");
            m.Gold.Should().Be(0, $"{type}: the victory screen pays the gold");
        }
    }

    [Fact]
    public async Task A_grouped_player_in_the_fight_gets_the_chamber_cleared_in_their_own_floor_state()
    {
        var hero = Hero("Leader");
        var ally = Follower("Ally");
        var d = Dungeon(hero, new MemoryStream(), 10);
        Teammates(d).Add(ally);

        await Win(d, BossRoom(), hero, new List<Character> { ally });

        Marked(ally).Should().BeTrue("the chamber is cleared for every grouped player who won it");
        ally.Statistics.TotalSecretsFound.Should().Be(1, "the secret is counted per player, as the leader's is");
    }

    [Fact]
    public async Task Each_player_is_paid_the_victory_reward_once()
    {
        var hero = Hero("Leader");
        hero.Gold = 0;
        hero.Experience = 0;
        var ally = Follower("Ally");
        var d = Dungeon(hero, new MemoryStream(), 10);
        Teammates(d).Add(ally);

        await Win(d, BossRoom(), hero, new List<Character> { ally });

        hero.Gold.Should().Be(Echo.RewardGold);
        hero.Experience.Should().Be(TeamHQBonus.ApplyXP(hero, Echo.RewardXP));
        ally.Gold.Should().Be(Echo.RewardGold, "the follower is paid the victory reward once");
        ally.Experience.Should().Be(TeamHQBonus.ApplyXP(ally, Echo.RewardXP));
    }

    [Fact]
    public async Task A_grouped_player_not_in_the_fight_gets_nothing()
    {
        var hero = Hero("Leader");
        var ally = Follower("Ally");
        var late = Follower("Latecomer");   // in the party now, not when the fight started
        var d = Dungeon(hero, new MemoryStream(), 10);
        Teammates(d).Add(ally);
        Teammates(d).Add(late);

        await Win(d, BossRoom(), hero, new List<Character> { ally });

        Marked(late).Should().BeFalse();
        late.DungeonFloorStates.Should().BeEmpty();
        late.Gold.Should().Be(0);
        late.Statistics.TotalSecretsFound.Should().Be(0);
    }

    [Fact]
    public async Task A_grouped_player_who_fell_in_the_fight_keeps_the_chamber_and_is_not_paid()
    {
        var hero = Hero("Leader");
        var fallen = Follower("Fallen");
        fallen.HP = 0;
        var d = Dungeon(hero, new MemoryStream(), 10);

        await Win(d, BossRoom(), hero, new List<Character> { fallen });

        Marked(fallen).Should().BeFalse("a player who did not see the win can still win it");
        fallen.Gold.Should().Be(0);
    }

    [Fact]
    public void The_mark_reaches_the_players_own_cached_floor_and_survives_their_floor_save()
    {
        var gen = DungeonGenerator.GenerateFloor(EchoFloor);
        string bossId = gen.SecretBossRoomId;
        var ally = Follower("Ally");
        var theirDungeon = Dungeon(ally, new MemoryStream(), 0);
        typeof(DungeonLocation).GetField("currentFloor", F)!.SetValue(theirDungeon, gen);
        typeof(DungeonLocation).GetMethod("SaveFloorState", F)!.Invoke(theirDungeon, new object[] { ally });

        typeof(DungeonLocation).GetMethod("MarkSecretBossWon", S)!.Invoke(null, new object?[] { ally, theirDungeon, EchoFloor, bossId });

        var room = gen.Rooms.Single(r => r.Id == bossId);
        room.EventCompleted.Should().BeTrue("the player's cached floor carries the win");
        room.SecretBossDefeated.Should().BeTrue();

        // their own later save of the cached floor keeps the mark
        typeof(DungeonLocation).GetMethod("SaveFloorState", F)!.Invoke(theirDungeon, new object[] { ally });
        var rs = ally.DungeonFloorStates[EchoFloor].RoomStates[bossId];
        rs.EventCompleted.Should().BeTrue();
        rs.SecretBossDefeated.Should().BeTrue();
    }
}
