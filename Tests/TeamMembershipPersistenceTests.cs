using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace UsurperRemake.Tests
{
    /// <summary>
    /// Player report (online): "friend says he sees me as part of the team, when I
    /// check it says I'm the only one in the team, tried remaking team and neither of
    /// us could see the other."
    ///
    /// Player team membership is stored in each player's own save blob
    /// (player_data.player.team), and the roster screen builds its list by querying
    /// that column across all players. Joining or creating a team only set
    /// Character.Team in memory, so until that player's next autosave every OTHER
    /// player's roster query still read their previous value. Each side saw a
    /// different team, and remaking the team reproduced the same gap.
    ///
    /// Confirmed against production: both players had identical team strings in the
    /// database by the time it was inspected, and running the roster query by hand
    /// returned both rows -- the data converged once autosaves landed. The bug was
    /// purely the window before that.
    ///
    /// This is a structural test because the failure is invisible to behavioural
    /// tests: nothing throws, nothing is corrupted, the rows just disagree for a while.
    /// </summary>
    public class TeamMembershipPersistenceTests
    {
        private static string SourceOf(params string[] relativePath)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj")))
                dir = dir.Parent;
            dir.Should().NotBeNull("the test must be able to find the repo root");
            return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(relativePath).ToArray()));
        }

        private static string[] TeamCornerLines() =>
            SourceOf("Scripts", "Locations", "TeamCornerLocation.cs")
                .Replace("\r", "")
                .Split('\n');

        [Fact]
        public void EveryTeamMembershipChange_IsPersistedImmediately()
        {
            var lines = TeamCornerLines();

            // Each site that reassigns the player's team must persist within a few
            // lines, or other players keep reading the stale row.
            var mutations = Enumerable.Range(0, lines.Length)
                .Where(i => lines[i].Contains("currentPlayer.Team = "))
                .ToList();

            mutations.Should().NotBeEmpty("the team join/create/leave sites must still exist");

            foreach (int i in mutations)
            {
                string window = string.Join(" ", lines.Skip(i).Take(12));
                window.Should().Contain("PersistTeamMembershipChange",
                    $"the team assignment at line {i + 1} (\"{lines[i].Trim()}\") must be persisted " +
                    "immediately -- otherwise other players' roster queries read the previous value " +
                    "until this player's next autosave, and the two sides disagree about who is on the team");
            }
        }

        [Fact]
        public void ThePersistenceHelperExists_AndIsOnlineGated()
        {
            var src = SourceOf("Scripts", "Locations", "TeamCornerLocation.cs");

            int at = src.IndexOf("private async Task PersistTeamMembershipChange(", StringComparison.Ordinal);
            at.Should().BeGreaterThan(0, "the shared persistence helper must exist");

            string body = src.Substring(at, Math.Min(1200, src.Length - at));
            body.Should().Contain("IsOnlineMode",
                "single-player builds its roster from memory and needs no immediate write");
            body.Should().Contain("SaveCurrentGame",
                "the player's own save row is what the roster query reads");
            body.Should().Contain("catch",
                "a failed save must not break the join flow the player just completed");
        }

        [Fact]
        public void RosterExclusion_IsNamedForWhatItActuallyMatches()
        {
            // The roster query compares the exclude argument against display_name.
            // While it was named "excludeUsername", the next caller to pass a real
            // account key would silently fail to exclude the viewer -- which is
            // exactly the defect behind the v0.57.7 "you can fight yourself in the
            // arena" bug. The name is the guard rail here.
            var src = SourceOf("Scripts", "Systems", "SqlSaveBackend.cs");

            int at = src.IndexOf("GetPlayerTeamMembers(string teamName", StringComparison.Ordinal);
            at.Should().BeGreaterThan(0);

            string signature = src.Substring(at, Math.Min(200, src.Length - at));
            signature.Should().Contain("excludeDisplayName",
                "the parameter is matched against display_name, so it must not be called a username");
            signature.Should().NotContain("excludeUsername");
        }
    }
}
