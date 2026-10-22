---
title: The Music Shop
path: /wiki/en/world/music-shop/
checked: 1.3.6
sources: Scripts/Locations/MusicShopLocation.cs, Scripts/Data/EquipmentData.cs, Scripts/Core/GameConfig.cs, Scripts/Systems/CombatEngine.cs
history: 1.3.6 | none
history: 1.3.5 | none
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | A bought instrument is put on at the counter only for a Bard or a prestige class, not for a Mystic Shaman.
history: 1.3.0 | A companion of the shop who left you over resentment is away from the shop until forgiveness in the Temple's Faith room brings the companion back.
history: 1.2.5 | Instrument names and the Electron menu labels are shown in the player's language, and long purchase messages wrap at 79 columns.
---
The Music Shop specializes in instruments, performances and songs rather than general weapons. Instruments are equipment with their own weapon type and restrictions.

## Instruments

Browse the shop, compare the level and purchase requirements, and check whether the intended user can equip the instrument. The shop does not simply hide every low-level instrument from a high-level shopper.

For a Bard, an instrument can be relevant to the actions you use. It is not interchangeable with a sword merely because both occupy a weapon slot.

## Music Shop songs: prices and effects

Hiring a performance (P) is a service, not buying an instrument. A performance gives you a song buff for your next {{balance:SongBuffDuration}} fights, and a new song replaces the one you have. A Bard pays 25 percent less. Prices, with no tax added:

- **War March:** 200 + level x 10 gold. +{{percent:SongWarMarchBonus}} percent attack damage.
- **Lullaby of Iron:** 200 + level x 10 gold. +{{percent:SongIronLullabyBonus}} percent defence.
- **Fortune's Tune:** 300 + level x 15 gold. +{{percent:SongFortuneBonus}} percent gold from kills.
- **Battle Hymn:** 400 + level x 20 gold. +{{percent:SongBattleHymnBonus}} percent attack and +{{percent:SongBattleHymnBonus}} percent defence.

At level 20 War March costs 400 gold, or 300 for a Bard.

## Lore songs and companions

Lore songs (L) are a story-facing activity and can reveal history. Companion-related shop options depend on story state. A companion who left you over resentment is away from the shop, and the shop then says that forgiveness in the Temple's Faith room can bring the companion back.

See [companions](/wiki/en/combat/companions/) and [shops](/wiki/en/items/shops/) for the broader distinctions.
