---
title: What each god's boon does
path: /wiki/en/gods/boons/
checked: 1.3.1
sources: Scripts/Systems/GodBoonSystem.cs, Scripts/Systems/DivineBlessingSystem.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/StatEffectsSystem.cs, Scripts/Systems/SpellSystem.cs, Scripts/Systems/WorldBossSystem.cs, Scripts/Locations/WildernessLocation.cs, Scripts/Locations/DarkAlleyLocation.cs, Scripts/Locations/HomeLocation.cs, Scripts/Locations/SettlementLocation.cs, Scripts/Systems/QuestSystem.cs, Scripts/Core/Character.cs, Scripts/Core/GameConfig.cs
---
A boon is your god's domain effect. The numbers below are the full boon, which you have at Zealot and Chosen; a Follower gets {{balance:GodBoonFollowerStrengthPct}} percent of each and a Devout {{balance:GodBoonDevoutStrengthPct}} percent (the [Favor table](/wiki/en/gods/favor/)). A boon only helps in the place that uses it.

## Boon strength by Favor tier

A Devout follower of Amara heals {{balance:GodBoonAmaraHealPct}} x {{balance:GodBoonDevoutStrengthPct}} / 100 percent more, about 10 percent. A whole-number boon is rounded half up and is at least 1 while it applies. A player-god's followers get the same boon of its domain, scaled again by the god's standing (see [player-gods](/wiki/en/gods/player-gods/#domain-boon)).

## Fighting and healing boons

- **[Solarius](/wiki/en/gods/solarius/):** {{balance:GodBoonSolariusHealPct}} percent more healing from your spells, abilities, potions and the healing herb while you fight at least one living undead or demon. It also gives {{balance:GodBoonSolariusUndeadDamagePct}} percent more damage on your basic swings against undead and demons.
- **[Valorian](/wiki/en/gods/valorian/):** {{balance:GodBoonValorianLowHpDamagePct}} percent more damage while you are below {{balance:GodBoonValorianHpThresholdPct}} percent of your maximum HP, on your basic swings.
- **[Amara](/wiki/en/gods/amara/):** {{balance:GodBoonAmaraHealPct}} percent more healing from every heal you cast (spells, abilities and songs, on yourself or an ally) and {{balance:GodBoonAmaraHealPct}} percent stronger party wards you raise.
- **[Judicar](/wiki/en/gods/judicar/):** {{balance:GodBoonJudicarDefencePct}} percent less damage from each monster hit, always leaving at least 1, and {{balance:GodBoonJudicarBountyPct}} percent more gold from bounties you collect.
- **[Umbrath](/wiki/en/gods/umbrath/):** {{balance:GodBoonUmbrathTheftPct}} percentage points on your Dark Alley pickpocket chance and its cap. It also adds {{balance:GodBoonUmbrathCritPct}} points of critical hit chance on your basic swings, under the same ceiling as every other crit bonus.
- **[Arcanus](/wiki/en/gods/arcanus/):** {{balance:GodBoonArcanusSpellPct}} percent more spell damage and {{balance:GodBoonArcanusManaRegenPct}} percent more mana regained each combat round. A larger mana pool is a different effect.
- **[Discordia](/wiki/en/gods/discordia/):** {{balance:GodBoonDiscordiaPvpDamagePct}} percent more damage against players, and each monster has a {{balance:GodBoonDiscordiaFirstActionFailPct}} percent chance that its first action of the fight fails.

## Survival and reward boons

- **[Terran](/wiki/en/gods/terran/):** {{balance:GodBoonTerranMaxHpPct}} percent more maximum HP, and {{balance:GodBoonTerranYieldPct}} percent more garden herbs a day and a larger settlement council share. A larger maximum is not a free heal.
- **[Mortis](/wiki/en/gods/mortis/):** {{balance:GodBoonMortisDeathGoldCutPct}} percent less gold lost when you die. The death-saving Miracle is a separate Chosen benefit.

## Does Sylvana double wilderness gold and experience

Yes, at full strength. Sylvana's boon is {{balance:GodBoonSylvanaWildernessPct}} percent more, which doubles these wilderness gains at Zealot and Chosen: the bonus gold after a wilderness fight you win, gold you find while foraging, gold in ruins, and the experience a wilderness shrine gives. A Follower gets a third more and a Devout two thirds more. The experience and gold of the wilderness fight itself, and rewards outside the wilderness, are not doubled.

## Read the actual strength

Each god's page shows its base constants, deeds and taboos. The Altars screen in the Temple shows your current boon, ward and tier.

God wards protect Mental and are separate from these boons; see [Favor](/wiki/en/gods/favor/#mental-wards). A domain's damage or reward boon does not erase grief, strain or withdrawal.
