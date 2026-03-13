# Usurper Reborn v0.52.3 - Prestige Class Overhaul

---

## Bug Fixes

- **All prestige class spells completely non-functional** — `ExecuteSpellEffect()` in SpellSystem.cs only had switch cases for Cleric, Magician, and Sage. All 25 prestige class spells (5 each for Tidesworn, Wavecaller, Cyclebreaker, Abysswarden, Voidreaver) silently failed — mana was deducted but zero effect was applied (no damage, no healing, no buffs, no debuffs). Attack-type spells in multi-monster combat got fallback damage from a safety net, but Buff/Heal/Debuff spells produced absolutely nothing. Added `ExecuteTideswornSpell()`, `ExecuteWavecallerSpell()`, `ExecuteCyclebreakerSpell()`, `ExecuteAbysswardenSpell()`, and `ExecuteVoidreaverSpell()` methods with full implementations for all 25 spells including damage scaling, healing, protection bonuses, status effects, lifesteal, HP sacrifice costs, and special effects. This bug has existed since prestige classes were first implemented in v0.47.0.

- **Fracture Reality echo damage missing kill tracking** — The 25% echo (double damage) from Fracture Reality never checked if the target died in either combat path. In single-monster combat, the monster could reach negative HP from echo damage but not be registered as defeated. Added kill tracking after both damage applications in both single and multi-monster paths.

- **Quantum State description overstated dodge chance** — Description said "50% chance to avoid any attack" but the Blur status only provides 20% miss chance. The ability actually grants a guaranteed dodge of the first attack plus 20% ongoing evasion. Updated description to accurately reflect the mechanic: "Dodge the next attack and 20% evasion for 3 rounds."

- **StatusEffect.Lifesteal never consumed** — The Lifesteal status enum value existed and was set by 5+ abilities (Corrupting Touch, Dark Pact, lifesteal_10, Apotheosis of Ruin, etc.) but `ApplyPostHitEnchantments()` never checked for it. Abilities that granted lifesteal via StatusEffect had zero actual lifesteal. Added `StatusLifestealPercent` property to Character, set it per-ability with appropriate percentages (10%/15%/20%/25%), added consumption in `ApplyPostHitEnchantments()`, and cleanup when status expires.

- **Umbral Step guaranteed crit not working** — The Hidden status was set but never checked in the crit logic. Fixed by using the same `TempAttackBonus += 999` pattern as Cyclebreaker's Temporal Feint for a guaranteed critical hit on the next attack.

- **Noctura's Whisper description overstated effect** — Spell description claimed "20% skip turn" but the implementation only applies weaken (attack/defense debuff) with no skip-turn mechanic. Removed the false "20% skip turn" from the description.

- **Apotheosis of Ruin lifesteal broken** — Set `StatusEffect.Lifesteal` for 4 rounds but never set `StatusLifestealPercent`, so the 20% lifesteal that was supposed to sustain you through the 40% HP sacrifice did absolutely nothing. Fixed by setting `StatusLifestealPercent = 20` in both combat paths.

- **Void Shroud reflection wrong percentage and wrong message** — Used `GameConfig.WavecallerReflectionPercent` (15%) instead of the advertised 25%, and displayed Wavecaller-themed "Harmonic energy reflects..." message. Fixed with class-specific percentages and Voidreaver-themed messaging.

- **Void Bolt "ignores all defense" did nothing** — SpellSystem set `SpecialEffect = "ignore_defense"` but no handler existed in `HandleSpecialSpellEffectOnMonster`. The spell dealt normal damage with full defense applied. Added handler that deals bonus damage equal to target's armor to compensate for the defense subtraction.

- **Unmaking kill reward never triggered** — Description said "If kills: restore all HP and mana" and SpellSystem set `SpecialEffect = "unmaking"`, but no handler existed. The 250-mana, 25% HP cost spell had no kill payoff. Added handler that fully restores HP and mana when the target dies.

- **Blood Pact "+30% crit" never applied** — Description said "+50 attack, +30% crit" but implementation only granted attack bonus. Fixed by granting guaranteed critical strike on next attack and updated description to reflect the mechanic.

- **Reap "kill resets cooldown" never implemented** — Description said kills reset the cooldown, but no cooldown reset code existed. After killing with Reap, the 3-round cooldown applied normally. Added cooldown reset on kill in both combat paths.

- **Death's Embrace revive-on-death not implemented** — Description said "If HP reaches 0, revive with 1 HP and become invulnerable for 1 round" but implementation only gave Regenerating (3 rounds) + DodgeNextAttack. No death-prevention mechanic existed — the level 90 capstone's defining feature was missing. Added `DeathsEmbraceActive` flag on Character, revive check after both regular and ability damage application (revives at 15% max HP, consumes the effect, grants dodge).

- **Offer Flesh desperate bonus only lasted 1 round** — In desperate mode (<25% HP), the +60 bonus ATK was applied via `TempAttackBonus` which is a 1-round bonus. The base +60 ATK lasts 3 rounds, so the "double" bonus degraded to normal after the first round. Fixed by setting `TempAttackBonusDuration` to match the ability duration.

- **Cyclebreaker Cycle Memory XP bonus missing from multi-monster combat** — The +5%/cycle XP bonus was applied in single-monster and group combat paths but not in HandleVictoryMultiMonster. Players fighting multiple monsters got no cycle XP bonus. Added the bonus between NG+ multiplier and Guild XP bonus.

- **DeathsEmbraceActive and StatusLifestealPercent not reset at combat start** — These combat-transient flags were never cleared at initialization, so they could persist from a previous combat if the player fled. Added resets in both PlayerVsMonsters and PlayerVsPlayer initialization blocks.

- **Cyclebreaker Probability Shift spell had no effect** — SpellSystem set `SpecialEffect = "probability_shift"` but no handler existed in `HandleSpecialSpellEffectOnMonster`. The debuff (reduce accuracy and power) silently did nothing. Added handler that applies weakened + confused for 3 rounds.

- **Cyclebreaker Echo of Tomorrow "ignores 50% defense" did nothing** — SpellSystem set `SpecialEffect = "ignore_half_defense"` but no handler existed. The spell dealt normal damage with full defense. Added handler that deals bonus damage equal to 50% of target's armor.

- **Symphony of the Depths "+30% crit" never applied** — Description claimed +30% crit but implementation only gave attack + protection bonuses. Added guaranteed crit on next hit via TempAttackBonus pattern. Updated description to "guaranteed crit on next hit."

- **Consume the Fallen description overstated effect** — Description claimed "50% of last killed enemy's max HP" but implementation heals 25% of caster's max HP (min 100). Updated description to match actual mechanic.

- **Paradox Collapse description overstated effect** — Description claimed "+10% of all damage dealt this fight" bonus but no damage tracking existed. Removed the unimplemented bonus from description.

- **Cycle Rewind description misleading** — Description said "restores HP to 3 rounds ago" but implementation heals a flat 30% max HP + cures disease. Updated description to match actual mechanic.

- **Dark Alley gambling used `new Random()` instead of `Random.Shared`** — Five places in gambling, pickpocket, and encounter code created new Random instances instead of using the shared instance. This caused poor randomness quality and potential seed collisions. Fixed all 5 occurrences (Loaded Dice, Three Card Monte, Skull & Bones, Pickpocket, Undercover Guard).

- **Dark Alley smoke bomb limit showed poison message** — When carrying 3 smoke bombs, the limit message used `dark_alley.bm_poison_limit` (poison-themed) instead of a smoke bomb-specific message. Added `dark_alley.bm_smoke_limit` localization key.

- **Enforcer encounter missing death handler** — If the Loan Shark Enforcer killed the player, no messaging or cleanup occurred. Added death handler that forgives the loan (can't collect from a corpse) with appropriate messaging.

- **Fence bonus from Shadows faction never applied** — `FactionSystem.GetFencePriceModifier()` returns a rank-scaled bonus (10-37.5%) but the Fence used hardcoded 0.70/0.80 sell rates ignoring faction rank. Now applies the faction modifier, capped at 95% of item value. Display shows current rate.

- **Safe House only restored HP** — The Safe House healed 50% HP but ignored mana entirely. Caster classes now also restore 50% mana when resting at the Safe House.

---

## Balance Changes

### Dark Alley

- **Pit fight betting rebalanced** — Maximum bet reduced from `level * 500` to `level * 200` (level 100: 50,000g → 20,000g). Maximum multiplier reduced from 3.0x to 2.5x for reckless bets. Daily potential reduced from ~750k to ~150k at level 100.

- **Skull & Bones unique mechanic** — Previously used flat win chance identical to the other games. Now uses WIS-based insight bonus (WIS/300), giving Sages, Clerics, and other wisdom-heavy classes an edge at reading the skull's patterns.

- **NPC pit fights give more reputation** — NPC arena fights now grant +8 Dark Alley reputation (up from +5), rewarding the harder and riskier challenge of fighting a named opponent.

- **Safe House robbery scaled by alignment** — Previously flat 10% robbery chance for non-Shadows. Now scales: Evil players (Darkness > 300) 2%, Dark players 5%, neutral 8%, Good/Holy players 15%. Being known in the underworld means you're less likely to be marked.

- **Shadows members get 10% discount on all shady shops** — Drug Palace, Steroid Shop, Orbs Club, Beer Hut, and Alchemist Heaven now apply a 10% discount for Shadows faction members, stacking with the existing alignment-based pricing.

- **Evil deeds grant Dark Alley reputation** — Evil deeds were the only major Dark Alley activity that didn't increase reputation. Now grants reputation scaled by tier: Petty +3, Serious +8, Dark +15.

- **Fence sales grant +1 Darkness** — Selling stolen goods through the Fence now increases Darkness by 1 per transaction, reflecting the minor criminal nature of the activity.

- **Loan repayment grants +1 Chivalry** — Paying off a loan in full now grants +1 Chivalry. Even criminals respect someone who keeps their word.

- **Lyris companion too squishy and drawing too much aggro** — Lyris (CombatRole.Hybrid) had support-tier survivability (+1 HP/level, +1 DEF/level) but was mapped to Paladin for aggro purposes (180 weight, 2nd highest). She drew heavy fire but couldn't survive it. Buffed Hybrid role: HP scaling +1→+4/level, defense scaling +1→+2/level, added +2 CON/level. Reduced aggro mapping from Paladin (180) to Ranger (130). Changes apply to both `ApplyCompanionLevelUpStats()` and `ScaleCompanionStatsToLevel()`.

- **Thundering Roar taunt off-by-one** — `TauntRoundsLeft--` was decremented *before* `SelectMonsterTarget()` checked the taunt, consuming one round of taunt before it could take effect. A 3-round taunt effectively lasted 2 rounds. Moved the tick-down to after target selection so taunts last their full stated duration.

- **AoE abilities split total damage instead of hitting each target** — `ApplyAoEDamage()` divided total damage by monster count, making AoE strictly worse than single-target abilities (e.g., Throw Bomb at 40 base dealt more per-target damage than Explosive Flask at 75 base split across 3 targets). AoE now deals full damage to each target. Base damage values reduced to ~45% across all 14 AoE abilities to compensate, resulting in slightly higher total output against 3+ targets while single-target abilities remain better for focus fire. Same fix applied to `aoe_holy` abilities which had their own inline damage split.

---

## Balance Changes

### Cyclebreaker

- **Probability Manipulation passive** — New class passive grants 25% chance to resist incoming debuffs from monster abilities (stun, poison, fear, curse, etc.). Fits the "bend probability" class fantasy. Displayed in `/health` under active buffs.

- **Cycle Memory passive** — New class passive grants +5% XP bonus per NG+ cycle, capping at +25% at Cycle 6+. Rewards repeated playthroughs and thematically represents the Cyclebreaker remembering past cycles. Applied in all 3 XP award paths (single-monster, multi-monster, group combat). Displayed in `/health`.

- **Borrowed Power buffed** — Previously gave +1 ATK/DEF per cycle count (max +10), which was negligible at NG+ power levels (+2 at Cycle 2). Now scales with both cycle count AND player level: `cycle * (level/10 + 1)`, capped at +50. At Cycle 2, Level 50, this gives +12 ATK/DEF instead of +2. At Cycle 4, Level 80, gives +36 instead of +4. Updated description to reflect new scaling.

- **Chrono Surge reworked** — Was strictly inferior to Timeline Split (1-round Haste vs 3-round Haste). Now reduces all ability cooldowns by 2 rounds in addition to granting 1 round of Haste. This makes it a unique time manipulation utility — use it to bring powerful abilities like Cycle's End or Singularity back online faster. Updated description.

### Abysswarden

- **Abyssal Siphon passive** — New class passive grants 10% lifesteal on all attacks. Every hit heals the Abysswarden for 10% of damage dealt, applied through the unified `ApplyPostHitEnchantments()` pipeline so it works with regular attacks, abilities, and dual-wield. Displayed in `/health`.

- **Prison Warden's Resilience passive** — New class passive reduces all incoming damage by 10%. Thematically represents the Abysswarden's experience containing Old God prisoners hardening them against attacks. Displayed in `/health`.

- **Corruption Harvest passive** — New class passive heals 15% max HP when killing a poisoned enemy. Synergizes with Corrupting Touch and other poison abilities, rewarding the Abysswarden's corruption-themed playstyle. Works in both single-monster and multi-monster combat. Displayed in `/health`.

- **Corrupting Touch buffed** — Base damage increased from 15 to 40 per tick. At 5 ticks, total damage goes from 75 to 200 (before scaling). The old value was negligible at NG+ power levels. Now provides meaningful DoT that justifies the ability slot and 3-round cooldown.

### Voidreaver

- **Void Hunger passive** — New class passive heals 10% max HP on every kill. Sustains the glass cannon playstyle by rewarding aggressive play and compensating for HP sacrifice abilities. Works in both single-monster and multi-monster combat. Displayed in `/health`.

- **Pain Threshold passive** — New class passive grants +20% ability damage when below 50% HP. Rewards the risk of Voidreaver's HP sacrifice abilities — the lower your health, the harder you hit. Applied to all ability damage in both combat paths. Displayed in `/health`.

- **Soul Eater passive** — New class passive restores 15% max mana on every killing blow. Sustains the Voidreaver's expensive spell kit (Unmaking costs 250 mana) by rewarding kills with mana recovery. Works in both combat paths. Displayed in `/health`.

- **Hungering Strike buffed** — Base damage increased from 60 to 90. The old value made Voidreaver's level 1 ability the weakest among prestige classes. At 90 base damage with 30% lifesteal (27 HP heal), it now competes with other prestige openers.

---

## Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.52.3; Cyclebreaker constants (`CyclebreakerDebuffResistChance` 25%, `CyclebreakerCycleXPBonus` +5%/cycle, `CyclebreakerCycleXPBonusCap` 25% max); Abysswarden constants (`AbysswardenAbyssalSiphonPercent` 10%, `AbysswardenPrisonWardResist` 10%, `AbysswardenCorruptionHealPercent` 15%); Voidreaver constants (`VoidreaverReflectionPercent` 25%, `VoidreaverVoidHungerPercent` 10%, `VoidreaverPainThresholdBonus` 20%, `VoidreaverSoulEaterManaPercent` 15%)
- `Scripts/Core/Character.cs` — `StatusLifestealPercent` property for ability-granted lifesteal; `DeathsEmbraceActive` flag for Voidreaver revive-on-death; Lifesteal status expiry cleanup
- `Scripts/Systems/SpellSystem.cs` — Added 5 prestige class cases to `ExecuteSpellEffect()` switch; 5 new execution methods implementing all 25 prestige spells; Noctura's Whisper description fix; Blood Pact guaranteed crit + updated description; Void Bolt `ignore_defense` and Unmaking `unmaking` special effects; Symphony of the Depths guaranteed crit added; Consume the Fallen, Paradox Collapse, Cycle Rewind descriptions corrected to match implementations
- `Scripts/Systems/CombatEngine.cs` — Cyclebreaker: Probability Manipulation passive, Cycle Memory XP bonus (all 3 victory paths including multi-monster fix), Borrowed Power level scaling, Chrono Surge cooldown reduction, Fracture Reality kill tracking. Abysswarden: Abyssal Siphon passive lifesteal, Prison Warden's Resilience damage reduction, Corruption Harvest heal-on-kill. Voidreaver: Apotheosis lifesteal fix (`StatusLifestealPercent = 20`), Void Shroud reflection fix (25% with class-specific message), Reap cooldown reset on kill, Death's Embrace revive-on-death (both damage paths), Offer Flesh desperate bonus duration fix, Void Hunger/Soul Eater kill passives (both HandleVictory paths), Pain Threshold ability damage bonus (both paths). Shared: `ignore_defense`, `unmaking`, `probability_shift`, and `ignore_half_defense` handlers in `HandleSpecialSpellEffectOnMonster`; StatusEffect.Lifesteal consumption in `ApplyPostHitEnchantments`; Reflecting status class-specific percentages; combat-transient flag resets (`DeathsEmbraceActive`, `StatusLifestealPercent`) at combat initialization (PvM and PvP). Taunt `TauntRoundsLeft--` moved after `SelectMonsterTarget()` to fix off-by-one. `ApplyAoEDamage()` no longer splits damage — deals full damage to each target. `aoe_holy` inline split removed
- `Scripts/Systems/ClassAbilitySystem.cs` — Updated descriptions for Borrowed Power, Quantum State, Chrono Surge; Corrupting Touch base damage 15→40; Hungering Strike base damage 60→90; AoE base damage reduction across 14 abilities (all `aoe`, `aoe_holy`, `aoe_confusion`, `aoe_corrode` abilities) to compensate for full-damage-per-target change
- `Scripts/Systems/CompanionSystem.cs` — Hybrid role HP scaling +1→+4/level, defense +1→+2/level, added +2 CON/level in both `ApplyCompanionLevelUpStats()` and `ScaleCompanionStatsToLevel()`; Hybrid aggro class mapping changed from Paladin to Ranger
- `Scripts/Systems/LocalizationSystem.cs` — Added Hungarian (`hu`) to `KnownLanguageNames` dictionary as "Magyar (AI Translated)"
- `Scripts/Locations/BaseLocation.cs` — Cyclebreaker, Abysswarden, and Voidreaver added to `hasAnyBuff` check in `/health`; passive display for all three classes (Probability Manipulation, Cycle Memory, Abyssal Siphon, Prison Warden's Resilience, Corruption Harvest, Void Hunger, Pain Threshold, Soul Eater)
- `Localization/hu.json` — **NEW** — Hungarian translation (community-contributed, AI-assisted)
