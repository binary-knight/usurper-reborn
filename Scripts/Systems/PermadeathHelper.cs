using System;
using System.Threading.Tasks;
using UsurperRemake.UI;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// v0.60.0 beta: shared online-mode permadeath logic. Three duplicate
    /// death handlers exist (CombatEngine.HandlePlayerDeath,
    /// LocationManager.HandlePlayerDeath, GameEngine.HandlePlayerDeath).
    /// Each needs the same auto-revive-or-permadeath behavior. Rather than
    /// triple-inline it, the shared cinematic + delete + broadcast +
    /// news-post lives here and the death handlers just call into it.
    /// </summary>
    public static class PermadeathHelper
    {
        /// <summary>
        /// Online-mode death-handling. If the player has Resurrections > 0,
        /// consumes one and returns true (caller should restore HP and
        /// continue play). If Resurrections == 0, plays the permadeath
        /// cinematic, archives + deletes the account, broadcasts to all
        /// online players, posts news, sets IsIntentionalExit, and returns
        /// false (caller should NOT continue play -- session is over).
        /// </summary>
        public static async Task<bool> HandleOnlineDeath(global::Character player, TerminalEmulator terminal, string killerName)
        {
            // v0.60.7: admin master switch. When permadeath is disabled
            // server-wide, every death short-circuits to a full-heal revive
            // (no resurrection consumed, no permadeath ever). The legacy
            // Temple / Deal with Death / Accept Fate menu only fires for
            // CombatEngine deaths -- non-combat deaths (location hazards,
            // system-initiated) reach this helper instead and a soft revive
            // is the appropriate fallback for those paths.
            if (!GameConfig.OnlinePermadeathEnabled)
            {
                long restoredHP = player.MaxHP;
                player.HP = restoredHP;
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("");
                terminal.WriteLine($"  Divine intervention restores you. ({restoredHP}/{player.MaxHP} HP)");
                terminal.SetColor("gray");
                terminal.WriteLine("  (Permadeath is disabled on this server.)");
                terminal.WriteLine("");
                await Task.Delay(2000);
                return true;
            }

            if (player.Resurrections > 0)
            {
                player.Resurrections--;
                player.ResurrectionsUsed++;
                int restoredHP = (int)(player.MaxHP * 0.5);
                player.HP = restoredHP;

                terminal.SetColor("bright_yellow");
                terminal.WriteLine("");
                terminal.WriteLine($"  Divine intervention restores you. ({restoredHP}/{player.MaxHP} HP)");
                int rezLeft = player.Resurrections;
                if (rezLeft == 0)
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"  WARNING: This was your final resurrection.");
                    terminal.WriteLine($"  The next death will erase your character permanently.");
                }
                else
                {
                    terminal.SetColor("yellow");
                    int max = Math.Max(1, player.MaxResurrections);
                    terminal.WriteLine($"  Resurrections remaining: {rezLeft} of {max}");
                }
                terminal.WriteLine("");
                await Task.Delay(2000);
                return true;
            }

            // Permadeath flow
            await ExecutePermadeath(player, terminal, killerName);
            return false;
        }

        /// <summary>
        /// The cinematic + erasure + broadcast. Standalone so any death
        /// path can invoke it directly when bypassing the resurrection
        /// check (eg the rage event).
        /// </summary>
        public static async Task ExecutePermadeath(global::Character player, TerminalEmulator terminal, string killerName)
        {
            try { terminal.ClearScreen(); } catch { /* ignore */ }

            try
            {
                terminal.SetColor("dark_red");
                terminal.WriteLine("");
                terminal.WriteLine("");
                terminal.WriteLine($"  You have died with no resurrections remaining.");
                await Task.Delay(2000);
                terminal.WriteLine("");
                terminal.WriteLine("  The threads that bind your soul to the world fray.");
                await Task.Delay(2000);
                terminal.WriteLine("  No temple will receive you. No god will bargain.");
                await Task.Delay(2000);
                terminal.WriteLine("  No coin will buy your return.");
                await Task.Delay(2500);

                terminal.WriteLine("");
                terminal.SetColor("bright_red");
                terminal.WriteLine($"  {player.Name2 ?? player.Name1 ?? "Mortal"}, you have exhausted your lives.");
                await Task.Delay(2500);

                // v0.63.0 slice 3 D4: Inheritance. Before the Veil closes, if
                // the player has at least one living adult child, half the gold
                // passes to the eldest, with a brief news entry tying the legacy
                // back to them. Idempotent via PermadeathInheritanceClaimed.
                await TryDistributeInheritance(player, terminal);

                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine("  The Veil closes for the last time.");
                await Task.Delay(2000);
                terminal.WriteLine("  Your record is being erased.");
                await Task.Delay(2500);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("DEATH_CAP",
                    $"Cinematic threw for '{player.Name}': {ex.Message}. Continuing to deletion.");
            }

            try
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                // v0.61.1: route delete + world-state purge through CharacterKey
                // so alt characters delete the alt's DB row, not the main account's.
                // Username is the SSH account name (= main char save key) and is
                // init-only on SessionContext, so it never tracks
                // SwitchToAltCharacter. Pre-fix, dying on an alt with 0
                // resurrections deleted the main character (potentially the
                // player's immortal) while leaving the alt row that actually died
                // sitting in the DB. CharacterKey fallback covers single-player /
                // BBS where it may be empty.
                string username = (!string.IsNullOrEmpty(ctx?.CharacterKey) ? ctx!.CharacterKey : ctx?.Username) ?? player.Name1 ?? player.Name2 ?? "";
                // ActiveSessions is keyed by SSH account (one socket per account,
                // shared by main + alt), so SuppressDisconnectSave lookup must use
                // Username regardless of which character died.
                string sessionLookupKey = ctx?.Username ?? username;
                string displayName = player.Name2 ?? player.Name1 ?? username;
                int finalLevel = player.Level;
                string className = player.Class.ToString();

                // v0.60.0 beta: BEFORE the DB delete, suppress every possible
                // future save for this username:
                //   1. In-memory blacklist (WriteGameData checks it on every
                //      save attempt, blocks if present). Stops fire-and-forget
                //      autosaves that started before the cinematic.
                //   2. Session SuppressDisconnectSave flag. Stops the
                //      emergency-save-on-disconnect from re-INSERTing the
                //      character's data when the session terminates.
                // Without these, players reported being able to log straight
                // back into their permadied characters because something
                // re-saved between the soft-delete and the disconnect.
                if (!string.IsNullOrEmpty(username))
                {
                    SqlSaveBackend.MarkUsernameErased(username);
                    try
                    {
                        var session = UsurperRemake.Server.MudServer.Instance?.ActiveSessions
                            .TryGetValue(sessionLookupKey.ToLowerInvariant(), out var s) == true ? s : null;
                        if (session != null) session.SuppressDisconnectSave = true;
                    }
                    catch (Exception ex) { DebugLogger.Instance.LogWarning("DEATH_CAP", $"SuppressDisconnectSave set failed: {ex.Message}"); }
                }

                if (SaveSystem.Instance?.Backend is SqlSaveBackend sqlBackend && !string.IsNullOrEmpty(username))
                {
                    // v0.60.5: purge shared world-state references (guild membership,
                    // bounties, trades, world-boss damage, etc.) BEFORE clearing the
                    // player_data so any joined queries in the purge hooks still
                    // resolve. Player report (Rage): "lost all 4 lives, made a new
                    // char, came back in my guild still, actually still worshiping
                    // the same god." Fixed by this purge + the in-memory hook.
                    sqlBackend.PurgePlayerWorldState(username, displayName);

                    sqlBackend.DeleteGameData(username, bypassArchive: false);
                    DebugLogger.Instance.LogWarning("DEATH_CAP",
                        $"Permadeleted '{username}' (display='{displayName}', lv={finalLevel}, class={className}, killer={killerName}). 7-day /restore window active.");
                }

                // v0.65.0 (eldruin/Eldruin report): permadeath must also clear the
                // character's identity-linked state that lives OUTSIDE the players
                // row, or a same-name recreation re-binds to it. SQL-side state is
                // handled by PurgePlayerWorldState above; these three live in
                // shared in-memory systems + their world_state mirrors.
                try
                {
                    // (1) Claimed quests: keyed by Occupier = display name in the
                    // shared questDatabase. Remove them and push the cleaned list
                    // to world_state["quests"] so it's durable (otherwise they
                    // re-surface for the next character named the same).
                    int removedQuests = QuestSystem.RemovePlayerQuests(displayName, username, player.Name1, player.Name2);
                    if (removedQuests > 0)
                        DebugLogger.Instance.LogInfo("DEATH_CAP", $"Removed {removedQuests} claimed quest(s) for permadied '{displayName}'.");
                    if (UsurperRemake.BBS.DoorMode.IsOnlineMode && OnlineStateManager.IsActive)
                        await OnlineStateManager.Instance!.SaveSharedQuestsNow();
                }
                catch (Exception qex) { DebugLogger.Instance.LogWarning("DEATH_CAP", $"Quest purge failed: {qex.Message}"); }

                try
                {
                    // (2) Children: disown so the dead parent's kids don't grant a
                    // same-name recreation family bonuses / show as their children.
                    // Use the ID-first Character overload (v0.63.0 slice 4) for the
                    // same rename/name-collision robustness the NG+/delete paths use.
                    int disowned = UsurperRemake.Systems.FamilySystem.Instance?.DisownChildrenOf(player) ?? 0;
                    if (disowned > 0)
                    {
                        DebugLogger.Instance.LogInfo("DEATH_CAP", $"Disowned {disowned} child(ren) of permadied '{displayName}'.");
                        if (UsurperRemake.BBS.DoorMode.IsOnlineMode && OnlineStateManager.IsActive)
                            await OnlineStateManager.Instance!.SaveSharedChildrenNow();
                    }
                }
                catch (Exception cex) { DebugLogger.Instance.LogWarning("DEATH_CAP", $"Child disown failed: {cex.Message}"); }

                if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                {
                    string eulogy = $"\r\n  *** {displayName} the Lv.{finalLevel} {className} has been erased forever, slain by {killerName}. ***\r\n";
                    try
                    {
                        // ANSI: bright red ([1;31m) ... reset ([0m).
                        // Earlier write missed the ESC byte and rendered as
                        // literal "[1;31m" text in clients.
                        UsurperRemake.Server.MudServer.Instance?.BroadcastToAll(
                            "[1;31m" + eulogy + "[0m", excludeUsername: username);
                    }
                    catch (Exception ex) { DebugLogger.Instance.LogError("DEATH_CAP", $"Broadcast failed: {ex.Message}"); }

                    try
                    {
                        if (OnlineStateManager.IsActive)
                        {
                            _ = OnlineStateManager.Instance!.AddNews(
                                $"{displayName} the Lv.{finalLevel} {className} fell forever to {killerName}. Their soul has left the world.",
                                "permadeath");
                        }
                    }
                    catch (Exception ex) { DebugLogger.Instance.LogError("DEATH_CAP", $"News post failed: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("DEATH_CAP",
                    $"Permadelete failed for '{player.Name}': {ex.Message}");
            }

            try
            {
                terminal.WriteLine("");
                terminal.SetColor("dark_gray");
                terminal.WriteLine("  Your character has been erased.");
                terminal.WriteLine("  If this was a mistake, contact an admin on Discord");
                terminal.WriteLine("  within 7 days for a possible restoration.");
                terminal.WriteLine("  Disconnecting.");
                terminal.WriteLine("");
                await Task.Delay(3000);
            }
            catch { /* ignore */ }

            var sessionCtx = UsurperRemake.Server.SessionContext.Current;
            if (sessionCtx != null) sessionCtx.IsIntentionalExit = true;

            // v0.61.0 hotfix (issue #106): actively close the session socket. Without
            // this, IsIntentionalExit is just a "don't save on disconnect" marker --
            // it doesn't unwind the running game loop. The player stays in whatever
            // location they died in (dungeon, in fastfinge's report), with their
            // character already deleted from the DB, until they manually log off.
            // Closing the socket here unblocks the session's blocking ReadLineAsync,
            // which propagates through the session-loop catch and tears down cleanly.
            // Fire-and-forget so we don't await the 2s message delay inside
            // DisconnectAsync -- by the time the caller's caller (combat / boss /
            // dungeon) tries to continue, the socket is already gone.
            try
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                // Session lookup MUST use Username (the SSH account name), not
                // CharacterKey — ActiveSessions is keyed by account, and the alt
                // and main share one socket. CharacterKey here would miss the
                // session entirely when on an alt.
                string sessionKey = ctx?.Username ?? "";
                if (!string.IsNullOrEmpty(sessionKey))
                {
                    var session = UsurperRemake.Server.MudServer.Instance?.ActiveSessions
                        .TryGetValue(sessionKey.ToLowerInvariant(), out var s) == true ? s : null;
                    if (session != null)
                    {
                        _ = session.DisconnectAsync(Loc.Get("permadeath.disconnect_msg"));
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("DEATH_CAP",
                    $"Force-disconnect after permadeath failed: {ex.Message}");
            }
        }

        /// <summary>
        /// v0.63.0 slice 3 D4: Inheritance on permadeath. When the player
        /// permadies with at least one living adult child, half the gold
        /// passes to the eldest (by Age, ties broken by ID order), the heir
        /// gets a one-time grief XP buff, and a news entry ties the legacy
        /// to them. Idempotent via PermadeathInheritanceClaimed so the
        /// distribution can't double-fire on a malformed save / restart.
        /// </summary>
        private static async Task TryDistributeInheritance(
            global::Character player, TerminalEmulator terminal)
        {
            try
            {
                if (player == null) return;
                if (player.PermadeathInheritanceClaimed) return;

                var family = UsurperRemake.Systems.FamilySystem.Instance;
                if (family == null) return;

                var adultChildren = family.GetAdultChildrenOf(player);
                if (adultChildren == null || adultChildren.Count == 0) return;

                // Pick the eldest adult child (highest Age). Tiebreak by Name2
                // ordinal so the result is deterministic across saves.
                var heir = adultChildren
                    .OrderByDescending(n => n.Age)
                    .ThenBy(n => n.Name2 ?? "", StringComparer.Ordinal)
                    .FirstOrDefault();
                if (heir == null) return;

                // Inheritance: 50% of current gold, +500 XP determination buff.
                long passed = Math.Max(0, player.Gold / 2);
                heir.Gold += passed;
                heir.Experience += 500;

                terminal.WriteLine("");
                terminal.SetColor("bright_magenta");
                string heirName = heir.DisplayName ?? heir.Name2 ?? heir.Name1 ?? "your child";
                terminal.WriteLine(
                    $"  {UsurperRemake.Systems.Loc.Get("permadeath.inheritance_estate", heirName, passed)}");
                await Task.Delay(2500);
                terminal.SetColor("gray");
                terminal.WriteLine(
                    $"  {UsurperRemake.Systems.Loc.Get("permadeath.inheritance_legacy", heirName)}");
                await Task.Delay(2500);

                // News entry so anyone else online (or returning later) sees
                // the legacy as a real world event.
                try
                {
                    global::NewsSystem.Instance?.Newsy(
                        UsurperRemake.Systems.Loc.Get("permadeath.inheritance_news",
                            player.Name2 ?? player.Name1 ?? "An adventurer", heirName, passed));
                }
                catch (Exception nx)
                {
                    DebugLogger.Instance.LogWarning("DEATH_CAP",
                        $"Inheritance news post failed: {nx.Message}");
                }

                // Persist the heir's new gold + XP via the shared NPC state
                // save so a session reload after the cinematic doesn't undo it.
                try
                {
                    if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                    {
                        await UsurperRemake.Systems.OnlineStateManager.Instance!.SaveAllSharedState();
                    }
                }
                catch (Exception sx)
                {
                    DebugLogger.Instance.LogWarning("DEATH_CAP",
                        $"Post-inheritance SaveAllSharedState failed: {sx.Message}");
                }

                // Mark the inheritance claimed AND persist before the player's
                // save row gets deleted. Save-state-reviewer F1 audit (v0.63.0
                // slice 3): without an explicit save here, the flag lives only
                // on the in-memory Character and is gone the moment DeleteGameData
                // fires. A subsequent admin `/restore` would bring back the
                // pre-distribution state and let a second permadeath double-grant
                // the heir. The save here gets archived by DeleteGameData's
                // archive step so the restored row reflects the post-distribution
                // state, and the idempotency guard at the top of this method
                // does its job for real.
                player.PermadeathInheritanceClaimed = true;
                try
                {
                    var ge = GameEngine.Instance;
                    if (ge != null)
                    {
                        await ge.SaveCurrentGame();
                    }
                }
                catch (Exception psx)
                {
                    DebugLogger.Instance.LogWarning("DEATH_CAP",
                        $"Post-inheritance player save failed: {psx.Message}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("DEATH_CAP",
                    $"TryDistributeInheritance failed: {ex.Message}");
            }
        }
    }
}
