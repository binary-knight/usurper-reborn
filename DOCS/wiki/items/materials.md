---
title: Crafting materials
path: /wiki/en/items/materials/
checked: 1.3.3
sources: Scripts/Core/GameConfig.cs, Scripts/Locations/DungeonLocation.cs, Scripts/Systems/OldGodBossSystem.cs, Scripts/Data/OldGodsData.cs, Scripts/Locations/MagicShopLocation.cs, Scripts/Locations/InnLocation.cs, Scripts/Locations/BaseLocation.cs, Scripts/Systems/ArtifactSystem.cs, Scripts/Systems/MetaProgressionSystem.cs
history: 1.3.3 | none
history: 1.3.2 | An alliance with Noctura hands over the Shadow Crown.
---
There are 8 crafting materials. They drop only in the dungeon, each on its own band of floors, and nothing drops on floors 1 to 19. The Magic Shop spends them on its highest [enchants](/wiki/en/items/enchanting/), and the Master Trainer at the Inn spends them on the last stat trainings. Materials do not take a backpack slot.

## List your materials

Type `/materials` anywhere (or `/mats`, `/mat` or `/m`) to list every material you hold, with its count and the floors it drops on.

## Crafting materials by floor

{{table:materials}}

## Material drop chance

A material can drop only on a floor inside its band. Where two bands overlap, the material is picked at random among those that drop on that floor. Difficulty does not change these chances.

- A won room fight with ordinary monsters: {{percent:MaterialDropChanceRegular}} percent chance of 1 material.
- A won room fight that held a mini-boss: {{percent:MaterialDropChanceMiniBoss}} percent chance of 1 material.
- A won boss room fight: always {{balance:MaterialDropCountBossMin}} or {{balance:MaterialDropCountBossMax}} of one material. This is the only certain drop.
- A treasure chest that opens to treasure: {{percent:MaterialDropChanceTreasure}} percent chance of 1 material.
- An Old God you defeat or save: 2 of the god's own material, and 1 Heart of the Ocean when the god's floor is 50 or deeper. The spoiler below names each god's material.

## Each material

### Crimson War Shard (crimson shards)

A Crimson War Shard drops on floors 20 to 35. Its one use is the Phoenix Fire enchant, which takes 1 Crimson War Shard and 1 Fading Starlight Dust.

### Withered Heart Petal

A Withered Heart Petal drops on floors 35 to 50. Nothing in the game spends it: no enchant and no training asks for it. You can collect it and see it in your list.

### Iron Judgment Link

An Iron Judgment Link drops on floors 50 to 65. Nothing in the game spends it: no enchant and no training asks for it.

### Shadow Silk Thread

A Shadow Silk Thread drops on floors 60 to 75. The Legendary enchant takes 1 with 1 Heart of the Ocean, and Frostbite takes 1 with 1 Fading Starlight Dust.

### Fading Starlight Dust

Fading Starlight Dust drops on floors 75 to 90. The Mythic enchant takes 1 on its own, Phoenix Fire takes 1 with 1 Crimson War Shard, and Frostbite takes 1 with 1 Shadow Silk Thread.

### Terravok's Heartstone

Terravok's Heartstone drops on floors 85 to 100. The Godforged enchant takes 1 with 1 Eye of Manwe.

### Eye of Manwe

An Eye of Manwe drops on floors 95 to 100. The Godforged enchant takes 1 with 1 Terravok's Heartstone, and the 5th stat training of an attribute at the Inn takes 1 with 1 Heart of the Ocean.

### Heart of the Ocean

A Heart of the Ocean drops on floors 50 to 100, the widest band. The Legendary enchant takes 1 with 1 Shadow Silk Thread, the 4th stat training of an attribute at the Inn takes 1, and the 5th takes 1 with 1 Eye of Manwe. Every Old God on floor 50 or deeper also leaves 1.

## Spending materials

An enchant takes 1 of each material it lists. The materials are spent when you pay, before the 4th or 5th enchant's failure roll, so a failed enchant still uses them up. See [enchanting](/wiki/en/items/enchanting/).

The Master Trainer at the Inn (W) sells +1 to an attribute 5 times per attribute. The 4th training of an attribute also takes 1 Heart of the Ocean, and the 5th takes 1 Heart of the Ocean and 1 Eye of Manwe. See [the Inn](/wiki/en/world/rest/).

## Old God rewards

{{table:old-god-rewards}}

:::spoiler Spoiler: how artifacts work
Each Old God you defeat or save hands over one artifact, except Manwe, who gives none, and an alliance with Noctura hands over hers as well; the seventh artifact appears once you hold the other six. An artifact's grants are permanent and come once. An Intelligence bonus on an artifact is granted as half Wisdom and half Dexterity, and an all-stats bonus raises Strength, Defence, Stamina, Agility, Charisma, Dexterity and Wisdom; the table shows what you receive. The Artifact Hunter bonus of a later life multiplies every grant by 1.5.
:::
