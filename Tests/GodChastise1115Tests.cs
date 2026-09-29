using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 5b: Chastise. A player-god takes GodChastiseFavorLoss Favor from one of its
/// own player followers, once a day each (kept on the god, in the god's save, cleared with the deeds),
/// for one deed and no divine XP. A follower live in another session is changed in memory with the
/// stat update left to that session; one not online is changed on the save in SQL.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodChastise1115Tests : IDisposable
{
    private const string God = "Korvessa";
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-chastise-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private static (Character c, GodSystem gods) Follower(string name, int favor, string god = God) =>
        GodMiracles1115Tests.Worshipper(name, god, favor);

    private static Character Immortal(string name) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, IsImmortal = true, DivineName = God, GodLevel = 1
    };

    private static string Body(string file, string signature) => GodMiracles1115Tests.Body(file, signature);

    // ---------------- -5 Favor ----------------

    [Fact]
    public void Chastise_TakesFiveFavor()
    {
        var (c, gods) = Follower("GcFive", 40);
        var o = ImmortalDeedSystem.Chastise(c, God, otherSession: false, gods);
        o.Refused.Should().BeFalse();
        o.FavorLost.Should().Be(GameConfig.GodChastiseFavorLoss).And.Be(5);
        o.FavorNow.Should().Be(35);
        c.GodFavor.Should().Be(35);
    }

    [Fact]
    public void Chastise_NeverTakesFavorBelowZero()
    {
        var (c, gods) = Follower("GcZero", 3);
        ImmortalDeedSystem.Chastise(c, God, otherSession: false, gods).FavorLost.Should().Be(3);
        c.GodFavor.Should().Be(0);
    }

    [Fact]
    public void Chastise_CrossingATier_GoesThroughFavorSystem_AndIsLeftToALiveSession()
    {
        var (c, gods) = Follower("GcTier", GameConfig.GodFavorTierDevoutMin + 1);
        c.GodBoonRecalcPending = false;
        ImmortalDeedSystem.Chastise(c, God, otherSession: true, gods);
        FavorSystem.GetTier(c.GodFavor).Should().Be(GodFavorTier.Follower);
        c.GodBoonRecalcPending.Should().BeTrue("the follower's own session applies the tier's stats");
    }

    [Fact]
    public void Chastise_OfAnNpcOrAnotherGodsFollower_IsRefused()
    {
        var npc = new Character { Name1 = "GcNpc", Name2 = "GcNpc", AI = CharacterAI.Computer, WorshippedGod = God };
        ImmortalDeedSystem.Chastise(npc, God, otherSession: false, new GodSystem()).Refused.Should().BeTrue();
        var (other, gods) = Follower("GcOther", 40, "Zephyrine");
        ImmortalDeedSystem.Chastise(other, God, otherSession: false, gods).Refused.Should().BeTrue();
        other.GodFavor.Should().Be(40);
    }

    // ---------------- Once a day per follower ----------------

    [Fact]
    public void Chastise_OnceADayPerFollower_KeptOnTheGod()
    {
        var god = Immortal("GcGod");
        ImmortalDeedSystem.CanChastise(god, "acct_a").Should().BeTrue();
        ImmortalDeedSystem.MarkChastised(god, "ACCT_A ");
        ImmortalDeedSystem.CanChastise(god, "acct_a").Should().BeFalse("once a day, any letter case");
        ImmortalDeedSystem.CanChastise(god, "acct_b").Should().BeTrue("each follower has their own day");
        ImmortalDeedSystem.MarkChastised(god, "acct_a");
        god.ChastisedToday.Should().Equal("acct_a");
        ImmortalDeedSystem.ClearChastised(god);
        ImmortalDeedSystem.CanChastise(god, "acct_a").Should().BeTrue();
        ImmortalDeedSystem.CanChastise(god, "").Should().BeFalse();
    }

    [Fact]
    public void Chastise_TheDaysList_SurvivesSaveAndLoad()
    {
        var god = Immortal("GcSave");
        ImmortalDeedSystem.MarkChastised(god, "acct_a");
        var method = typeof(SaveSystem).GetMethod("SerializePlayer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var data = (PlayerData)method.Invoke(SaveSystem.Instance, new object[] { god })!;
        data.ChastisedToday.Should().Equal("acct_a");
        var back = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(data))!;
        var restored = MenuKeysNeedEnterPref1115Tests.Restore(back);
        ImmortalDeedSystem.CanChastise(restored, "acct_a").Should().BeFalse("a reload does not give the chastise back");
        ImmortalDeedSystem.CanChastise(restored, "acct_b").Should().BeTrue();
    }

    [Fact]
    public void OldSaves_ReadNoneChastised_AndTheEditorShowsIt()
    {
        var data = JsonSerializer.Deserialize<PlayerData>("{}")!;
        data.ChastisedToday.Should().BeEmpty();
        MenuKeysNeedEnterPref1115Tests.Restore(data).ChastisedToday.Should().BeEmpty();
        GodMiracles1115Tests.Source("Scripts/Editor/PlayerSaveEditor.cs").Should().Contain("p.ChastisedToday = new List<string>();");
    }

    [Fact]
    public void TheGodsDailyReset_ClearsTheList_WithTheDeeds()
    {
        var god = Immortal("GcDaily");
        god.DeedsLeft = 0;
        ImmortalDeedSystem.MarkChastised(god, "acct_a");
        typeof(DailySystemManager).GetMethod("ProcessGodDailyMaintenance", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(DailySystemManager.Instance, new object?[] { god, null });
        god.DeedsLeft.Should().Be(GameConfig.GodDeedsPerDay[0]);
        ImmortalDeedSystem.CanChastise(god, "acct_a").Should().BeTrue();
    }

    // ---------------- The deed ----------------

    [Fact]
    public void ChastiseDeed_CostsOneDeed_NoXp_OnlyWhenNotRefused_AndMarksTheFollower()
    {
        string body = Body("Scripts/Locations/PantheonLocation.cs", "private async Task DeedChastiseFollower(");
        int refused = body.IndexOf("if (outcome.Refused)", StringComparison.Ordinal);
        int spend = body.IndexOf("currentPlayer.DeedsLeft--;", StringComparison.Ordinal);
        int mark = body.IndexOf("ImmortalDeedSystem.MarkChastised(currentPlayer, target.Username);", StringComparison.Ordinal);
        refused.Should().BeGreaterThan(0);
        spend.Should().BeGreaterThan(refused);
        mark.Should().BeGreaterThan(refused);
        GodMiracles1115Tests.Count(body, "currentPlayer.DeedsLeft--;").Should().Be(1, "a chastise costs one deed");
        body.Should().NotContain("GodExperience", "a chastise gives no divine XP");
        body.Should().Contain("b.IsPlayer && ImmortalDeedSystem.CanChastise(currentPlayer, b.Username)");
        body.Should().Contain("pantheon.chastise_favor");

        string menu = Body("Scripts/Locations/PantheonLocation.cs", "private async Task PerformDivineDeeds(");
        menu.Should().Contain("case \"7\": await DeedChastiseFollower(); break;");
        menu.Should().Contain("WriteMenuOption(\"7\", Loc.Get(\"pantheon.menu_chastise\")");
    }

    [Fact]
    public void Online_TheChastise_GoesToTheLiveCharacterBeforeTheSave()
    {
        string body = Body("Scripts/Locations/PantheonLocation.cs", "private async Task<ChastiseOutcome> ApplyChastiseToPlayer(");
        int live = body.IndexOf("ImmortalDeedSystem.Chastise(live.Player, godName, otherSession: true)", StringComparison.Ordinal);
        int saved = body.IndexOf("backend.UpdateFollowerSaveOffline", StringComparison.Ordinal);
        live.Should().BeGreaterThan(0);
        saved.Should().BeGreaterThan(live);
        body.Should().Contain("Loc.GetIn(lang, \"favor.loss\"");
        body.Should().Contain("Loc.GetIn(s.Lang, \"favor.loss\"");
    }

    // ---------------- Offline ----------------

    private Task Save(string key, PlayerData p, Dictionary<string, string>? canon = null) =>
        Db.WriteGameData(key, new SaveGameData
        {
            Version = GameConfig.SaveVersion,
            Player = p,
            StorySystems = new StorySystemsData { PlayerGods = canon ?? new Dictionary<string, string>() }
        });

    private Task<ChastiseOutcome?> ChastiseOffline(string key) =>
        Db.UpdateFollowerSaveOffline<ChastiseOutcome?>(key, (p, gods) =>
        {
            var o = ImmortalDeedSystem.ChastiseSaved(p, gods, God);
            return (!o.Refused, o);
        });

    [Fact]
    public async Task Offline_Chastise_TakesFiveFavorOnTheSave()
    {
        await Save("acct_d", new PlayerData { Name1 = "Dara", Name2 = "Dara", Level = 4, Gold = 99, WorshippedGod = God, GodFavor = 30, GodFavorGod = God, GodFavorSchema = GameConfig.GodFavorSchemaCurrent });
        var o = await ChastiseOffline("acct_d");
        o!.Value.FavorLost.Should().Be(5);
        var back = (await Db.ReadGameData("acct_d"))!.Player;
        back.GodFavor.Should().Be(25);
        back.Gold.Should().Be(99);
        back.DivineBlessingCombats.Should().Be(0, "a chastise is not a blessing");
    }

    [Fact]
    public async Task Offline_Chastise_OfAnotherGodsFollower_WritesNothing()
    {
        await Save("acct_e", new PlayerData { Name1 = "Eben", Name2 = "Eben", WorshippedGod = "Zephyrine", GodFavor = 30, GodFavorGod = "Zephyrine", GodFavorSchema = GameConfig.GodFavorSchemaCurrent });
        (await ChastiseOffline("acct_e"))!.Value.Refused.Should().BeTrue();
        (await Db.ReadGameData("acct_e"))!.Player.GodFavor.Should().Be(30);
    }

    // ---------------- Loc ----------------

    [Fact]
    public void Loc_TheChastiseKeys_AreInAllFiveLanguages()
    {
        GodBlessChastise1115Tests.KeysInAllLanguages(new[]
        {
            "pantheon.menu_chastise", "pantheon.menu_chastise_desc", "pantheon.no_followers_to_chastise",
            "pantheon.chastise_title", "pantheon.chastise_prompt", "pantheon.chastise_success",
            "pantheon.chastise_favor", "pantheon.chastise_received",
        });
    }
}
