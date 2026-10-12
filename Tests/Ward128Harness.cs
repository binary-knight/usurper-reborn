using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UsurperRemake;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.8 (H3): the seeded fight falls and the seeded world run of the ward tests, on H2's world harness
/// (RecruitPoolWorld, IsolatedGame). This file names nothing that the base commit (b07b3596) lacks: the ward
/// switch is reached by reflection, so the same file produces the goldens of rows 3 and 8 on the base.
/// A fall is the real path of a fight: CombatEngine.HandleTeammateDeathDispatch with the fight's CombatResult,
/// which is where a monster's killing blow and a damage-over-time death both arrive.
/// </summary>
internal static partial class RecruitPoolRuns
{
    private const BindingFlags WardF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>Sets GameConfig.WardEnabled when the commit has it (the base commit does not).</summary>
    internal static void SetWardSwitch(bool on) =>
        typeof(GameConfig).GetProperty("WardEnabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?.SetValue(null, on);

    /// <summary>A fight in which NPC allies fall: a hero, a monster, a terminal that keeps what is shown.</summary>
    internal sealed class WardFightRig
    {
        public readonly MemoryStream Out = new();
        public readonly TerminalEmulator Term;
        public readonly CombatEngine Engine;
        public readonly Character Hero;
        public readonly CombatResult Result;

        public WardFightRig(string heroTeam, IEnumerable<string>? lines = null)
        {
            Pacing.Disabled = true;
            Term = new TerminalEmulator(new LineStream(lines ?? Array.Empty<string>()), Out);
            Engine = new CombatEngine(Term);
            Hero = new Character
            {
                Name1 = "Ward Hero", Name2 = "Ward Hero", AI = CharacterAI.Human, Class = CharacterClass.Warrior,
                Race = CharacterRace.Human, Level = 20, HP = 300, MaxHP = 300, Mental = 100, MentalHintShown = true,
                Team = heroTeam, CurrentLocation = "Dungeon",
            };
            Result = new CombatResult
            {
                Player = Hero,
                Teammates = new List<Character>(),
                Monsters = new List<Monster> { new Monster { Name = "Rat", Level = 1, HP = 10, MaxHP = 10 } },
            };
        }

        /// <summary>The ally stands in the party at full health, falls, and the death is dispatched.</summary>
        public void Fall(Character ally, string killer = "Deep Thing")
        {
            if (ally is NPC n) { n.IsDead = false; n.IsPermaDead = false; n.DeathDate = null; }
            if (!Result.Teammates.Contains(ally)) Result.Teammates.Add(ally);
            ally.HP = 0;
            ((Task)typeof(CombatEngine).GetMethod("HandleTeammateDeathDispatch", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(Engine, new object[] { ally, killer, Result })!).GetAwaiter().GetResult();
        }

        /// <summary>What the terminal showed so far, without colour codes.</summary>
        public string Shown()
        {
            Term.StreamWriterInternal!.Flush();
            return Regex.Replace(Encoding.UTF8.GetString(Out.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
        }
    }

    /// <summary>The race with the most living members in the roster: the race floor of the permadeath roll is far.</summary>
    internal static CharacterRace CommonRace(RecruitPoolWorld w) =>
        w.Alive().GroupBy(n => n.Race).OrderByDescending(g => g.Count()).ThenBy(g => (int)g.Key).First().Key;

    /// <summary>
    /// "seed,level,falls[,on|off]": a seeded fight in which a hired NPC of that level falls `falls` times
    /// (stood up again between falls). Returns the count of permanent deaths, the draws of the world
    /// simulator's random (count and hash), the state of the shared random, and a hash of every row shown.
    /// </summary>
    public static string WardFight(string arg)
    {
        var a = arg.Split(',');
        int seed = int.Parse(a[0]), level = int.Parse(a[1]), falls = int.Parse(a[2]);
        SetWardSwitch(!(a.Length > 3 && a[3] == "off"));
        SilenceNews();
        using var w = new RecruitPoolWorld(seed);
        WorldSimulator.RegisterPlayerTeam(PlayerTeam);
        try
        {
            var ally = Make("Hired Hand", CommonRace(w), level, team: PlayerTeam);
            var rig = new WardFightRig(PlayerTeam);
            int permanent = 0;
            for (int i = 0; i < falls; i++)
            {
                rig.Fall(ally);
                if (ally.IsPermaDead) permanent++;
            }
            return $"perm={permanent} simDraws={w.SimRandom.Draws} simHash={w.SimRandom.Hash:x16} shared={RecruitPoolWorld.SharedState():x16} " +
                   $"rows={RecruitPoolWorld.Sha16(rig.Shown())}";
        }
        finally { WorldSimulator.UnregisterPlayerTeam(PlayerTeam); }
    }

    /// <summary>
    /// "seed,ticks[,on|off]": a seeded world with a player's team of five hired NPCs, run for `ticks` ticks.
    /// Returns the part of the day the run fell in ("straddle" when it crossed a boundary) and its digest.
    /// The recruit pool switch is left as the commit has it.
    /// </summary>
    public static string WardGolden(string arg)
    {
        var a = arg.Split(',');
        int seed = int.Parse(a[0]), ticks = int.Parse(a[1]);
        SetWardSwitch(!(a.Length > 2 && a[2] == "off"));
        SilenceNews();
        int h0 = DateTime.Now.Hour;
        using var w = new RecruitPoolWorld(seed);
        WorldSimulator.RegisterPlayerTeam(PlayerTeam);
        try
        {
            foreach (var hired in w.Alive().Where(n => string.IsNullOrEmpty(n.Team)).OrderBy(n => n.Name2, StringComparer.Ordinal).Take(5))
                hired.Team = PlayerTeam;
            for (int i = 0; i < ticks; i++) w.Step();
            int h1 = DateTime.Now.Hour;
            string mark = DayPart(h0) != DayPart(h1) ? "straddle" : DayPart(h0);
            int onTeam = w.Npcs.Count(n => n.Team == PlayerTeam);
            int teamPermanent = w.Npcs.Count(n => n.Team == PlayerTeam && n.IsPermaDead);
            return $"{mark} {w.Digest()} onTeam={onTeam} teamPermanent={teamPermanent}";
        }
        finally { WorldSimulator.UnregisterPlayerTeam(PlayerTeam); }
    }
}
