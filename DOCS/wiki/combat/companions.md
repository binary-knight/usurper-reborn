---
title: Companions and beasts
path: /wiki/en/combat/companions/
checked: 1.3.4
sources: Scripts/Systems/CompanionSystem.cs, Scripts/Data/BeastData.cs, Scripts/Systems/MentalSystem.cs, Scripts/Locations/InnLocation.cs, Scripts/Locations/DungeonLocation.cs, Scripts/Locations/TempleLocation.cs, Scripts/Locations/PrisonLocation.cs, Scripts/Locations/MusicShopLocation.cs, Scripts/Locations/HealerLocation.cs, Scripts/Locations/MainStreetLocation.cs, Scripts/Locations/WildernessLocation.cs, Scripts/Data/WildernessData.cs, Scripts/Locations/HomeLocation.cs, Scripts/Systems/CombatEngine.cs, Scripts/Core/Items.cs, Scripts/Core/Character.cs, Scripts/Systems/BetrayalSystem.cs, Scripts/Systems/WorldSimulator.cs, Scripts/Systems/DifficultySystem.cs, Scripts/Systems/StatEffectsSystem.cs, Scripts/Systems/AlignmentSystem.cs, Scripts/Systems/DailySystemManager.cs, Scripts/Systems/MiracleSystem.cs, Scripts/Systems/TrainingSystem.cs, Scripts/Locations/LevelMasterLocation.cs, Scripts/Core/GameConfig.cs
history: 1.3.4 | none
history: 1.3.3 | A companion at 0 HP in a fight is Downed and dies only when a monster finishes it or when, still standing, it takes a killing blow in your place; leaving it to die now means holding a healing potion and not rescuing it while it is Downed.
history: 1.3.2 | Companions regain stamina and mana at the end of each round, and when your gold does not cover every entry fee only the NPCs you cannot pay for stay at the dungeon entrance.
history: 1.3.0 | Companions remember a murder they witness and a companion left to die: after a warning the least loyal one leaves, handing back its gear, and forgiveness in the Temple's Faith room brings it back.
history: 1.2.5 | Companion descriptions, romance milestones, quest history and last words are shown in all five languages.
history: 1.2.4 | none
history: 1.2.3 | Beast species names are translated into all five languages; beast names stay in English.
---
Story companions and tameable beasts are separate systems. A beast does not take a companion's place, follow its recruitment rules or give its bonuses. For the party as a whole (size, fees, experience, stances) see [party and allies](/wiki/en/combat/party/).

## Story companions

There are five story companions. Each has a role that decides how it grows and its starting gear, and in a fight it uses the class in the "Fights as" column. A weapon in its main hand must be of a type in its column and must also allow the companion's class (see [weapons and shields a class can use](/wiki/en/characters/classes/#weapons-and-shields-a-class-can-use)).

{{table:companions}}

You can have {{balance:MaxActiveCompanions}} companions active at once. A companion recruited while {{balance:MaxActiveCompanions}} are active waits at the Inn; swap them there with P (manage party), then S.

## Recruiting a companion

A companion can be recruited once your level reaches its recruit level in the table; below it the companion is not ready. It joins at its recruit level plus 5, whatever your own level.

### Recruit any companion at the Inn

At the Inn, A (approach the stranger) appears when at least one companion is alive, not recruited, has not left you over resentment, and has a recruit level you have reached. Pick the companion's number, then R. This works even if you declined or never saw that companion's own scene.

### Aldric: the Inn

From level 10, each time you enter the Inn there is a 20 percent chance of a bandit scene, at most once per session; answer Y to take Aldric with you. From level 3 a one-time line at the Inn shows a scarred soldier watching.

### Lyris: dungeon floor 15

From level 15, the first mysterious shrine you find on dungeon floor 15 shows Lyris. R asks her to join; T talks first and then asks. The scene does not come back, whatever you choose. With 4 allies already in your party you must remove one to make room. From level 5 a one-time line on Main Street hints at her.

### Mira: the Temple

From level 20 the Temple menu shows M, the Meditation Chapel. R there recruits Mira. The option is gone after one visit, whatever you choose. From level 5 a one-time line at the Healer hints at her.

### Vex: the prison

From level 25, while you are a prisoner, the prison menu shows V. E lets Vex help you escape, and he joins. The option is gone after one meeting. From level 4 a one-time line on Main Street hints at him.

### Melodia: the Music Shop

At the Music Shop press T. From level 20 Melodia offers to join and Y accepts; below level 20 she only talks.

## Companion levels and growth

A companion uses the same experience table as you and stops at level 100. Each level gained adds 12 to 24 HP plus 1 for every 10 levels; a Tank gets 8 more, a Healer 2 more and a Bard 1 more. Other gains follow the role: a Tank gains 2 or 3 defence and 1 or 2 attack, a Damage companion 3 or 4 attack, 1 or 2 speed and 1 defence, a Healer 4 or 5 healing, 2 or 3 magic and 1 defence, a Bard 3 or 4 magic, 2 or 3 healing, 1 or 2 speed and 1 attack.

A living companion and a living NPC teammate in your dungeon party each take 75 percent of the experience you gain from a room feature you examine. In a group with another player they also each take 75 percent of the experience from treasure, a treasure chest, a cleared floor and the floor boss bonus, on top of their part of the fight's experience.

A companion can train a skill up to Superb; an NPC ally stops at Expert. At the Level Master, H (help a team member) gives an active companion, an NPC on your team or your spouse up to half of your own experience, which leaves your total.

## Companion loyalty

Loyalty runs from 0 to 100 and starts at 50. Each level a companion gains adds 1. At the Inn, P (manage party), the companion's number, then G gives a gift: 50 gold for 3 loyalty, 200 gold for 8, 500 gold for 15. There is no daily limit on gifts.

Difficulty scales loyalty gains only: Easy gives 1.5 times, Hard 0.75 times and Nightmare half, never less than 1. Losses are not scaled.

### Romance with Lyris or Melodia

Lyris and Melodia are the two companions open to romance. At the Inn, P, the companion's number, then R, once a day: with loyalty 60 or more the bond grows by one step with a chance of 30 plus your Charisma, at most 80 percent. The bond runs up to 10.

## Companion personal quests

A personal quest can start at loyalty 50, so a new companion can start it at once. At the Inn, P, the companion's number, then Q, and accept: accepting adds 10 loyalty. Its scenes then come only while the companion is in your active party, on a room you enter for the first time, one quest scene per room:

- Mira: dungeon floors 40 to 50, 15 percent a room.
- Melodia: floors 50 to 60, 15 percent a room.
- Aldric: floors 55 to 65, 15 percent a room.
- Lyris: floors 80 to 90, 15 percent a room.
- Vex: any floor, 10 percent a room, once one of these holds: 10 real days since he joined, floor 70 or deeper, or his second story scene is done.

### Companion story scenes

Before the quest, each companion has two story scenes, in order, on any floor at or below a minimum: Aldric 10 then 30, Lyris 15 then 45, Mira 20 then 32, Melodia 20 then 38, Vex 25 then 45. Each new room has a 12 percent chance per active companion, with one scene per companion per dungeon visit and none on an Old God's floor. A room that plays a story scene plays no quest scene. Story scenes stop once the companion's quest is complete.

## When a companion dies

A companion at 0 HP in a fight is Downed, not dead: it stays in the party until you rescue it, a monster finishes it, or the fight ends and it stands up again with half its maximum HP (see [Downed allies](/wiki/en/combat/party/#downed-allies)). A companion that a monster finishes is dead for good. Its equipment and its bag go into your inventory until your inventory is full; the rest is lost. The death costs you {{balance:MentalCompanionGriefLoss}} Mental before any god's ward and starts grief: see [Mental](/wiki/en/characters/mental/).

If a companion was Downed at the start of your turn, you held a usable healing potion and you did not choose Rescue, its finish also costs {{balance:AbandonCompanionLoyaltyPenalty}} loyalty and counts as leaving a companion to die (see resentment below).

### A companion who dies for you

When a monster's blow would kill you and a companion is in the fight, an active companion may take the blow and die in your place; you live with 1 HP. Only a companion standing in the fight can, never a Downed one, and it dies at once: a sacrifice is never a down. Aldric can always do it: his chance is his loyalty, and above loyalty 50 it is certain. Any other companion needs loyalty 90 or more, with a chance equal to its loyalty. A romance above 5 adds 30 to the chance.

### Aldric and your Darkness

Once a game day the game checks Aldric: if your Darkness is 5000 or more while his loyalty is 80 or more, he dies. This happens whether or not he is in your active party.

### How long Vex lives

Vex dies of his illness 30 real days after you recruit him, checked once a game day. Nothing in the game stops it.

## Resentment and leaving

Your story companions hold two things against you: a murder they witness, and a companion left to die. Each adds {{balance:TeamBetrayalPointsPerEvent}} to the party's resentment, once per event however many companions saw it. A warning comes at {{balance:TeamBetrayalWarningPoints}}. At 100 the least loyal companion in your active party leaves. The scene is shown between actions in town, never in the dungeon and never in prison. The companion hands back the gear and items that fit in your pack; what does not fit stays with the companion. Its level, experience and bond with you are kept.

A murder here is an NPC's permanent death that you caused. Each active companion loses {{balance:CompanionLossPerMurder}} loyalty for a deliberate murder and 5 for any other.

A loyalty gain of {{balance:TeamBetrayalKindnessMinGain}} or more, counted before difficulty, with a companion who is with you takes its amount off the resentment. The level up gain of 1 never counts.

A companion who left cannot be recruited again in the usual way. The Faith room of the [Temple](/wiki/en/world/temple/) can settle it; a forgiven companion waits at the Inn with loyalty {{balance:TeamBetrayalRejoinLoyalty}}.

## Companion potions and rest

A companion carries up to 5 healing potions plus 1 per level. Resting at Home, sleeping at the Inn and the daily reset refill the potions of your active companions: a Healer or Bard to 2 plus half its level, any other companion to 5 plus half its level, within that limit. A Healer or Bard also gets mana potions: 3 plus a third of its level, at most 3 plus half its level. A rest at Home and a night at the Inn also heal the whole party to full.

Each living story companion in your dungeon party cuts dungeon Mental strain by {{balance:MentalStrainCompanionCutPct}} percent, up to {{balance:MentalStrainCompanionCutMaxPct}} percent.

## Beasts

### Where to find a beast

Beasts are met only in the [wilderness](/wiki/en/world/wilderness/). After each expedition's main encounter the chance of a beast sighting is {{balance:BeastEncounterChancePercent}} percent, and you have {{balance:WildernessMaxDailyExplorations}} expeditions a day. The beast is picked at random from the species of the region you explore that your level meets and that you do not own yet. If no species qualifies, nothing is sighted.

### Beast species

A region opens at its own level, so a species is met from the higher of the region's level and its own; the table gives that level.

{{table:beasts}}

### Taming a beast

Press T when a beast is sighted. You get {{balance:BeastTameAttempts}} attempts; each rolls a twenty-sided die plus a quarter of your Charisma plus an eighth of your Dexterity and succeeds at the beast's taming difficulty or more. Example: Charisma 40 and Dexterity 24 add 13, so a Dire Wolf (19) needs a 6. Attunement at the Heart of the Tide shrine adds {{balance:ShrineVeloraCharismaBonus}} Charisma to this roll. If every attempt fails the beast flees with no other cost, and the species can be sighted again later.

### Beast roster and the active beast

Your roster holds {{balance:BeastMaxRosterSize}} beasts, one of each species; a species you own is never sighted again. Only one beast is active at a time. The first beast you tame becomes active at once; later ones wait in the roster. At Home press Y and type a beast's number to make it active, or U to have none active.

### Forest Hawk

The Forest Hawk marks 1 room in 20 (at least 1) of the floor as explored on your map when you step into the floor from the dungeon's floor overview with E. Taking the stairs to another floor does not trigger it.

### Mountain Goat

The Mountain Goat takes 1 off the fatigue each new dungeon room costs. A room costs 1 fatigue in any armor weight, so with the goat it costs none. Room fatigue is charged in single-player only.

### Cave Spider

The Cave Spider adds 5 to your critical hit chance, inside the same Dexterity based cap as other bonuses, and multiplies NPC reactions to you by 0.9.

### Marsh Toad

The Marsh Toad gives a 25 percent chance to resist the poison of a dungeon trap, a poisoned chest or a pixie's curse; poison from monsters in a fight is not covered. Once a day, the first time you step into a floor from the floor overview, it gives you 1 antidote.

### Bog Wisp

With the Bog Wisp you start every fight Hidden for 2 rounds, and each of your hits has a 12 percent chance to add shadow damage of a tenth of the hit, except against demons.

### Tidepool Sprite

The Tidepool Sprite restores 5 percent of your maximum mana for each game hour that passes. The game clock moves in single-player only, so online it gives nothing.

### Dire Wolf and Storm Eagle: fighting beasts

An active Dire Wolf or Storm Eagle joins your party each time you enter the dungeon, even when 4 allies are already there. It fights at the higher of its own level and half your level. From its base values in the table it gains, per level above 1, 10 percent of base HP, 8 percent of base attack and 5 percent of base defence. Example: with you at level 60 a Dire Wolf fights at level 30 with 312 HP, 59 attack and 19 defence. It has no mana.

Each Storm Eagle hit adds lightning damage of a tenth of the hit and has a 15 percent chance to stun the target for 1 round.

A fighting beast is never lost. At 0 HP it is Downed like any ally. After each fight it is back at full HP, and if a monster finishes it in a fight it is back at full HP the next time you enter the dungeon. Each won fight gives it a twentieth of your share of the fight's experience, at least 5, and it needs 50 times its level to gain a level. Passive beasts never gain levels.

Sylvana's Miracle calls a Dire Wolf at your own level for one fight, with the same scaling: see [Miracles](/wiki/en/gods/miracles/).

## Group health

A follower's condition counts even when your own HP is full. See [groups](/wiki/en/combat/groups/) and [Mental](/wiki/en/characters/mental/) for strain, recovery and collapse.
