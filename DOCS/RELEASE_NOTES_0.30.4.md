# Usurper Reborn - v0.30.4 Class Ability Effects Overhaul

Every non-spellcaster class ability now has a unique combat effect. Previously, ~25 of 30+ ability special effects silently did nothing — players spent stamina on high-level abilities and got only base damage. Additionally, spell-applied monster status effects (Sleep, Fear, Stun, Slow) were set but never actually checked, so those spells had no real effect in combat.

---

## Bug Fixes

- **Monster status effects never worked**: Spells that set `IsSleeping`, `IsFeared`, `IsStunned`, and `IsSlowed` on monsters (via `HandleSpecialSpellEffectOnMonster`) had no effect because `ProcessMonsterAction` never checked these properties. Monsters acted normally despite being "asleep" or "feared." Added full status checks: sleeping/feared/stunned monsters skip their turn with appropriate messages and duration tracking. Slowed monsters have a 50% chance to skip. Sleep breaks on damage.

- **~25 ability special effects did nothing**: The `ApplyAbilityEffects` switch statements only handled 8 of 30+ effect types. High-stamina abilities like Assassinate, Frenzy, Frost Bomb, Divine Shield, Bloodlust, and many more had no special mechanic — just base damage. All missing effects are now implemented in both combat paths (multi-monster and legacy single-monster).

---

## New Ability Effects

### Warrior
- **Reckless Attack**: Trade -20 defense for bonus damage (risk/reward)
- **Desperate Strike**: +50% bonus damage when below 40% HP
- **Champion's Strike**: Self-heal on hit (Level x 3 HP)
- **Maelketh's Fury**: Enhanced rage state with status display

### Barbarian
- **Frenzy**: 2-4 extra hits at 1/3 base damage each
- **Bloodlust**: Heal on every kill (Level x 2 + 20 HP) for the rest of combat
- **Intimidating Roar**: 70% chance to fear target for 3 rounds (35% vs bosses)
- **Unstoppable**: Full status immunity + clears all negative effects
- **Avatar of Destruction**: Rage + status immunity combined

### Paladin
- **Divine Smite**: Double damage vs Undead and Demons
- **Holy Avenger**: +75% damage vs Undead/Demons + self-heal
- **Judgment Day**: Holy AoE with 1.5x bonus vs Undead/Demons per target
- **Divine Shield**: 2 rounds of complete invulnerability (blocks ALL damage)
- **Cleansing Light**: Removes all negative status effects
- **Avatar of Light**: 1 round invulnerable + status immunity

### Assassin
- **Backstab**: Flavor text (critical damage already applied by formula)
- **Death Mark**: Target takes 30% bonus damage for 4 rounds from all sources
- **Assassinate**: 50% instant kill if target below 25% HP (never works on bosses)
- **Shadow Step**: Dodge next attack + 50 temp defense for 2 rounds
- **Vanish**: Dodge next attack + Hidden status
- **Noctura's Embrace**: Hidden for 2 rounds
- **Death Blossom**: AoE + double damage to targets below 30% HP

### Ranger
- **Precise Shot**: Flavor text (guaranteed hit already applied)
- **Hunter's Mark**: Target takes 30% bonus damage for 4 rounds
- **Camouflage**: Dodge next attack
- **Legendary Shot**: Stun target for 1 round

### Bard & Jester
- **Inspiring Tune**: +15 attack to teammates for 3 rounds (+10 self if solo)
- **Deadly Joke**: 65% chance to confuse target for 2 rounds (confused monsters may skip turns or hit themselves)
- **Legend Incarnate**: Regenerating for 5 rounds

### Alchemist
- **Throw Bomb (Fire)**: Bonus damage vs Plant/Beast monsters + 2-round burn DoT
- **Frost Bomb**: 75% chance to freeze target for 2 rounds (40% vs bosses) — frozen monsters cannot act
- **Transmutation**: Clear all negative effects + Regenerating for 5 rounds

### Universal
- **Iron Will**: Status immunity for ability duration
- **Execute**: Double damage to targets below 30% HP (AoE version)

---

## New Monster Status Effects

- **Frozen**: Monster cannot act. Ticks down each round.
- **Confused**: 50% chance to skip turn, 25% chance to hit self for 1/3 of its own Strength. Ticks down each round.
- **Marked**: Monster takes 30% bonus damage from all sources. Ticks down each round.

---

## Combat Integration

- **Invulnerable blocks all damage**: Divine Shield and Avatar of Light now truly block all incoming damage — normal attacks, monster special abilities, life drain, and multiplied attacks are all stopped.
- **Status Immunity blocks debuffs**: Monster special abilities that inflict status effects (poison, stun, etc.) are blocked when the player has status immunity active. Shows "Your iron will resists [effect]!" message.
- **Sleep breaks on damage**: Sleeping monsters wake up when hit, providing tactical counterplay.
- **Bloodlust heal-on-kill**: Works through all kill paths — single target damage, AoE damage, and ability damage.
- **End-of-round tickdown**: Status immunity duration properly ticks down each round with expiry message.
- **Combat reset**: All transient combat flags (Bloodlust, StatusImmunity) properly reset at the start of each new combat encounter.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version bump to 0.30.4 |
| `Scripts/Core/Monster.cs` | Added `IsMarked`/`MarkedDuration`, `IsFrozen`/`FrozenDuration`, `IsConfused`/`ConfusedDuration` properties |
| `Scripts/Core/Character.cs` | Added `HasBloodlust`, `HasStatusImmunity`/`StatusImmunityDuration` properties; reset in `ClearAllStatuses()` |
| `Scripts/Systems/CombatEngine.cs` | Added ~28 switch cases to both `ApplyAbilityEffectsMultiMonster` and `ApplyAbilityEffects` (legacy); fixed `ProcessMonsterAction` to check IsSleeping/IsFeared/IsStunned/IsFrozen/IsConfused/IsSlowed/IsMarked; added marked damage bonus (+30%) in `ApplySingleMonsterDamage` and `ApplyAoEDamage`; added sleep-breaks-on-damage; added bloodlust heal-on-kill in both damage paths; added Invulnerable damage block in normal attacks and all 3 TryMonsterSpecialAbility damage paths; added status immunity check for monster-inflicted debuffs; added StatusImmunityDuration tickdown in `ProcessEndOfRoundAbilityEffects`; added HasBloodlust/HasStatusImmunity reset in `PlayerVsMonsters` combat init |
