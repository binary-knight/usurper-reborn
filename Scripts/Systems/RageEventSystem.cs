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

        // Sysop-controlled kill switch. v0.60.0 beta-launch decision: disabled
        // by default (the sysop chose to wipe the server early and lift the
        // rage event rather than wait out the original 24-hour window). The
        // /rage on slash command can re-enable it within the date window if
        // the design call changes; outside the window it stays dormant
        // regardless of this flag.
        private static volatile bool _sysopDisabled = true;

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
            // Mark this session as rage-killed BEFORE anything else. SqlSaveBackend
            // checks this flag (and the process-wide erased-username set populated
            // by DeleteAccountCompletely) on every save and refuses to write,
            // preventing a fire-and-forget autosave started earlier in the load
            // path from re-inserting the row after we delete it.
            try
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) ctx.IsRageKilled = true;
            }
            catch { /* ignore */ }

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
                terminal.WriteLine($"  {Loc.Get("rage.world_holds_breath")}");
                await Pacing.Wait(2500);
                terminal.WriteLine("");
                terminal.WriteLine($"  {Loc.Get("rage.presence_forms")}");
                await Pacing.Wait(1500);
                terminal.WriteLine($"  {Loc.Get("rage.vast")}");
                await Pacing.Wait(800);
                terminal.WriteLine($"  {Loc.Get("rage.ancient")}");
                await Pacing.Wait(800);
                terminal.WriteLine($"  {Loc.Get("rage.furious")}");
                await Pacing.Wait(2000);

                terminal.WriteLine("");
                terminal.SetColor("bright_red");
                terminal.WriteLine($"  {Loc.Get("rage.strides_in")}");
                terminal.WriteLine($"  {Loc.Get("rage.eyes_burning")}");
                terminal.WriteLine($"  {Loc.Get("rage.silent_centuries")}");
                await Pacing.Wait(2500);
                terminal.WriteLine("");
                terminal.WriteLine($"  {Loc.Get("rage.watching_ends")}");
                await Pacing.Wait(2500);

                terminal.WriteLine("");
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("rage.looks_upon_you", player?.Name2 ?? player?.Name1 ?? Loc.Get("rage.mortal"))}");
                terminal.WriteLine($"  {Loc.Get("rage.sees_every_choice")}");
                terminal.WriteLine($"  {Loc.Get("rage.every_cheese")}");
                await Pacing.Wait(3000);
                terminal.WriteLine("");
                terminal.SetColor("dark_red");
                terminal.WriteLine($"  {Loc.Get("rage.not_impressed")}");
                await Pacing.Wait(2500);

                terminal.WriteLine("");
                terminal.SetColor("bright_red");
                terminal.WriteLine($"  {Loc.Get("rage.mortal_rumbles")}");
                terminal.WriteLine($"  {Loc.Get("rage.lived_as_you_saw_fit")}");
                await Pacing.Wait(2000);
                terminal.WriteLine($"  {Loc.Get("rage.die_as_i_see_fit")}");
                await Pacing.Wait(2500);

                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine($"  {Loc.Get("rage.raise_sword")}");
                await Pacing.Wait(1500);
                terminal.SetColor("dark_red");
                terminal.WriteLine($"  {Loc.Get("rage.he_laughs")}");
                terminal.WriteLine($"  {Loc.Get("rage.laugh_end_of_age")}");
                await Pacing.Wait(2500);

                terminal.WriteLine("");
                terminal.SetColor("bright_red");
                terminal.WriteLine($"  {Loc.Get("rage.blow_falls")}");
                await Pacing.Wait(1200);
                terminal.WriteLine($"  {Loc.Get("rage.just_one")}");
                await Pacing.Wait(2000);

                terminal.WriteLine("");
                terminal.SetColor("dark_gray");
                terminal.WriteLine($"  {Loc.Get("rage.unmade")}");
                await Pacing.Wait(3000);

                terminal.WriteLine("");
                terminal.WriteLine($"  {Loc.Get("rage.sun_rises")}");
                terminal.WriteLine($"  {Loc.Get("rage.no_one_remembers")}");
                await Pacing.Wait(3500);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("RAGE_EVENT",
                    $"Cinematic threw for '{username}': {ex.Message}. Continuing to deletion.");
            }

            // Hard-delete the entire account row, not just the character data.
            // After this runs, AuthenticatePlayer returns "Unknown username" --
            // the player cannot log back in with their old credentials. Their
            // password_hash is gone with the row. This is what the user asked
            // for: divine erasure that takes the SSH account too, not just the
            // character. The whole DB will be wiped at beta launch anyway, so
            // there is no recovery path even if we wanted one.
            try
            {
                if (SaveSystem.Instance?.Backend is SqlSaveBackend sqlBackend && !string.IsNullOrEmpty(username))
                {
                    sqlBackend.DeleteAccountCompletely(username);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("RAGE_EVENT",
                    $"Account hard-delete failed for '{username}': {ex.Message}");
            }

            try
            {
                terminal.WriteLine("");
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("rage.record_erased")}");
                terminal.WriteLine($"  {Loc.Get("rage.name_will_not_answer")}");
                terminal.WriteLine($"  {Loc.Get("rage.disconnecting")}");
                terminal.WriteLine("");
                await Pacing.Wait(2500);
            }
            catch { /* ignore */ }
        }
    }
}
