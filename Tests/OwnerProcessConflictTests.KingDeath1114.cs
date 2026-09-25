using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.14: a monarch's death is stored as a marked vacancy, so a fresh process loads the throne empty.</summary>
public partial class OwnerProcessConflictTests
{
    [Fact]
    public async Task AMonarchsDeath_IsStoredAsAVacancy_ThatAFreshLoadKeeps()
    {
        await WithKing("Aldric", 1000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", NpcCourt("Aldric", 1000));
            var sim = new WorldSimService(_db);
            sim.LoadRoyalCourtFromWorldState();
            var aldric = Npc("npc_kd_aldric", "Aldric");
            aldric.King = true;

            CastleLocation.VacateThrone("The ruler has fallen in battle.");

            var stored = await StoredCourt();
            OnlineStateManager.IsVacancy(stored).Should().BeTrue("the death is written as a marked vacancy");
            stored.MonarchHistory.Select(m => m.Name).Should().Contain("Aldric", "the ended reign is recorded");
            CastleLocation.GetCurrentKing().Should().BeNull();
            aldric.King.Should().BeFalse();

            // the world sim's own court save leaves the vacancy stored
            await sim.SaveRoyalCourtToWorldState();
            OnlineStateManager.IsVacancy(await StoredCourt()).Should().BeTrue();

            // a fresh process, holding some other court, loads the empty throne
            CastleLocation.SetKing(King.CreateNewKing("Aldric", CharacterAI.Computer, CharacterSex.Male));
            OnlineStateManager.NoteRoyalCourtVersion(null);
            await NewOsm(_db).LoadRoyalCourtFromWorldState();
            var loaded = CastleLocation.GetCurrentKing();
            (loaded == null || !loaded.IsActive).Should().BeTrue("a restart does not restore the dead monarch");
        }));
    }

    [Fact]
    public async Task AMonarchsDeath_Offline_EmptiesTheThrone()
    {
        await WithKing("Aldric", 1000, async _ =>
        {
            CastleLocation.VacateThrone("The ruler has died of old age.");
            CastleLocation.GetCurrentKing().Should().BeNull();
            CastleLocation.GetMonarchHistory().Select(m => m.Name).Should().Contain("Aldric");
        });
    }
}
