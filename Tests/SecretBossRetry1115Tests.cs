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
/// v1.1.15: the dungeon secret boss room is spent only by a win. A flee, a loss, a death or a fight not
/// entered at Mental 0 leaves it to try again; nothing is granted at the fight start; a win is paid once,
/// survives a save of the floor, and a wrong pre-fight choice does not change the shared boss data.
/// </summary>
[Collection("SharedGameSingletons")]
public class SecretBossRetry1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int EchoFloor = 75;   // EchoOfSelf: no pre-fight choice, triggers the memory flash

    private static DungeonLocation Dungeon(Character hero, int floor, MemoryStream output, params string[] lines)
    {
        var term = new TerminalEmulator(new LineStream(lines), output);
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, floor);
        return d;
    }

    private static DungeonRoom BossRoom() => new DungeonRoom
    {
        Id = "vault", HasEvent = true, HasSecretBoss = true,
        EventType = DungeonEventType.SecretBoss, SecretBossType = SecretBossType.EchoOfSelf,
    };

    private static Task HandleRoomEvent(DungeonLocation d, DungeonRoom room) =>
        (Task)typeof(DungeonLocation).GetMethod("HandleRoomEvent", F)!.Invoke(d, new object[] { room })!;

    private static Task Finish(DungeonLocation d, DungeonRoom room, Character hero, Monster boss, CombatResult r) =>
        (Task)typeof(DungeonLocation).GetMethod("FinishSecretBoss", F)!
            .Invoke(d, new object[] { room, SecretBossType.EchoOfSelf, hero, boss, r })!;

    private static string[] Keys(int n) => Enumerable.Repeat("", n).ToArray();

    private static void ClearFlags()
    {
        StoryProgressionSystem.Instance.SetStoryFlag("memory_flash_pending", false);
        StoryProgressionSystem.Instance.SetStoryFlag("cycle_revealed", false);
    }

    [Fact]
    public async Task A_fight_not_entered_at_Mental_0_leaves_the_boss_and_grants_nothing()
    {
        ClearFlags();
        var hero = Hero("Spent", 0);
        var output = new MemoryStream();
        var room = BossRoom();
        await HandleRoomEvent(Dungeon(hero, EchoFloor, output, Keys(10)), room);

        room.EventCompleted.Should().BeFalse("only a win spends the secret boss");
        room.SecretBossDefeated.Should().BeFalse();
        hero.Statistics.TotalSecretsFound.Should().Be(0, "nothing is counted at the fight start");
        StoryProgressionSystem.Instance.HasStoryFlag("memory_flash_pending").Should().BeFalse("the flag is set by the win");
        var text = Text(output);
        text.Should().Contain(Loc.Get("mental.collapse_before_fight"));
        text.Should().Contain(Loc.Get("dungeon.secret_boss_remains"));
    }

    [Fact]
    public async Task A_flee_a_loss_or_a_death_leaves_the_boss_to_try_again()
    {
        var boss = new Monster { Name = "Echo", HP = 500, MaxHP = 500 };

        var fled = Hero("Fled");
        var room = BossRoom();
        var output = new MemoryStream();
        await Finish(Dungeon(fled, EchoFloor, output, Keys(3)), room, fled, boss, new CombatResult { Outcome = CombatOutcome.PlayerEscaped });
        room.EventCompleted.Should().BeFalse();
        room.SecretBossDefeated.Should().BeFalse();
        Text(output).Should().Contain(Loc.Get("dungeon.secret_boss_remains"));

        var lost = Hero("Lost");
        lost.HP = 0;
        room = BossRoom();
        await Finish(Dungeon(lost, EchoFloor, new MemoryStream(), Keys(3)), room, lost, boss, new CombatResult { Outcome = CombatOutcome.PlayerDied });
        room.EventCompleted.Should().BeFalse();
        room.SecretBossDefeated.Should().BeFalse();

        var died = Hero("Died");
        room = BossRoom();
        Func<Task> toTemple = () => Finish(Dungeon(died, EchoFloor, new MemoryStream(), Keys(3)), room, died, boss,
            new CombatResult { Outcome = CombatOutcome.PlayerDied, ShouldReturnToTemple = true });
        try { await toTemple(); } catch (Exception) { /* the Temple move leaves the location */ }
        room.EventCompleted.Should().BeFalse();
        room.SecretBossDefeated.Should().BeFalse();
    }

    [Fact]
    public async Task A_win_spends_the_room_and_pays_once()
    {
        ClearFlags();
        var hero = Hero("Winner");
        hero.Gold = 0;
        var room = BossRoom();
        var d = Dungeon(hero, EchoFloor, new MemoryStream(), Keys(10));
        var boss = new Monster { Name = "Echo", HP = 0, MaxHP = 500 };
        await Finish(d, room, hero, boss, new CombatResult { Outcome = CombatOutcome.Victory });

        room.EventCompleted.Should().BeTrue("a win spends the secret boss");
        room.SecretBossDefeated.Should().BeTrue();
        hero.Statistics.TotalSecretsFound.Should().Be(1);
        StoryProgressionSystem.Instance.HasStoryFlag("memory_flash_pending").Should().BeTrue();
        long paid = SecretBossManager.Instance.GetBoss(SecretBossType.EchoOfSelf)!.RewardGold;
        hero.Gold.Should().Be(paid);

        // Coming back to the room after the win (the V action's gate bypassed) does not fight or pay again;
        // the dungeon has no input left, so a second fight would fail on the first prompt.
        await HandleRoomEvent(Dungeon(hero, EchoFloor, new MemoryStream()), room);
        hero.Gold.Should().Be(paid, "a win is paid once");
        hero.Statistics.TotalSecretsFound.Should().Be(1);
        room.EventCompleted.Should().BeTrue();
    }

    [Fact]
    public void The_room_state_survives_saving_and_restoring_the_floor()
    {
        var gen = DungeonGenerator.GenerateFloor(EchoFloor);
        gen.HasSecretBoss.Should().BeTrue("floor 75 places its secret boss");
        string bossId = gen.SecretBossRoomId;

        var hero = Hero("Saver");
        var d = Dungeon(hero, EchoFloor, new MemoryStream());
        var save = typeof(DungeonLocation).GetMethod("SaveFloorState", F)!;
        var restore = typeof(DungeonLocation).GetMethod("GenerateOrRestoreFloor", F)!;
        var floorField = typeof(DungeonLocation).GetField("currentFloor", F)!;

        // a fight that was not won: the room is still open after a save and a restore
        floorField.SetValue(d, gen);
        save.Invoke(d, new object[] { hero });
        var restored = GetFloor(restore.Invoke(d, new object[] { hero, EchoFloor })!);
        var room = restored.Rooms.Single(r => r.Id == bossId);
        room.EventCompleted.Should().BeFalse();
        room.SecretBossDefeated.Should().BeFalse();

        // a win: both marks come back
        room.EventCompleted = true;
        room.SecretBossDefeated = true;
        floorField.SetValue(d, restored);
        save.Invoke(d, new object[] { hero });
        var again = GetFloor(restore.Invoke(d, new object[] { hero, EchoFloor })!);
        var won = again.Rooms.Single(r => r.Id == bossId);
        won.EventCompleted.Should().BeTrue();
        won.SecretBossDefeated.Should().BeTrue();
    }

    private static DungeonFloor GetFloor(object result) => (DungeonFloor)result.GetType().GetField("Floor")!.GetValue(result)!;

    [Fact]
    public async Task A_wrong_choice_makes_that_fight_harder_without_changing_the_shared_boss()
    {
        var mgr = SecretBossManager.Instance;
        var data = mgr.GetBoss(SecretBossType.TheForgottenEighth)!;
        data.RequiresChoice.Should().BeTrue();
        int baseAttack = data.Stats.Attack;
        string wrong = data.CorrectChoice == 0 ? "2" : "1";

        for (int i = 0; i < 2; i++)
        {
            var term = new TerminalEmulator(new LineStream(new[] { "", "", wrong, "" }), new MemoryStream());
            var r = await mgr.EncounterBoss(SecretBossType.TheForgottenEighth, Hero("Wrong"), term);
            r.WrongChoice.Should().BeTrue();
            data.Stats.Attack.Should().Be(baseAttack, "a retry or another player starts from the base stats");
        }

        var harder = mgr.CreateBossMonster(SecretBossType.TheForgottenEighth, 50, 1.5);
        var normal = mgr.CreateBossMonster(SecretBossType.TheForgottenEighth, 50);
        harder.Strength.Should().BeGreaterThan(normal.Strength);
    }
}
