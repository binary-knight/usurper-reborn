using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.4 self-host: the site the game's reset pointer names. The rule is the web side's (accountSiteUrl in
/// web/ssh-proxy.js), tested on the same cases: both read web/test/site-url-cases.json.
/// </summary>
[Collection("SharedGameSingletons")]
public class AccountMailSite_SelfHost134Tests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-sh134-site-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "web"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    private static JsonElement Cases() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "web", "test", "site-url-cases.json"))).RootElement;

    [Fact]
    public void Rule_AcceptsAndRefuses_TheWebSidesCases()
    {
        var cases = Cases();
        var accepted = cases.GetProperty("accepted").EnumerateArray().ToList();
        var refused = cases.GetProperty("refused").EnumerateArray().Select(e => e.GetString()!).ToList();
        accepted.Count.Should().BeGreaterOrEqualTo(8);
        refused.Count.Should().BeGreaterOrEqualTo(20);
        foreach (var pair in accepted)
        {
            string raw = pair[0].GetString()!, want = pair[1].GetString()!;
            MailSite.Accept(raw).Should().Be(want, raw);
            MailSite.Resolve(raw).Should().Be(want, raw);
        }
        foreach (var raw in refused)
        {
            MailSite.Accept(raw).Should().BeNull(raw);
            MailSite.Resolve(raw).Should().Be(MailSite.Default, raw);
        }
        MailSite.Accept(null).Should().BeNull();
        MailSite.Accept("https://" + new string('a', 63) + ".example.com").Should().NotBeNull();
        MailSite.Accept("https://" + new string('a', 64) + ".example.com").Should().BeNull("a label over 63");
        MailSite.Default.Should().Be("https://usurper-reborn.net");
    }

    [Fact]
    public void TheWebDefault_IsTheGameDefault()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "web", "ssh-proxy.js"));
        src.Should().Contain($"const ACCOUNT_SITE = '{MailSite.Default}';");
        src.Should().Contain("up.run('mail_site', site);", "the heartbeat writes the key the game reads");
        MailSite.Key.Should().Be("mail_site");
    }

    [Fact]
    public void Read_TheRow_OrTheDefault_WhenMissingOrRefused()
    {
        _ = Db;
        MailSite.Read(Db).Should().Be(MailSite.Default, "no row");
        MailSite.Read(null).Should().Be(MailSite.Default);
        void Set(string? v) => AccountMailData_PwReset134Tests.Exec(_path,
            "INSERT OR REPLACE INTO world_state (key, value, version, updated_at) VALUES ('mail_site', @v, 1, datetime('now'));", ("@v", v));
        Set("https://game.example.com:8443");
        MailSite.Read(Db).Should().Be("https://game.example.com:8443");
        Set("https://game.example.com/path");
        MailSite.Read(Db).Should().Be(MailSite.Default, "a path");
        Set("javascript:alert(1)");
        MailSite.Read(Db).Should().Be(MailSite.Default);
        MailSite.ForgotUrl("https://game.example.com").Should().Be("https://game.example.com/account/forgot");
    }

    [Fact]
    public void ThePointer_IsOneRow_InEveryLanguage_WithTheSitesPage()
    {
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            string line = UsurperRemake.Server.MudServer.WithForgotPointer(Loc.GetIn(lang, "auth.err_bad_login"), lang, "https://game.example.com");
            line.Should().EndWith(" https://game.example.com/account/forgot", lang);
            line.IndexOfAny(new[] { '\r', '\n' }).Should().Be(-1, "the AUTH line stays one line (its recorded width exception)");
            line.Should().NotContain("usurper-reborn.net", lang);
        }
    }
}
