using System.Threading.Tasks;

namespace UsurperRemake.Systems;

/// <summary>
/// 1.2.0 Temple gods piece 2: an immortal picks their god's domain, one of the ten boon domains.
/// Asked at ascension, and on each Pantheon visit until chosen. Once chosen it is never asked again.
/// </summary>
public static class GodDomainPicker
{
    /// <summary>
    /// Shows the ten domains with their boon and ward and asks for one. A number out of range is
    /// asked again, a declined confirmation goes back to the list, and Enter leaves the choice for
    /// the next visit. Returns true when the immortal has a domain (chosen now or before).
    /// </summary>
    public static async Task<bool> PickAsync(Character immortal, TerminalEmulator terminal)
    {
        if (immortal == null || terminal == null || !immortal.IsImmortal) return false;
        if (GodBoonSystem.ParseDomain(immortal.DivineDomain) != GodDomain.None) return true;

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("god.domain_pick_title"), "bright_yellow");
        terminal.WriteLine(Loc.Get("god.domain_pick_intro"), "white");
        terminal.WriteLine("");
        var domains = GodBoonSystem.AllDomains;
        for (int i = 0; i < domains.Count; i++)
        {
            var d = domains[i];
            terminal.WriteLine($"  {Loc.Get("god.domain_pick_entry", i + 1, GodBoonSystem.DomainName(d), GodBoonSystem.CanonGodOf(d))}", "bright_cyan");
            terminal.WriteLine($"      {Loc.Get("god.boon_line", GodBoonSystem.DescribeBoon(d, 100), 100)}", "gray");
            terminal.WriteLine($"      {Loc.Get("god.ward_line", GodBoonSystem.DescribeWard(d))}", "darkgray");
        }

        while (true)
        {
            terminal.WriteLine("");
            string input = (await terminal.GetInputAsync(Loc.Get("god.domain_pick_prompt")))?.Trim() ?? "";
            if (input.Length == 0)
            {
                terminal.WriteLine(Loc.Get("god.domain_pick_later"), "gray");
                return false;
            }
            if (!int.TryParse(input, out int n) || n < 1 || n > domains.Count)
            {
                terminal.WriteLine(Loc.Get("god.domain_pick_invalid"), "yellow");
                continue;
            }
            var chosen = domains[n - 1];
            if (!await terminal.AskYesNoAsync(Loc.Get("god.domain_pick_confirm", GodBoonSystem.DomainName(chosen))))
                continue;
            immortal.DivineDomain = chosen.ToString();
            terminal.WriteLine(Loc.Get("god.domain_pick_done", immortal.DivineName, GodBoonSystem.DomainName(chosen)), "bright_yellow");
            return true;
        }
    }
}
