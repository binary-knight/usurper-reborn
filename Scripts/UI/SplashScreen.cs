using System;
using System.Threading.Tasks;

namespace UsurperRemake.UI
{
    /// <summary>
    /// Displays the USURPER REBORN ASCII art splash screen.
    /// Inspired by the original 1993 Usurper title screen with fire effects and dramatic block letters.
    /// Fits within 80x25 BBS terminal (SyncTERM compatible).
    /// </summary>
    public static class SplashScreen
    {
        public static async Task Show(dynamic terminal)
        {
            terminal.ClearScreen();

            // === FIRE TOP (3 rows — base/dense/tips, hanging down) ===          Lines 1-3
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("████████████████████████████████████████████████████████████████████████████████");
            terminal.SetColor("red");
            terminal.WriteLine("▓▒░▒▓██▓▒░▒▓███▓▒░▒▓██▓▒░▒▓███▓▒░▒▓██▓▒░▒▓███▓▒░▒▓██▓▒░▒▓███▓▒░▒▓██▓▒░▒▓█");
            terminal.SetColor("dark_red");
            terminal.WriteLine("░  ░▒░  ░░  ░▒▒░ ░  ░▒░  ░░  ░▒░  ░ ░▒░  ░░  ░▒▒░ ░  ░▒░  ░░  ░▒░  ░░  ░");

            // === USURPER — 5 rows, bright_red, centered ===                     Lines 4-8
            string p1 = "           "; // ~11 char left pad to center ~57-char text in 80 cols
            terminal.SetColor("bright_red");
            terminal.WriteLine(p1 + "██  ██ ▄█████▄ ██  ██ ██████▄ ██████▄ ██████ ██████▄");
            await Task.Delay(40);
            terminal.WriteLine(p1 + "██  ██ ██▀▀▀▀▀ ██  ██ ██   ██ ██   ██ ██     ██   ██");
            await Task.Delay(40);
            terminal.WriteLine(p1 + "██  ██  ▀████▄ ██  ██ ██████▀ ██████▀ █████  ██████▀");
            await Task.Delay(40);
            terminal.WriteLine(p1 + "██  ██      ██ ██  ██ ██  ▀█▄ ██      ██     ██  ▀█▄");
            await Task.Delay(40);
            terminal.WriteLine(p1 + " ▀████▀ ▀████▀▀ ▀████▀ ██   ▀█ ██      ██████ ██   ▀█");
            await Task.Delay(40);

            terminal.WriteLine("");                                             // Line 9

            // === REBORN — 3 rows, bright_yellow, centered ===                   Lines 10-12
            // R distinct from B: R has closed top + separated leg at bottom
            string p2 = "                        "; // ~24 char left pad to center ~32-char text
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(p2 + "█▀▀█ █▀▀▀ █▀▀▄  ▄▀▀▄  █▀▀█ █▄  █");
            await Task.Delay(40);
            terminal.WriteLine(p2 + "█▄▄▀ █▄▄  █▀▀▄  █  █  █▄▄▀ █ ▀▄█");
            await Task.Delay(40);
            terminal.WriteLine(p2 + "█  █ █▄▄▄ █▄▄▀  ▀▄▄▀  █  █ █   █");
            await Task.Delay(40);

            // === SEPARATOR ===                                                  Line 13
            terminal.SetColor("dark_red");
            terminal.WriteLine("    ════════════════════════════════════════════════════════════════════════");

            // === TAGLINE ===                                                    Lines 14-15
            terminal.SetColor("bright_white");
            terminal.WriteLine("          Face death, kill your friends, pump on 'roids and");
            terminal.WriteLine("          claim the throne, and ultimately find yourself.");

            terminal.WriteLine("");                                             // Line 16

            // === INSPIRED BY + CREDITS ===                                      Lines 17-20
            terminal.SetColor("cyan");
            terminal.WriteLine("                    Inspired by the 1993 Classic.");
            terminal.SetColor("gray");
            terminal.WriteLine("          1993 - Original by Jakob Dangarden (JAS Software)");
            terminal.WriteLine("          2026 - Reborn by Jason Knight");
            terminal.SetColor("dark_green");
            terminal.WriteLine($"                         v{GameConfig.Version} \"{GameConfig.VersionName}\"");

            // === FIRE BOTTOM (3 rows — tips/dense/base, reaching up) ===        Lines 21-23
            terminal.SetColor("dark_red");
            terminal.WriteLine("░  ░▒░  ░░  ░▒▒░ ░  ░▒░  ░░  ░▒░  ░ ░▒░  ░░  ░▒▒░ ░  ░▒░  ░░  ░▒░  ░░  ░");
            terminal.SetColor("red");
            terminal.WriteLine("▓▒░▒▓██▓▒░▒▓███▓▒░▒▓██▓▒░▒▓███▓▒░▒▓██▓▒░▒▓███▓▒░▒▓██▓▒░▒▓███▓▒░▒▓██▓▒░▒▓█");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("████████████████████████████████████████████████████████████████████████████████");

            // === PRESS ANY KEY ===                                              Line 25
            terminal.SetColor("bright_white");
            terminal.Write("                           Press any key...");
            terminal.SetColor("white");

            await terminal.WaitForKey("");
            terminal.ClearScreen();
        }
    }
}
