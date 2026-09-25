using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.13: bail an NPC king grants is stored at once, so it can be paid at once.</summary>
public partial class OwnerProcessConflictTests
{
    private static PrisonLocation Prison(params string[] lines) =>
        new PrisonLocation(GameEngine.Instance, new TerminalEmulator(new LineStream(lines), new MemoryStream()));

    private static Task<T> RunPrison<T>(PrisonLocation prison, string method, Character player) =>
        (Task<T>)typeof(PrisonLocation).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(prison, new object[] { player })!;

    private static Task RunPrison(PrisonLocation prison, string method, Character player) =>
        (Task)typeof(PrisonLocation).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(prison, new object[] { player })!;

    private string NpcCourtHolding(string king, string prisoner, bool withRecord)
    {
        var court = JsonSerializer.Deserialize<RoyalCourtSaveData>(NpcCourt(king, 1000), Json)!;
        if (withRecord) court.Prisoners.Add(CastleLocation.PrisonerRecord(prisoner, 7, "Theft"));
        return JsonSerializer.Serialize(court, Json);
    }

    private static Character Prisoner(string name, long gold = 50_000) =>
        new Character { Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Level = 10, HP = 100, MaxHP = 100, Gold = gold, DaysInPrison = 7 };

    [Fact]
    public async Task BailGrantedByAnNpcKing_CanBePaidAtOnce()
    {
        await WithKing("Aldric", 1000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", NpcCourtHolding("Aldric", "Pat", withRecord: true));
            await NewOsm(_db).LoadRoyalCourtFromWorldState();
            CastleLocation.GetCurrentKing()!.AI = CharacterAI.Computer;   // an NPC king (the loader keeps the fixture's AI)
            var pat = Prisoner("Pat");

            await RunPrison(Prison("1", "", ""), "HandlePetitionKing", pat);   // request bail
            long bail = 1000 + pat.Level * 500;
            CastleLocation.GetCurrentKing()!.Prisoners["Pat"].BailAmount.Should().Be(bail);

            // paying at once meets the stored amount
            (await RunPrison<bool>(Prison("Y", "", ""), "HandlePayBail", pat)).Should().BeTrue("the granted bail is the stored one");
            pat.Gold.Should().Be(50_000 - bail);
            pat.DaysInPrison.Should().Be(0);
            var stored = await StoredCourt();
            stored.Prisoners.Should().NotContain(p => p.CharacterName == "Pat");
            stored.Treasury.Should().Be(1000 + bail);

            // and a bail granted, then a reload, then paid
            await _db.SaveWorldState("royal_court", NpcCourtHolding("Aldric", "Pat", withRecord: true));
            await NewOsm(_db).LoadRoyalCourtFromWorldState();
            pat.DaysInPrison = 7;
            await RunPrison(Prison("1", "", ""), "HandlePetitionKing", pat);
            await NewOsm(_db).LoadRoyalCourtFromWorldState();
            (await RunPrison<bool>(Prison("Y", "", ""), "HandlePayBail", pat)).Should().BeTrue("the bail survives a reload");
            pat.DaysInPrison.Should().Be(0);
        }));
    }
}
