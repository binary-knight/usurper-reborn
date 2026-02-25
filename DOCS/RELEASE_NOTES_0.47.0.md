# Usurper Reborn v0.47.0 - Prestige

## New Feature: NG+ Prestige Classes

Five powerful new classes available exclusively to players who have completed the game at least once. Each prestige class is tied to a specific alignment path and draws on the game's deepest mythology — the Ocean, the Old Gods, and the cycle of reality itself.

Unlike the 11 base classes, prestige classes have **both spells AND combat abilities**, making them uniquely versatile and strictly more powerful than their base counterparts.

### How to Unlock

Prestige classes are gated behind your ending history. Each completed ending permanently unlocks specific classes:

- **Savior ending** unlocks **Tidesworn** (Holy) and **Wavecaller** (Good)
- **Defiant ending** unlocks **Cyclebreaker** (Neutral)
- **Usurper ending** unlocks **Abysswarden** (Dark) and **Voidreaver** (Evil)
- **True Ending** or **Secret Ending** unlocks all five

Classes are cumulative — complete multiple endings across cycles to unlock more options.

### The Five Prestige Classes

**Tidesworn** (Holy) — *The Ocean's Divine Shield*
Mortal vessels of the Ocean's will who channel primordial waters. The ultimate tank/healer hybrid with the highest aggro weight in the game. Spells include Tidal Ward (damage reflection), Covenant of the Deep (party-wide buffs), and Deluge of Sanctity (AoE damage + self-heal). Abilities like Breakwater (+100 defense) and Living Waters (healing + regeneration) make them nearly unkillable.

**Wavecaller** (Good) — *Conductor of the Ocean's Harmonics*
Support specialists who amplify their allies' potential. The strongest party buffer in the game with massive group healing and crowd control. Symphony of the Depths grants +60 attack, +40 defense, and +30% crit to all allies at the cost of the Wavecaller's own health. Abilities include Inspiring Cadence (party attack buff) and Crescendo (AoE damage scaling with party size).

**Cyclebreaker** (Neutral) — *Reality Manipulator*
Those who perceive the cycle's machinery and exploit the seams between iterations. Perfectly balanced stats with reality-bending abilities. Deja Vu grants perfect evasion, Probability Shift cripples enemy accuracy, and Paradox Collapse deals bonus damage based on total combat damage dealt. The Borrowed Power ability scales with cycle number, growing stronger each playthrough.

**Abysswarden** (Dark) — *Warden of the Old Gods' Prisons*
Those who siphon divine energy from the sealed domains. A drain/debuff striker with excellent sustain through life steal. Devour Essence deals massive damage while healing 75% back and restoring mana. Noctura's Whisper debuffs enemies with a chance to skip turns. Seal Fracture deals 200 damage with overflow spreading to all enemies on kill.

**Voidreaver** (Evil) — *Consumer of the Void Between Cycles*
The ultimate glass cannon. Sacrifice your own health for devastating power. Blood Pact permanently buffs attack for the fight at the cost of HP. Void Bolt ignores all defense. Unmaking deals 350-450 damage and costs 25% HP — but restores everything if it kills. Apotheosis of Ruin burns 40% HP for 4 rounds of +100 attack, cleave damage, and 20% lifesteal.

### Power Level

Prestige classes gain approximately 20-33% more stats per level than base classes. Combined with having both spells and abilities, they represent a meaningful reward for completing the game.

| Class | Role | Aggro | Key Strength |
|-------|------|-------|--------------|
| Tidesworn | Tank/Healer | 190 | Highest defense, party protection |
| Wavecaller | Support/Buffer | 85 | Best party buffs, group healing |
| Cyclebreaker | Versatile | 110 | Probability manipulation, balanced |
| Abysswarden | Drain/Debuff | 75 | Life drain sustain, powerful debuffs |
| Voidreaver | Glass Cannon | 140 | Highest raw damage, HP-for-power trades |

## Bug Fix: Immortal Reroll NG+ Integration

Players who ascended to immortality and later renounced were not receiving NG+ benefits (prestige classes, stat bonuses, XP multiplier, gold bonus). The ending was never recorded and the cycle counter never incremented during the ascension path.

**Fixed**: Ascending to godhood now properly records the completed ending and advances the NG+ cycle. When an immortal renounces and rerolls, they receive the full NG+ experience — prestige class selection, cycle stat bonuses, and ending-specific perks — just like players who chose NG+ directly after defeating Manwe.

Legacy migration: Players who ascended before this fix will have their ending inferred from their divine alignment (Light→Savior, Dark→Usurper, Balance→Defiant) when they renounce.

## Improvement: Online Admin Console UX

The Online Admin Console now uses numbered player selection for all operations — matching the SysOp Console pattern. Previously, most operations required typing usernames manually.

- **List/Edit merged**: [1] now shows a paginated player list where you enter a number to jump directly into the stat editor
- **Ban, Delete, Reset Password, Immortalize**: All use numbered selection lists instead of free-text username entry
- **Immortalize filter**: Only shows eligible players (excludes already-immortal and alt characters)
- **Prestige class names**: Both Admin and SysOp consoles now display prestige class names correctly

## Old God Boss Abilities Now Functional

All seven Old God boss encounters now have fully working combat abilities. Previously, boss ability names were announced during combat but had no mechanical effect — bosses just performed normal attacks every round. Now every ability has a real combat impact.

### Generic Boss Abilities (All 7 Gods)

Over 40 boss abilities across all seven gods are now mapped to actual combat effects:

- **Damage abilities** (Cleave, Whirlwind, Solar Flare, World Breaker, etc.) — Deal 1.5x to 3x normal damage
- **Self-healing** (Blood Sacrifice, Light's Embrace, Core Awakening, etc.) — Restore 5-10% of max HP
- **Stun/CC** (Binding Chains, Charm, Judgment, etc.) — Stun the player for 1 round
- **Summon** (Summon Soldiers, Shade Clones, etc.) — Spawn 1-2 minion monsters mid-fight
- **Buff** (War Cry, Berserker Rage, Enrage, etc.) — Increase boss damage for 3 rounds
- **Debuff** (Heartbreak, Memory Theft, etc.) — Reduce player stats for 3 rounds via Cursed status
- **Life drain** (Passion's Fire, Shadow Merge, etc.) — Deal damage and heal 30% back
- **Fear** (Primal Roar, Shadow Sovereignty, etc.) — Apply Fear status for 2 rounds

Bosses now use abilities 70% of the time (up from 30%), making fights much more dynamic and tactical.

### Manwe Custom Abilities (Final Boss)

Manwe, the Creator, has 12 unique abilities across three combat phases that escalate as the fight progresses:

**Phase 1 (100-50% HP):**
- **Word of Creation** — Heals 8% max HP
- **Unmake** — 2.5x damage strike
- **Divine Judgment** — 1.5x damage, +50% bonus if player's Darkness exceeds Chivalry
- **Time Stop** — Stuns the player for 1 round
- **Reality Warp** — Random effect: self-heal, damage, or stat debuff

**Phase 2 (50-10% HP):**
- **Split Form** — Summons a "Shadow of Manwe" clone (25,000 HP, half stats). Can only be used once.
- **Light Incarnate** — 3x damage blast
- **Shadow Incarnate** — 2x damage + 30% life drain
- **The Question** — 50% chance Manwe ponders (skips attack), 50% chance devastating 3x damage

**Phase 3 (Below 10% HP):**
- **Final Word** — 4x damage
- **Creation's End** — Instant kill if player is below 20% HP. Blocked by the Worldstone artifact (reduced to 3x damage instead).
- **The Offer** — Combat pauses. Manwe offers the player a choice: accept peace and end the fight, or refuse and continue to the death. Can only occur once.

## Missing Old God Dialogue Trees

Three Old Gods were missing their dialogue trees entirely, causing encounters to fall through to forced combat with no narrative context.

### Aurelion, the Dimming Light (Floor 85)

- **"Your light blinds the world. I'll put it out."** — Aggressive approach. Aurelion meets you with full force.
- **"I've come to free you, not fight you."** — Humble approach. Aurelion tests your worthiness through combat.
- **"Show me the truth you guard."** — If your Chivalry is high enough (1000+), Aurelion shares his truth and marks as Awakened. Otherwise, prove yourself in battle.

### Terravok, the Worldbreaker (Floor 95)

- **"Wake up and face me, mountain."** — Aggressive. Terravok erupts in rage.
- **"I mean you no harm. I seek passage."** — Respectful approach. Terravok tests you with restraint.
- **"Sleep on, old one. I'll find another way."** — Terravok remains dormant, marked as Awakened.

### Manwe, the Creator (Floor 100)

- **"I'm here to end you, Creator."** — Aggressive. Manwe fights at full power with rage modifiers (+25% damage, +15% crit, -15% defense).
- **"The cycle of suffering must end."** — Righteous. Balanced modifiers (+15% defense, boss -10% damage).
- **"I don't want to fight you."** branches into:
  - **"Let me take the burden."** — Compassionate. Boss weakened (-20% damage, -15% defense).
  - **"Walk away from creation."** — Alliance. No combat. Gain Ocean Insight and the Creator's Rest wave fragment.

## Expanded Prestige Class Abilities (Levels 50-95)

All five prestige classes now have **11 abilities each** spanning levels 1 through 95, plus access to all 5 universal abilities (Second Wind, Focus, Rally, Desperate Strike, Iron Will). Previously they only had 5 abilities spanning levels 1-40. Every ability's special effect now has a full combat implementation — the original 25 abilities were placeholders with no mechanical effect.

### New Abilities by Class

**Tidesworn** (Holy Tank/Healer):
- **Abyssal Anchor** (Lv50) — Root in place with +80 DEF. Enemies deal reduced damage.
- **Sanctified Torrent** (Lv60) — 120 holy AoE. 2x damage vs undead/demons. Heals 20% dealt.
- **Ocean's Embrace** (Lv70) — Full party heal 150 HP + cleanse all debuffs.
- **Tidal Colossus** (Lv80) — Grow massive: +60 ATK, +60 DEF, stun immunity for 4 rounds.
- **Eternal Vigil** (Lv90) — Invulnerable for 2 rounds.
- **Wrath of the Deep** (Lv95) — 350 damage capstone. Instant kill below 30% HP (non-boss). Heals 50%.

**Wavecaller** (Support/Buffer):
- **Harmonic Shield** (Lv50) — All allies +40 DEF and damage reflection for 3 rounds.
- **Dissonant Wave** (Lv60) — All enemies: weakened, vulnerable, 25% stun chance.
- **Resonance Cascade** (Lv70) — 100 AoE. +25% damage per additional enemy hit.
- **Tidal Harmony** (Lv80) — All allies heal 200 HP. Self gains +40 ATK for 4 rounds.
- **Ocean's Voice** (Lv90) — Ultimate party buff: +50 ATK, +30 DEF, +20% crit for all allies.
- **Grand Finale** (Lv95) — 300 AoE + 50 per active buff. Consumes all buffs. Capstone.

**Cyclebreaker** (Reality Manipulation):
- **Timeline Split** (Lv50) — Temporal clone mirrors attacks for 3 rounds (Haste effect).
- **Causality Loop** (Lv60) — Enemy confused + weakened for 3 rounds.
- **Chrono Surge** (Lv70) — Take 2 actions this round.
- **Singularity** (Lv80) — 200 AoE. Stunned enemies take 2x damage.
- **Temporal Prison** (Lv90) — Target cannot act for 2 rounds (boss: 1 round).
- **Cycle's End** (Lv95) — 400 damage + 50 per NG+ cycle (max +250). Ignores 50% defense. Capstone.

**Abysswarden** (Drain/Debuff):
- **Soul Leech** (Lv50) — 130 damage. 40% lifesteal (60% if target poisoned).
- **Abyssal Eruption** (Lv60) — 150 AoE + corruption DoT on all enemies.
- **Dark Pact** (Lv70) — Sacrifice 20% max HP. Gain +80 ATK and 25% lifesteal.
- **Prison Warden's Command** (Lv80) — Target: -50% ATK, -50% DEF (boss: half effect).
- **Consume Soul** (Lv90) — 250 damage. On kill: +5 permanent ATK this combat.
- **Abyss Unchained** (Lv95) — 380 AoE. Full HP heal. Remove all debuffs. Capstone.

**Voidreaver** (Glass Cannon):
- **Devour** (Lv50) — 160 damage. 50% lifesteal. Below 30% HP: damage doubles.
- **Entropic Blade** (Lv60) — 180 damage ignoring defense. Costs 10% HP.
- **Blood Frenzy** (Lv70) — Sacrifice 25% HP. Double attack speed + 50 ATK for 3 rounds.
- **Void Rupture** (Lv80) — 220 AoE. Killed enemies explode for 100 damage to others.
- **Death's Embrace** (Lv90) — If HP drops to 0, revive with 1 HP + 1 round invulnerability.
- **Annihilation** (Lv95) — Costs 50% HP. 500 single-target damage. Instant kill below 50% HP (non-boss). Capstone.

### All 55 Prestige Special Effects Implemented

Every prestige ability special effect now has full combat logic including: damage modifiers, lifesteal, DoT/corruption, status buffs, stat debuffs, AoE with chaining, party buffs, clone/haste, cycle-scaling damage, and execute mechanics.

## Bug Fixes

- **Void Key Awarded After Manwe**: The Void Key artifact was incorrectly configured as Manwe's boss drop, meaning players received it after defeating the final boss — but it's supposed to be the key that unlocks Manwe's prison in the first place. The Void Key now correctly auto-triggers when collecting 6 artifacts from the other gods (as intended by ArtifactSystem). Manwe no longer drops any artifact.
- **Admin Stat Edits Not Persisting**: Online Admin Console stat changes (Strength, Defence, etc.) reverted on the next login because only computed stats were updated, not base stats. Both are now set together so changes survive RecalculateStats().
- **Manwe Encounter Not Triggering**: Old God encounter prerequisites only counted gods with "Defeated" status, ignoring Saved/Allied/Awakened paths. Players who chose non-combat resolutions couldn't progress. Now counts any resolved god state.
- **Disconnect After Manwe Loses NG+ Progress**: If a player disconnected after defeating Manwe but before answering the NG+ prompt, their ending was lost. Added a login failsafe that detects interrupted ending sequences and re-triggers them.
- **Auto-Combat Item Drops Accidentally Discarded**: When auto-combat ended and an item dropped, any key pressed to stop auto-combat could leak into the item prompt, silently discarding equipment. Now flushes pending input before loot prompts and validates E/T/L input strictly.
- **Paladin/Bard/Alchemist Mana Display in Character Creation**: Class preview showed "Mana: None" for classes that gain mana per level. Now shows "+N/level" in preview and calculates effective mana including INT/WIS bonuses on the stat roll screen.
- **XP Curse Trap Rebalanced**: Dungeon curse traps could drain 250 XP on floor 5 with zero counterplay. Now has Wisdom resistance (up to 60%), XP drain capped to 5% or level×25, Constitution reduction up to 40%.
- **Romantic Encounter UX**: Street encounter outcome now shows a "Press any key" prompt instead of a 2-second auto-dismiss, so players can actually read what happened.
- **Dungeon Floor Cleanup**: Removed duplicate Old God encounter triggers from floor 80 (Terravok) and floor 100 (Manwe) story events. Boss room encounters now handle all god fights consistently without double prompts.

## Improvement: Character Display Name

Online mode now prompts for a character display name during creation — enter a name like "Thalric Ironvane" or press Enter to use the account name as-is. The account username remains the internal save key.

## Files Changed

- `GameConfig.cs` — Version 0.47.0; MaxClasses 11→16; added 5 prestige class starting attributes, class names, help text, descriptions, `PrestigeClasses` set, `IsPrestigeClass()` helper
- `Character.cs` — Added Tidesworn, Wavecaller, Cyclebreaker, Abysswarden, Voidreaver to CharacterClass enum
- `CharacterCreationSystem.cs` — Added `GetUnlockedPrestigeClasses()` gating method; prestige class section in SelectClass menu with NG+ separator and descriptions; ending-to-class mapping
- `SpellSystem.cs` — Added 25 prestige class spells (5 per class): Tidesworn holy water magic, Wavecaller harmonics, Cyclebreaker temporal spells, Abysswarden prison siphon spells, Voidreaver void annihilation spells
- `ClassAbilitySystem.cs` — Added 55 prestige class abilities (11 per class, levels 1-95): Tidesworn defensive/healing, Wavecaller party support, Cyclebreaker reality manipulation, Abysswarden drain/shadow, Voidreaver self-sacrifice/damage. Added prestige classes to all 5 universal abilities (Second Wind, Focus, Rally, Desperate Strike, Iron Will)
- `LevelMasterLocation.cs` — Added 5 per-level stat increase cases for prestige classes
- `CombatEngine.cs` — Added 5 aggro weight entries for prestige classes; implemented all 55 prestige ability special effects (damage, lifesteal, DoT, buffs, debuffs, AoE, party support, execute mechanics, cycle-scaling)
- `EndingsSystem.cs` — Records completed ending and increments cycle when player ascends to immortality (was skipped before)
- `PantheonLocation.cs` — Legacy migration: infers ending from divine alignment for pre-fix immortals when renouncing
- `OnlineAdminConsole.cs` — Full UX overhaul: numbered player selection for all operations; merged List+Edit; added `ShowPlayerList()` helper; added prestige class names; Immortalize filters to eligible players
- `SysOpConsoleManager.cs` — Fixed hardcoded class count (was `<= 10`, now uses `MaxClasses`); added prestige class names to ClassNames array
- `EnhancedNPCBehaviorSystem.cs` — Fixed hardcoded array size (was `bool[11]`, now uses `MaxClasses`)
- `web/ssh-proxy.js` — Added 5 prestige class entries to CLASS_NAMES mapping
- `DialogueSystem.cs` — Added `RegisterAurelionDialogue()`, `RegisterTerravokDialogue()`, `RegisterManweDialogue()` with full branching dialogue trees and story flag effects
- `OldGodBossSystem.cs` — Updated `ApplyManweModifiers()` to check dialogue flags (compassion, righteous, aggressive) before applying god-count bonuses; `CheckPrerequisites()` counts any resolved god state (not just Defeated); Manwe artifact award guarded (no drop)
- `OldGodsData.cs` — Removed Manwe's `ArtifactDropped = VoidKey` (Void Key auto-triggers via ArtifactSystem when 6 artifacts collected)
- `OnlineAdminConsole.cs` — Admin stat editor now sets both computed and base stats to prevent RecalculateStats() from reverting changes
- `EndingsSystem.cs` — Sets `ending_sequence_completed` story flag after NG+ prompt answered or immortal ascension; auto-saves after flag set
- `GameEngine.cs` — Login failsafe: detects interrupted ending sequences (Manwe resolved + ending flag + no completion flag) and re-triggers
- `CombatEngine.cs` — Added `TryBossAbility()` routing, `TryGenericBossAbility()` with ~40 ability mappings, `TryManweBossAbility()` with 12 custom Manwe abilities; boss ability chance 30%→70%; Manwe Split Form and Offer tracking flags
- `DungeonLocation.cs` — Removed duplicate Terravok encounter from case 80, removed duplicate Manwe encounter + moral paradox fallback from case 100
- `StreetEncounterSystem.cs` — Romantic encounter: replaced 2-second delay with PressAnyKey prompt
- `CharacterCreationSystem.cs` — Class preview mana shows "+N/level" for Paladin/Bard/Alchemist; stat roll screen shows effective mana with INT/WIS bonuses; online mode character creation prompts for display name
- `DungeonLocation.cs` — XP curse trap: Wisdom resistance, drain cap, Constitution reduction
