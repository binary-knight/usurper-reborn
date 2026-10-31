using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// 1.3.9: the SSH relay's informational "[RELAY] ..." lines went to stderr, which an SSH session
/// shows the player, so every web and SSH player saw "[RELAY] MUD relay mode enabled", the user and
/// port, and "Connecting to MUD server" before the login box. They are gone; the relay's error lines,
/// which tell a player why the connection failed, stay.
/// </summary>
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
