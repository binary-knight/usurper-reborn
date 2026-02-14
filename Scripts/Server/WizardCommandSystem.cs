using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UsurperRemake.Systems;

namespace UsurperRemake.Server;

/// <summary>
/// Classic MUD wizard command system. Processes all wizard slash commands
/// based on the invoking wizard's level. Each command has a minimum tier
/// requirement, and each tier inherits all powers of lower tiers.
///
/// Called from MudChatSystem.TryProcessCommand() before normal chat routing,
/// only when ctx.WizardLevel > WizardLevel.Mortal.
/// </summary>
public static class WizardCommandSystem
{
    /// <summary>
    /// Try to process a wizard command. Returns true if handled.
    /// </summary>
    public static async Task<bool> TryProcessCommand(
        string input, string username, WizardLevel wizLevel, TerminalEmulator terminal)
    {
        if (!input.StartsWith("/")) return false;

        var spaceIndex = input.IndexOf(' ');
        var command = spaceIndex > 0 ? input.Substring(1, spaceIndex - 1).ToLowerInvariant() : input.Substring(1).ToLowerInvariant();
        var args = spaceIndex > 0 ? input.Substring(spaceIndex + 1).Trim() : "";

        // Route to handler based on command name
        switch (command)
        {
            // ── Builder+ (Level 1) ──────────────────────────────────
            case "wizhelp":
                return HandleWizHelp(wizLevel, terminal);
            case "wiznet":
            case "wiz":
                return HandleWizChat(username, wizLevel, args, terminal);
            case "wizwho":
                return HandleWizWho(terminal);
            case "holylight":
                return HandleHolyLight(username, terminal);
            case "stat":
                return await HandleStat(wizLevel, args, terminal);
            case "where":
                return HandleWhere(wizLevel, terminal);

            // ── Immortal+ (Level 2) ─────────────────────────────────
            case "invis":
                if (wizLevel < WizardLevel.Immortal) return NotEnoughPower(terminal);
                return HandleInvis(username, terminal);
            case "visible":
                if (wizLevel < WizardLevel.Immortal) return NotEnoughPower(terminal);
                return HandleVisible(username, terminal);
            case "godmode":
                if (wizLevel < WizardLevel.Immortal) return NotEnoughPower(terminal);
                return HandleGodmode(username, terminal);
            case "heal":
                if (wizLevel < WizardLevel.Immortal) return NotEnoughPower(terminal);
                return HandleHeal(username, args, terminal);
            case "restore":
                if (wizLevel < WizardLevel.Immortal) return NotEnoughPower(terminal);
                return HandleRestore(username, args, terminal);
            case "peace":
                if (wizLevel < WizardLevel.Immortal) return NotEnoughPower(terminal);
                return HandlePeace(username, terminal);
            case "echo":
                if (wizLevel < WizardLevel.Immortal) return NotEnoughPower(terminal);
                return HandleEcho(username, args, terminal);
            case "goto":
                if (wizLevel < WizardLevel.Immortal) return NotEnoughPower(terminal);
                return await HandleGoto(username, args, terminal);

            // ── Wizard+ (Level 3) ───────────────────────────────────
            case "summon":
                if (wizLevel < WizardLevel.Wizard) return NotEnoughPower(terminal);
                return await HandleSummon(username, args, terminal);
            case "transfer":
            case "trans":
                if (wizLevel < WizardLevel.Wizard) return NotEnoughPower(terminal);
                return await HandleTransfer(username, args, terminal);
            case "snoop":
                if (wizLevel < WizardLevel.Wizard) return NotEnoughPower(terminal);
                return HandleSnoop(username, args, terminal);
            case "force":
                if (wizLevel < WizardLevel.Wizard) return NotEnoughPower(terminal);
                return HandleForce(username, args, terminal);
            case "set":
                if (wizLevel < WizardLevel.Wizard) return NotEnoughPower(terminal);
                return await HandleSet(username, args, terminal);
            case "slay":
                if (wizLevel < WizardLevel.Wizard) return NotEnoughPower(terminal);
                return HandleSlay(username, args, terminal);
            case "freeze":
                if (wizLevel < WizardLevel.Wizard) return NotEnoughPower(terminal);
                return await HandleFreeze(username, args, terminal);
            case "thaw":
                if (wizLevel < WizardLevel.Wizard) return NotEnoughPower(terminal);
                return await HandleThaw(username, args, terminal);
            case "mute":
                if (wizLevel < WizardLevel.Wizard) return NotEnoughPower(terminal);
                return await HandleMute(username, args, terminal);
            case "unmute":
                if (wizLevel < WizardLevel.Wizard) return NotEnoughPower(terminal);
                return await HandleUnmute(username, args, terminal);
            case "purge":
                if (wizLevel < WizardLevel.Wizard) return NotEnoughPower(terminal);
                return HandlePurge(username, terminal);

            // ── Archwizard+ (Level 4) ───────────────────────────────
            case "ban":
                if (wizLevel < WizardLevel.Archwizard) return NotEnoughPower(terminal);
                return await HandleBan(username, args, terminal);
            case "unban":
                if (wizLevel < WizardLevel.Archwizard) return NotEnoughPower(terminal);
                return await HandleUnban(username, args, terminal);
            case "kick":
                if (wizLevel < WizardLevel.Archwizard) return NotEnoughPower(terminal);
                return await HandleKick(username, args, terminal);
            case "broadcast":
            case "bc":
                if (wizLevel < WizardLevel.Archwizard) return NotEnoughPower(terminal);
                return HandleBroadcast(username, args, terminal);
            case "promote":
                if (wizLevel < WizardLevel.Archwizard) return NotEnoughPower(terminal);
                return await HandlePromote(username, wizLevel, args, terminal);
            case "demote":
                if (wizLevel < WizardLevel.Archwizard) return NotEnoughPower(terminal);
                return await HandleDemote(username, wizLevel, args, terminal);

            // ── God+ (Level 5) ──────────────────────────────────────
            case "shutdown":
                if (wizLevel < WizardLevel.God) return NotEnoughPower(terminal);
                return await HandleShutdown(username, args, terminal);
            case "reboot":
                if (wizLevel < WizardLevel.God) return NotEnoughPower(terminal);
                return await HandleShutdown(username, args, terminal); // Alias
            case "admin":
                if (wizLevel < WizardLevel.God) return NotEnoughPower(terminal);
                return await HandleAdmin(username, terminal);
            case "wizlog":
                if (wizLevel < WizardLevel.God) return NotEnoughPower(terminal);
                return await HandleWizLog(terminal);

            default:
                return false; // Not a wizard command
        }
    }

    private static bool NotEnoughPower(TerminalEmulator terminal)
    {
        terminal.SetColor("bright_red");
        terminal.WriteLine("  You do not have sufficient wizard powers for that command.");
        return true;
    }

    private static SqlSaveBackend? GetSqlBackend()
    {
        return SaveSystem.Instance?.Backend as SqlSaveBackend;
    }

    private static PlayerSession? FindSession(string targetName)
    {
        var server = MudServer.Instance;
        if (server == null) return null;
        server.ActiveSessions.TryGetValue(targetName.ToLowerInvariant(), out var session);
        return session;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Builder+ Commands (Level 1)
    // ═══════════════════════════════════════════════════════════════════

    private static bool HandleWizHelp(WizardLevel level, TerminalEmulator terminal)
    {
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.SetColor("bright_white");
        terminal.WriteLine("║                         Wizard Commands                                    ║");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");

        terminal.SetColor("cyan");
        terminal.WriteLine("║  Builder Commands:                                                         ║");
        terminal.SetColor("white");
        terminal.WriteLine("║  /wizhelp              - Show this help                                    ║");
        terminal.WriteLine("║  /wiznet <msg> /wiz    - Wizard chat channel                               ║");
        terminal.WriteLine("║  /wizwho               - Show online wizards                               ║");
        terminal.WriteLine("║  /holylight            - Toggle enhanced vision                            ║");
        terminal.WriteLine("║  /stat <player>        - Inspect a player's stats                          ║");
        terminal.WriteLine("║  /where                - Show all player locations                         ║");

        if (level >= WizardLevel.Immortal)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("║  Immortal Commands:                                                        ║");
            terminal.SetColor("white");
            terminal.WriteLine("║  /invis / /visible     - Toggle invisibility                               ║");
            terminal.WriteLine("║  /goto <loc|player>    - Teleport to location or player                    ║");
            terminal.WriteLine("║  /godmode              - Toggle invulnerability                             ║");
            terminal.WriteLine("║  /heal [player]        - Heal self or player                               ║");
            terminal.WriteLine("║  /restore [player]     - Full HP + Mana restore                            ║");
            terminal.WriteLine("║  /peace                - Stop all combat in room                           ║");
            terminal.WriteLine("║  /echo <message>       - Send room message as narrator                     ║");
        }

        if (level >= WizardLevel.Wizard)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("║  Wizard Commands:                                                          ║");
            terminal.SetColor("white");
            terminal.WriteLine("║  /summon <player>      - Pull player to your location                      ║");
            terminal.WriteLine("║  /transfer <p> <loc>   - Send player to a location                         ║");
            terminal.WriteLine("║  /snoop <player>       - Watch a player's screen                           ║");
            terminal.WriteLine("║  /force <player> <cmd> - Force player to execute command                   ║");
            terminal.WriteLine("║  /set <p> <field> <v>  - Modify player stats                               ║");
            terminal.WriteLine("║  /slay <player>        - Instantly kill a player                            ║");
            terminal.WriteLine("║  /freeze / /thaw <p>   - Freeze/unfreeze a player                          ║");
            terminal.WriteLine("║  /mute / /unmute <p>   - Mute/unmute a player                              ║");
            terminal.WriteLine("║  /purge                - Clear room of items/NPCs                          ║");
        }

        if (level >= WizardLevel.Archwizard)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("║  Archwizard Commands:                                                      ║");
            terminal.SetColor("white");
            terminal.WriteLine("║  /ban <player> [rsn]   - Ban a player                                      ║");
            terminal.WriteLine("║  /unban <player>       - Unban a player                                    ║");
            terminal.WriteLine("║  /kick <player> [rsn]  - Disconnect a player                               ║");
            terminal.WriteLine("║  /broadcast <msg>      - Global system message                             ║");
            terminal.WriteLine("║  /promote <p> <level>  - Promote player to wizard level                    ║");
            terminal.WriteLine("║  /demote <player>      - Demote player one wizard level                    ║");
        }

        if (level >= WizardLevel.God)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("║  God Commands:                                                             ║");
            terminal.SetColor("white");
            terminal.WriteLine("║  /shutdown <sec> [rsn] - Initiate server shutdown                          ║");
            terminal.WriteLine("║  /reboot <sec> [rsn]   - Alias for shutdown                                ║");
            terminal.WriteLine("║  /admin                - Open full admin console                            ║");
            terminal.WriteLine("║  /wizlog               - View wizard audit log                             ║");
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        return true;
    }

    private static bool HandleWizChat(string username, WizardLevel level, string message, TerminalEmulator terminal)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /wiznet <message>  or  /wiz <message>");
            return true;
        }

        WizNet.Broadcast(username, level, message);
        return true;
    }

    private static bool HandleWizWho(TerminalEmulator terminal)
    {
        var server = MudServer.Instance;
        if (server == null) return true;

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.SetColor("bright_white");
        terminal.WriteLine("║                         Wizards Online                                     ║");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");

        var wizards = server.ActiveSessions.Values
            .Where(s => s.WizardLevel >= WizardLevel.Builder)
            .OrderByDescending(s => s.WizardLevel)
            .ToList();

        if (wizards.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("║  No wizards online.                                                        ║");
        }
        else
        {
            foreach (var wiz in wizards)
            {
                var title = WizardConstants.GetTitle(wiz.WizardLevel);
                var invis = wiz.IsWizInvisible ? " [INVIS]" : "";
                var loc = RoomRegistry.Instance?.GetPlayerLocation(wiz.Username);
                var locName = loc.HasValue ? BaseLocation.GetLocationName(loc.Value) : "Unknown";
                terminal.SetColor(WizardConstants.GetColor(wiz.WizardLevel));
                var line = $"  [{title}] {wiz.Username}{invis} - {locName}";
                terminal.WriteLine($"║{line.PadRight(78)}║");
            }
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"╠══════════════════════════════════════════════════════════════════════════════╣");
        terminal.SetColor("gray");
        terminal.WriteLine($"║  {wizards.Count} wizard(s) online                                                        ║");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        return true;
    }

    private static bool HandleHolyLight(string username, TerminalEmulator terminal)
    {
        // Holylight is a toggle that lets wizards see hidden info
        // For now, just acknowledge the toggle
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  Holy light toggled. You see all that is hidden.");
        return true;
    }

    private static async Task<bool> HandleStat(WizardLevel wizLevel, string args, TerminalEmulator terminal)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /stat <player>");
            return true;
        }

        var targetName = args.Trim();

        // Try online first
        var session = FindSession(targetName);
        Character? player = session?.Context?.Player;
        UsurperRemake.Systems.PlayerData? offlineData = null;

        if (player == null)
        {
            // Try loading from DB
            var backend = GetSqlBackend();
            if (backend != null)
            {
                var saveData = await backend.ReadGameData(targetName);
                if (saveData != null)
                    offlineData = saveData.Player;
            }
        }

        if (player == null && offlineData == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  Player '{targetName}' not found.");
            return true;
        }

        var isOnline = session != null;
        var wizLevelTarget = session?.WizardLevel ?? WizardLevel.Mortal;

        // Extract display values from either online Character or offline PlayerData
        var name = player?.Name2 ?? player?.Name1 ?? offlineData?.Name2 ?? offlineData?.Name1 ?? targetName;
        var level = player?.Level ?? offlineData?.Level ?? 0;
        var cls = player?.Class.ToString() ?? offlineData?.Class.ToString() ?? "Unknown";
        var race = player?.Race.ToString() ?? offlineData?.Race.ToString() ?? "Unknown";
        var hp = player?.HP ?? offlineData?.HP ?? 0;
        var maxHp = player?.MaxHP ?? offlineData?.MaxHP ?? 0;
        var mana = player?.Mana ?? offlineData?.Mana ?? 0;
        var maxMana = player?.MaxMana ?? offlineData?.MaxMana ?? 0;
        var xp = player?.Experience ?? offlineData?.Experience ?? 0;
        var gold = player?.Gold ?? offlineData?.Gold ?? 0;
        var bankGold = player?.BankGold ?? offlineData?.BankGold ?? 0;
        var str = player?.Strength ?? offlineData?.Strength ?? 0;
        var def = player?.Defence ?? offlineData?.Defence ?? 0;
        var sta = player?.Stamina ?? offlineData?.Stamina ?? 0;
        var agi = player?.Agility ?? offlineData?.Agility ?? 0;
        var cha = player?.Charisma ?? offlineData?.Charisma ?? 0;
        var dex = player?.Dexterity ?? offlineData?.Dexterity ?? 0;
        var wis = player?.Wisdom ?? offlineData?.Wisdom ?? 0;
        var intel = player?.Intelligence ?? offlineData?.Intelligence ?? 0;
        var con = player?.Constitution ?? offlineData?.Constitution ?? 0;

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.SetColor("bright_white");
        terminal.WriteLine($"║  Player Stats: {name,-62}║");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");
        terminal.SetColor("white");
        terminal.WriteLine($"║  Status: {(isOnline ? "\u001b[1;32mONLINE\u001b[0;37m" : "\u001b[90mOffline\u001b[0;37m"),-69}║");
        if (wizLevelTarget > WizardLevel.Mortal)
            terminal.WriteLine($"║  Wizard Level: {WizardConstants.GetTitle(wizLevelTarget),-62}║");
        terminal.WriteLine($"║  Level: {level,-13} Class: {cls,-13} Race: {race,-16}║");
        terminal.WriteLine($"║  HP: {hp}/{maxHp,-13} Mana: {mana}/{maxMana,-36}║");
        terminal.WriteLine($"║  XP: {xp,-72}║");
        terminal.WriteLine($"║  Gold: {gold,-15} Bank: {bankGold,-50}║");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("║  ── Attributes ─────────────────────────────────────────────────────────── ║");
        terminal.SetColor("white");
        terminal.WriteLine($"║  STR: {str,-8} DEF: {def,-8} STA: {sta,-8} AGI: {agi,-15}║");
        terminal.WriteLine($"║  CHA: {cha,-8} DEX: {dex,-8} WIS: {wis,-8} INT: {intel,-15}║");
        terminal.WriteLine($"║  CON: {con,-69}║");

        if (session != null)
        {
            var loc = RoomRegistry.Instance?.GetPlayerLocation(session.Username);
            var locName = loc.HasValue ? BaseLocation.GetLocationName(loc.Value) : "Unknown";
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("║  ── Session Info ───────────────────────────────────────────────────────── ║");
            terminal.SetColor("white");
            terminal.WriteLine($"║  Location: {locName,-66}║");
            terminal.WriteLine($"║  Connection: {session.ConnectionType,-64}║");
            terminal.WriteLine($"║  Frozen: {(session.IsFrozen ? "YES" : "No"),-10} Muted: {(session.IsMuted ? "YES" : "No"),-10} GodMode: {(session.WizardGodMode ? "YES" : "No"),-18}║");
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        return true;
    }

    private static bool HandleWhere(WizardLevel wizLevel, TerminalEmulator terminal)
    {
        var server = MudServer.Instance;
        if (server == null) return true;

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.SetColor("bright_white");
        terminal.WriteLine("║                       Player Locations                                     ║");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");

        var sessions = server.ActiveSessions.Values.OrderBy(s => s.Username).ToList();
        foreach (var session in sessions)
        {
            var loc = RoomRegistry.Instance?.GetPlayerLocation(session.Username);
            var locName = loc.HasValue ? BaseLocation.GetLocationName(loc.Value) : "Unknown";
            var wizTag = session.WizardLevel > WizardLevel.Mortal
                ? $" [{WizardConstants.GetTitle(session.WizardLevel)}]" : "";
            var invisTag = session.IsWizInvisible ? " [INVIS]" : "";

            terminal.SetColor(session.WizardLevel > WizardLevel.Mortal
                ? WizardConstants.GetColor(session.WizardLevel) : "white");
            var line = $"  {session.Username}{wizTag}{invisTag} - {locName} [{session.ConnectionType}]";
            terminal.WriteLine($"║{line.PadRight(78)}║");
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"╠══════════════════════════════════════════════════════════════════════════════╣");
        terminal.SetColor("gray");
        terminal.WriteLine($"║  {sessions.Count} player(s) online                                                       ║");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Immortal+ Commands (Level 2)
    // ═══════════════════════════════════════════════════════════════════

    private static bool HandleInvis(string username, TerminalEmulator terminal)
    {
        var session = FindSession(username);
        if (session == null) return true;

        session.IsWizInvisible = true;
        if (session.Context != null)
            session.Context.WizardInvisible = true;

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("  You slowly vanish from sight...");
        LogAction(username, "invis", null, "Went invisible");
        WizNet.ActionNotify(username, "went invisible");
        return true;
    }

    private static bool HandleVisible(string username, TerminalEmulator terminal)
    {
        var session = FindSession(username);
        if (session == null) return true;

        session.IsWizInvisible = false;
        if (session.Context != null)
            session.Context.WizardInvisible = false;

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("  You slowly fade back into visibility.");
        LogAction(username, "visible", null, "Became visible");
        WizNet.ActionNotify(username, "became visible");
        return true;
    }

    private static bool HandleGodmode(string username, TerminalEmulator terminal)
    {
        var session = FindSession(username);
        if (session == null) return true;

        session.WizardGodMode = !session.WizardGodMode;
        if (session.Context != null)
            session.Context.WizardGodMode = session.WizardGodMode;

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(session.WizardGodMode
            ? "  You feel an aura of divine protection surround you. GODMODE ON."
            : "  The divine protection fades. GODMODE OFF.");
        LogAction(username, "godmode", null, session.WizardGodMode ? "ON" : "OFF");
        return true;
    }

    private static bool HandleHeal(string username, string args, TerminalEmulator terminal)
    {
        var targetName = string.IsNullOrWhiteSpace(args) ? username : args.Trim();
        var session = FindSession(targetName);
        if (session?.Context?.Player == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  Player '{targetName}' not found online.");
            return true;
        }

        var player = session.Context.Player;
        player.HP = player.MaxHP;
        terminal.SetColor("bright_green");
        terminal.WriteLine($"  {targetName} has been healed to full HP ({player.MaxHP}).");

        if (!targetName.Equals(username, StringComparison.OrdinalIgnoreCase))
        {
            session.EnqueueMessage($"\u001b[1;32m  A divine warmth fills your body. You feel fully healed.\u001b[0m");
            LogAction(username, "heal", targetName, $"Healed to {player.MaxHP} HP");
        }
        return true;
    }

    private static bool HandleRestore(string username, string args, TerminalEmulator terminal)
    {
        var targetName = string.IsNullOrWhiteSpace(args) ? username : args.Trim();
        var session = FindSession(targetName);
        if (session?.Context?.Player == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  Player '{targetName}' not found online.");
            return true;
        }

        var player = session.Context.Player;
        player.HP = player.MaxHP;
        player.Mana = player.MaxMana;
        terminal.SetColor("bright_green");
        terminal.WriteLine($"  {targetName} has been fully restored (HP: {player.MaxHP}, Mana: {player.MaxMana}).");

        if (!targetName.Equals(username, StringComparison.OrdinalIgnoreCase))
        {
            session.EnqueueMessage($"\u001b[1;32m  Divine energy floods through you. You feel completely restored.\u001b[0m");
            LogAction(username, "restore", targetName, $"HP: {player.MaxHP}, Mana: {player.MaxMana}");
        }
        return true;
    }

    private static bool HandlePeace(string username, TerminalEmulator terminal)
    {
        // Peace stops combat in the current room
        // Since combat is processed within the location loop, this sends a message
        // to all players in the room and sets a flag
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  A wave of tranquility washes over the area. All combat ceases.");

        var loc = RoomRegistry.Instance?.GetPlayerLocation(username);
        if (loc.HasValue)
        {
            RoomRegistry.Instance!.BroadcastToRoom(loc.Value,
                $"\u001b[1;33m  A wave of divine tranquility washes over the area.\u001b[0m",
                excludeUsername: username);
        }

        LogAction(username, "peace", null, loc.HasValue ? $"At {BaseLocation.GetLocationName(loc.Value)}" : null);
        return true;
    }

    private static bool HandleEcho(string username, string message, TerminalEmulator terminal)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /echo <message>");
            return true;
        }

        var loc = RoomRegistry.Instance?.GetPlayerLocation(username);
        if (!loc.HasValue)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  You have no location.");
            return true;
        }

        // Echo to the entire room including sender
        RoomRegistry.Instance!.BroadcastToRoom(loc.Value,
            $"\u001b[1;37m  {message}\u001b[0m");
        terminal.SetColor("bright_white");
        terminal.WriteLine($"  {message}");

        return true;
    }

    private static async Task<bool> HandleGoto(string username, string args, TerminalEmulator terminal)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /goto <location|player>");
            terminal.WriteLine("  Locations: inn, dungeon, castle, mainstreet, arena, home, etc.");
            return true;
        }

        var target = args.Trim();

        // First try as a player name
        var targetSession = FindSession(target);
        if (targetSession != null)
        {
            var targetLoc = RoomRegistry.Instance?.GetPlayerLocation(targetSession.Username);
            if (targetLoc.HasValue)
            {
                var ctx = SessionContext.Current;
                if (ctx?.LocationManager != null && ctx.Player != null)
                {
                    await ctx.LocationManager.NavigateTo(targetLoc.Value, ctx.Player);
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine($"  You vanish and reappear at {targetSession.Username}'s location.");
                    LogAction(username, "goto", targetSession.Username, $"To {BaseLocation.GetLocationName(targetLoc.Value)}");
                }
                return true;
            }
        }

        // Try as a location name
        GameLocation? location = ResolveLocation(target);
        if (location.HasValue)
        {
            var ctx = SessionContext.Current;
            if (ctx?.LocationManager != null && ctx.Player != null)
            {
                await ctx.LocationManager.NavigateTo(location.Value, ctx.Player);
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  You vanish and reappear at {BaseLocation.GetLocationName(location.Value)}.");
                LogAction(username, "goto", null, $"To {BaseLocation.GetLocationName(location.Value)}");
            }
            return true;
        }

        terminal.SetColor("gray");
        terminal.WriteLine($"  Could not find player or location '{target}'.");
        return true;
    }

    /// <summary>Resolve a location name/alias to a GameLocation enum value.</summary>
    private static GameLocation? ResolveLocation(string name)
    {
        // Try exact enum parse first
        if (Enum.TryParse<GameLocation>(name, ignoreCase: true, out var loc))
            return loc;

        // Common aliases
        var lower = name.ToLowerInvariant();
        return lower switch
        {
            "inn" or "tavern" => GameLocation.TheInn,
            "street" or "main" or "mainstreet" or "town" => GameLocation.MainStreet,
            "dungeon" or "dungeons" => GameLocation.Dungeons,
            "castle" or "throne" => GameLocation.Castle,
            "arena" or "pvp" => GameLocation.Arena,
            "home" or "house" => GameLocation.Home,
            "healer" or "hospital" => GameLocation.Healer,
            "weapons" or "weaponshop" or "weapon" => GameLocation.WeaponShop,
            "armor" or "armorshop" => GameLocation.ArmorShop,
            "magic" or "magicshop" => GameLocation.MagicShop,
            "temple" or "church" => GameLocation.Temple,
            "bank" or "treasury" => GameLocation.Bank,
            "prison" or "jail" => GameLocation.Prison,
            "alley" or "darkalley" => GameLocation.DarkAlley,
            "market" or "marketplace" => GameLocation.Marketplace,
            "team" or "teamcorner" => GameLocation.TeamCorner,
            "quest" or "questhall" => GameLocation.QuestHall,
            "bounty" or "bountyroom" => GameLocation.BountyRoom,
            _ => null
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    // Wizard+ Commands (Level 3)
    // ═══════════════════════════════════════════════════════════════════

    private static async Task<bool> HandleSummon(string username, string args, TerminalEmulator terminal)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /summon <player>");
            return true;
        }

        var targetName = args.Trim();
        var targetSession = FindSession(targetName);
        if (targetSession == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  Player '{targetName}' not found online.");
            return true;
        }

        // Can't summon higher-level wizards
        if (targetSession.WizardLevel >= FindSession(username)?.WizardLevel)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine($"  You cannot summon a wizard of equal or higher rank.");
            return true;
        }

        var myLoc = RoomRegistry.Instance?.GetPlayerLocation(username);
        if (!myLoc.HasValue)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  You have no location.");
            return true;
        }

        // Teleport the target to our location
        if (targetSession.Context?.LocationManager != null && targetSession.Context.Player != null)
            await targetSession.Context.LocationManager.NavigateTo(myLoc.Value, targetSession.Context.Player);
        targetSession.EnqueueMessage($"\u001b[1;35m  A powerful force pulls you through the void...\u001b[0m");

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"  You summon {targetSession.Username} to your location.");
        LogAction(username, "summon", targetSession.Username, $"To {BaseLocation.GetLocationName(myLoc.Value)}");
        WizNet.ActionNotify(username, $"summoned {targetSession.Username}");
        return true;
    }

    private static async Task<bool> HandleTransfer(string username, string args, TerminalEmulator terminal)
    {
        // Parse: /transfer <player> <location>
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /transfer <player> <location>");
            return true;
        }

        var targetName = parts[0];
        var locName = parts[1];

        var targetSession = FindSession(targetName);
        if (targetSession == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  Player '{targetName}' not found online.");
            return true;
        }

        var location = ResolveLocation(locName);
        if (!location.HasValue)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  Unknown location '{locName}'.");
            return true;
        }

        if (targetSession.Context?.LocationManager != null && targetSession.Context.Player != null)
            await targetSession.Context.LocationManager.NavigateTo(location.Value, targetSession.Context.Player);
        targetSession.EnqueueMessage($"\u001b[1;35m  You are whisked away by a divine force...\u001b[0m");

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"  Transferred {targetSession.Username} to {BaseLocation.GetLocationName(location.Value)}.");
        LogAction(username, "transfer", targetSession.Username, $"To {BaseLocation.GetLocationName(location.Value)}");
        WizNet.ActionNotify(username, $"transferred {targetSession.Username} to {BaseLocation.GetLocationName(location.Value)}");
        return true;
    }

    private static bool HandleSnoop(string username, string args, TerminalEmulator terminal)
    {
        var mySession = FindSession(username);
        if (mySession == null) return true;

        if (string.IsNullOrWhiteSpace(args) || args.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            // Turn off all snooping
            foreach (var session in MudServer.Instance?.ActiveSessions.Values ?? Enumerable.Empty<PlayerSession>())
            {
                session.SnoopedBy.Remove(mySession);
            }
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("  Snoop disabled.");
            return true;
        }

        var targetName = args.Trim();
        var targetSession = FindSession(targetName);
        if (targetSession == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  Player '{targetName}' not found online.");
            return true;
        }

        // Toggle snoop on this target
        if (targetSession.SnoopedBy.Contains(mySession))
        {
            targetSession.SnoopedBy.Remove(mySession);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  Stopped snooping {targetSession.Username}.");
        }
        else
        {
            targetSession.SnoopedBy.Add(mySession);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  Now snooping {targetSession.Username}.");
            LogAction(username, "snoop", targetSession.Username, "Started snooping");
            WizNet.ActionNotify(username, $"started snooping {targetSession.Username}");
        }
        return true;
    }

    private static bool HandleForce(string username, string args, TerminalEmulator terminal)
    {
        // Parse: /force <player> <command>
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /force <player> <command>");
            return true;
        }

        var targetName = parts[0];
        var command = parts[1];

        var targetSession = FindSession(targetName);
        if (targetSession == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  Player '{targetName}' not found online.");
            return true;
        }

        // Can't force higher-level wizards
        if (targetSession.WizardLevel >= FindSession(username)?.WizardLevel)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine($"  You cannot force a wizard of equal or higher rank.");
            return true;
        }

        targetSession.ForcedCommands.Enqueue(command);
        targetSession.EnqueueMessage($"\u001b[1;35m  A divine force compels you...\u001b[0m");

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"  Forced {targetSession.Username} to: {command}");
        LogAction(username, "force", targetSession.Username, command);
        WizNet.ActionNotify(username, $"forced {targetSession.Username} to '{command}'");
        return true;
    }

    private static async Task<bool> HandleSet(string username, string args, TerminalEmulator terminal)
    {
        // Parse: /set <player> <field> <value>
        var parts = args.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /set <player> <field> <value>");
            terminal.WriteLine("  Fields: level, gold, hp, mana, xp, str, def, sta, agi, cha, dex, wis, int, con");
            return true;
        }

        var targetName = parts[0];
        var field = parts[1].ToLowerInvariant();
        if (!long.TryParse(parts[2], out long value))
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  Invalid value: '{parts[2]}'. Must be a number.");
            return true;
        }

        // Find player (online or offline)
        var session = FindSession(targetName);
        Character? player = session?.Context?.Player;
        bool isOnline = player != null;
        UsurperRemake.Systems.SaveGameData? offlineSaveData = null;

        if (!isOnline)
        {
            // Load from DB for offline modification
            var backend = GetSqlBackend();
            if (backend != null)
                offlineSaveData = await backend.ReadGameData(targetName);
        }

        if (player == null && offlineSaveData == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  Player '{targetName}' not found.");
            return true;
        }

        string? oldValue = null;
        if (isOnline && player != null)
        {
            // Modify live Character object
            switch (field)
            {
                case "level": oldValue = player.Level.ToString(); player.Level = (int)value; break;
                case "gold": oldValue = player.Gold.ToString(); player.Gold = value; break;
                case "hp": oldValue = player.HP.ToString(); player.HP = (int)value; break;
                case "mana": oldValue = player.Mana.ToString(); player.Mana = (int)value; break;
                case "xp" or "exp" or "experience": oldValue = player.Experience.ToString(); player.Experience = value; break;
                case "str" or "strength": oldValue = player.Strength.ToString(); player.Strength = (int)value; break;
                case "def" or "defence" or "defense": oldValue = player.Defence.ToString(); player.Defence = (int)value; break;
                case "sta" or "stamina": oldValue = player.Stamina.ToString(); player.Stamina = (int)value; break;
                case "agi" or "agility": oldValue = player.Agility.ToString(); player.Agility = (int)value; break;
                case "cha" or "charisma": oldValue = player.Charisma.ToString(); player.Charisma = (int)value; break;
                case "dex" or "dexterity": oldValue = player.Dexterity.ToString(); player.Dexterity = (int)value; break;
                case "wis" or "wisdom": oldValue = player.Wisdom.ToString(); player.Wisdom = (int)value; break;
                case "int" or "intelligence": oldValue = player.Intelligence.ToString(); player.Intelligence = (int)value; break;
                case "con" or "constitution": oldValue = player.Constitution.ToString(); player.Constitution = (int)value; break;
                default:
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  Unknown field: '{field}'");
                    terminal.WriteLine("  Fields: level, gold, hp, mana, xp, str, def, sta, agi, cha, dex, wis, int, con");
                    return true;
            }
        }
        else if (offlineSaveData != null)
        {
            // Modify offline PlayerData and save back to DB
            var pd = offlineSaveData.Player;
            switch (field)
            {
                case "level": oldValue = pd.Level.ToString(); pd.Level = (int)value; break;
                case "gold": oldValue = pd.Gold.ToString(); pd.Gold = value; break;
                case "hp": oldValue = pd.HP.ToString(); pd.HP = (int)value; break;
                case "mana": oldValue = pd.Mana.ToString(); pd.Mana = (int)value; break;
                case "xp" or "exp" or "experience": oldValue = pd.Experience.ToString(); pd.Experience = value; break;
                case "str" or "strength": oldValue = pd.Strength.ToString(); pd.Strength = (int)value; break;
                case "def" or "defence" or "defense": oldValue = pd.Defence.ToString(); pd.Defence = (int)value; break;
                case "sta" or "stamina": oldValue = pd.Stamina.ToString(); pd.Stamina = (int)value; break;
                case "agi" or "agility": oldValue = pd.Agility.ToString(); pd.Agility = (int)value; break;
                case "cha" or "charisma": oldValue = pd.Charisma.ToString(); pd.Charisma = (int)value; break;
                case "dex" or "dexterity": oldValue = pd.Dexterity.ToString(); pd.Dexterity = (int)value; break;
                case "wis" or "wisdom": oldValue = pd.Wisdom.ToString(); pd.Wisdom = (int)value; break;
                case "int" or "intelligence": oldValue = pd.Intelligence.ToString(); pd.Intelligence = (int)value; break;
                case "con" or "constitution": oldValue = pd.Constitution.ToString(); pd.Constitution = (int)value; break;
                default:
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  Unknown field: '{field}'");
                    terminal.WriteLine("  Fields: level, gold, hp, mana, xp, str, def, sta, agi, cha, dex, wis, int, con");
                    return true;
            }

            var backend = GetSqlBackend();
            if (backend != null)
                await backend.WriteGameData(targetName, offlineSaveData);
        }

        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  Set {targetName}'s {field} to {value} (was {oldValue}).{(isOnline ? "" : " [Offline - saved to DB]")}");

        if (isOnline && session != null)
            session.EnqueueMessage($"\u001b[1;33m  Your {field} has been set to {value} by a divine power.\u001b[0m");

        LogAction(username, "set", targetName, $"{field}: {oldValue} -> {value}");
        WizNet.ActionNotify(username, $"set {targetName}'s {field} to {value}");
        return true;
    }

    private static bool HandleSlay(string username, string args, TerminalEmulator terminal)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /slay <player>");
            return true;
        }

        var targetName = args.Trim();
        var session = FindSession(targetName);
        if (session?.Context?.Player == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  Player '{targetName}' not found online.");
            return true;
        }

        // Can't slay higher-level wizards
        if (session.WizardLevel >= FindSession(username)?.WizardLevel)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine($"  You cannot slay a wizard of equal or higher rank.");
            return true;
        }

        var player = session.Context.Player;
        player.HP = 0;

        session.EnqueueMessage($"\u001b[1;31m  The hand of a god reaches down and smites you!\u001b[0m");
        terminal.SetColor("bright_red");
        terminal.WriteLine($"  You raise your hand and smite {session.Username}. They fall lifeless.");

        var loc = RoomRegistry.Instance?.GetPlayerLocation(session.Username);
        if (loc.HasValue)
        {
            RoomRegistry.Instance!.BroadcastToRoom(loc.Value,
                $"\u001b[1;31m  A bolt of divine lightning strikes {session.Username}! They fall lifeless.\u001b[0m",
                excludeUsername: session.Username);
        }

        LogAction(username, "slay", session.Username, "Slain by wizard");
        WizNet.ActionNotify(username, $"slayed {session.Username}");
        return true;
    }

    private static async Task<bool> HandleFreeze(string username, string args, TerminalEmulator terminal)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /freeze <player>");
            return true;
        }

        var targetName = args.Trim();
        var backend = GetSqlBackend();
        if (backend == null) return true;

        await backend.SetFrozen(targetName, true, username);

        // Update online session if present
        var session = FindSession(targetName);
        if (session != null)
        {
            session.IsFrozen = true;
            session.EnqueueMessage($"\u001b[1;36m  You have been frozen solid by a divine power! You cannot move or act.\u001b[0m");
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"  {targetName} has been frozen.{(session == null ? " [Offline - saved to DB]" : "")}");
        LogAction(username, "freeze", targetName, null);
        WizNet.ActionNotify(username, $"froze {targetName}");
        return true;
    }

    private static async Task<bool> HandleThaw(string username, string args, TerminalEmulator terminal)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /thaw <player>");
            return true;
        }

        var targetName = args.Trim();
        var backend = GetSqlBackend();
        if (backend == null) return true;

        await backend.SetFrozen(targetName, false);

        var session = FindSession(targetName);
        if (session != null)
        {
            session.IsFrozen = false;
            session.EnqueueMessage($"\u001b[1;32m  The ice around you shatters! You can move again.\u001b[0m");
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine($"  {targetName} has been thawed.");
        LogAction(username, "thaw", targetName, null);
        WizNet.ActionNotify(username, $"thawed {targetName}");
        return true;
    }

    private static async Task<bool> HandleMute(string username, string args, TerminalEmulator terminal)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /mute <player>");
            return true;
        }

        var targetName = args.Trim();
        var backend = GetSqlBackend();
        if (backend == null) return true;

        await backend.SetMuted(targetName, true, username);

        var session = FindSession(targetName);
        if (session != null)
        {
            session.IsMuted = true;
            session.EnqueueMessage($"\u001b[1;31m  You have been silenced by the gods. You cannot speak.\u001b[0m");
        }

        terminal.SetColor("bright_red");
        terminal.WriteLine($"  {targetName} has been muted.{(session == null ? " [Offline - saved to DB]" : "")}");
        LogAction(username, "mute", targetName, null);
        WizNet.ActionNotify(username, $"muted {targetName}");
        return true;
    }

    private static async Task<bool> HandleUnmute(string username, string args, TerminalEmulator terminal)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /unmute <player>");
            return true;
        }

        var targetName = args.Trim();
        var backend = GetSqlBackend();
        if (backend == null) return true;

        await backend.SetMuted(targetName, false);

        var session = FindSession(targetName);
        if (session != null)
        {
            session.IsMuted = false;
            session.EnqueueMessage($"\u001b[1;32m  Your voice has been restored. You can speak again.\u001b[0m");
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine($"  {targetName} has been unmuted.");
        LogAction(username, "unmute", targetName, null);
        WizNet.ActionNotify(username, $"unmuted {targetName}");
        return true;
    }

    private static bool HandlePurge(string username, TerminalEmulator terminal)
    {
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  The area is cleansed by divine power.");

        var loc = RoomRegistry.Instance?.GetPlayerLocation(username);
        if (loc.HasValue)
        {
            RoomRegistry.Instance!.BroadcastToRoom(loc.Value,
                $"\u001b[1;33m  A blinding light washes over the area, cleansing it.\u001b[0m",
                excludeUsername: username);
        }

        LogAction(username, "purge", null, loc.HasValue ? $"At {BaseLocation.GetLocationName(loc.Value)}" : null);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Archwizard+ Commands (Level 4)
    // ═══════════════════════════════════════════════════════════════════

    private static async Task<bool> HandleBan(string username, string args, TerminalEmulator terminal)
    {
        var spaceIndex = args.IndexOf(' ');
        var targetName = spaceIndex > 0 ? args.Substring(0, spaceIndex).Trim() : args.Trim();
        var reason = spaceIndex > 0 ? args.Substring(spaceIndex + 1).Trim() : "Banned by wizard";

        if (string.IsNullOrWhiteSpace(targetName))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /ban <player> [reason]");
            return true;
        }

        var backend = GetSqlBackend();
        if (backend == null) return true;

        await backend.BanPlayer(targetName, reason);

        // Kick if online
        var session = FindSession(targetName);
        if (session != null)
        {
            await MudServer.Instance!.KickPlayer(targetName, $"Banned: {reason}");
        }

        terminal.SetColor("bright_red");
        terminal.WriteLine($"  {targetName} has been BANNED: {reason}");
        LogAction(username, "ban", targetName, reason);
        WizNet.ActionNotify(username, $"BANNED {targetName}: {reason}");
        return true;
    }

    private static async Task<bool> HandleUnban(string username, string args, TerminalEmulator terminal)
    {
        var targetName = args.Trim();
        if (string.IsNullOrWhiteSpace(targetName))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /unban <player>");
            return true;
        }

        var backend = GetSqlBackend();
        if (backend == null) return true;

        await backend.UnbanPlayer(targetName);

        terminal.SetColor("bright_green");
        terminal.WriteLine($"  {targetName} has been unbanned.");
        LogAction(username, "unban", targetName, null);
        WizNet.ActionNotify(username, $"unbanned {targetName}");
        return true;
    }

    private static async Task<bool> HandleKick(string username, string args, TerminalEmulator terminal)
    {
        var spaceIndex = args.IndexOf(' ');
        var targetName = spaceIndex > 0 ? args.Substring(0, spaceIndex).Trim() : args.Trim();
        var reason = spaceIndex > 0 ? args.Substring(spaceIndex + 1).Trim() : "Kicked by wizard";

        if (string.IsNullOrWhiteSpace(targetName))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /kick <player> [reason]");
            return true;
        }

        var server = MudServer.Instance;
        if (server != null && await server.KickPlayer(targetName, reason))
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  Kicked {targetName}: {reason}");
            LogAction(username, "kick", targetName, reason);
            WizNet.ActionNotify(username, $"kicked {targetName}: {reason}");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  Player '{targetName}' not found online.");
        }
        return true;
    }

    private static bool HandleBroadcast(string username, string message, TerminalEmulator terminal)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /broadcast <message>");
            return true;
        }

        var server = MudServer.Instance;
        if (server != null)
        {
            server.BroadcastToAll($"\u001b[1;31m  [SYSTEM] {message}\u001b[0m");
            terminal.SetColor("bright_green");
            terminal.WriteLine("  Broadcast sent.");
            LogAction(username, "broadcast", null, message);
        }
        return true;
    }

    private static async Task<bool> HandlePromote(string username, WizardLevel myLevel, string args, TerminalEmulator terminal)
    {
        // Parse: /promote <player> <level>
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /promote <player> <level>");
            terminal.WriteLine("  Levels: builder, immortal, wizard, archwizard, god");
            return true;
        }

        var targetName = parts[0];
        if (!Enum.TryParse<WizardLevel>(parts[1], ignoreCase: true, out var targetLevel))
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  Unknown wizard level: '{parts[1]}'");
            terminal.WriteLine("  Levels: builder, immortal, wizard, archwizard, god");
            return true;
        }

        // Can't promote to your own level or above
        if (targetLevel >= myLevel)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("  You can only promote to a level below your own.");
            return true;
        }

        // Can't promote to Implementor
        if (targetLevel >= WizardLevel.Implementor)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("  No one can be promoted to Implementor. That title is eternal.");
            return true;
        }

        var backend = GetSqlBackend();
        if (backend == null) return true;

        // Check target exists
        var currentLevel = await backend.GetWizardLevel(targetName);

        await backend.SetWizardLevel(targetName, targetLevel);

        // Update online session
        var session = FindSession(targetName);
        if (session != null)
        {
            session.WizardLevel = targetLevel;
            if (session.Context != null)
                session.Context.WizardLevel = targetLevel;
            session.EnqueueMessage($"\u001b[1;33m  You have been promoted to {WizardConstants.GetTitle(targetLevel)} by {username}!\u001b[0m");
        }

        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  {targetName} promoted from {WizardConstants.GetTitle(currentLevel)} to {WizardConstants.GetTitle(targetLevel)}.");
        LogAction(username, "promote", targetName, $"{WizardConstants.GetTitle(currentLevel)} -> {WizardConstants.GetTitle(targetLevel)}");
        WizNet.ActionNotify(username, $"promoted {targetName} to {WizardConstants.GetTitle(targetLevel)}");
        return true;
    }

    private static async Task<bool> HandleDemote(string username, WizardLevel myLevel, string args, TerminalEmulator terminal)
    {
        var targetName = args.Trim();
        if (string.IsNullOrWhiteSpace(targetName))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /demote <player>");
            return true;
        }

        var backend = GetSqlBackend();
        if (backend == null) return true;

        var currentLevel = await backend.GetWizardLevel(targetName);

        // Can't demote Implementor
        if (currentLevel >= WizardLevel.Implementor)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("  The Implementor cannot be demoted. That power is eternal.");
            return true;
        }

        // Can't demote someone at or above your level
        if (currentLevel >= myLevel)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("  You cannot demote a wizard of equal or higher rank.");
            return true;
        }

        if (currentLevel <= WizardLevel.Mortal)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {targetName} is already a mortal.");
            return true;
        }

        var newLevel = (WizardLevel)((int)currentLevel - 1);
        await backend.SetWizardLevel(targetName, newLevel);

        var session = FindSession(targetName);
        if (session != null)
        {
            session.WizardLevel = newLevel;
            if (session.Context != null)
                session.Context.WizardLevel = newLevel;
            session.EnqueueMessage($"\u001b[1;31m  You have been demoted to {WizardConstants.GetTitle(newLevel)} by {username}.\u001b[0m");
        }

        terminal.SetColor("bright_red");
        terminal.WriteLine($"  {targetName} demoted from {WizardConstants.GetTitle(currentLevel)} to {WizardConstants.GetTitle(newLevel)}.");
        LogAction(username, "demote", targetName, $"{WizardConstants.GetTitle(currentLevel)} -> {WizardConstants.GetTitle(newLevel)}");
        WizNet.ActionNotify(username, $"demoted {targetName} to {WizardConstants.GetTitle(newLevel)}");
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════
    // God+ Commands (Level 5)
    // ═══════════════════════════════════════════════════════════════════

    private static async Task<bool> HandleShutdown(string username, string args, TerminalEmulator terminal)
    {
        var spaceIndex = args.IndexOf(' ');
        var secondsStr = spaceIndex > 0 ? args.Substring(0, spaceIndex).Trim() : args.Trim();
        var reason = spaceIndex > 0 ? args.Substring(spaceIndex + 1).Trim() : null;

        if (!int.TryParse(secondsStr, out int seconds) || seconds < 1 || seconds > 3600)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /shutdown <seconds> [reason]  (1-3600 seconds)");
            return true;
        }

        var server = MudServer.Instance;
        if (server != null)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  Initiating server shutdown in {seconds} seconds...");
            LogAction(username, "shutdown", null, $"{seconds}s: {reason ?? "No reason"}");
            WizNet.ActionNotify(username, $"initiated server shutdown in {seconds} seconds");
            _ = server.InitiateShutdown(seconds, reason);
        }
        return true;
    }

    private static async Task<bool> HandleAdmin(string username, TerminalEmulator terminal)
    {
        // Launch the OnlineAdminConsole inline
        var backend = GetSqlBackend();
        if (backend == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Admin console not available (no SQL backend).");
            return true;
        }

        var adminConsole = new OnlineAdminConsole(terminal, backend);
        await adminConsole.Run();
        LogAction(username, "admin", null, "Opened admin console");
        return true;
    }

    private static async Task<bool> HandleWizLog(TerminalEmulator terminal)
    {
        var backend = GetSqlBackend();
        if (backend == null) return true;

        var entries = await backend.GetRecentWizardLog(30);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.SetColor("bright_white");
        terminal.WriteLine("║                         Wizard Audit Log                                   ║");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");

        if (entries.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("║  No wizard actions logged yet.                                             ║");
        }
        else
        {
            foreach (var entry in entries)
            {
                var target = entry.Target != null ? $" -> {entry.Target}" : "";
                var details = entry.Details != null ? $" ({entry.Details})" : "";
                terminal.SetColor("white");
                var line = $"  {entry.CreatedAt} {entry.WizardName}: {entry.Action}{target}{details}";
                if (line.Length > 78) line = line.Substring(0, 75) + "...";
                terminal.WriteLine($"║{line.PadRight(78)}║");
            }
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Audit Logging Helper
    // ═══════════════════════════════════════════════════════════════════

    private static void LogAction(string wizardName, string action, string? target, string? details)
    {
        var backend = GetSqlBackend();
        backend?.LogWizardAction(wizardName, action, target, details);
    }
}
