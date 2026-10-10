using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.8 (F5), row 9: a door process that does not host the world sim service runs a simulator loop of its
/// own on its own copy of the roster, and its changes reach the shared roster with the session's save. That
/// loop knew only its own session's team. Online it now reads the players' teams from the store before each
/// step. Two processes over one store as the other tests of this class build them: the lock holder (the
/// stored roster) and a door session without the lock (DoorLogin).
/// </summary>
public partial class OwnerProcessConflictTests
{
    [Fact]
    public async Task Row9_ADoorsOwnSimulatorLoop_NoLongerHarmsAnotherPlayersHire()
    {
        const BindingFlags P = BindingFlags.Instance | BindingFlags.NonPublic;
        var saveSystemField = typeof(SaveSystem).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static)!;
        var saveSystemBefore = saveSystemField.GetValue(null);
        bool onlineBefore = UsurperRemake.BBS.DoorMode.IsOnlineMode;
        var liveBefore = WorldSimulator.LiveSessionTeams;
        var simBefore = WorldSimulator.Instance;
        NewsSystem.Instance.SetCatchUpBuffer(new List<string>(), 0);
        try
        {
            // the store: another player's team with two NPC members, and the door player's own hire
            var elder = Npc("npc_r9_elder", "Other Elder"); elder.Team = "Other Hall"; elder.Level = 40;
            var hire = Npc("npc_r9_hire", "Other Hire"); hire.Team = "Other Hall"; hire.Level = 5;
            var own = Npc("npc_r9_own", "Door Hire"); own.Team = "Door Hall"; own.Level = 5;
            await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, RosterJson());
            Exec("INSERT INTO players (username, display_name, player_data) VALUES ('other player', 'Other Player', '{\"player\":{\"level\":10,\"team\":\"Other Hall\"}}');");
            _db.TakeOverWorldSimLock("mud_holder");

            // the door process: not the owner; its roster is a copy; its list holds its own session's team
            WorldEditLog.OwnerOverride = false;
            var dbA = new SqlSaveBackend(_path);
            var osmA = await DoorLogin(dbA);
            SaveSystem.InitializeWithBackend(dbA);
            TeamCornerRig.SetOnline(true);
            WorldSimulator.LiveSessionTeams = () => new[] { "Door Hall" };
            WorldSimulator.RegisterPlayerTeam("Door Hall");
            WorldSimulator.IsPlayerTeam("Other Hall").Should().BeFalse("no session of this process is on it");

            // one pass of the door's own loop: the store is read first. The simulator is left inactive, so the
            // step that follows the read does nothing here (a whole step would change the king and the tax
            // of the game this test shares with the other tests of its collection)
            var sim = new WorldSimulator();
            sim.SessionLoopStep().Should().BeTrue();
            WorldSimulator.IsPlayerTeam("Other Hall").Should().BeTrue("the loop read the store before its step");

            // the death guard and the dissolve, on the door's copies
            var doorCopy = Find("Other Hire")!;
            sim.MarkNPCDead(doorCopy, 0f, "a monster", "Dungeon");
            doorCopy.IsDead.Should().BeFalse("another player's hire is held at the world death site");
            doorCopy.HP = 0;   // as dead as the simulator can make it: the team has one living NPC member now
            for (int i = 0; i < 50; i++) typeof(WorldSimulator).GetMethod("CheckTeamBetrayals", P)!.Invoke(sim, null);
            Find("Other Elder")!.Team.Should().Be("Other Hall", "a player's team with one living NPC member is not dissolved");
            doorCopy.HP = doorCopy.MaxHP;
            sim.MarkNPCDead(Find("Door Hire")!, 0f, "a monster", "Dungeon");
            Find("Door Hire")!.IsDead.Should().BeFalse("its own session's hire, as before");

            // the door's ordinary save, then the lock holder's reload
            (await osmA.SaveSharedNPCsVersionedAsync(dbA, OnlineStateManager.SerializeCurrentNPCs())).Should().BeTrue();
            var stored = await StoredRoster();
            stored.Single(d => d.Name == "Other Hire").IsDead.Should().BeFalse();
            stored.Single(d => d.Name == "Other Elder").Team.Should().Be("Other Hall");
            WorldEditLog.OwnerOverride = true;
            NPCSpawnSystem.Instance.ActiveNPCs.Clear();
            await (Task)typeof(WorldSimService).GetMethod("LoadWorldState", P)!.Invoke(new WorldSimService(_db, heartbeatOwnerId: "mud_holder"), null)!;
            Find("Other Hire")!.IsDead.Should().BeFalse("the lock holder sees the hire alive");
            Find("Other Elder")!.Team.Should().Be("Other Hall");
        }
        finally
        {
            foreach (string team in new[] { "Other Hall", "Door Hall" }) WorldSimulator.UnregisterPlayerTeam(team);
            WorldSimulator.LiveSessionTeams = liveBefore;
            typeof(WorldSimulator).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, simBefore);
            TeamCornerRig.SetOnline(onlineBefore);
            saveSystemField.SetValue(null, saveSystemBefore);
            NewsSystem.Instance.ClearCatchUpBuffer();
        }
    }
}
