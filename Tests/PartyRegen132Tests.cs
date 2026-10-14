using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.2: every living party member regains combat stamina and mana at the end of a round, by the
/// leader's own formula on that member's stats: stamina 3 + Stamina/15 + armor tier bonus (2 for
/// light or no armor), mana 2 + Wisdom/10 (at most 15) for a class with spells. Before, only the
/// leader did. Nothing else of the round end changes, and nobody regains twice in one round.
/// </summary>
[Collection("SharedGameSingletons")]
public class PartyRegen132Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static CombatEngine Engine(params Character[] teammates)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character>(teammates));
        return engine;
    }

    private static void RoundEnd(CombatEngine engine, Character leader) =>
        typeof(CombatEngine).GetMethod("ProcessEndOfRoundAbilityEffects", F)!.Invoke(engine, new object[] { leader });

    // Stamina 30: 3 + 2 + 2 (no armor counts as light) = 7. Wisdom 50: 2 + 5 = 7.
    private static Character Leader() => new Character
    {
        Name2 = "Leader", Class = CharacterClass.Cleric, Race = CharacterRace.Human, AI = CharacterAI.Human,
        Level = 10, HP = 200, MaxHP = 200, Stamina = 30, Wisdom = 50,
        CurrentCombatStamina = 20, Mana = 10, MaxMana = 100,
    };

    // Stamina 45: 3 + 3 + 2 = 8. Wisdom 80: 2 + 8 = 10.
    private static NPC NpcTeammate() => new NPC
    {
        Name1 = "Teammate", Name2 = "Teammate", Class = CharacterClass.Magician, Race = CharacterRace.Elf, AI = CharacterAI.Computer,
        Level = 10, HP = 120, MaxHP = 120, Stamina = 45, Wisdom = 80,
        CurrentCombatStamina = 20, Mana = 10, MaxMana = 100,
    };

    // Stamina 60: 3 + 4 + 2 = 9. Wisdom 30: 2 + 3 = 5.
    private static Character Companion() => new Character
    {
        Name2 = "Companion", Class = CharacterClass.Cleric, Race = CharacterRace.Human, IsCompanion = true,
        Level = 10, HP = 120, MaxHP = 120, Stamina = 60, Wisdom = 30,
        CurrentCombatStamina = 20, Mana = 10, MaxMana = 100,
    };

    // Stamina 15: 3 + 1 + 2 = 6. Wisdom 200: 2 + 20 capped at 15.
    private static Character Follower() => new Character
    {
        Name2 = "Follower", Class = CharacterClass.Sage, Race = CharacterRace.Human, AI = CharacterAI.Human,
        Level = 10, HP = 120, MaxHP = 120, Stamina = 15, Wisdom = 200,
        CurrentCombatStamina = 20, Mana = 10, MaxMana = 100,
        RemoteTerminal = new TerminalEmulator(new MemoryStream(), new MemoryStream()),
    };

    [Fact]
    public void CompanionAndNpcTeammate_RegainStaminaAndMana_ByTheirOwnStats()
    {
        var npc = NpcTeammate();
        var comp = Companion();
        RoundEnd(Engine(npc, comp), Leader());

        npc.CurrentCombatStamina.Should().Be(28, "3 + 45/15 + 2 for no armor");
        npc.Mana.Should().Be(20, "2 + 80/10");
        comp.CurrentCombatStamina.Should().Be(29, "3 + 60/15 + 2 for no armor");
        comp.Mana.Should().Be(15, "2 + 30/10");
    }

    [Fact]
    public void NonCaster_RegainsStamina_ButNoMana()
    {
        // The leader's mana regen needs a class with spells; a Warrior with a mana pool gets none.
        var warrior = NpcTeammate();
        warrior.Class = CharacterClass.Warrior;
        RoundEnd(Engine(warrior), Leader());

        warrior.CurrentCombatStamina.Should().Be(28);
        warrior.Mana.Should().Be(10, "the leader's formula gives no mana to a class without spells");
    }

    [Fact]
    public void Leader_RegainsTheSameAmounts_AloneOrListedAmongTheTeammates()
    {
        var alone = Leader();
        RoundEnd(Engine(), alone);
        alone.CurrentCombatStamina.Should().Be(27, "3 + 30/15 + 2 for no armor");
        alone.Mana.Should().Be(17, "2 + 50/10");

        var listed = Leader();
        RoundEnd(Engine(listed, NpcTeammate()), listed);
        listed.CurrentCombatStamina.Should().Be(27, "the leader regains once even when the roster lists it");
        listed.Mana.Should().Be(17);
    }

    [Fact]
    public void DeadMember_AndMemberAtMax_GainNothing()
    {
        var dead = NpcTeammate();
        dead.HP = 0;
        var full = Companion();
        full.CurrentCombatStamina = full.MaxCombatStamina;
        full.Mana = full.MaxMana;
        var beast = new Character { Name2 = "Wolf", Class = CharacterClass.Warrior, IsPet = true, HP = 80, MaxHP = 80, Mana = 0, MaxMana = 0 };
        beast.CurrentCombatStamina = beast.MaxCombatStamina;
        long fullStamina = full.CurrentCombatStamina, beastStamina = beast.CurrentCombatStamina;

        RoundEnd(Engine(dead, full, beast), Leader());

        dead.CurrentCombatStamina.Should().Be(20, "a dead member regains nothing");
        dead.Mana.Should().Be(10);
        full.CurrentCombatStamina.Should().Be(fullStamina);
        full.Mana.Should().Be(full.MaxMana);
        beast.CurrentCombatStamina.Should().Be(beastStamina);
        beast.Mana.Should().Be(0);
    }

    [Fact]
    public void GroupedFollower_RegainsOnce_PerRound()
    {
        var f = Follower();
        f.IsGroupedPlayer.Should().BeTrue();
        // Listed twice (a roster fault) still gives one regen.
        RoundEnd(Engine(f, f), Leader());
        f.CurrentCombatStamina.Should().Be(26, "3 + 15/15 + 2 for no armor, once");
        f.Mana.Should().Be(25, "2 + 200/10 capped at 15, once");
    }

    [Fact]
    public void Followers_StillRegain_WhenTheLeaderHasFallen()
    {
        // The leader's round end does not run while the leader is down; the party pass still does.
        var f = Follower();
        var leader = Leader();
        leader.HP = 0;
        var party = typeof(CombatEngine).GetMethod("RegeneratePartyAtRoundEnd", F);
        party.Should().NotBeNull("the party regeneration is its own step of the round end");
        party!.Invoke(Engine(f), new object[] { leader });
        f.CurrentCombatStamina.Should().Be(26);
        f.Mana.Should().Be(25);
        leader.CurrentCombatStamina.Should().Be(20, "a fallen leader regains nothing");

        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        int call = src.IndexOf("ProcessEndOfRoundAbilityEffects(player);", System.StringComparison.Ordinal);
        call.Should().BeGreaterThan(0);
        src.Substring(call, 400).Should().Contain("else", "the round loop runs the party pass when the leader is down")
            .And.Contain("RegeneratePartyAtRoundEnd(player);");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
