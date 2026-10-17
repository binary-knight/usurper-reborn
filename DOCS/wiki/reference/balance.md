---
title: Balance rules
path: /wiki/en/reference/balance/
checked: 1.3.4
sources: Scripts/Core/GameConfig.cs, Scripts/Systems/GameDataLoader.cs, Scripts/Data/BalanceConfig.cs, Scripts/Systems/WikiDataExporter.cs, Scripts/Systems/StatEffectsSystem.cs, Scripts/Systems/CombatEngine.cs
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | Six balance values that no game code reads are no longer listed.
history: 1.3.1 | none
history: 1.3.0 | none
history: 1.2.7 | none
history: 1.2.6 | none
history: 1.2.5 | none
history: 1.2.4 | none
history: 1.2.3 | none
---
Moddable defaults are exported from the release's balance model. A server can override them in its balance file, and a single constant is not the full formula for every combat path. Six balance values that no game code reads are left out: the old critical hit chance and multiplier, the Backstab and Berserk multipliers, the Doom countdown and the tank's share of area damage.

Critical hits have no single chance or multiplier: the chance comes from Dexterity and gear and the damage from Dexterity, as [critical hits](/wiki/en/combat/#critical-hits) explains. Backstab's triple damage is fixed in the game and a server cannot change it.

The advanced disclosure contains exact combat, faith and Mental constants. Field names preserve the source identity for verification.

{{table:balance}}

Schema and generated game version are checked across all datasets during the build. [Entity pages](/wiki/en/) provide context for individual definitions rather than a promise that every field applies to every fight.
