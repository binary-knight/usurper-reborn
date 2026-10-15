---
title: Home and family
path: /wiki/en/world/home/
checked: 1.3.1
sources: Scripts/Locations/HomeLocation.cs, Scripts/Locations/ChurchLocation.cs, Scripts/Systems/RelationshipSystem.cs, Scripts/Systems/IntimacySystem.cs, Scripts/Systems/FamilySystem.cs, Scripts/Core/Child.cs, Scripts/Systems/AlignmentSystem.cs, Scripts/Locations/CastleLocation.cs, Scripts/Systems/VisualNovelDialogueSystem.cs, Scripts/Locations/MainStreetDistricts.cs, Scripts/Core/GameConfig.cs, Scripts/Systems/DailySystemManager.cs, Scripts/Systems/CombatEngine.cs
history: 1.2.6 | A child's name given at birth or at Home cannot contain < > & or a double quote; the child keeps its name instead.
history: 1.2.5 | The Home screens are shown in all five languages, and long upgrade, trophy and pet rows wrap at 79 columns.
history: 1.2.4 | Online your Home is private, and a spouse left alone for 21 days you were present writes you a letter.
---
Your Home is a private base with rest, storage, upgrades and family life. Online, "Also here" is not shown at home, and say, emote, disconnect notices and other room lines from other players do not reach you there. From Main Street open the Home and Hearth district with H, then press H for Home. It appears after your first few levels.

## The Home menu

- **E Rest:** heals part of your HP and mana and the whole party. Rests per day and the amount healed grow with your Living Quarters. A rest also restores some Mental once a day.
- **Z Sleep:** in single-player you sleep in the evening, recover fully and wake the next morning. Online, sleeping at home needs the Reinforced Door and logs you out safely.
- **D, W, L:** deposit, withdraw and list items in your chest.
- **A and J:** gather and use herbs from your garden.
- **U Upgrades:** buy home improvements.
- **F, C, P, B:** your family, your children, your partner and the bedroom.
- **X Resurrect:** raise a dead spouse or lover for the dead character's level x 1,000 gold.
- **T, Y, G, V, H, I, S:** trophies, tamed beasts, partner gear, party inventories, a healing potion, your inventory and status.

## Home rest

A Home rest (E) heals a share of your maximum HP and mana set by your Living Quarters: 25 percent with none, up to 100 percent at level 5, with 1 to 5 rests a day. It also heals your whole party to full. With a Hearth, the rest gives a Well-Rested bonus to your attack and defence for your next few fights. The tables below give every level.

## Home upgrades and prices

Buy upgrades with U at Home, one level at a time. Living Quarters, Bed, Chest, Hearth and Herb Garden have 5 levels each and the Training Room has 10.

{{table:home-upgrades}}

A Bed level multiplies the chance of a child: 15 percent each time with a spouse and 5 percent with a lover, times the Bed's figure. Each Herb Garden level also unlocks the next [herb](/wiki/en/items/consumables/). Each Training Room level adds +1 to your base Strength, Dexterity, Constitution, Intelligence, Wisdom and Charisma; Agility and Stamina are not trained there.

## One-time Home purchases

- Trophy Room, 500,000 gold: shows your trophies (T).
- Study, 750,000 gold: {{percent:StudyXPBonus}} percent more combat experience.
- Servants' Quarters, 500,000 gold: {{balance:ServantsDailyGoldBase}} + level x {{balance:ServantsDailyGoldPerLevel}} gold each day.
- Reinforced Door, 250,000 gold: online, lets you sleep at Home safely.
- Legendary Armory, 2,500,000 gold: +5 permanent damage and +5 permanent defence.
- Vitality Fountain, 5,000,000 gold: bonus maximum HP equal to 10 percent of your maximum HP when you buy it.

## Marriage

You can marry an NPC you love at the Old Church, which offers ceremonies of different cost, or by proposing in conversation once the NPC is your lover. A ruler can also arrange a royal wedding at the [Castle](/wiki/en/world/castle/). Both of you must be adults and unmarried, not related, in love for some days, and the NPC must accept. A Church wedding costs 1,000 to 5,000 gold, moves you 10 toward Chivalry and gives 5 Charisma for good; its conditions and the chance an NPC says yes are on [the Church](/wiki/en/world/church/#how-much-does-a-wedding-cost). Courtship itself is covered in [relationships](/wiki/en/world/relationships/).

A living spouse adds combat experience and can join your dungeon party. The partner menu offers dinner, a walk, a fire and more; these share a cooldown and restore Mental. A spouse you neglect for too long loves you less, and the greeting at your door warns you. After 21 of your days without contact, your spouse sends you a letter. It appears on screen, and you can read it again in your online mailbox, or offline with the MAIL command on Main Street. Divorce is free from the partner menu at Home, but it hurts your children.

## Children

A child can be conceived with a spouse or a lover, up to {{balance:MaxPlayerChildren}} living children; the chance is set by your Bed (see above). You name a child when it is born and can rename it from C at Home. A name with < > & or a double quote is refused: a newborn keeps the name the game gave it, and a renamed child keeps its old name.

### When does my child grow up

A child ages one year every {{balance:NpcLifecycleHoursPerYear}} real hours and becomes an adult at 18: about 7.2 real days after birth. An adult child becomes a townsperson and can join your team at a discount. When the town is full, a child of age waits away and joins the town when there is room.

### What bonuses do children give

Each child under 18 gives you 2 percent more combat experience, 50 maximum HP, 5 Strength, 3 Charisma and 100 gold a day. Past 5 children each further child counts half, except for the gold. A royal child adds 5 more Charisma and 500 more gold a day. With 3 children you gain 6 percent experience, 150 HP, 15 Strength, 9 Charisma and 300 gold a day. The bonuses end when a child comes of age.

### Parenting a child

Use C at Home to parent a child, once per child every 20 real hours. You get a scenario for the child's age (0 to 4, 5 to 12 or 13 to 17) with 3 choices that move the child's soul, which runs from -500 to 500. Your own alignment shifts the result: when you are good, a kind choice gains your Chivalry minus Darkness / 20 more, at most 10, and when you are dark it loses your Darkness gap / 30, at most 5; a cruel choice mirrors this. With 300 Chivalry and no Darkness a kind choice of +20 becomes +30.

At 18 the soul sets the adult: above 200 the child becomes a Paladin, Cleric or Warrior with 50 to 99 Chivalry; below -200 an Assassin, Magician or Barbarian with 50 to 99 Darkness; otherwise any starting class.

### What is the dynasty tier

Your standing grows with your adult children: 1 makes you a Founder, 3 a Patriarch or Matriarch, 5 whose souls were all above 100 (or all below -100) when they came of age a Bloodline, and a Bloodline with a grandchild a Dynasty. NPCs react to you 5, 10, 15 or 20 percent better by tier.

### Orphans and adoption

A child whose parents are both gone goes to the royal orphanage. Only the ruler adopts orphans: see [the Castle](/wiki/en/world/castle/#royal-magic-the-orphanage-and-court-politics).

## Loss

A spouse's or lover's death brings grief and Mental loss. See [Mental](/wiki/en/characters/mental/) and [Home, the Inn and rest](/wiki/en/world/rest/) for recovery.
