using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.0: the elite affixes beside the oath-mark (H3). Two fight rows that no earlier piece held: an
/// oath-marked NPC of the player's team falls to a blow that an affix made the killing one, and the fall is
/// handled as H3 says. Row C: the blow of a Commanding elite's ally, which kills only because of the aura.
/// Row V: the tick of the poison a Venomous elite's hit laid. Each in two outcomes: the fall is not permanent
/// (the fallen banner and the caught row, no scene) or it is (the scene, no caught row). The scenarios run in
/// a copy of the game of their own (IsolatedGame) on the seeded world of the oath-mark tests.
/// </summary>
[Collection("SharedGameSingletons")]
public class EliteAffixOathMark130Tests
{
    [Theory]
    [InlineData("caught")]
    [InlineData("permanent")]
    public void C_ACommandedBlow_ThatFellsAnOathMarkedNpc_ShowsTheCaughtRow_OrTheScene(string outcome) =>
        IsolatedGame.Run(nameof(RecruitPoolRuns.AffixOathMarkCommanded), outcome).Should().Be("ok");

    [Theory]
    [InlineData("caught")]
    [InlineData("permanent")]
    public void V_AVenomTick_ThatFellsAnOathMarkedNpc_ShowsTheCaughtRow_OrTheScene(string outcome) =>
        IsolatedGame.Run(nameof(RecruitPoolRuns.AffixOathMarkVenom), outcome).Should().Be("ok");
}

internal static partial class RecruitPoolRuns
{
    /// <summary>A seeded random whose Next(100) is 0: the Venomous chance always lands; every other draw is the seed's.</summary>
    private sealed class VenomLandsRandom : Random
    {
        public VenomLandsRandom(int seed) : base(seed) { }
        public override int Next(int maxValue) => maxValue == 100 ? 0 : base.Next(maxValue);
    }

    private static Monster AffixFoe(string tier, long hp, long strength, string? affix = null)
    {
        var m = new Monster
        {
            Name = tier, TierName = tier, Level = 8, HP = hp, MaxHP = hp, Strength = strength, Defence = 5, Experience = 1, Gold = 0,
            IsActive = true, CanSpeak = false,
        };
        if (affix != null)
        {
            m.IsElite = true;
            m.EliteAffixes.Add(affix);
            m.Name = MonsterNames.FromKey("monster.elite." + affix, m.Name);
        }
        return m;
    }

    /// <summary>A fight rig with the given foes and a seeded engine random.</summary>
    private static WardFightRig AffixRig(Random random, params Monster[] foes)
    {
        var rig = new WardFightRig(PlayerTeam);
        rig.Result.Monsters.Clear();
        rig.Result.Monsters.AddRange(foes);
        typeof(CombatEngine).GetField("random", WardF)!.SetValue(rig.Engine, random);
        // as a running fight holds them: a teammate's own turn reads the party from the engine
        typeof(CombatEngine).GetField("currentPlayer", WardF)!.SetValue(rig.Engine, rig.Hero);
        typeof(CombatEngine).GetField("currentTeammates", WardF)!.SetValue(rig.Engine, rig.Result.Teammates);
        return rig;
    }

    private static void EngineCall(WardFightRig rig, string method, params object?[] args) =>
        ((Task)typeof(CombatEngine).GetMethod(method, WardF)!.Invoke(rig.Engine, args)!).GetAwaiter().GetResult();

    /// <summary>The ally stands in the party again, alive, with room above the per-hit cap.</summary>
    private static void IntoTheParty(WardFightRig rig, NPC ally, long hp)
    {
        StandUp(ally);
        ally.MaxHP = ally.BaseMaxHP = 100_000;
        ally.HP = hp;
        ally.ActiveStatuses.Clear();
        ally._hitsThisRound = 0;   // a first blow of a round: a later one is cut by a quarter
        if (!rig.Result.Teammates.Contains(ally)) rig.Result.Teammates.Add(ally);
    }

    /// <summary>What H3 shows for the fall: caught (banner, caught row, no scene, no row of loss) or
    /// permanent (banner, the scene in order, no caught row).</summary>
    private static void TheFallIsAsTheOathMarkSays(string outcome, string shown, NPC ally, FixedRandom roll, WardFightRig rig)
    {
        string name = ally.DisplayName;
        string banner = Loc.Get("combat.npc_fallen", name);
        string caught = Loc.Get("oath_mark.caught", name);
        string[] scene = { Loc.Get("oath_mark.flares", name), Loc.Get("oath_mark.breaks"), Loc.Get(WardSystem.LastLineKey(ally.ID), name), Loc.Get("oath_mark.gone", name) };
        roll.Doubles.Should().Be(1, "the permadeath roll is the one draw of the world simulator's random");
        ally.IsDead.Should().BeTrue();
        rig.Result.Teammates.Should().NotContain(ally);
        Times(shown, banner).Should().Be(1, shown);
        if (outcome == "caught")
        {
            ally.IsPermaDead.Should().BeFalse();
            Times(shown, caught).Should().Be(1, shown);
            shown.IndexOf(caught, StringComparison.Ordinal).Should().BeGreaterThan(shown.IndexOf(banner, StringComparison.Ordinal));
            foreach (string row in scene) shown.Should().NotContain(row);
            shown.Should().NotContain(Loc.Get("combat.teammate_sacrifice", name));
        }
        else
        {
            ally.IsPermaDead.Should().BeTrue();
            shown.Should().NotContain(caught);
            int at = shown.IndexOf(banner, StringComparison.Ordinal);
            foreach (string row in scene)
            {
                Times(shown, row).Should().Be(1, row);
                int next = shown.IndexOf(row, StringComparison.Ordinal);
                next.Should().BeGreaterThan(at, row);
                at = next;
            }
        }
    }

    /// <summary>
    /// Row C. A Goblin strikes the oath-marked NPC with a living Commanding elite beside it. The NPC has one
    /// hit point more than the same seeded blow deals without the aura, so it falls to the commanded blow and
    /// stands after the plain one. "caught" or "permanent" sets the permadeath roll.
    /// </summary>
    public static string AffixOathMarkCommanded(string outcome)
    {
        SetWardSwitch(true);
        SilenceNews();
        GameConfig.Language = "en";
        using var w = new RecruitPoolWorld(33);
        WorldSimulator.RegisterPlayerTeam(PlayerTeam);
        try
        {
            GriefSystem.Instance.Reset();
            RomanceTracker.Instance.Reset();
            var ally = Make("Hired Hand", CommonRace(w), 5, team: PlayerTeam);
            WardSystem.IsWarded(ally).Should().BeTrue();

            // the same seeded blow three times: measured without the aura, then with one hit point to spare
            // without the aura, then the same with it
            long Blow(bool commanding, long hp, out WardFightRig rig, out FixedRandom roll)
            {
                roll = new FixedRandom(outcome == "caught" ? 0.999 : 0.0);
                SetSimRandom(w, roll);
                var elite = AffixFoe("Orc", 5000, 1, commanding ? EliteAffixes.Commanding : null);
                var goblin = AffixFoe("Goblin", 4000, 300);
                rig = AffixRig(new Random(128), elite, goblin);
                IntoTheParty(rig, ally, hp);
                EngineCall(rig, "MonsterAttacksCompanion", goblin, ally, rig.Result, rig.Result.Monsters);
                return hp - ally.HP;
            }

            long plain = Blow(false, 50_000, out _, out var noRoll);
            plain.Should().BeGreaterThan(20, "a real blow");
            noRoll.Doubles.Should().Be(0);
            long commanded = (long)Math.Round(plain * EliteAffixes.CommandDamageMultiplier, MidpointRounding.AwayFromZero);
            commanded.Should().BeGreaterThan(plain);

            // without the aura the NPC stands with 1 hit point: no banner, no oath-mark row
            Blow(false, plain + 1, out var plainRig, out var plainRoll).Should().Be(plain);
            ally.HP.Should().Be(1);
            ally.IsDead.Should().BeFalse();
            plainRoll.Doubles.Should().Be(0);
            plainRig.Shown().Should().NotContain(Loc.Get("combat.npc_fallen", ally.DisplayName)).And.NotContain(Loc.Get("oath_mark.caught", ally.DisplayName));

            // with it the same blow is the killing one
            Blow(true, plain + 1, out var rig, out var roll);
            ally.HP.Should().Be(0, "the commanded blow deals {0}, the plain one {1}", commanded, plain);
            TheFallIsAsTheOathMarkSays(outcome, rig.Shown(), ally, roll, rig);
            return "ok";
        }
        finally { WorldSimulator.UnregisterPlayerTeam(PlayerTeam); }
    }

    /// <summary>
    /// Row V. A Venomous elite's hit poisons the oath-marked NPC (the chance is set to land); the NPC is left
    /// with 1 hit point, and the poison's tick at the start of its own turn is the killing blow. The same hit
    /// by the same monster without the affix lays no poison and the NPC takes its turn.
    /// </summary>
    public static string AffixOathMarkVenom(string outcome)
    {
        SetWardSwitch(true);
        SilenceNews();
        GameConfig.Language = "en";
        using var w = new RecruitPoolWorld(33);
        WorldSimulator.RegisterPlayerTeam(PlayerTeam);
        try
        {
            GriefSystem.Instance.Reset();
            RomanceTracker.Instance.Reset();
            var ally = Make("Hired Hand", CommonRace(w), 5, team: PlayerTeam);
            WardSystem.IsWarded(ally).Should().BeTrue();

            WardFightRig Turn(bool venomous, out FixedRandom roll)
            {
                roll = new FixedRandom(outcome == "caught" ? 0.999 : 0.0);
                SetSimRandom(w, roll);
                var elite = AffixFoe("Giant Spider", 1_000_000, 200, venomous ? EliteAffixes.Venomous : null);
                var rig = AffixRig(new VenomLandsRandom(128), elite);
                IntoTheParty(rig, ally, 50_000);
                EngineCall(rig, "MonsterAttacksCompanion", elite, ally, rig.Result, rig.Result.Monsters);
                ally.HP.Should().BeLessThan(50_000, "the hit did damage");
                ally.IsAlive.Should().BeTrue();
                ally.HasStatus(StatusEffect.Poisoned).Should().Be(venomous);
                // the tick at the start of its own turn
                ally.HP = 1;
                EngineCall(rig, "ProcessTeammateActionMultiMonster", ally, rig.Result.Monsters, rig.Result);
                return rig;
            }

            // without the affix: no poison, no tick, no fall
            var plainRig = Turn(false, out var plainRoll);
            ally.IsDead.Should().BeFalse();
            ally.HP.Should().BeGreaterThan(0);
            plainRoll.Doubles.Should().Be(0);
            plainRig.Shown().Should().NotContain(Loc.Get("combat.npc_fallen", ally.DisplayName)).And.NotContain(Loc.Get("oath_mark.caught", ally.DisplayName));

            // with it: the line of the poison, then its tick fells the NPC
            var rig = Turn(true, out var roll);
            string shown = rig.Shown();
            Times(shown, Loc.Get("combat.companion_afflicted", ally.DisplayName, "Poisoned").Trim()).Should().Be(1, shown);
            ally.HP.Should().Be(0);
            TheFallIsAsTheOathMarkSays(outcome, shown, ally, roll, rig);
            shown.IndexOf(Loc.Get("combat.npc_fallen", ally.DisplayName), StringComparison.Ordinal)
                .Should().BeGreaterThan(shown.IndexOf(Loc.Get("combat.companion_afflicted", ally.DisplayName, "Poisoned").Trim(), StringComparison.Ordinal));
            return "ok";
        }
        finally { WorldSimulator.UnregisterPlayerTeam(PlayerTeam); }
    }
}
