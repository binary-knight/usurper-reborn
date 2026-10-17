using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.BBS;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.4 (1.6): E on the online character screen. The address shows masked only; add or change and remove need the
/// current password; a change writes a verify request and leaves the old address until it is confirmed; a remove
/// clears it and writes a notice to it (S5). With mail off there is no E, and E is any other key as before.
/// </summary>
[Collection("SharedGameSingletons")]
public class EmailMenu_PwReset134Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly FieldInfo OnlineField = typeof(DoorMode).GetField("_onlineMode", SNP)!;
    private static readonly FieldInfo SaveField = typeof(SaveSystem).GetField("instance", SNP)!;
    private readonly object? _onlineBefore = OnlineField.GetValue(null);
    private readonly object? _saveBefore = SaveField.GetValue(null);
    private readonly Func<DateTime> _clock = MailSwitch.UtcNow;
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-pw134-menu-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);

    public void Dispose()
    {
        OnlineField.SetValue(null, _onlineBefore);
        SaveField.SetValue(null, _saveBefore);
        MailSwitch.UtcNow = _clock;
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);
    private long Count(string sql) => Convert.ToInt64(AccountMailData_PwReset134Tests.Scalar(_path, sql));
    private List<Dictionary<string, object?>> Rows(string sql) => AccountMailData_PwReset134Tests.Rows(_path, sql);

    private async Task<string> Menu(string username, params string[] lines)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines), output);
        await EmailMenu.RunAsync(term, Db, username, "198.51.100.80");
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r\n", "\n");
    }

    private async Task Owner()
    {
        (await Db.RegisterPlayer("Owner", "owner-pass-1")).success.Should().BeTrue();
        AccountMailData_PwReset134Tests.Exec(_path, "UPDATE players SET email = 'owner.name@example.net', email_verified_at = datetime('now') WHERE username = 'owner';");
    }

    [Fact]
    public async Task Menu_ShowsTheAddressMaskedOnly()
    {
        await Owner();
        var screen = await Menu("Owner", "0");
        screen.Should().Contain(L("en", "email.current", "o***@e***.net"));
        screen.Should().NotContain("owner.name").And.NotContain("example.net");
        screen.Should().Contain(L("en", "email.opt_change")).And.Contain(L("en", "email.opt_remove"));

        (await Db.RegisterPlayer("Bare", "bare-pass-12")).success.Should().BeTrue();
        var bare = await Menu("Bare", "0");
        bare.Should().Contain(L("en", "email.none")).And.NotContain(L("en", "email.opt_remove"), "nothing to remove");
    }

    [Fact]
    public async Task AddOrChange_WithoutTheCurrentPassword_IsRefused()
    {
        await Owner();
        var blank = await Menu("Owner", "1", "", "");
        blank.Should().Contain(L("en", "ui.cancelled"));
        var wrong = await Menu("Owner", "1", "wrong-pass-1", "new.owner@example.com", "");
        wrong.Should().Contain(L("en", "auth.err_current_password"));
        Count("SELECT COUNT(*) FROM mail_requests;").Should().Be(0, "no request without the current password");

        var bad = await Menu("Owner", "1", "owner-pass-1", "not an address", "");
        bad.Should().Contain(L("en", "email.err_invalid"));
        Count("SELECT COUNT(*) FROM mail_requests;").Should().Be(0);

        var ok = await Menu("Owner", "1", "owner-pass-1", "New.Owner@Example.com", "");
        ok.Should().Contain(L("en", "auth.reg_email_sent")).And.Contain(L("en", "email.change_note"));
        var row = Rows("SELECT kind, username, address FROM mail_requests;").Single();
        row["kind"].Should().Be("verify");
        row["address"].Should().Be("new.owner@example.com");
        Db.GetAccountEmail("Owner").Email.Should().Be("owner.name@example.net", "the old address stays until the new one is confirmed");
    }

    [Fact]
    public async Task Remove_NeedsThePassword_ThenNotifiesTheRemovedAddress()
    {
        await Owner();
        var wrong = await Menu("Owner", "2", "wrong-pass-1", "");
        wrong.Should().Contain(L("en", "auth.err_current_password"));
        Db.GetAccountEmail("Owner").Email.Should().Be("owner.name@example.net");

        var removed = await Menu("Owner", "2", "owner-pass-1", "");
        removed.Should().Contain(L("en", "email.removed"));
        Db.GetAccountEmail("Owner").Email.Should().BeNull();
        var notice = Rows("SELECT kind, address FROM mail_requests;").Single();
        notice["kind"].Should().Be("remove_notice");
        notice["address"].Should().Be("owner.name@example.net");
    }

    [Fact]
    public void E_IsShown_OnlyOnAServer_WhileMailIsOn()
    {
        _ = Db;
        var shown = typeof(GameEngine).GetMethod("EmailMenuShown", SNP)!;
        SaveSystem.InitializeWithBackend(Db);
        OnlineField.SetValue(null, true);

        AccountMailData_PwReset134Tests.SetMailSwitch(_path, "1", 1);
        ((bool)shown.Invoke(null, null)!).Should().BeTrue("a server sending mail");
        AccountMailData_PwReset134Tests.SetMailSwitch(_path, "0", 1);
        ((bool)shown.Invoke(null, null)!).Should().BeFalse("mail off: no email menu");
        AccountMailData_PwReset134Tests.SetMailSwitch(_path, "1", 11);
        ((bool)shown.Invoke(null, null)!).Should().BeFalse("the sender stopped beating");
        AccountMailData_PwReset134Tests.SetMailSwitch(_path, "1", 1);
        OnlineField.SetValue(null, false);
        ((bool)shown.Invoke(null, null)!).Should().BeFalse("single player has no account");

        var src = File.ReadAllText(Path.Combine(UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot(), "Scripts", "Core", "GameEngine.cs"));
        src.Should().Contain("if (EmailMenuShown())\n                WriteMenuKey(\"E\", Loc.Get(\"engine.menu_email\"));");
        src.Should().Contain("if (EmailMenuShown() && SaveSystem.Instance.Backend is SqlSaveBackend emailDb)");
        src.Should().Contain("goto default; // mail off: E is any other key, as before 1.3.4");
    }

    [Fact]
    public void MenuRows_Fit79Columns_InEnglishAndHungarian()
    {
        var longest = AccountEmail.Mask("a@d." + new string('t', 200));
        foreach (var lang in new[] { "en", "hu" })
        {
            var rows = new List<string>
            {
                L(lang, "email.title_visual"), L(lang, "email.current", longest), L(lang, "email.none"), L(lang, "email.about"),
                "  [1] " + L(lang, "email.opt_change"), "  [2] " + L(lang, "email.opt_remove"),
                L(lang, "email.new_prompt") + new string('x', 30), L(lang, "email.err_invalid"), L(lang, "email.change_note"),
                L(lang, "email.removed"), L(lang, "email.failed"), "  [E] " + L(lang, "engine.menu_email"),
            };
            foreach (var row in rows)
                row.Length.Should().BeLessOrEqualTo(79, $"[{lang}] {row}");
        }
    }
}
