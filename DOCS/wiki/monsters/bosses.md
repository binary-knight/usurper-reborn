---
title: Boss reference
path: /wiki/en/monsters/bosses/
checked: 1.3.7
sources: Scripts/Data/WorldBossData.cs, Scripts/Systems/SpellSystem.cs, Scripts/Data/OldGodsData.cs, Scripts/Data/SecretBosses.cs, Scripts/Locations/DungeonLocation.cs, Scripts/Systems/MonsterGenerator.cs, Scripts/Core/Monster.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/OldGodBossSystem.cs, Scripts/Systems/DungeonGenerator.cs, Scripts/Core/GameConfig.cs, Scripts/Systems/MiracleSystem.cs
history: 1.3.7 | Against Manwe the Creator's Ward stops 55 percent of what gear enchantments add after a hit, Godbane held or not, and the warning before the fight says so.
history: 1.3.6 | An Old God's divine armor and phase immunity now cut weapon poison, Poisoned, Corrupting Touch and Searing Totem ticks as a hit, fear and confusion on a boss follow the hold and soft control rules, and gear's magic resistance does not reduce any Old God attack.
history: 1.3.5 | Only an Old God's own counter enchant, Sunbane, Stonebane or Godbane, removes its divine armor and it also holds back two of the god's abilities, Maelketh has less health with two to four fighters, and Manwe has 40 percent less HP.
history: 1.3.4 | Doom starts at Aurelion, Shadow Incarnate's message says Manwe heals, and Manwe's named abilities follow the difficulty, Shield Wall Formation, a failed flee, Divine Mandate and Death's Embrace.
history: 1.3.3 | From floor 25 floor bosses have more HP and hit harder and bring more escorts as the floor's groups grow, and every boss can finish a Downed ally.
history: 1.3.2 | Dispel Evil on an enraged boss takes back its enrage, floors keep their secret boss rooms, and Boss Slayer gear adds its damage against bosses and mini bosses.
history: 1.2.5 | Boss names, titles, world boss elements and Old God ability names are shown in the player's language, and Old God names on the wiki come in all five languages.
history: 1.2.4 | In group combat, a secret boss's battle cry is shown in each player's own language.
---
Every dungeon floor ends in a boss room. Bosses hit harder and last longer than the monsters around them, and some change their abilities in phases during the fight. The base definitions below are not a prediction of a live scaled encounter.

## Floor bosses

A floor boss is a monster of the floor's level from a family tier that covers that level, with 2.8 times the HP, 1.25 times the Strength, punch and weapon power and 1.2 times the Defense and armor of an ordinary monster of the same tier. On floor 20 with a tier power of 1.0 an ordinary monster has 3,478 HP and a floor boss 9,738. From floor 25 the [deep factors](/wiki/en/monsters/#how-monster-hp-strength-and-defense-are-worked-out) multiply on top of these, so the deep floors' bosses, like their other monsters, are built for a full team of five. A tier can have a separate boss power: the Orc keeps its full power as a boss. Its attack power counts 1.3 times, as for every boss, and the boss limits on one hit are on [combat](/wiki/en/combat/#most-damage-from-one-hit).

The boss's name comes from the floor's theme and floor number, so a floor always has the same boss name; the [dungeon floors table](/wiki/en/world/dungeon/#dungeon-floors-table) lists them. It uses the abilities of the tier it was drawn from. The boss room never ambushes you. Like every boss, a floor boss spends its action to announce and then finish a [Downed ally](/wiki/en/combat/party/#downed-allies).

### Floor boss escorts

The boss room draws a group as any room does, without elites, stragglers or the floor 4 to 10 rules, and the boss takes the first place in it. The boss therefore fights alone or with the rest of that group: no escort or one on floors 1 to 10 and up to 2 escorts on floors 11 to 24; from floor 25 the number of escorts is one fewer than the floor's group size above, on average 1 on floor 25, 2.5 on floor 50 and 3 from floor 61, at most 4. When the draw was a champion, the boss fights alone.

### Floor boss rewards

Beating the boss room pays a bonus on top of the kills: floor x 500 gold plus 0 to 999 more, and floor x 300 experience. On floor 20 that is 10,000 to 10,999 gold and 6,000 experience. In a group of players the gold is split evenly and each player's experience share follows the group rules. A boss of level 25 or more also gives 10 Fame, a lower one 5. On floors 20 and deeper a won boss room always drops {{balance:MaterialDropCountBossMin}} or {{balance:MaterialDropCountBossMax}} of one [crafting material](/wiki/en/items/materials/).

### Guardians that block the way down

Some floors have a guardian that must be dealt with before you can go deeper; see [how deep you can go](/wiki/en/world/dungeon/#how-deep-you-can-go), whose spoiler names those floors. How those fights scale is in a spoiler below.

### Old God fights: enrage, channels and marks

Old Gods enrage after a set number of rounds, channel attacks that hit the whole party and mark party members with lasting harm, so a healer teammate and a fast teammate who can interrupt matter. The numbers for each are in the spoilers below. [Magic resistance](/wiki/en/items/enchanting/#magic-resistance) from gear does not reduce any Old God attack.

## World bosses

World bosses belong to the shared online system: when they spawn, how the fight works and what a kill pays are on [the world boss](/wiki/en/online/world-boss/). Daily Miracles do not work in world boss fights.

{{table:bosses}}

## Story and secret encounters

The disclosures contain names, base stats and phase definitions that can spoil discoveries. Their contents do not appear in search results.

:::spoiler Spoiler: secret bosses
Four secret bosses wait in a secret vault on floors 25, 50, 75 and 99. A secret boss fights at the higher of its base level and your level + 5. Its HP is its listed base HP + 100 per level, its Strength and its punch are each its listed attack + 2 per level, and its Defense is its listed defense + 1 per level. Its other listed values (magic power, speed, critical chance and abilities) are not used in the fight: it makes normal attacks.

The floor 50 boss asks you a question first. The right answer is the fourth; any other answer multiplies its attack by 1.5 for that fight only. The kill itself pays nothing; a win pays the boss's reward: 10,000 experience and 5,000 gold on floor 25, 25,000 and 12,000 on floor 50, 50,000 and 25,000 on floor 75, and 100,000 experience and no gold on floor 99. Grouped players who fought with you are paid the same. Only a win spends the encounter: after a flight or a loss you can try again.
:::

:::spoiler Spoiler: Old God fights
An Old God's HP, Strength and Defense in the table below are multiplied by {{balance:BaseMonsterDifficultyScale}} in the fight; its Strength is then split evenly into Strength and weapon power, and its Defense into Defense and armor. Each artifact you hold adds 10 percent to its HP (at most 40 percent) and 5 percent to its damage (at most 20 percent).

Aurelion, Terravok and Manwe wear divine armor: every hit you deal them, spells and abilities included, does 25, 35 and 50 percent less damage. Burn ticks, reflected damage and the added damage of a Mystic Shaman's weapon enchant are not cut by the armor; a weapon poison tick, a Poisoned tick, a Corrupting Touch tick and a Searing Totem pulse are cut, as a hit is. Only that god's own counter enchant on your main hand or off hand weapon (Sunbane for Aurelion, Stonebane for Terravok, Godbane for Manwe) removes it; any other enchant, or a weapon's name, does not change it. On Manwe the Creator's Ward also stops 55 percent of what your gear's enchantments add after a hit, Godbane held or not: a weapon's fire, frost, lightning, poison, holy or shadow proc has 45 percent of its chance, Lifedrinker, Siphon and equipment Thorns give 45 percent of their amount, and Boss Slayer adds 4.5 percent instead of 10. His summons and every other foe take these in full. The counter also holds back two of the god's abilities, Aurelion's Light's Embrace and Sacrifice, Terravok's Stone Skin and Entomb, and Manwe's Word of Creation and Time Stop: on a turn it draws one, the god strikes instead.

**Phases.** An Old God enters phase 2 at half its HP and phase 3 at a fifth (Manwe at a tenth). Each phase adds its abilities to those it draws from; it uses one on 70 percent of its turns. Potions in an Old God fight have a cooldown of 1 round.

**Enrage.** At its enrage round, with warnings at half and three quarters of it, the god's Strength doubles ({{balance:BossEnrageDamageMultiplier}} times), its Defense rises {{balance:BossEnrageDefenseMultiplier}} times and it attacks {{balance:BossEnrageExtraAttacks}} more times a round for the rest of the fight. A Dispel Evil takes the enrage back: its Strength and Defense fall back by the factor the enrage raised them, its extra attacks end, and it does not enrage again that fight.

**Area attacks and channels.** From round 2, on every round that is a multiple of its number, the god strikes everyone in the party for its area damage less 3 x the square root of each target's Defense. A taunting teammate, or a Warrior, Paladin, Barbarian or Tidesworn you play, takes the hit for the others: the others take 40 percent and the tank 130 percent. A channel starts the same way and lands two rounds later on everyone, for its damage less 3 x the square root of Defense. A teammate with Agility plus half Dexterity of 80 or more may interrupt it; a Dispel Evil ends it too, and also ends a phase immunity and cuts the god's Defense by 20 percent.

**Corruption and Doom.** From phase 2 a god with corruption has a 30 percent chance each round to put a stack on a random party member, 2 stacks in phase 3, up to {{balance:BossCorruptionMaxStacks}}; each stack deals its damage every round. Doom starts at Aurelion: in phase 3 Aurelion, Terravok and Manwe have a 15 percent chance each round to mark a member without Doom, who dies outright when the count reaches 0 (3 rounds for Aurelion and Terravok, 2 for Manwe). Maelketh, Veloura, Thorgrim and Noctura, her betrayal fight included, do not use Doom. Only a healer teammate (Cleric, Paladin, Bard, Wavecaller, Abysswarden or a healer specialization) clears them: first the Doom closest to landing, otherwise 3 + its level / 20 stacks from a member holding 3 or more. You cannot clear them yourself.

**Immunity.** At phase 2 Noctura, Terravok and Manwe become immune to physical damage for 4 rounds and Aurelion to magical damage; Manwe adds magical immunity for 4 rounds at phase 3. Damage of the blocked kind is cut to {{percent:BossPhaseImmunityResidual}} percent; a weapon poison tick and a Poisoned tick count as physical, a Corrupting Touch tick and a Searing Totem pulse as magical.

**Fighting alone.** With no living NPC teammate or companion, an Old God's named abilities deal {{percent:OldGodSoloDamageBonus}} percent more damage and you then take {{percent:OldGodSoloPlayerDamageReduction}} percent less, 0.92 times in all. Manwe's own abilities skip this.

**Maelketh and a small party.** With fewer than five fighters his health is lower: three quarters with four, a half with three and a quarter with two. The fighters are you and every living teammate, companion or ally, counted once as the fight starts, so a fall during the fight does not change it; an ally told to wait is not a fighter. Alone or with five or more his health is as above. No other Old God changes with the party's size.

**Most damage from one hit.** An Old God's area attack and channel have no cap on the damage to you; a teammate takes at most {{percent:OldGodTeammateDamageCapPercent}} percent of its maximum HP from one hit. Manwe's own abilities take at most 85 percent of your maximum HP from the first round, without the lower cap of the first rounds.

**Named abilities.** An Old God's ability works from its attack power: a heavy one hits for 2 or 3 times it and a medium one for 1.5 times, less your Defense; a self heal restores 5 or 10 percent of the god's maximum HP; a war cry adds 30 percent attack for 3 rounds. Maelketh calls 2 Spectral Soldiers at phase 2, then 1 or 2 more every 3 rounds while fewer than 6 foes stand; a soldier has 50 + 5 x his level HP, 190 at level 28. Manwe's Word of Creation heals him 8 percent, his Shadow Incarnate heals him 30 percent of the damage it deals, his Split Form adds a Shadow of Manwe of 25,000 HP once, and his Creation's End kills you outright below a fifth of your HP; with the Worldstone it deals heavy damage instead, which can still kill you.
:::

{{table:old-god-mechanics}}

{{table:secret-bosses}}
