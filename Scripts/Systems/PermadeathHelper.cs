using System;
using System.Linq;
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
                terminal.WriteLine($"  {Loc.Get("death.divine_restore", restoredHP, player.MaxHP)}");
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("death.permadeath_disabled")}");
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
                terminal.WriteLine($"  {Loc.Get("death.divine_restore", restoredHP, player.MaxHP)}");
                int rezLeft = player.Resurrections;
                if (rezLeft == 0)
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"  {Loc.Get("death.final_warning")}");
                    terminal.WriteLine($"  {Loc.Get("death.final_warning2")}");
                }
                else
                {
                    terminal.SetColor("yellow");
                    int max = Math.Max(1, player.MaxResurrections);
                    terminal.WriteLine($"  {Loc.Get("death.rez_remaining", rezLeft, max)}");
                }
                // v0.65.6: lives are renewable now -- say so at the moment the
                // player is most afraid, or the countdown reads as a death spiral.
                terminal.SetColor("gray");
                terminal.WriteLine($"  {UsurperRemake.Systems.Loc.Get("death.lives_recover_hint")}");
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
                terminal.WriteLine($"  {Loc.Get("permadeath.no_rez")}");
                await Task.Delay(2000);
                terminal.WriteLine("");
                terminal.WriteLine($"  {Loc.Get("permadeath.threads")}");
                await Task.Delay(2000);
                terminal.WriteLine($"  {Loc.Get("permadeath.no_temple")}");
                await Task.Delay(2000);
                terminal.WriteLine($"  {Loc.Get("permadeath.no_coin")}");
                await Task.Delay(2500);

                terminal.WriteLine("");
                terminal.SetColor("bright_red");
                terminal.WriteLine($"  {Loc.Get("permadeath.exhausted", player.Name2 ?? player.Name1 ?? "???")}");
                await Task.Delay(2500);

                // v0.63.0 slice 3 D4: Inheritance. Before the Veil closes, if
                // the player has at least one living adult child, half the gold
                // passes to the eldest, with a brief news entry tying the legacy
                // back to them. Idempotent via PermadeathInheritanceClaimed.
                await TryDistributeInheritance(player, terminal);

                // v0.65.8 (R5) Fallen Legacy: deletion is no longer a total
                // loss. Say so DURING the worst moment of the player's session.
                terminal.WriteLine("");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  {Loc.Get("permadeath.legacy_recorded")}");
                await Task.Delay(2500);
                terminal.SetColor("cyan");
                terminal.WriteLine($"  {Loc.Get("permadeath.legacy_heirloom_hint")}");
                await Task.Delay(2500);

                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("permadeath.veil_closes")}");
                await Task.Delay(2000);
                terminal.WriteLine($"  {Loc.Get("permadeath.erasing")}");
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
                        if (session != null) { session.SuppressDisconnectSave = true; session.SuppressDisconnectSaveKey = username.ToLowerInvariant(); }
                    }
                    catch (Exception ex) { DebugLogger.Instance.LogWarning("DEATH_CAP", $"SuppressDisconnectSave set failed: {ex.Message}"); }
                }

                if (SaveSystem.Instance?.Backend is SqlSaveBackend sqlBackend && !string.IsNullOrEmpty(username))
                {
                    // v0.65.8 (R5) Fallen Legacy: carve the memorial + queue the
                    // heirloom BEFORE the row is deleted. Heirloom scales with the
                    // fallen character's LEVEL (never wealth), claimed by the next
                    // character created on this account key.
                    sqlBackend.RecordFallenLegacy(username, displayName, finalLevel, className,
                        killerName, GameConfig.GetFallenLegacyGold(finalLevel));
                }

                // v0.60.5: purge shared world-state references (guild membership,
                // bounties, trades, world-boss damage, etc.) BEFORE clearing the
                // player_data so any joined queries in the purge hooks still
                // resolve. Player report (Rage): "lost all 4 lives, made a new
                // char, came back in my guild still, actually still worshiping
                // the same god." Fixed by this purge + the in-memory hook.
                // v1.1.11: the purge, the quest removal and the child disown now live in
                // PurgeDeletedCharacterAsync, which every online delete path shares.
                await PurgeDeletedCharacterAsync(SaveSystem.Instance?.Backend as SqlSaveBackend, username, displayName, player);

                if (SaveSystem.Instance?.Backend is SqlSaveBackend sqlDelete && !string.IsNullOrEmpty(username))
                {
                    sqlDelete.DeleteGameData(username, bypassArchive: false);
                    DebugLogger.Instance.LogWarning("DEATH_CAP",
                        $"Permadeleted '{username}' (display='{displayName}', lv={finalLevel}, class={className}, killer={killerName}). 7-day /restore window active.");
                }

                if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                {
                    // v0.65.12 (loc audit): eulogy is rendered per-recipient below.
                    try
                    {
                        // ANSI: bright red ([1;31m) ... reset ([0m).
                        // Earlier write missed the ESC byte and rendered as
                        // literal "[1;31m" text in clients.
                        UsurperRemake.Server.MudServer.Instance?.BroadcastLocalized(
                            lang => "[1;31m\r\n  *** " + Loc.GetIn(lang, "permadeath.eulogy", displayName, finalLevel, className, killerName) + " ***\r\n[0m",
                            excludeUsername: username);
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
                terminal.WriteLine($"  {Loc.Get("permadeath.erased")}");
                terminal.WriteLine($"  {Loc.Get("permadeath.contact_admin")}");
                terminal.WriteLine($"  {Loc.Get("permadeath.contact_admin2")}");
                terminal.WriteLine($"  {Loc.Get("permadeath.disconnecting")}");
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

        /// <summary>
        /// v1.1.11: the one purge for a character leaving the world, shared by permadeath, the
        /// character-select deletes (N, D) and both admin deletes. Player report: a character deleted
        /// and recreated under the same name kept the old quest list and god. Call it BEFORE
        /// DeleteGameData, which still archives the row for the 7-day /restore; as with permadeath,
        /// a restore does not bring back the world state cleared here.
        /// </summary>
        public static async Task PurgeDeletedCharacterAsync(SqlSaveBackend? backend, string? username, string? displayName, global::Character? player = null)
        {
            string name = !string.IsNullOrWhiteSpace(displayName) ? displayName! : (username ?? "");
            if (string.IsNullOrWhiteSpace(name)) return;

            // SQL rows keyed by the character key, then PermadeathPurgeHook (god worship, relationships).
            if (backend != null && !string.IsNullOrWhiteSpace(username))
                backend.PurgePlayerWorldState(username!, name);

            try
            {
                // Claimed quests (Occupier / OfferedTo = display name) and the King's WANTED bounty on
                // the character. Pushed even when nothing was removed, since world_state may still hold
                // a stale copy that a same-name character would merge back on load.
                int removed = QuestSystem.RemovePlayerQuests(name) + QuestSystem.RemoveBountiesOnPlayer(name);
                // the shared record is edited in place, not replaced by this process's list (review)
                if (UsurperRemake.BBS.DoorMode.IsOnlineMode && OnlineStateManager.IsActive)
                    removed += await OnlineStateManager.Instance!.RemoveSharedQuestsAsync(q => QuestLeftByCharacter(q, name));
                if (removed > 0)
                    DebugLogger.Instance.LogInfo("DELETE", $"Removed {removed} quest(s) and bounties for deleted '{name}'.");
            }
            catch (Exception qex) { DebugLogger.Instance.LogWarning("DELETE", $"Quest purge failed for '{name}': {qex.Message}"); }

            try
            {
                // The guild_members row went with the SQL purge; the cache is keyed by the character key
                // only (a display name can be another account's key, review)
                if (!string.IsNullOrWhiteSpace(username)) GuildSystem.Instance?.ForgetMember(username);
            }
            catch (Exception gex) { DebugLogger.Instance.LogWarning("DELETE", $"Guild cache clear failed for '{name}': {gex.Message}"); }

            try
            {
                // v1.1.11: a team or guild the character led passes to its highest-level remaining player
                if (!string.IsNullOrWhiteSpace(username))
                {
                    backend?.PassTeamLeadershipOfDeleted(username!);
                    GuildSystem.Instance?.PassLeadershipOf(username!);
                }
            }
            catch (Exception lex) { DebugLogger.Instance.LogWarning("DELETE", $"Leadership succession failed for '{name}': {lex.Message}"); }

            try
            {
                // v1.1.11: a deleted king abdicates through the normal path (history, NPC succession, persist)
                string? shown = player?.DisplayName ?? (string.IsNullOrWhiteSpace(username) ? null : backend?.GetStoredDisplayName(username!));
                if (global::CastleLocation.AbdicateDeletedKing(name, shown, "left the throne and the realm"))
                    DebugLogger.Instance.LogInfo("DELETE", $"Deleted '{name}' held the throne; the reign has ended.");
            }
            catch (Exception tex) { DebugLogger.Instance.LogWarning("DELETE", $"Throne handover failed for '{name}': {tex.Message}"); }

            try
            {
                // v1.1.11: NPC grudges naming the character. In memory, and in the shared npcs record
                // edited in place (a world-sim reload would otherwise bring them back).
                int forgotten = ForgetNpcGrudgesAgainst(name);
                if (backend != null) forgotten += await RemoveSharedNpcGrudgesAsync(backend, name);
                if (forgotten > 0)
                    DebugLogger.Instance.LogInfo("DELETE", $"Dropped {forgotten} NPC grudge memory(ies) of deleted '{name}'.");
            }
            catch (Exception mex) { DebugLogger.Instance.LogWarning("DELETE", $"NPC grudge clear failed for '{name}': {mex.Message}"); }

            try
            {
                int widowed = ClearNpcSpousesOf(name);
                if (widowed > 0)
                    DebugLogger.Instance.LogInfo("DELETE", $"Cleared the marriage of {widowed} NPC(s) to deleted '{name}'.");
            }
            catch (Exception sex) { DebugLogger.Instance.LogWarning("DELETE", $"NPC spouse clear failed for '{name}': {sex.Message}"); }

            try
            {
                // Children match parents by name, so a same-name recreation would inherit them. The
                // ID-first Character overload is used when the caller has the character (permadeath).
                var family = FamilySystem.Instance;
                int disowned = family == null ? 0 : (player != null ? family.DisownChildrenOf(player) : family.DisownChildrenOf(name));
                if (disowned > 0)
                {
                    DebugLogger.Instance.LogInfo("DELETE", $"Disowned {disowned} child(ren) of deleted '{name}'.");
                    if (UsurperRemake.BBS.DoorMode.IsOnlineMode && OnlineStateManager.IsActive)
                        await OnlineStateManager.Instance!.SaveSharedChildrenNow();
                }
            }
            catch (Exception cex) { DebugLogger.Instance.LogWarning("DELETE", $"Child disown failed for '{name}': {cex.Message}"); }
        }

        /// <summary>v1.1.11: a shared quest the character claimed, was offered, or a Crown bounty on it.</summary>
        public static bool QuestLeftByCharacter(QuestData q, string name) =>
            string.Equals(q.Occupier, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(q.OfferedTo, name, StringComparison.OrdinalIgnoreCase) ||
            QuestSystem.IsBountyOnPlayer(q.Initiator, q.TitleKey, q.TargetNPCName, q.IsPlayerBounty, name);

        /// <summary>v1.1.11: drop every live NPC's grudges against the name (MemorySystem.IsGrudge).</summary>
        public static int ForgetNpcGrudgesAgainst(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return 0;
            var npcs = NPCSpawnSystem.Instance?.ActiveNPCs;
            if (npcs == null) return 0;
            int removed = 0;
            foreach (var npc in npcs.ToList())
            {
                if (npc == null) continue;
                var brainMemory = npc.Brain?.Memory;
                if (brainMemory != null) removed += brainMemory.ForgetGrudgesAgainst(name!);
                if (npc.Memory != null && !ReferenceEquals(npc.Memory, brainMemory)) removed += npc.Memory.ForgetGrudgesAgainst(name!);
            }
            return removed;
        }

        /// <summary>
        /// v1.1.11: remove the grudges against the name from the npcs JSON, touching nothing else.
        /// Returns the edited JSON, or null when nothing matched.
        /// </summary>
        public static string? RemoveGrudgesFromNpcJson(string json, string name, out int removed)
        {
            removed = 0;
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(name)) return null;
            if (System.Text.Json.Nodes.JsonNode.Parse(json) is not System.Text.Json.Nodes.JsonArray npcs) return null;
            foreach (var npc in npcs)
            {
                if (npc?["memories"] is not System.Text.Json.Nodes.JsonArray memories) continue;
                for (int i = memories.Count - 1; i >= 0; i--)
                {
                    var m = memories[i];
                    if (m == null || !string.Equals(StringOf(m["involvedCharacter"]), name, StringComparison.OrdinalIgnoreCase)) continue;
                    float impact = 0f;
                    try { impact = m["emotionalImpact"]?.GetValue<float>() ?? 0f; } catch { }
                    bool typed = Enum.TryParse<MemoryType>(StringOf(m["type"]), out var type);
                    if ((typed && MemorySystem.IsGrudge(type, impact)) || (!typed && impact < 0f))
                    {
                        memories.RemoveAt(i);
                        removed++;
                    }
                }
            }
            return removed > 0 ? npcs.ToJsonString() : null;
        }

        private static string StringOf(System.Text.Json.Nodes.JsonNode? node)
        {
            try { return node?.GetValue<string>() ?? ""; } catch { return ""; }
        }

        /// <summary>
        /// v1.1.11: the shared npcs record, edited in place under its version (as RemoveSharedQuestsAsync
        /// does for quests), never replaced by this process's list. Retries if another writer got in first.
        /// </summary>
        public static async Task<int> RemoveSharedNpcGrudgesAsync(SqlSaveBackend backend, string name)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                long version = backend.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);
                string? json = await backend.LoadWorldState(OnlineStateManager.KEY_NPCS);
                if (string.IsNullOrEmpty(json)) return 0;
                string? edited = RemoveGrudgesFromNpcJson(json, name, out int removed);
                if (edited == null) return 0;
                if (await backend.SaveWorldStateIfVersion(OnlineStateManager.KEY_NPCS, edited, version)) return removed;
            }
            DebugLogger.Instance.LogWarning("DELETE", $"Shared NPC grudges of '{name}' not cleared: the record kept changing.");
            return 0;
        }

        /// <summary>
        /// v1.1.11: end the marriage of any NPC whose spouse was the deleted character, clearing the
        /// same three flags a divorce clears on the NPC. An NPC married to another NPC (registry) is
        /// left alone, in case that NPC shares the name.
        /// </summary>
        public static int ClearNpcSpousesOf(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return 0;
            var npcs = NPCSpawnSystem.Instance?.ActiveNPCs;
            if (npcs == null) return 0;
            int cleared = 0;
            foreach (var npc in npcs.ToList())
            {
                if (npc == null || !string.Equals(npc.SpouseName, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(npc.ID) && NPCMarriageRegistry.Instance.IsMarriedToNPC(npc.ID)) continue;
                npc.Married = false;
                npc.IsMarried = false;
                npc.SpouseName = "";
                cleared++;
            }
            return cleared;
        }
    }
}
