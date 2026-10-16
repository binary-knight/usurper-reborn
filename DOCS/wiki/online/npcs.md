---
title: NPC life and relationships
path: /wiki/en/online/npcs/
checked: 1.3.2
sources: Scripts/Systems/CompanionSystem.cs, Scripts/Systems/WorldSimulator.cs, Scripts/Systems/NPCSpawnSystem.cs, Scripts/AI/EnhancedNPCBehaviors.cs, Scripts/Systems/FamilySystem.cs, Scripts/Systems/WorldSimService.cs, Scripts/BBS/DoorMode.cs, Scripts/Core/GameConfig.cs, DOCS/release-notes/RELEASE_NOTES_1.2.0.md
history: 1.3.2 | NPCs on your team or in your party and your spouse or lover keep their gear, and NPC shopping buys only gear the NPC's class can use.
history: 1.3.0 | A companion can leave over resentment, taking a new lover without telling a spouse or an exclusive lover brings a heartbreak scene in town, and a spouse or lover who falls at your side and will recover is no longer lost to you.
history: 1.2.5 | Companion screens, romance milestones and companion quest notices are shown in all five languages.
history: 1.2.4 | none
---
Townsfolk are part of the persistent world, not static vending machines. They age, marry, have children, die and move in, in single-player and online. The rules below are per world tick: a tick comes every 30 seconds while a single-player game runs, and online every 60 seconds unless the server sets another interval.

## How long does an NPC live: lifespan by race

An NPC ages one game year every {{balance:NpcLifecycleHoursPerYear}} real hours, counted from its birth date on the real clock, and dies of old age at its race's lifespan, for good. The [races table](/wiki/en/characters/races/) lists every race: a Human lives 75 game years, about 30 real days from birth, an Elf 200 (80 days) and a Gnoll 50 (20 days).

A town NPC made when the world starts is aged 18 to 49 and an ordinary newcomer 20 to 35, so its remaining life is its race's lifespan minus its age, times {{balance:NpcLifecycleHoursPerYear}} hours. Story characters never age and never die of old age. An NPC on your team does not age while it serves you; after it leaves, it cannot die of old age for {{balance:RecruitOldAgeGraceDays}} days.

### Can a race die out

No. While 3 or fewer NPCs of a race are alive, old age passes them by: an NPC of that race who reaches the lifespan has its birth date moved 7 real days later instead. A race with fewer than 5 living NPCs receives newcomers up to 5 at the next tick, alternating male and female.

### When an NPC dies of old age

Its spouse is widowed, a player spouse is told, a player's romance with it ends, an NPC on a player's team leaves its gear, items and gold to the team leader, and its children who are left without parents go to the royal orphanage.

## Do NPCs come back after they die

An NPC that dies and is not dead for good returns after 20 ticks (10 minutes in single-player) on Main Street at full HP, with half its gold.

### Can NPCs die permanently

A death is permanent by chance: {{percent:PermadeathChanceDungeonSolo}} percent alone in the dungeon, {{percent:PermadeathChanceDungeonTeam}} percent with a team in the dungeon or at a player's side (where an NPC at 0 HP is first Downed and dies only when it is finished, see [Downed allies](/wiki/en/combat/party/#downed-allies)), {{percent:PermadeathChanceNPCvsNPC}} percent in a fight between NPCs and {{percent:PermadeathChanceTeamWar}} percent in a team war. The chance is multiplied by 1 minus {{balance:PermadeathLevelReduction}} per level of the NPC, never below 1 percent: a level 20 NPC at a player's side has 1.4 percent. No death is permanent while fewer than {{balance:PermadeathPopulationFloor}} NPCs are alive, while {{balance:PermadeathRaceFloor}} or fewer of its race are alive, or for a story character, and away from its leader an NPC on a player's team cannot die for good. See [party and allies](/wiki/en/combat/party/) for your own allies.

## How many NPCs can the town have

At most {{balance:MaxNPCPopulation}} living NPCs. At 200, newcomers, new pregnancies and comings of age stop; from 190 only a race short of 5 NPCs still receives newcomers.

### What level are town NPCs, and where do they start

An NPC made when the world starts gets a level by a roll: 20 percent of them 1 to 15, 30 percent 15 to 40, 30 percent 40 to 65 and 20 percent 65 to 80. A newcomer's level follows the town: in single-player the average level of the living NPCs, at least 10, plus or minus 10; online the average plus or minus 3. A newcomer's class leans toward the classes the town lacks.

A new NPC starts in one of 15 places, weighted: Main Street, the Inn and the Castle twice each, and the Auction House, the Temple, the Church, the Weapon, Armor and Magic shops, the Bank, the Healer and the Dark Alley once each. A moving NPC goes to another place, more often where its friends are and less often where its enemies are or a crowd of 6 or more has gathered.

## How do NPCs get married

An unmarried NPC of level 5 or more has a 1 in 500 chance each tick to try. The partner must be level 5 or more, within 10 levels, attracted to it and not close kin, and the pair must score above 0.3 for compatibility; the try then succeeds at the compatibility x 50 percent. An NPC in a polyamorous or open relationship can try while married.

## How often do NPCs have children

Each tick a female NPC aged 18 to 45 who is not already expecting becomes pregnant with a chance that falls as the town grows: 1 in 150 while the town (NPCs and children) holds fewer than 40, 1 in 400 below 80, 1 in 800 below 120, 1 in 2,000 below 160, and 1 in 4,000 after that. The father is her husband; a married NPC who is flirtatious (above 0.6) has a 15 percent chance that he is an affair partner instead, and an unmarried flirtatious one (above 0.5) conceives with an affair partner. A couple has at most 4 children. A pregnancy lasts 7 real hours. A child ages a year every {{balance:NpcLifecycleHoursPerYear}} real hours and becomes an adult townsperson at 18, about 7.2 real days after birth.

## Do NPCs divorce

Each tick a married couple divorces with a base chance of 0.3 percent in single-player and 0.02 percent online. Each partner with a commitment below 0.3 adds 0.5 percent, each with a flirtatiousness above 0.7 adds 0.3 percent, and opposite alignments add 0.4 percent; the total is cut to a fifth when both have a commitment above 0.7.

## Friendship and faith

A friend conversation and resting at the Inn with a close friend can restore Mental within daily limits. Shared faith can warm a first conversation of the day; opposed gods can cool it.

Chosen characters can receive remarks about their god's mark. NPC faith also contributes to Temple standing and can change through conversion.

## Loss and grief

The death of a companion, teammate, spouse or lover can cause Mental loss and grief. Grief stages have their own consequences, listed in the [Mental guide](/wiki/en/characters/mental/#grief); a replacement party member is not an immediate cure for the relationship's loss.

A hired NPC or an NPC friend in your dungeon party who falls in your fight and will recover is carried out to mend. If that NPC is your spouse, lover or affair partner, the marriage or romance is not ended; it ends only when the NPC is lost for good.

## Betrayal

Story companions can leave you over resentment: see [companions](/wiki/en/combat/companions/). Taking a new lover while an NPC spouse who agreed to no other, or an exclusive NPC lover, has not been told brings a heartbreak scene the next time you are between actions in town. The scene gives 3 Wisdom and does not change the marriage or lover records.

Inspect current dialogue and Status rather than expecting a wiki to know whether a particular NPC is alive on your server.
