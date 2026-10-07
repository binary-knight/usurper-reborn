using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8: the web admin's player password reset writes the game's own PBKDF2 "salt:hash" form. The
/// vector in Fixtures/game-password-vector.json is what web/test/auth.test.js makes with the web
/// reset helper (a fixed salt); here the game's VerifyPassword accepts it, a real login with it
/// succeeds, and the wrong password is refused. The web helper's work factor is the game's.
/// </summary>
[Collection("SharedGameSingletons")]
public class WebPasswordReset128Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-webreset-128-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);

    public void Dispose()
    {
        if (_db == null) return;
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private sealed record Vector(string Password, string WrongPassword, string SaltBase64, int Iterations, int KeyBytes, string Stored);

    private static Vector Fixture()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "game-password-vector.json")));
        var r = doc.RootElement;
        return new Vector(r.GetProperty("password").GetString()!, r.GetProperty("wrong_password").GetString()!,
            r.GetProperty("salt_base64").GetString()!, r.GetProperty("iterations").GetInt32(),
            r.GetProperty("key_bytes").GetInt32(), r.GetProperty("stored").GetString()!);
    }

    private static bool VerifyPassword(string password, string stored) =>
        (bool)typeof(SqlSaveBackend).GetMethod("VerifyPassword", SNP)!.Invoke(null, new object[] { password, stored })!;

    private void InsertAccount(string key, string hash)
    {
        _ = Db;
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO players (username, display_name, password_hash, player_data) VALUES (@u, @u, @h, '{}');";
        cmd.Parameters.AddWithValue("@u", key);
        cmd.Parameters.AddWithValue("@h", hash);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void WebVector_IsTheGameForm_AndTheGameVerifiesIt()
    {
        var v = Fixture();
        v.Password.Should().Contain("ä", "a non-ASCII character proves both sides hash the UTF-8 bytes");
        v.Iterations.Should().Be(SqlSaveBackend.Pbkdf2Iterations);
        v.Stored.Should().StartWith(v.SaltBase64 + ":");
        SqlSaveBackend.IsUsableHash(v.Stored).Should().BeTrue();
        var parts = v.Stored.Split(':');
        Convert.FromBase64String(parts[0]).Length.Should().Be(16, "the game's salt size");
        Convert.FromBase64String(parts[1]).Length.Should().Be(v.KeyBytes).And.Be(32, "the game's key size");

        VerifyPassword(v.Password, v.Stored).Should().BeTrue("the game accepts the hash the web reset makes");
        VerifyPassword(v.WrongPassword, v.Stored).Should().BeFalse();
    }

    [Fact]
    public async Task AccountResetFromTheWeb_LogsInWithTheNewPassword_Only()
    {
        var v = Fixture();
        InsertAccount("web reset player", v.Stored);
        (await Db.AuthenticatePlayer("Web Reset Player", v.WrongPassword, null, "en")).success.Should().BeFalse();
        var ok = await Db.AuthenticatePlayer("Web Reset Player", v.Password, null, "en");
        ok.success.Should().BeTrue("a player reset from the web admin can log in");
        ok.reason.Should().Be(SqlSaveBackend.LoginFailure.None);
    }

    [Fact]
    public void TheWebResetHelper_UsesTheGameWorkFactor()
    {
        var js = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "web", "ssh-proxy.js"));
        int Const(string name)
        {
            var m = Regex.Match(js, @"\bconst " + name + @" = (\d+);");
            m.Success.Should().BeTrue(name + " is declared in ssh-proxy.js");
            return int.Parse(m.Groups[1].Value);
        }
        Const("GAME_PBKDF2_ITERATIONS").Should().Be(SqlSaveBackend.Pbkdf2Iterations);
        Const("GAME_PBKDF2_SALT_BYTES").Should().Be(16);
        Const("GAME_PBKDF2_KEY_BYTES").Should().Be(32);
    }
}
