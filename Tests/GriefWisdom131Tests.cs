using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.1 (G1): the Wisdom the Main Street line shows for completed grief is added to the stat. It is 5 for
/// each story companion whose grief reached Acceptance (at most 25), derived at every recalculation and never
/// stored; NPC griefs give none; only the player whose griefs they are gets it, in any session.
/// State is in memory only: the grief singleton is filled through Deserialize and reset in Dispose.
/// </summary>
[Collection("SharedGameSingletons")]
public class GriefWisdom131Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static GriefSystem Grief => GriefSystem.Instance;

    public GriefWisdom131Tests() { Grief.Reset(); OceanPhilosophySystem.Instance.Reset(); }
    public void Dispose() { Grief.Reset(); OceanPhilosophySystem.Instance.Reset(); }

    private static Character Caster(string name) => new()
    {
        Name1 = name, Name2 = name, Class = CharacterClass.Magician, Race = CharacterRace.Human, Level = 20,
        AI = CharacterAI.Human, BaseStrength = 10, BaseDexterity = 10, BaseConstitution = 10,
        BaseIntelligence = 20, BaseWisdom = 20, BaseCharisma = 10, BaseMaxHP = 400, BaseMaxMana = 200,
        BaseDefence = 5, BaseStamina = 10, BaseAgility = 10, HP = 400, Mana = 200, Mental = 80
    };

    private static T AsPlayer<T>(Character hero, Func<T> body)
    {
        var engine = GameEngine.Instance;
        var old = engine.CurrentPlayer;
        engine.CurrentPlayer = hero;
        try { return body(); }
        finally { engine.CurrentPlayer = old; }
    }

    private static GriefState Done(CompanionId id) => new()
    {
        CompanionId = id, CompanionName = id.ToString(), DeathType = DeathType.Combat,
        CurrentStage = GriefStage.Acceptance, IsComplete = true
    };

    private static GriefState Running(CompanionId id) => new()
    {
        CompanionId = id, CompanionName = id.ToString(), DeathType = DeathType.Combat,
        CurrentStage = GriefStage.Denial, StageStartDay = 1, GriefStartDay = 1
    };

    private static GriefState DoneNpc(string npcId) => new()
    {
        NpcId = npcId, CompanionName = "Friend", DeathType = DeathType.Combat,
        CurrentStage = GriefStage.Acceptance, IsComplete = true
    };

    /// <summary>The session's griefs, set in memory the way a load sets them.</summary>
    private static void Hold(IEnumerable<GriefState> companions, IEnumerable<GriefState>? npcs = null) =>
        Grief.Deserialize(new GriefSystemData
        {
            ActiveGrief = companions.ToList(),
            ActiveNpcGrief = (npcs ?? Array.Empty<GriefState>()).ToList()
        });

    private static string MainStreetJourney(Character hero)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 10)), output);
        var street = new MainStreetLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
        var task = (Task)typeof(MainStreetLocation).GetMethod("ShowStoryProgress", F)!.Invoke(street, null)!;
        Task.WhenAny(task, Task.Delay(10000)).GetAwaiter().GetResult();
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
    }

    // 1
    [Fact]
    public void NoCompletedGrief_WisdomIsTheValueWithoutThePiece()
    {
        var hero = Caster("Gw1");
        AsPlayer(hero, () =>
        {
            hero.RecalculateStats();
            hero.Wisdom.Should().Be(20, "base Wisdom, no gear, no awakening");
            long mana = hero.MaxMana;
            Hold(new[] { Running(CompanionId.Aldric) });
            hero.RecalculateStats();
            hero.Wisdom.Should().Be(20, "a grief that is still running gives nothing");
            hero.MaxMana.Should().Be(mana);
            hero.BaseWisdom.Should().Be(20);
            return 0;
        });
    }

    // 2
    [Fact]
    public void OneCompletedCompanionGrief_IsFiveWisdom_AndItsMana_TwoAreTen()
    {
        var hero = Caster("Gw2");
        AsPlayer(hero, () =>
        {
            hero.RecalculateStats();
            long wis = hero.Wisdom, mana = hero.MaxMana;
            long manaFor5 = StatEffectsSystem.GetWisdomManaBonus(wis + 5) - StatEffectsSystem.GetWisdomManaBonus(wis);
            manaFor5.Should().Be(15);

            Hold(new[] { Done(CompanionId.Lyris) });
            hero.RecalculateStats();
            hero.Wisdom.Should().Be(wis + 5);
            hero.MaxMana.Should().Be(mana + manaFor5);

            Hold(new[] { Done(CompanionId.Lyris), Done(CompanionId.Mira) });
            hero.RecalculateStats();
            hero.Wisdom.Should().Be(wis + 10, "two companions");
            hero.MaxMana.Should().Be(mana + 2 * manaFor5);
            return 0;
        });
    }

    // 3
    [Fact]
    public void ThreeRecalculations_GiveTheSameWisdom_AndNeverTouchTheBase()
    {
        var hero = Caster("Gw3");
        AsPlayer(hero, () =>
        {
            Hold(new[] { Done(CompanionId.Vex) });
            for (int i = 0; i < 3; i++)
            {
                hero.RecalculateStats();
                hero.Wisdom.Should().Be(25, $"recalculation {i + 1}");
                hero.BaseWisdom.Should().Be(20, $"recalculation {i + 1}");
            }
            return 0;
        });
    }

    // 4
    [Fact]
    public void ASaveAndLoad_KeepsWisdomAndBase_AndDoesNotDouble()
    {
        // The hero wears nothing and holds no weapon power bonus, so the saved WeapPow and ArmPow are 0 and the
        // load does not take the old-equipment migration (GameEngine.RestorePlayerFromSaveDataCore).
        var hero = Caster("Gw4");
        var (story, restored) = AsPlayer(hero, () =>
        {
            Hold(new[] { Done(CompanionId.Lyris) });
            hero.RecalculateStats();
            hero.Wisdom.Should().Be(25);
            hero.BaseWisdom.Should().Be(20);
            hero.WeapPow.Should().Be(0);
            hero.ArmPow.Should().Be(0);
            var data = SaveSystem.Instance.SerializeStorySystemsPublic();
            data.ActiveGriefs.Should().ContainSingle().Which.IsComplete.Should().BeTrue();
            var back = JsonSerializer.Deserialize<StorySystemsData>(JsonSerializer.Serialize(data))!;
            return (back, StatRewards1115Tests.RoundTrip(hero));
        });
        restored.Should().NotBeSameAs(hero);
        restored.BaseWisdom.Should().Be(20, "the save holds the base, not the derived Wisdom");

        Grief.Reset();                                   // a new session
        AsPlayer(restored, () =>
        {
            SaveSystem.Instance.RestoreStorySystems(story);
            restored.Wisdom.Should().Be(25, "the grief Wisdom arrives at the load, once");
            restored.BaseWisdom.Should().Be(20);
            restored.RecalculateStats();
            restored.Wisdom.Should().Be(25);
            restored.BaseWisdom.Should().Be(20);
            return 0;
        });
    }

    // 5
    [Fact]
    public void AnNpcAndACompanion_GetNothing_FromThePlayersGrief()
    {
        var hero = Caster("Gw5");
        var npc = new NPC { Name1 = "Gw5Npc", Name2 = "Gw5Npc", Class = CharacterClass.Cleric, Level = 10, BaseWisdom = 15, BaseMaxHP = 100, BaseMaxMana = 50, HP = 100 };
        var companion = Caster("Gw5Comp");
        companion.IsCompanion = true;
        companion.CompanionId = CompanionId.Mira;
        AsPlayer(hero, () =>
        {
            npc.RecalculateStats();
            companion.RecalculateStats();
            long npcWis = npc.Wisdom, compWis = companion.Wisdom;

            Hold(new[] { Done(CompanionId.Lyris), Done(CompanionId.Aldric) });
            hero.RecalculateStats();
            hero.Wisdom.Should().Be(30);
            npc.RecalculateStats();
            companion.RecalculateStats();
            npc.Wisdom.Should().Be(npcWis, "an NPC has no grief Wisdom");
            companion.Wisdom.Should().Be(compWis, "a companion has no grief Wisdom");
            return 0;
        });
    }

    // 6
    [Fact]
    public void CompletingAGrief_RaisesWisdomAtOnce()
    {
        var hero = Caster("Gw6");
        AsPlayer(hero, () =>
        {
            hero.RecalculateStats();
            long wis = hero.Wisdom, mana = hero.MaxMana;
            Grief.BeginGrief(CompanionId.Melodia, "Melodia", DeathType.Combat);
            hero.Wisdom.Should().Be(wis);
            for (int day = 1; day <= 6 && !Grief.HasCompletedGriefCycle; day++) Grief.UpdateGrief(day * 10000);
            Grief.HasCompletedGriefCycle.Should().BeTrue();
            hero.Wisdom.Should().Be(wis + 5, "the daily update that completes the grief recalculates the player");
            hero.MaxMana.Should().Be(mana + 15);
            hero.BaseWisdom.Should().Be(20);
            return 0;
        });
    }

    // 7
    [Fact]
    public void MainStreet_ShowsTheNumberTheStatGained()
    {
        var hero = Caster("Gw7");
        AsPlayer(hero, () =>
        {
            hero.RecalculateStats();
            long wis = hero.Wisdom;
            Hold(new[] { Done(CompanionId.Lyris), Running(CompanionId.Aldric) });
            hero.RecalculateStats();
            long gained = hero.Wisdom - wis;
            gained.Should().Be(5);
            Grief.GetCurrentEffects().PermanentWisdomBonus.Should().Be((int)gained);
            MainStreetJourney(hero).Should().Contain(Loc.Get("main_street.grief_wisdom", gained));
            return 0;
        });
    }

    // 8
    [Fact]
    public void ACompletedNpcGrief_AddsNothing_ToTheStatOrTheMainStreetNumber()
    {
        var hero = Caster("Gw8");
        AsPlayer(hero, () =>
        {
            hero.RecalculateStats();
            long wis = hero.Wisdom, mana = hero.MaxMana;
            Hold(new[] { Running(CompanionId.Aldric) }, new[] { DoneNpc("gw8_friend") });
            hero.RecalculateStats();
            hero.Wisdom.Should().Be(wis);
            hero.MaxMana.Should().Be(mana);
            Grief.HasCompletedGriefCycle.Should().BeTrue("the NPC grief is complete");
            Grief.GetCurrentEffects().PermanentWisdomBonus.Should().Be(0);
            string text = MainStreetJourney(hero);
            text.Should().Contain("Aldric", "the grief block is open");
            text.Should().NotContain(Loc.Get("main_street.grief_wisdom", 5));

            Hold(new[] { Done(CompanionId.Lyris), Running(CompanionId.Aldric) }, new[] { DoneNpc("gw8_friend") });
            hero.RecalculateStats();
            hero.Wisdom.Should().Be(wis + 5, "the companion's grief counts, the NPC's does not");
            MainStreetJourney(hero).Should().Contain(Loc.Get("main_street.grief_wisdom", 5));
            return 0;
        });
    }

    // 9
    [Fact]
    public void AGroupedFollower_KeepsItsOwnGriefWisdom_InTheLeadersSession()
    {
        var follower = Caster("Gw9F");
        var bare = Caster("Gw9B");       // a follower with no grief of its own
        var leader = Caster("Gw9L");
        long plain = AsPlayer(bare, () => { bare.RecalculateStats(); return bare.Wisdom; });
        var own = AsPlayer(follower, () =>
        {
            Hold(new[] { Done(CompanionId.Lyris) });       // the follower's own session
            follower.RecalculateStats();
            follower.Mana = follower.MaxMana;
            return (wis: follower.Wisdom, mana: follower.MaxMana);
        });
        own.wis.Should().Be(plain + 5);

        Grief.Reset();                                     // the leader's session, with its own griefs
        AsPlayer(leader, () =>
        {
            Hold(new[] { Done(CompanionId.Mira), Done(CompanionId.Vex) });
            leader.RecalculateStats();
            leader.Wisdom.Should().Be(plain + 10);
            follower.RecalculateStats();                   // equipping loot in the leader's session
            follower.Wisdom.Should().Be(plain + 5, "its own grief, not the leader's two and not none");
            follower.MaxMana.Should().Be(own.mana);
            follower.Mana.Should().Be(own.mana, "nothing clamped the pool");
            bare.RecalculateStats();
            bare.Wisdom.Should().Be(plain, "the leader's griefs never reach a follower");
            return 0;
        });
        typeof(Character).GetProperty("GriefWisdom")!
            .GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>().Should().NotBeNull("the stamp is not saved");
    }

    // 10
    [Fact]
    public void FiveCompletedCompanionGriefs_AreTwentyFive()
    {
        var hero = Caster("Gw10");
        AsPlayer(hero, () =>
        {
            hero.RecalculateStats();
            long wis = hero.Wisdom;
            var all = Enum.GetValues<CompanionId>();
            all.Should().HaveCount(5);
            Hold(all.Select(Done), new[] { DoneNpc("gw10_a"), DoneNpc("gw10_b") });
            hero.RecalculateStats();
            hero.Wisdom.Should().Be(wis + 25);
            hero.BaseWisdom.Should().Be(20);
            Grief.GetCurrentEffects().PermanentWisdomBonus.Should().Be(25);
            return 0;
        });
    }
}
