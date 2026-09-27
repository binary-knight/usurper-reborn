using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15 Mental piece 4: the Change overflow guard, the full default Mental, and the old
/// direct Mental write (memory recovery) routed through MentalSystem.Change. The random
/// maintenance gain was removed in piece 5a (ApplyDailyReset replaces it).
/// </summary>
[Collection("SharedGameSingletons")]
public class MentalLoss1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static Character Hero(int mental = 100, int addict = 0) => new Character
    {
        Name1 = "Hero", Name2 = "Hero", AI = CharacterAI.Human,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human, Mental = mental, Addict = addict,
    };

    // Overflow guard

    [Fact]
    public void An_absurd_gain_stops_at_the_cap_instead_of_wrapping()
    {
        var c = Hero(50);
        MentalSystem.Change(c, int.MaxValue).Should().Be(50);
        c.Mental.Should().Be(100);

        var capped = Hero(50, addict: 40);   // cap 80
        MentalSystem.Change(capped, int.MaxValue).Should().Be(30);
        capped.Mental.Should().Be(80);
    }

    [Fact]
    public void An_absurd_loss_stops_at_zero()
    {
        var c = Hero(50);
        MentalSystem.Change(c, int.MinValue).Should().Be(-50);
        c.Mental.Should().Be(0);
    }

    // Default

    [Fact]
    public void A_bare_character_starts_with_full_mental()
    {
        var c = new Character();
        c.Mental.Should().Be(GameConfig.MaxMentalStability);
        MentalSystem.GetBand(c.Mental).Should().Be(MentalBand.Stable);
    }

    [Fact]
    public void A_current_schema_save_at_zero_mental_still_loads_at_zero()
    {
        var restored = MenuKeysNeedEnterPref1115Tests.Restore(new PlayerData { Mental = 0, MentalSchema = GameConfig.MentalSchemaCurrent });
        restored.Mental.Should().Be(0, "the new default must not replace a real saved value");
    }

    // Routed writes

    [Fact]
    public async Task Memory_recovery_gain_stops_at_the_addiction_cap()
    {
        var c = Hero(60, addict: 80);   // cap 60
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 5)), new MemoryStream());
        var feature = new RoomFeature("Old Mirror", "", FeatureInteraction.Examine);
        var method = typeof(FeatureInteractionSystem).GetMethod("HandleMemoryTrigger", F)!;
        await (Task)method.Invoke(FeatureInteractionSystem.Instance, new object[] { feature, c, 1, term, new FeatureOutcome() })!;
        c.Mental.Should().Be(60, "the memory boost goes through Change, which stops at the cap");
    }
}
