using System;
using System.IO;
using FluentAssertions;
using UsurperRemake.Server;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8 (design C3 row 6): a token login line or a probe that reaches a server or relay from 1.0.0 to 1.2.7
/// is refused at the part count, before any password work, without a log line that holds it and without
/// echoing it. The two parsers below are frozen copies of release-1.2.7 (d547b7f6): the protocol branch of
/// MudServer.HandleConnectionAsync (MudServer.cs:678-720, the same text as v1.0.0 MudServer.cs:584-586 for the
/// part check) and RelayClient.ParseDirectAuth (RelayClient.cs:184-219). Only the I/O is replaced by return
/// values; the parsing is verbatim. The lines fed to them are made by the 1.2.8 production code.
/// </summary>
public class Legacy127ParsersTests
{
    private sealed record Frozen127Reply(string Reply, string Log, bool VerifyCalled);

    /// <summary>release-1.2.7 MudServer protocol branch, up to the password verify. The ERR text is the static one.</summary>
    private static Frozen127Reply Frozen127Server(string authLine)
    {
        var log = new StringWriter();
        bool verifyCalled = false;
        string username;
        string connectionType;
        string? clientVersion = null;

        // Protocol mode: parse AUTH header
        var parts = authLine!.Split(':', 5);
        if (parts.Length < 3 || string.IsNullOrWhiteSpace(parts[1]))
        {
            return new Frozen127Reply("ERR:Invalid auth format. Expected AUTH:username:connectionType", log.ToString(), verifyCalled);
        }

        string? password = null;
        bool isRegistration = false;

        if (parts.Length == 5 && parts[3].Trim().Equals("REGISTER", StringComparison.OrdinalIgnoreCase))
        {
            username = parts[1].Trim();
            password = parts[2];
            connectionType = parts[4].Trim();
            isRegistration = true;
        }
        else if (parts.Length >= 4)
        {
            username = parts[1].Trim();
            password = parts[2];
            connectionType = parts[3].Trim();
            if (parts.Length == 5 && !string.IsNullOrWhiteSpace(parts[4]))
                clientVersion = parts[4].Trim();
        }
        else
        {
            username = parts[1].Trim();
            connectionType = parts[2].Trim();
        }

        connectionType = MudServer.ParseConnectionTypeParams(connectionType, out _);
        log.WriteLine($"[MUD] Connection from 127.0.0.1: user={username}, type={connectionType}, auth={(password != null ? (isRegistration ? "register" : "password") : "trusted")}");
        if (password != null)
            verifyCalled = true; // AuthenticatePlayer: PBKDF2, RecordFailedLogin, AuthFailedLogLine
        _ = clientVersion;
        return new Frozen127Reply("OK", log.ToString(), verifyCalled);
    }

    /// <summary>release-1.2.7 RelayClient.ParseDirectAuth, verbatim but for the log writer.</summary>
    private static (string username, string password, string connectionType, bool isRegistration, string? clientVersion)?
        Frozen127ParseDirectAuth(string authLine, TextWriter error)
    {
        // Strip the "AUTH:" prefix
        var payload = authLine.Substring(5);
        var parts = payload.Split(':');

        bool IsRegisterAt(int i) =>
            parts.Length > i && parts[i].Equals("REGISTER", StringComparison.OrdinalIgnoreCase);

        if (parts.Length == 4 && IsRegisterAt(2))
        {
            // username:password:REGISTER:connectionType
            return (parts[0], parts[1], parts[3], true, null);
        }
        if (parts.Length == 5 && IsRegisterAt(2))
        {
            // username:password:REGISTER:connectionType:clientVersion
            return (parts[0], parts[1], parts[3], true, parts[4]);
        }
        if (parts.Length == 3)
        {
            // username:password:connectionType
            return (parts[0], parts[1], parts[2], false, null);
        }
        if (parts.Length == 4)
        {
            // username:password:connectionType:clientVersion
            return (parts[0], parts[1], parts[2], false, parts[3]);
        }

        error.WriteLine($"[RELAY] Invalid AUTH format: {parts.Length} parts");
        return null;
    }

    private static (string Line, string Token, string Verifier) TokenLine(string? version)
    {
        var t = LoginTokenProtocol.NewToken();
        string token = $"{t.Selector}.{t.Verifier}";
        return (LoginTokenProtocol.FormatTokenLogin(token, "Steam", version), token, t.Verifier);
    }

    [Theory]
    [InlineData("1.2.8")]
    [InlineData(null)]
    public void TokenLine_OnA127Server_GetsTheStaticErr_NoVerify_NoLog(string? version)
    {
        var (line, token, verifier) = TokenLine(version);
        var r = Frozen127Server(line);
        r.Reply.Should().Be("ERR:Invalid auth format. Expected AUTH:username:connectionType", "refused at the part count");
        r.VerifyCalled.Should().BeFalse("no password work, so no failed login and no throttle record");
        r.Log.Should().BeEmpty("the old server logs nothing for it");
        r.Reply.Should().NotContain(verifier).And.NotContain(token);
    }

    [Theory]
    [InlineData("1.2.8")]
    [InlineData(null)]
    public void TokenLine_OnA127Relay_IsRefused_LoggingOnlyThePartCount(string? version)
    {
        var (line, token, verifier) = TokenLine(version);
        var error = new StringWriter();
        Frozen127ParseDirectAuth(line, error).Should().BeNull("the old relay shows its menu again");
        error.ToString().Should().Be($"[RELAY] Invalid AUTH format: 1 parts{Environment.NewLine}");
        error.ToString().Should().NotContain(verifier).And.NotContain(token);
    }

    [Fact]
    public void Probe_OnA127ServerAndRelay_IsRefusedTheSameWay()
    {
        var r = Frozen127Server(LoginTokenProtocol.CapsLine);
        r.Reply.Should().StartWith("ERR:Invalid auth format");
        r.VerifyCalled.Should().BeFalse();
        r.Log.Should().BeEmpty();
        var error = new StringWriter();
        Frozen127ParseDirectAuth(LoginTokenProtocol.CapsLine, error).Should().BeNull();
        error.ToString().Should().Be($"[RELAY] Invalid AUTH format: 1 parts{Environment.NewLine}");
    }

    [Fact]
    public void Frozen127Copies_StillMatchTheLegacyShapes()
    {
        // the copies are faithful: a 1.1 password line still parses as a login on both
        Frozen127Server("AUTH:Someone:pw:Steam:1.2.7").VerifyCalled.Should().BeTrue();
        Frozen127ParseDirectAuth("AUTH:Someone:pw:Steam:1.2.7", TextWriter.Null).Should().NotBeNull();
        RelayClient.ParseDirectAuth("AUTH:Someone:pw:Steam:1.2.7").Should().Be(
            Frozen127ParseDirectAuth("AUTH:Someone:pw:Steam:1.2.7", TextWriter.Null), "1.2.8 keeps the 1.2.7 relay parser");
    }
}
