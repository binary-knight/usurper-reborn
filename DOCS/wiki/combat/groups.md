---
title: Groups, followers and support
path: /wiki/en/combat/groups/
checked: 1.3.2
sources: Scripts/Systems/CombatEngine.cs, Scripts/Systems/TeammatePolicy.cs, Scripts/Systems/MonsterTactics.cs, Scripts/Core/Character.cs, Scripts/Core/GameConfig.cs, Scripts/Systems/SpellSystem.cs, Scripts/Systems/PotionBonus.cs, DOCS/release-notes/RELEASE_NOTES_1.2.0.md
history: 1.3.2 | Every party member regains stamina and mana at round end, a Cleric teammate cures poison and disease with Purify, and a living Paladin on your side makes monsters roll against 2 more armor class.
history: 1.3.0 | Teammates and companions roll to hit as you do, a healing potion given to an ally heals at least 20 percent of the ally's maximum HP, and a hired NPC or a friend in your party carries the oath-mark of your banner.
history: 1.2.7 | none
history: 1.2.6 | none
history: 1.2.5 | Group combat lines name monsters in each player's own language and wrap inside 79 columns.
history: 1.2.4 | A follower's turn screen, menus, round status, group lines and NPC ally victory lines are shown in each player's own language.
---
Groups can combine damage, healing and protection. A teammate has its own resources and condition; your healthy leader does not make everyone healthy.

## Support roles

Heals restore HP. Party wards absorb later damage and are not the same as immediately filling health. Sage ward changes in 1.2.0 make support relevant to party survival; read the spell description before deciding whether a heal or shield is needed.

## Hits and potions

A teammate or companion makes the same [hit roll](/wiki/en/combat/#the-hit-roll) as you for each physical swing at a monster, with its own stats. A healing potion you give to an ally heals at least 20 percent of the ally's maximum HP.

## Who monsters attack

With allies alive, each monster picks its target at random, weighted: the chance to be picked is your weight divided by the sum of every party member's weight. A Paladin weighs 180 and a Magician 60, so with only those two the Paladin is picked 3 times in 4. A taunt comes first: a taunted monster attacks the taunter while the taunt lasts, every time for a hard taunt such as Taunt (T) and some of the time for a soft one. Alone, you are always the target.

The weight starts from the class:

{{table:target-weights}}

Then it changes:

- Armor power above 50 adds 30, above 30 adds 15, below 15 takes 20 off.
- Maximum HP above 500 adds 20, below 100 takes 20 off.
- [Defend](/wiki/en/combat/#defend-d) adds 40 for you, a grouped player, or an ally on the Aggressive stance; the table's last column shows it at the class weight.
- An ally's weight is then multiplied by its [stance](/wiki/en/combat/party/#ally-stances): 70 percent on Cautious.
- The weight is never below 10.
- Beast, Insectoid, Shadow and Undead monsters add {{balance:PredatorBonus}} to anyone at {{balance:PredatorHpPercent}} percent HP or less. Goblinoid, Fey and Demonic monsters add {{balance:SkirmisherBonus}} to a Magician, Sage, Cleric, Alchemist, Wavecaller or Abysswarden. Neither applies in an Old God fight.

## Monster hits on allies

A monster's normal attack on a teammate or companion makes no hit roll and cannot be dodged: it lands unless the monster is distracted (25 percent miss for an ordinary distraction, at most 60) or the ally has Blur (20 percent miss).

- **Ally defence:** the ally's Defence + a random 0 to Defence / 8 + a random 0 to 5 x the square root of its armor power, plus a ward, any temporary defence and 2 while a living Paladin is in the party. Armor power 400 absorbs 0 to 100. A Barbarian or Mystic Shaman ally then takes 2 less, never below the minimum.
- **Several hits in one round:** the second hit on the same ally that round does 25 percent less and every later one 50 percent less.
- **Minimum and caps:** a hit does at least the larger of 1, 5 percent of the attack and 0.25 percent of the ally's maximum HP, and at most 75 percent of its maximum HP (85 percent from a boss, {{percent:BossFirstRoundsDamageCapPercent}} percent in a boss fight's first {{balance:BossFirstRoundsDamageCapRounds}} rounds). An ally that defends takes half.
- **Bosses:** a boss's hit on an ally does at least 1.5 x the boss's level before the cut for several hits in a round. In an Old God fight no single hit on an ally takes more than {{percent:OldGodTeammateDamageCapPercent}} percent of its maximum HP.

## Teammates regenerate stamina and mana

At the end of every round each living party member regains combat stamina and, for a class with mana, mana, by the same rule as you on its own stats: see [regeneration in combat](/wiki/en/combat/#regeneration-in-combat). It happens also after you have fallen. HP regeneration from gear, race and alignment is yours alone.

## Curing poison and disease

A Cleric in your party who knows Purify casts it on a living party member who is poisoned or diseased once nobody needs healing, you first, then whoever has the smallest share of HP left. Purify removes the Poisoned and Diseased statuses and trap poison. It costs normal mana, one cast a turn, and the Inn's spell switch for Purify turns it off. A party healer never picks Purify as a heal.

## The oath-mark

An NPC hired for your team, and an NPC friend who joins your dungeon party, carries the oath-mark of your banner; story companions do not. The game says so when you hire, in your team view and when someone joins the party. The oath-mark does not change the chance that a fall at your side is permanent: see [when an ally falls](/wiki/en/combat/party/#when-an-ally-falls). When one of them falls in your fight and will recover, the game says the oath-mark held. When one is lost for good, a short scene plays. If the one who fell and will recover is your spouse, lover or affair partner, the marriage or romance is not ended.

## Mental in a group

Grouped followers pay their own dungeon strain and end-of-fight losses using their own race, class and HP. Camp and Safe Haven recovery is shared with grouped followers. Fear and hallucinations still follow the affected player's Mental.

A follower who collapses may leave the group for rescue or die in deeper floors. Inspect group condition before another fight.

## Devotion

Living grouped players earn their own god's qualifying victory deeds, within their own daily cap. A Miracle is also personal to the player who can call it.

Read [party and allies](/wiki/en/combat/party/) for party size, fees, experience and stances, [teams and guilds](/wiki/en/online/teams/) for the social side and [companions](/wiki/en/combat/companions/) for story followers.
