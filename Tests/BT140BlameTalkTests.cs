using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.PartySurvival133Harness;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.8: the SawCombat memory a witness records when the player left an ally to fall (combat.memory_saw_ally_left)
/// still lowers the witness's impression of the player by 0.1, live and after a load, but the dialogue memory layer
/// (DialogueEnhancer.GetMemoryFlavor) never picks it: it picks among the player's other memories as before, or
/// says no memory line. Every other SawCombat memory is picked and spoken as before.
/// </summary>
[Collection("SharedGameSingletons")]
public class BT140BlameTalkTests
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const string Player = "Tester";

    private static NPC WorldNpc(string id, string name)
    {
        var npc = new NPC
        {
            ID = id, Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Race = CharacterRace.Human,
            Level = 10, HP = 500, MaxHP = 500, BaseMaxHP = 500, CurrentLocation = "Main Street",
        };
        npc.Personality = PersonalityProfile.GenerateForArchetype("commoner");
        npc.Brain = new NPCBrain(npc, npc.Personality);
        npc.Memory = npc.Brain.Memory;
        return npc;
    }

    private static MemorySystem Mem(NPC npc) => npc.Brain!.Memory;

    /// <summary>The blame memory as CombatEngine.RecordFinishWitnesses writes it.</summary>
    private static MemoryEvent BlameMemory(string fallen = "Mira") => new MemoryEvent
    {
        Type = MemoryType.SawCombat,
        Description = Loc.GetIn("en", "combat.memory_saw_ally_left", Player, fallen),
        InvolvedCharacter = Player,
        Importance = 0.6f,
        EmotionalImpact = -0.4f,
        Location = "Dungeon",
    };

    /// <summary>An older, weaker memory of a kindness.</summary>
    private static MemoryEvent OlderHelped() => new MemoryEvent
    {
        Type = MemoryType.Helped,
        Description = "Tester helped me",
        InvolvedCharacter = Player,
        Importance = 0.5f,
        EmotionalImpact = 0.5f,
        Timestamp = DateTime.Now.AddHours(-2),
    };

    /// <summary>A SawCombat memory about the player that is not the blame memory.</summary>
    private static MemoryEvent OtherSawCombat() => new MemoryEvent
    {
        Type = MemoryType.SawCombat,
        Description = "Saw Tester fight a stranger in the street",
        InvolvedCharacter = Player,
        Importance = 0.6f,
        EmotionalImpact = -0.4f,
    };

    private static HashSet<string> Pool(string valence)
    {
        var when = Loc.Get("dialogue.enhance.when_today");
        return Enumerable.Range(1, 3).Select(i => Loc.Get($"dialogue.enhance.memory_{valence}_{i}", when)).ToHashSet();
    }

    private static string? Talk(NPC npc) => DialogueEnhancer.GetMemoryFlavor(npc, new Character { Name1 = Player, Name2 = Player });

    private static void InEnglish(Action body)
    {
        var lang = GameConfig.Language;
        try { GameConfig.Language = "en"; body(); }
        finally { GameConfig.Language = lang; }
    }

    /// <summary>Saved as the online save writes them and replayed as a load replays them (as CS140CascadeTests).</summary>
    private static MemorySystem Replay(NPC npc)
    {
        var saved = (List<MemoryData>)typeof(OnlineStateManager).GetMethod("SerializeMemoriesStatic", SNP)!
            .Invoke(null, new object?[] { Mem(npc) })!;
        var fresh = WorldNpc(npc.ID + "_loaded", npc.Name2);
        foreach (var memData in saved)
        {
            if (!Enum.TryParse<MemoryType>(memData.Type, out var memType)) continue;
            Mem(fresh).RecordEvent(new MemoryEvent
            {
                Type = memType, Description = memData.Description, InvolvedCharacter = memData.InvolvedCharacter,
                Timestamp = MemorySystem.RestoredTimestamp(memData.Timestamp, true, DateTime.Now),
                Importance = memData.Importance, EmotionalImpact = memData.EmotionalImpact,
            }, keepTimestamp: true);
        }
        return fresh.Brain!.Memory;
    }

    [Fact]
    public void TheBlameMemoryAlone_GivesNoMemoryLine()
    {
        var npc = WorldNpc("bt140_a", "Blame Witness A");
        Mem(npc).RecordEvent(BlameMemory());
        Mem(npc).GetMemoriesAboutCharacter(Player).Should().ContainSingle();
        InEnglish(() =>
        {
            for (int i = 0; i < 20; i++)
                Talk(npc).Should().BeNull("the witness does not bring up the failed rescue");
        });
    }

    [Fact]
    public void TheBlameMemoryAsTheStrongest_TheOlderMemorysLineIsPicked_AsWithoutTheBlame()
    {
        var withBlame = WorldNpc("bt140_b", "Blame Witness B");
        Mem(withBlame).RecordEvent(OlderHelped(), keepTimestamp: true);
        Mem(withBlame).RecordEvent(BlameMemory());
        Mem(withBlame).GetMemoriesAboutCharacter(Player).OrderByDescending(m => m.Importance).First().Type
            .Should().Be(MemoryType.SawCombat, "the blame memory is the strongest memory about the player");
        var without = WorldNpc("bt140_b0", "Blame Witness B0");
        Mem(without).RecordEvent(OlderHelped(), keepTimestamp: true);
        InEnglish(() =>
        {
            var pos = Pool("pos");
            for (int i = 0; i < 20; i++)
            {
                Talk(without).Should().BeOneOf(pos, "the base: the kindness is the only memory");
                Talk(withBlame).Should().BeOneOf(pos, "the kindness is picked as if the blame memory were not there");
            }
        });
    }

    [Fact]
    public void AnotherSawCombatAboutThePlayer_IsStillPickedAndSpoken()
    {
        var npc = WorldNpc("bt140_c", "Blame Witness C");
        Mem(npc).RecordEvent(OlderHelped(), keepTimestamp: true);
        Mem(npc).RecordEvent(OtherSawCombat());
        InEnglish(() =>
        {
            var neg = Pool("neg");
            for (int i = 0; i < 20; i++)
                Talk(npc).Should().BeOneOf(neg, "a SawCombat that is not the blame memory is picked by importance and spoken as before");
        });
    }

    [Fact]
    public void TheBlameAndAnotherSawCombat_TheOtherIsPicked()
    {
        var npc = WorldNpc("bt140_d", "Blame Witness D");
        Mem(npc).RecordEvent(OtherSawCombat());
        Mem(npc).RecordEvent(BlameMemory());
        InEnglish(() =>
        {
            var neg = Pool("neg");
            for (int i = 0; i < 20; i++)
                Talk(npc).Should().BeOneOf(neg);
        });
    }

    [Fact]
    public void TheImpression_AfterTheBlameMemory_AndAfterASaveAndALoad_IsMinus01ToTheBit()
    {
        var npc = WorldNpc("bt140_e", "Blame Witness E");
        Mem(npc).RecordEvent(BlameMemory());
        float live = Mem(npc).GetCharacterImpression(Player);
        float expected = 0f + MemorySystem.ImpressionChangeFor(MemoryType.SawCombat);
        BitConverter.GetBytes(live).Should().Equal(BitConverter.GetBytes(expected));
        BitConverter.GetBytes(live).Should().Equal(BitConverter.GetBytes(-0.1f));
        var loaded = Replay(npc);
        BitConverter.GetBytes(loaded.GetCharacterImpression(Player)).Should().Equal(BitConverter.GetBytes(live), "a load replays the blame memory");
        InEnglish(() =>
        {
            var loadedNpc = new NPC { ID = "bt140_e_loaded", Name1 = npc.Name2, Name2 = npc.Name2 };
            loadedNpc.Personality = npc.Personality;
            loadedNpc.Brain = new NPCBrain(loadedNpc, loadedNpc.Personality);
            foreach (var m in loaded.AllMemories) loadedNpc.Brain.Memory.RecordEvent(m, keepTimestamp: true);
            Talk(loadedNpc).Should().BeNull("the loaded blame memory is still not brought up");
        });
    }

    // ---------- through the game: a witness who saw the player leave an ally to fall ----------

    [Fact]
    public async Task AWitnessOfALeftToFallFinish_LowersItsImpression_ButSaysNoMemoryLine()
    {
        var twin = WorldNpc("bt140_g", "Blame Witness G");
        var active = NPCSpawnSystem.Instance.ActiveNPCs;
        var news = new List<string>();
        try
        {
            active.Add(twin);
            NewsSystem.Instance.SetCatchUpBuffer(news);
            var copy = Ally(twin.Name2);
            copy.ID = twin.ID;
            var fallen = Ally("Mira");
            var wolf = Mob("Wolf");
            var r = NewRig(owner: Hero(potions: 3), team: new List<Character> { fallen, copy }, monsters: new[] { wolf }, script: Enters);
            await Down(r, fallen, wolf);
            r.Engine.NoteOwnerTurn(r.Owner, r.Team, new CombatAction { Type = CombatActionType.Attack });
            r.Result.CurrentRound = 1;
            (await r.Engine.TryFinishOrAnnounce(wolf, r.Result)).Should().BeTrue();
            r.Result.CurrentRound = 2;
            (await r.Engine.TryFinishOrAnnounce(wolf, r.Result)).Should().BeTrue();

            r.Owner.Name2.Should().Be(Player);
            var blame = Mem(twin).GetMemoriesAboutCharacter(Player);
            blame.Should().ContainSingle(m => m.Type == MemoryType.SawCombat, "the player could have rescued and did not");
            BitConverter.GetBytes(Mem(twin).GetCharacterImpression(Player)).Should().Equal(BitConverter.GetBytes(-0.1f));
            InEnglish(() =>
            {
                for (int i = 0; i < 20; i++) Talk(twin).Should().BeNull();
            });
        }
        finally
        {
            NewsSystem.Instance.ClearCatchUpBuffer();
            active.Remove(twin);
            RestoreSilence();
        }
    }
}
