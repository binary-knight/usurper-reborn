using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0: three rare rewards restore HP or Mana in full and grant a stat that raises the maximum
/// (Constitution, Intelligence, Wisdom). The restore comes after the grant, so the pools end full at
/// the new maximum instead of short by the new bonus.
/// </summary>
[Collection("SharedGameSingletons")]
public class StatRewardsHealOrder1115Tests
{
    private const BindingFlags FS = BindingFlags.NonPublic | BindingFlags.Static;

    /// <summary>A Random whose Next returns a fixed value (clamped to the range) and NextDouble 0.</summary>
    private sealed class PickRandom : Random
    {
        private readonly int _pick;
        public PickRandom(int pick) { _pick = pick; }
        public override double NextDouble() => 0.0;
        public override int Next(int maxValue) => Math.Min(_pick, maxValue - 1);
        public override int Next(int minValue, int maxValue) => Math.Clamp(_pick, minValue, maxValue - 1);
    }

    private static async Task Encounter(string method, Character c, int pick)
    {
        var field = typeof(RareEncounters).GetField("random", FS)!;
        var old = field.GetValue(null);
        field.SetValue(null, new PickRandom(pick));
        try
        {
            var m = typeof(RareEncounters).GetMethod(method, FS);
            m.Should().NotBeNull($"{method} must exist");
            var term = new TerminalEmulator(new StatRewardsDecisions1115Tests.ScriptedStream(string.Concat(Enumerable.Repeat("\n", 40))), new MemoryStream());
            try { await (Task)m!.Invoke(null, new object[] { term, c, 10 })!; }
            catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        }
        finally { field.SetValue(null, old); }
    }

    private static Character Hurt(string name)
    {
        var c = StatRewards1115Tests.Fresh(name, CharacterClass.Magician);
        c.HP = 1;
        c.Mana = 0;
        return c;
    }

    [Fact]
    public async Task TheTimeWarpYouth_EndsWithFullHPAndMana_AtTheNewMaximum()
    {
        var c = Hurt("ShoWarp");
        long maxHp = c.MaxHP;
        await Encounter("TimeWarpEncounter", c, 3);
        c.BaseConstitution.Should().Be(13);
        c.MaxHP.Should().BeGreaterThan(maxHp, "Constitution raises Max HP");
        c.HP.Should().Be(c.MaxHP);
        c.Mana.Should().Be(c.MaxMana);
    }

    [Fact]
    public async Task TheCrystalCave_EndsWithFullMana_AtTheNewMaximum()
    {
        var c = Hurt("ShoCrystal");
        long maxMana = c.MaxMana;
        await Encounter("CrystalCaveEncounter", c, 0);
        c.BaseIntelligence.Should().Be(23);
        c.MaxMana.Should().BeGreaterThan(maxMana, "Intelligence raises Max Mana");
        c.Mana.Should().Be(c.MaxMana);
    }

    [Fact]
    public async Task TheAuroraVision_EndsWithFullHPAndMana_AtTheNewMaximum()
    {
        var c = Hurt("ShoAurora");
        long maxMana = c.MaxMana;
        await Encounter("AuroraVisionEncounter", c, 0);
        c.BaseWisdom.Should().Be(23);
        c.MaxMana.Should().BeGreaterThan(maxMana, "Wisdom raises Max Mana");
        c.HP.Should().Be(c.MaxHP);
        c.Mana.Should().Be(c.MaxMana);
    }
}
