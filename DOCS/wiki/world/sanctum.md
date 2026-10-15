---
title: The Sanctum
path: /wiki/en/world/sanctum/
checked: 1.3.2
sources: Scripts/Locations/SanctumLocation.cs, Scripts/Systems/AlignmentSystem.cs, Scripts/Data/HonorTournamentData.cs, Scripts/Core/GameConfig.cs
history: 1.3.2 | The Sanctum turns an Evil character away at the door, as the Church does.
history: 1.2.5 | Honor tournament champions and their drops are shown in each player's language, and the daily cost row is translated into all five languages.
---
The Sanctum offers charity and the honor tournament. It is separate from the Temple and the Castle's political menu. Open it from Main Street in the Castle Grounds district with S. Its holy wards turn an Evil character away at the door, as the Church's do; every other alignment may enter.

## How much do alms cost

Three acts of charity move you toward Chivalry, each priced by your level:

- **Alms (A):** {{balance:AlmsGoldPerLevel}} gold x your level, {{balance:MaxAlmsPerDay}} times a day, {{balance:AlmsChivalryReward}} toward Chivalry each.
- **The orphanage (O):** {{balance:OrphanageGoldPerLevel}} gold x your level, {{balance:MaxOrphanageGiftsPerDay}} time a day, {{balance:OrphanageChivalryReward}} toward Chivalry.
- **The hospice (H):** {{balance:HospiceGoldPerLevel}} gold x your level, {{balance:MaxHospiceTithesPerDay}} time a day, {{balance:HospiceChivalryReward}} toward Chivalry.

A member of the Faith pays {{percent:SanctumFaithMemberDiscount}} percent less. At level 20 alms cost 1,000 gold, the orphanage 4,000 and the hospice 3,000. The Chivalry follows the usual rule for alignment gains, so it shrinks once your Chivalry is high (see [alignment](/wiki/en/characters/stats/#alignment)). A charitable gift is not a shop purchase and returns no equipment.

## How do I unlock the honor tournament

The Tournament of Honor (T) appears from 250 Chivalry, the Defender rank of renown. It needs a player fight left for the day and uses the same one run a day as the Anchor Road Gauntlet, so you can do one or the other. The entry fee is {{balance:GauntletEntryFeeQuadraticCoefficient}} x your level squared: 4,000 gold at level 20.

## What does the honor tournament pay

You fight 3 champions in a row. Each champion you beat pays {{balance:GauntletGoldPerWavePerLevel}} x your level gold, {{balance:GauntletXPPerWave}} x (champion number + 3) x your level experience, 5, 10 and 15 Fame for the first, second and third, and 10, 15 and 20 toward Chivalry, plus a themed item. A lost fight ends the tournament and keeps what you won before it: it is a real death {{balance:GauntletDeathChancePercent}} percent of the time, and otherwise you are left at 1 HP.

Prepare for tournament combat with the same care as other chained fights. Mental collapse can interrupt a chain before the next fight.

## Hall of Heroes

The Hall of Heroes (V) records recognition.

For character condition and recovery, see [Mental](/wiki/en/characters/mental/) and the [Healer](/wiki/en/world/healer/).
