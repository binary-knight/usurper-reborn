using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.13: an orphan's arrival and graduation are guarded court changes, so a court reload undoes neither.</summary>
public partial class OwnerProcessConflictTests
{
    private static void RunSimStep(WorldSimulator sim, string method, params object[] args) =>
        typeof(WorldSimulator).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(sim, args);

    private string NpcCourtWithOrphan(string king, string orphan, int yearsOld) =>
        JsonSerializer.Serialize(new RoyalCourtSaveData
        {
            KingName = king, KingAI = (int)CharacterAI.Computer, Treasury = 1000, TaxRate = 7,
            Orphans =
            {
                new RoyalOrphanSaveData
                {
                    Name = orphan, Age = yearsOld, Sex = (int)CharacterSex.Female, IsRealOrphan = true, Happiness = 50,
                    ArrivalDate = DateTime.Now.ToString("o"),
                    BirthDate = DateTime.Now.AddHours(-GameConfig.NpcLifecycleHoursPerYear * (yearsOld + 0.5)).ToString("o")
                }
            }
        }, Json);

    [Fact]
    public async Task AnOrphansGraduation_ThenCourtPolitics_ThenAnotherTick_CreatesOneNpc()
    {
        await WithKing("Aldric", 1000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", NpcCourtWithOrphan("Aldric", "Pipwyn", 19));
            new WorldSimService(_db).LoadRoyalCourtFromWorldState();
            CastleLocation.GetCurrentKing()!.Orphans.Select(o => o.Name).Should().Contain("Pipwyn");
            var sim = new WorldSimulator();

            RunSimStep(sim, "ProcessOrphanAging");
            RunCourtPolitics();
            RunSimStep(sim, "ProcessOrphanAging");
            RunSimStep(sim, "ProcessOrphanAging");

            NPCSpawnSystem.Instance.ActiveNPCs.Count(n => (n.Name2 ?? "").StartsWith("Pipwyn")).Should().Be(1, "the orphan graduates once");
            (await StoredCourt()).Orphans.Should().NotContain(o => o.Name == "Pipwyn", "the graduation is written to the stored court");
            CastleLocation.GetCurrentKing()!.Orphans.Should().NotContain(o => o.Name == "Pipwyn");
        }));
    }

    [Fact]
    public async Task AnOrphansArrival_SurvivesTheNextGuardedCourtChange()
    {
        var mother = Npc("npc_orph_m", "Marabel");
        var father = Npc("npc_orph_f", "Dunstan");
        mother.IsDead = true;
        father.IsDead = true;
        var child = new Child
        {
            Name = "Wrenna", Mother = "Marabel", Father = "Dunstan", MotherID = "npc_orph_m", FatherID = "npc_orph_f",
            Age = 6, BirthDate = DateTime.Now.AddHours(-GameConfig.NpcLifecycleHoursPerYear * 6), Sex = CharacterSex.Female
        };
        FamilySystem.Instance.AllChildren.Add(child);
        try
        {
            await WithKing("Aldric", 1000, async _ => await AsTheWorldSim(async () =>
            {
                await _db.SaveWorldState("royal_court", NpcCourt("Aldric", 1000));
                new WorldSimService(_db).LoadRoyalCourtFromWorldState();

                RunSimStep(new WorldSimulator(), "CheckForOrphanedChildren", mother);
                (await CastleLocation.CourtChangeAsync(court => { court.Treasury += 50; return true; })).Should().BeTrue();   // a deposit

                (await StoredCourt()).Orphans.Select(o => o.Name).Should().Contain("Wrenna", "the arrival is its own guarded write");
                CastleLocation.GetCurrentKing()!.Orphans.Select(o => o.Name).Should().Contain("Wrenna");
                (await StoredCourt()).Treasury.Should().Be(1050);
            }));
        }
        finally { FamilySystem.Instance.AllChildren.Remove(child); }
    }
}
