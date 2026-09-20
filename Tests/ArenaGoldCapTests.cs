using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperRemake.Locations;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.7: one PvP fight moves at most the per-fight cap for the recipient's level (steal and
/// salvage together), on both the win and the loss branch; the alt cap stays layered under it; and
/// the winner is credited only with what was actually taken from the loser's saved gold.
/// </summary>
[Collection("SharedGameSingletons")]
public class ArenaGoldCapTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-arena-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;
    public ArenaGoldCapTests() { _db = new SqlSaveBackend(_path); }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { File.Delete(_path); } catch { } }

    [Fact]
    public void TheIncident_IsCappedToTheLowHundredsOfThousands()
    {
        long tenPercent = (long)(29_800_000 * GameConfig.PvPGoldStealPercent);
        ArenaLocation.CapPvPGold(tenPercent, 39, false, 0, "attacker").Should().Be(195_000);
        ArenaLocation.CapPvPGold(3_318_269, 39, false, 0, "attacker").Should().Be(195_000);
        ArenaLocation.CapPvPGold(900_000, 100, false, 0, "attacker").Should().Be(500_000);
        ArenaLocation.CapPvPGold(40_000, 39, false, 0, "attacker").Should().Be(40_000, "an ordinary steal is untouched");
        ArenaLocation.CapPvPGold(-5, 39, false, 0, "attacker").Should().Be(0);
    }

    [Fact]
    public void Salvage_CountsAgainstTheSameCap_AndTheAltCapStaysLayeredUnderIt()
    {
        ArenaLocation.CapPvPGold(3_000_000, 39, false, 150_000, "attacker").Should().Be(45_000, "195,000 less the salvage already paid");
        ArenaLocation.CapPvPGold(3_000_000, 39, false, 400_000, "attacker").Should().Be(0);
        long altCap = GameConfig.PvPAltGoldStealBase + GameConfig.PvPAltGoldStealPerLevel * 39;
        ArenaLocation.CapPvPGold(3_000_000, 39, true, 0, "main__alt").Should().Be(altCap);
        ArenaLocation.IsAltAccount("main__alt").Should().BeTrue();
        ArenaLocation.IsAltAccount("main28").Should().BeFalse("a numbered second account is not an alt by name; the universal cap is what covers it");
    }

    [Fact]
    public void TheLossBranch_UsesTheDefendersLevel()
    {
        // a rich level-90 main throws a fight to a level-5 defender: the defender's level sets the cap
        ArenaLocation.CapPvPGold(7_500_000, 5, false, 0, "defender").Should().Be(25_000);
        ArenaLocation.CapPvPGold(7_500_000, 5, true, 0, "defender__alt").Should().Be(GameConfig.PvPAltGoldStealBase + GameConfig.PvPAltGoldStealPerLevel * 5);
    }

    private void Seed(string username, long gold)
    {
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO players (username, display_name, password_hash, player_data) VALUES (@u, @u, 'x', @d);";
        cmd.Parameters.AddWithValue("@u", username);
        cmd.Parameters.AddWithValue("@d", $"{{\"player\":{{\"gold\":{gold},\"level\":25}}}}");
        cmd.ExecuteNonQuery();
    }

    private long Gold(string username)
    {
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT CAST(json_extract(player_data,'$.player.gold') AS INTEGER) FROM players WHERE username=@u;";
        cmd.Parameters.AddWithValue("@u", username);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public async Task TheCredit_IsWhatWasActuallyTaken_NeverMore()
    {
        Seed("rich", 29_800_000); Seed("poor", 120);
        (await _db.TakeGoldFromPlayer("rich", 195_000)).Should().Be(195_000);
        Gold("rich").Should().Be(29_605_000, "the loser loses exactly what the winner gains");
        (await _db.TakeGoldFromPlayer("poor", 195_000)).Should().Be(120, "a spent balance yields what it holds");
        Gold("poor").Should().Be(0);
        (await _db.TakeGoldFromPlayer("poor", 195_000)).Should().Be(0);
        (await _db.TakeGoldFromPlayer("nobody", 195_000)).Should().Be(0, "a missing row mints nothing");
        (await _db.TakeGoldFromPlayer("rich", 0)).Should().Be(0);
        (await _db.TakeGoldFromPlayer("rich", -50)).Should().Be(0);
        Gold("rich").Should().Be(29_605_000);
    }

    [Fact]
    public async Task ConcurrentTakes_NeverTakeMoreThanIsThere()
    {
        Seed("shared", 500_000);
        var taken = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => _db.TakeGoldFromPlayer("shared", 195_000)));
        taken.Sum().Should().Be(500_000, "twelve winners cannot take more than the loser held");
        Gold("shared").Should().Be(0);
    }
}
