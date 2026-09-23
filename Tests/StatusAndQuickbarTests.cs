using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.11:
/// a spell short of mana showed only "(unavailable)", so a new Magician suspected the staff (player report);
/// it now says how much mana is needed and how much there is.
/// </summary>
[Collection("SharedGameSingletons")]
public class StatusAndQuickbarTests
{
    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static Character NewMagician(long mana)
    {
        var term = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        var c = new Character { Name1 = "qb_mage", Name2 = "QB Mage", Class = CharacterClass.Magician, Race = CharacterRace.Elf, Level = 2, Intelligence = 20, Wisdom = 15, HP = 30, MaxHP = 30 };
        typeof(CharacterCreationSystem).GetMethod("GiveStartingWeapon", F)!.Invoke(new CharacterCreationSystem(term), new object[] { c });
        c.Spell = Enumerable.Range(0, GameConfig.MaxSpells).Select(_ => new System.Collections.Generic.List<bool> { false, false }).ToList();
        c.Spell[0][0] = true;
        c.MaxMana = 100; c.Mana = mana;
        GameEngine.AutoPopulateQuickbar(c);
        return c;
    }

    private static System.Collections.Generic.List<(string key, string slotId, string displayName, bool available)> Quickbar(Character c)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        return (System.Collections.Generic.List<(string, string, string, bool)>)typeof(CombatEngine).GetMethod("GetQuickbarActions", F)!.Invoke(engine, new object[] { c })!;
    }

    [Fact]
    public void ASpellShortOfMana_SaysHowMuchIsNeeded()
    {
        var mage = NewMagician(mana: 2);
        var slot = Quickbar(mage).Single(a => a.slotId == "spell:1");
        slot.available.Should().BeFalse();
        var spell = SpellSystem.GetSpellInfo(CharacterClass.Magician, 1)!;
        slot.displayName.Should().Be(Loc.Get("combat.qb_need_mana", spell.Name, SpellSystem.CalculateManaCost(spell, mage), 2));
    }

    [Fact]
    public void ASpellWithEnoughMana_StillShowsItsCost()
    {
        var mage = NewMagician(mana: 100);
        var slot = Quickbar(mage).Single(a => a.slotId == "spell:1");
        slot.available.Should().BeTrue("the new Magician's staff and mana are enough");
        slot.displayName.Should().EndWith(" MP)");
    }
}
