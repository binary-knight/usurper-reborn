---
title: The world boss
path: /wiki/en/online/world-boss/
checked: 1.3.8
sources: Scripts/Systems/WorldBossSystem.cs, Scripts/Systems/WorldBossMath.cs, Scripts/Data/WorldBossData.cs, Scripts/Locations/MainStreetDistricts.cs, Scripts/Systems/DivineBlessingSystem.cs, Scripts/Systems/WorldEventSystem.cs, Scripts/Core/GameConfig.cs, Scripts/Locations/MainStreetClassic.cs
history: 1.3.8 | none
history: 1.3.7 | none
history: 1.3.6 | Your attack, Power Attack and Precise Strike now set off your main hand weapon's effects on the world boss, your gear's life steal heals you there, and Titan's Resolve on world boss armor gives 5 percent more maximum HP in place of defense.
history: 1.3.5 | none
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | Your attacks on a world boss roll on your full critical hit chance, and a second world boss kill refreshes the running Realm Celebrates.
---
A world boss is a shared online fight: every player who joins fights the same boss and its HP is shared. It exists only in online play. The bosses and their abilities are in the [boss reference](/wiki/en/monsters/bosses/#world-bosses).

## Where is the world boss

From Main Street press 7 for the World Boss; it is on the online row of the classic menu. With the district layout, open the Online district with O, then press B. You need level {{balance:WorldBossMinLevel}} to join. Rewards from a boss that fell while you were away are paid the next time you open it.

## When does the world boss spawn

A world boss appears every day at {{balance:WorldBossSpawnHourEastern}}:00 US Eastern time, whether or not anyone is online, and stays for {{balance:WorldBossWindowHours}} hours. A notice goes out {{balance:WorldBossNoticeHoursBefore}} hour before it appears.

### Which world boss will spawn

The game takes the median level of the players active in the last {{balance:WorldBossActiveDays}} days and picks at random among the bosses whose base level is at most {{balance:WorldBossPickLevelSlack}} above it. The boss fights at the higher of its base level and that median.

### What happens if the world boss is not killed

A boss alive at the end of its window withdraws and returns the next evening with {{percent:WorldBossNightRegenFraction}} percent of its maximum HP regained, for at most {{balance:WorldBossMaxNights}} nights. While nobody has hit it for {{balance:WorldBossRallyIdleMinutes}} minutes it regains 0.5 percent of its maximum HP every 30 seconds.

## How a world boss fight works

One visit lasts at most {{balance:WorldBossMaxRoundsPerSession}} rounds, then you rest and the game saves. You can come back {{balance:WorldBossRetreatCooldownSeconds}} seconds after a retreat or a rest and {{balance:WorldBossFallCooldownSeconds}} seconds after a fall.

### World boss damage cap

Your damage in one round counts for at most 0.6 percent of the boss's maximum HP, 1.5 times that (0.9 percent) while it is staggered. A player below the boss's level has the damage scaled down by N(your level) / N(boss level), where N(L) = 2 x L + 1.5 x L^1.05: at level 30 against a level 40 boss that is about 0.75.

Your attack, Power Attack and Precise Strike set off the fire, lightning, poison, holy and shadow effects of your main hand weapon, at the same chance and share of the hit as against a monster, and their damage is added to that round's damage before the cap. Lightning does not stun the boss, frost does nothing here, holy does not do the double damage it does to undead monsters, and weapon poison is one hit of 10 percent of the hit or the weapon's poison value, not 4 rounds of poison. Your gear's life steal heals you by its percent of the hit. Spells, class abilities and items set off none of them.

### World boss phases, focus and telegraphs

- **Phases:** phase 2 starts at {{percent:WorldBossPhase2Threshold}} percent of its HP and phase 3 at {{percent:WorldBossPhase3Threshold}} percent.
- **Focus:** the boss's basic attacks hit the player it focuses for {{balance:WorldBossFocusMultiplier}} times and everyone else for {{balance:WorldBossOffFocusMultiplier}} times. F (Challenge) takes the focus for {{balance:WorldBossChallengeHoldSeconds}} seconds.
- **Strikes:** a telegraphed strike costs {{percent:WorldBossTelegraphAnsweredPercent}} percent of your maximum HP if you braced (B) and {{percent:WorldBossTelegraphUnansweredPercent}} percent and its status if you did not.
- **Channels:** a telegraphed channel is broken by interrupts (T): it needs as many as the players engaged, at most {{balance:WorldBossInterruptsMax}}. A broken channel staggers the boss for {{balance:WorldBossStaggerSeconds}} seconds. A channel that lands costs everyone {{percent:WorldBossTelegraphUnansweredPercent}} percent of maximum HP, half that for a player who braced.
- **Unavoidable hits:** one takes at most {{percent:WorldBossUnavoidableCapPercent}} percent of your maximum HP.

Each answer and the Challenge use your round. Daily Miracles do not work in a world boss fight. Your god's damage boons and a prayer blessing's damage bonus do count here (see [boons](/wiki/en/gods/boons/)).

## How much damage to qualify for world boss loot

Your score is the damage you dealt to the boss over one player's share of its HP, a third of its maximum (the boss is built for {{balance:WorldBossKillBudgetPlayers}} players), at most 1.0. You qualify for the kill's pay with a score of {{balance:WorldBossQualifyScore}} or more.

## World boss rewards

A kill pays each qualified player experience of 450 x level^1.5 x (0.25 + 0.75 x score) and gold of 150 x level^1.5 x the same factor, both times the together bonus: {{percent:WorldBossTogetherBonusPerAlly}} percent more for each other qualified player, at most {{balance:WorldBossTogetherBonusCap}} times. The experience is capped at half the experience your next level needs. A qualified player also gains {{balance:WorldBossFameQualified}} Fame and an item: Common below a score of 0.25, Uncommon from 0.25, Rare from 0.5 and Epic from 0.75. A world boss armor item (not a weapon) of Epic rarity or better carries Titan's Resolve: 5 percent more maximum HP for whoever wears it, a companion or ally included, once however many worn items carry it. The top damage dealer gains {{balance:WorldBossFameMvpExtra}} Fame more, and a Legendary item when {{balance:WorldBossMvpLegendaryMinHumans}} or more players qualified. A kill also starts The Realm Celebrates, 10 percent more experience from monster fights until the next daily reset; a second kill before then does not add another (see [world events](/wiki/en/online/events/)).

A night the boss survives pays {{percent:WorldBossWithdrawalPayFraction}} percent of that night's kill experience and gold to those who fought it.

The pay for one player alone, by level:

{{table:world-boss-rewards}}

At the lower levels the cap decides: up to level 30 in the table even the qualifying score pays the full capped experience.
