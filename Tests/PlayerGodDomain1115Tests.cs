using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 2, player-god domain: the immortal picks one of the ten boon domains
/// (saved, five sites, invalid or missing reads as not chosen), and the god's followers get that
/// domain's boon and Mental ward. The boon is scaled by the god's standing against the strongest
/// canon god (floor to cap), and an immortal away too long decays toward the floor.
/// </summary>
[Collection("SharedGameSingletons")]
public class PlayerGodDomain1115Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-domain-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private static Character Hero(string name) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 10, HP = 100, MaxHP = 100, Mental = 80
    };

    /// <summary>A follower of a player-god with its boon cached as the Temple or the login would.</summary>
    private static (Character c, GodSystem gods) Follower(string name, string god, int favor, GodDomain domain, int scale)
    {
        var gods = new GodSystem();
        var c = Hero(name);
        GodRegistry.SetWorshippedGod(c, god, gods).Should().BeTrue();
        c.GodFavor = favor;
        GodBoonSystem.SetPlayerGodBoon(c, god, domain, scale);
        return (c, gods);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(RepoRoot(), file));

    // ---------------- Scale ----------------

    [Theory]
    [InlineData(100, 100, 0, 100)]
    [InlineData(60, 100, 0, 60)]
    [InlineData(10, 100, 0, 50)]     // below the floor
    [InlineData(500, 100, 0, 120)]   // above the cap
    [InlineData(0, 0, 0, 50)]        // nobody anywhere: the floor
    [InlineData(5, 0, 0, 120)]       // followers and no canon standing: the cap
    public void Scale_IsStandingOverTheStrongestCanonGod_ClampedToFloorAndCap(long standing, long canon, int days, int expected) =>
        GodBoonSystem.PlayerGodScalePct(standing, canon, days).Should().Be(expected);

    [Fact]
    public void Scale_AnInactiveImmortal_DecaysTowardTheFloor_AfterTheGraceDays()
    {
        int grace = GameConfig.GodPlayerInactiveDays;
        GodBoonSystem.PlayerGodScalePct(120, 100, grace).Should().Be(120, "still within the grace days");
        GodBoonSystem.PlayerGodScalePct(120, 100, grace + 1).Should().Be(120 - GameConfig.GodPlayerInactiveDecayPctPerDay);
        GodBoonSystem.PlayerGodScalePct(120, 100, grace + 100).Should().Be(GameConfig.GodPlayerBoonFloorPct);
    }

    [Fact]
    public void DaysInactive_ZeroWhileOnline_ElseWholeDaysSinceTheLastLogin()
    {
        var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        GodBoonSystem.DaysInactive(true, now.AddDays(-30), now).Should().Be(0);
        GodBoonSystem.DaysInactive(false, now.AddDays(-9.5), now).Should().Be(9);
        GodBoonSystem.DaysInactive(false, null, now).Should().Be(0, "no record: not counted as away");
    }

    // ---------------- A follower's boon ----------------

    [Fact]
    public void Follower_GetsThePlayerGodsDomainBoon_AtTierTimesScale()
    {
        var (c, gods) = Follower("PgdNat", "Zephyrine", 60, GodDomain.Nature, 120);
        GodBoonSystem.GetDomain(c, gods).Should().Be(GodDomain.Nature);
        GodBoonSystem.GetStrengthPct(c, gods).Should().Be(120);
        GodBoonSystem.WildernessGain(c, 100, gods).Should().Be(220, "a rising player-god out-blesses a canon god: +120% against Sylvana's +100%");
        var (f, fGods) = Follower("PgdNatF", "Zephyrine", 0, GodDomain.Nature, 60);
        GodBoonSystem.GetStrengthPct(f, fGods).Should().Be(19, "Follower 33% of a 60% god");
    }

    [Fact]
    public void Follower_TheCacheOfAnotherGod_GivesNothing()
    {
        var (c, gods) = Follower("PgdStale", "Zephyrine", 60, GodDomain.Nature, 120);
        GodBoonSystem.SetPlayerGodBoon(c, "Korvath", GodDomain.Nature, 120);
        GodBoonSystem.GetDomain(c, gods).Should().Be(GodDomain.None);
        GodBoonSystem.GetStrengthPct(c, gods).Should().Be(0);
    }

    [Fact]
    public void CanonGods_KeepFixedBoons_WhateverTheCacheSays()
    {
        var gods = new GodSystem();
        var c = Hero("PgdCanon");
        GodRegistry.SetWorshippedGod(c, "Sylvana", gods);
        c.GodFavor = 60;
        GodBoonSystem.SetPlayerGodBoon(c, "Sylvana", GodDomain.Chaos, 50);
        GodBoonSystem.GetDomain(c, gods).Should().Be(GodDomain.Nature);
        GodBoonSystem.GetStrengthPct(c, gods).Should().Be(100);
    }

    [Fact]
    public void Follower_GetsTheDomainsMentalWard_AtDevout()
    {
        var c = Hero("PgdWard");
        GodRegistry.SetWorshippedGod(c, "Zephyrine").Should().BeTrue();
        try
        {
            c.GodFavor = 30;
            GodBoonSystem.SetPlayerGodBoon(c, "Zephyrine", GodDomain.Love, 50);
            c.Mental = 100;
            MentalSystem.ApplyCompanionGrief(c).Should().Be(-(GameConfig.MentalCompanionGriefLoss - GameConfig.MentalCompanionGriefLoss / 2));
        }
        finally { GodRegistry.SetWorshippedGod(c, null); }
    }

    // ---------------- The immortal's domain, saved ----------------

    [Theory]
    [InlineData("Nature", GodDomain.Nature)]
    [InlineData("chaos", GodDomain.Chaos)]
    [InlineData("", GodDomain.None)]
    [InlineData(null, GodDomain.None)]
    [InlineData("Sunshine", GodDomain.None)]
    [InlineData("None", GodDomain.None)]
    [InlineData("3", GodDomain.None)]
    public void ParseDomain_OnlyTheTenNames(string? saved, GodDomain expected) =>
        GodBoonSystem.ParseDomain(saved).Should().Be(expected);

    private static PlayerData Serialize(Character c)
    {
        var method = typeof(SaveSystem).GetMethod("SerializePlayer", F)!;
        return (PlayerData)method.Invoke(SaveSystem.Instance, new object[] { c })!;
    }

    [Fact]
    public void DivineDomain_RoundTripsThroughSaveAndRestore()
    {
        var c = Hero("PgdSave");
        c.IsImmortal = true; c.DivineName = "Zephyrine"; c.DivineDomain = "Magic";
        var data = Serialize(c);
        data.DivineDomain.Should().Be("Magic");
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        json.Should().Contain("\"divineDomain\":\"Magic\"", "the SQL immortal query reads $.player.divineDomain");
        MenuKeysNeedEnterPref1115Tests.Restore(data).DivineDomain.Should().Be("Magic");
    }

    [Fact]
    public void DivineDomain_AnOldOrBadSave_ReadsAsNotChosen()
    {
        var c = Hero("PgdOld");
        c.IsImmortal = true; c.DivineName = "Zephyrine";
        var data = Serialize(c);
        data.DivineDomain = null!;
        MenuKeysNeedEnterPref1115Tests.Restore(data).DivineDomain.Should().Be("");
        data.DivineDomain = "Sunshine";
        MenuKeysNeedEnterPref1115Tests.Restore(data).DivineDomain.Should().Be("");
    }

    [Fact]
    public void Editor_HasTheDomainField()
    {
        Source("Scripts/Editor/PlayerSaveEditor.cs").Should().Contain("p.DivineDomain =");
    }

    // ---------------- Online: the immortal row and the scale from saved followers ----------------

    private Task Save(string key, PlayerData p, Dictionary<string, string>? canon = null) =>
        Db.WriteGameData(key, new SaveGameData
        {
            Version = GameConfig.SaveVersion,
            Player = p,
            StorySystems = new StorySystemsData { PlayerGods = canon ?? new Dictionary<string, string>() }
        });

    private static PlayerData Mortal(string name, string playerGod, int favor) =>
        new PlayerData { Name1 = name, Name2 = name, Level = 5, WorshippedGod = playerGod, GodFavor = favor, GodFavorGod = playerGod, GodFavorSchema = GameConfig.GodFavorSchemaCurrent };

    private void SetLastLogin(string user, DateTime utc)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE players SET last_login = @t WHERE LOWER(username) = LOWER(@u);";
        cmd.Parameters.AddWithValue("@t", utc.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@u", user);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task Online_TheImmortalRow_CarriesTheDomainAndLastLogin()
    {
        await Save("zeph", new PlayerData { Name1 = "Zeph", Name2 = "Zeph", Level = 90, IsImmortal = true, DivineName = "Zephyrine", DivineDomain = "Chaos" });
        var info = (await Db.GetImmortalPlayers()).Single(i => i.DivineName == "Zephyrine");
        info.DivineDomain.Should().Be("Chaos");
        info.LastLogin.Should().NotBeNull();
        (DateTime.UtcNow - info.LastLogin!.Value).TotalMinutes.Should().BeLessThan(10, "read as UTC");
    }

    [Fact]
    public async Task Online_PlayerGodBoon_FromStandingAgainstTheStrongestCanonGod()
    {
        await Save("zeph", new PlayerData { Name1 = "Zeph", Name2 = "Zeph", Level = 90, IsImmortal = true, DivineName = "Zephyrine", DivineDomain = "Chaos" });
        await Save("a1", Mortal("A1", "Zephyrine", 30));
        await Save("a2", Mortal("A2", "Zephyrine", 30));
        var b1 = Mortal("B1", "", 80);
        b1.GodFavorGod = "Amara";
        await Save("b1", b1, new Dictionary<string, string> { ["B1"] = "Amara" });
        var now = DateTime.UtcNow;
        var (domain, scale) = await GodBoonSystem.PlayerGodBoonAsync("Zephyrine", Db, now);
        domain.Should().Be(GodDomain.Chaos);
        scale.Should().Be(75, "standing 60 against Amara's 80");
    }

    [Fact]
    public async Task Online_AnImmortalAwayTooLong_Decays()
    {
        await Save("zeph", new PlayerData { Name1 = "Zeph", Name2 = "Zeph", Level = 90, IsImmortal = true, DivineName = "Zephyrine", DivineDomain = "Chaos" });
        await Save("a1", Mortal("A1", "Zephyrine", 100));
        var now = DateTime.UtcNow;
        (await GodBoonSystem.PlayerGodBoonAsync("Zephyrine", Db, now)).ScalePct.Should().Be(GameConfig.GodPlayerBoonCapPct);
        SetLastLogin("zeph", now.AddDays(-(GameConfig.GodPlayerInactiveDays + 3)));
        (await GodBoonSystem.PlayerGodBoonAsync("Zephyrine", Db, now)).ScalePct
            .Should().Be(GameConfig.GodPlayerBoonCapPct - 3 * GameConfig.GodPlayerInactiveDecayPctPerDay);
    }

    [Fact]
    public async Task Online_AnUnknownGod_GivesNoBoon()
    {
        (await GodBoonSystem.PlayerGodBoonAsync("Nobody", Db, DateTime.UtcNow)).Should().Be((GodDomain.None, 0));
    }

    // ---------------- The picker ----------------

    private static (TerminalEmulator term, MemoryStream output) Stream(params string[] lines)
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new LineStream(lines), output), output);
    }

    [Fact]
    public async Task Picker_ChoosesADomain_AfterConfirmation()
    {
        var c = Hero("PgdPick"); c.IsImmortal = true; c.DivineName = "Zephyrine";
        var (term, _) = Stream("12", "3", "N", "9", "Y");
        (await GodDomainPicker.PickAsync(c, term)).Should().BeTrue();
        c.DivineDomain.Should().Be("Nature", "12 is out of range, 3 was declined, 9 confirmed");
    }

    [Fact]
    public async Task Picker_EnterLeavesItForLater_AndAChosenDomainIsNeverAskedAgain()
    {
        var c = Hero("PgdLater"); c.IsImmortal = true; c.DivineName = "Zephyrine";
        var (term, _) = Stream("");
        (await GodDomainPicker.PickAsync(c, term)).Should().BeFalse();
        c.DivineDomain.Should().Be("");
        c.DivineDomain = "War";
        var (term2, out2) = Stream("5", "Y");
        (await GodDomainPicker.PickAsync(c, term2)).Should().BeTrue();
        c.DivineDomain.Should().Be("War", "one time only");
        out2.Length.Should().Be(0, "nothing is shown");
    }

    [Fact]
    public void Wiring_AscensionAndThePantheon_AskForTheDomain_TheLoginAndTempleRefreshTheBoon()
    {
        Source("Scripts/Systems/EndingsSystem.cs").Should().Contain("await GodDomainPicker.PickAsync(player, terminal);");
        Source("Scripts/Locations/PantheonLocation.cs").Should().Contain("await GodDomainPicker.PickAsync(player, term);");
        Source("Scripts/Core/GameEngine.cs").Should().Contain("await GodBoonSystem.RefreshPlayerGodBoonAsync(currentPlayer);");
        var temple = Source("Scripts/Locations/TempleLocation.cs");
        temple.Should().Contain("await GodBoonSystem.RefreshPlayerGodBoonAsync(currentPlayer);");
    }

    [Fact]
    public void Display_ThePlayerGodListAndThePantheonStatus_ShowTheDomainAndScale()
    {
        var temple = Source("Scripts/Locations/TempleLocation.cs");
        temple.Should().Contain("Loc.Get(\"god.player_domain_line\", GodBoonSystem.DomainName(god.Domain), god.BoonScalePct)");
        temple.Should().Contain("GodBoonSystem.DescribeBoon(god.Domain, god.BoonScalePct)");
        Source("Scripts/Locations/PantheonLocation.cs").Should().Contain("god.player_domain_line");
    }

    [Fact]
    public void Loc_DomainAndPickerKeys_InAllFiveLanguages()
    {
        var keys = GodBoonSystem.AllDomains.Select(d => "god.domain." + d.ToString().ToLowerInvariant())
            .Concat(new[] { "god.domain_pick_title", "god.domain_pick_intro", "god.domain_pick_entry", "god.domain_pick_prompt",
                "god.domain_pick_invalid", "god.domain_pick_confirm", "god.domain_pick_done", "god.domain_pick_later",
                "god.player_domain_line", "god.player_domain_none" }).ToList();
        foreach (var lang in new[] { "en", "es", "fr", "it", "hu" })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json")));
            foreach (var k in keys)
                doc.RootElement.TryGetProperty(k, out _).Should().BeTrue($"{lang} has {k}");
        }
    }
}
