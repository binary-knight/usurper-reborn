---
title: New Game Plus (NG+) and cycles
path: /wiki/en/characters/new-game-plus/
checked: 1.3.0
sources: Scripts/Systems/EndingsSystem.cs, Scripts/Systems/OpeningSequence.cs, Scripts/Core/GameEngine.cs, Scripts/Systems/StoryProgressionSystem.cs, Scripts/Systems/MetaProgressionSystem.cs, Scripts/Systems/CharacterCreationSystem.cs, Scripts/Core/GameConfig.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/ArtifactSystem.cs
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

A new character on a later cycle starts with:

- extra Strength, Defence and Stamina for each completed cycle;
- extra starting gold for each completed cycle;
- an experience multiplier that grows with each cycle;
- a higher starting level if a previous life finished at a high level.

The ending you reached adds its own bonus on top. Monsters also grow stronger and drop more gold on later cycles, so the cycle is not only an easier replay.

### NG+ starting level

A new life starts at level 5 if a life you finished had reached level 50, and at level 10 if one had reached level 100. Otherwise it starts at level 1. The higher start comes with the class growth and the training points of the levels it skips, and your experience is set to that level's total.

## Prestige classes

Five prestige classes can be chosen at character creation on NG+: Tidesworn, Wavecaller, Cyclebreaker, Abysswarden and Voidreaver. Which ones you may pick depends on the endings you have reached. Prestige classes ignore the ordinary class and armor weight limits on equipment. See the [class catalog](/wiki/en/characters/classes/) for their numbers.

:::spoiler Spoiler: which ending unlocks which prestige class
The Savior ending unlocks Tidesworn and Wavecaller. The Defiant ending unlocks Cyclebreaker. The Usurper ending unlocks Abysswarden and Voidreaver. The True Ending and the secret ending unlock all five.

Ending bonuses for the next cycle: Usurper adds Strength and Darkness; Savior adds Chivalry and knowledge of artifact locations; Defiant adds experience and an ancient key; the True Ending adds all three base stats, artifact knowledge, the key and more experience.

Lasting unlocks from a finished life: after the Usurper ending, a monster's attack is cut by 10 percent before your Defence is taken off it. After a life that ended with all 7 artifacts collected, the stats an artifact grants are 50 percent larger.

The secret ending closes the cycle instead of offering NG+ or ascension.
:::

For the story itself, read [The story, Old Gods and endings](/wiki/en/world/story/).
