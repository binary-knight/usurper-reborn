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

    // ─── v1.1.10: guards on hard control in a duel, the rules a monster stun already has ───

    private static (CombatEngine engine, System.Action<string, int> cast) Duel(Character target)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        var m = typeof(CombatEngine).GetMethod("ApplyPvPSpellEffect", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var caster = Duelist("Caster");
        return (engine, (effect, rounds) => m.Invoke(engine, new object[] { caster, target, new SpellSystem.SpellResult { SpecialEffect = effect, Duration = rounds } }));
    }

    /// <summary>One duel round's end, as the PvP loop runs it: statuses tick, then the control guard.</summary>
    private static void EndRound(CombatEngine engine, Character c) { c.ProcessStatusEffects(); engine.TickPvPControl(c); }

    private static bool Held(Character c) => c.HasStatus(StatusEffect.Stunned) || c.HasStatus(StatusEffect.Frozen) || c.HasStatus(StatusEffect.Sleeping);

    [Fact]
    public void ANewHold_DoesNotLandWhileOneHolds()
    {
        var target = Duelist("Target");
        var (engine, cast) = Duel(target);
        cast("stun", 2);
        cast("freeze", 3);
        target.HasStatus(StatusEffect.Stunned).Should().BeTrue();
        target.HasStatus(StatusEffect.Frozen).Should().BeFalse("a second hold used to replace or stack on the first");
    }

    [Fact]
    public void AHold_IsCappedAtThreeRounds()
    {
        var target = Duelist("Target");
        var (_, cast) = Duel(target);
        cast("freeze", 20);   // a Sage's Freeze at high proficiency asks for this
        target.ActiveStatuses[StatusEffect.Frozen].Should().Be(GameConfig.MaxStunDurationNormal);
    }

    [Fact]
    public void AfterAHoldEnds_TheFighterIsImmune_ThenTheNextHoldIsShorter()
    {
        var target = Duelist("Target");
        var (engine, cast) = Duel(target);
        cast("stun", 1);
        EndRound(engine, target);                       // the stun runs out; immunity starts
        Held(target).Should().BeFalse();
        for (int r = 0; r < GameConfig.StunImmunityRoundsAfterRecovery; r++)
        {
            cast("stun", 2);
            Held(target).Should().BeFalse($"immune round {r + 1}");
            EndRound(engine, target);
        }
        cast("stun", 2);
        target.ActiveStatuses[StatusEffect.Stunned].Should().Be(1, "the second hold in the window is halved");
    }

    [Fact]
    public void CastingEveryRound_CanNoLongerHoldAFighterForTheWholeDuel()
    {
        // The report: one side cast every round and the other never acted. Count the rounds held
        // out of twenty with a 2-round stun cast at the start of every round.
        var target = Duelist("Target");
        var (engine, cast) = Duel(target);
        int held = 0;
        for (int round = 0; round < 20; round++)
        {
            cast("stun", 2);
            if (Held(target)) held++;
            EndRound(engine, target);
        }
        held.Should().BeLessThan(10, "the fighter gets to act most rounds");
    }

    [Fact]
    public void HoldsThatComeAgainAsSoonAsTheyCan_GetShorter()
    {
        // Codex's case: a 2-round stun asked for every round. Rounds held and immune rounds used to count
        // towards forgetting the last hold, so every hold that landed was a full 2 rounds. Only free
        // rounds count now, so the second hold that lands is halved.
        var target = Duelist("Target");
        var (engine, cast) = Duel(target);
        var landed = new System.Collections.Generic.List<int>();
        for (int round = 0; round < 16; round++)
        {
            bool before = Held(target);
            cast("stun", 2);
            if (!before && target.HasStatus(StatusEffect.Stunned)) landed.Add(target.ActiveStatuses[StatusEffect.Stunned]);
            EndRound(engine, target);
        }
        landed.Should().HaveCountGreaterThan(1);
        landed[0].Should().Be(2);
        landed[1].Should().Be(1, "the second hold within the window is halved");
    }

    [Fact]
    public void AHoldAfterFourFreeRounds_IsStillShorter()
    {
        // Codex's round-2 case: a 2-round stun on round 1 (rounds 1 and 2 held), then rounds 3 to 6 free
        // (the first three immune), then another stun on round 7. Only four free rounds have passed, so
        // it is halved; counting the round the first hold ended as free reset the returns one round early.
        var target = Duelist("Target");
        var (engine, cast) = Duel(target);
        cast("stun", 2);
        for (int round = 1; round <= 6; round++) EndRound(engine, target);
        Held(target).Should().BeFalse();
        cast("stun", 2);
        target.ActiveStatuses[StatusEffect.Stunned].Should().Be(1);
    }

    [Fact]
    public void AShortenedHold_OnAFighterWhoHasActed_StillCostsATurn()
    {
        // Codex round 7: the AI defender acts after the attacker, so a hold it casts on the attacker ticks
        // once at the round's end before the attacker's next turn. A second hold, halved to one round,
        // ran out there, cost nothing, and still gave the immunity.
        var target = Duelist("Target");
        var (engine, cast) = Duel(target);
        var taken = (System.Collections.Generic.HashSet<Character>)typeof(CombatEngine)
            .GetField("_pvpTurnTakenThisRound", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(engine)!;
        cast("stun", 2);
        for (int round = 1; round <= 5; round++) EndRound(engine, target);   // held, ended, three immune rounds
        Held(target).Should().BeFalse();

        taken.Add(target);                    // the target has had this round's turn
        cast("stun", 2);                      // the second hold in the window: halved to one round
        EndRound(engine, target);
        Held(target).Should().BeTrue("the hold must still cost the target its next turn");
    }

    [Fact]
    public void AShortenedHold_OnAFighterStillToAct_KeepsItsLength()
    {
        var target = Duelist("Target");
        var (engine, cast) = Duel(target);
        cast("stun", 2);
        for (int round = 1; round <= 5; round++) EndRound(engine, target);
        cast("stun", 2);
        target.ActiveStatuses[StatusEffect.Stunned].Should().Be(1, "it costs this round's turn, which is still to come");
    }

    [Fact]
    public void Freeze_StillFreezes()
    {
        var target = Duelist("Target");
        Apply(target, "freeze");
        target.HasStatus(StatusEffect.Frozen).Should().BeTrue("the Sage's Freeze is the freezing spell");
    }
}
