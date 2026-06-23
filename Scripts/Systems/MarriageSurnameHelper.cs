using UsurperRemake.Utils;
using UsurperRemake.Systems;
using System;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// v0.65.1: marriage-ceremony prompt that lets a PLAYER take a family surname --
/// the spouse's surname, or a freshly chosen one (player request). Sets
/// Character.FamilySurname, which rides on DisplayName for display ONLY; the
/// identity key (Name2) is never touched, so nothing that keys on the player's
/// name (parent/child matching, quests, worship, relationships) is disturbed.
/// Shared by every player-facing ceremony (Church, Love Corner, the visual-novel
/// proposal). Best-effort and non-fatal: any failure leaves the name unchanged so
/// the marriage itself is never interrupted.
/// </summary>
public static class MarriageSurnameHelper
{
    private const int MaxSurnameLength = 20;

    public static async Task OfferAsync(TerminalEmulator terminal, Character player, string spouseDisplayName)
    {
        try
        {
            if (terminal == null || player == null || !player.IsPlayer) return;

            // Spouse's surname = last whitespace-delimited token of their name, if any.
            string spouseSurname = ExtractSurname(spouseDisplayName);

            terminal.WriteLine("");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"  {Loc.Get("marriage.surname_prompt")}");
            terminal.SetColor("white");
            terminal.WriteLine($"  [1] {Loc.Get("marriage.surname_keep", player.DisplayName)}");

            int takeSpouseOption = 0;
            int newOption;
            if (!string.IsNullOrEmpty(spouseSurname) &&
                !string.Equals(spouseSurname, player.FamilySurname, StringComparison.OrdinalIgnoreCase))
            {
                takeSpouseOption = 2;
                terminal.WriteLine($"  [2] {Loc.Get("marriage.surname_take_spouse", spouseSurname)}");
                newOption = 3;
            }
            else
            {
                newOption = 2;
            }
            terminal.WriteLine($"  [{newOption}] {Loc.Get("marriage.surname_new")}");

            terminal.SetColor("gray");
            string choice = (await terminal.GetInput($"  {Loc.Get("ui.your_choice")} ")).Trim();

            if (takeSpouseOption > 0 && choice == takeSpouseOption.ToString())
            {
                player.FamilySurname = spouseSurname;
                AnnounceNewName(terminal, player);
                await RefreshOnlinePresence(player);
            }
            else if (choice == newOption.ToString())
            {
                string entered = SanitizeSurname(await terminal.GetInput($"  {Loc.Get("marriage.surname_enter")} "));
                if (!string.IsNullOrEmpty(entered))
                {
                    player.FamilySurname = entered;
                    AnnounceNewName(terminal, player);
                    await RefreshOnlinePresence(player);
                }
                else
                {
                    AnnounceUnchanged(terminal, player);
                }
            }
            else
            {
                // [1] keep, or any other input -> no change.
                AnnounceUnchanged(terminal, player);
            }
        }
        catch
        {
            // A cosmetic flourish must never break the wedding.
        }
    }

    private static void AnnounceNewName(TerminalEmulator terminal, Character player)
    {
        terminal.SetColor("bright_green");
        terminal.WriteLine($"  {Loc.Get("marriage.surname_set", player.DisplayName)}");
    }

    private static void AnnounceUnchanged(TerminalEmulator terminal, Character player)
    {
        terminal.SetColor("gray");
        terminal.WriteLine($"  {Loc.Get("marriage.surname_unchanged", player.DisplayName)}");
    }

    /// <summary>
    /// v0.65.1: push the new married name to the live /who list immediately (online
    /// mode). The next autosave persists players.display_name via WriteGameData;
    /// this just keeps presence in sync mid-session. Best-effort.
    /// </summary>
    private static async Task RefreshOnlinePresence(Character player)
    {
        try
        {
            if (OnlineStateManager.IsActive && OnlineStateManager.Instance != null)
                await OnlineStateManager.Instance.UpdateDisplayName(player.DisplayName);
        }
        catch { /* presence refresh is cosmetic */ }
    }

    /// <summary>Last whitespace-delimited token of a name, or "" if it's single-token.</summary>
    public static string ExtractSurname(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "";
        fullName = fullName.Trim();
        int sp = fullName.LastIndexOf(' ');
        return (sp > 0 && sp < fullName.Length - 1) ? fullName.Substring(sp + 1) : "";
    }

    /// <summary>Letters / space / hyphen / apostrophe only; collapse, cap length.</summary>
    private static string SanitizeSurname(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var sb = new StringBuilder();
        foreach (char c in raw.Trim())
        {
            if (char.IsLetter(c) || c == ' ' || c == '-' || c == '\'')
                sb.Append(c);
            if (sb.Length >= MaxSurnameLength) break;
        }
        return sb.ToString().Trim();
    }
}
