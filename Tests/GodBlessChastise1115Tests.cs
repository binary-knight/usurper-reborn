using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 5b: a player-god's bless gives the follower Favor (FavorSource.ImmortalBlessing,
/// capped a day) and a combat bonus that follows the follower's Favor tier; smite never strikes the
/// god's own follower; the bless and smite messages are in Loc. A follower live in another session
/// is changed in memory with the stat update left to that session; one not online is changed on the
/// save in SQL.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodBlessChastise1115Tests : IDisposable
{
    private const string God = "Korvessa";
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-bless-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private static (Character c, GodSystem gods) Follower(string name, int favor, string god = God) =>
        GodMiracles1115Tests.Worshipper(name, god, favor);

    private static string Source(string file) => GodMiracles1115Tests.Source(file);
    private static string Body(string file, string signature) => GodMiracles1115Tests.Body(file, signature);

    // ---------------- Tier-scaled bonus ----------------

    [Theory]
    [InlineData(GodFavorTier.Follower, 0.05f)]
    [InlineData(GodFavorTier.Devout, 0.07f)]
    [InlineData(GodFavorTier.Zealot, 0.10f)]
    [InlineData(GodFavorTier.Chosen, 0.10f)]
    public void BlessBonus_FollowsTheTier(GodFavorTier tier, float expected)
    {
        ImmortalDeedSystem.BlessBonusFor(tier).Should().BeApproximately(expected, 0.0001f);
    }

    [Theory]
    [InlineData(0, 0.05f)]
    [InlineData(30, 0.07f)]
    [InlineData(60, 0.10f)]
    [InlineData(80, 0.10f)]
    public void Bless_GivesTheFollowerTheBonusOfTheirTier(int favor, float expected)
    {
        var (c, gods) = Follower($"GbTier{favor}", favor);
        var o = ImmortalDeedSystem.Bless(c, God, otherSession: false, gods);
        o.Refused.Should().BeFalse();
        o.Bonus.Should().BeApproximately(expected, 0.0001f);
        c.DivineBlessingBonus.Should().BeApproximately(expected, 0.0001f);
        c.DivineBlessingCombats.Should().Be(GameConfig.GodBlessCombatDuration);
    }

    [Fact]
    public void Bless_TakesTheTierAfterItsOwnFavor()
    {
        var (c, gods) = Follower("GbCross", GameConfig.GodFavorTierDevoutMin - 2);
        var o = ImmortalDeedSystem.Bless(c, God, otherSession: false, gods);
        o.FavorNow.Should().Be(GameConfig.GodFavorTierDevoutMin);
        o.Bonus.Should().BeApproximately(GameConfig.GodBlessBonusDevout, 0.0001f);
    }

    [Fact]
    public void Bless_OfAnNpc_GivesNoFavor_AndTheFullNpcBonus()
    {
        var npc = new Character { Name1 = "GbNpc", Name2 = "GbNpc", AI = CharacterAI.Computer, WorshippedGod = God };
        var o = ImmortalDeedSystem.Bless(npc, God, otherSession: false, new GodSystem());
        o.Refused.Should().BeFalse();
        o.FavorGained.Should().Be(0);
        npc.GodFavor.Should().Be(0);
        npc.DivineBlessingBonus.Should().BeApproximately(GameConfig.GodBlessBonusNpc, 0.0001f);
        GameConfig.GodBlessBonusNpc.Should().BeApproximately(0.10f, 0.0001f, "an NPC bless keeps the 10% it gave before tiers");
    }

    [Fact]
    public void Bless_NeverWeakensAStrongerRunningBlessing_ButReplacesAnEndedOne()
    {
        ImmortalDeedSystem.MergeBlessing(3, 0.15f, 0.05f).Should().Be((GameConfig.GodBlessCombatDuration, 0.15f));
        ImmortalDeedSystem.MergeBlessing(0, 0.15f, 0.05f).Should().Be((GameConfig.GodBlessCombatDuration, 0.05f));
        ImmortalDeedSystem.MergeBlessing(20, 0.02f, 0.07f).Should().Be((20, 0.07f));
    }

    // ---------------- Favor and its daily cap ----------------

    [Fact]
    public void Bless_GivesTwoFavor_OnceADay_ThenAgainAfterTheDailyReset()
    {
        var (c, gods) = Follower("GbCap", 40);
        var first = ImmortalDeedSystem.Bless(c, God, otherSession: false, gods);
        first.FavorGained.Should().Be(GameConfig.GodBlessFavorGain).And.Be(2);
        c.GodFavor.Should().Be(42);
        FavorSystem.GainedToday(c, FavorSource.ImmortalBlessing).Should().Be(2);

        var second = ImmortalDeedSystem.Bless(c, God, otherSession: false, gods);
        second.Refused.Should().BeFalse("the combat blessing still lands");
        second.FavorGained.Should().Be(0, "the day's cap of 2 is spent");
        c.GodFavor.Should().Be(42);

        FavorSystem.ApplyDailyReset(c, gods);
        ImmortalDeedSystem.Bless(c, God, otherSession: false, gods).FavorGained.Should().Be(2);
        GameConfig.GodBlessFavorDailyCap.Should().Be(2);
    }

    [Fact]
    public void Bless_CountsOnlyItsOwnSource()
    {
        var (c, gods) = Follower("GbSource", 40);
        FavorSystem.Prayer(c, gods).Should().BeGreaterThan(0);
        ImmortalDeedSystem.Bless(c, God, otherSession: false, gods).FavorGained.Should().Be(2);
        FavorSystem.GainedToday(c, FavorSource.Prayer).Should().Be(GameConfig.GodFavorPrayerGain);
    }

    [Fact]
    public void Bless_OfAPlayerWhoFollowsAnotherGod_IsRefused_AndChangesNothing()
    {
        var (c, gods) = Follower("GbOther", 40, "Zephyrine");
        var o = ImmortalDeedSystem.Bless(c, God, otherSession: false, gods);
        o.Refused.Should().BeTrue();
        c.GodFavor.Should().Be(40);
        c.DivineBlessingCombats.Should().Be(0);
    }

    // ---------------- Online: another session's character ----------------

    [Fact]
    public void Online_ATierCrossedByBless_IsLeftToTheFollowersOwnSession()
    {
        var (c, gods) = Follower("GbLive", GameConfig.GodFavorTierDevoutMin - 1);
        c.GodBoonRecalcPending = false;
        ImmortalDeedSystem.Bless(c, God, otherSession: true, gods).FavorGained.Should().Be(2);
        c.GodBoonRecalcPending.Should().BeTrue("the live session applies the tier's stats at its next safe point");

        var (own, ownGods) = Follower("GbOwn", GameConfig.GodFavorTierDevoutMin - 1);
        own.GodBoonRecalcPending = false;
        ImmortalDeedSystem.Bless(own, God, otherSession: false, ownGods);
        own.GodBoonRecalcPending.Should().BeFalse();
    }

    [Fact]
    public void TheOldOfflineBlessingWrite_IsGone()
    {
        Source("Scripts/Systems/IOnlineSaveBackend.cs").Should().NotContain("ApplyDivineBlessing");
        Source("Scripts/Systems/SqlSaveBackend.cs").Should().NotContain("ApplyDivineBlessing");
    }

    [Fact]
    public void Online_TheBlessDeed_GoesToTheLiveCharacterBeforeTheSave()
    {
        string body = Body("Scripts/Locations/PantheonLocation.cs", "private async Task<BlessOutcome> ApplyBlessToPlayer(");
        int live = body.IndexOf("ImmortalDeedSystem.Bless(live.Player, godName, otherSession: true)", StringComparison.Ordinal);
        int saved = body.IndexOf("backend.UpdateFollowerSaveOffline", StringComparison.Ordinal);
        live.Should().BeGreaterThan(0);
        saved.Should().BeGreaterThan(live, "a live follower is never written in SQL, where its session's save would overwrite it");
        body.Should().Contain("return outcome;");
        body.Should().Contain("Loc.GetIn(lang, \"favor.gain\"");
        body.Should().NotContain("ApplyDivineBlessing");
    }

    // ---------------- Offline: the save in SQL ----------------

    private static PlayerData Saved(string name, string god, int favor, string favorGod, int schema = GameConfig.GodFavorSchemaCurrent) =>
        new PlayerData { Name1 = name, Name2 = name, Level = 7, Gold = 1234, WorshippedGod = god, GodFavor = favor, GodFavorGod = favorGod, GodFavorSchema = schema, Language = "fr" };

    private Task Save(string key, PlayerData p, Dictionary<string, string>? canon = null) =>
        Db.WriteGameData(key, new SaveGameData
        {
            Version = GameConfig.SaveVersion,
            Player = p,
            StorySystems = new StorySystemsData { PlayerGods = canon ?? new Dictionary<string, string>() }
        });

    private async Task<(BlessOutcome Outcome, string Lang)?> BlessOffline(string key) =>
        (await Db.UpdateFollowerSaveOffline<(BlessOutcome Outcome, string Lang)?>(key, (p, gods) =>
        {
            var o = ImmortalDeedSystem.BlessSaved(p, gods, God);
            return (!o.Refused, (o, p.Language));
        })).Result;

    private object? Scalar(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    [Fact]
    public async Task Offline_Bless_WritesFavorTheCapAndTheTierBonus_ToTheSave()
    {
        await Save("acct_b", Saved("Brin", God, GameConfig.GodFavorTierDevoutMin - 2, God));
        var r = await BlessOffline("acct_b");
        r.Should().NotBeNull();
        r!.Value.Outcome.FavorGained.Should().Be(2);
        r.Value.Lang.Should().Be("fr");

        var back = (await Db.ReadGameData("acct_b"))!.Player;
        back.GodFavor.Should().Be(GameConfig.GodFavorTierDevoutMin);
        back.GodFavorDayGains[nameof(FavorSource.ImmortalBlessing)].Should().Be(2);
        back.DivineBlessingCombats.Should().Be(GameConfig.GodBlessCombatDuration);
        back.DivineBlessingBonus.Should().BeApproximately(GameConfig.GodBlessBonusDevout, 0.0001f);
        back.Gold.Should().Be(1234, "only the Favor and blessing fields are written");
        back.Level.Should().Be(7);

        var again = await BlessOffline("acct_b");
        again!.Value.Outcome.FavorGained.Should().Be(0, "the day's cap is kept in the save");
        (await Db.ReadGameData("acct_b"))!.Player.GodFavor.Should().Be(GameConfig.GodFavorTierDevoutMin);
    }

    [Fact]
    public async Task Offline_Bless_OfASaveFromBeforeFavor_IsKeptByItsFirstLoad()
    {
        await Save("acct_l", Saved("Lorn", God, 0, "", schema: 0));
        (await BlessOffline("acct_l"))!.Value.Outcome.FavorGained.Should().Be(2);
        var back = (await Db.ReadGameData("acct_l"))!.Player;
        back.GodFavorSchema.Should().Be(GameConfig.GodFavorSchemaCurrent, "the load would otherwise reset it");
        back.GodFavor.Should().Be(GameConfig.GodFavorLegacyStart + 2);
        back.GodFavorGod.Should().Be(God);
    }

    [Fact]
    public async Task Offline_Bless_OfASaveThatFollowsAnotherGod_WritesNothing()
    {
        await Save("acct_c", Saved("Cade", God, 30, God), new Dictionary<string, string> { ["Cade"] = "Solarius" });
        string before = (string)Scalar("SELECT player_data FROM players WHERE username = 'acct_c';")!;
        (await BlessOffline("acct_c"))!.Value.Outcome.Refused.Should().BeTrue("the canon god wins, as on load");
        ((string)Scalar("SELECT player_data FROM players WHERE username = 'acct_c';")!).Should().Be(before);
        (await Db.GetSavedWorshippedGod("acct_c")).Should().Be("Solarius");
    }

    [Fact]
    public async Task Offline_NoSave_GivesNoResult()
    {
        (await BlessOffline("nobody_here")).Should().BeNull();
        (await Db.GetSavedWorshippedGod("nobody_here")).Should().Be("");
    }

    // ---------------- Smite ----------------

    [Fact]
    public void Smite_IsRefusedOnTheGodsOwnFollower_AndAllowedOnOthers()
    {
        ImmortalDeedSystem.CanSmite(God, God).Should().BeFalse();
        ImmortalDeedSystem.CanSmite(God, "korvessa ").Should().BeFalse("any letter case");
        ImmortalDeedSystem.CanSmite(God, "Zephyrine").Should().BeTrue();
        ImmortalDeedSystem.CanSmite(God, "Solarius").Should().BeTrue();
        ImmortalDeedSystem.CanSmite(God, "").Should().BeTrue("a pagan");
        ImmortalDeedSystem.CanSmite(God, null).Should().BeTrue();
    }

    [Fact]
    public void Smite_ChecksTheTargetsGodNow_BeforeTheDeedIsSpent()
    {
        string body = Body("Scripts/Locations/PantheonLocation.cs", "private async Task DeedSmiteMortal(");
        int check = body.IndexOf("if (!ImmortalDeedSystem.CanSmite(currentPlayer.DivineName, await CurrentGodOfAsync(target)))", StringComparison.Ordinal);
        int spend = body.IndexOf("currentPlayer.DeedsLeft--;", StringComparison.Ordinal);
        check.Should().BeGreaterThan(0);
        spend.Should().BeGreaterThan(check);
        body.Substring(check, spend - check).Should().Contain("pantheon.smite_own_follower").And.Contain("return;");
        GodMiracles1115Tests.Count(body, "ImmortalDeedSystem.CanSmite(currentPlayer.DivineName, ").Should().Be(3, "the NPC list, the player list and the check");
    }

    [Fact]
    public void Smite_OfOthers_IsUnchanged()
    {
        string body = Body("Scripts/Locations/PantheonLocation.cs", "private async Task DeedSmiteMortal(");
        body.Should().Contain("float smitePercent = GameConfig.GodSmiteMinPercent + (float)(rng.NextDouble() * (GameConfig.GodSmiteMaxPercent - GameConfig.GodSmiteMinPercent));");
        body.Should().Contain("int expGain = GameConfig.GodSmiteExp;");
        body.Should().Contain("target.NpcRef.HP = Math.Max(1, target.NpcRef.HP - damage);");
        body.Should().Contain("GameConfig.GodSmitePlayerCooldownMinutes");
        GameConfig.GodSmiteMinPercent.Should().Be(0.10f);
        GameConfig.GodSmiteMaxPercent.Should().Be(0.25f);
        GameConfig.GodSmiteExp.Should().Be(20);
    }

    // ---------------- Bless deed wiring ----------------

    [Fact]
    public void BlessDeed_SpendsTheDeedOnlyWhenNotRefused_AndShowsTheFavorLine()
    {
        string body = Body("Scripts/Locations/PantheonLocation.cs", "private async Task DeedBlessFollower(");
        int refused = body.IndexOf("if (outcome.Refused)", StringComparison.Ordinal);
        int spend = body.IndexOf("currentPlayer.DeedsLeft--;", StringComparison.Ordinal);
        refused.Should().BeGreaterThan(0);
        spend.Should().BeGreaterThan(refused);
        GodMiracles1115Tests.Count(body, "currentPlayer.DeedsLeft--;").Should().Be(1, "a bless costs one deed");
        body.Should().Contain("pantheon.bless_favor").And.Contain("pantheon.bless_favor_capped");
        body.Should().Contain("Loc.Get(\"pantheon.bless_effect\", (int)Math.Round(outcome.Bonus * 100), outcome.Combats)");
    }

    // ---------------- Loc ----------------

    public static readonly string[] Keys =
    {
        "pantheon.bless_title", "pantheon.bless_prompt", "pantheon.not_your_follower", "pantheon.bless_favor",
        "pantheon.bless_favor_capped", "pantheon.bless_news", "pantheon.bless_received", "pantheon.smite_title",
        "pantheon.smite_prompt", "pantheon.smite_own_follower", "pantheon.smite_news", "pantheon.smite_received",
        "pantheon.smite_received_offline",
    };

    internal static void KeysInAllLanguages(IEnumerable<string> keys)
    {
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            var root = JsonDocument.Parse(Source($"Localization/{lang}.json")).RootElement;
            foreach (var key in keys)
            {
                root.TryGetProperty(key, out var v).Should().BeTrue($"{key} must be in {lang}.json");
                v.GetString().Should().NotBeNullOrWhiteSpace($"{key} in {lang}.json");
                v.GetString().Should().NotContain("\u2014").And.NotContain("\u2013");
            }
        }
    }

    [Fact]
    public void Loc_TheBlessAndSmiteKeys_AreInAllFiveLanguages()
    {
        KeysInAllLanguages(Keys);
    }

    [Fact]
    public void Loc_NoEnglishLeftInTheBlessAndSmiteMessages()
    {
        string src = Source("Scripts/Locations/PantheonLocation.cs");
        src.Should().NotContain("\"BLESS FOLLOWER\"").And.NotContain("\"SMITE MORTAL\"");
        src.Should().NotContain("has blessed you!").And.NotContain("struck you with divine lightning");
        src.Should().NotContain("blessed {target.Name}!").And.NotContain("with divine lightning!\");");
    }
}
