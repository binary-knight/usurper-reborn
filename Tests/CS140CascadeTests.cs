using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using UsurperRemake.Utils;
using Xunit;
using static UsurperReborn.Tests.PartySurvival133Harness;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.4.0 cascade (design CS-140 v2; his picks 5, C1 and C2): when a monster finishes an ally, every other ally
/// in scope in the fight, Downed ones included, saw it. An NPC witness's live world twin records one SawDeath
/// memory naming the fallen ally, with no change to its impression of the fallen ally. When the player could
/// have rescued the fallen ally (the left-to-die test), each NPC witness thinks 0.15 less of the player, with
/// no memory naming the player, and each companion witness loses 15 loyalty with a line in its history. A
/// non-permanent NPC fall writes one death news line, and the permadeath line is unchanged.
/// Every case runs through the game: a Downed ally, the owner's turn, the announce and the finish.
/// </summary>
[Collection("SharedGameSingletons")]
public class CS140CascadeTests
{
    private const BindingFlags NPI = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const string MemoryKey = "combat.memory_saw_ally_finished";
    private const string HistoryKey = "companion.history_saw_ally_left";

    private sealed class FixedRandom : Random
    {
        private readonly double _value;
        public FixedRandom(double value) { _value = value; }
        public override double NextDouble() => _value;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override int Next() => 0;
    }

    private static NPC WorldNpc(string id, string name, int level = 10)
    {
        var npc = new NPC
        {
            ID = id, Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Race = CharacterRace.Human,
            Level = level, HP = 500, MaxHP = 500, BaseMaxHP = 500, Strength = 30, BaseStrength = 30,
            CurrentLocation = "Main Street",
        };
        npc.Personality = PersonalityProfile.GenerateForArchetype("commoner");
        npc.Brain = new NPCBrain(npc, npc.Personality);
        npc.Memory = npc.Brain.Memory;
        return npc;
    }

    /// <summary>The party copy of a world NPC (DungeonLocation builds the party from copies).</summary>
    private static Character Copy(NPC twin)
    {
        var c = Ally(twin.Name2);
        c.ID = twin.ID;
        return c;
    }

    private static MemorySystem Mem(NPC npc) => npc.Brain!.Memory;

    private static List<MemoryEvent> SawDeaths(NPC npc) => Mem(npc).GetMemoriesOfType(MemoryType.SawDeath);

    /// <summary>The world, the news buffer and the companion singletons, put back after the body.</summary>
    private static async Task InWorld(NPC[] twins, Func<List<string>, Task> body, WorldSimulator? sim = null)
    {
        var active = NPCSpawnSystem.Instance.ActiveNPCs;
        var simBefore = typeof(WorldSimulator).GetField("_instance", SNP)!.GetValue(null);
        var news = new List<string>();
        try
        {
            foreach (var t in twins) active.Add(t);
            if (sim != null) typeof(WorldSimulator).GetField("_instance", SNP)!.SetValue(null, sim);
            NewsSystem.Instance.SetCatchUpBuffer(news);
            await body(news);
        }
        finally
        {
            NewsSystem.Instance.ClearCatchUpBuffer();
            foreach (var t in twins) active.Remove(t);
            typeof(WorldSimulator).GetField("_instance", SNP)!.SetValue(null, simBefore);
            RestoreSilence();
        }
    }

    private static Task<bool> Act(Rig r, Monster m, int round)
    {
        r.Result.CurrentRound = round;
        return r.Engine.TryFinishOrAnnounce(m, r.Result);
    }

    /// <summary>The fallen ally is Downed, the owner takes a turn (Attack) with it Downed, and the wolf announces
    /// and then finishes it. With potions the owner could have rescued it; with none it could not.</summary>
    private static async Task<Rig> FinishFight(Character fallen, IEnumerable<Character> others, long potions,
        Action<Rig>? beforeFinish = null)
    {
        var wolf = Mob("Wolf");
        var team = new List<Character> { fallen };
        team.AddRange(others);
        var r = NewRig(owner: Hero(potions: potions), team: team, monsters: new[] { wolf }, script: Enters);
        await Down(r, fallen, wolf);
        beforeFinish?.Invoke(r);
        r.Engine.NoteOwnerTurn(r.Owner, r.Team, new CombatAction { Type = CombatActionType.Attack });
        (await Act(r, wolf, 1)).Should().BeTrue("the wolf announces its finish");
        (await Act(r, wolf, 2)).Should().BeTrue("the wolf finishes the ally");
        r.Engine.StatsFor(fallen).Finished.Should().BeTrue();
        return r;
    }

    // ---------- 1. an NPC witness remembers the fall, naming the fallen ally, and its impression of it does not move ----------

    [Fact]
    public async Task AnNpcWitness_RecordsOneSawDeathNamingTheFallenAlly_AndItsImpressionOfTheFallenIsUnchanged()
    {
        var twin = WorldNpc("cs140_w1", "Cascade Witness One");
        Mem(twin).CharacterImpressions["Mira"] = 0.3f;
        await InWorld(new[] { twin }, async _ =>
        {
            var r = await FinishFight(Ally("Mira"), new[] { Copy(twin) }, potions: 0);
            var seen = SawDeaths(twin);
            seen.Should().ContainSingle("one memory of the fall");
            seen[0].InvolvedCharacter.Should().Be("Mira", "the memory names the fallen ally");
            seen[0].Description.Should().Be(Loc.GetIn("en", MemoryKey, "Mira", "Wolf"), "stored in English");
            seen[0].Importance.Should().Be(0.8f);
            seen[0].EmotionalImpact.Should().Be(-0.6f);
            Mem(twin).GetCharacterImpression("Mira").Should().Be(0.3f, "the witness grieves the fallen ally, it does not blame it");
            Mem(twin).GetCharacterImpression(r.Owner.Name2).Should().Be(0f, "no rescue was possible, so no blame");
        });
    }

    [Fact]
    public async Task ADownedWitness_Counts()
    {
        var twin = WorldNpc("cs140_w2", "Cascade Witness Two");
        var downedWitness = Copy(twin);
        await InWorld(new[] { twin }, async _ =>
        {
            await FinishFight(Ally("Mira"), new[] { downedWitness }, potions: 0, beforeFinish: r =>
            {
                downedWitness.HP = 0;
                r.Engine.EnterDownedOrDie(downedWitness, "Wolf", null, r.Result).GetAwaiter().GetResult();
                downedWitness.IsDowned.Should().BeTrue();
            });
            downedWitness.IsDowned.Should().BeTrue("the second ally is still Downed when the first is finished");
            SawDeaths(twin).Should().ContainSingle(m => m.InvolvedCharacter == "Mira", "a Downed ally was there and saw it");
        });
    }

    [Fact]
    public async Task AnEcho_AMercenary_AGroupedPlayer_AndTheMiracleBeast_AreNotWitnesses()
    {
        var plain = WorldNpc("cs140_w3", "Cascade Witness Three");
        var echoTwin = WorldNpc("cs140_echo", "Cascade Echo");
        var mercTwin = WorldNpc("cs140_merc", "Cascade Merc");
        var groupTwin = WorldNpc("cs140_group", "Cascade Grouped");
        var beastTwin = WorldNpc("cs140_beast", "Cascade Beast");
        var echo = Copy(echoTwin); echo.IsEcho = true;
        var merc = Copy(mercTwin); merc.IsMercenary = true;
        var grouped = Copy(groupTwin); grouped.RemoteTerminal = new TerminalEmulator(new System.IO.MemoryStream(), new System.IO.MemoryStream());
        var beast = Copy(beastTwin); beast.IsMiracleAlly = true;
        await InWorld(new[] { plain, echoTwin, mercTwin, groupTwin, beastTwin }, async _ =>
        {
            await FinishFight(Ally("Mira"), new[] { Copy(plain), echo, merc, grouped, beast }, potions: 3);
            SawDeaths(plain).Should().ContainSingle("an NPC ally in scope is a witness");
            foreach (var t in new[] { echoTwin, mercTwin, groupTwin, beastTwin })
            {
                Mem(t).AllMemories.Should().BeEmpty($"{t.Name2} is out of scope and is not a witness");
                Mem(t).CharacterImpressions.Should().BeEmpty($"{t.Name2} is out of scope and is not blamed");
            }
        });
    }

    // ---------- 2. blame (C1): 0.15 off the impression of the player, only when the player could have rescued ----------

    [Theory]
    [InlineData(3L, -0.15f)]
    [InlineData(0L, 0f)]
    public async Task AWitnessesImpressionOfThePlayer_DropsBy015_OnlyWhenThePlayerCouldHaveRescued(long potions, float expected)
    {
        var twin = WorldNpc("cs140_w4", "Cascade Witness Four");
        await InWorld(new[] { twin }, async _ =>
        {
            var r = await FinishFight(Ally("Mira"), new[] { Copy(twin) }, potions);
            Mem(twin).GetCharacterImpression(r.Owner.Name2).Should().BeApproximately(expected, 0.0001f);
            SawDeaths(twin).Should().ContainSingle("the memory is written either way");
        });
        (typeof(GameConfig).GetField("CascadeWitnessImpressionDrop")?.GetValue(null)).Should().Be(0.15f, "the GameConfig const");
    }

    [Theory]
    [InlineData(-0.30f, -0.45f)]
    [InlineData(-0.40f, -0.55f)]
    public async Task NoMemoryNamesThePlayer_SoAWitnessAfterTheDropIsNeverARevengeCandidate(float before, float after)
    {
        var twin = WorldNpc("cs140_w5", "Cascade Witness Five", level: 10);
        await InWorld(new[] { twin }, async _ =>
        {
            Character owner = null!;
            await FinishFight(Ally("Mira"), new[] { Copy(twin) }, potions: 3, beforeFinish: r =>
            {
                owner = r.Owner;
                Mem(twin).CharacterImpressions[r.Owner.Name2] = before;
            });
            Mem(twin).GetCharacterImpression(owner.Name2).Should().BeApproximately(after, 0.0001f);
            Mem(twin).AllMemories.Should().NotContain(m => m.InvolvedCharacter == owner.Name2, "this piece writes no memory naming the player");
            var street = StreetEncounterSystem.Instance;
            typeof(StreetEncounterSystem).GetMethod("FindGrudgeNPC", NPI)!.Invoke(street, new object[] { owner, new List<NPC> { twin } })
                .Should().BeNull("the grudge attacks need a SawDeath, Defeated or Murdered memory naming the player");
            ((bool)typeof(StreetEncounterSystem).GetMethod("HasMurderGrudge", NPI)!.Invoke(street, new object[] { owner, new List<NPC> { twin } })!)
                .Should().BeFalse();
        });
    }

    // ---------- 3. a companion witness (C2): the left-to-die loyalty step with a history line, only when the player could have helped ----------

    [Theory]
    [InlineData(3L, 45)]
    [InlineData(0L, 60)]
    public async Task ACompanionWitness_LosesLoyaltyWithAHistoryLine_OnlyWhenThePlayerCouldHaveHelped(long potions, int loyaltyAfter)
    {
        await InWorld(Array.Empty<NPC>(), async _ =>
        {
            var witness = FreshCompanion(CompanionId.Aldric, loyalty: 60);
            var aldric = CompanionSystem.Instance.GetCompanion(CompanionId.Aldric)!;
            int historyBefore = aldric.History.Count;
            await FinishFight(Ally("Mira"), new[] { witness }, potions);
            aldric.LoyaltyLevel.Should().Be(loyaltyAfter);
            if (potions > 0)
            {
                aldric.History.Count.Should().Be(historyBefore + 1);
                var line = aldric.History.Last();
                line.Description.Should().Be(Loc.GetIn("en", HistoryKey, "Mira"), "stored in English, as every history line");
                line.LoyaltyChange.Should().Be(-GameConfig.AbandonCompanionLoyaltyPenalty);
            }
            else
            {
                aldric.History.Count.Should().Be(historyBefore, "no rescue was possible, so no loyalty line");
            }
        });
    }

    [Fact]
    public void TheHistoryLine_IsShownInEachLanguage_AndItsRowsFit79Columns()
    {
        var c = CompanionSystem.Instance.GetCompanion(CompanionId.Aldric)!;
        const string longName = "Bartholomew Underhill the Unready";
        var evt = new CompanionEvent { Type = CompanionEventType.LoyaltyChange, Description = Loc.GetIn("en", HistoryKey, longName) };
        var prev = GameConfig.Language;
        try
        {
            foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
            {
                GameConfig.Language = lang;
                string shown = CompanionSystem.HistoryLabel(c, evt);
                shown.Should().Be(Loc.GetIn(lang, HistoryKey, longName));
                if (lang != "en") shown.Should().NotBe(Loc.GetIn("en", HistoryKey, longName), $"{lang} has its own text");
                if (lang is "en" or "hu")
                {
                    string when = $"  {GameConfig.FormatShortDate(new DateTime(2026, 12, 31), 0)} - ";
                    foreach (var row in UIHelper.WrapAfterPrefix(when, shown))
                        (when.Length + row.Length).Should().BeLessThanOrEqualTo(79, $"{lang}: {row}");
                }
            }
        }
        finally { GameConfig.Language = prev; }
    }

    // ---------- 4. one death news line per fall ----------

    [Fact]
    public async Task ANonPermanentNpcFall_WritesOneDeathNewsLine()
    {
        var fallenTwin = WorldNpc("cs140_f1", "Cascade Fallen One");
        await InWorld(new[] { fallenTwin }, async news =>
        {
            var r = await FinishFight(Copy(fallenTwin), Array.Empty<Character>(), potions: 0);
            fallenTwin.IsDead.Should().BeTrue();
            fallenTwin.IsPermaDead.Should().BeFalse("the fixed draw of 0.99 is no permanent death");
            var lines = news.Where(l => l.Contains(fallenTwin.Name2)).ToList();
            lines.Should().ContainSingle("one line per fall: " + string.Join(" / ", lines));
            string where = string.IsNullOrEmpty(r.Owner.CurrentLocation) ? Loc.Get("combat.news_the_dungeons") : r.Owner.CurrentLocation;
            lines[0].Should().EndWith(Loc.Get("news.death", fallenTwin.Name, "Wolf", where));
        }, sim: NewSim(0.99));
    }

    [Fact]
    public async Task APermanentNpcFall_KeepsItsWillNotReturnLine_Unchanged_AndNoOther()
    {
        var fallenTwin = WorldNpc("cs140_f2", "Cascade Fallen Two", level: 1);
        var town = Enumerable.Range(0, GameConfig.PermadeathPopulationFloor + 5)
            .Select(i => WorldNpc($"cs140_town_{i}", $"Cascade Townsfolk {i}")).ToArray();
        await InWorld(town.Append(fallenTwin).ToArray(), async news =>
        {
            await FinishFight(Copy(fallenTwin), Array.Empty<Character>(), potions: 0);
            fallenTwin.IsPermaDead.Should().BeTrue("the fixed draw of 0 is a permanent death");
            var lines = news.Where(l => l.Contains(fallenTwin.Name2)).ToList();
            lines.Should().ContainSingle("one line per fall: " + string.Join(" / ", lines));
            lines[0].Should().Be("☠ " + Loc.Get("worldsim.news_permadeath", fallenTwin.Name, "Wolf"));
        }, sim: NewSim(0.0));
    }

    private static WorldSimulator NewSim(double draw)
    {
        var before = typeof(WorldSimulator).GetField("_instance", SNP)!.GetValue(null);
        var sim = new WorldSimulator();   // the constructor sets the instance; InWorld puts the old one back
        typeof(WorldSimulator).GetField("_instance", SNP)!.SetValue(null, before);
        typeof(WorldSimulator).GetField("random", NPI | BindingFlags.Public)!.SetValue(sim, new FixedRandom(draw));
        return sim;
    }

    // ---------- 5. the live twin, not the stale party copy, holds the memory ----------

    [Fact]
    public async Task TheLiveTwin_NotTheStalePartyCopy_HoldsTheMemoryAndTheBlame()
    {
        var twin = WorldNpc("cs140_w6", "Cascade Witness Six");
        var staleCopy = WorldNpc("cs140_w6", "Cascade Witness Six");   // its own brain and memory
        staleCopy.HP = 100; staleCopy.MaxHP = 100;
        await InWorld(new[] { twin }, async _ =>
        {
            var r = await FinishFight(Ally("Mira"), new Character[] { staleCopy }, potions: 3);
            SawDeaths(twin).Should().ContainSingle("the live twin is the one the saves carry");
            Mem(twin).GetCharacterImpression(r.Owner.Name2).Should().BeApproximately(-0.15f, 0.0001f);
            Mem(staleCopy).AllMemories.Should().BeEmpty("the party copy is not written");
            Mem(staleCopy).CharacterImpressions.Should().BeEmpty();
        });
    }
}
