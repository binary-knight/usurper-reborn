using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.13: an NPC coronation is one versioned write, so the same tick's court politics read the new king.</summary>
public partial class OwnerProcessConflictTests
{
    private static readonly FieldInfo OnlineModeField =
        typeof(UsurperRemake.BBS.DoorMode).GetField("_onlineMode", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo SimulatorInstance =
        typeof(WorldSimulator).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>The world sim's side: online, its court changes going to this store, holding the stored court.</summary>
    private async Task AsTheWorldSim(Func<Task> body)
    {
        bool online = (bool)OnlineModeField.GetValue(null)!;
        var simStore = OnlineStateManager.SimCourtStore;
        var simulator = SimulatorInstance.GetValue(null);
        OnlineModeField.SetValue(null, true);
        OnlineStateManager.SimCourtStore = _db;
        try { await body(); }
        finally
        {
            OnlineModeField.SetValue(null, online);
            OnlineStateManager.SimCourtStore = simStore;
            SimulatorInstance.SetValue(null, simulator);
        }
    }

    private static void RunCourtPolitics() =>
        typeof(WorldSimulator).GetMethod("ProcessRoyalCourtPolitics", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(new WorldSimulator(), null);

    private string NpcCourt(string king, long treasury) =>
        JsonSerializer.Serialize(new RoyalCourtSaveData { KingName = king, KingAI = (int)CharacterAI.Computer, Treasury = treasury, TaxRate = 7, CityTaxPercent = 3 }, Json);

    [Fact]
    public async Task AnNpcCoronation_ThenCourtPolitics_InTheSameTick_KeepsTheNewKing()
    {
        await WithKing("Aldric", 1000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", NpcCourt("Aldric", 1000));
            var sim = new WorldSimService(_db);
            sim.LoadRoyalCourtFromWorldState();   // the sim holds A at the stored version
            var aldric = Npc("npc_aldric", "Aldric");
            aldric.King = true;
            var brenna = Npc("npc_brenna", "Brenna");

            // an NPC wins a throne challenge, and the same tick's court politics follow
            ChallengeSystem.Instance.CrownNewKing(brenna, CastleLocation.GetCurrentKing()!);
            (await StoredCourt()).KingName.Should().Be("Brenna", "the coronation is written at once");
            RunCourtPolitics();

            var stored = await StoredCourt();
            stored.KingName.Should().Be("Brenna", "court politics read the new king, and never put the old court back");
            stored.Treasury.Should().Be(500, "half the stored treasury is inherited");
            stored.TaxRate.Should().Be(7);
            stored.CityTaxPercent.Should().Be(3);
            stored.Prisoners.Select(p => p.CharacterName).Should().Contain("Aldric", "the deposed monarch's cell is in the same write");
            var king = CastleLocation.GetCurrentKing()!;
            king.Name.Should().Be("Brenna");
            king.Treasury.Should().Be(500);
            OnlineStateManager.RoyalCourtVersion.Should().Be(_db.GetWorldStateVersion("royal_court"), "the in-memory court is the written copy");
            brenna.King.Should().BeTrue();
            aldric.King.Should().BeFalse();
            aldric.DaysInPrison.Should().BeGreaterThan(0);
        }));
    }

    [Fact]
    public async Task AnNpcCoronation_AfterTheStoredThroneChangedHands_IsNotWritten()
    {
        await WithKing("Aldric", 1000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", NpcCourt("Aldric", 1000));
            var sim = new WorldSimService(_db);
            sim.LoadRoyalCourtFromWorldState();
            var brenna = Npc("npc_brenna", "Brenna");

            // another process crowned Cedric after the sim loaded Aldric
            (await _db.SaveWorldStateIfVersion("royal_court", NpcCourt("Cedric", 3000), _db.GetWorldStateVersion("royal_court"))).Should().BeTrue();
            long version = _db.GetWorldStateVersion("royal_court");

            ChallengeSystem.Instance.CrownNewKing(brenna, CastleLocation.GetCurrentKing()!);

            _db.GetWorldStateVersion("royal_court").Should().Be(version, "nothing was written over Cedric's court");
            (await StoredCourt()).KingName.Should().Be("Cedric");
            CastleLocation.GetCurrentKing()!.Name.Should().Be("Cedric", "the in-memory court is the stored one");
            brenna.King.Should().BeFalse("the coronation's other effects follow only a written crown");
        }));
    }

    [Fact]
    public async Task AFailedChallengersCell_SurvivesTheSameTicksCourtPolitics()
    {
        await WithKing("Aldric", 1000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", NpcCourt("Aldric", 1000));
            var sim = new WorldSimService(_db);
            sim.LoadRoyalCourtFromWorldState();
            var dorn = Npc("npc_dorn", "Dorn");

            typeof(ChallengeSystem).GetMethod("ImprisonChallenger", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(ChallengeSystem.Instance, new object?[] { dorn, 7, "Failed throne challenge", true });
            RunCourtPolitics();

            (await StoredCourt()).Prisoners.Select(p => p.CharacterName).Should().Contain("Dorn");
            CastleLocation.GetCurrentKing()!.Prisoners.Keys.Should().Contain("Dorn");
        }));
    }
}
