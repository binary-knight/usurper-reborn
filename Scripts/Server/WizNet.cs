using System;
using System.Linq;

namespace UsurperRemake.Server;

/// <summary>
/// WizNet: wizard-only communication channel. Messages delivered instantly
/// via PlayerSession.EnqueueMessage() to all connected wizards (level >= Builder).
/// Classic MUD feature for wizard coordination and awareness.
/// </summary>
public static class WizNet
{
    /// <summary>
    /// Send a chat message on WizNet. Only delivered to sessions with WizardLevel >= Builder.
    /// Format: [WizNet] &lt;Title&gt; Name: message
    /// </summary>
    public static void Broadcast(string senderName, WizardLevel senderLevel, string message)
    {
        var server = MudServer.Instance;
        if (server == null) return;

        var title = WizardConstants.GetTitle(senderLevel);
        var color = WizardConstants.GetAnsiColor(senderLevel);
        var formatted = $"\u001b[1;36m  [WizNet] {color}{title} {senderName}\u001b[1;36m: {message}\u001b[0m";

        foreach (var kvp in server.ActiveSessions)
        {
            if (kvp.Value.WizardLevel >= WizardLevel.Builder)
            {
                kvp.Value.EnqueueMessage(formatted);
            }
        }
    }

    /// <summary>
    /// Send a system notification on WizNet (e.g., login/logout, server events).
    /// Format: [WizNet] :: message ::
    /// </summary>
    public static void SystemNotify(string message)
    {
        var server = MudServer.Instance;
        if (server == null) return;

        var formatted = $"\u001b[1;33m  [WizNet] :: {message} ::\u001b[0m";

        foreach (var kvp in server.ActiveSessions)
        {
            if (kvp.Value.WizardLevel >= WizardLevel.Builder)
            {
                kvp.Value.EnqueueMessage(formatted);
            }
        }
    }

    /// <summary>
    /// Send an action notification on WizNet (e.g., promote, ban, slay).
    /// Format: [WizNet] >> message
    /// </summary>
    public static void ActionNotify(string wizardName, string action)
    {
        var server = MudServer.Instance;
        if (server == null) return;

        var formatted = $"\u001b[1;35m  [WizNet] >> {wizardName} {action}\u001b[0m";

        foreach (var kvp in server.ActiveSessions)
        {
            if (kvp.Value.WizardLevel >= WizardLevel.Builder)
            {
                kvp.Value.EnqueueMessage(formatted);
            }
        }
    }
}
