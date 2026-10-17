using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.4 password self-reset at the login menu (telnet, MUD clients, the web terminal): the forgot password choice
/// after a failed login with one reply and one row for any name, the per-IP cap, the optional email step after a
/// registration, the 8 character minimum there, and the one-time email question after login (EmailPrompt). Mail
/// off shows none of it. Rows fit 79 columns in English and Hungarian.
/// </summary>
[Collection("SharedGameSingletons")]
public class LoginScreen_PwReset134Tests : IDisposable
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-pw134-login-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);
    private readonly Func<DateTime> _clock = MailSwitch.UtcNow;
    private readonly List<string> _ips = new();

    public void Dispose()
    {
        MailSwitch.UtcNow = _clock;
        foreach (var ip in _ips) FailedLogins().TryRemove(ip, out _);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    // ---------- helpers ----------

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);
    private static string Visible(string row) => Regex.Replace(row, "\u001b\\[[0-9;]*[A-Za-z]", "");

    private void MailOn() { _ = Db; AccountMailData_PwReset134Tests.SetMailSwitch(_path, "1", 1); }
    private void MailOff() { _ = Db; AccountMailData_PwReset134Tests.SetMailSwitch(_path, "0", 1); }
    private long Count(string sql) => Convert.ToInt64(AccountMailData_PwReset134Tests.Scalar(_path, sql));
    private List<Dictionary<string, object?>> Rows(string sql) => AccountMailData_PwReset134Tests.Rows(_path, sql);

    private static ConcurrentDictionary<string, (int Fails, DateTime LastFail, DateTime LockUntil)> FailedLogins() =>
        (ConcurrentDictionary<string, (int, DateTime, DateTime)>)typeof(MudServer).GetField("_failedLogins", SNP)!.GetValue(null)!;

    private string Ip(int n)
    {
        var ip = $"198.51.100.{n}";
        FailedLogins().TryRemove(ip, out _);
        _ips.Add(ip);
        return ip;
    }

    private static MudServer NewServer()
    {
        var server = (MudServer)RuntimeHelpers.GetUninitializedObject(typeof(MudServer));
        typeof(MudServer).GetField("<ActiveSessions>k__BackingField", NP)!
            .SetValue(server, new ConcurrentDictionary<string, PlayerSession>());
        server.StopAfterLoginReply = true; // the login replies only: no game session starts
        return server;
    }

    private static async Task Send(NetworkStream s, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await s.WriteAsync(bytes, 0, bytes.Length);
        await s.FlushAsync();
    }

    private static async Task<bool> ReadMore(NetworkStream s, StringBuilder output)
    {
        var buffer = new byte[4096];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        int read;
        try { read = await s.ReadAsync(buffer, 0, buffer.Length, cts.Token); }
        catch (Exception) { return false; }
        if (read == 0) return false;
        output.Append(Encoding.UTF8.GetString(buffer, 0, read));
        return true;
    }

    private static async Task WaitFor(NetworkStream s, StringBuilder output, string marker, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (Regex.Matches(output.ToString(), Regex.Escape(marker)).Count < count && DateTime.UtcNow < deadline)
            if (!await ReadMore(s, output)) return;
    }

    /// <summary>
    /// One interactive connection from <paramref name="ip"/>: each step is a line to send and the number of menu
    /// prompts to wait for first (null: wait for the text given instead). Ends with Q at the menu.
    /// </summary>
    private async Task<string> Interactive(string ip, params (string Send, object WaitFor)[] steps)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var output = new StringBuilder();
        string choice = L("en", "auth.choice");
        try
        {
            using var tcp = new TcpClient();
            var acceptTask = listener.AcceptTcpClientAsync();
            await tcp.ConnectAsync(IPAddress.Loopback, port);
            var accepted = await acceptTask;
            var handle = typeof(MudServer).GetMethod("HandleConnectionAsync", NP)!;
            var serverTask = (Task)handle.Invoke(NewServer(), new object[] { accepted, Db, cts.Token })!;
            var s = tcp.GetStream();
            await Send(s, $"X-IP:{ip}\n");
            foreach (var (line, wait) in steps)
            {
                if (wait is int menus) await WaitFor(s, output, choice, menus);
                else await WaitFor(s, output, (string)wait, Regex.Matches(output.ToString(), Regex.Escape((string)wait)).Count + 1);
                try { await Send(s, line + "\n"); } catch (IOException) { break; }
            }
            while (await ReadMore(s, output)) { }
            await serverTask.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally { listener.Stop(); }
        return Visible(output.ToString());
    }

    private async Task Register(string name, string password = "long-enough-1") =>
        (await Db.RegisterPlayer(name, password)).success.Should().BeTrue();

    /// <summary>A failed login, the forgot choice with <paramref name="name"/>, then Q.</summary>
    private Task<string> ForgotFlow(string ip, string name) => Interactive(ip,
        ("L", 1), ("Someone", L("en", "auth.username")), ("wrong-pass-1", L("en", "auth.password")), ("F", 2), (name, L("en", "auth.forgot_name")), ("Q", 3));

    // ================= 1.3 the forgot password choice =================

    [Fact]
    public async Task ForgotChoice_OnlyAfterAFailedLogin_AndOnlyWithMailOn()
    {
        await Register("Real Name");
        MailOn();
        var on = await Interactive(Ip(61), ("L", 1), ("Real Name", L("en", "auth.username")), ("wrong-pass-1", L("en", "auth.password")), ("Q", 2));
        string choice = L("en", "auth.choice");
        int second = on.IndexOf(L("en", "auth.err_bad_login"), StringComparison.Ordinal);
        second.Should().BeGreaterThan(-1);
        on.Substring(0, second).Should().NotContain("[F]", "no forgot choice before a failed login");
        on.Substring(second).Should().Contain("[F] " + L("en", "auth.forgot"), "after a failed login with mail on");
        on.Should().Contain(choice);

        MailOff();
        var off = await Interactive(Ip(62), ("L", 1), ("Real Name", L("en", "auth.username")), ("wrong-pass-1", L("en", "auth.password")), ("F", 2), ("Q", 3));
        off.Should().NotContain("[F]", "mail off: no reset choice");
        off.Should().NotContain(L("en", "auth.forgot_name"));
        Count("SELECT COUNT(*) FROM mail_requests;").Should().Be(0);
    }

    [Fact]
    public async Task ForgotReply_AndRow_AreTheSame_ForAKnownAndAnUnknownName()
    {
        await Register("Real Name");
        MailOn();
        var known = await ForgotFlow(Ip(63), "Real Name");
        var unknown = await ForgotFlow(Ip(64), "Ghost Name");

        string Reply(string output)
        {
            int from = output.IndexOf(L("en", "auth.forgot_sent"), StringComparison.Ordinal);
            from.Should().BeGreaterThan(-1, output);
            int to = output.IndexOf(L("en", "auth.choice"), from, StringComparison.Ordinal);
            return output.Substring(from, to - from);
        }
        Reply(known).Should().Be(Reply(unknown), "one reply for every name");
        Reply(known).Should().Contain(L("en", "auth.forgot_sent2"));

        var rows = Rows("SELECT kind, username, address, ip, lang, status FROM mail_requests ORDER BY id;");
        rows.Should().HaveCount(2);
        rows[0]["username"].Should().Be("real name");
        rows[1]["username"].Should().Be("ghost name");
        foreach (var row in rows) { row.Remove("username"); row.Remove("ip"); }
        rows[0].Should().BeEquivalentTo(rows[1], "the same row for a known and an unknown name");
        rows[0]["kind"].Should().Be("reset");
        rows[0]["address"].Should().BeNull();
    }

    [Fact]
    public async Task ForgotPastTheCap_SameReply_NoRow()
    {
        MailOn();
        string ip = Ip(65);
        for (int i = 0; i < GameConfig.MailRequestsPerIpPerHour; i++)
            AccountMailData_PwReset134Tests.Exec(_path, "INSERT INTO mail_requests (kind, username, ip) VALUES ('reset', 'earlier', @ip);", ("@ip", ip));
        var output = await ForgotFlow(ip, "Real Name");
        output.Should().Contain(L("en", "auth.forgot_sent")).And.Contain(L("en", "auth.forgot_sent2"));
        Count("SELECT COUNT(*) FROM mail_requests;").Should().Be(GameConfig.MailRequestsPerIpPerHour, "no row past the cap");
    }

    // ================= 1.4 the registration email step, and the minimum there =================

    private Task<string> RegisterFlow(string ip, string name, string password, params string[] emails)
    {
        var steps = new List<(string, object)> { ("R", 1), (name, L("en", "auth.reg_username")),
            (password, L("en", "auth.reg_password")), (password, L("en", "auth.reg_confirm")) };
        foreach (var email in emails) steps.Add((email, L("en", "auth.reg_email")));
        return Interactive(ip, steps.ToArray());
    }

    [Fact]
    public async Task Registration_AsksForAnOptionalAddress_WhenMailIsOn()
    {
        MailOn();
        var set = await RegisterFlow(Ip(66), "Set Player", "set-pass-1", "not an address", "Set.Player@Example.com");
        set.Should().Contain(L("en", "auth.err_email_invalid")).And.Contain(L("en", "auth.reg_email_sent"));
        var row = Rows("SELECT kind, username, address FROM mail_requests;").Single();
        row["kind"].Should().Be("verify");
        row["username"].Should().Be("set player");
        row["address"].Should().Be("set.player@example.com");
        Db.GetAccountEmail("Set Player").Should().Be((true, (string?)null, true), "the address waits for its link; the account was asked");

        await RegisterFlow(Ip(67), "Skip Player", "skip-pass-1", "");
        Count("SELECT COUNT(*) FROM mail_requests;").Should().Be(1, "Enter skips");
        Db.GetAccountEmail("Skip Player").Prompted.Should().BeTrue();
    }

    [Fact]
    public async Task Registration_NoEmailStep_WhenMailIsOff_OrOverTheAuthLine()
    {
        MailOff();
        var output = await RegisterFlow(Ip(68), "Quiet Player", "quiet-pass-1");
        output.Should().NotContain(L("en", "auth.reg_email"));
        output.Should().Contain(L("en", "auth.welcome"));
        Db.GetAccountEmail("Quiet Player").Prompted.Should().BeFalse("not asked: the first login with mail on asks");
        Count("SELECT COUNT(*) FROM mail_requests;").Should().Be(0);
    }

    [Fact]
    public async Task Registration_RefusesASevenCharacterPassword()
    {
        MailOn();
        var output = await Interactive(Ip(69), ("R", 1), ("Short Player", L("en", "auth.reg_username")), ("seven77", L("en", "auth.reg_password")), ("Q", 2));
        output.Should().Contain(L("en", "auth.err_password_len"));
        Db.PlayerExists("Short Player").Should().BeFalse();
    }

    // ================= 1.5 the one-time email question =================

    private (TerminalEmulator Term, MemoryStream Output) Term(params string[] lines)
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new LineStream(lines), output), output);
    }

    private static string Plain(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r\n", "\n");
    }

    [Fact]
    public async Task FirstLoginQuestion_ShowsOnce_NeverAgainAfterASkipOrASet_NeverWithMailOff()
    {
        await Register("Skipper");
        await Register("Setter");
        await Register("Later One");
        await Register("Has One");
        AccountMailData_PwReset134Tests.Exec(_path, "UPDATE players SET email = 'has.one@example.net' WHERE username = 'has one';");

        // mail off: never shown, nothing marked
        MailOff();
        int before = EmailPrompt.Drawn;
        var (t0, o0) = Term("");
        await EmailPrompt.AskIfDueAsync(t0, Db, "Later One", "198.51.100.70");
        EmailPrompt.Drawn.Should().Be(before);
        Plain(t0, o0).Should().NotContain(L("en", "email.prompt_title"));
        Db.GetAccountEmail("Later One").Prompted.Should().BeFalse();

        MailOn();
        // a skip
        var (t1, o1) = Term("");
        await EmailPrompt.AskIfDueAsync(t1, Db, "Skipper", "198.51.100.70");
        EmailPrompt.Drawn.Should().Be(before + 1);
        Plain(t1, o1).Should().Contain(L("en", "email.prompt_title"));
        foreach (var row in L("en", "email.skipped").Split('\n')) Plain(t1, o1).Should().Contain(" " + row + "\n", "1.3.4 self-host: the skip text has more than one row");
        Db.GetAccountEmail("Skipper").Prompted.Should().BeTrue();
        await EmailPrompt.AskIfDueAsync(Term("").Term, Db, "Skipper", "198.51.100.70");
        EmailPrompt.Drawn.Should().Be(before + 1, "never again after a skip");

        // a typo, then an address
        var (t2, o2) = Term("not-an-address", "Setter@Example.net");
        await EmailPrompt.AskIfDueAsync(t2, Db, "Setter", "198.51.100.71");
        Plain(t2, o2).Should().Contain(L("en", "auth.err_email_invalid")).And.Contain(L("en", "auth.reg_email_sent"));
        Rows("SELECT kind, username, address FROM mail_requests;").Single()["address"].Should().Be("setter@example.net");
        await EmailPrompt.AskIfDueAsync(Term("").Term, Db, "Setter", "198.51.100.71");
        EmailPrompt.Drawn.Should().Be(before + 2, "never again after a set");

        // an account that already has an address, and one turned on later
        await EmailPrompt.AskIfDueAsync(Term("").Term, Db, "Has One", "198.51.100.72");
        EmailPrompt.Drawn.Should().Be(before + 2);
        await EmailPrompt.AskIfDueAsync(Term("").Term, Db, "Later One", "198.51.100.72");
        EmailPrompt.Drawn.Should().Be(before + 3, "asked once mail is on");
    }

    [Fact]
    public void GameEngine_AsksOncePerSession_OnAServerOnly()
    {
        var src = File.ReadAllText(Path.Combine(UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot(), "Scripts", "Core", "GameEngine.cs"));
        src.Should().Contain("if (!_emailAskedThisSession && UsurperRemake.BBS.DoorMode.IsOnlineMode && !UsurperRemake.BBS.DoorMode.IsInDoorMode)");
        src.IndexOf("EmailPrompt.AskIfDueAsync(", StringComparison.Ordinal)
            .Should().BeGreaterThan(src.IndexOf("TelemetryPrompt.AskPlayerIfNeededAsync(terminal, playerName)", StringComparison.Ordinal));
    }

    // ================= 1.3.4 self-host: the operator warning and the privacy line =================

    internal const string OperatorSentence = "If you forget your password, the server's operator will have to reset it.";
    // 1.3.4: "This server uses..." (his choice, 2026-10-17): the line is each server's own promise, the official one or another
    internal const string PrivacyLine = "This server uses your address only for password resets and notices about your account. It is never sold or shared, except with the mail service that delivers these messages.";
    internal static string Flat(string s) => s.Replace("\n", " ");
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    [Fact]
    public void TheWarningAndPrivacyTexts_AreTheRulingsWords_AndTranslated()
    {
        Flat(L("en", "auth.reg_email_note")).Should().Be("Optional. Without an address, a forgotten password can only be reset by the server's operator.");
        Flat(L("en", "auth.reg_email_skipped")).Should().Be("No address added. " + OperatorSentence);
        Flat(L("en", "email.about")).Should().Be(PrivacyLine);
        Flat(L("en", "email.prompt_body")).Should().Contain(OperatorSentence).And.NotContain("nothing else");
        Flat(L("en", "email.skipped")).Should().Be("No address added. " + OperatorSentence + " E on the character screen adds one later.");
        Flat(L("en", "email.none")).Should().Be("No address on this account. " + OperatorSentence);
        Flat(L("en", "email.removed")).Should().Be("The address was removed. A notice is being sent to it. " + OperatorSentence);
        foreach (var lang in Langs.Skip(1))
            foreach (var key in new[] { "auth.reg_email_note", "auth.reg_email_skipped", "email.about", "email.prompt_body", "email.skipped", "email.none", "email.removed" })
                L(lang, key).Should().NotBe(L("en", key), $"{lang} {key} is translated");
    }

    [Fact]
    public async Task Registration_ShowsTheOperatorNote_AndThePrivacyLine_BeforeTheEmailStep()
    {
        MailOn();
        var output = await RegisterFlow(Ip(140), "Note Player", "note-pass-1", "Note.Player@Example.com");
        int ask = output.IndexOf(L("en", "auth.reg_email"), StringComparison.Ordinal);
        ask.Should().BeGreaterThan(0);
        foreach (var key in new[] { "auth.reg_email_note", "email.about" })
            foreach (var row in L("en", key).Split('\n'))
            {
                int at = output.IndexOf("  " + row + "\r\n", StringComparison.Ordinal);
                at.Should().BeGreaterThan(0, $"{key}: {row}");
                at.Should().BeLessThan(ask, "before the question");
            }
        output.Should().NotContain(L("en", "auth.reg_email_skipped").Split('\n')[0], "an address was added");
        output.Should().Contain(L("en", "auth.reg_email_sent"));
    }

    [Fact]
    public async Task Registration_Enter_SaysTheOperatorResets_LeavesNoAddress_NoRow()
    {
        MailOn();
        var output = await RegisterFlow(Ip(141), "Skip Two", "skip-pass-2", "");
        int ask = output.IndexOf(L("en", "auth.reg_email"), StringComparison.Ordinal);
        foreach (var row in L("en", "auth.reg_email_skipped").Split('\n'))
            output.IndexOf("  " + row + "\r\n", StringComparison.Ordinal).Should().BeGreaterThan(ask, row);
        Db.GetAccountEmail("Skip Two").Should().Be((true, (string?)null, true), "no address, and asked");
        Count("SELECT COUNT(*) FROM mail_requests;").Should().Be(0, "no row");
    }

    [Fact]
    public async Task Registration_MailOff_ShowsNoneOfTheLines()
    {
        MailOff();
        var output = await RegisterFlow(Ip(142), "Off Player", "off-pass-12");
        output.Should().Contain(L("en", "auth.welcome"));
        foreach (var key in new[] { "auth.reg_email_note", "email.about", "auth.reg_email_skipped" })
            foreach (var row in L("en", key).Split('\n'))
                output.Should().NotContain(row, key);
    }

    [Fact]
    public async Task FirstLoginQuestion_SaysTheOperatorResets_AndShowsThePrivacyLine()
    {
        await Register("Asked One");
        await Register("Off One");
        MailOff();
        var (t0, o0) = Term("");
        await EmailPrompt.AskIfDueAsync(t0, Db, "Off One", "198.51.100.143");
        var off = Plain(t0, o0);
        foreach (var key in new[] { "email.prompt_body", "email.about", "email.skipped" })
            foreach (var row in L("en", key).Split('\n'))
                off.Should().NotContain(row, "mail off: " + key);

        MailOn();
        var (t1, o1) = Term("");
        await EmailPrompt.AskIfDueAsync(t1, Db, "Asked One", "198.51.100.143");
        var screen = Plain(t1, o1);
        int ask = screen.IndexOf(L("en", "email.prompt_ask").Trim(), StringComparison.Ordinal);
        ask.Should().BeGreaterThan(0);
        foreach (var key in new[] { "email.prompt_body", "email.about" })
            foreach (var row in L("en", key).Split('\n'))
            {
                int at = screen.IndexOf(" " + row + "\n", StringComparison.Ordinal);
                at.Should().BeGreaterThan(0, $"{key}: {row}");
                at.Should().BeLessThan(ask, "before the question");
            }
        screen.Should().Contain(" " + OperatorSentence + "\n");
        foreach (var row in L("en", "email.skipped").Split('\n'))
            screen.IndexOf(" " + row + "\n", StringComparison.Ordinal).Should().BeGreaterThan(ask, row);
        Db.GetAccountEmail("Asked One").Email.Should().BeNull();
        Count("SELECT COUNT(*) FROM mail_requests;").Should().Be(0, "a skip writes no row");
    }

    // ================= 79 columns =================

    [Fact]
    public void NewRows_Fit79Columns_InEnglishAndHungarian()
    {
        foreach (var lang in new[] { "en", "hu" })
        {
            var rows = new List<string>();
            foreach (var row in MudServer.ForgotReplyRows(lang)) { rows.Add("  " + row); rows.Add(row); }
            foreach (var key in new[] { "auth.forgot_name", "auth.reg_email", "auth.err_email_invalid", "auth.reg_email_sent" })
                rows.Add("  " + L(lang, key) + " ");
            rows.AddRange(MudServer.AuthBoxRows(L(lang, "auth.title"), new[] {
                ("1;36", "L", L(lang, "auth.login")), ("1;32", "R", L(lang, "auth.register")), ("1;33", "F", L(lang, "auth.forgot")),
                ("1;35", "G", $"{L(lang, "auth.language")} (Magyar)"), ("1;31", "Q", L(lang, "auth.quit")) }).Select(Visible));
            rows.Add("[F] " + L(lang, "auth.forgot"));
            rows.Add(" " + L(lang, "email.prompt_title"));
            rows.AddRange(L(lang, "email.prompt_body").Split('\n').Select(l => " " + l));
            rows.Add(" " + L(lang, "email.prompt_ask") + new string('x', 20));
            rows.AddRange(L(lang, "email.skipped").Split('\n').Select(l => " " + l));
            // 1.3.4 self-host: the operator warnings and the privacy line, at the registration step and the question
            foreach (var key in new[] { "auth.reg_email_note", "auth.reg_email_skipped", "email.about" })
                rows.AddRange(L(lang, key).Split('\n').Select(l => "  " + l));
            rows.AddRange(L(lang, "email.about").Split('\n').Select(l => " " + l));
            rows.Add(" " + L(lang, "auth.err_email_invalid"));
            rows.Add(" " + L(lang, "auth.reg_email_sent"));
            foreach (var row in rows)
                row.Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] {row}");
            // the box keeps the whole label
            MudServer.AuthBoxRows("t", new[] { ("1;33", "F", L(lang, "auth.forgot")) }).Select(Visible).Should().Contain(r => r.Contains(L(lang, "auth.forgot")));
        }
    }
}
