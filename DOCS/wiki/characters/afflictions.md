---
title: Afflictions, addiction and treatment
path: /wiki/en/characters/afflictions/
checked: 1.3.5
sources: Scripts/Locations/HealerLocation.cs, Scripts/Systems/MentalSystem.cs, Scripts/Core/StatusEffect.cs, Scripts/Core/GameConfig.cs, Scripts/Core/Character.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/MonsterAbilities.cs, Scripts/Systems/EliteAffixes.cs, Scripts/Systems/MonsterTactics.cs, Scripts/Systems/TrainingSystem.cs, Scripts/Locations/BaseLocation.cs, Scripts/Locations/DungeonLocation.cs
history: 1.3.5 | A slow on a monster counts down once a round even when the monster acts more than once, and a boss or mini boss now resists the Frostbite and Frostbrand slows as it resists other slows.
history: 1.3.4 | Probability Shift now keeps the monster from scoring critical hits and makes 30 percent of its normal attacks that would hit you or an ally miss, in place of confusing and slowing it.
history: 1.3.3 | Poison, bleeding or another status that takes a companion or NPC ally to 0 HP leaves it Downed, not dead.
history: 1.3.2 | A Cleric's Purify cures a poisoned or diseased party member, and an Assassin's landed melee hit has a 5 percent chance to poison a monster for 3 rounds.
history: 1.3.0 | An elite monster with the Venomous trait can poison with its blows, and the lines that say you cannot act name the status in your language.
history: 1.2.5 | The Healer's treatment labels in the graphical client and the cursed item names at Remove Curse are shown in the player's language.
---
A combat status, an addiction and a persistent affliction are not interchangeable. Read Status and the treatment menu to identify what is wrong.

## Broken

Broken follows Mental collapse. It remains even if ordinary recovery raises your Mental again, weakens combat and experience gains, and prevents deeper dungeon travel. Talk Therapy clears it. Rehab also clears it for an addicted character.

## Addiction

Addiction lowers your ordinary Mental cap and can cause withdrawal. A drug may make Mental look healthier temporarily, but its crash can leave you worse off. Healer Addiction Rehab addresses addiction rather than merely healing HP.

### Withdrawal and overdose

A day of withdrawal costs {{balance:MentalWithdrawalLossPerSeverity}} Mental for each point of severity, and severity is one point for every 25 of your addiction value. An overdose costs {{balance:MentalOverdoseLoss}} Mental. The [Mental guide](/wiki/en/characters/mental/#drug-high-and-crash) has the cap, the high and the crash.

## Combat statuses

A status lasts the number of rounds the spell, ability or monster that put it on gave it, and counts down once a round. The table shows what the game's own rules do with each status; the damage columns are for the character who has it.

{{table:statuses}}

### Poison, bleeding and burning damage per round

Poison does 2 to 5 damage a round plus 1 per 10 levels of the character who has it. Bleeding does 1 to 6 plus 1 per 5 levels, burning 2 to 8 plus 1 per 4 levels, frost 1 to 3 plus 1 per 8 levels, a curse 1 to 2 plus 1 per 10 levels, and disease 1 plus 1 per 15 levels. Your statuses tick at the start of each round, before you act; a teammate's at the start of its own turn. Regenerating heals 1 to 6 HP a round.

### How long a status lasts and whether it stacks

Statuses do not stack. Getting the same status again replaces the rounds left with the new duration, even when that is shorter. A hold of N rounds costs you N - 1 turns, because a round ticks off before you act. The end of a fight removes holds, Slow, Blur, Haste, Blessed, Protected and Reflecting; other statuses, such as poison or bleeding, keep their rounds left for your next fight.

### What each status does to you

- **Stunned or Frozen:** you lose your turn. A monster's Sleep, Paralyze, Charm and web attacks all stun you.
- **Silenced:** no spells.
- **Blinded:** monsters more than 5 levels above you get 6 more armor class against your hit roll; against others it changes nothing.
- **Weakened:** your basic swing and Power Attack lose 4 + level / 10 power.
- **Cursed:** damage each round only.
- **Feared:** no effect on what you can do.
- **Blessed:** + level / 5 + 2 attack power, +2 defence, and 2 less armor class for your hit roll against a monster more than 5 levels above you.
- **Vulnerable, Exhausted, Berserk, Empowered, Shielded and Protected** change nothing in a monster fight.

A dungeon monster never puts Paralyzed, Sleeping, Confused, Slow or Diseased on you; those come from world bosses or other players, and Charmed only from other players.

Rage, Power Attack's stance, Hidden, Defend and Blur are explained under [tactical actions](/wiki/en/combat/#tactical-actions) and [dodge, Blur and block](/wiki/en/combat/#dodge-blur-and-block).

### How long does a stun last

A monster's stun or other hold on you lasts at most {{balance:MaxStunDurationNormal}} rounds, and one put on you during the monsters' turn lasts at least 2, so you lose at least your next turn. While a hold lasts no new one lands, and for {{balance:StunImmunityRoundsAfterRecovery}} rounds after it ends you cannot be held. A second hold soon after is cut to half length and a third to a quarter (at least the 2 rounds above), and a fourth is refused until {{balance:StunDRWindowRounds}} rounds pass without one.

### Resisting a status

A monster's ability puts its status on you only if each of these fails, in order: status immunity (always resists), a Cyclebreaker's {{percent:CyclebreakerDebuffResistChance}} percent, a Paladin's {{percent:PaladinDivineResolveStatusResist}} percent, Calm Waters' {{percent:CalmWatersResistChance}} percent; then the ability's own chance is rolled. Old God abilities check only immunity and Calm Waters. Outside Old God fights a monster never uses a status ability on a target that already has that status.

### Trap poison

Poison from a trap or an event is a separate counter, not the Poisoned status. In a fight it does 2 to 5 damage a round + level / 10 + a fifth of the poison's strength, at most a tenth of your maximum HP (at least 3). An antidote (outside a fight), Purify and a sanctuary cure it.

### Venomous elites

From dungeon floor {{balance:EliteMinFloor}} an [elite monster](/wiki/en/monsters/#elites) with the Venomous trait poisons you or an ally with {{balance:VenomChancePercent}} percent of its normal blows that did damage, for {{balance:VenomRounds}} rounds, unless the target is already poisoned. The usual resistances apply.

## Statuses on monsters

- **Burning:** a burning monster loses 4 to 6 percent of its maximum HP a round (at least 4), plus 1 to (your level / 5 + 1).
- **Poisoned:** 3 to 5 percent (at least 3), plus the same. Both tick at the monster's first action each round.
- **Charmed:** the monster skips its attack {{balance:CharmSkipAttackChance}} percent of the time.
- **Confused:** the monster loses its action 50 percent of the time; otherwise it hits itself for a third of its Strength 25 percent of the time.
- **Slowed:** the monster loses its action 50 percent of the time.
- **Feared:** the monster loses every action while the fear lasts.
- **Weakened:** the monster's attack is 0.70 times.
- **Probability Shift:** a Cyclebreaker spell. For 3 rounds the monster never scores a critical hit, and 30 percent of its normal attacks that would hit you or an ally miss. Its special abilities are not affected.

### Stuns and holds on monsters

A stun, freeze, sleep or bind on a monster lasts at most {{balance:MaxStunDurationNormal}} rounds. While one lasts no other lands, and for {{balance:StunImmunityRoundsAfterRecovery}} rounds after it ends, or after damage wakes a sleeping monster, it cannot be held. The second hold in a fight lasts half as long, the third a quarter, and after three holds a monster cannot be frozen, put to sleep or bound again that fight; stuns recover one step for every {{balance:StunDRWindowRounds}} rounds the monster acts unstunned.

### Can bosses be stunned

A boss or champion resists a stun, freeze, sleep or bind {{percent:BossStunResistChance}} percent of the time and is held {{balance:MaxStunDurationBoss}} round at most.

Fear on a boss, an Old God or a champion is a hold too: from the Fear spell, Soul Rend, Intimidating Roar and Dissonant Whispers it is resisted {{percent:BossStunResistChance}} percent of the time, lasts {{balance:MaxStunDurationBoss}} round at most, counts in the same hold budget and is followed by the same immunity, and no other hold lands while it lasts. On other monsters fear lasts its full rounds, outside the hold budget.

A boss or champion resists soft control {{balance:BossSoftControlResistPercent}} percent of the time, and soft control that lands lasts half as long. This covers a Sage's control spells, the slow of Frost Touch and Ice Storm, and the confusion of Mind Spike, Psychic Scream, Primal Scream, Carnival of Chaos, Causality Loop, Deadly Joke and Last Laugh (the last two roll their own chance first). A confusion holds a boss 2 rounds at most.

### Weapon poison

A hit with a poison weapon has a 20 percent chance to poison its target for 4 rounds. Each round, at the target's first action, it loses the larger of 5 percent of that hit and a quarter of the weapon's poison value (raised by your team's Armory, as your hits are), at least 1. A new poisoning before the 4 rounds end starts them again and keeps the larger of the two amounts; it never stacks. Physical immunity and divine armor cut each tick as they cut a hit. Weapon poison is separate from Poisoned: a target can have both, and both tick. In a player fight and on the world boss weapon poison is one hit instead: 10 percent of the hit, or the weapon's poison value raised by the Armory.

Before another expedition, treat the condition itself and restock supplies. See [Mental](/wiki/en/characters/mental/) and [combat](/wiki/en/combat/).
