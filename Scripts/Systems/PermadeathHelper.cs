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
        /// <param name="deferred">v1.1.13: a purge queued by a web delete and run later, after the rows are gone.</param>
        /// <param name="shownName">v1.1.13: the display name (a married name) read before the row went, when no player is given.</param>
        /// <param name="characterId">v1.1.13: the character's ID, read before the row went, when no player is given.</param>
        /// <param name="deletedAt">v1.1.13: when the character was deleted (a queued purge runs later); default now.</param>
        /// <param name="untimedAtDelete">v1.1.13: a queued delete's finding that no other player used the name then.</param>
        public static async Task PurgeDeletedCharacterAsync(SqlSaveBackend? backend, string? username, string? displayName, global::Character? player = null, bool deferred = false,
            string? shownName = null, string? characterId = null, DateTime? deletedAt = null, bool? untimedAtDelete = null)
        {
            string name = !string.IsNullOrWhiteSpace(displayName) ? displayName! : (username ?? "");
            if (string.IsNullOrWhiteSpace(name)) return;

            // SQL rows keyed by the character key, then PermadeathPurgeHook (god worship, relationships).
            if (backend != null && !string.IsNullOrWhiteSpace(username))
                backend.PurgePlayerWorldState(username!, name);

            // v1.1.11: every name the character was known by; a royal-debt bounty names the married display name
            string? shown = player?.DisplayName ?? (!string.IsNullOrWhiteSpace(shownName) ? shownName
                          : (string.IsNullOrWhiteSpace(username) ? null : backend?.GetStoredDisplayName(username!)));
            var aliases = CharacterAliases(name, player?.Name2, shown);
            // quests record only the character's Name2 (Occupier / OfferedTo); the married display name is for
            // bounties only, since another character's Name2 may equal it (review)
            var questNames = CharacterAliases(name, player?.Name2);
            // v1.1.11: an extra alias another player now uses (a living "Bob Smith") names their bounty, not this one's
            if (backend != null && !string.IsNullOrWhiteSpace(username))
                aliases = aliases.Where(a => questNames.Any(n => string.Equals(n, a, StringComparison.OrdinalIgnoreCase))
                                             || !backend.IsNameUsedByAnotherPlayer(a, username!)).ToList();

            try
            {
                // Claimed quests (Occupier / OfferedTo = display name) and the King's WANTED bounty on
                // the character. Pushed even when nothing was removed, since world_state may still hold
                // a stale copy that a same-name character would merge back on load.
                // v1.1.11: every removed bounty is also claimed, so another process's cached copy of it can
                // never be paid, even against a later character of the same name
                var claimKeys = new HashSet<string>();
                int removed = QuestSystem.RemovePlayerQuests(questNames.ToArray()) + aliases.Sum(a => QuestSystem.RemoveBountiesOnPlayer(a, claimKeys));
                // the shared record is edited in place, not replaced by this process's list (review)
                if (UsurperRemake.BBS.DoorMode.IsOnlineMode && OnlineStateManager.IsActive)
                    removed += await OnlineStateManager.Instance!.RemoveSharedQuestsAsync(q =>
                    {
                        bool left = QuestLeftByCharacter(q, questNames, aliases);
                        if (left && aliases.Any(a => QuestSystem.IsBountyOnPlayer(q.Initiator, q.TitleKey, q.TargetNPCName, q.IsPlayerBounty, a)))
                            claimKeys.Add(QuestSystem.BountyClaimKey(q));
                        return left;
                    });
                foreach (var key in claimKeys) backend?.TryClaimBounty(key, "(deleted character)");
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
                    // v1.1.11: a door process has no GuildSystem of its own (only the MUD server makes one)
                    var guilds = GuildSystem.Instance ?? (backend != null ? new GuildSystem(backend.DatabasePath, register: false) : null);
                    guilds?.PassLeadershipOf(username!);
                }
            }
            catch (Exception lex) { DebugLogger.Instance.LogWarning("DELETE", $"Leadership succession failed for '{name}': {lex.Message}"); }

            try
            {
                // v1.1.11: a deleted king abdicates through the normal path (history, NPC succession, persist)
                // v1.1.11: a married name another living player now uses names their reign, not this one's
                string? kingAlias = shown != null && aliases.Contains(shown, StringComparer.OrdinalIgnoreCase) ? shown : null;
                // v1.1.13: a reigning character's delete is logged first, so the owner vacates the throne again
                // over a stale or old-binary court write
                long throneEdit = 0;
                if (backend != null)
                {
                    bool reigns = global::CastleLocation.IsDeletedCharactersReign(global::CastleLocation.GetCurrentKing(), name, kingAlias);
                    if (!reigns && UsurperRemake.BBS.DoorMode.IsOnlineMode && OnlineStateManager.IsActive)
                        reigns = global::CastleLocation.SharedCourtNamesDeletedCharacter(await OnlineStateManager.Instance!.ReadRoyalCourtFromWorldState(), name, kingAlias);
                    if (reigns)
                        throneEdit = WorldEditLog.AppendVacateThrone(backend, name, kingAlias != null ? new[] { kingAlias } : Array.Empty<string>(), username);
                }
                if (await global::CastleLocation.AbdicateDeletedKingAsync(name, kingAlias, "left the throne and the realm"))
                {
                    DebugLogger.Instance.LogInfo("DELETE", $"Deleted '{name}' held the throne; the reign has ended.");
                    if (throneEdit > 0 && UsurperRemake.BBS.DoorMode.IsOnlineMode && WorldEditLog.IsOwnerProcess(backend))
                        backend!.MarkWorldEditsApplied(new[] { throneEdit }, WorldEditLog.ProcessLabel);
                }
            }
            catch (Exception tex) { DebugLogger.Instance.LogWarning("DELETE", $"Throne handover failed for '{name}': {tex.Message}"); }

            try
            {
                // v1.1.13: NPC grudges and marriages naming the character, cleared on the live roster and
                // persisted at once under the record's version. Enemies and KnownCharacters carry no time, so
                // they are cleared only while no other player row uses the name; a deferred purge runs after
                // the account's rows are gone, so any row with the name is a later character.
                // v1.1.13: a queued purge also needs no other player to have used the name at the delete
                bool untimedToo = untimedAtDelete != false
                                  && (backend == null || !questNames.Any(a => backend.IsNameUsedByAnotherPlayer(a, deferred ? "" : (username ?? ""))));
                var cutOff = deletedAt ?? DateTime.Now;   // v1.1.13: a queued purge cuts off at the delete, not at its run
                string? id = !string.IsNullOrWhiteSpace(player?.ID) ? player!.ID
                           : (!string.IsNullOrWhiteSpace(characterId) ? characterId
                           : (backend != null && !string.IsNullOrWhiteSpace(username) ? backend.GetStoredCharacterId(username!) : null));
                var ids = CharacterIdsToUnmarry(id, questNames, untimedToo);
                // v1.1.13: logged first, so the owner re-applies it over any stale or old-binary write
                long editId = backend != null ? WorldEditLog.AppendForgetCharacter(backend, questNames, username, cutOff, untimedToo, ids) : 0;
                int forgotten = await ForgetCharacterInNpcWorldAsync(backend, questNames, cutOff, untimedToo,
                    onWritten: () => { if (editId > 0 && WorldEditLog.IsOwnerProcess(backend)) backend!.MarkWorldEditsApplied(new[] { editId }, WorldEditLog.ProcessLabel); },
                    characterIds: ids);
                if (forgotten > 0)
                    DebugLogger.Instance.LogInfo("DELETE", $"Cleared {forgotten} NPC grudge(s), enemy entries and marriage(s) of deleted '{name}'.");
            }
            catch (Exception mex) { DebugLogger.Instance.LogWarning("DELETE", $"NPC grudge and marriage clear failed for '{name}': {mex.Message}"); }

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

        /// <summary>
        /// v1.1.11: as above for a character known by several names (a royal-debt bounty names the married
        /// display name): a match on the name or any alias.
        /// </summary>
        public static bool QuestLeftByCharacter(QuestData q, IReadOnlyList<string> questNames, IReadOnlyList<string> bountyAliases) =>
            questNames.Any(n => QuestLeftByCharacter(q, n)) ||
            bountyAliases.Any(a => QuestSystem.IsBountyOnPlayer(q.Initiator, q.TitleKey, q.TargetNPCName, q.IsPlayerBounty, a));

        /// <summary>v1.1.11: the distinct, non-blank names a character went by, the name first.</summary>
        public static List<string> CharacterAliases(params string?[] names)
        {
            var list = new List<string>();
            foreach (var n in names)
                if (!string.IsNullOrWhiteSpace(n) && !list.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase))) list.Add(n!);
            return list;
        }

        /// <summary>
        /// v1.1.13: every live NPC forgets its grudges against the name recorded at or before recordedBy
        /// (MemorySystem.IsGrudge). With untimedToo, the name also leaves Enemies and KnownCharacters; those
        /// lists carry no time, so the caller passes false when a later character may already use the name.
        /// </summary>
        public static int ForgetNpcGrudgesAgainst(string? name, DateTime? recordedBy = null, bool untimedToo = true)
        {
            if (string.IsNullOrWhiteSpace(name)) return 0;
            var npcs = NPCSpawnSystem.Instance?.ActiveNPCs;
            if (npcs == null) return 0;
            int removed = 0;
            foreach (var npc in npcs.ToList())
            {
                if (npc == null) continue;
                var brainMemory = npc.Brain?.Memory;
                if (brainMemory != null) removed += brainMemory.ForgetGrudgesAgainst(name!, recordedBy);
                if (npc.Memory != null && !ReferenceEquals(npc.Memory, brainMemory)) removed += npc.Memory.ForgetGrudgesAgainst(name!, recordedBy);
                if (!untimedToo) continue;
                if (brainMemory != null) removed += brainMemory.ForgetNegativeImpressionOf(name!);
                if (npc.Memory != null && !ReferenceEquals(npc.Memory, brainMemory)) removed += npc.Memory.ForgetNegativeImpressionOf(name!);
                removed += npc.Enemies?.RemoveAll(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase)) ?? 0;
                removed += npc.KnownCharacters?.RemoveAll(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase)) ?? 0;
            }
            return removed;
        }

        /// <summary>
        /// v1.1.13: end the marriage of any NPC whose spouse was the deleted character, clearing the three
        /// flags a divorce clears and its registry entry. An NPC married to another NPC of that name is
        /// left alone: the registry partner is a live NPC, or a live NPC of the name names it as spouse.
        /// </summary>
        public static int ClearNpcSpousesOf(string? name, ISet<string>? endedIds = null)
        {
            if (string.IsNullOrWhiteSpace(name)) return 0;
            var npcs = NPCSpawnSystem.Instance?.ActiveNPCs;
            if (npcs == null) return 0;
            int cleared = 0;
            foreach (var npc in npcs.ToList())
            {
                if (npc == null || !string.Equals(npc.SpouseName, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (RegisteredToAnotherNpc(npc.ID, pid => IsActiveNpcId(pid, npc))) continue;
                string own = npc.Name2 ?? npc.Name1 ?? "";
                if (!string.IsNullOrEmpty(own) && npcs.Any(o => o != null && !ReferenceEquals(o, npc)
                        && string.Equals(o.Name2 ?? o.Name1, name, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(o.SpouseName, own, StringComparison.OrdinalIgnoreCase))) continue;
                npc.Married = false;
                npc.IsMarried = false;
                npc.SpouseName = "";
                if (!string.IsNullOrEmpty(npc.ID))
                {
                    NPCMarriageRegistry.Instance.EndMarriage(npc.ID);   // v1.1.13: as a divorce does
                    endedIds?.Add(npc.ID);
                }
                cleared++;
            }
            return cleared;
        }

        /// <summary>
        /// v1.1.13: the registry marries this NPC to another NPC. The registry also holds player-NPC
        /// marriages, so membership alone proves nothing: the partner must itself be a live NPC.
        /// </summary>
        internal static bool RegisteredToAnotherNpc(string? npcId, Func<string, bool> isNpcId)
        {
            if (string.IsNullOrEmpty(npcId)) return false;
            var partner = NPCMarriageRegistry.Instance.GetSpouseId(npcId!);
            return !string.IsNullOrEmpty(partner) && partner != npcId && isNpcId(partner!);
        }

        private static bool IsActiveNpcId(string id, NPC? self) =>
            NPCSpawnSystem.Instance?.ActiveNPCs?.Any(n => n != null && !ReferenceEquals(n, self) && n.ID == id) == true;

        /// <summary>
        /// v1.1.13: the NPC half of the purge: grudges, Enemies and KnownCharacters, and marriages naming the
        /// character, on the live roster and registry, then written at once (OnlineStateManager.PersistNpcWorldNow).
        /// The clean-up and the serialize hold the roster lock a login's RestoreNPCs holds, so neither can
        /// interleave with it. Spouses carry no time, so they are cleared only with untimedToo (v1.1.13);
        /// registry marriages of characterIds are ended whatever the NPC's SpouseName says. A retry after a reload re-applies it with the same cut-off, the delete
        /// time: a reload keeps each memory's recorded time (v1.1.13).
        /// </summary>
        internal static async Task<int> ForgetCharacterInNpcWorldAsync(SqlSaveBackend? backend, IReadOnlyList<string> names,
            DateTime deletedAt, bool untimedToo, Func<Task>? beforeWrite = null, Action? onWritten = null,
            IReadOnlyCollection<string>? characterIds = null, int rosterWaitMs = 10000)
        {
            var endedMarriages = new HashSet<string>();
            int CleanUp()
            {
                int n = 0;
                foreach (var a in names)
                {
                    n += ForgetNpcGrudgesAgainst(a, deletedAt, untimedToo);
                    // v1.1.13: a spouse name carries no time, so a later character of the name keeps its marriage
                    if (untimedToo) n += ClearNpcSpousesOf(a, endedMarriages);
                }
                n += EndRegistryMarriagesOf(characterIds, endedMarriages);
                return n;
            }
            if (backend == null) return CleanUp();
            return await OnlineStateManager.PersistNpcWorldNow(backend, CleanUp, endedMarriages, beforeWrite, onWritten, rosterWaitMs);
        }

        /// <summary>
        /// v1.1.13: the character IDs whose registry marriages the delete ends. An ID is the character's own, so
        /// a later character of the name has another; an old save's ID that is only the name counts only with
        /// untimedToo.
        /// </summary>
        internal static List<string> CharacterIdsToUnmarry(string? id, IReadOnlyList<string> names, bool untimedToo)
        {
            var ids = new List<string>();
            if (string.IsNullOrWhiteSpace(id)) return ids;
            if (!untimedToo && names.Any(n => string.Equals(n, id, StringComparison.OrdinalIgnoreCase))) return ids;
            ids.Add(id!);
            return ids;
        }

        /// <summary>
        /// v1.1.13: end every registry marriage of these character IDs, whatever the NPC's SpouseName says (a
        /// reloaded clean roster has it empty while a process's registry still pairs them). The NPC side's ID
        /// and the character's go into endedIds, so the stored marriages record loses the pair too.
        /// </summary>
        internal static int EndRegistryMarriagesOf(IEnumerable<string>? characterIds, ISet<string>? endedIds)
        {
            if (characterIds == null) return 0;
            int ended = 0;
            foreach (var id in characterIds)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                var partner = NPCMarriageRegistry.Instance.GetSpouseId(id);
                if (string.IsNullOrEmpty(partner)) continue;
                NPCMarriageRegistry.Instance.EndMarriage(id);
                endedIds?.Add(id);
                endedIds?.Add(partner!);
                ended++;
            }
            return ended;
        }
    }
}
