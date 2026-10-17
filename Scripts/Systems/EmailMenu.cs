using System;
using System.Threading.Tasks;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// 1.3.4 (1.6): E on the online character screen, shown only while this server sends mail. Shows the account's
    /// confirmed address masked (a***@d***.net), never in full. Add or change asks for the current password and puts
    /// the new address in a verify request (the web server notifies the old address once the new one is confirmed);
    /// remove asks for the current password, clears the address and sends a notice to it (S5).
    /// </summary>
    public static class EmailMenu
    {
        public static async Task RunAsync(TerminalEmulator terminal, SqlSaveBackend db, string username, string? ip)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("email.title") : Loc.Get("email.title_visual"));
            terminal.WriteLine("");

            var (_, email, _) = db.GetAccountEmail(username);
            terminal.SetColor("white");
            terminal.WriteLine(email != null ? Loc.Get("email.current", AccountEmail.Mask(email)) : Loc.Get("email.none"));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("email.about"));
            terminal.WriteLine("");
            WriteChoice(terminal, "1", Loc.Get("email.opt_change"));
            if (email != null) WriteChoice(terminal, "2", Loc.Get("email.opt_remove"));
            WriteChoice(terminal, "0", Loc.Get("prefs.back"));
            terminal.WriteLine("");

            string choice = (await terminal.GetInput(Loc.Get("ui.your_choice"))).Trim();
            if (choice == "1") await ChangeAsync(terminal, db, username, ip, email != null);
            else if (choice == "2" && email != null) await RemoveAsync(terminal, db, username, ip);
        }

        private static async Task ChangeAsync(TerminalEmulator terminal, SqlSaveBackend db, string username, string? ip, bool hasAddress)
        {
            terminal.WriteLine("");
            var password = await terminal.GetMaskedInput(Loc.Get("engine.current_password_prompt"));
            if (string.IsNullOrWhiteSpace(password)) { await Cancelled(terminal); return; }
            var address = (await terminal.GetInput(Loc.Get("email.new_prompt"))).Trim();
            if (address.Length == 0) { await Cancelled(terminal); return; }

            var result = await db.RequestEmailChange(username, password, address, ip, GameConfig.Language);
            switch (result)
            {
                case SqlSaveBackend.EmailChangeResult.Requested:
                    terminal.WriteLine(Loc.Get("auth.reg_email_sent"), "bright_green");
                    if (hasAddress) terminal.WriteLine(Loc.Get("email.change_note"), "white");
                    break;
                case SqlSaveBackend.EmailChangeResult.BadPassword:
                    terminal.WriteLine(Loc.Get("auth.err_current_password"), "red");
                    break;
                case SqlSaveBackend.EmailChangeResult.BadAddress:
                    terminal.WriteLine(Loc.Get("email.err_invalid"), "red");
                    break;
                default:
                    terminal.WriteLine(Loc.Get("email.failed"), "red");
                    break;
            }
            await terminal.PressAnyKey();
        }

        private static async Task RemoveAsync(TerminalEmulator terminal, SqlSaveBackend db, string username, string? ip)
        {
            terminal.WriteLine("");
            var password = await terminal.GetMaskedInput(Loc.Get("engine.current_password_prompt"));
            if (string.IsNullOrWhiteSpace(password)) { await Cancelled(terminal); return; }

            var result = await db.RemoveEmail(username, password, ip, GameConfig.Language);
            switch (result)
            {
                case SqlSaveBackend.EmailChangeResult.Removed:
                    terminal.WriteLine(Loc.Get("email.removed"), "bright_green");
                    break;
                case SqlSaveBackend.EmailChangeResult.NoAddress:
                    terminal.WriteLine(Loc.Get("email.none"), "white");
                    break;
                case SqlSaveBackend.EmailChangeResult.BadPassword:
                    terminal.WriteLine(Loc.Get("auth.err_current_password"), "red");
                    break;
                default:
                    terminal.WriteLine(Loc.Get("email.failed"), "red");
                    break;
            }
            await terminal.PressAnyKey();
        }

        private static async Task Cancelled(TerminalEmulator terminal)
        {
            terminal.WriteLine(Loc.Get("ui.cancelled"), "yellow");
            await Pacing.Wait(1000);
        }

        /// <summary>"[1] Label" on screen, "1. Label" for a screen reader.</summary>
        private static void WriteChoice(TerminalEmulator terminal, string key, string label)
        {
            if (GameConfig.ScreenReaderMode)
            {
                terminal.WriteLine($"  {key}. {label}");
                return;
            }
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write(key);
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(label);
        }
    }
}
