using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15 Mental piece 5a: recovery sources, part A. The daily reset wired into
/// RunBasicDailyReset (the random maintenance gain removed), dungeon camp and Safe Haven rests
/// shared with grouped followers, and the town sources (Inn table and friend, Inn sleep and
/// rented room, Home rest and sleep, spouse, Temple prayer, Church confession).
/// </summary>
[Collection("SharedGameSingletons")]
public class MentalRecovery1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    /// <summary>Source text with // comments removed, so a commented-out call does not count as wired.</summary>
    internal static string Code(string text) => Regex.Replace(text, @"//[^\n]*", "");

    internal static string Src(params string[] parts) => Code(File.ReadAllText(Path.Combine(new[] { RepoRoot(), "Scripts" }.Concat(parts).ToArray())));

    /// <summary>The brace-balanced body of the first method named method in src.</summary>
    internal static string Body(string src, string method)
    {
        var m = Regex.Match(src, @"(?:Task|void|int|bool)\s+" + Regex.Escape(method) + @"\s*\(");
        m.Success.Should().BeTrue($"{method} must be defined");
        int brace = src.IndexOf('{', m.Index); int depth = 0;
        for (int i = brace; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(brace, i - brace + 1);
        }
        throw new InvalidOperationException("unbalanced braces");
    }

    internal static Character Hero(string name = "Hero", int mental = 50, int addict = 0) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        Mental = mental, Addict = addict, HP = 100, MaxHP = 100, MentalHintShown = true,
    };

    internal static TerminalEmulator Term(MemoryStream output) => new TerminalEmulator(new LineStream(Array.Empty<string>()), output);

    internal static string Text(MemoryStream output) =>
        Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");

    // Daily reset wiring

    [Fact]
    public void ApplyDailyReset_has_exactly_one_production_caller_in_RunBasicDailyReset()
    {
        var root = Path.Combine(RepoRoot(), "Scripts");
        var callers = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => Regex.Matches(Code(File.ReadAllText(f)), @"MentalSystem\.ApplyDailyReset\(").Select(_ => Path.GetFileName(f)))
            .ToList();
        callers.Should().Equal(new[] { "DailySystemManager.cs" }, "one call, from the daily reset");
        Body(Src("Systems", "DailySystemManager.cs"), "RunBasicDailyReset").Should().Contain("MentalSystem.ApplyDailyReset(player)");
        Body(Src("Systems", "DailySystemManager.cs"), "RunCatchUpDailyReset").Should().NotContain("Mental");
    }

    [Fact]
    public void Maintenance_no_longer_raises_mental()
    {
        Src("Systems", "MaintenanceSystem.cs").Should().NotContain("Mental", "the random daily gain is replaced by ApplyDailyReset");
    }

    [Fact]
    public void The_daily_reset_restores_mental_and_the_once_a_day_flags()
    {
        var c = Hero(mental: 40);
        MentalSystem.MarkUsed(c, MentalDailySource.InnTable | MentalDailySource.HomeRest);
        MentalSystem.ApplyDailyReset(c).Should().Be(GameConfig.MentalDailyReset);
        c.Mental.Should().Be(40 + GameConfig.MentalDailyReset);
        c.MentalRecoveryUsedToday.Should().Be(MentalDailySource.None);
    }

    // Dungeon rest

    private static (DungeonLocation d, Character hero, MemoryStream output) Dungeon(int mental = 50, int addict = 0)
    {
        var (d, _, output) = DungeonDangerLabel1114Tests.Rig(new DungeonRoom { Id = "r1", Name = "Hall" });
        var hero = (Character)typeof(BaseLocation).GetField("currentPlayer", F)!.GetValue(d)!;
        hero.Mental = mental; hero.Addict = addict; hero.MentalHintShown = true;
        return (d, hero, output);
    }

    [Fact]
    public void A_camp_rest_gains_six_and_prints_the_amount()
    {
        var (d, hero, output) = Dungeon(50);
        d.ApplyRestMentalRecovery(GameConfig.MentalDungeonCampGain);
        GameConfig.MentalDungeonCampGain.Should().Be(6);
        hero.Mental.Should().Be(56);
        Text(output).Should().Contain(Loc.Get("mental.gain", 6));
    }

    [Fact]
    public void A_dungeon_rest_stops_at_the_cap_and_is_quiet_there()
    {
        var (d, hero, output) = Dungeon(60, addict: 80);   // cap 60
        d.ApplyRestMentalRecovery(GameConfig.MentalSafeHavenGain);
        hero.Mental.Should().Be(60);
        Text(output).Should().NotContain(Loc.Get("mental.gain", GameConfig.MentalSafeHavenGain));
    }

    [Fact]
    public void Dungeon_rest_is_shared_by_grouped_followers_only()
    {
        var (d, hero, _) = Dungeon(50);
        var followerOut = new MemoryStream();
        var follower = Hero("Ally", 30);
        follower.RemoteTerminal = Term(followerOut);
        var npc = Hero("Hired Blade", 30); npc.AI = CharacterAI.Computer;
        var deadFollower = Hero("Fallen", 30); deadFollower.RemoteTerminal = Term(new MemoryStream()); deadFollower.HP = 0;
        d.teammates.AddRange(new[] { follower, npc, deadFollower });
        d.ApplyRestMentalRecovery(GameConfig.MentalSafeHavenGain);
        hero.Mental.Should().Be(60);
        follower.Mental.Should().Be(40);
        Text(followerOut).Should().Contain(Loc.Get("mental.gain", 10));
        npc.Mental.Should().Be(30);
        deadFollower.Mental.Should().Be(30);
    }

    [Fact]
    public void The_camp_applies_the_camp_gain()
    {
        GameConfig.MentalDungeonCampGain.Should().Be(6);
        Body(Src("Locations", "DungeonLocation.cs"), "RestInRoom").Should().Contain("ApplyRestMentalRecovery(GameConfig.MentalDungeonCampGain);");
    }

    [Fact]
    public void The_safe_haven_applies_its_gain_only_inside_the_once_per_floor_branch()
    {
        GameConfig.MentalSafeHavenGain.Should().Be(10);
        var body = Body(Src("Locations", "DungeonLocation.cs"), "RestSpotEncounter");
        int guard = body.IndexOf("if (!hasCampedThisFloor)", StringComparison.Ordinal);
        int gain = body.IndexOf("ApplyRestMentalRecovery(GameConfig.MentalSafeHavenGain);", StringComparison.Ordinal);
        int sleep = body.IndexOf("RestAndAdvanceToMorning", StringComparison.Ordinal);
        int alreadyRested = body.IndexOf("dungeon.sanctuary_already_rested", StringComparison.Ordinal);
        guard.Should().BeGreaterThan(0);
        gain.Should().BeGreaterThan(guard);
        gain.Should().BeLessThan(sleep, "the gain lands before a night's sleep can run the daily reset");
        gain.Should().BeLessThan(alreadyRested);
        Regex.Matches(body, "ApplyRestMentalRecovery").Count.Should().Be(1);
    }

    // Town sources: the once-a-day helper

    [Theory]
    [InlineData(MentalDailySource.HomeRest, 8)]
    [InlineData(MentalDailySource.Spouse, 8)]
    [InlineData(MentalDailySource.TemplePrayer, 10)]
    [InlineData(MentalDailySource.Confession, 5)]
    [InlineData(MentalDailySource.HomeSleep, 20)]
    public void A_daily_source_applies_once_then_zero_until_the_daily_reset(MentalDailySource source, int expected)
    {
        int amount = source switch
        {
            MentalDailySource.HomeSleep => GameConfig.MentalHomeSleepGain,
            MentalDailySource.HomeRest => GameConfig.MentalHomeRestGain,
            MentalDailySource.Spouse => GameConfig.MentalSpouseGain,
            MentalDailySource.TemplePrayer => GameConfig.MentalTemplePrayerGain,
            _ => GameConfig.MentalConfessionGain,
        };
        amount.Should().Be(expected);
        var c = Hero(mental: 40);
        MentalSystem.TryDailyGain(c, source, amount).Should().Be(expected);
        c.Mental.Should().Be(40 + expected);
        MentalSystem.UsedToday(c, source).Should().BeTrue();
        MentalSystem.TryDailyGain(c, source, amount).Should().Be(0, "a second same-day use applies nothing");
        c.Mental.Should().Be(40 + expected);
        MentalSystem.ApplyDailyReset(c);
        int afterReset = c.Mental;
        MentalSystem.TryDailyGain(c, source, amount).Should().Be(expected, "the daily reset restores the source");
        c.Mental.Should().Be(afterReset + expected);
    }

    [Fact]
    public void Sleep_gains_are_fifteen_at_the_inn_and_twenty_at_home()
    {
        GameConfig.MentalInnSleepGain.Should().Be(15);
        GameConfig.MentalHomeSleepGain.Should().Be(20);
    }

    [Fact]
    public void A_daily_source_at_the_cap_applies_zero_and_keeps_the_day()
    {
        var c = Hero(mental: 60, addict: 80);   // cap 60
        MentalSystem.GainAvailable(c, MentalDailySource.TemplePrayer).Should().BeFalse();
        MentalSystem.TryDailyGain(c, MentalDailySource.TemplePrayer, GameConfig.MentalTemplePrayerGain).Should().Be(0);
        MentalSystem.UsedToday(c, MentalDailySource.TemplePrayer).Should().BeFalse("a use at the cap is quiet and does not spend the day");
    }

    [Fact]
    public void Daily_sources_skip_npcs()
    {
        var npc = Hero("Patron", 40); npc.AI = CharacterAI.Computer;
        MentalSystem.TryDailyGain(npc, MentalDailySource.Spouse, GameConfig.MentalSpouseGain).Should().Be(0);
        npc.Mental.Should().Be(40);
    }

    [Fact]
    public void GainAvailable_is_false_once_used_today_and_true_before()
    {
        var c = Hero(mental: 40);
        MentalSystem.GainAvailable(c, MentalDailySource.Confession).Should().BeTrue();
        MentalSystem.GainAvailable(c).Should().BeTrue();
        MentalSystem.MarkUsed(c, MentalDailySource.Confession);
        MentalSystem.GainAvailable(c, MentalDailySource.Confession).Should().BeFalse();
        MentalSystem.GainAvailable(c).Should().BeTrue("no source given checks the cap only");
    }

    [Fact]
    public void ReportGain_prints_nothing_for_zero()
    {
        var output = new MemoryStream();
        var c = Hero(mental: 60);
        MentalUi.ReportGain(Term(output), c, 60, 0);
        Text(output).Trim().Should().BeEmpty();
    }

    // Inn table and the friend variant

    private static NPC Patron(string id, string name) => new NPC { ID = id, Name1 = name, Name2 = name, Level = 10, HP = 100, MaxHP = 100 };

    private static void SetScore(Character a, Character b, int score)
    {
        var rec = RelationshipSystem.GetOrCreateRelationship(a, b);
        if (rec.Name1 == a.Name) rec.Relation1 = score; else rec.Relation2 = score;
    }

    [Fact]
    public void The_inn_table_gives_five_alone_and_shares_the_day_with_the_friend_variant()
    {
        RelationshipSystem.Instance.Reset();
        var c = Hero("Rester", 40);
        var friend = Patron("npc_friend_1", "Friendly Patron");
        SetScore(c, friend, GameConfig.RelationFriendship);
        GameConfig.MentalInnTableGain.Should().Be(5);
        MentalSystem.ApplyInnTable(c, new Character[] { Patron("npc_stranger_1", "Stranger") }).Should().Be(5);
        MentalSystem.ApplyInnTable(c, new Character[] { friend }).Should().Be(0, "InnTable and InnFriend share one daily use");
        c.Mental.Should().Be(45);
        MentalSystem.ApplyDailyReset(c);
        int afterReset = c.Mental;
        MentalSystem.ApplyInnTable(c, null).Should().Be(5);
        c.Mental.Should().Be(afterReset + 5);
    }

    [Fact]
    public void The_inn_table_gives_eight_with_a_friend_present_then_nothing_that_day()
    {
        RelationshipSystem.Instance.Reset();
        var c = Hero("Rester", 40);
        var friend = Patron("npc_friend_2", "Old Friend");
        SetScore(c, friend, GameConfig.RelationFriendship);
        GameConfig.MentalInnFriendGain.Should().Be(8);
        MentalSystem.ApplyInnTable(c, new Character[] { Patron("npc_stranger_2", "Stranger"), friend }).Should().Be(8);
        MentalSystem.UsedToday(c, MentalDailySource.InnFriend).Should().BeTrue();
        MentalSystem.ApplyInnTable(c, new Character[] { friend }).Should().Be(0);
        MentalSystem.ApplyInnTable(c, null).Should().Be(0, "the plain table shares the friend's daily use");
        c.Mental.Should().Be(48);
    }

    [Fact]
    public void An_inn_friend_is_the_players_feeling_at_friendship_or_better()
    {
        RelationshipSystem.Instance.Reset();
        var c = Hero("Rester", 40);
        var trusted = Patron("npc_trust", "Trusted One");
        var adoring = Patron("npc_adore", "Admirer");
        var friend = Patron("npc_friend_3", "Friend");
        SetScore(c, trusted, GameConfig.RelationTrust);       // 50, one step short
        SetScore(adoring, c, GameConfig.RelationLove);        // their feeling, not the player's
        SetScore(c, friend, GameConfig.RelationFriendship);
        MentalSystem.IsFriend(c, trusted).Should().BeFalse();
        MentalSystem.IsFriend(c, adoring).Should().BeFalse();
        MentalSystem.IsFriend(c, friend).Should().BeTrue();
    }

    // Town sources: wiring

    private static int At(string body, string text)
    {
        int i = body.IndexOf(text, StringComparison.Ordinal);
        i.Should().BeGreaterThan(0, $"expected '{text}'");
        return i;
    }

    [Fact]
    public void Inn_table_rest_applies_the_inn_table_source()
    {
        var body = Body(Src("Locations", "InnLocation.cs"), "RestAtTable");
        At(body, "MentalSystem.ApplyInnTable(currentPlayer, GetLiveNPCsAtLocation())");
    }

    [Fact]
    public void Inn_sleep_applies_the_sleep_gain_before_the_night()
    {
        var body = Body(Src("Locations", "InnLocation.cs"), "SleepAtInn");
        At(body, "MentalSystem.Change(currentPlayer, GameConfig.MentalInnSleepGain)").Should().BeLessThan(At(body, "RestAndAdvanceToMorning"));
    }

    [Fact]
    public void A_rented_room_warns_at_the_cap_before_charging_and_gains_after_paying()
    {
        var body = Body(Src("Locations", "InnLocation.cs"), "RentRoom");
        int notice = At(body, "if (!MentalSystem.GainAvailable(currentPlayer))");
        int confirm = At(body, "AskYesNoAsync(Loc.Get(\"inn.rent_confirm\")");
        int pay = At(body, "currentPlayer.Gold -= totalCost");
        int gain = At(body, "MentalSystem.Change(currentPlayer, GameConfig.MentalInnSleepGain)");
        int save = At(body, "SaveCurrentGame()");
        notice.Should().BeLessThan(confirm);
        gain.Should().BeGreaterThan(pay);
        gain.Should().BeLessThan(save);
    }

    [Fact]
    public void Home_rest_applies_the_home_rest_source_after_the_rest_limit()
    {
        var body = Body(Src("Locations", "HomeLocation.cs"), "DoRest");
        int gain = At(body, "MentalSystem.TryDailyGain(currentPlayer, MentalDailySource.HomeRest, GameConfig.MentalHomeRestGain)");
        gain.Should().BeGreaterThan(At(body, "currentPlayer.HomeRestsToday++"));
    }

    [Fact]
    public void Home_sleep_applies_the_sleep_gain_in_both_modes()
    {
        var single = Body(Src("Locations", "HomeLocation.cs"), "SleepAtHome");
        At(single, "MentalSystem.Change(currentPlayer, GameConfig.MentalHomeSleepGain)").Should().BeLessThan(At(single, "RestAndAdvanceToMorning"));
        var online = Body(Src("Locations", "HomeLocation.cs"), "SleepAtHomeOnline");
        At(online, "MentalSystem.TryDailyGain(currentPlayer, MentalDailySource.HomeSleep, GameConfig.MentalHomeSleepGain)").Should().BeLessThan(At(online, "throw new LocationExitException"));
        online.Should().NotContain("MentalSystem.Change(", "online home sleep gives its Mental once a day");
    }

    [Fact]
    public void HomeSleep_is_appended_as_bit_ten_and_the_earlier_bits_keep_their_values()
    {
        // Saves store the flags as an int, so existing bits must never move.
        ((int)MentalDailySource.InnTable).Should().Be(1 << 0);
        ((int)MentalDailySource.Wilderness).Should().Be(1 << 6);
        ((int)MentalDailySource.HomeRest).Should().Be(1 << 9);
        ((int)MentalDailySource.HomeSleep).Should().Be(1 << 10);
        Enum.GetValues<MentalDailySource>().Max(v => (int)v).Should().Be(1 << 10);
    }

    [Fact]
    public void A_second_online_home_sleep_the_same_day_gives_no_mental_but_the_rented_room_still_does()
    {
        var c = Hero(mental: 30);
        MentalSystem.TryDailyGain(c, MentalDailySource.HomeSleep, GameConfig.MentalHomeSleepGain).Should().Be(20);
        MentalSystem.TryDailyGain(c, MentalDailySource.HomeSleep, GameConfig.MentalHomeSleepGain).Should().Be(0);
        c.Mental.Should().Be(50);
        var room = Body(Src("Locations", "InnLocation.cs"), "RentRoom");
        room.Should().NotContain("MentalDailySource", "the rented room is paid for and gives its Mental every time");
        At(room, "MentalSystem.Change(currentPlayer, GameConfig.MentalInnSleepGain)");
    }

    [Fact]
    public void Spouse_time_applies_the_spouse_source_for_dinner_walk_and_fire()
    {
        var body = Body(Src("Locations", "HomeLocation.cs"), "SpendQualityTime");
        int guard = At(body, "if (relationType == \"spouse\" && choice >= 1 && choice <= 3)");
        At(body, "MentalSystem.TryDailyGain(currentPlayer, MentalDailySource.Spouse, GameConfig.MentalSpouseGain)").Should().BeGreaterThan(guard);
    }

    [Fact]
    public void Temple_prayer_applies_the_prayer_source_in_both_branches()
    {
        var body = Body(Src("Locations", "TempleLocation.cs"), "ProcessDailyPrayer");
        const string call = "MentalSystem.TryDailyGain(currentPlayer, MentalDailySource.TemplePrayer, GameConfig.MentalTemplePrayerGain)";
        Regex.Matches(body, Regex.Escape(call)).Count.Should().Be(2);
        At(body, call).Should().BeGreaterThan(At(body, "CanPrayToday"));
        body.LastIndexOf(call, StringComparison.Ordinal).Should().BeGreaterThan(At(body, "temple.god_no_longer_exists_short"));
    }

    [Fact]
    public void Confession_warns_before_charging_and_gains_after_paying()
    {
        var body = Body(Src("Locations", "ChurchLocation.cs"), "ProcessConfession");
        int notice = At(body, "if (!MentalSystem.GainAvailable(currentPlayer, MentalDailySource.Confession))");
        int confirm = At(body, "AskYesNoAsync(Loc.Get(\"church.confess_prompt\"))");
        int pay = At(body, "currentPlayer.Gold -= penanceCost");
        int gain = At(body, "MentalSystem.TryDailyGain(currentPlayer, MentalDailySource.Confession, GameConfig.MentalConfessionGain)");
        notice.Should().BeLessThan(confirm);
        gain.Should().BeGreaterThan(pay);
    }
}
