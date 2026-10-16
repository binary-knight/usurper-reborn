using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// Design item G (issue #41): an NPC left to die while the player held a usable heal and
/// chose something else resents it. "Could have helped" is judged against the owner's most
/// recent turn, never against potion possession at the instant of death.
/// </summary>
[Collection("SharedGameSingletons")]
public class AllyAbandonmentTests
{
    private static Character Owner(long potions = 3, long mana = 0, CharacterClass cls = CharacterClass.Warrior) => new Character
    {
        Name2 = "Hero", Class = cls, Race = CharacterRace.Human, Healing = potions, Mana = mana, MaxMana = mana,
    };

    private static Character Ally(long hp, long maxHp = 100) => new Character
    {
        Name2 = "Mira", Class = CharacterClass.Cleric, Race = CharacterRace.Elf, HP = hp, MaxHP = maxHp,
    };

    private static CombatEngine EngineFor(Character owner)
    {
        var engine = new CombatEngine();
        typeof(CombatEngine).GetField("_combatOwner", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(engine, owner);
        return engine;
    }

    private static CombatAction Attack() => new CombatAction { Type = CombatActionType.Attack };

    /// <summary>1.3.3 party survival: an ally in scope is lost only at a finish, from Downed; the question
    /// became "Downed at the owner's turn start, a potion held, no Rescue".</summary>
    private static Character DownedAlly() { var a = Ally(0); a.Lifecycle = AllyLifecycle.Downed; return a; }

    [Fact]
    public void PotionsAndADownedAllyAndAnAttack_IsAnOpportunity()
    {
        // 1.3.3: was a low ally (40 of 100); an ally in scope now counts only when Downed at the turn start
        var owner = Owner(); var ally = DownedAlly();
        var engine = EngineFor(owner);
        engine.NoteOwnerTurn(owner, new List<Character> { ally }, Attack());
        engine.CouldHaveHelped(ally, owner).Should().BeTrue();
    }

    [Fact]
    public void NoPotionsAndNoMana_IsNotAnOpportunity()
    {
        var owner = Owner(potions: 0); var ally = Ally(40);
        var engine = EngineFor(owner);
        engine.NoteOwnerTurn(owner, new List<Character> { ally }, Attack());
        engine.CouldHaveHelped(ally, owner).Should().BeFalse();
    }

    [Fact]
    public void ACastableHeal_DoesNotCount_ForADownedAlly()
    {
        // 1.3.3: no spell lifts a Downed ally (Rescue takes a potion), so a castable heal no longer blames
        var owner = Owner(potions: 0, mana: 20, cls: CharacterClass.Cleric); owner.Level = 1;
        owner.Spell = new List<List<bool>> { new() { true } }; // Cure Light (level 1) learned; base cost 15
        var ally = DownedAlly();
        var engine = EngineFor(owner);
        engine.NoteOwnerTurn(owner, new List<Character> { ally }, Attack());
        engine.CouldHaveHelped(ally, owner).Should().BeFalse("a Cleric's Cure Light cannot pick up a Downed ally");
        owner.Healing = 1;
        engine.CouldHaveHelped(ally, owner).Should().BeTrue("a potion could have");
    }

    [Fact]
    public void ACasterWithManaButNoHealSpell_IsNotBlamed()
    {
        var owner = Owner(potions: 0, mana: 50, cls: CharacterClass.Magician); owner.Level = 1; var ally = Ally(40);
        var engine = EngineFor(owner);
        engine.NoteOwnerTurn(owner, new List<Character> { ally }, Attack());
        engine.CouldHaveHelped(ally, owner).Should().BeFalse("no heal spell was castable, whatever the mana");
    }

    [Fact]
    public void AnAllyAboveHalf_AtTheTurnStart_IsNotBlamed()
    {
        // Burst kills from 60 percent are not the player's fault.
        var owner = Owner(); var ally = Ally(60);
        var engine = EngineFor(owner);
        engine.NoteOwnerTurn(owner, new List<Character> { ally }, Attack());
        ally.HP = 0;
        engine.CouldHaveHelped(ally, owner).Should().BeFalse();
    }

    [Fact]
    public void ChoosingAid_ThatTurn_IsNotAbandonment()
    {
        var owner = Owner(); var ally = Ally(40);
        var engine = EngineFor(owner);
        engine.NoteOwnerTurn(owner, new List<Character> { ally }, new CombatAction { Type = CombatActionType.HealAlly });
        engine.CouldHaveHelped(ally, owner).Should().BeFalse();
    }

    [Fact]
    public void PotionLockout_Mercenaries_Echoes_GroupedPlayers_AndExhibitions_AreExcluded()
    {
        var owner = Owner(); var ally = Ally(40);
        var engine = EngineFor(owner);
        engine.NoteOwnerTurn(owner, new List<Character> { ally }, Attack());
        owner.PotionCooldownRounds = 2;
        engine.CouldHaveHelped(ally, owner).Should().BeFalse("a boss potion lockout means no potion was usable");
        owner.PotionCooldownRounds = 0;
        ally.IsMercenary = true;
        engine.CouldHaveHelped(ally, owner).Should().BeFalse();
        ally.IsMercenary = false; ally.IsEcho = true;
        engine.CouldHaveHelped(ally, owner).Should().BeFalse();
        ally.IsEcho = false; ally.GroupPlayerUsername = "beta"; ally.RemoteTerminal = new TerminalEmulator();
        engine.CouldHaveHelped(ally, owner).Should().BeFalse("grouped players die on their own session, item B");
        ally.GroupPlayerUsername = null; ally.RemoteTerminal = null; owner.IsExhibitionCombat = true;
        engine.CouldHaveHelped(ally, owner).Should().BeFalse();
    }

    [Fact]
    public void AFollowersTurn_DoesNotOverwriteTheOwnersSnapshot()
    {
        var owner = Owner(); var follower = Ally(100); follower.GroupPlayerUsername = "beta"; follower.RemoteTerminal = new TerminalEmulator();
        var ally = DownedAlly();   // 1.3.3: Downed, was low (40 of 100)
        var engine = EngineFor(owner);
        engine.NoteOwnerTurn(owner, new List<Character> { ally, follower }, Attack());
        engine.NoteOwnerTurn(follower, new List<Character> { ally, follower }, new CombatAction { Type = CombatActionType.HealAlly });
        engine.CouldHaveHelped(ally, owner).Should().BeTrue("only the owner's own turn is judged");
    }
}
