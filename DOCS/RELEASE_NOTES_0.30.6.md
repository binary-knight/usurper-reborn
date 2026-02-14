# Usurper Reborn - v0.30.6 NPC Teammate Combat Overhaul & Guard Arrest Fix

NPC teammates now use the full spell effect and class ability systems in dungeon combat. Previously, teammate spells dealt damage only (no status effects) and teammates were limited to 5 hardcoded abilities across only 5 classes. Now all 11 classes use their full ability trees and all spell effects (poison, freeze, stun, holy, drain, etc.) trigger when teammates cast spells.

Also fixes the guard arrest system — surrendering, losing to, or failing to flee from guards now properly sends players to prison instead of doing nothing. Prison sentences now tick down daily so players don't get stuck.

---

## Bug Fixes

- **Teammate offensive spells had no special effects**: When an NPC teammate cast an offensive spell (Fireball, Ice Storm, Confusion, etc.), only raw damage was applied — the spell's SpecialEffect was never processed. A teammate Magician casting Fireball dealt damage but didn't apply burn DoT. A teammate Sage casting Confusion did damage but didn't confuse the target. Fixed by calling `HandleSpecialSpellEffectOnMonster` for both AoE and single-target teammate spells, matching the player's spell path. All 18 spell effects from v0.30.5 now work for teammates.

- **Teammate abilities limited to 5 hardcoded actions**: NPC teammates only had 5 manually coded abilities (Warrior Power Attack, Ranger Multi-Shot, Assassin Backstab, Paladin Smite, Bard Inspire) with no special effects. The other 6 classes (Barbarian, Alchemist, Jester, Sage, Cleric, Magician) had no abilities at all. None of the 30+ ability effects from v0.30.4 (Frenzy, Bloodlust, Divine Shield, Death Mark, Assassinate, etc.) triggered for teammates. Replaced with the full `ClassAbilitySystem` — teammates now pick from their entire ability tree based on class, level, and combat situation, and effects are applied through `ApplyAbilityEffectsMultiMonster` (same as the player path).

- **Surrendering to guards did nothing**: When a player with Darkness > 100 was confronted by a guard patrol and chose to surrender, the game displayed "The guards arrest you and take you to prison..." but nothing happened (TODO). Now properly sets a prison sentence and sends the player to the Prison location. Guards confiscate 25% of gold on arrest.

- **Failing to flee from guards did nothing**: When a player failed their escape roll against guards, "The guards catch you!" appeared but nothing happened (TODO). Now arrests the player with an extra day added to the sentence for resisting.

- **Losing a fight with guards had no consequences**: If a player fought the guards and lost but survived, they simply returned to the street. Now arrests the player with 2 extra days for violent resistance, plus +30 Darkness.

- **Prison sentences never decreased for players**: The daily system decremented NPC prison sentences but skipped players entirely. A player sent to prison would be stuck forever. Added player prison sentence decrement to `DailySystemManager.ProcessDailyEvents()`.

- **No Darkness display on stats screen**: The character stats alignment section only showed Chivalry — Darkness was hidden. Players could accumulate Darkness > 100 without knowing they'd crossed the "wanted" threshold. Now shows Darkness value and status: "Clean record" (0-20), "Rumored misdeeds" (21-50), "Suspicious reputation" (51-100), "WANTED by the Royal Guard!" (101+).

- **Guard encounter gave no explanation for arrest**: Guards said "Halt! We've been looking for you!" with no indication of why. Now shows the player's Darkness value in the confrontation message.

- **Prison release didn't reduce Darkness**: After serving a sentence, Darkness remained unchanged, leading to immediate re-arrest. Serving time now reduces Darkness by up to 75 points.

---

## NPC Teammate Spell Effects

Teammate casters now apply all spell special effects when casting:

- **AoE spells**: After dealing damage to all targets, each surviving monster receives the spell's SpecialEffect (e.g., a teammate casting Ice Storm now slows all surviving monsters)
- **Single-target spells**: After dealing damage, the target receives the spell's SpecialEffect (e.g., a teammate casting Poison Touch now poisons the target for 5 rounds)
- All 18 spell effects work: poison, freeze, frost, web, confusion, mass_confusion, dominate, holy, fire, drain, death, disintegrate, psychic, dispel, convert, escape

---

## NPC Teammate Ability System

Teammates now use the full ClassAbilitySystem instead of hardcoded actions:

### All 11 Classes Have Abilities
Every non-spellcaster class now uses their full ability tree in combat. A level 40 Barbarian teammate can use Frenzy (multi-hit), Bloodlust (heal on kill), and Intimidating Roar (fear). A level 50 Paladin teammate can use Divine Smite, Divine Shield (invulnerability), and Holy Avenger.

### Smart Ability Selection AI
- **AoE priority**: If 3+ monsters are alive, prefers AoE abilities (Whirlwind, Holy Explosion, etc.)
- **Execute priority**: If a monster is below 30% HP, prefers execute-type abilities (Execute, Assassinate)
- **Strongest available**: Otherwise picks the highest-level attack ability the teammate can afford
- **Buff/Defense fallback**: Occasionally uses buff or defensive abilities when no attack is available
- **Stamina management**: Only uses abilities the teammate can afford stamina-wise; 30% chance per turn to avoid spamming

### Full Effect System
All 30+ ability effects now trigger for teammates:
- Stun, poison, charm, holy damage, fire damage, drain, rage
- Execute (bonus damage to low-HP targets), last stand (bonus when low HP)
- Frenzy (multi-hit), backstab (critical), dodge preparation
- Inspire (party buff), intimidate (fear), smoke screen
- Status immunity, armor pierce, and more

---

## Guard Encounter System

When a player with Darkness > 100 encounters a guard patrol:

| Choice | Result |
|--------|--------|
| **Surrender** | Arrested — default sentence, 25% gold confiscated |
| **Fight (win)** | Escape but +30 Darkness |
| **Fight (lose)** | Arrested — sentence + 2 extra days for violence, +30 Darkness |
| **Bribe (100g)** | Guards look the other way |
| **Run (success)** | Escape into the crowd |
| **Run (fail)** | Arrested — sentence + 1 extra day for resisting |

Players with Darkness <= 100 are told "Stay out of trouble, citizen" and guards move on.

---

## SysOp Player Pardon

New `[P] Pardon Player` option in the SysOp Administration Console. Allows server administrators to release a player from prison, clear their Darkness, or grant a full pardon.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version bump to 0.30.6 |
| `Scripts/Systems/CombatEngine.cs` | Added `HandleSpecialSpellEffectOnMonster` calls to `TryTeammateOffensiveSpell` for both AoE and single-target paths; replaced `TryTeammateClassAbility` 5-class hardcoded switch with full `ClassAbilitySystem.GetAvailableAbilities()` + `UseAbility()` + `ApplyAbilityEffectsMultiMonster()` pipeline covering all 11 classes; removed 5 hardcoded methods |
| `Scripts/Systems/StreetEncounterSystem.cs` | Implemented guard arrest for surrender, failed flee, and fight loss; all paths set DaysInPrison and throw LocationExitException to Prison; added Darkness display to guard confrontation |
| `Scripts/Locations/MainStreetLocation.cs` | Added Darkness value and wanted status to character stats alignment section |
| `Scripts/Locations/PrisonLocation.cs` | Added Darkness reduction (-75) on prison sentence completion |
| `Scripts/Systems/DailySystemManager.cs` | Added player DaysInPrison decrement and PrisonEscapes restoration to ProcessDailyEvents() |
| `Scripts/Systems/SysOpConsoleManager.cs` | Added `[P] Pardon Player` option — reads player save, shows prison/Darkness status, offers release/clear/full pardon, writes modified save |
