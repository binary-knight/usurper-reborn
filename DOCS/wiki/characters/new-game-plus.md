---
title: New Game Plus (NG+) and cycles
path: /wiki/en/characters/new-game-plus/
checked: 1.3.6
sources: Scripts/Systems/EndingsSystem.cs, Scripts/Systems/OpeningSequence.cs, Scripts/Core/GameEngine.cs, Scripts/Systems/StoryProgressionSystem.cs, Scripts/Systems/MetaProgressionSystem.cs, Scripts/Systems/CharacterCreationSystem.cs, Scripts/Core/GameConfig.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/ArtifactSystem.cs, Scripts/Core/Items.cs
history: 1.3.6 | none
history: 1.3.5 | none
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | Prestige class spells start at Average proficiency, and a saved prestige class spell below Average is raised to Average when the character loads.
history: 1.3.1 | none
history: 1.3.0 | A new character, on NG+ too, starts with no record of betrayals.
history: 1.2.7 | none
history: 1.2.6 | The new character's name cannot contain < > & or a double quote.
history: 1.2.5 | The ending, ascension and Eternal Cycle screens, including the prestige class list and descriptions, are shown in all five languages.
history: 1.2.4 | The alt character slot earned by ascending is kept when a new life starts.
history: 1.2.3 | none
---
New Game Plus, shown in the game as the Eternal Cycle, starts a new life after you finish the main story. Each finished life adds one to your cycle count.

## How NG+ starts

NG+ is offered only after the main story's final encounter ends with an ending. After the ending and credits, the game asks two questions: whether to ascend to godhood, and whether to begin the Eternal Cycle. You must choose one of them; there is no return to ordinary mortal play with the finished character.

The NG+ screen shows your current and next cycle and the prestige classes the ending unlocks. Ascending instead turns the character into a [player-god](/wiki/en/gods/player-gods/).

An alt character cannot ascend; the game then offers it the Eternal Cycle. Reaching the level cap of 100 is not required for either path.

## What carries over

NG+ creates a brand new character. The old save is replaced, and these do not carry over:

- level, experience, gold, equipment and items;
- your god and Favor (you choose a god again at the Temple);
- story progress, seals, artifacts, companions, romances and the record of betrayals.

In single-player the town, its NPCs and your family also start fresh. Online, the world is shared, so the town stays, and children of your previous life no longer count as yours.

A few lifetime records do survive: completed sellsword contracts, lifetime charity gold and completed family arcs, which give a starting Charisma bonus.

## Cycle bonuses

A new character on a later cycle starts with, for each completed cycle:

- {{balance:CycleStatBonusPerCycle}} more Strength, Defense and Stamina, for good;
- 500 more starting gold;
- 10 percent more experience from monster fights.

On cycle 3, with 2 cycles completed, that is 10 more of each stat, 1,000 gold and 1.2 times the experience. A previous life that finished at a high level also raises the starting level (below). The ending you reached adds its own bonus on top.

### How much harder is NG+

Monsters grow stronger on later cycles: their HP, Strength, Defense, punch, weapon power and armor are {{balance:NGPlusCycle2MonsterBuff}} times on cycle 2, 1.56 times on cycle 3 ({{balance:NGPlusCycle2MonsterBuff}} x {{balance:NGPlusCycle3MonsterBuff}}) and 2.34 times from cycle 4 (x {{balance:NGPlusCycle4MonsterBuff}} more). Their gold is {{balance:NGPlusCycle2GoldBuff}} times on cycles 2 and 3 and 3 times from cycle 4 (x {{balance:NGPlusCycle4GoldBuff}} more), so the cycle is not only an easier replay.

### NG+ starting level

A new life starts at level 5 if a life you finished had reached level 50, and at level 10 if one had reached level 100. Otherwise it starts at level 1. The higher start comes with the class growth and the training points of the levels it skips, and your experience is set to that level's total.

## Prestige classes

Five prestige classes can be chosen at character creation on NG+: Tidesworn, Wavecaller, Cyclebreaker, Abysswarden and Voidreaver. Which ones you may pick depends on the endings you have reached. Prestige classes ignore the ordinary class and armor weight limits on equipment. See the [class catalog](/wiki/en/characters/classes/) for their numbers.

:::spoiler Spoiler: which ending unlocks which prestige class
The Savior ending unlocks Tidesworn and Wavecaller. The Defiant ending unlocks Cyclebreaker. The Usurper ending unlocks Abysswarden and Voidreaver. The True Ending and the secret ending unlock all five.

Ending bonuses for the next cycle: Usurper adds 10 Strength and 100 Darkness; Savior adds 100 Chivalry and knowledge of artifact locations; Defiant adds 0.25 to the experience multiplier and an ancient key; the True Ending adds 15 Strength, Defense and Stamina, artifact knowledge, the key and 0.5 to the experience multiplier.

Lasting unlocks from a finished life: after the Usurper ending, a monster's attack is cut by 10 percent before your Defense is taken off it. After a life that ended with all 7 artifacts collected, the stats an artifact grants are 50 percent larger.

The secret ending closes the cycle instead of offering NG+ or ascension.
:::

For the story itself, read [The story, Old Gods and endings](/wiki/en/world/story/).
