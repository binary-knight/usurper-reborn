using System;
using System.Threading.Tasks;
using UsurperRemake.BBS;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// 1.3.4 (1.5): the one optional email question for an account that has none and was never asked (accounts made
    /// before 1.3.4, and registrations that did not pass the login menu's email step). Shown once after login, after
    /// the telemetry question, only while this server sends mail (<see cref="MailSwitch"/>). Enter skips; a typo asks
    /// again (three tries, then it counts as a skip); an address goes into a verify request. A skip or an address sets
    /// email_prompted_at, so it is never shown again; a dropped connection sets nothing and the next login asks.
    /// </summary>
    public static class EmailPrompt
    {
        internal const int MaxTries = 3;

        /// <summary>Questions drawn in this process (tests).</summary>
        internal static int Drawn { get; private set; }

        public static async Task AskIfDueAsync(TerminalEmulator terminal, SqlSaveBackend? db, string? username, string? ip)
        {
            if (db == null || string.IsNullOrEmpty(username)) return;
            if (!db.EmailPromptDue(username)) return;
            Drawn++;
            terminal.WriteLine("");
            terminal.WriteLine(" " + Loc.Get("email.prompt_title"), "bright_yellow");
            foreach (var line in Loc.Get("email.prompt_body").Split('\n'))
                terminal.WriteLine(" " + line, "white");
            // what the address is used for, and that it is never sold or shared
            foreach (var line in Loc.Get("email.about").Split('\n'))
                terminal.WriteLine(" " + line, "gray");
            terminal.WriteLine("");
            for (int i = 0; i < MaxTries; i++)
            {
                string typed = (await terminal.GetInput(" " + Loc.Get("email.prompt_ask"))).Trim();
                if (DoorMode.IsDisconnected) return;
                if (typed.Length == 0) break;
                if (AccountEmail.Normalize(typed) == null)
                {
                    terminal.WriteLine(" " + Loc.Get("auth.err_email_invalid"), "red");
                    continue;
                }
                db.RequestEmailVerify(username, typed, ip, GameConfig.Language);
                db.MarkEmailPrompted(username);
                terminal.WriteLine(" " + Loc.Get("auth.reg_email_sent"), "bright_green");
                await Pacing.Wait(1500);
                return;
            }
            db.MarkEmailPrompted(username);
            foreach (var line in Loc.Get("email.skipped").Split('\n'))
                terminal.WriteLine(" " + line, "gray");
            await Pacing.Wait(1500);
        }
    }
}
