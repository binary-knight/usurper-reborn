---
title: Afflictions, addiction and treatment
path: /wiki/en/characters/afflictions/
checked: 1.3.1
sources: Scripts/Locations/HealerLocation.cs, Scripts/Systems/MentalSystem.cs, Scripts/Core/StatusEffect.cs, Scripts/Core/GameConfig.cs, Scripts/Core/Character.cs
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

Poison, holds, confusion and other effects have their own duration and resistance rules. Bosses can resist effects that work on ordinary monsters. From dungeon floor 3 an [elite monster](/wiki/en/monsters/#elites) with the Venomous trait can poison you or a companion with a blow that did damage; the usual resistances to a status apply. An ability description tells you what it attempts, not that every target must accept it.

Before another expedition, treat the condition itself and restock supplies. See [Mental](/wiki/en/characters/mental/) and [combat](/wiki/en/combat/).
