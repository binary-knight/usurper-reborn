using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// 1.3.9: the SSH relay's informational "[RELAY] ..." lines went to stderr, which an SSH session
/// shows the player, so every web and SSH player saw "[RELAY] MUD relay mode enabled", the user and
/// port, and "Connecting to MUD server" before the login box. They are gone; the relay's error lines,
/// which tell a player why the connection failed, stay.
/// </summary>
[Collection("SharedGameSingletons")]
public class RelayQuiet139Tests
{
    private static string Src(params string[] path)
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(path).ToArray()));
    }

    private static string[] RelayStderrLines() =>
        new[] { Src("Scripts", "BBS", "DoorMode.cs"), Src("Console", "Bootstrap", "Program.cs"), Src("Scripts", "Server", "RelayClient.cs") }
            .SelectMany(s => Regex.Matches(s, "Console\\.Error\\.WriteLine\\(\\$?\"\\[RELAY\\][^\"]*\"").Select(m => m.Value))
            .ToArray();

    [Fact]
    public void TheInformationalRelayLinesNoLongerReachThePlayer()
    {
        var lines = RelayStderrLines();
        foreach (var gone in new[] { "MUD relay mode enabled", "] User: ", "Connecting to MUD server", "Terminal in raw mode", "Connected as" })
            lines.Should().NotContain(l => l.Contains(gone), gone);
    }

    [Fact]
    public void StartingTheRelayPrintsNothingOnStderr()
    {
        // ParseCommandLineArgs sets DoorMode's static state; every static field is put back after.
        var fields = typeof(UsurperRemake.BBS.DoorMode).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(f => !f.IsLiteral && !f.IsInitOnly).ToArray();
        var saved = fields.Select(f => f.GetValue(null)).ToArray();
        var oldErr = Console.Error;
        var err = new StringWriter();
        Console.SetError(err);
        try
        {
            UsurperRemake.BBS.DoorMode.ParseCommandLineArgs(new[] { "--mud-relay", "--mud-port", "4999" });
            UsurperRemake.BBS.DoorMode.IsMudRelayMode.Should().BeTrue("the flag still works");
        }
        finally
        {
            Console.SetError(oldErr);
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, saved[i]);
        }
        err.ToString().Should().NotContain("[RELAY]", "the player sees the relay's stderr");
    }

    [Fact]
    public void OnlyTheRelayErrorLinesStayOnStderr()
    {
        var lines = RelayStderrLines();
        lines.Should().NotBeEmpty();
        var errors = new[] { "Invalid AUTH format", "Server closed connection during auth", "Auth failed", "Unexpected auth response",
                             "Cannot connect to game server", "Console.ReadKey not available" };
        lines.Should().OnlyContain(l => errors.Any(e => l.Contains(e)), "every [RELAY] line left on stderr reports a failure");
        foreach (var e in errors) lines.Should().Contain(l => l.Contains(e), e + " still tells the player what failed");
    }
}
