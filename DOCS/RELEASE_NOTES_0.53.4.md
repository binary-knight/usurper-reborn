# Usurper Reborn v0.53.4 Release Notes

**Version Name:** Ancestral Spirits

## Prestige Class Spell Training Fix

Prestige classes (Tidesworn, Wavecaller, Cyclebreaker, Abysswarden, Voidreaver) were not recognized as spellcasters by `IsSpellcaster()`, which could prevent the [A]/[S] hybrid menu from appearing at the Level Master. While `SpellSystem.HasSpells()` already included prestige classes as a fallback, `IsSpellcaster()` is used in other code paths. Both methods now consistently include all 5 prestige classes.

## Spell Target Random Selection Fix

Single-target attack spells (like Ocean's Rebuke) in multi-monster combat did not allow pressing Enter to randomly select a target — it showed "Invalid target" and re-prompted. Regular attacks allowed this. Now all spell target prompts accept Enter for random target selection, consistent with regular attacks.

## Floor 100 Story Text Fix

The text before the Manwe encounter falsely presented three explicit choices ("DESTROY him", "SAVE him", "DEFY him") as if the player picks their ending directly. The ending is actually determined by alignment and gods saved/destroyed throughout the game. Text rewritten to reflect this: "Your journey has shaped you... your actions will determine your fate." Updated in all 5 languages.

## Tidal Reflect Combat Reset

The `Reflecting` status effect (from Tidal Ward and other reflect spells) was not cleared at combat start, allowing it to persist between fights. Now cleared alongside `Protected`, `Blessed`, and `Haste`.

## Undertow Stance Taunt

Tidesworn's Undertow Stance now taunts all living monsters for 3 rounds in addition to the existing -20% damage weaken and +35 DEF. In multi-monster combat, all enemies receive both the taunt (forced to attack you) and the weaken. Reinforces the Tidesworn's role as a tank class.

## AoE Spells Skip Target Prompt

AoE spells (like Deluge of Sanctity) no longer ask "Target all monsters? (Y/N)" — they automatically hit all enemies. Single-target spells still prompt for target selection.

## Spell Natural 1 No Longer Auto-Fails

Spells no longer auto-fail on a natural 1 roll. Previously, rolling a 1 on the D20 was a critical failure regardless of modifier — so a roll of `1 + 28 = 29 vs DC 11` would still fizzle, which felt punishing and confusing. Following D&D 5e rules (where natural 1/20 only apply to attack rolls, not ability checks), spells now only fail from:
- **Inexperience fumble** (flat chance based on proficiency, reduced by training)
- **Not beating the DC** (total roll < DC)

High-proficiency casters no longer lose mana to an uncontrollable 5% failure rate.

## Spell Fumble Message Clarity

Spell failure messages now explain why the spell failed instead of showing a misleading roll. Inexperience fumbles show "Miscast! Train at the Level Master to reduce fumble chance." instead of a fake roll of 0. Normal failures still show the full roll breakdown.

## Quest Hall Global Commands Fix

Global commands (`!`, `/health`, `/gear`, `?`) didn't work in the Quest Hall — same issue as the Temple in v0.53.3. Added `TryProcessGlobalCommand` call.

## Two-Handed Weapon Shield Loot Warning

When finding a shield as loot while wielding a two-handed weapon (staff, bow, etc.), the stat comparison said "Slot is empty - this would be an upgrade!" without warning that equipping the shield would unequip your main weapon. Now shows a clear warning.

## Ring Slot Selection Fix

Equipping a ring to the Right Finger from the inventory always put it on the Left Finger instead. The "smart" ring slot logic overrode the player's explicit choice when both slots were empty. Now respects the chosen slot and only redirects when the chosen slot is occupied.

## Vicious Mockery Off-Hand Fix

Non-melee abilities (Vicious Mockery, Carnival of Chaos, charm/fear/confusion effects) no longer trigger off-hand follow-up attacks when dual-wielding. Only physical strike abilities get the off-hand swing.

## Companion Reflect Fix

Damage reflection (from Tidal Ward, Harmonic Shield, etc.) was completely ignored when monsters attacked companions/teammates. The `MonsterAttacksCompanion` path had no reflect check. Now properly reflects damage back at attackers when companions have the `Reflecting` status.

## NPC Teammate Empty Slot Loot Fix

NPC teammates never picked up loot for empty equipment slots. Armor items with stat bonuses but `Armor = 0` were calculated as `itemPower = 0`, matching the empty slot's `currentPower = 0`, causing the pickup check to skip. Now any item with stats is always picked up for an empty slot.

## Item Serialization Stat Loss Fix

Three critical serialization bugs caused items to permanently lose stats on save/load:
- **Agility and Stamina** fields were completely missing from `InventoryItemData` — ALL items with these bonuses lost them on save/load, in both inventory and chest.
- **BlockChance and ShieldBonus** were missing from chest serialization — shields stored in the chest lost their block stats.
- **BlockChance and ShieldBonus** were missing from inventory deserialization — even though saved, these fields were never restored on load.

## Enchantment Menu Duplicate Fix

The Magic Shop enchantment menu displayed Mythic, Legendary, Godforged, Phoenix Fire, and Frostbite enchantments twice — once in the "Special Enchantments" section and again in the "Mythic & Elemental" section. The special section loop ran through all remaining tiers instead of stopping at tier 9.

## Voidreaver Class Rebalance

The Voidreaver prestige class was massively overtuned — dealing 17k damage at level 35 and 40k at level 55 through multiplicative stat scaling, defense-ignoring spells, and stacking lifesteal. Comprehensive rebalance:

**Stat growth per level reduced:** STR 5→4, INT 5→3, DEX 4→3, HP 6→5, Mana 12→10.

**Ability nerfs:**
- Hungering Strike: 90→60 base, lifesteal 30%→20%
- Devour: 160→120 base, lifesteal 50%→30%, low HP bonus 2x→1.5x
- Entropic Blade: 180→140 base
- Void Rupture: 220→160 base, explosion 100→60
- Annihilation: 500→300 base
- Apotheosis of Ruin: +100 ATK→+60, duration 4→3 rounds, lifesteal 20%→10%

**Spell nerf:** Unmaking base damage 350-450→200-280.

**Passive nerfs:** Void Hunger heal 10%→5%, Pain Threshold 20%→15%, Soul Eater mana 15%→10%, Reflection 25%→15%.

## NPC Duel/Murder XP Exploit Fix

Street encounter NPC fights and murders awarded XP twice — once from the combat engine and again from the encounter handler. Inn duels had the same double-dipping plus no daily limit. Fixed:
- Street fights and murders no longer add bonus XP on top of combat engine rewards
- Inn duels capped at 3 per day (shares counter with Seth fights)

## Bank Robbery Gold Exploit Fix

Players could rob the bank, redeposit the stolen gold, and rob again endlessly — the safe contents included the robber's own deposits. Robbery loot now excludes the robbing player's own bank balance.

## Bank Robbery Difficulty Increase

Bank robbery guards significantly buffed to make robberies a serious endgame challenge:
- Base guard count: 2→4, cap raised to 12
- War hounds: always present (1-3), no longer 50% chance
- Captain HP/STR/DEF roughly doubled (now 3x dungeon monster scaling)
- Regular guard HP/STR/DEF roughly doubled (now 2x dungeon monster scaling)
- Repeat robbery penalty doubled

## Throne Challenge Difficulty Increase

Taking the throne is now near-impossible without endgame power:
- King defender HP bonus: +35%→+100% (double HP)
- King defender DEF bonus: +20%→+50%
- Level range tightened: must be within 10 levels of king (was 20)
- Fallback king minimum level: 20→30, faster reign scaling
- Fallback king stats roughly 60% higher across the board
- Royal guard stats roughly 60% higher

## Siren's Lament Critical Cast

Siren's Lament (Wavecaller debuff spell) now has a meaningful critical cast bonus: double duration (8 rounds instead of 4) and bonus psychic damage scaled with CHA.

## Missing Localization Key Fix

`combat.reality_unravels` (Voidreaver Unmaking kill reward message) was showing as a raw key. Added to en.json.

## Unmaking Spell Cooldown

Voidreaver's Unmaking spell now has a 1-round cooldown. The quickbar shows `(CD:1)` when on cooldown, the spell is grayed out, and attempting to cast it shows "Unmaking is on cooldown!" without consuming the turn. Previously it could be spammed every round.

## Bank Robbery Empty Vault Message

When the bank vault only contains the robber's own deposits, the robbery now shows "You crack the vault open... but it's all YOUR gold in there!" instead of the confusing "SUCCESS! You stole 0 gold!" The vault remaining display also excludes the player's own balance.

## Bank Guard Loot Quality Fix

Bank guards had no `Level` set on their Monster objects, causing the loot system to generate level-1 quality drops regardless of the player's level. Captain now drops level+5 quality loot, regular guards at player level, war hounds at level-5.

## Inn Duel Relationship Display Fix

The NPC interaction screen showed `Relationship: {0}: Neutral` with a raw placeholder instead of the actual relationship text. The `Loc.Get` call was missing the parameter. Also fixed a duplicate acceptance line that showed `{0} accepts your challenge!` twice (second line changed to "They'll regret this decision!").

## Mugger Encounter Invalid Input Fix

Pressing any invalid key at the mugger encounter (Fight/Surrender/Run) silently ended the encounter, letting the player avoid the fight. Invalid input now defaults to fight — you can't ignore thugs.

## Bank Guard Resign Key Fix

The `[*]` Resign Guard Duty key at the bank conflicted with the `*` global inventory command. Changed to `[X]`.

## Godot Reference Cleanup

Removed 11 leftover "Godot" engine references from code comments across 6 files. The game was originally prototyped in Godot before migrating to pure .NET console.

---

## Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.53.4; Voidreaver passive reductions; King defender bonuses increased (HP +100%, DEF +50%); level range tightened to 10
- `Scripts/Core/Character.cs` — Ring slot selection respects player choice instead of always overriding to LFinger; `UnmakingCooldown` transient combat property
- `Scripts/Systems/ClassAbilitySystem.cs` — `IsSpellcaster()` includes all 5 prestige classes; Undertow Stance taunt + description; Voidreaver ability base damage reductions across 6 abilities; Apotheosis ATK/duration/lifesteal reduced
- `Scripts/Systems/CombatEngine.cs` — `Reflecting` status cleared at combat start; spell target selection allows Enter for random; AoE spells auto-target all; Undertow Stance taunt in both combat paths; two-handed weapon shield loot warning; companion reflect check in `MonsterAttacksCompanion`; non-melee abilities skip off-hand follow-up; NPC teammate empty slot loot fix; Apotheosis/Devour/Void Rupture combat handlers rebalanced; lifesteal_20 handler; Unmaking cooldown check in quickbar display + unavailable handler + round tick-down; cooldown-blocked spells skip "Train at Master" message
- `Scripts/Systems/SpellSystem.cs` — Natural 1 no longer auto-fails spells; fumble message clarity; Unmaking base damage 350-450→200-280; Unmaking 1-round cooldown; Siren's Lament crit bonus
- `Scripts/Systems/StreetEncounterSystem.cs` — FightNPC and murder double XP removed; mugger encounter defaults to fight on invalid input
- `Scripts/Systems/SaveDataStructures.cs` — Added `Agility` and `Stamina` fields to `InventoryItemData`
- `Scripts/Systems/SaveSystem.cs` — Agility/Stamina in inventory serialization; BlockChance/ShieldBonus/Agility/Stamina in chest serialization
- `Scripts/Core/GameEngine.cs` — BlockChance/ShieldBonus/Agility/Stamina in both inventory and chest deserialization; Godot comment cleanup
- `Scripts/Locations/BankLocation.cs` — Robbery excludes player's own deposits; empty vault message; guard stats doubled; base guard count 2→4; war hounds always present; captain 3x dungeon scaling; Level set on all guard monsters for loot quality; resign key `*`→`X`
- `Scripts/Locations/CastleLocation.cs` — Fallback king/guard stats significantly buffed; king minimum level 20→30
- `Scripts/Locations/InnLocation.cs` — Duel daily limit (3/day); duel double XP removed; relationship display `{0}` fix; duel acceptance duplicate line fix
- `Scripts/Locations/LevelMasterLocation.cs` — Voidreaver stat growth reduced (STR 5→4, INT 5→3, DEX 4→3, HP 6→5, Mana 12→10)
- `Scripts/Locations/QuestHallLocation.cs` — Added `TryProcessGlobalCommand` for global command support
- `Scripts/Locations/MagicShopLocation.cs` — Enchantment menu special section capped at tier 9 to prevent duplicate display
- `Scripts/UI/TerminalEmulator.cs` — Godot comment cleanup
- `Scripts/BBS/BBSTerminalAdapter.cs` — Godot comment cleanup
- `Scripts/Systems/CompanionSystem.cs` — Godot comment cleanup
- `Scripts/Systems/MaintenanceSystem.cs` — Godot comment cleanup
- `Scripts/Systems/NewsSystem.cs` — Godot comment cleanup
- `Scripts/Systems/WorldSimulator.cs` — Godot comment cleanup
- `Localization/en.json` — Floor 100 story text rewritten; bank vault empty/own gold messages; duel regret line; combat.reality_unravels key; inn.too_many_duels key
- `Localization/es.json` — Floor 100 story text translated
- `Localization/fr.json` — Floor 100 story text translated
- `Localization/hu.json` — Floor 100 story text translated
- `Localization/it.json` — Floor 100 story text translated
