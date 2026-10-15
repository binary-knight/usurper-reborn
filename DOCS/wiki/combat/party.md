---
title: Party and allies
path: /wiki/en/combat/party/
checked: 1.3.2
sources: Scripts/Locations/DungeonLocation.cs, Scripts/Systems/TeamBalanceSystem.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/TeammatePolicy.cs, Scripts/Systems/TeamSystem.cs, Scripts/Systems/MaintenanceSystem.cs, Scripts/Systems/WorldSimulator.cs, Scripts/Systems/CompanionSystem.cs, Scripts/Locations/CastleLocation.cs, Scripts/Locations/LevelMasterLocation.cs, Scripts/Locations/TeamCornerLocation.cs, Scripts/Locations/InnLocation.cs, Scripts/Locations/HomeLocation.cs, Scripts/Systems/DailySystemManager.cs, Scripts/Core/GameConfig.cs
history: 1.3.2 | An NPC at Friendship or closer charges 15 percent less to enter the dungeon and one at Anger or worse 25 percent more, a short purse leaves behind only the NPCs you cannot pay for, and NPC party members keep their gear.
---
Your dungeon party is you and the allies who fight beside you: story companions, NPCs from your team, your spouse and lovers, an active fighting beast, and as ruler your royal mercenaries. Online, grouped players and player echoes can join too. This guide covers what every kind of ally shares; [companions and beasts](/wiki/en/combat/companions/) covers the story companions and beasts themselves.

## Party size

A dungeon party holds you and at most 4 allies. When you enter the dungeon your active companions join first, then the NPC allies you took along while fewer than 4 allies are present, then your active fighting beast, which joins even when 4 are already there. Player echoes and royal mercenaries come after it and join only while fewer than 4 allies are present, the beast included.

## Who can join the party

On a dungeon floor's overview, T opens party management. A adds an NPC ally: an NPC on your team who is alive and not in prison, your spouse, or one of your lovers, while the same holds for them. C and a number add a recruited companion who is not active. Both need fewer than 4 allies in the party. R removes an ally. Royal mercenaries join by themselves while you rule.

## Dungeon entry fee

An NPC ally more than 5 levels above you charges a fee each time you enter the dungeon: the levels above you minus 5 (at most 20), times 100, times 1 plus a tenth of its level rounded down. Example: you are level 10 and the NPC level 30, so 15 times 100 times 4 is 6,000 gold. Your spouse pays half that fee and a lover 25 percent less. Otherwise an NPC whose relation to you is Friendship or closer charges 15 percent less, and one at Anger or worse 25 percent more; a lover who is also a friend still pays 25 percent less. Story companions, beasts, player echoes and mercenaries charge nothing.

The game shows the fees and asks you to pay them. If you say no, every NPC ally with a fee stays at the entrance. If your gold does not cover them all, the game goes through the NPC allies from the dearest down and keeps each one whose fee your remaining gold still covers; you are asked to pay for those, and the others stay at the entrance. Your story companions, your beast, player echoes, mercenaries and allies without a fee come along either way. Saying no then leaves every NPC ally with a fee behind.

## Experience in a party

### Team bonus

A fight won with at least one living story companion or grouped player in it pays 15 percent more experience and 15 percent more gold. NPC allies, beasts and mercenaries alone do not give the bonus.

### Experience cut for a stronger ally

Your experience from a fight is cut when the highest level living ally who is not a story companion or a grouped player is above your level: 6 to 10 levels above leaves you 75 percent, 11 to 15 leaves 50 percent, 16 to 20 leaves 25 percent, and 21 or more leaves 10 percent. Up to 5 levels above costs nothing.

### Experience split

By default the fight's experience is split evenly between you and every living ally below level 100 in the party, and you take what is left over. Grouped players and player echoes take no share here: grouped players earn their own. With X in party management you can set your own percentages for you and 4 ally places instead. Then the share of a place whose ally is dead, missing or at level 100 goes to you, or, with automatic redistribution on, is spread over you and the living allies. Percent you leave unassigned is not paid to anyone.

### Catch-up for allies below your level

An ally below your level gets 10 percent more of its experience share for each level it is behind, with no cap: 10 levels behind doubles it.

### Giving your experience to an ally

At the Level Master, H (help a team member) gives an active companion, an NPC on your team or your spouse up to half of your own experience; it leaves your total.

## Gold in a party

Without a grouped player in the fight you keep all the gold. Online, with a grouped player in the fight, the gold is split equally between you and every living ally except story companions and player echoes; each grouped player and each NPC ally gets one share.

## Ally stances

Each ally that takes orders has a stance: Balanced (the default), Aggressive or Cautious. Set it for a companion at the Inn with P, then T, or for any ally in a dungeon room with Y (party), then T. Grouped players, player echoes and royal mercenaries take no orders and always act Balanced. The thresholds are shares of the ally's own maximum HP:

{{table:ally-stances}}

An ally that is about to die drinks its own potion before helping anyone. An ally with only one potion keeps it for itself, unless the most hurt member is you and you are below 30 percent HP. A braced ally takes half damage for the round. Monsters choose a Cautious ally less often, and a braced Aggressive ally draws their attacks.

When nobody in the party needs a heal, a Cleric ally who can cast Purify cures a member who is poisoned or diseased: you first, then the member with the lowest share of HP.

## Potions and rest for allies

A rest at Home, a night at the Inn and the daily reset refill the potions of your active companions and of every NPC on your team or in your dungeon party, and the rest at Home and the night at the Inn also heal all of them to full. An NPC ally that casts spells is filled to 2 healing potions plus half its level, any other to 5 plus half its level, never above 5 plus its level; a caster also gets 3 mana potions plus a third of its level, never above 3 plus half its level. A refill never lowers what an NPC ally already carries. Companion potions are on [companions and beasts](/wiki/en/combat/companions/#companion-potions-and-rest).

## Party gear

NPCs in your party keep the gear you give them. The world does not re-gear or sell from an NPC on your team, an NPC in your dungeon party, your spouse or your lovers: only your own actions change their gear. NPCs on your team or in your dungeon party also stay out of the town's errands; your spouse and lovers still go about town and come home to you, but never to buy gear. Other NPCs do not attack an NPC on your team or in your dungeon party; a spouse or lover who is not in your dungeon party can still be attacked in town. Online, this covers your team, and your dungeon party while it is in the dungeon. NPCs outside a party shop for gear their class can use and replace a piece only with one that is stronger and worth more; the piece they replace goes to their stock for sale.

## Hiring NPCs

At Team Corner, N hires an NPC who is not on a team onto your own team. You need a team first, and it holds at most 5 members counting you (see [teams](/wiki/en/online/teams/)). Hiring works in single-player and online.

### Hire price

- The per-level part is the lower of {{balance:NpcRecruitmentCostPerLevel}} gold per level and {{balance:NpcRecruitmentCostPerLevelSquared}} gold times the level squared: lower below level 25, the same from level 25 up. The NPC's Strength, Defence and Agility added together, times 20 gold, are added.
- An NPC above your level costs {{balance:RecruitAboveLevelPercentPerLevel}} percent more for each level above you, up to {{balance:RecruitAboveLevelMaxPercent}} percent of the price at 10 levels above. An NPC more than 5 levels below you costs 30 percent less.
- Your relationship with the NPC then multiplies the price: Friendship {{percent:RecruitPriceFriendship}} percent, Trust {{percent:RecruitPriceTrust}}, Respect {{percent:RecruitPriceRespect}}, Normal {{percent:RecruitPriceNormal}}, Suspicious {{percent:RecruitPriceSuspicious}}, Anger {{percent:RecruitPriceAnger}}. An NPC at Enemy or Hate is listed but will not join.
- Your own grown child joins at {{percent:AdultChildRecruitmentDiscount}} percent of the price, whatever your relationship.
- No price is below 100 gold.
- The hire list shows the NPCs nearest your level first, within each relationship group.

### Who cannot be hired

An NPC already on a team, a special town character, a prisoner, an immortal and a dead NPC are not for hire. An NPC whose parent, sibling or child you killed refuses. Your spouse, lovers, casual partners and exes are not on the hire list: a spouse or lover joins the party from the dungeon party menu instead.

### The recruit pool

The town keeps a pool of up to {{balance:RecruitPoolSize}} NPCs without a team to hire: {{balance:RecruitPoolPerBand}} each at levels 1 to 5, 6 to 10 and 11 to 15. An NPC on your team does not age while it serves you. An NPC past its lifespan dies seven days after leaving your team.

### Daily wages

Each living NPC on your team costs a daily wage: the lower of {{balance:NpcDailyWagePerLevel}} gold per level and {{balance:NpcDailyWagePerLevelSquared}} gold times the level squared, at least 1. The wages are taken from the gold you carry once a day, at the daily maintenance. If you cannot pay them all, all the gold you carry is taken and nobody leaves the team.

## Royal mercenaries

Only the ruler can hire royal mercenaries, at the Castle with B (bodyguards). You can have up to {{balance:MaxRoyalMercenaries}}, and each costs {{balance:MercenaryBaseCost}} gold plus {{balance:MercenaryCostPerLevel}} gold per level of yours, from your own gold. They join your dungeon party while you rule, within the 4 ally limit, and take no stance orders.

## When an ally falls

### Story companion

A companion who falls in a fight is dead for good: see [when a companion dies](/wiki/en/combat/companions/#when-a-companion-dies).

### NPC ally

An NPC ally, spouse or lover who falls in your fight dies for good with a chance of {{percent:PermadeathChanceDungeonTeam}} percent times 1 minus {{balance:PermadeathLevelReduction}} per level of the NPC, never under 1 percent: 1.4 percent at level 20. No NPC death is permanent while fewer than {{balance:PermadeathPopulationFloor}} NPCs are alive or {{balance:PermadeathRaceFloor}} or fewer of its race are alive, and story NPCs are exempt. An ally who does not die for good comes back to life after a short while; if a saved party member is dead when you enter the dungeon, a line tells you, and it rejoins on a later entry once it is alive again; Team Corner can raise a fallen member of your team for 1,000 gold per level. If you held a potion or a heal and did not aid it while it was below half HP, its opinion of you worsens. A permanent death of your spouse, a lover or a friend starts grief: see [Mental](/wiki/en/characters/mental/). The oath-mark is on [groups](/wiki/en/combat/groups/#the-oath-mark).

### Royal mercenary

A mercenary who falls is gone for good; hire another at the Castle.

### Beast

A fighting beast is never lost: see [fighting beasts](/wiki/en/combat/companions/#dire-wolf-and-storm-eagle-fighting-beasts).

### Player echo

A player echo that falls dissipates with no lasting effect on the player it copies.
