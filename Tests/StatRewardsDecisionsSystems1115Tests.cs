using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 lost stat rewards, piece 4 commit 2: the NG+ cycle bonus is a lasting grant, with a
/// one-time login backfill for NG+ characters made before; the dark bargain's stat loss lasts; the
/// birthday Love gift is a lasting +5 Charisma; the Stamina top-up at rest is gone.
/// </summary>
[Collection("SharedGameSingletons")]
public class StatRewardsDecisionsSystems1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static Character Fresh(string name) => StatRewards1115Tests.Fresh(name);

    private static Task Lasts(Character plain, Character granted, StatKind stat, long delta, string what) =>
        StatRewardsDecisions1115Tests.ShouldLastAgainst(plain, granted, stat, delta, what);

    private static Dictionary<StatKind, long> Bases(Character c) =>
        Enum.GetValues<StatKind>().ToDictionary(s => s, s => c.GetBaseStat(s));

    /// <summary>A character saved by an older version: <paramref name="field"/> is absent, so it loads false.</summary>
    private static Character OldSave(Character c, string field)
    {
        var ser = typeof(SaveSystem).GetMethod("SerializePlayer", F)!;
        PlayerData data;
        try { data = (PlayerData)ser.Invoke(SaveSystem.Instance, new object[] { c })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(data))!.AsObject();
        node.Remove(field).Should().BeTrue("the field is saved");
        var back = JsonSerializer.Deserialize<PlayerData>(node.ToJsonString())!;
        return MenuKeysNeedEnterPref1115Tests.Restore(back);
    }

    private static StorySystemsData Cycle(int cycle) => new StorySystemsData { CurrentCycle = cycle };

    // ---------------- NG+ cycle bonus ----------------

    [Fact]
    public async Task TheNgPlusCycleBonus_Lasts()
    {
        var plain = Fresh("SdNgPlain");
        var c = Fresh("SdNg");
        long gold = c.Gold;
        // cycle 2 is the first NG+; the Usurper ending adds +10 Strength and no story flags
        CycleSystem.Instance.ApplyCycleBonusesToNewCharacter(c, 2, EndingType.Usurper);
        c.RecalculateStats();   // what CreateNewGame does next; it wiped the bonus before 1.2.0
        c.BaseStrength.Should().Be(10 + 15);
        c.BaseDefence.Should().Be(5 + 5);
        c.BaseStamina.Should().Be(10 + 5);
        c.Gold.Should().Be(gold + 500, "the gold part is unchanged");
        c.CycleStatBonusApplied.Should().BeTrue("a character made on this version is never backfilled");
        await Lasts(plain, c, StatKind.Strength, 15, "the NG+ Strength");
        var plain2 = Fresh("SdNgPlain2");
        var c2 = Fresh("SdNg2");
        CycleSystem.Instance.ApplyCycleBonusesToNewCharacter(c2, 3, EndingType.Usurper);
        await Lasts(plain2, c2, StatKind.Stamina, 10, "the NG+ Stamina at cycle 3");
    }

    [Fact]
    public async Task TheNgPlusBackfill_RunsOnce_AndIsIdempotentAcrossTwoLogins()
    {
        var c = OldSave(Fresh("SdBackfill"), nameof(PlayerData.CycleStatBonusApplied));
        c.CycleStatBonusApplied.Should().BeFalse("an older save has no flag, so the backfill is due");
        c.ArtifactStatsApplied.Should().BeTrue();
        var before = Bases(c);

        GameEngine.RunStatRewardMigrations(c, Cycle(3));   // first login: two completed cycles
        c.CycleStatBonusApplied.Should().BeTrue();
        c.BaseStrength.Should().Be(before[StatKind.Strength] + 10);
        c.BaseDefence.Should().Be(before[StatKind.Defence] + 10);
        c.BaseStamina.Should().Be(before[StatKind.Stamina] + 10);
        foreach (var s in new[] { StatKind.Dexterity, StatKind.Constitution, StatKind.Intelligence, StatKind.Wisdom, StatKind.Charisma, StatKind.Agility, StatKind.MaxHP, StatKind.MaxMana })
            c.GetBaseStat(s).Should().Be(before[s], $"{s} is not part of the cycle bonus");
        var afterFirst = Bases(c);

        var again = StatRewards1115Tests.RoundTrip(c);   // saved, then the second login
        again.CycleStatBonusApplied.Should().BeTrue();
        GameEngine.RunStatRewardMigrations(again, Cycle(3));
        Bases(again).Should().Equal(afterFirst, "the second login adds nothing");

        await Lasts(Fresh("SdBackfillPlain"), again, StatKind.Defence, 10, "the backfilled Defence");
    }

    [Fact]
    public void TheNgPlusBackfill_AtCycleOne_LeavesTheCharacterUnchanged()
    {
        var c = OldSave(Fresh("SdCycleOne"), nameof(PlayerData.CycleStatBonusApplied));
        var before = Bases(c);
        (long str, long def, long sta) = (c.Strength, c.Defence, c.Stamina);
        GameEngine.RunStatRewardMigrations(c, Cycle(1));
        Bases(c).Should().Equal(before);
        (c.Strength, c.Defence, c.Stamina).Should().Be((str, def, sta));
        c.CycleStatBonusApplied.Should().BeTrue();
        GameEngine.RunStatRewardMigrations(c, Cycle(4));
        Bases(c).Should().Equal(before, "once done, a later cycle number changes nothing");
    }

    [Fact]
    public void TheNgPlusBackfill_DoesNotCountTheAscensionStep_ForAnImmortal()
    {
        // a first-cycle character who ascended: the cycle went 1 to 2 without a new start
        var c = OldSave(Fresh("SdImmortal"), nameof(PlayerData.CycleStatBonusApplied));
        c.IsImmortal = true;
        var before = Bases(c);
        GameEngine.RunStatRewardMigrations(c, Cycle(2));
        Bases(c).Should().Equal(before, "an immortal who never started NG+ had no cycle bonus");
        c.CycleStatBonusApplied.Should().BeTrue();

        var d = OldSave(Fresh("SdImmortal3"), nameof(PlayerData.CycleStatBonusApplied));
        d.IsImmortal = true;
        GameEngine.RunStatRewardMigrations(d, Cycle(3));   // an NG+ (cycle 2) character who then ascended
        d.BaseStrength.Should().Be(10 + 5);
    }

    [Fact]
    public void ACharacterMadeOnThisVersion_IsNeverBackfilled()
    {
        var r = StatRewards1115Tests.RoundTrip(Fresh("SdNgNew"));
        r.CycleStatBonusApplied.Should().BeTrue();
        var before = Bases(r);
        GameEngine.RunStatRewardMigrations(r, Cycle(5));
        Bases(r).Should().Equal(before);
    }

    // ---------------- dark bargain ----------------

    [Fact]
    public async Task TheDarkBargainStatLoss_Lasts()
    {
        var plain = Fresh("SdBargainPlain");
        var c = Fresh("SdBargain");
        int loss = 3;
        CombatEngine.ApplyDarkBargainStatLoss(c, 0, ref loss).Should().Be("Strength");
        loss.Should().Be(3);
        c.BaseStrength.Should().Be(7);
        await Lasts(plain, c, StatKind.Strength, -3, "the dark bargain Strength loss");
    }

    [Fact]
    public void TheDarkBargainMaxHPLoss_IsInBase_AndStopsAtTheFloors()
    {
        var c = Fresh("SdBargainHP");
        int loss = 4;
        CombatEngine.ApplyDarkBargainStatLoss(c, 5, ref loss).Should().Be("Max HP (-20)");
        loss.Should().Be(20, "the summary shows the Max HP amount");
        c.BaseMaxHP.Should().Be(80);
        c.RecalculateStats();
        c.BaseMaxHP.Should().Be(80);

        var d = Fresh("SdBargainFloor");
        d.GrantPermanentStat(StatKind.Agility, -8);   // Base 2
        int l2 = 5;
        CombatEngine.ApplyDarkBargainStatLoss(d, 3, ref l2).Should().Be("Agility");
        d.BaseAgility.Should().Be(1, "the floor is 1");
    }

    [Fact]
    public void TheDarkBargain_UsesTheLastingHelper()
    {
        string src = File.ReadAllText(Path.Combine(StatRewardsDecisions1115Tests.RepoRoot(), "Scripts/Systems/CombatEngine.cs"));
        string body = StatRewardsDecisions1115Tests.Body(src, "internal static string ApplyDarkBargainStatLoss(");
        new Regex(@"player\.(Strength|Defence|Stamina|Agility|Charisma|MaxHP)\s*(=|\+=|-=)").Matches(body).Should().BeEmpty();
        src.Should().Contain("string lostStatName = ApplyDarkBargainStatLoss(player, random.Next(6), ref statLoss);");
    }

    // ---------------- birthday ----------------

    [Fact]
    public async Task TheBirthdayLoveGift_IsALastingFiveCharisma()
    {
        GameConfig.BirthdayLoveGift.Should().Be(5);
        var plain = Fresh("SdBirthdayPlain");
        var c = Fresh("SdBirthday");
        MailSystem.GiveBirthdayLoveGift(c);
        c.BaseCharisma.Should().Be(10 + 5);
        await Lasts(plain, c, StatKind.Charisma, 5, "the birthday Charisma");

        string src = File.ReadAllText(Path.Combine(StatRewardsDecisions1115Tests.RepoRoot(), "Scripts/Systems/MailSystem.cs"));
        string body = StatRewardsDecisions1115Tests.Body(src, "private static async Task ProcessBirthdayMail(");
        body.Should().Contain("GiveBirthdayLoveGift(player);");
        body.Should().Contain("Loc.Get(\"mail.birthday_gained_cha\", GameConfig.BirthdayLoveGift)");
        body.Should().NotContain("player.Charisma +=");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void TheBirthdayCharismaLine_TakesTheAmount(string lang)
    {
        var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(StatRewardsDecisions1115Tests.RepoRoot(), "Localization", lang + ".json")));
        string line = doc.RootElement.GetProperty("mail.birthday_gained_cha").GetString()!;
        line.Should().Contain("{0}");
        line.Should().NotContain("500");
    }

    // ---------------- Stamina top-up at rest ----------------

    [Theory]
    [InlineData("Scripts/Locations/DormitoryLocation.cs")]
    [InlineData("Scripts/Locations/CastleLocation.cs")]
    [InlineData("Scripts/Locations/MainStreetLocation.cs")]
    [InlineData("Scripts/Locations/HomeLocation.cs")]
    [InlineData("Scripts/Locations/InnLocation.cs")]
    public void NoRestTopsStaminaUpToTwiceConstitution(string file)
    {
        string src = File.ReadAllText(Path.Combine(StatRewardsDecisions1115Tests.RepoRoot(), file));
        new Regex(@"\.Stamina\s*=\s*Math\.Max\(").Matches(src).Should().BeEmpty("a derived Stamina write at rest was wiped at the next fight");
    }
}
