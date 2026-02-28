using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Checks GitHub for newer versions of the game and notifies the player.
    /// </summary>
    public class VersionChecker
    {
        private static VersionChecker? _instance;
        public static VersionChecker Instance => _instance ??= new VersionChecker();

        // GitHub API endpoint for latest release
        private const string GitHubApiUrl = "https://api.github.com/repos/binary-knight/usurper-reborn/releases/latest";
        private const string GitHubReleasesUrl = "https://github.com/binary-knight/usurper-reborn/releases/latest";

        // Cache file to avoid checking too frequently
        private readonly string cacheFilePath;
        private const int CacheHours = 4; // Only check every 4 hours

        // Results from last check
        public bool NewVersionAvailable { get; private set; }
        public string LatestVersion { get; private set; } = "";
        public string CurrentVersion => GameConfig.Version;
        public string ReleaseUrl { get; private set; } = GitHubReleasesUrl;
        public string ReleaseNotes { get; private set; } = "";
        public bool CheckCompleted { get; private set; }
        public bool CheckFailed { get; private set; }
        public string CheckFailedReason { get; private set; } = "";

        // Auto-update properties
        public bool IsDownloading { get; private set; }
        public int DownloadProgress { get; private set; }
        public string DownloadError { get; private set; } = "";
        private List<GitHubAsset> releaseAssets = new List<GitHubAsset>();

        /// <summary>
        /// Detects if the game was launched via Steam.
        /// Steam handles its own updates, so we skip the GitHub update check.
        /// </summary>
        public bool IsSteamBuild
        {
            get
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;

                // Check for steam_appid.txt (created by Steam or placed for Steamworks)
                if (File.Exists(Path.Combine(baseDir, "steam_appid.txt")))
                    return true;

                // Check for Steam API DLLs (indicates Steamworks SDK integration)
                if (File.Exists(Path.Combine(baseDir, "steam_api64.dll")) ||
                    File.Exists(Path.Combine(baseDir, "steam_api.dll")) ||
                    File.Exists(Path.Combine(baseDir, "libsteam_api.so")) ||
                    File.Exists(Path.Combine(baseDir, "libsteam_api.dylib")))
                    return true;

                // Check for Steam environment variable (set when launched from Steam)
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SteamAppId")))
                    return true;

                return false;
            }
        }

        public VersionChecker()
        {
            _instance = this;
            cacheFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version_cache.json");
        }

        /// <summary>
        /// Check for updates asynchronously. Safe to call - will not throw.
        /// </summary>
        public async Task CheckForUpdatesAsync(bool forceCheck = false)
        {
            // Skip update check for Steam builds - Steam handles updates
            if (IsSteamBuild)
            {
                DebugLogger.Instance.LogInfo("UPDATE", "Skipping version check - Steam build detected (Steam handles updates)");
                CheckCompleted = true;
                return;
            }

            // Skip update check for online server mode unless SysOp forces it
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && !forceCheck)
            {
                DebugLogger.Instance.LogInfo("UPDATE", "Skipping version check - online server mode (use SysOp console to check manually)");
                CheckCompleted = true;
                return;
            }

            DebugLogger.Instance.LogInfo("UPDATE", $"Version check started (current: {CurrentVersion}, force: {forceCheck})");

            try
            {
                // Check if we should skip due to cache (unless forced)
                if (!forceCheck && ShouldSkipCheck())
                {
                    DebugLogger.Instance.LogDebug("UPDATE", "Skipping version check (cached)");
                    CheckCompleted = true;
                    return;
                }

                DebugLogger.Instance.LogInfo("UPDATE", $"Fetching from GitHub API: {GitHubApiUrl}");

                // Force TLS 1.2+ — required by GitHub API, not always default on 32-bit Windows
                var handler = new System.Net.Http.HttpClientHandler();
                try
                {
                    handler.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                                           System.Security.Authentication.SslProtocols.Tls13;
                }
                catch { /* SslProtocols may be restricted on some platforms — proceed with defaults */ }

                using var client = new HttpClient(handler);
                client.DefaultRequestHeaders.Add("User-Agent", $"UsurperReborn/{GameConfig.Version}");
                client.Timeout = TimeSpan.FromSeconds(15);

                var response = await client.GetStringAsync(GitHubApiUrl);
                var release = JsonSerializer.Deserialize<GitHubRelease>(response);

                if (release != null && !string.IsNullOrEmpty(release.tag_name))
                {
                    LatestVersion = release.tag_name.TrimStart('v', 'V');
                    ReleaseUrl = release.html_url ?? GitHubReleasesUrl;
                    ReleaseNotes = release.body ?? "";
                    releaseAssets = release.assets ?? new List<GitHubAsset>();

                    DebugLogger.Instance.LogInfo("UPDATE", $"GitHub response: tag={release.tag_name}, assets={releaseAssets.Count}");

                    // Compare versions
                    NewVersionAvailable = IsNewerVersion(LatestVersion, CurrentVersion);

                    if (NewVersionAvailable)
                    {
                        DebugLogger.Instance.LogInfo("UPDATE", $"New version available: {LatestVersion} (current: {CurrentVersion})");
                        var platformAsset = GetPlatformAsset();
                        DebugLogger.Instance.LogInfo("UPDATE", $"Platform asset found: {(platformAsset != null ? platformAsset.name : "NONE")}");
                    }
                    else
                    {
                        DebugLogger.Instance.LogDebug("UPDATE", $"Running latest version: {CurrentVersion}");
                    }

                    // Save cache
                    SaveCache();
                }
                else
                {
                    DebugLogger.Instance.LogWarning("UPDATE", "GitHub response was null or had no tag_name");
                }

                CheckCompleted = true;
            }
            catch (HttpRequestException ex)
            {
                DebugLogger.Instance.LogDebug("UPDATE", $"Could not check for updates (offline?): {ex.Message}");
                CheckFailed = true;
                CheckFailedReason = ex.Message;
                CheckCompleted = true;
            }
            catch (TaskCanceledException)
            {
                DebugLogger.Instance.LogDebug("UPDATE", "Version check timed out");
                CheckFailed = true;
                CheckFailedReason = "Request timed out (15s)";
                CheckCompleted = true;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("UPDATE", $"Version check failed: {ex.Message}");
                CheckFailed = true;
                CheckFailedReason = ex.Message;
                CheckCompleted = true;
            }
        }

        /// <summary>
        /// Compare two version strings (e.g., "0.12.0-alpha" vs "0.11.0-alpha")
        /// Returns true if latestVersion is newer than currentVersion
        /// </summary>
        private bool IsNewerVersion(string latestVersion, string currentVersion)
        {
            try
            {
                // Strip -alpha, -beta, etc. suffixes for comparison
                var latest = StripSuffix(latestVersion);
                var current = StripSuffix(currentVersion);

                var latestParts = latest.Split('.');
                var currentParts = current.Split('.');

                for (int i = 0; i < Math.Max(latestParts.Length, currentParts.Length); i++)
                {
                    int latestNum = i < latestParts.Length && int.TryParse(latestParts[i], out int ln) ? ln : 0;
                    int currentNum = i < currentParts.Length && int.TryParse(currentParts[i], out int cn) ? cn : 0;

                    if (latestNum > currentNum) return true;
                    if (latestNum < currentNum) return false;
                }

                return false; // Versions are equal
            }
            catch
            {
                // If parsing fails, assume no update needed
                return false;
            }
        }

        private string StripSuffix(string version)
        {
            var dashIndex = version.IndexOf('-');
            return dashIndex > 0 ? version.Substring(0, dashIndex) : version;
        }

        /// <summary>
        /// Check if we should skip the version check due to recent cache
        /// </summary>
        private bool ShouldSkipCheck()
        {
            try
            {
                if (!File.Exists(cacheFilePath))
                    return false;

                var cacheJson = File.ReadAllText(cacheFilePath);
                var cache = JsonSerializer.Deserialize<VersionCache>(cacheJson);

                if (cache == null)
                    return false;

                // Invalidate cache if our version changed (e.g., user updated the game)
                if (cache.CurrentVersion != CurrentVersion)
                {
                    DebugLogger.Instance.LogDebug("UPDATE", $"Cache invalidated - version changed from {cache.CurrentVersion} to {CurrentVersion}");
                    return false;
                }

                // Check if cache is still valid (within time window)
                if (DateTime.Now - cache.LastCheck < TimeSpan.FromHours(CacheHours))
                {
                    // Restore cached results
                    LatestVersion = cache.LatestVersion ?? "";
                    NewVersionAvailable = cache.NewVersionAvailable;
                    ReleaseUrl = cache.ReleaseUrl ?? GitHubReleasesUrl;
                    ReleaseNotes = cache.ReleaseNotes ?? "";
                    releaseAssets = cache.Assets ?? new List<GitHubAsset>();
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Save check results to cache
        /// </summary>
        private void SaveCache()
        {
            try
            {
                var cache = new VersionCache
                {
                    LastCheck = DateTime.Now,
                    CurrentVersion = CurrentVersion,  // Store our version to detect upgrades/downgrades
                    LatestVersion = LatestVersion,
                    NewVersionAvailable = NewVersionAvailable,
                    ReleaseUrl = ReleaseUrl,
                    ReleaseNotes = ReleaseNotes,
                    Assets = releaseAssets
                };

                var json = JsonSerializer.Serialize(cache);
                File.WriteAllText(cacheFilePath, json);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogDebug("UPDATE", $"Could not save version cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Display update notification to terminal
        /// </summary>
        public async Task DisplayUpdateNotification(TerminalEmulator terminal)
        {
            if (!NewVersionAvailable)
                return;

            // Check if running in BBS door mode
            bool isBBSMode = UsurperRemake.BBS.DoorMode.IsInDoorMode;

            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("╔════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                         NEW VERSION AVAILABLE                              ║");
            terminal.WriteLine("╚════════════════════════════════════════════════════════════════════════════╝");
            terminal.SetColor("white");
            terminal.WriteLine("");
            terminal.WriteLine($"  Current version: {CurrentVersion}");
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  Latest version:  {LatestVersion}");
            terminal.SetColor("white");
            terminal.WriteLine("");

            if (isBBSMode)
            {
                // In BBS mode, tell the player to notify their sysop
                terminal.SetColor("cyan");
                terminal.WriteLine("  A newer version of Usurper Reborn is available!");
                terminal.WriteLine("  Please notify your SysOp to update the game.");
                terminal.SetColor("gray");
                terminal.WriteLine("");
                terminal.WriteLine("  SysOp: Download the latest version at:");
                terminal.SetColor("cyan");
                terminal.WriteLine($"  {ReleaseUrl}");
                terminal.SetColor("white");
            }
            else
            {
                terminal.WriteLine("  Download the latest version at:");
                terminal.SetColor("cyan");
                terminal.WriteLine($"  {ReleaseUrl}");
                terminal.SetColor("white");
                terminal.WriteLine("");

                // Show brief release notes if available (only for non-BBS mode)
                if (!string.IsNullOrEmpty(ReleaseNotes))
                {
                    terminal.SetColor("gray");
                    var notes = ReleaseNotes.Length > 200
                        ? ReleaseNotes.Substring(0, 200) + "..."
                        : ReleaseNotes;
                    // Clean up markdown and show first few lines
                    notes = notes.Replace("#", "").Replace("*", "").Replace("\r", "");
                    var lines = notes.Split('\n');
                    terminal.WriteLine("  Release notes:");
                    for (int i = 0; i < Math.Min(lines.Length, 3); i++)
                    {
                        if (!string.IsNullOrWhiteSpace(lines[i]))
                            terminal.WriteLine($"    {lines[i].Trim()}");
                    }
                    terminal.SetColor("white");
                    terminal.WriteLine("");
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.Write("  Press any key to continue...");
            terminal.SetColor("white");
            await terminal.PressAnyKey();
            terminal.WriteLine("");
        }

        /// <summary>
        /// Simple prompt asking if user wants to open the download page
        /// </summary>
        public async Task<bool> PromptForUpdate(TerminalEmulator terminal)
        {
            if (!NewVersionAvailable)
                return false;

            terminal.SetColor("cyan");
            terminal.Write("  Would you like to open the download page? (Y/N): ");
            terminal.SetColor("white");

            var response = await terminal.ReadLineAsync();
            return response?.Trim().ToUpper() == "Y";
        }

        /// <summary>
        /// Open the release URL in the default browser
        /// </summary>
        public void OpenDownloadPage()
        {
            try
            {
                // Cross-platform way to open URL
                var url = ReleaseUrl;

                if (OperatingSystem.IsWindows())
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                else if (OperatingSystem.IsLinux())
                {
                    System.Diagnostics.Process.Start("xdg-open", url);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    System.Diagnostics.Process.Start("open", url);
                }

                DebugLogger.Instance.LogInfo("UPDATE", $"Opened download page: {url}");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("UPDATE", $"Could not open browser: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the appropriate download asset for the current platform
        /// </summary>
        private GitHubAsset? GetPlatformAsset()
        {
            if (releaseAssets == null || releaseAssets.Count == 0)
                return null;

            // Asset naming pattern: UsurperReborn-v0.12-Windows-x64.zip
            string platformPattern;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                platformPattern = RuntimeInformation.OSArchitecture == Architecture.X64 ? "Windows-x64" : "Windows-x86";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                platformPattern = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "Linux-ARM64" : "Linux-x64";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                platformPattern = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "macOS-AppleSilicon" : "macOS-Intel";
            }
            else
            {
                return null;
            }

            // Find the matching asset (looking for .zip file with platform name)
            // Exclude WezTerm/Desktop bundles — they have nested folders that break the updater
            return releaseAssets.FirstOrDefault(a =>
                a.name != null &&
                a.name.Contains(platformPattern, StringComparison.OrdinalIgnoreCase) &&
                a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                !a.name.Contains("WezTerm", StringComparison.OrdinalIgnoreCase) &&
                !a.name.Contains("Desktop", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Check if auto-update is available for this platform
        /// </summary>
        public bool CanAutoUpdate()
        {
            return NewVersionAvailable && GetPlatformAsset() != null;
        }

        /// <summary>
        /// Download and install the update automatically
        /// </summary>
        public async Task<bool> DownloadAndInstallUpdateAsync(Action<int>? progressCallback = null)
        {
            var asset = GetPlatformAsset();
            if (asset == null || string.IsNullOrEmpty(asset.browser_download_url))
            {
                DownloadError = "No compatible update package found for this platform.";
                return false;
            }

            IsDownloading = true;
            DownloadProgress = 0;
            DownloadError = "";

            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var tempDir = Path.Combine(Path.GetTempPath(), $"UsurperUpdate_{Guid.NewGuid():N}");
                var zipPath = Path.Combine(tempDir, "update.zip");
                var extractDir = Path.Combine(tempDir, "extracted");

                Directory.CreateDirectory(tempDir);
                Directory.CreateDirectory(extractDir);

                DebugLogger.Instance.LogInfo("UPDATE", $"Downloading update from: {asset.browser_download_url}");
                DebugLogger.Instance.LogInfo("UPDATE", $"Temp directory: {tempDir}");

                // Download the update
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", $"UsurperReborn/{GameConfig.Version}");
                client.Timeout = TimeSpan.FromMinutes(10); // Allow longer timeout for downloads

                using var response = await client.GetAsync(asset.browser_download_url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var downloadedBytes = 0L;

                using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var downloadStream = await response.Content.ReadAsStreamAsync())
                {
                    var buffer = new byte[81920];
                    int bytesRead;

                    while ((bytesRead = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        downloadedBytes += bytesRead;

                        if (totalBytes > 0)
                        {
                            DownloadProgress = (int)((downloadedBytes * 100) / totalBytes);
                            progressCallback?.Invoke(DownloadProgress);
                        }
                    }
                }

                DebugLogger.Instance.LogInfo("UPDATE", $"Download complete: {downloadedBytes} bytes");
                DownloadProgress = 100;
                progressCallback?.Invoke(100);

                // Extract the update
                DebugLogger.Instance.LogInfo("UPDATE", "Extracting update...");
                ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

                // If the ZIP had a single nested folder (e.g., UsurperReborn-win64/), flatten it
                extractDir = FlattenExtractedDirectory(extractDir);

                // Create the updater script (skip relaunch in BBS door mode — BBS handles restarts)
                var updaterPath = CreateUpdaterScript(appDir, extractDir, tempDir, BBS.DoorMode.IsInDoorMode);
                if (string.IsNullOrEmpty(updaterPath))
                {
                    DownloadError = "Failed to create updater script.";
                    return false;
                }

                DebugLogger.Instance.LogInfo("UPDATE", $"Updater script created: {updaterPath}");

                // Launch the updater and exit
                LaunchUpdater(updaterPath);
                return true;
            }
            catch (Exception ex)
            {
                DownloadError = $"Download failed: {ex.Message}";
                DebugLogger.Instance.LogWarning("UPDATE", $"Auto-update failed: {ex.Message}");
                return false;
            }
            finally
            {
                IsDownloading = false;
            }
        }

        /// <summary>
        /// If the extracted ZIP contains a single subdirectory with all the files, return that
        /// subdirectory path instead. This handles ZIPs that wrap everything in a folder.
        /// </summary>
        private string FlattenExtractedDirectory(string extractDir)
        {
            try
            {
                var entries = Directory.GetFileSystemEntries(extractDir);
                if (entries.Length == 1 && Directory.Exists(entries[0]))
                {
                    // Single subdirectory — check if it contains the actual game files
                    var subDir = entries[0];
                    var subFiles = Directory.GetFiles(subDir);
                    if (subFiles.Any(f => Path.GetFileName(f).Equals("UsurperReborn.exe", StringComparison.OrdinalIgnoreCase) ||
                                          Path.GetFileName(f).Equals("UsurperReborn", StringComparison.OrdinalIgnoreCase) ||
                                          Path.GetFileName(f).Equals("UsurperReborn.dll", StringComparison.OrdinalIgnoreCase)))
                    {
                        DebugLogger.Instance.LogInfo("UPDATE", $"Flattened nested directory: {Path.GetFileName(subDir)}");
                        return subDir;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("UPDATE", $"Error checking for nested directory: {ex.Message}");
            }
            return extractDir;
        }

        /// <summary>
        /// Create a platform-specific updater script
        /// </summary>
        private string? CreateUpdaterScript(string appDir, string extractDir, string tempDir, bool skipRelaunch = false)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return CreateWindowsUpdater(appDir, extractDir, tempDir, skipRelaunch);
                }
                else
                {
                    return CreateUnixUpdater(appDir, extractDir, tempDir, skipRelaunch);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("UPDATE", $"Failed to create updater: {ex.Message}");
                return null;
            }
        }

        private string CreateWindowsUpdater(string appDir, string extractDir, string tempDir, bool skipRelaunch = false)
        {
            var updaterPath = Path.Combine(tempDir, "updater.bat");
            var exeName = "UsurperReborn.exe";
            var exePath = Path.Combine(appDir, exeName);

            var relaunchSection = skipRelaunch
                ? @"echo.
echo Update complete! The BBS will launch the new version on next connection."
                : $@"echo.
echo Update complete! Starting game...
start """" ""{exePath}""";

            var logFile = Path.Combine(appDir, "update.log");

            var script = $@"@echo off
echo Usurper Reborn Auto-Updater
echo ===========================
echo.
echo Waiting for game to close...
echo [%date% %time%] Updater started > ""{logFile}""
echo [%date% %time%] Source: {extractDir} >> ""{logFile}""
echo [%date% %time%] Target: {appDir} >> ""{logFile}""
timeout /t 3 /nobreak >nul

:waitloop
tasklist /FI ""IMAGENAME eq {exeName}"" 2>nul | find /I ""{exeName}"" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto waitloop
)

echo [%date% %time%] Game process exited >> ""{logFile}""
echo.
echo Installing update...

REM Try xcopy with retries for transient file locks
set RETRY=0
:copyretry
xcopy /E /Y /Q ""{extractDir}\*"" ""{appDir}""
if errorlevel 1 (
    set /A RETRY+=1
    echo [%date% %time%] xcopy attempt %RETRY% failed >> ""{logFile}""
    if %RETRY% LSS 3 (
        echo Retry %RETRY%/3 - waiting for file locks to release...
        timeout /t 2 /nobreak >nul
        goto copyretry
    )
    echo.
    echo ERROR: Failed to copy update files after 3 attempts.
    echo Please download the update manually from:
    echo {ReleaseUrl}
    echo [%date% %time%] FAILED after 3 attempts >> ""{logFile}""
    pause
    goto cleanup
)

echo [%date% %time%] Files copied successfully >> ""{logFile}""

{relaunchSection}

:cleanup
echo.
echo Cleaning up...
rd /S /Q ""{tempDir}"" 2>nul
";

            File.WriteAllText(updaterPath, script);
            return updaterPath;
        }

        private string CreateUnixUpdater(string appDir, string extractDir, string tempDir, bool skipRelaunch = false)
        {
            var updaterPath = Path.Combine(tempDir, "updater.sh");
            var exeName = "UsurperReborn";
            var exePath = Path.Combine(appDir, exeName);

            // Build script with explicit \n to avoid CRLF issues when compiled on Windows.
            // C# verbatim strings embed the source file's line endings (CRLF on Windows),
            // which causes "bad interpreter" errors when bash tries to run the script on Linux.
            var logFile = Path.Combine(appDir, "update.log");

            var lines = new List<string>
            {
                "#!/bin/bash",
                $"LOGFILE=\"{logFile}\"",
                "echo \"Usurper Reborn Auto-Updater\"",
                "echo \"===========================\"",
                "echo \"\"",
                "echo \"Waiting for game to close...\"",
                "echo \"[$(date)] Updater started\" > \"$LOGFILE\"",
                $"echo \"[$(date)] Source: {extractDir}\" >> \"$LOGFILE\"",
                $"echo \"[$(date)] Target: {appDir}\" >> \"$LOGFILE\"",
                "sleep 3",
                "",
                "# Wait for any running instances to close",
                $"while pgrep -f \"{exeName}\" > /dev/null 2>&1; do",
                "    sleep 1",
                "done",
                "",
                "echo \"[$(date)] Game process exited\" >> \"$LOGFILE\"",
                "echo \"\"",
                "echo \"Installing update...\"",
                $"cp -rf \"{extractDir}/\". \"{appDir}/\"",
                "if [ $? -ne 0 ]; then",
                "    echo \"\"",
                "    echo \"ERROR: Failed to copy update files.\"",
                "    echo \"Please download the update manually from:\"",
                $"    echo \"{ReleaseUrl}\"",
                "    read -p \"Press Enter to continue...\"",
                $"    rm -rf \"{tempDir}\"",
                "    exit 1",
                "fi",
                "",
                "# Make the executable runnable",
                $"chmod +x \"{exePath}\"",
                "",
            };

            if (skipRelaunch)
            {
                lines.Add("echo \"\"");
                lines.Add("echo \"Update complete! The BBS will launch the new version on next connection.\"");
            }
            else
            {
                lines.Add("echo \"\"");
                lines.Add("echo \"Update complete! Starting game...\"");
                lines.Add($"\"{exePath}\" &");
            }

            lines.Add("");
            lines.Add("echo \"\"");
            lines.Add("echo \"Cleaning up...\"");
            lines.Add($"rm -rf \"{tempDir}\"");
            lines.Add("");
            var script = string.Join("\n", lines);

            File.WriteAllText(updaterPath, script, new System.Text.UTF8Encoding(false));

            // Make the script executable
            try
            {
                System.Diagnostics.Process.Start("chmod", $"+x \"{updaterPath}\"")?.WaitForExit();
            }
            catch { }

            return updaterPath;
        }

        private void LaunchUpdater(string updaterPath)
        {
            try
            {
                // In BBS door mode, hide the updater window — no console to show it on
                bool hideWindow = BBS.DoorMode.IsInDoorMode;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{updaterPath}\"",
                        UseShellExecute = !hideWindow,
                        CreateNoWindow = hideWindow
                    });
                }
                else
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = $"\"{updaterPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = false
                    });
                }

                DebugLogger.Instance.LogInfo("UPDATE", "Updater launched, requesting game exit...");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("UPDATE", $"Failed to launch updater: {ex.Message}");
            }
        }

        /// <summary>
        /// Display auto-update prompt and handle the update process
        /// </summary>
        public async Task<bool> PromptAndInstallUpdate(TerminalEmulator terminal)
        {
            // In BBS mode, never offer auto-update - sysop must update manually
            if (UsurperRemake.BBS.DoorMode.IsInDoorMode)
            {
                DebugLogger.Instance.LogDebug("UPDATE", "Auto-update skipped - running in BBS door mode");
                return false;
            }

            if (!CanAutoUpdate())
            {
                // Fall back to browser-based update
                if (await PromptForUpdate(terminal))
                {
                    OpenDownloadPage();
                }
                return false;
            }

            terminal.SetColor("cyan");
            terminal.WriteLine("");
            terminal.WriteLine("  Auto-update is available for your platform!");
            terminal.WriteLine("");
            terminal.Write("  Would you like to download and install automatically? (Y/N): ");
            terminal.SetColor("white");

            var response = await terminal.ReadLineAsync();
            if (response?.Trim().ToUpper() != "Y")
            {
                // Offer manual download as fallback
                terminal.SetColor("gray");
                terminal.Write("  Would you like to open the download page instead? (Y/N): ");
                terminal.SetColor("white");
                response = await terminal.ReadLineAsync();
                if (response?.Trim().ToUpper() == "Y")
                {
                    OpenDownloadPage();
                }
                return false;
            }

            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("  Downloading update...");
            terminal.SetColor("white");

            var lastProgress = 0;
            var success = await DownloadAndInstallUpdateAsync(progress =>
            {
                if (progress >= lastProgress + 10 || progress == 100)
                {
                    terminal.Write($"\r  Progress: {progress}%   ");
                    lastProgress = progress;
                }
            });

            terminal.WriteLine("");

            if (success)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine("");
                terminal.WriteLine("  Update downloaded successfully!");
                terminal.WriteLine("  The game will now close and update automatically.");
                terminal.WriteLine("");
                terminal.SetColor("yellow");
                terminal.Write("  Press any key to continue...");
                terminal.SetColor("white");
                await terminal.PressAnyKey();
                return true; // Signal that game should exit
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  {DownloadError}");
                terminal.SetColor("gray");
                terminal.WriteLine("");
                terminal.Write("  Would you like to open the download page instead? (Y/N): ");
                terminal.SetColor("white");
                response = await terminal.ReadLineAsync();
                if (response?.Trim().ToUpper() == "Y")
                {
                    OpenDownloadPage();
                }
                return false;
            }
        }

        // JSON models for GitHub API response
        private class GitHubRelease
        {
            public string? tag_name { get; set; }
            public string? html_url { get; set; }
            public string? body { get; set; }
            public string? name { get; set; }
            public bool prerelease { get; set; }
            public bool draft { get; set; }
            public List<GitHubAsset>? assets { get; set; }
        }

        private class GitHubAsset
        {
            public string? name { get; set; }
            public string? browser_download_url { get; set; }
            public long size { get; set; }
        }

        private class VersionCache
        {
            public DateTime LastCheck { get; set; }
            public string? CurrentVersion { get; set; }  // The version we were running when we cached
            public string? LatestVersion { get; set; }
            public bool NewVersionAvailable { get; set; }
            public string? ReleaseUrl { get; set; }
            public string? ReleaseNotes { get; set; }
            public List<GitHubAsset>? Assets { get; set; }
        }
    }
}
