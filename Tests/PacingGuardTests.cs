using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// Guards the pacing rule: a presentation pause goes through Pacing.Wait so the test run can skip it.
/// A bare literal Task.Delay is allowed only where the wait is functional (input poll, socket retry,
/// drain before a read, wait before exit). Each allowed file lists how many there are.
///
/// Limit: rule 2 is a text scan for an assignment of Pacing.Disabled. Setting the internal property by
/// reflection would not trip it; nothing under Scripts does that.
/// </summary>
public class PacingGuardTests
{
    private static readonly Regex BareDelay = new(@"await (System\.Threading\.Tasks\.)?Task\.Delay\(\s*[0-9_]+\s*\)\s*;", RegexOptions.Compiled);
    private static readonly Regex DisabledAssign = new(@"Pacing\s*\.\s*Disabled\s*=(?!=)", RegexOptions.Compiled);
    private static readonly Regex DisabledAssignInside = new(@"(?<![\w.])Disabled\s*=(?![=>])", RegexOptions.Compiled);

    // Functional waits, by repo relative path with forward slashes. Scripts/Server/** is allowed as a folder.
    private static readonly Dictionary<string, int> Allowed = new()
    {
        ["Scripts/BBS/SocketTerminal.cs"] = 2,          // socket would-block retry
        ["Scripts/Systems/OnlineStateManager.cs"] = 1,  // roster lock poll
        ["Scripts/Systems/OnlinePlaySystem.cs"] = 5,    // relay drain and connect waits
        ["Scripts/Systems/CombatEngine.cs"] = 1,        // skippable pause, input poll with elapsed counter
        ["Scripts/Core/GameEngine.cs"] = 1,             // wait for the world sim to stop
        ["Scripts/Systems/OnlineAdminConsole.cs"] = 1,  // input drain
        ["Scripts/UI/TerminalEmulator.cs"] = 1,         // before Environment.Exit
        ["Scripts/Locations/BaseLocation.cs"] = 1,      // Electron settings flush
    };

    private const string ServerFolder = "Scripts/Server/";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    private static IEnumerable<(string Rel, string Text)> ScriptFiles()
    {
        string root = RepoRoot();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            yield return (rel, File.ReadAllText(path));
        }
    }

    [Fact]
    public void BareLiteralDelays_OnlyWhereListed()
    {
        var problems = new List<string>();
        foreach (var (rel, text) in ScriptFiles())
        {
            if (rel.StartsWith(ServerFolder, StringComparison.Ordinal)) continue;
            int found = BareDelay.Matches(text).Count;
            int allowed = Allowed.TryGetValue(rel, out var n) ? n : 0;
            if (found != allowed)
                problems.Add($"{rel}: {found} bare literal Task.Delay, allowed {allowed}");
        }
        foreach (var listed in Allowed.Keys)
            File.Exists(Path.Combine(RepoRoot(), listed)).Should().BeTrue($"the allowlist names {listed}");
        problems.Should().BeEmpty("a presentation pause must use Pacing.Wait; list a file only for a functional wait");
    }

    [Fact]
    public void NoScriptAssignsPacingDisabled()
    {
        var offenders = new List<string>();
        foreach (var (rel, text) in ScriptFiles())
        {
            if (DisabledAssign.IsMatch(text)) offenders.Add(rel);
            if (rel == "Scripts/Core/Pacing.cs" && DisabledAssignInside.IsMatch(text)) offenders.Add(rel);
        }
        offenders.Should().BeEmpty("only the test project may set Pacing.Disabled");
    }

    [Fact]
    public void PacingIsDisabledInTheTestRun()
    {
        Pacing.Disabled.Should().BeTrue("Tests/TestPacing.cs sets it in a module initializer");
    }
}
