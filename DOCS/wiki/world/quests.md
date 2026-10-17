---
title: Quests and discoveries
path: /wiki/en/world/quests/
checked: 1.3.4
sources: Scripts/Locations/QuestHallLocation.cs, Scripts/Systems/QuestSystem.cs, Scripts/Core/Quest.cs, Scripts/Core/GameConfig.cs, Scripts/Locations/CastleLocation.cs, Scripts/Systems/GodBoonSystem.cs
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | Crown monster bounties pay twice their tier's gold and experience, and the Bandits on the Trade Road contract can be finished.
history: 1.3.0 | One kill or one defeat advances each quest objective once, and the Quest Hall quest to slay a monster type's champion shows the champion's own name and is completed by killing that champion in the dungeon.
history: 1.2.5 | A player in any language can no longer take a second royal quest, and royal quests to clear a dungeon floor are now floor quests.
---
Quests have different origins: the Quest Hall's bounty board, the Crown's bounties, royal quests from an audience at the Castle, the [Sellsword Hall](/wiki/en/world/anchor-road/#sellsword-hall-contracts) on Anchor Road, conversations and exploration can each lead to a goal. Check the active quest text rather than interpreting every rumor as an accepted task.

## Track the actual objective

A target, prerequisite and reward belong to the specific quest. Finishing a similar activity does not necessarily satisfy another quest. One kill or one defeat advances each objective once. A Quest Hall quest to slay a monster type's champion shows the name the champion has in the fight, and killing that champion in the dungeon completes it. Return to the appropriate giver or use the quest menu when the game asks for it.

## How many quests a day

You can claim {{balance:MaxQuestsPerDay}} quests a day at the Quest Hall, and the hall shows at most {{balance:MaxAvailableQuestsShown}} open quests, only those whose level range holds your level.

## The Quest Hall bounty board

The board keeps 5 open dungeon quests and refreshes each day; an unclaimed one leaves after 3 days. A quest's difficulty is your level / 5, plus or minus 1, kept between 1 and 4. It is offered from your level - 5 to your level + 15 and gives you {{balance:DefaultQuestDays}} days plus its difficulty to finish. Its target is one of four kinds, aimed at floors between your level - 10 and your level + 10:

- **Defeat a boss:** kill a monster type's champion of about your level + 3 x difficulty.
- **Expedition:** reach a floor about your level + 2 x difficulty + 1 to 3 and defeat 3 + 2 x difficulty monsters.
- **Clear a floor:** reach and clear a floor about your level + difficulty, plus or minus a little.
- **Survive the depths:** descend 3 x difficulty + 2 to 4 floors (at most 15) and defeat 10 monsters.

## Quest reward by tier

A quest's reward tier follows its difficulty: low for difficulty 1, medium for 2, high for 3 and 4. Its kind is one of five at random:

- Experience: your level x 100, 500 or 1,000.
- Gold: your level x 150, 500 or 1,000.
- Healing potions: 50, 100 or 200, as many as fit under the potion limit.
- Chivalry or Darkness: 25, 75 or 110.

Every completed quest also adds 5 Fame, and 3 more for a Crown bounty or a quest against an NPC. A quest from a faction adds 50 standing with it.

## Crown bounties

The Crown keeps 3 to 5 bounties on the board; an unclaimed one leaves after 7 days, and each gives 14 days to finish. 70 percent of new bounties name a living NPC of level 5 or more (never the ruler or a story character); the rest ask you to slay monsters.

### Bounty on an NPC

A bounty on an NPC pays its level x 100 x its difficulty in gold, where the difficulty is its level / 15 + 1, at most 4, and the pay at most 25,500. Beating that NPC in a fight pays it whether or not you claimed the bounty. It also pays experience: the higher of your level x 50 and a fifth of the gold. A level 45 NPC (difficulty 4) pays 18,000 gold and, to a level 40 character, 3,600 experience. Judicar's boon can raise the gold. The [law guide](/wiki/en/world/law/#bounties) covers bounties on players and kill contracts.

### Crown monster bounties

A monster bounty asks for a number of kills by its kind (3 to 7) plus 2 x its difficulty (1 to 4), and you can take it from level 5 x its difficulty. It pays twice the gold or experience of its base tier (low for difficulty 1, medium for 2, high for 3 and 4); potions and Chivalry or Darkness are paid at the base tier.

## Royal quests

A royal quest comes from an audience at the [Castle](/wiki/en/world/castle/#royal-quests); you can hold one at a time. Its difficulty is 1 + your level / 15, at most 4, and it gives you 7 + 2 x difficulty days. The offer shows what it pays: when you accept, a quarter of (500 to 999) x your level x (difficulty + 1) gold and a quarter of (100 to 199) x your level x (difficulty + 1) experience; when you complete it, your level x 1,000 gold.

Its goal is one of: kill 5 + 3 x difficulty monsters; reach a floor at your level + 3 x difficulty; clear a floor at your level - 5 + 5 x difficulty; or hunt a criminal. Floors are kept between 1 and your level + 10.

## Keep story discoveries in context

Some paths depend on choices and resolved encounters. This wiki does not put late outcomes or ending walkthroughs into search snippets. Secret boss definitions are available through an explicit disclosure in the [boss reference](/wiki/en/monsters/bosses/).
