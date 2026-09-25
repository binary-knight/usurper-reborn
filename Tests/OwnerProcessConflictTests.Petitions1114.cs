using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.14: a royal petition ruling that spends treasury gold is refused, with no outcome, when the treasury is short.</summary>
public partial class OwnerProcessConflictTests
{
    private sealed class FixedRandom : Random
    {
        private readonly int _value;
        public FixedRandom(int value) => _value = value;
        public override int Next(int maxValue) => Math.Min(_value, maxValue - 1);
        public override int Next(int min, int max) => min;
        public override double NextDouble() => 0;
    }

    /// <summary>A royal petition of this kind (0 tax, 1 justice, 2 monster) ruled with this choice; returns the screen text.</summary>
    private static async Task<string> RulePetition(int kind, string choice, Character player, NPC petitioner)
    {
        var petitions = new NPCPetitionSystem();
        typeof(NPCPetitionSystem).GetField("_random", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(petitions, new FixedRandom(kind));
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { choice, "", "" }), output);
        await (Task)typeof(NPCPetitionSystem).GetMethod("ExecuteRoyalPetition", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(petitions, new object[] { petitioner, player, term })!;
        return Encoding.UTF8.GetString(output.ToArray());
    }

    [Fact]
    public async Task AnUnaffordableGuardDeployment_LeavesTheTreasury_AndGivesNoOutcome()
    {
        await WithKing("Kim", 100, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", Court("Kim", 100));
            await NewOsm(_db).LoadRoyalCourtFromWorldState();
            var kim = PlayerKing("Kim");

            string screen = await RulePetition(2, "S", kim, Npc("npc_pt_ada", "Ada"));
            (await StoredCourt()).Treasury.Should().Be(100, "the 500 gold deployment is refused");
            CastleLocation.GetCurrentKing()!.Treasury.Should().Be(100);
            kim.Chivalry.Should().Be(0, "a refused ruling has no outcome");
            screen.Should().Contain(Loc.Get("petition.royal.treasury_short", 500, 100));
            screen.Should().NotContain(Loc.Get("petition.royal.monster_send_result"));

            // the stored treasury fell after this court was loaded: the guarded change itself refuses
            await _db.SaveWorldState("royal_court", Court("Kim", 1000));
            await NewOsm(_db).LoadRoyalCourtFromWorldState();
            await OtherCourtWrite("Kim", 300);
            await RulePetition(2, "S", kim, Npc("npc_pt_bo", "Bo"));
            (await StoredCourt()).Treasury.Should().Be(300);
            CastleLocation.GetCurrentKing()!.Treasury.Should().Be(300, "the refusal loads the stored court");
            kim.Chivalry.Should().Be(0);

            // an affordable one is paid and granted
            await OtherCourtWrite("Kim", 800);
            await NewOsm(_db).LoadRoyalCourtFromWorldState();
            await RulePetition(2, "S", kim, Npc("npc_pt_cy", "Cy"));
            (await StoredCourt()).Treasury.Should().Be(300);
            kim.Chivalry.Should().BeGreaterThan(0);
        }));
    }

    [Fact]
    public async Task UnaffordableTaxReliefAndCompensation_AreRefused_AndAffordableOnesArePaid()
    {
        await WithKing("Kim", 20, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", Court("Kim", 20));   // tax rate 7: relief costs 35, half relief 14
            await NewOsm(_db).LoadRoyalCourtFromWorldState();
            var kim = PlayerKing("Kim");
            kim.Gold = 1000;

            await RulePetition(0, "G", kim, Npc("npc_pt_di", "Di"));
            (await StoredCourt()).Treasury.Should().Be(20);
            kim.Chivalry.Should().Be(0);

            var eli = Npc("npc_pt_eli", "Eli");   // compensation: 200 + level 10 * 20
            await RulePetition(1, "C", kim, eli);
            (await StoredCourt()).Treasury.Should().Be(20);
            kim.Gold.Should().Be(1000, "a refused compensation costs the player nothing either");

            await RulePetition(0, "H", kim, Npc("npc_pt_fay", "Fay"));
            (await StoredCourt()).Treasury.Should().Be(6, "the half relief is affordable");
            kim.Chivalry.Should().BeGreaterThan(0);

            await OtherCourtWrite("Kim", 1000);
            await NewOsm(_db).LoadRoyalCourtFromWorldState();
            await RulePetition(1, "C", kim, Npc("npc_pt_gus", "Gus"));
            (await StoredCourt()).Treasury.Should().Be(600);
            kim.Gold.Should().Be(800);
        }));
    }
}
