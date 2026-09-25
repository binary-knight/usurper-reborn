using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.13: a failed throne challenge writes what the defence lost in the same court change as the challenger's cell.</summary>
public partial class OwnerProcessConflictTests
{
    private string DefendedCourt(string king) =>
        JsonSerializer.Serialize(new RoyalCourtSaveData
        {
            KingName = king, KingAI = (int)CharacterAI.Computer, Treasury = 1000, TaxRate = 7,
            Guards =
            {
                new RoyalGuardSaveData { Name = "Gareth Vale", AI = (int)CharacterAI.Computer, Loyalty = 100, DailySalary = 10, IsActive = true },
                new RoyalGuardSaveData { Name = "Bryn Holt", AI = (int)CharacterAI.Computer, Loyalty = 100, DailySalary = 10, IsActive = true },
                new RoyalGuardSaveData { Name = "Hugo", AI = (int)CharacterAI.Human, Loyalty = 80, DailySalary = 10, IsActive = true },
            },
            MonsterGuards = { new MonsterGuardSaveData { Name = "Moat Wyrm", Level = 5, HP = 40, MaxHP = 40, Strength = 1, Defence = 0, WeapPow = 1 } }
        }, Json);

    [Fact]
    public async Task AFailedThroneChallenge_KeepsTheBeatenDefendersAndThePenalty_ThroughTheCellWriteAndAReload()
    {
        await WithKing("Aldric", 1000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", DefendedCourt("Aldric"));
            new WorldSimService(_db).LoadRoyalCourtFromWorldState();

            // the king is far too strong; the challenger beats the monster and both NPC guards first
            var aldric = Npc("npc_ch_aldric", "Aldric");
            aldric.King = true;
            aldric.Strength = 100_000; aldric.Defence = 100_000; aldric.MaxHP = 1_000_000; aldric.HP = 1_000_000; aldric.WeapPow = 1000;
            var dorn = Npc("npc_ch_dorn", "Dorn");
            dorn.Level = 20; dorn.Strength = 5000; dorn.WeapPow = 3; dorn.Defence = 10_000; dorn.MaxHP = 5000; dorn.HP = 5000;
            dorn.Brain!.Personality!.Ambition = 0.95f;

            // the human guard did not answer the summons: the defence event has run out
            CastleLocation.GetCurrentKing()!.ActiveDefenseEvent = new PendingDefenseEvent { ChallengerName = "Dorn", TicksRemaining = 1 };
            CastleLocation.GetCurrentKing()!.AI = CharacterAI.Computer;   // an NPC king (the loader keeps the fixture's AI)

            var challenges = (ChallengeSystem)Activator.CreateInstance(typeof(ChallengeSystem), nonPublic: true)!;
            typeof(ChallengeSystem).GetMethod("ProcessThroneChallenge", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(challenges, null);

            void LossesKept(RoyalCourtSaveData court, string when)
            {
                court.KingName.Should().Be("Aldric", "the challenge failed");
                court.MonsterGuards.Should().BeEmpty($"the slain monster stays slain {when}");
                court.Guards.Select(g => g.Name).Should().Equal(new[] { "Hugo" }, $"both beaten guards stay gone {when}");
                court.Guards.Single().Loyalty.Should().Be(65, $"the missed-defence penalty stays {when}");
                court.Prisoners.Select(p => p.CharacterName).Should().Contain("Dorn");
            }
            LossesKept(await StoredCourt(), "in the cell's write");

            // a reload and the next guarded court change keep all of it
            new WorldSimService(_db).LoadRoyalCourtFromWorldState();
            (await CastleLocation.CourtChangeAsync(court => { court.Treasury += 5; return true; })).Should().BeTrue();
            LossesKept(await StoredCourt(), "after a reload and a deposit");
            var king = CastleLocation.GetCurrentKing()!;
            king.Guards.Select(g => g.Name).Should().Equal(new[] { "Hugo" });
            king.MonsterGuards.Should().BeEmpty();
            dorn.DaysInPrison.Should().BeGreaterThan(0);
        }));
    }

    [Fact]
    public async Task APlayersFailedChallenge_WritesTheBeatenDefenders_OnTheStoredCourt()
    {
        await WithKing("Aldric", 1000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", DefendedCourt("Aldric"));
            new WorldSimService(_db).LoadRoyalCourtFromWorldState();
            // another process pays the treasury after this one loaded the court
            var other = JsonSerializer.Deserialize<RoyalCourtSaveData>(DefendedCourt("Aldric"), Json)!;
            other.Treasury = 4000;
            (await _db.SaveWorldStateIfVersion("royal_court", JsonSerializer.Serialize(other, Json), _db.GetWorldStateVersion("royal_court"))).Should().BeTrue();

            var losses = new DefenceLosses();
            losses.MonstersSlain.Add("Moat Wyrm");
            losses.GuardsLost.Add("Bryn Holt");
            await CastleLocation.RecordDefenceLossesAsync(losses);

            var stored = await StoredCourt();
            stored.Treasury.Should().Be(4000, "the losses are applied to the stored court, not written over it");
            stored.MonsterGuards.Should().BeEmpty();
            stored.Guards.Select(g => g.Name).Should().Equal(new[] { "Gareth Vale", "Hugo" });
            CastleLocation.GetCurrentKing()!.Guards.Select(g => g.Name).Should().Equal(new[] { "Gareth Vale", "Hugo" });
        }));
    }
}
