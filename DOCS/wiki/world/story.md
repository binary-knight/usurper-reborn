---
title: The story, Old Gods and endings
path: /wiki/en/world/story/
checked: 1.3.6
sources: Scripts/Systems/OpeningStorySystem.cs, Scripts/Systems/OpeningSequence.cs, Scripts/Systems/SevenSealsSystem.cs, Scripts/Systems/ArtifactSystem.cs, Scripts/Systems/OceanPhilosophySystem.cs, Scripts/Systems/AwakeningScreens.cs, Scripts/Systems/OldGodBossSystem.cs, Scripts/Data/OldGodsData.cs, Scripts/Systems/EndingsSystem.cs, Scripts/Locations/TempleLocation.cs, Scripts/Systems/AmnesiaSystem.cs, Scripts/Systems/StoryProgressionSystem.cs, Scripts/Systems/MoralParadoxSystem.cs, Scripts/Locations/DungeonLocation.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/OldGodEchoSystem.cs, Scripts/Systems/BetrayalSystem.cs, Scripts/Core/GameEngine.cs, Scripts/Core/GameConfig.cs
history: 1.3.6 | none
history: 1.3.5 | Only an Old God's own counter enchant removes its divine armor, Maelketh has less health with two to four fighters, and Manwe has 40 percent less HP.
history: 1.3.4 | Maelketh, Veloura, Thorgrim and Noctura, her betrayal fight included, no longer put Doom on the party.
history: 1.3.3 | none
history: 1.3.2 | An alliance with Noctura hands over the Shadow Crown, and the ending checks and the ending screen count the same saved and destroyed gods.
history: 1.3.0 | A betrayal scene now plays once before one late story fight, and the Temple's Faith room can settle a betrayal afterwards.
history: 1.2.6 | none
history: 1.2.5 | The Mysterious Stranger now meets characters from level 3 in town, and the answer you give carries into the Manwe fight; the story is shown in all five languages.
history: 1.2.4 | In group combat, an Old God's opening line is shown in each player's own language.
---
The main story runs alongside ordinary adventuring. You can play at your own pace; the story waits for you in the dungeon and the Temple. This page keeps names, locations and outcomes inside spoiler disclosures.

## How the story starts

Your character wakes with no memory and finds a letter written by a past self. It speaks of seals hidden below the town and warns you to trust no one. From level 3, a stranger may approach you as you enter a place in town, once per character, and the answer you give matters later. As you gain levels, townsfolk such as a Temple priest and a veteran at the Inn give further hints about where to look.

## Seals and artifacts

The story has two sets of objects to gather:

- **Seals.** One is in town and the rest are deeper in the dungeon. Examining the ancient stones in the Temple's Old Stones room is the place to begin.
- **Artifacts.** The Old Gods hold most of them, and a final artifact appears once you hold the others.

The Old Gods are ancient bosses deep in the dungeon. Each encounter needs a minimum level and the earlier encounters resolved. An encounter begins with a conversation, and your answers can matter as much as the fight. Some gods can be fought to the end, and some can be spared or allied. Old Gods enrage, channel and mark the party, so a healer and a fast teammate matter; the [boss reference](/wiki/en/monsters/bosses/) has the fight rules in a spoiler.

## The Awakening

The Awakening measures what your character comes to understand. It rises through stages from loss, mercy and wonder: companions, sparing a beaten foe, dreams, visions, discoveries, Wave Fragments and the songs of the Old Gods. Each of the seven stages brings a lasting boon: 3 Wisdom at stage 1, then more mana, experience, HP, damage and less damage taken, all listed under [Awakening boons](/wiki/en/characters/stats/#awakening-boons). Read your Ocean Journal from Main Street: Status district, then Progress. The last stage has its own condition, which the journal describes.

## Endings: how to get each ending

Your choices with the Old Gods, your alignment, the seals and your Awakening decide which ending you reach. The requirements of each ending, the True Ending's among them, are in the spoiler below. After an ending you decide between [ascending as an immortal](/wiki/en/gods/player-gods/) and starting [New Game Plus](/wiki/en/characters/new-game-plus/).

:::spoiler Spoiler: seal floors and the Old Gods
One seal is in the Temple. The others lie on dungeon floors 15, 30, 45, 60, 80 and 99.

In order: Maelketh on floor 25, Veloura on floor 40, Thorgrim on floor 55, Noctura on floor 70, Aurelion on floor 85, Terravok on floor 95, and Manwe, the Weary Creator, on floor 100. You can meet an Old God from 10 levels below its floor: Manwe from level 90. The earlier gods must be resolved first: Veloura and Thorgrim need one god resolved, Noctura two, Aurelion three, Terravok four, and Manwe all six. Killed, saved, allied and awakened all count as resolved.

A god killed or saved leaves 2 of its crafting material, and each of the first six gives its artifact; an alliance with Noctura gives hers too. Maelketh and Thorgrim cannot be saved. Veloura, Aurelion and Terravok can be spared in the conversation before the fight, which saves them at once and hands over the artifact; no current choice sends a spared god away to be restored later. Noctura can be allied in the conversation, and the alliance hands over the Shadow Crown, so the final artifact still appears once you hold all six god artifacts. A character allied with her in an earlier version receives the Crown at the next load, and the final artifact at the next place entered once all six are held. If she is allied when you beat Manwe in a fight, she turns on you: a betrayal scene plays once before that fight, and afterwards the Faith room of the Temple lists it to forgive, or to avenge once if she escaped the fight. Manwe is met only after all six are resolved, and the final choice comes when his strength fails.
:::

:::spoiler Spoiler: what each answer and outcome gives
**Outcomes.** A kill moves you 100 toward Darkness and pays the god's level x {{balance:OldGodDefeatXPPerLevel}} experience, level x {{balance:OldGodDefeatGoldPerLevel}} gold and 50 Fame, on top of the fight's own win of level x 2,000 experience and level x 500 gold; a god of floor 50 or deeper also leaves a Heart of the Ocean. Maelketh, level 28, pays 112,000 experience and 28,000 gold, plus the fight's 56,000 and 14,000. A save pays level x 1,000 experience and moves you 100 and then 150 more toward Chivalry, with no gold or Fame; a save in the conversation has no fight win. An alliance moves you 50 toward Chivalry and gives 2 Wisdom. Losing an Old God fight and surviving the death screen wakes you at a quarter of your maximum HP and costs the god's level x 100 experience.

**Saving in the fight.** With V in the fight you can save a god that can be saved: you need the Soulweaver's Loom, the god at half its HP or less, and Chivalry at least 200 above your Darkness. Veloura and Aurelion can be saved this way only once you hold their own artifact, Noctura and Terravok on a first meeting.

**How the answers change the fight.** An answer can change your damage, the god's damage and the god's defense. Your damage changes the god's HP instead: 25 percent more damage means the god has 1 / 1.25 of its HP, so Maelketh's 49,500, alone or with five or more fighters, becomes 39,600. The god's damage is its own factor divided by your defense factor, never more than {{balance:OldGodDialogueBossDamageCap}} times, and it also scales the god's area attacks, channels and corruption.

- **Maelketh:** challenging him gives you 25 percent more damage, 15 percent less defense, 10 points more critical hit chance and bloodlust. Asking him to teach you gives 20 percent more defense, his damage x0.85 and a dodge of his first attack. The answer of peace changes nothing.
- **Veloura:** hostility gives you 15 percent more damage and her damage x1.10. Empathy weakens her (Strength x0.85) and sets her damage x0.80; a mercy kill weakens her and sets her defense x0.70. Mira in your party multiplies her damage by 0.9 more.
- **Thorgrim:** defiance gives you 10 percent more damage and his damage x1.15 (held to x1.10). The Right of Challenge gives 15 percent more defense plus 20. The paradox confuses him for the whole fight, with his damage x0.75 and defense x0.85.
- **Noctura:** the teaching fight sets her damage x0.5 and defense x0.7 and gives you 15 percent more damage. If the Stranger found you unwilling (receptivity below 0) or she is enraged: her damage x1.25 (held to x1.10) and 2 points less critical hit chance. Receptivity 25 or more: her damage x0.7, her defense x0.85 and 10 percent more damage for you. Otherwise 10 percent more damage for you and 2 points less critical hit chance.
- **Aurelion:** defiance gives you 20 percent more damage and 10 percent less defense; humility sets his damage x0.85 and gives 15 percent more defense.
- **Terravok:** a destructive answer gives you 30 percent more damage and his damage x1.20 (held to x1.10); respect weakens him and sets his damage x0.9.
- **Manwe:** compassion sets his damage x0.8 and defense x0.85; righteousness gives 15 percent more defense and his damage x0.9; any other answer gives 25 percent more damage, 10 points more critical hit chance and 15 percent less defense. Then, by the other gods: two or more allied add 0.20 to your damage and defense; otherwise three or more saved weaken him and set his damage x0.85 more; otherwise five or more destroyed add 0.35 to your damage and raise the critical hit bonus to 15 points. Defiance to the Stranger adds 50 damage and the willing hero's answer 30 defense.

**Manwe's offer.** Once, at a tenth of his HP, Manwe may stop and offer peace. Y spares him and ends the fight as a save; N goes on, and a second draw of the offer strikes instead. Noctura's own offer in her fight only makes her skip that attack; her alliance comes only from the conversation before it.

**Noctura's betrayal fight.** You first regain half your missing HP. She fights from 300,000 base HP with one attack a round. A win pays 300,000 experience, 100,000 gold and 100 Fame; a loss lets her escape with her power.

**Your god and the Old Gods.** A Zealot or Chosen of a Temple god deals {{balance:GodEchoDamagePct}} percent more spell and basic swing damage to the Old God that god echoes: Solarius Aurelion, Valorian Maelketh, Amara Veloura, Judicar Thorgrim, Umbrath Noctura, Terran Terravok and Arcanus Manwe. A player-god echoes through the Temple god of its domain.
:::

:::spoiler Spoiler: ending conditions
The ending is decided when Manwe is resolved, by a fight, a save or an alliance; it is not picked from a menu. If the session ends before the ending plays, it plays at your next login. The game checks the endings in this order and the first that fits is yours: the secret Dissolution ending, the True Ending, the older True Ending rule, Usurper, Savior, then Defiant. So Usurper wins over Savior when both fit.

Alignment here is Chivalry minus Darkness.

- **Usurper:** alignment below -300, or 5 or more of the six gods before Manwe destroyed. Beating Noctura after she turns on you counts as destroying her.
- **Savior:** alignment above 300, or 3 or more gods saved. The gods that count as saved are Veloura, Aurelion and Terravok when saved, and Noctura while she is still your ally. She stops counting when you beat Manwe in a fight, because she then turns on you; she still counts when Manwe is allied in the conversation or spared through his offer.
- **Defiant:** anything else.
- **True Ending:** all seven seals, Awakening stage 7, a companion's death witnessed or a grief fully lived through, at least 2 of the four gods above counted as saved, alignment from -500 to 500, and a Blood Price below {{balance:MurderWeightEndingBlock}}.
- **The older True Ending rule:** from the third cycle, Noctura still allied, 2 of Veloura, Aurelion and Terravok saved, all seven seals and alignment from -200 to 200. It asks no Awakening, grief or Blood Price.
- **Dissolution (secret):** from the third cycle, 2 or more different endings completed before, Awakening stage 7, the full truth of who you are, all 7 Wave Fragments and a Blood Price below {{balance:MurderWeightEndingBlock}}. You learn the full truth from the memory of the full truth, or when a major memory brings your recovered memories to 8.

A Blood Price of {{balance:MurderWeightEndingBlock}} or more closes the True Ending and Dissolution, and above {{balance:MurderWeightAwakeningBlock}} you gain no Awakening insights from dreams, visions, discoveries, songs or choices. After every ending but Dissolution the game offers ascension and then the Eternal Cycle; Dissolution offers neither.
:::

:::spoiler Spoiler: story chapters and the moral choices
Four events move the story's chapter forward: meeting the Stranger, a first visit to floor 45, holding all six god artifacts (when the final artifact appears), and a first visit to floor 100. No other event or conversation sets a chapter, so the chapters between them never come.

Five moral choices are written, each offered once and each waiting for a chapter: the possessed child on floor 25, Veloura's cure on floor 65, freeing Terravok on floor 80, destroying the darkness on floor 95 (with the Sunforged Blade), and a final choice on floor 100. With the chapters above, a floor by floor descent never offers the possessed child, whose floor checks it only on the first visit; Veloura's cure and freeing Terravok come only on a first visit to their floor after the final artifact; destroying the darkness is checked on every visit to floor 95 once the final artifact is held. No code offers the final choice.
:::

Secret boss definitions are in the [boss reference](/wiki/en/monsters/bosses/).
