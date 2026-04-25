# v0.57.15 - Tank Speed-Leveling Fix + Per-Channel Chat Mutes

## Speed-Leveling Fix (Player-MaxHP Damage Floor)

Two compounding causes:

**`GetEffectiveArmPow` returned ArmPow uncapped.** WeapPow has a soft cap at 1200 with 50% diminishing above it (added in v0.54.0); ArmPow had no equivalent. The comment in the file even said *"defense was never the balance problem."* Endgame gear stacking proved otherwise.

**The 5%-of-monster-attack floor was too generous at high floors.** At floor 71 monsters hit for ~40-60 raw, so the existing `Math.Max(monsterAttack / 20, ...)` floor produced 2-3 minimum damage. Vs 6,686 MaxHP that's 0.03% per hit and a tank could fight forever. The floor scaled with monster output, not player resilience, so endgame players outpaced it.

**Fix: player-MaxHP-scaling damage floor.** New `GetMinIncomingDamage(target, rawAttack)` helper returns `max(1, rawAttack/20, MaxHP × 0.25%)`. The 0.25%-of-MaxHP component scales with the player so high-tank builds can't fall below it. At Coosh's 6,686 MaxHP that's ~17 minimum dmg per hit, instead of the previous 2-3. A 100 HP newbie still floors at 1 (unchanged). Applied to all four monster-vs-player damage paths in `CombatEngine.cs`: basic attack ([line 4452](Scripts/Systems/CombatEngine.cs#L4452)), DirectDamage ability ([line 4746](Scripts/Systems/CombatEngine.cs#L4746)), life-steal ability ([line 4871](Scripts/Systems/CombatEngine.cs#L4871)), DamageMultiplier ability ([line 4917](Scripts/Systems/CombatEngine.cs#L4917)). Same floor extended to companion-vs-monster path ([line 17125](Scripts/Systems/CombatEngine.cs#L17125)). New `GameConfig.MinIncomingDamageMaxHpPercent` constant (default 0.0025) makes the floor tunable.

What this means in practice. A player's pre-fix 4,747 damage across 750 fights becomes roughly 50 damage per fight (3 hits × 17 minimum) = ~37,500 across the same 750 fights, or 5.6× his MaxHP cumulative. They have to engage with the encounter; heal, potion, use abilities... instead of leaving auto-battle running unattended for 100 fights.

## Per-Channel Chat Mutes

New muting pattern for the slash chat channels. Type the channel command with no message and the channel toggles between muted and unmuted.

- `/gos` (or `/gossip`) — toggle gossip channel mute
- `/shout` — toggle shout channel mute
- `/gc` — toggle guild chat mute (only available if you're in a guild)
- `/tell` — toggle incoming-tell mute (anti-harassment; senders see *"X has private messages muted. Your tell was not delivered."*)

Mutes are per-character and persisted in the save (new `MutedChannels` field on `Character` and `PlayerData`, serialized as `List<string>`). They survive logouts, character switches, and server restarts. Each toggle confirms the new state and reminds you how to send a real message:

```
Gossip channel MUTED. You will no longer see gossip messages.
(Usage: /gossip <message>  (or /gos); type the command alone to unmute.)
```

Implementation: new `MudServer.BroadcastToAll(message, excludeUsername, channelKey)` parameter. When `channelKey` is set, recipients with that channel in their `MutedChannels` set are skipped. `channelKey` is null for system messages, level-up announcements, world events, etc., so non-chat broadcasts always deliver. Guild chat doesn't go through `BroadcastToAll` (it iterates online guild members directly), so the same mute filter is inlined into that loop.

`/say` and `/emote` (room-local) are deliberately not muteable — those are part of the immediate environment and there's no spam vector. Same for system / login-logout messages, which use null channelKey.

## Files Changed

- `Scripts/Core/GameConfig.cs` — version bump 0.57.14 → 0.57.15. New constant `MinIncomingDamageMaxHpPercent` (0.0025).
- `Scripts/Systems/CombatEngine.cs` — new `GetMinIncomingDamage(target, rawAttack)` helper. Floor applied at four monster-vs-player damage paths (basic attack, DirectDamage, life-steal, DamageMultiplier) and the companion-vs-monster path.
- `Scripts/Core/Character.cs` — new `MutedChannels` HashSet for per-channel chat mutes.
- `Scripts/Systems/SaveDataStructures.cs` — `PlayerData.MutedChannels` (List<string>) for save persistence.
- `Scripts/Systems/SaveSystem.cs` — serializes `Character.MutedChannels` into `PlayerData`.
- `Scripts/Core/GameEngine.cs` — restores `MutedChannels` (case-insensitive) from PlayerData on load.
- `Scripts/Server/MudServer.cs` — `BroadcastToAll` gains optional `channelKey` parameter; recipients with the channel muted are skipped.
- `Scripts/Server/RoomRegistry.cs` — `BroadcastGlobal` forwards channelKey to `MudServer.BroadcastToAll`.
- `Scripts/Server/MudChatSystem.cs` — new `ToggleChannelMute` helper. `HandleGossip` / `HandleShout` / `HandleTell` / `HandleGuildChat` call it on empty-args. Gossip and shout broadcasts pass the matching channelKey; guild chat per-recipient loop respects the recipient's mute; tell sender sees a "muted" notice when target has tells off.
## Deploy Notes

Game binary only — both features are server-side C#. Linux-x64 tarball → `/opt/usurper`, restart `usurper-mud` + `sshd-usurper`. No Node-side or web changes for this release.

The damage-floor change is immediate: every combat after the deploy uses the new floor. No save-format change. Existing tank characters keep all their gear; they just take more damage per hit going forward.
