using System;
using System.Threading.Tasks;
using UsurperRemake.Systems;

namespace UsurperRemake.Server;

/// <summary>
/// v1.2 (design item B): what happens when a grouped follower dies in the leader's fight.
///
/// The leader's combat runs on the leader's session, and everything in the death pipeline
/// (PermadeathHelper resolves the character to erase from SessionContext.Current; the save
/// writes the current session's character) would act on the leader if run there. So the
/// combat only marks the follower (Mark), and the follower's own session resolves the death
/// (Resolve) when its follower loop exits, or at next login if the session dropped first.
/// The mark is persisted with the character so a disconnect cannot lose the death and let
/// the saved-dead check apply the wrong penalty.
/// </summary>
public static class GroupFollowerDeath
{
    /// <summary>
    /// Called from the leader's combat at the grouped-player death sites. Touches only the
    /// follower's in-memory character and session flags; never their save.
    /// </summary>
    public static void Mark(global::Character follower, string killerName)
    {
        follower.PendingGroupDeath = string.IsNullOrWhiteSpace(killerName) ? "?" : killerName;
        follower.IsAwaitingCombatInput = false;
        if (string.IsNullOrEmpty(follower.GroupPlayerUsername)) return;
        var session = GroupSystem.GetSession(follower.GroupPlayerUsername);
        if (session == null) return;
        session.IsGroupFollower = false; // GroupFollowerLoop leaves on its next read
        session.EnqueueMessage($"[1;31m  {Loc.Get("group.follower_death_header", follower.PendingGroupDeath)}[0m");
    }

    /// <summary>
    /// Runs on the follower's own session: the same bookkeeping as a solo death, then the
    /// online death policy (resurrection consumed, or permadeath, or the admin's soft
    /// revive), then a save. Returns false when the character was erased.
    /// </summary>
    public static async Task<bool> Resolve(global::Character player, TerminalEmulator terminal, string killerName)
    {
        player.PendingGroupDeath = null;
        player.PlaythroughDeaths++;
        player.MDefeats++;
        player.Fame = Math.Max(0, player.Fame - 1);
        player.Statistics?.RecordDeath(toPlayer: false);

        terminal.WriteLine("");
        terminal.SetColor("red");
        terminal.WriteLine($"  {Loc.Get("group.follower_death_header", UsurperRemake.Systems.MonsterNames.Display(killerName))}");

        bool alive;
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            alive = await PermadeathHelper.HandleOnlineDeath(player, terminal, killerName);
        }
        else
        {
            // Groups only exist online; keep the offline path harmless.
            player.HP = Math.Max(1, player.MaxHP / 2);
            alive = true;
        }
        if (!alive) return false;
        if (player.HP <= 0) player.HP = Math.Max(1, player.MaxHP / 2);

        terminal.SetColor("gray");
        terminal.WriteLine($"  {Loc.Get("group.follower_death_left")}");
        terminal.WriteLine("");

        try
        {
            if (GameEngine.Instance != null) await GameEngine.Instance.SaveCurrentGame();
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogWarning("GROUP", $"Save after follower death failed: {ex.Message}");
        }
        return true;
    }
}
