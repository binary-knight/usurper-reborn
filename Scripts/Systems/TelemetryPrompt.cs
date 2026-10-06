using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UsurperRemake.BBS;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// 1.2.7 (T3): the opt-in telemetry question and its Preferences line. Single player and Steam ask once
    /// per copy of the game before the main menu; a BBS door or a server asks each player once after the
    /// MOTD, only while the operator switch is on. Only a yes or a no answers: Enter alone and any other
    /// key ask again, and no answer (the invalid answers ran out, end of input, a hang up) leaves the
    /// state "not asked", so the question comes back at the next start. Every line goes through Loc.
    /// </summary>
    public static class TelemetryPrompt
    {
        /// <summary>Tests: the store the Preferences line uses instead of the active save directory's.</summary>
        internal static TelemetryStore? StoreOverride { get; set; }

        /// <summary>The store the Preferences line works on.</summary>
        internal static TelemetryStore? CurrentStore() => StoreOverride ?? TelemetryConsent.CurrentStore();

        /// <summary>True when a person can answer on this terminal: a MUD or BBS stream, a door or online
        /// session, or a local console whose input is not redirected. A non interactive run (input piped
        /// or closed) is never asked. Tests replace it.</summary>
        internal static Func<TerminalEmulator, bool> InputIsInteractive { get; set; } = DefaultInputIsInteractive;

        internal static bool DefaultInputIsInteractive(TerminalEmulator terminal) =>
            terminal.StreamWriterInternal != null || DoorMode.IsInDoorMode || DoorMode.IsOnlineMode || !Console.IsInputRedirected;

        /// <summary>Questions drawn in this process (tests).</summary>
        internal static int Drawn { get; private set; }

        /// <summary>The lines above the question, as shown: the title, a blank line, the text of the variant
        /// (shared: BBS door and server; else single player and Steam), a blank line.</summary>
        internal static List<string> Lines(bool shared)
        {
            var lines = new List<string> { Loc.Get("telemetry.prompt_title"), "" };
            lines.AddRange(Loc.Get(shared ? "telemetry.prompt_body_shared" : "telemetry.prompt_body_single").Split('\n'));
            lines.Add("");
            return lines;
        }

        /// <summary>The question line, with the language's own yes/no letters.</summary>
        internal static string Question() => $"{Loc.Get("telemetry.prompt_ask")} {Loc.Get("ui.yn_prompt")}";

        /// <summary>Draw the question and read the answer: true yes, false no, null no answer. Plain lines
        /// in every mode, so a screen reader reads them as they are. 1.2.7 (T3b): only the yes this
        /// language offers counts; another language's yes is asked again, so it never stores a yes.</summary>
        internal static async Task<bool?> AskAsync(TerminalEmulator terminal, bool shared)
        {
            Drawn++;
            var lines = Lines(shared);
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Length == 0) terminal.WriteLine("");
                else terminal.WriteLine(" " + lines[i], i == 0 ? "bright_yellow" : "white");
            }
            return await terminal.AskYesNoOrNoAnswerAsync(" " + Question(), offeredYesOnly: true);
        }

        /// <summary>The approved line shown when an answer could not be written.</summary>
        internal static async Task NotSaved(TerminalEmulator terminal)
        {
            terminal.WriteLine(" " + Loc.Get("telemetry.not_saved"), "red");
            await Pacing.Wait(1500);
        }

        /// <summary>
        /// Single player and Steam, at start before the main menu: ask once. Not asked again once a yes or a
        /// no is stored. No answer is stored as not asked (asked again next start). A non interactive run
        /// draws nothing. A shared install is never asked here.
        /// </summary>
        public static async Task AskInstallIfNeededAsync(TerminalEmulator terminal, TelemetryStore? store = null)
        {
            store ??= TelemetryConsent.CurrentStore();
            if (store == null || store.IsSharedInstall) return;
            if (store.State.Asked) return;
            if (!InputIsInteractive(terminal)) return;
            terminal.ClearScreen();
            bool? answer;
            try { answer = await AskAsync(terminal, shared: false); }
            catch (Exception ex) when (ConnectionClosedException.IsDisconnect(ex))
            {
                store.InstallAskInterrupted();
                throw;
            }
            bool ok = answer.HasValue ? store.SetInstallAnswer(answer.Value) : store.InstallAskInterrupted();
            if (!ok) await NotSaved(terminal);
        }

        /// <summary>
        /// BBS door and server, after login and the MOTD: ask this player once, only while the operator
        /// switch is on. The answer is stored for this player's account only. No answer is stored as not
        /// asked; a MUD hang up is stored the same way and the session still ends.
        /// </summary>
        public static async Task AskPlayerIfNeededAsync(TerminalEmulator terminal, string? loginName, TelemetryStore? store = null)
        {
            store ??= TelemetryConsent.CurrentStore();
            if (store == null || !store.IsSharedInstall) return;
            if (!TelemetryConsent.OperatorAllows()) return;
            if (TelemetryConsent.PlayerKey(loginName) == null) return;
            if (store.PlayerWasAsked(loginName)) return;
            if (!InputIsInteractive(terminal)) return;
            bool? answer;
            try { answer = await AskAsync(terminal, shared: true); }
            catch (Exception ex) when (ConnectionClosedException.IsDisconnect(ex))
            {
                store.PlayerAskInterrupted(loginName);
                throw;
            }
            bool ok = answer.HasValue ? store.SetPlayerAnswer(loginName, answer.Value) : store.PlayerAskInterrupted(loginName);
            if (!ok) await NotSaved(terminal);
        }

        // ---------- Preferences ----------

        /// <summary>The Preferences line is shown on single player and Steam, and on a BBS door or server only
        /// while the operator switch is on.</summary>
        internal static bool PreferenceShown(TelemetryStore? store) =>
            store != null && (!store.IsSharedInstall || TelemetryConsent.OperatorAllows());

        /// <summary>The line's label and its On or Off, as the approved text has it.</summary>
        internal static string PreferenceLabel(TelemetryStore store, string? loginName) =>
            $"{Loc.Get("telemetry.pref_row")}: {Loc.Get(store.ShouldQueue(loginName) ? "telemetry.pref_on" : "telemetry.pref_off")}";

        /// <summary>
        /// The Preferences line chosen. Off: the full question again, and only a yes turns it on (a no stores
        /// no, no answer changes nothing). On, BBS door or server: turned off at once (the player's No, which
        /// deletes the queue). On, single player and Steam: Turn off, or New random id (the id is the
        /// player's own there; on a BBS or server it belongs to the operator). Any answer not written shows
        /// the not saved line.
        /// </summary>
        internal static async Task PreferenceChosenAsync(TerminalEmulator terminal, TelemetryStore store, string? loginName, bool screenReader)
        {
            bool shared = store.IsSharedInstall;
            bool ok;
            if (!store.ShouldQueue(loginName))
            {
                terminal.ClearScreen();
                bool? answer = await AskAsync(terminal, shared);
                if (answer == null) return;
                ok = shared ? store.SetPlayerAnswer(loginName, answer.Value) : store.SetInstallAnswer(answer.Value);
            }
            else if (shared)
            {
                ok = store.SetPlayerAnswer(loginName, false);
            }
            else
            {
                terminal.WriteLine("");
                terminal.WriteLine(" " + PreferenceLabel(store, loginName), "bright_cyan");
                WriteChoice(terminal, "1", Loc.Get("telemetry.pref_turn_off"), screenReader);
                WriteChoice(terminal, "2", Loc.Get("telemetry.pref_new_id"), screenReader);
                WriteChoice(terminal, "0", Loc.Get("prefs.back"), screenReader);
                string choice = (await terminal.GetInput(Loc.Get("ui.choice"))).Trim();
                if (choice == "1") ok = store.SetInstallAnswer(false);
                else if (choice == "2") ok = store.NewInstallId() != null;
                else return;
            }
            if (!ok) await NotSaved(terminal);
        }

        /// <summary>One sub choice: "[1] Turn off" on screen, "1. Turn off" for a screen reader.</summary>
        private static void WriteChoice(TerminalEmulator terminal, string key, string label, bool screenReader)
        {
            if (screenReader)
            {
                terminal.WriteLine($"  {key}. {label}");
                return;
            }
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write(key);
            terminal.SetColor("white");
            terminal.WriteLine($"] {label}");
        }
    }
}
