using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UsurperRemake.BBS;
using UsurperRemake.Systems;

/// <summary>
/// Bug reporting system that collects diagnostic info and submits reports.
/// - BBS users: saves locally + posts to Discord webhook
/// - Local/Steam users: saves locally + posts to Discord + opens browser to GitHub Issues
/// </summary>
public static class BugReportSystem
{
    private const string GitHubIssuesUrl = "https://github.com/binary-knight/usurper-reborn/issues/new";
    private const string DiscordWebhookUrl = "https://discord.com/api/webhooks/1470239063574315102/YxbnkRF5VGRWSqS4xUZBtyuL9BjtsglHCnV4_L57QE3RySFySOMAtf3Pe3TrFOINkDDu";

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Generate a bug report, save locally, post to Discord, and optionally open browser.
    /// </summary>
    public static async Task ReportBug(TerminalEmulator terminal, Character? player)
    {
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("═══════════════════════════════════════════════════════════");
        terminal.WriteLine("                         BUG REPORT");
        terminal.WriteLine("═══════════════════════════════════════════════════════════");
        terminal.SetColor("white");
        terminal.WriteLine("");
        terminal.WriteLine("Describe the bug briefly. What happened? What did you expect?");
        terminal.WriteLine("");

        // Get bug description from user
        terminal.SetColor("cyan");
        var description = await terminal.GetInputAsync("> ");

        if (string.IsNullOrWhiteSpace(description))
        {
            terminal.WriteLine("Bug report cancelled.", "yellow");
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine("");
        terminal.WriteLine("Collecting diagnostic information...", "gray");

        // Collect diagnostic info
        var diagnostics = CollectDiagnostics(player);

        // Always save locally
        string localPath = SaveReportLocally(description, diagnostics);

        // Post to Discord webhook
        bool discordSent = await PostToDiscord(description, diagnostics);

        // Show results
        terminal.WriteLine("");
        if (localPath != null)
        {
            terminal.WriteLine($"Report saved to: bug_reports/", "green");
        }

        if (discordSent)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("Report sent to the developer! Thank you for the feedback.");
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("Could not send report online (no internet?).");
            if (localPath != null)
            {
                terminal.WriteLine("Your report was saved locally - the SysOp can forward it.");
            }
        }

        // For non-BBS users, also try browser
        if (!DoorMode.IsInDoorMode)
        {
            var issueBody = BuildIssueBody(description, diagnostics);
            var title = TruncateForTitle(description);
            var url = BuildGitHubIssueUrl(title, issueBody);
            TryCopyToClipboard(issueBody);
            TryOpenBrowser(url);
        }

        terminal.SetColor("white");
        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Save the bug report to a local file in the bug_reports/ directory.
    /// </summary>
    private static string SaveReportLocally(string description, DiagnosticInfo info)
    {
        try
        {
            var reportsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bug_reports");
            Directory.CreateDirectory(reportsDir);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var playerName = info.PlayerName?.Replace(" ", "_") ?? "unknown";
            var filename = $"bug_{timestamp}_{playerName}.txt";
            var filepath = Path.Combine(reportsDir, filename);

            var sb = new StringBuilder();
            sb.AppendLine("=== USURPER REBORN BUG REPORT ===");
            sb.AppendLine($"Date: {info.Timestamp}");
            sb.AppendLine($"Version: {info.GameVersion} ({info.VersionName})");
            sb.AppendLine($"Platform: {info.Platform}");
            sb.AppendLine($"Build: {(info.IsSteamBuild ? "Steam" : info.IsBBSDoor ? "BBS Door" : "Standard")}");
            if (info.IsBBSDoor)
            {
                sb.AppendLine($"BBS: {info.BBSName}");
                sb.AppendLine($"User: {info.PlayerName}");
            }
            sb.AppendLine();
            sb.AppendLine("=== DESCRIPTION ===");
            sb.AppendLine(description);
            sb.AppendLine();
            if (info.PlayerLevel > 0)
            {
                sb.AppendLine("=== CHARACTER ===");
                sb.AppendLine($"Level {info.PlayerLevel} {info.PlayerRace} {info.PlayerClass}");
                sb.AppendLine($"HP: {info.CurrentHP}/{info.MaxHP}");
                sb.AppendLine($"Location: {info.CurrentLocation}");
                if (info.DungeonFloor > 0)
                    sb.AppendLine($"Dungeon Floor: {info.DungeonFloor}");
                sb.AppendLine($"Play Time: {info.TotalPlayTime}");
                sb.AppendLine();
            }
            if (!string.IsNullOrEmpty(info.RecentLogEntries))
            {
                sb.AppendLine("=== RECENT DEBUG LOG ===");
                sb.AppendLine(info.RecentLogEntries);
            }

            File.WriteAllText(filepath, sb.ToString());
            DebugLogger.Instance?.LogInfo("BUG_REPORT", $"Bug report saved to {filepath}");
            return filepath;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("BUG_REPORT", $"Failed to save bug report locally: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Post the bug report to a Discord webhook channel.
    /// </summary>
    private static async Task<bool> PostToDiscord(string description, DiagnosticInfo info)
    {
        try
        {
            var sb = new StringBuilder();

            // Discord embed-style formatting
            sb.AppendLine("**Bug Report** from Usurper Reborn");
            sb.AppendLine("```");
            sb.AppendLine($"Version:  {info.GameVersion}");
            sb.AppendLine($"Platform: {info.Platform}");
            sb.AppendLine($"Build:    {(info.IsSteamBuild ? "Steam" : info.IsBBSDoor ? "BBS Door" : "Standard")}");
            if (info.IsBBSDoor)
            {
                sb.AppendLine($"BBS:      {info.BBSName}");
                sb.AppendLine($"User:     {info.PlayerName}");
            }
            if (info.PlayerLevel > 0)
            {
                sb.AppendLine($"Player:   Lv.{info.PlayerLevel} {info.PlayerRace} {info.PlayerClass}");
                sb.AppendLine($"Location: {info.CurrentLocation}{(info.DungeonFloor > 0 ? $" (Floor {info.DungeonFloor})" : "")}");
            }
            sb.AppendLine("```");
            sb.AppendLine($"> {description}");

            // Add recent log if available (truncated for Discord's 2000 char limit)
            if (!string.IsNullOrEmpty(info.RecentLogEntries))
            {
                var logPreview = info.RecentLogEntries;
                // Discord message limit is 2000 chars, leave room for the rest
                int maxLogLength = 1500 - sb.Length;
                if (maxLogLength > 100 && logPreview.Length > 0)
                {
                    if (logPreview.Length > maxLogLength)
                        logPreview = logPreview.Substring(logPreview.Length - maxLogLength);
                    sb.AppendLine("```");
                    sb.AppendLine(logPreview);
                    sb.AppendLine("```");
                }
            }

            // Truncate to Discord's 2000 char limit
            var content = sb.ToString();
            if (content.Length > 1990)
                content = content.Substring(0, 1987) + "...";

            var payload = JsonSerializer.Serialize(new { content });

            var httpContent = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(DiscordWebhookUrl, httpContent);

            if (response.IsSuccessStatusCode)
            {
                DebugLogger.Instance?.LogInfo("BUG_REPORT", "Bug report sent to Discord successfully");
                return true;
            }
            else
            {
                DebugLogger.Instance?.LogWarning("BUG_REPORT", $"Discord webhook returned {response.StatusCode}");
                return false;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogWarning("BUG_REPORT", $"Failed to send to Discord: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Collect diagnostic information about the game state.
    /// </summary>
    private static DiagnosticInfo CollectDiagnostics(Character? player)
    {
        var info = new DiagnosticInfo
        {
            GameVersion = GameConfig.Version,
            VersionName = GameConfig.VersionName,
            Platform = GetPlatformString(),
            OSVersion = Environment.OSVersion.ToString(),
            DotNetVersion = Environment.Version.ToString(),
            Is64Bit = Environment.Is64BitProcess,
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            IsSteamBuild = IsSteamBuild(),
            IsBBSDoor = DoorMode.IsInDoorMode,
            BBSName = DoorMode.SessionInfo?.BBSName ?? "",
            PlayerName = DoorMode.IsInDoorMode
                ? DoorMode.GetPlayerName()
                : player?.Name ?? "Unknown"
        };

        if (player != null)
        {
            info.PlayerLevel = player.Level;
            info.PlayerClass = player.Class.ToString();
            info.PlayerRace = player.Race.ToString();
            info.CurrentHP = player.HP;
            info.MaxHP = player.MaxHP;
            info.CurrentLocation = ((GameLocation)player.Location).ToString();
            info.DungeonFloor = GetDungeonFloor(player);
            info.TotalPlayTime = player.Statistics?.GetFormattedPlayTime() ?? "Unknown";
        }

        // Get recent debug log entries
        info.RecentLogEntries = GetRecentLogEntries(20);

        return info;
    }

    /// <summary>
    /// Build the issue body with all diagnostic info (for GitHub Issues).
    /// </summary>
    private static string BuildIssueBody(string description, DiagnosticInfo info)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Bug Description");
        if (!string.IsNullOrWhiteSpace(description))
        {
            sb.AppendLine(description);
        }
        else
        {
            sb.AppendLine("_Please describe what happened and what you expected to happen._");
        }
        sb.AppendLine();

        sb.AppendLine("## Steps to Reproduce");
        sb.AppendLine("1. ");
        sb.AppendLine("2. ");
        sb.AppendLine("3. ");
        sb.AppendLine();

        sb.AppendLine("## Game State");
        sb.AppendLine("| Property | Value |");
        sb.AppendLine("|----------|-------|");
        sb.AppendLine($"| Version | {info.GameVersion} ({info.VersionName}) |");
        sb.AppendLine($"| Build Type | {(info.IsSteamBuild ? "Steam" : info.IsBBSDoor ? "BBS Door" : "Standard")} |");
        sb.AppendLine($"| Platform | {info.Platform} |");
        sb.AppendLine($"| OS | {info.OSVersion} |");
        if (info.IsBBSDoor)
        {
            sb.AppendLine($"| BBS | {info.BBSName} |");
            sb.AppendLine($"| BBS User | {info.PlayerName} |");
        }
        if (info.PlayerLevel > 0)
        {
            sb.AppendLine($"| Character | Level {info.PlayerLevel} {info.PlayerRace} {info.PlayerClass} |");
            sb.AppendLine($"| HP | {info.CurrentHP}/{info.MaxHP} |");
            sb.AppendLine($"| Location | {info.CurrentLocation} |");
            if (info.DungeonFloor > 0)
            {
                sb.AppendLine($"| Dungeon Floor | {info.DungeonFloor} |");
            }
            sb.AppendLine($"| Play Time | {info.TotalPlayTime} |");
        }
        sb.AppendLine();

        if (!string.IsNullOrEmpty(info.RecentLogEntries))
        {
            sb.AppendLine("<details>");
            sb.AppendLine("<summary>Recent Debug Log (click to expand)</summary>");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(info.RecentLogEntries);
            sb.AppendLine("```");
            sb.AppendLine("</details>");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine($"_Reported via in-game bug reporter at {info.Timestamp}_");

        return sb.ToString();
    }

    /// <summary>
    /// Build the GitHub issue URL with pre-filled title and body.
    /// </summary>
    private static string BuildGitHubIssueUrl(string title, string body)
    {
        var encodedTitle = Uri.EscapeDataString(title);
        var encodedBody = Uri.EscapeDataString(body);

        var url = $"{GitHubIssuesUrl}?title={encodedTitle}&body={encodedBody}&labels=bug,in-game-report";

        if (url.Length > 8000)
        {
            var truncatedBody = body.Substring(0, Math.Min(body.Length, 3000)) + "\n\n_[Log truncated due to URL length]_";
            encodedBody = Uri.EscapeDataString(truncatedBody);
            url = $"{GitHubIssuesUrl}?title={encodedTitle}&body={encodedBody}&labels=bug,in-game-report";
        }

        return url;
    }

    /// <summary>
    /// Try to open the URL in the default browser.
    /// </summary>
    private static bool TryOpenBrowser(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = $"/c start \"\" \"{url}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                return true;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
                return true;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
                return true;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("BUG_REPORT", $"Failed to open browser: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Try to copy text to clipboard.
    /// </summary>
    private static bool TryCopyToClipboard(string text)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd",
                        Arguments = "/c clip",
                        RedirectStandardInput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.StandardInput.Write(text);
                process.StandardInput.Close();
                process.WaitForExit(2000);
                return true;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "xclip",
                            Arguments = "-selection clipboard",
                            RedirectStandardInput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    process.StandardInput.Write(text);
                    process.StandardInput.Close();
                    process.WaitForExit(2000);
                    return true;
                }
                catch { }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "pbcopy",
                        RedirectStandardInput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.StandardInput.Write(text);
                process.StandardInput.Close();
                process.WaitForExit(2000);
                return true;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogWarning("BUG_REPORT", $"Failed to copy to clipboard: {ex.Message}");
        }

        return false;
    }

    private static string GetPlatformString()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return $"Windows {(Environment.Is64BitProcess ? "x64" : "x86")}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return $"Linux {RuntimeInformation.OSArchitecture}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return $"macOS {RuntimeInformation.OSArchitecture}";
        return "Unknown";
    }

    private static bool IsSteamBuild()
    {
#if STEAM_BUILD
        return true;
#else
        return false;
#endif
    }

    private static int GetDungeonFloor(Character? player)
    {
        try
        {
            if (player != null && (GameLocation)player.Location == GameLocation.Dungeons)
            {
                return player.Statistics?.DeepestDungeonLevel ?? 0;
            }
        }
        catch { }
        return 0;
    }

    private static string GetRecentLogEntries(int lineCount)
    {
        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "debug.log");
            if (File.Exists(logPath))
            {
                var lines = File.ReadAllLines(logPath);
                var startIndex = Math.Max(0, lines.Length - lineCount);
                var recentLines = new string[Math.Min(lineCount, lines.Length)];
                Array.Copy(lines, startIndex, recentLines, 0, recentLines.Length);
                return string.Join("\n", recentLines);
            }
        }
        catch { }
        return "";
    }

    private static string TruncateForTitle(string text)
    {
        var cleaned = text.Replace("\n", " ").Replace("\r", " ").Trim();
        if (cleaned.Length > 80)
        {
            cleaned = cleaned.Substring(0, 77) + "...";
        }
        return $"[Bug] {cleaned}";
    }

    private class DiagnosticInfo
    {
        public string GameVersion { get; set; } = "";
        public string VersionName { get; set; } = "";
        public string Platform { get; set; } = "";
        public string OSVersion { get; set; } = "";
        public string DotNetVersion { get; set; } = "";
        public bool Is64Bit { get; set; }
        public bool IsSteamBuild { get; set; }
        public bool IsBBSDoor { get; set; }
        public string BBSName { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public int PlayerLevel { get; set; }
        public string PlayerClass { get; set; } = "";
        public string PlayerRace { get; set; } = "";
        public long CurrentHP { get; set; }
        public long MaxHP { get; set; }
        public string CurrentLocation { get; set; } = "";
        public int DungeonFloor { get; set; }
        public string TotalPlayTime { get; set; } = "";
        public string RecentLogEntries { get; set; } = "";
    }
}
