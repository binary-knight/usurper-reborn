using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.1 stat rewards piece 5: the admin console's push to a live session writes every stat to its
/// Base field, so a stat edit survives the RecalculateStats that follows; the wizard /set on an
/// offline player writes the saved Base field too, so the edit survives the load.
/// </summary>
[Collection("SharedGameSingletons")]
public class StatRewardsAdmin121Tests
{
    private static PlayerData Serialize(Character c)
    {
        var ser = typeof(SaveSystem).GetMethod("SerializePlayer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        try { return (PlayerData)ser.Invoke(SaveSystem.Instance, new object[] { c })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static PlayerData Json(PlayerData d) => JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(d))!;

    [Theory]
    [InlineData(StatKind.Stamina)]
    [InlineData(StatKind.Charisma)]
    [InlineData(StatKind.Wisdom)]
    [InlineData(StatKind.Intelligence)]
    [InlineData(StatKind.Constitution)]
    [InlineData(StatKind.Strength)]
    public void AnAdminConsolePush_OfAStat_SurvivesTheRecalc(StatKind stat)
    {
        var live = StatRewards1115Tests.Fresh("P5Admin" + stat);
        var edited = Serialize(live);
        long target = live.GetBaseStat(stat) + 25;
        switch (stat)
        {
            case StatKind.Stamina: edited.Stamina = target; break;
            case StatKind.Charisma: edited.Charisma = target; break;
            case StatKind.Wisdom: edited.Wisdom = target; break;
            case StatKind.Intelligence: edited.Intelligence = target; break;
            case StatKind.Constitution: edited.Constitution = target; break;
            case StatKind.Strength: edited.Strength = target; break;
        }

        OnlineAdminConsole.ApplyEditsToPlayer(live, edited);

        live.GetBaseStat(stat).Should().Be(target, "the push writes the Base field");
        StatRewards1115Tests.Derived(live, stat).Should().Be(target, "the edit survives the recalc at the end of the push");
        live.RecalculateStats();
        StatRewards1115Tests.Derived(live, stat).Should().Be(target, "and any later recalc");
        var back = StatRewards1115Tests.RoundTrip(live);
        StatRewards1115Tests.Derived(back, stat).Should().Be(target, "and a save round trip");
    }

    [Fact]
    public void WizardSetStr_OnAnOfflinePlayer_SurvivesTheLoad()
    {
        var c = StatRewards1115Tests.Fresh("P5WizStr");
        var pd = Json(Serialize(c));
        pd.BaseStrength.Should().BeGreaterThan(0, "a current save carries the Base field, which the load prefers");
        string shown = pd.Strength.ToString();

        WizardCommandSystem.ApplyOfflineSet(pd, "str", 77, out var oldValue).Should().BeTrue();
        oldValue.Should().Be(shown, "the message shows the stat the player saw");
        pd.BaseStrength.Should().Be(77);

        var saved = Json(pd);
        var loaded = MenuKeysNeedEnterPref1115Tests.Restore(saved);
        loaded.BaseStrength.Should().Be(77);
        loaded.Strength.Should().Be(77, "the edit survives the player's load");

        var echo = PlayerCharacterLoader.CreateFromSaveData(Json(pd), "P5WizStr");
        echo.Strength.Should().Be(77, "and the loader used for an offline player's copy");
    }

    [Theory]
    [InlineData("def", StatKind.Defence)]
    [InlineData("sta", StatKind.Stamina)]
    [InlineData("agi", StatKind.Agility)]
    [InlineData("cha", StatKind.Charisma)]
    [InlineData("dex", StatKind.Dexterity)]
    [InlineData("wis", StatKind.Wisdom)]
    [InlineData("int", StatKind.Intelligence)]
    [InlineData("con", StatKind.Constitution)]
    public void WizardSet_EveryStat_OnAnOfflinePlayer_WritesTheBaseField(string field, StatKind stat)
    {
        var c = StatRewards1115Tests.Fresh("P5WizAll" + field);
        var pd = Json(Serialize(c));
        WizardCommandSystem.ApplyOfflineSet(pd, field, 55, out _).Should().BeTrue();
        var loaded = MenuKeysNeedEnterPref1115Tests.Restore(Json(pd));
        loaded.GetBaseStat(stat).Should().Be(55, $"/set {field} survives the load");
    }

    [Fact]
    public void WizardSet_UnknownField_ChangesNothing()
    {
        var pd = Json(Serialize(StatRewards1115Tests.Fresh("P5WizBad")));
        var before = JsonSerializer.Serialize(pd);
        WizardCommandSystem.ApplyOfflineSet(pd, "luck", 5, out _).Should().BeFalse();
        JsonSerializer.Serialize(pd).Should().Be(before);
    }
}
