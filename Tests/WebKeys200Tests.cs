using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 2.0 phase 1: every key that worked on Main Street (both layouts) and in a dungeon room does the same thing after the
/// web client menu change, which moved the dungeon room's action list into one method that the room menu and the room
/// key handler both read.
///
/// Method. Each key is sent to the screen's own handler (MainStreetLocation.ProcessChoice, DungeonLocation.ProcessRoomChoice)
/// with no session, and the outcome is written as one line: where the key led (a LocationExitException's destination),
/// what the handler returned, the text it wrote (colour codes removed) and, in the dungeon, the room state after it.
/// Tests/Fixtures/web200/keys.txt was written by this test on origin/main at 0a3b1855, before the change, with
/// USURPER_WEB200_CAPTURE set; the test reads only methods that existed there, by reflection. Keys whose action opens
/// a whole game screen or rolls dice (a fight, collecting treasure, an event, examining, descending, camping, moving)
/// are sent only in rooms where the action cannot be done, so the handler's refusal is what is compared; the parity
/// tests in WebMenu200Tests cover that every key of the handler is still taken.
/// </summary>
[Collection("SharedGameSingletons")]
public class WebKeys200Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    private static string Plain(byte[] bytes) =>
        Regex.Replace(Encoding.UTF8.GetString(bytes), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "").Replace("\n", "|");

    private static TerminalEmulator Term(MemoryStream output) =>
        new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("R\n", 20)))), output);

    private static async Task<string> Outcome(Func<Task<bool>> act, TerminalEmulator t, MemoryStream output)
    {
        string result;
        try
        {
            var task = act();
            var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
            result = done != task ? "timeout" : "returned " + await task;
        }
        catch (LocationExitException ex) { result = "exit " + ex.DestinationLocation; }
        t.StreamWriterInternal?.Flush();
        return result + " :: " + Plain(output.ToArray());
    }

    private static readonly string[] StreetKeys =
        { "D", "I", "E", "A", "W", "M", "U", "J", "H", "B", "V", "T", "O", "L", "X", "K", "C", "Y", "+", "1", "2", "Z", "9", ">", "@", "6", "G" };

    // Districts keys that open a district are answered with R (back); the classic keys that open a screen are left out.
    private static readonly HashSet<string> ClassicSkip = new() { "M", "G", "H", "S", "N", "F", "P", "$", "=", "J", "3", "4", "5", "7", "R" };

    internal static async Task<List<string>> StreetLines(bool classic)
    {
        var lines = new List<string>();
        foreach (var key in StreetKeys)
        {
            if (classic && ClassicSkip.Contains(key)) continue;
            var hero = WebFlagOff200Tests.Hero(false, classic);
            var street = new MainStreetLocation();
            var output = new MemoryStream();
            var t = Term(output);
            typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, t);
            typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
            var method = typeof(MainStreetLocation).GetMethod("ProcessChoice", F)!;
            string o = await Outcome(() => (Task<bool>)method.Invoke(street, new object[] { key })!, t, output);
            lines.Add($"street {(classic ? "classic" : "districts")} {key} => {o}");
        }
        return lines;
    }

    // name, (set up the room), keys sent
    private static readonly (string Name, Action<DungeonRoom, DungeonLocation, Character> Setup, string[] Keys)[] RoomStates =
    {
        ("monsters", (r, d, h) => { r.HasMonsters = true; r.IsCleared = false; r.HasTreasure = true; r.HasStairsDown = true; r.Features.Clear(); r.HasEvent = false; },
            new[] { "T", "V", "X", "D", "R", "Y", "J", "U", "Q", "Z", "K", "H", "1", "N", "*", "%", "L" }),
        ("bare", (r, d, h) => { r.HasMonsters = false; r.IsCleared = true; r.HasTreasure = false; r.HasStairsDown = false; r.HasEvent = false; r.Features.Clear(); },
            new[] { "F", "T", "V", "X", "D", "Y", "J", "U", "Q", "Z", "N", "S" }),
        ("camped", (r, d, h) => { r.HasMonsters = false; r.IsCleared = true; r.HasTreasure = true; r.TreasureLooted = true; r.HasEvent = true; r.EventCompleted = true;
                typeof(DungeonLocation).GetField("hasCampedThisFloor", F)!.SetValue(d, true); },
            new[] { "F", "T", "V", "R", "Y", "Q" }),
    };

    internal static async Task<List<string>> RoomLines()
    {
        var lines = new List<string>();
        foreach (var (name, setup, keys) in RoomStates)
        {
            foreach (var key in keys)
            {
                var hero = WebFlagOff200Tests.Hero(false, false);
                hero.Healing = 0;
                var output = new MemoryStream();
                var t = Term(output);
                var (d, floor) = WebFlagOff200Tests.Dungeon(hero, t);
                var room = floor.GetCurrentRoom()!;
                room.Exits.Remove(Direction.North);
                setup(room, d, hero);
                var method = typeof(DungeonLocation).GetMethod("ProcessRoomChoice", F)!;
                string o = await Outcome(() => (Task<bool>)method.Invoke(d, new object[] { key })!, t, output);
                bool inRoom = (bool)typeof(DungeonLocation).GetField("inRoomMode", F)!.GetValue(d)!;
                lines.Add($"room {name} {key} => {o} :: inRoom={inRoom} at={floor.CurrentRoomId} cleared={room.IsCleared} looted={room.TreasureLooted} event={room.EventCompleted}");
            }
        }
        return lines;
    }

    private static async Task<string> AllLines()
    {
        bool pacing = Pacing.Disabled;
        string lang = GameConfig.Language;
        var saved = UsurperRemake.Server.SessionContext.Current;
        try
        {
            Pacing.Disabled = true;
            GameConfig.Language = "en";
            UsurperRemake.Server.SessionContext.Current = null;
            var lines = new List<string>();
            lines.AddRange(await StreetLines(false));
            lines.AddRange(await StreetLines(true));
            lines.AddRange(await RoomLines());
            return string.Join("\n", lines) + "\n";
        }
        finally
        {
            Pacing.Disabled = pacing;
            GameConfig.Language = lang;
            UsurperRemake.Server.SessionContext.Current = saved;
        }
    }

    [Fact]
    public async Task EveryKey_DoesWhatItDidBeforeTheChange()
    {
        string text = await AllLines();
        string? capture = Environment.GetEnvironmentVariable("USURPER_WEB200_CAPTURE");
        if (!string.IsNullOrEmpty(capture))
        {
            Directory.CreateDirectory(capture);
            File.WriteAllText(Path.Combine(capture, "keys.txt"), text);
            return;
        }
        var expected = File.ReadAllText(Path.Combine(RepoRoot(), "Tests", "Fixtures", "web200", "keys.txt")).Split('\n');
        var actual = text.Split('\n');
        actual.Should().Equal(expected, "each key's outcome must match the outcome from before the change");
    }
}
