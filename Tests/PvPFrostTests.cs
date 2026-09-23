using System.IO;
using System.Reflection;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.10: the Magician's Frost Touch and Ice Storm carry a "frost" effect that slows a monster,
/// but in PvP it froze the target solid, a full lost turn, and one side could keep the other frozen
/// for a whole fight (player report: "pretty much an autowin if it lands in PvP"). In PvP frost now
/// slows too. "freeze" (the Sage's Freeze spell) still freezes.
/// </summary>
[Collection("SharedGameSingletons")]
public class PvPFrostTests
{
    private static Character Duelist(string name) => new Character { Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Level = 30, HP = 900, MaxHP = 900 };

    private static void Apply(Character target, string effect)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetMethod("ApplyPvPSpellEffect", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(engine, new object[] { Duelist("Caster"), target, new SpellSystem.SpellResult { SpecialEffect = effect } });
    }

    [Fact]
    public void Frost_SlowsInPvP_ItDoesNotFreeze()
    {
        var target = Duelist("Target");
        Apply(target, "frost");
        target.HasStatus(StatusEffect.Slow).Should().BeTrue();
        target.HasStatus(StatusEffect.Frozen).Should().BeFalse("frost is a slow, as it is against a monster");
    }

    [Fact]
    public void Freeze_StillFreezes()
    {
        var target = Duelist("Target");
        Apply(target, "freeze");
        target.HasStatus(StatusEffect.Frozen).Should().BeTrue("the Sage's Freeze is the freezing spell");
    }
}
