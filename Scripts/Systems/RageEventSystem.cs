using System;
using System.Threading.Tasks;
using UsurperRemake.BBS;
using UsurperRemake.UI;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// v0.60.0 beta-launch event. The god RAGE descends on 2026-04-30 07:00 UTC
    /// (= 02:00 CDT, the sysop's wall clock), sees every mortal who logs in,
    /// scoffs at their petty victories, and erases them from existence. This is
    /// the in-world narrative for the database wipe that transitions the alpha
    /// server to beta. Every character who logs in during the window is
    /// permadeleted (bypassing the v0.57.22 deleted_characters 7-day archive:
    /// the divine erasure must be final). Whatever survives the rage event will
    /// then be wiped wholesale by the server-wipe button when the sysop is
    /// ready to migrate the world.
    ///
    /// Two safety layers gate firing:
    ///   1. Hardcoded date window: 2026-04-30 07:00 UTC through 2026-05-01
    ///      07:00 UTC (24 hours, 02:00 CDT to 02:00 CDT). Outside this window,
    ///      IsActive returns false unconditionally. After the window, the event
    ///      is permanently dormant; the code is harmless to ship alongside
    ///      post-beta releases.
    ///   2. In-process kill switch: SysopDisable can be flipped at runtime if
    ///      anything goes wrong (a sysop slash command sets it). Default true
    ///      so the date window auto-activates the event without requiring
    ///      manual enable.
    ///
    /// Not in release notes by design (matches the bot-detection precedent):
    /// players logging in tomorrow should be SURPRISED by the event. Knowing
    /// in advance ruins the moment, defeats the narrative purpose, and gives
    /// data hoarders an unfair head-start on backups.
    /// </summary>
    public static class RageEventSystem
    {
        // 24-hour window starting 02:00 CDT on 2026-04-30 (= 07:00 UTC) through
        // 02:00 CDT on 2026-05-01 (= 07:00 UTC). Sysop's local wall clock decides
        // the dramatic moment; UTC is what the server actually checks.
        private static readonly DateTime EventStartUtc = new DateTime(2026, 4, 30, 7, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime EventEndUtc = new DateTime(2026, 5, 1, 7, 0, 0, DateTimeKind.Utc);

        // Sysop-controlled kill switch. Default ON so the date window auto-fires.
        // Flipped via the /rage admin slash command if anything breaks during the event.
        private static volatile bool _sysopDisabled = false;

        public static bool IsActive => !_sysopDisabled
            && DateTime.UtcNow >= EventStartUtc
            && DateTime.UtcNow < EventEndUtc;

        public static bool IsSysopDisabled => _sysopDisabled;

        public static void DisableViaSysop()
        {
            _sysopDisabled = true;
            DebugLogger.Instance.LogWarning("RAGE_EVENT",
                "Rage event manually DISABLED via sysop kill switch.");
        }

        public static void EnableViaSysop()
        {
            _sysopDisabled = false;
            DebugLogger.Instance.LogWarning("RAGE_EVENT",
                "Rage event manually RE-ENABLED via sysop kill switch.");
        }

        public static string GetStatus()
        {
            string window = $"{EventStartUtc:yyyy-MM-dd HH:mm} UTC -> {EventEndUtc:yyyy-MM-dd HH:mm} UTC";
            string now = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";
            string inWindow = (DateTime.UtcNow >= EventStartUtc && DateTime.UtcNow < EventEndUtc) ? "YES" : "NO";
            string disabled = _sysopDisabled ? "YES (sysop kill switch)" : "no";
            string firing = IsActive ? "FIRING" : "dormant";
            return $"Rage event status: {firing}\n  Window: {window}\n  Now: {now}\n  In window: {inWindow}\n  Sysop disabled: {disabled}";
        }

        /// <summary>
        /// Run the rage cinematic and erase the player's character. Caller is
        /// responsible for forcing a disconnect after this returns (typically by
        /// returning early from the calling flow). Best-effort -- if anything
        /// throws during the cinematic, log it and continue to the deletion step
        /// so the event still has its narrative consequence.
        /// </summary>
        public static async Task RunRageEventAsync(global::Character player, TerminalEmulator terminal, string username)
        {
            try
            {
                terminal.ClearScreen();
            }
            catch { /* ignore */ }

            try
            {
                terminal.SetColor("dark_red");
                terminal.WriteLine("");
                terminal.WriteLine("");
                terminal.WriteLine("  The world holds its breath.");
                await Task.Delay(2500);
                terminal.WriteLine("");
                terminal.WriteLine("  A presence forms in the void.");
                await Task.Delay(1500);
                terminal.WriteLine("  Vast.");
                await Task.Delay(800);
                terminal.WriteLine("  Ancient.");
                await Task.Delay(800);
                terminal.WriteLine("  Furious.");
                await Task.Delay(2000);

                terminal.WriteLine("");
                terminal.SetColor("bright_red");
                terminal.WriteLine("  The god RAGE strides into Usurper's realm,");
                terminal.WriteLine("  his eyes burning with the heat of a thousand reborn worlds.");
                terminal.WriteLine("  He has been silent for centuries, watching.");
                await Task.Delay(2500);
                terminal.WriteLine("");
                terminal.WriteLine("  Tonight, the watching ends.");
                await Task.Delay(2500);

                terminal.WriteLine("");
                terminal.SetColor("yellow");
                terminal.WriteLine($"  He looks upon you, {player?.Name2 ?? player?.Name1 ?? "mortal"}.");
                terminal.WriteLine("  He sees every choice. Every grudge. Every shortcut.");
                terminal.WriteLine("  Every cheese. Every petty victory and every act of cowardice.");
                await Task.Delay(3000);
                terminal.WriteLine("");
                terminal.SetColor("dark_red");
                terminal.WriteLine("  He is not impressed.");
                await Task.Delay(2500);

                terminal.WriteLine("");
                terminal.SetColor("bright_red");
                terminal.WriteLine("  \"Mortal,\" Rage rumbles, and the sky cracks open.");
                terminal.WriteLine("  \"You have lived as you saw fit.\"");
                await Task.Delay(2000);
                terminal.WriteLine("  \"Now you will die as I see fit.\"");
                await Task.Delay(2500);

                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine("  You raise your sword.");
                await Task.Delay(1500);
                terminal.SetColor("dark_red");
                terminal.WriteLine("  He laughs.");
                terminal.WriteLine("  The laugh sounds like the end of an age.");
                await Task.Delay(2500);

                terminal.WriteLine("");
                terminal.SetColor("bright_red");
                terminal.WriteLine("  A blow falls.");
                await Task.Delay(1200);
                terminal.WriteLine("  Just one.");
                await Task.Delay(2000);

                terminal.WriteLine("");
                terminal.SetColor("dark_gray");
                terminal.WriteLine("  You are unmade.");
                await Task.Delay(3000);

                terminal.WriteLine("");
                terminal.WriteLine("  When the sun rises on the new world,");
                terminal.WriteLine("  no one will remember your name.");
                await Task.Delay(3500);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("RAGE_EVENT",
                    $"Cinematic threw for '{username}': {ex.Message}. Continuing to deletion.");
            }

            // Permadelete the character. bypassArchive=true makes this irreversible:
            // no /restore window, no recovery. The whole point of the event is the
            // finality, and the entire DB will be wiped wholesale anyway.
            try
            {
                if (SaveSystem.Instance?.Backend is SqlSaveBackend sqlBackend && !string.IsNullOrEmpty(username))
                {
                    sqlBackend.DeleteGameData(username, bypassArchive: true);
                    DebugLogger.Instance.LogWarning("RAGE_EVENT",
                        $"Permadeleted character for '{username}' via Rage event (bypassArchive=true).");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("RAGE_EVENT",
                    $"Permadelete failed for '{username}': {ex.Message}");
            }

            try
            {
                terminal.WriteLine("");
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine("  Your record has been erased from the world.");
                terminal.WriteLine("  Disconnecting.");
                terminal.WriteLine("");
                await Task.Delay(2500);
            }
            catch { /* ignore */ }
        }
    }
}
