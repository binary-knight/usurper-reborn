# Release Notes — v0.42.1 "Blood Price"

## Overview

Death now carries real weight. When an NPC dies — in a dungeon crawl, a gang war, a street brawl — there's a chance they don't come back. Ever. And when you're the one holding the blade, the world makes sure you feel it: nightmares, restless sleep, blocked enlightenment, and a reputation that spreads twice as fast as any good deed.

This update also adds full companion equipment and ability management, a persistent broadcast system for server admins, and numerous bug fixes for online mode.

---

## New Feature: NPC Permadeath

When an NPC dies — in a dungeon crawl, a gang war, a street brawl — there's a chance they don't come back. Ever. The world remembers them, the news feed mourns them, and their chair at the Inn stays empty.

- **Every NPC death rolls for permanence** — the chance varies by context:
  - 8% when an NPC dies alone in the dungeon
  - 5% when an NPC teammate falls during a dungeon expedition
  - 4% in NPC-vs-NPC fights and gang wars
  - 12% when you kill an NPC in self-defense (they attacked you first)
- **Story NPCs are protected** — the 10 NPCs with personal storylines (Pip, Elena, Marcus, etc.) can never permadeath
- **Population floor** — if the living NPC count drops below 40, permadeath stops rolling. The world won't depopulate itself
- **Higher-level NPCs are harder to lose** — each NPC level reduces the chance by 1%. A level 50 NPC has half the base chance; a level 80 NPC has only 20%
- **Permanent means permanent** — permadead NPCs are skipped by the respawn system entirely, same as NPCs who die of old age

## New Feature: Blood Price (Murder Consequences)

Killing in self-defense is one thing. Murder is another.

When you deliberately kill an NPC — through the street murder option or a dark magic assassination — that death is always permanent. No roll, no chance. They're gone, and you carry the weight.

### Murder Weight

Every deliberate kill adds to your Murder Weight, a hidden measure of blood on your hands:

- **Street murder**: +3.0 weight (you chose violence)
- **Dark magic assassination**: +2.5 weight (you paid someone else to do it)
- **Killing a well-liked NPC**: +1.5 bonus weight (the community noticed)
- Self-defense kills carry **no** murder weight — you didn't start it

Murder Weight decays naturally at 0.1 per real day. Time heals, slowly.

### Nightmares

Blood follows you into sleep. When you rest — at the Inn, at home, even in the dungeon — your dreams turn dark.

- **Weight 1-2**: Unsettling dreams. Shadows in your peripheral vision. The feeling of being watched. (30% chance per rest)
- **Weight 3-5**: You see their faces. The dreams use the real names of NPCs you've killed. Your spiritual progress suffers. (60% chance)
- **Weight 6+**: The dead put you on trial. Their voices echo through every dream. You can't escape what you've done. (85% chance)

Tier 2 and Tier 3 nightmares reduce awakening progress — the Ocean Philosophy system tracks your spiritual growth, and murderers find enlightenment harder to reach.

### Rest Penalty

Guilty consciences don't rest well. At Murder Weight 3-5, healing from rest is reduced to 75%. At 6+, it drops to 50%. The message "Your rest is troubled by dark memories..." appears when the penalty kicks in. This applies everywhere — the Inn, your home, dungeon safe rooms.

### Blocked Progression

- **Awakening**: At Murder Weight above 3, the Ocean Philosophy system blocks spiritual insight gains. You can't find inner peace while carrying that much blood
- **Endings**: The True Ending and the secret Dissolution Ending both require Murder Weight below 3. Murderers are locked out of transcendence
- **Shop prices**: At Murder Weight 5+, NPCs charge you 20% more. Word gets around

### Reputation Spread

Your reputation as a killer spreads twice as fast through the NPC gossip network. The normal 5% per-tick spread chance doubles to 10% when you have any murder weight at all.

### Blood Confession (Redemption)

The Church offers absolution — for a price. During confession, if the priest detects blood on your soul, he'll offer a "blood absolution" ritual:

- **Cost**: 500 gold base + 200 gold per point of Murder Weight
- **Effect**: Reduces Murder Weight by 2.5 (on top of the normal darkness reduction from confession)
- **Repeatable**: You can confess multiple times, chipping away at your weight over several visits
- **Not free**: The gold cost makes redemption a meaningful investment, not a quick reset

### Self-Defense vs. Murder

The system carefully distinguishes between justified and unjustified killing:

| Situation | Permadeath? | Blood Price? |
|-----------|-------------|-------------|
| You choose [Attack] on the street | Always permanent | Yes (3.0) |
| You pay for a dark magic hit | Always permanent | Yes (2.5) |
| Shadows assassination contract | Always permanent | 60% chance of 1.5 weight, 40% clean |
| Crown / King bounty kill | Always permanent | No — state-sanctioned |
| A faction ambusher attacks you, you win | 12% chance | No — self-defense |
| Your NPC teammate dies in the dungeon | 12% chance | No — not your fault |
| NPCs kill each other (world sim) | 4% chance | N/A — you weren't involved |

Bounty kills are still always permanent (the NPC is dead either way), but the Crown and the King's justice system don't weigh on your conscience. The Shadows are a different story — you're a professional, not a saint. Most hits go clean, but sometimes the face stays with you.

---

## New Feature: Companion Equipment & Ability Management

Your companions can now be properly outfitted and customized for combat.

### Equipment Management

- **[E] Manage Equipment** in the companion interaction menu at the Inn
- Equip weapons, armor, shields, rings, and accessories from your inventory onto companions
- Companion stats update in real-time — equipment bonuses apply through the same RecalculateStats system as player gear
- Unequip individual items or "Take All" to reclaim everything
- Stats display shows base values with equipment bonuses in green parentheses

### Ability Management

- **[A] Manage Combat Skills** lets you toggle individual combat abilities on/off
- Each ability shows its stamina cost, level requirement, and description
- Disabled abilities are skipped during combat AI selection
- Toggle individual abilities or "Enable All" to reset

### Equipment Recovery

- When a companion dies in combat, all their equipment is automatically returned to your inventory
- Works for both normal death and paradox death events

### Save Compatibility

- Existing saves load cleanly — companions start with no equipment and all abilities enabled (current behavior)
- Equipment and ability toggles persist across save/load

---

## New Feature: Persistent Broadcast Banner

Server admins can now set a persistent announcement that stays visible on every player's screen.

- `/broadcast <message>` sets a banner that appears on every location menu refresh in bright yellow: `*** message ***`
- `/broadcast` with no message clears the active banner
- SysOp console [B]roadcast also supports set/clear with the same behavior
- Players see the banner immediately when it's set, and on every subsequent screen refresh
- Useful for maintenance warnings, server restart notices, event announcements

---

## New Feature: Permadeath Dashboard (Website)

The website now tracks the consequences of violence in the world:

- **Permadead / Old Age** stat card — shows how many NPCs are permanently gone (combat deaths vs natural causes). Only appears once the first permanent death occurs.
- **Most Wanted** highlight card — the player with the highest Blood Price, displayed with a red border. Shows their name, level, class, and murder weight.
- **Permadeath news** — skull-emoji death announcements flow through the existing SSE live feed on the website.

---

## Bug Fixes

- **Royal Court Guards/Monsters not persisting**: Guards and moat monsters were lost between sessions in online mode. Now fully serialized in world_state including guard stats, loyalty, and monster stats.
- **King AI/Sex lost on reload**: King's AI type and sex enum were not saved, causing king to always reload as default. Now persisted.
- **Player flags stale after offline changes**: Player's King and CTurf flags could become incorrect if the world changed while they were offline. New SyncPlayerFlagsFromWorldState() corrects them on login.
- **CTurf sync one-directional**: BaseLocation CTurf sync now works bidirectionally — can set the flag true, not just clear it.
- **NPCs suicidally attacking much stronger players**: Grudge confrontation NPCs and jealous spouses now have a self-preservation chance to back down when 8+ levels below the player. Faction ambushers skip players 15+ levels above them entirely.
- **Rival adventurer stats too weak**: Dungeon rival encounters had trivially low stats. Now properly scaled to be a real threat.
- **Backspace handling in BBS/SSH**: Rewrote ReadLineWithBackspace to use ANSI save/restore cursor instead of character-by-character erasure.

## Enhancements

- **Password masking**: New GetMaskedInput() method shows `*` characters when typing passwords.
- **Enchantment Forge removed**: Removed incomplete placeholder feature from the Magic Shop.
- **SysOp menu label**: Changed from "SysOp Administration Console" to the shorter "SysOp Console" / "Admin Console".
- **Color Theme system**: New ColorTheme.cs with 5 selectable themes (Default, Classic Dark, Amber Retro, Green Phosphor, High Contrast).
- **Dungeon dreams**: Resting in the dungeon now triggers the dream system (previously only Inn and Home rest did).

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.42.1, 27 new constants for permadeath chances, blood price thresholds |
| `Scripts/Core/Character.cs` | MurderWeight, PermakillLog, LastMurderWeightDecay properties |
| `Scripts/Core/NPC.cs` | IsPermaDead property, 20% shop markup for killers |
| `Scripts/Core/GameEngine.cs` | SyncPlayerFlagsFromWorldState, password masking, royal court load ordering |
| `Scripts/Locations/InnLocation.cs` | Companion equipment/ability management UI (744 lines), rest penalty |
| `Scripts/Locations/BaseLocation.cs` | CTurf sync fix, broadcast banner, ambush level filter |
| `Scripts/Locations/ChurchLocation.cs` | Blood absolution confession option |
| `Scripts/Locations/DungeonLocation.cs` | Rest penalty, dungeon dreams, rival adventurer stat fix |
| `Scripts/Locations/HomeLocation.cs` | Rest penalty for murder weight |
| `Scripts/Locations/MagicShopLocation.cs` | Dark magic permadeath + blood price, removed Enchantment Forge |
| `Scripts/Locations/CastleLocation.cs` | PersistRoyalCourtToWorldState calls after guard/monster changes |
| `Scripts/Systems/CompanionSystem.cs` | EquippedItems, DisabledAbilities, equipment in combat, recovery on death |
| `Scripts/Systems/CombatEngine.cs` | Disabled abilities filter, NPC death via MarkNPCDead |
| `Scripts/Systems/SaveDataStructures.cs` | CompanionSaveInfo equipment fields, RoyalGuardSaveData, MonsterGuardSaveData, blood price fields |
| `Scripts/Systems/SaveSystem.cs` | Serialize/restore guards, monsters, blood price, companion equipment |
| `Scripts/Systems/WorldSimulator.cs` | RollPermadeath, MarkNPCDead, ApplyBloodPrice, centralized NPC death handling |
| `Scripts/Systems/WorldSimService.cs` | Guards/monsters serialization, king restoration fix |
| `Scripts/Systems/OnlineStateManager.cs` | Guards/monsters in world_state, IsPermaDead sync |
| `Scripts/Systems/OnlineAdminConsole.cs` | Persistent broadcast set/clear |
| `Scripts/Systems/StreetEncounterSystem.cs` | Self-preservation, blood price by bounty type |
| `Scripts/Systems/DreamSystem.cs` | 6 blood price nightmare entries across 3 tiers |
| `Scripts/Systems/DailySystemManager.cs` | Murder weight natural decay |
| `Scripts/Systems/OceanPhilosophySystem.cs` | Awakening blocked at murder weight > 3 |
| `Scripts/Systems/EndingsSystem.cs` | True/Dissolution endings gated at murder weight >= 3 |
| `Scripts/Systems/SocialInfluenceSystem.cs` | 2x reputation spread for killers |
| `Scripts/Systems/QuestSystem.cs` | GetActiveBountyInitiator helper |
| `Scripts/Server/MudServer.cs` | ActiveBroadcast static field |
| `Scripts/Server/WizardCommandSystem.cs` | Persistent broadcast set/clear |
| `Scripts/UI/TerminalEmulator.cs` | Backspace rework, GetMaskedInput |
| `Scripts/UI/ColorTheme.cs` | New — 5 selectable color themes |
| `web/ssh-proxy.js` | Permadeath stats, most wanted in API |
| `web/index.html` | Permadead/Old Age stat card, Most Wanted highlight |
