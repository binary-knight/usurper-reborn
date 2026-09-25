using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.13: abdication, an NPC's coronation, the player's claim of an empty throne and a rebellion are each one
/// versioned write of the stored court; a stale one writes nothing and changes nothing else.
/// </summary>
public partial class OwnerProcessConflictTests
{
    private const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>The castle, with a scripted terminal (beforeLine runs before each answer is read) and this player.</summary>
    private static CastleLocation Castle(Character player, IEnumerable<string> lines, Action<int>? beforeLine = null)
    {
        var castle = new CastleLocation();
        typeof(BaseLocation).GetField("terminal", Inst)!.SetValue(castle, new TerminalEmulator(new LineStream(lines, beforeLine), new MemoryStream()));
        typeof(BaseLocation).GetField("currentPlayer", Inst)!.SetValue(castle, player);
        typeof(CastleLocation).GetField("playerIsKing", Inst)!.SetValue(castle, player.King);
        return castle;
    }

    private static async Task<bool> RunCastle(CastleLocation castle, string method)
    {
        try { return await (Task<bool>)typeof(CastleLocation).GetMethod(method, Inst)!.Invoke(castle, null)!; }
        catch (LocationExitException) { return true; }
    }

    private static Character PlayerKing(string name) =>
        new Character { Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Level = 30, HP = 300, MaxHP = 300, King = true, NobleTitle = "King" };

    private string StoredVersionless(RoyalCourtSaveData court) => JsonSerializer.Serialize(court, Json);

    [Fact]
    public async Task AnAbdication_AfterAConcurrentWriteOfTheSameCourt_LandsWhole()
    {
        await WithKing("Kim", 1000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", Court("Kim", 1000));
            new WorldSimService(_db).LoadRoyalCourtFromWorldState();
            var seren = Npc("npc_ab_seren", "Seren");
            seren.Level = 40; seren.Gold = 40_000;
            var kim = PlayerKing("Kim");

            // another process deposits into Kim's treasury while Kim answers the prompt
            var castle = Castle(kim, new[] { "yes" }, _ => OtherCourtWrite("Kim", 2500).GetAwaiter().GetResult());
            await RunCastle(castle, "AttemptAbdication");

            var stored = await StoredCourt();
            stored.KingName.Should().Be("Seren", "the successor's coronation is in the abdication's write");
            stored.MonarchHistory.Select(m => (m.Name, m.EndReason)).Should().Contain(("Kim", "Abdicated"));
            CastleLocation.GetCurrentKing()!.Name.Should().Be("Seren");
            kim.King.Should().BeFalse();
            seren.Gold.Should().Be(20_000, "the successor's gift follows the written coronation");
        }));
    }

    [Fact]
    public async Task AnAbdication_AfterTheStoredThroneChangedHands_WritesNothing_AndChangesNothing()
    {
        await WithKing("Kim", 1000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", Court("Kim", 1000));
            new WorldSimService(_db).LoadRoyalCourtFromWorldState();
            var seren = Npc("npc_ab_seren2", "Seren");
            seren.Level = 40; seren.Gold = 40_000;
            var kim = PlayerKing("Kim");
            var history = CastleLocation.GetMonarchHistory().Count;

            // another process crowned Cedric while Kim answers the prompt
            long cedricAt = 0;
            var castle = Castle(kim, new[] { "yes" }, _ =>
            {
                OtherCourtWrite("Cedric", 3000).GetAwaiter().GetResult();
                cedricAt = _db.GetWorldStateVersion("royal_court");
            });
            await RunCastle(castle, "AttemptAbdication");

            _db.GetWorldStateVersion("royal_court").Should().Be(cedricAt, "nothing was written over Cedric's court");
            (await StoredCourt()).KingName.Should().Be("Cedric");
            kim.King.Should().BeTrue("the player's side follows only a written abdication");
            kim.NobleTitle.Should().Be("King");
            seren.Gold.Should().Be(40_000);
            CastleLocation.GetMonarchHistory().Count(m => m.EndReason == "Abdicated").Should().Be(0);
        }));
    }

    [Fact]
    public async Task AnNpcCoronation_OfAnEmptyThrone_IsWritten_AndAStaleOneWritesNothing()
    {
        await WithKing("Kim", 1000, async _ => await AsTheWorldSim(async () =>
        {
            var vacancy = new RoyalCourtSaveData { KingName = "", ThroneVacant = true, KingAI = 1 };
            await _db.SaveWorldState("royal_court", StoredVersionless(vacancy));
            await NewOsm(_db).LoadRoyalCourtFromWorldState();   // a door's loader: no succession of its own
            CastleLocation.GetCurrentKing().Should().BeNull();
            var seren = Npc("npc_cn_seren", "Seren");
            seren.Level = 40; seren.Gold = 40_000;

            // stale: another process crowned Cedric first
            await OtherCourtWrite("Cedric", 3000);
            long cedricAt = _db.GetWorldStateVersion("royal_court");
            CastleLocation.SetKing(null!);
            (await CastleLocation.CrownNPCAsync(seren)).Should().BeFalse();
            _db.GetWorldStateVersion("royal_court").Should().Be(cedricAt);
            (await StoredCourt()).KingName.Should().Be("Cedric");
            CastleLocation.GetCurrentKing()!.Name.Should().Be("Cedric", "the refusal loads the stored court");
            seren.Gold.Should().Be(40_000);

            // an empty stored throne: written
            await _db.SaveWorldState("royal_court", StoredVersionless(vacancy));
            await NewOsm(_db).LoadRoyalCourtFromWorldState();   // a door's loader: no succession of its own
            (await CastleLocation.CrownNPCAsync(seren)).Should().BeTrue();
            (await StoredCourt()).KingName.Should().Be("Seren");
            (await StoredCourt()).Treasury.Should().Be(20_000);
            CastleLocation.GetCurrentKing()!.Name.Should().Be("Seren");
            seren.Gold.Should().Be(20_000);
        }));
    }

    [Fact]
    public async Task APlayersClaimOfTheEmptyThrone_AfterAnotherCoronation_WritesNothing_AndChangesNothing()
    {
        await WithKing("Kim", 1000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", StoredVersionless(new RoyalCourtSaveData { KingName = "", ThroneVacant = true, KingAI = 1 }));
            new WorldSimService(_db).LoadRoyalCourtFromWorldState();
            var ana = new Character { Name1 = "Ana", Name2 = "Ana", Class = CharacterClass.Warrior, Level = 30, HP = 300, MaxHP = 300, Team = "Wolves" };
            long cedricAt = 0;

            // leave the team: yes; proclaim: yes; another process crowns Cedric before the claim
            var castle = Castle(ana, new[] { "Y", "Y" }, i =>
            {
                if (i != 1) return;
                OtherCourtWrite("Cedric", 3000).GetAwaiter().GetResult();
                cedricAt = _db.GetWorldStateVersion("royal_court");
            });
            await RunCastle(castle, "ClaimEmptyThrone");

            _db.GetWorldStateVersion("royal_court").Should().Be(cedricAt);
            (await StoredCourt()).KingName.Should().Be("Cedric");
            ana.King.Should().BeFalse();
            ana.Team.Should().Be("Wolves", "the team is left only for a written claim");

            // and with the throne still empty, the claim is written
            await _db.SaveWorldState("royal_court", StoredVersionless(new RoyalCourtSaveData { KingName = "", ThroneVacant = true, KingAI = 1 }));
            new WorldSimService(_db).LoadRoyalCourtFromWorldState();
            ana.Team = "";
            await RunCastle(Castle(ana, new[] { "Y" }), "ClaimEmptyThrone");
            (await StoredCourt()).KingName.Should().Be("Ana");
            ana.King.Should().BeTrue();
        }));
    }

    [Fact]
    public async Task ARebellionsVacancy_IsOneVersionedWrite_AndAStaleOneWritesNothing()
    {
        await WithKing("Kim", 1000, async _ => await AsTheWorldSim(async () =>
        {
            var court = JsonSerializer.Deserialize<RoyalCourtSaveData>(Court("Kim", 1000), Json)!;
            court.Prisoners.Add(CastleLocation.PrisonerRecord("Dorn", 7, "Theft"));
            await _db.SaveWorldState("royal_court", StoredVersionless(court));
            new WorldSimService(_db).LoadRoyalCourtFromWorldState();

            // stale: the stored throne changed hands first
            await OtherCourtWrite("Cedric", 3000);
            long cedricAt = _db.GetWorldStateVersion("royal_court");
            var released = new List<string>();
            (await CastleLocation.EndReignAsync("Kim", null, null, s => released = s.Prisoners.Select(p => p.CharacterName).ToList())).Should().BeFalse();
            _db.GetWorldStateVersion("royal_court").Should().Be(cedricAt);
            released.Should().BeEmpty();

            // the reigning king: the vacancy (its prisoners released) is written, and the in-memory king cleared
            await _db.SaveWorldState("royal_court", StoredVersionless(court));
            new WorldSimService(_db).LoadRoyalCourtFromWorldState();
            (await CastleLocation.EndReignAsync("Kim", null, null, s => released = s.Prisoners.Select(p => p.CharacterName).ToList())).Should().BeTrue();
            var stored = await StoredCourt();
            stored.ThroneVacant.Should().BeTrue();
            stored.KingName.Should().BeEmpty();
            stored.Prisoners.Should().BeEmpty();
            released.Should().Equal(new[] { "Dorn" });
            CastleLocation.GetCurrentKing().Should().BeNull();
        }));
    }

    [Fact]
    public void TheRebellion_EndsTheReignThroughTheVersionedWrite_BeforeThePlayersSide()
    {
        string body = CodeOnly(Source("Locations", "CastleLocation.cs"));
        int at = body.IndexOf("private async Task TriggerRebellion()", StringComparison.Ordinal);
        string rebellion = body.Substring(at, body.IndexOf("private async Task SetBailAmount()", at, StringComparison.Ordinal) - at);
        int write = rebellion.IndexOf("await EndReignAsync(currentKing.Name, null, null,", StringComparison.Ordinal);
        write.Should().BeGreaterThan(0);
        rebellion.IndexOf("currentPlayer.King = false;", StringComparison.Ordinal).Should().BeGreaterThan(write);
        rebellion.IndexOf("ImprisonPlayer(prisonerName, 0)", StringComparison.Ordinal).Should().BeGreaterThan(write);
        rebellion.Should().NotContain("PersistRoyalCourtToWorldState").And.NotContain("currentKing = null");
    }

    [Fact]
    public async Task APlayersAbdicationForAscension_AfterTheStoredThroneChangedHands_WritesNothing_AndChangesNothing()
    {
        await WithKing("Kim", 1000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", Court("Kim", 1000));
            new WorldSimService(_db).LoadRoyalCourtFromWorldState();
            var seren = Npc("npc_ap_seren", "Seren");
            seren.Level = 40; seren.Gold = 40_000;
            var kim = PlayerKing("Kim");

            // another process crowned Cedric first
            await OtherCourtWrite("Cedric", 3000);
            long cedricAt = _db.GetWorldStateVersion("royal_court");
            (await CastleLocation.AbdicatePlayerThroneAsync(kim, "ascended")).Should().BeTrue("the reign had already ended elsewhere");

            _db.GetWorldStateVersion("royal_court").Should().Be(cedricAt, "nothing was written over Cedric's court");
            (await StoredCourt()).KingName.Should().Be("Cedric");
            CastleLocation.GetCurrentKing()!.Name.Should().Be("Cedric", "the refusal loads the stored court");
            kim.King.Should().BeTrue("the player's side follows only a written abdication");
            kim.NobleTitle.Should().Be("King");
            seren.Gold.Should().Be(40_000);
            seren.King.Should().BeFalse();
            CastleLocation.GetMonarchHistory().Should().NotContain(m => m.EndReason == "ascended");
        }));
    }

    [Fact]
    public async Task APlayersAbdicationForAscension_OfTheStoredReign_LandsWithItsSuccessor()
    {
        await WithKing("Kim", 1000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", Court("Kim", 1000));
            new WorldSimService(_db).LoadRoyalCourtFromWorldState();
            var seren = Npc("npc_ap_seren2", "Seren");
            seren.Level = 40; seren.Gold = 40_000;
            var kim = PlayerKing("Kim");
            long before = _db.GetWorldStateVersion("royal_court");

            (await CastleLocation.AbdicatePlayerThroneAsync(kim, "ascended")).Should().BeTrue();

            _db.GetWorldStateVersion("royal_court").Should().Be(before + 1, "one versioned write");
            var stored = await StoredCourt();
            stored.KingName.Should().Be("Seren");
            stored.MonarchHistory.Select(m => (m.Name, m.EndReason)).Should().Contain(("Kim", "ascended"));
            CastleLocation.GetCurrentKing()!.Name.Should().Be("Seren");
            kim.King.Should().BeFalse();
            kim.NobleTitle.Should().BeNull();
            seren.King.Should().BeTrue();
            seren.Gold.Should().Be(20_000, "the successor's gift follows the written coronation");
        }));
    }

    [Fact]
    public async Task TheCastlesFallbackCourt_IsWrittenOnlyWhenNoCourtIsStored()
    {
        await WithKing("Kim", 1000, async _ => await AsTheWorldSim(async () =>
        {
            var kim = PlayerKing("Kim");
            var load = typeof(CastleLocation).GetMethod("LoadKingData", Inst)!;
            var isKing = typeof(CastleLocation).GetField("playerIsKing", Inst)!;

            // a vacancy is stored: nothing is written and the castle does not show the player as king
            var vacancy = StoredVersionless(new RoyalCourtSaveData { KingName = "", ThroneVacant = true, KingAI = 1 });
            await _db.SaveWorldState("royal_court", vacancy);
            await NewOsm(_db).LoadRoyalCourtFromWorldState();
            CastleLocation.GetCurrentKing().Should().BeNull();
            long vacantAt = _db.GetWorldStateVersion("royal_court");
            var castle = Castle(kim, Array.Empty<string>());
            load.Invoke(castle, null);
            _db.GetWorldStateVersion("royal_court").Should().Be(vacantAt, "a stored court is never replaced by the fallback");
            (await StoredCourt()).ThroneVacant.Should().BeTrue();
            CastleLocation.GetCurrentKing().Should().BeNull();
            ((bool)isKing.GetValue(castle)!).Should().BeFalse();

            // the stored court is the player's own: it is loaded, not replaced
            await _db.SaveWorldState("royal_court", Court("Kim", 1000));
            long kimAt = _db.GetWorldStateVersion("royal_court");
            CastleLocation.SetKing(null!);
            castle = Castle(kim, Array.Empty<string>());
            load.Invoke(castle, null);
            _db.GetWorldStateVersion("royal_court").Should().Be(kimAt);
            CastleLocation.GetCurrentKing()!.Treasury.Should().Be(1000, "the stored court is the one held");
            ((bool)isKing.GetValue(castle)!).Should().BeTrue();
        }));
    }

    [Fact]
    public async Task TheCastlesFallbackCourt_WithNoCourtStored_IsWrittenAtOnce()
    {
        await WithKing("Kim", 1000, async _ => await AsTheWorldSim(async () =>
        {
            _db.GetWorldStateVersion("royal_court").Should().Be(0);
            var kim = PlayerKing("Kim");
            CastleLocation.SetKing(null!);
            var castle = Castle(kim, Array.Empty<string>());
            typeof(CastleLocation).GetMethod("LoadKingData", Inst)!.Invoke(castle, null);

            _db.GetWorldStateVersion("royal_court").Should().Be(1, "the fresh court is one versioned write");
            (await StoredCourt()).KingName.Should().Be("Kim");
            CastleLocation.GetCurrentKing()!.Name.Should().Be("Kim");
            OnlineStateManager.RoyalCourtVersion.Should().Be(1);
        }));
    }
}
