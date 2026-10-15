---
title: Teams and guilds
path: /wiki/en/online/teams/
checked: 1.3.2
sources: Scripts/Systems/TeamSystem.cs, Scripts/Systems/GuildSystem.cs, Scripts/Locations/TeamCornerLocation.cs, Scripts/Systems/TeamHQBonus.cs, Scripts/Server/GroupSystem.cs, Scripts/Core/GameConfig.cs
history: 1.3.2 | none
history: 1.3.0 | Hiring an NPC costs less below level 25 and its daily wage follows the same curve, an NPC above your level costs 5 percent more per level up to 50 percent, the town keeps a pool of up to 18 low-level NPCs to hire, the hire list shows the NPCs nearest your level first, and hired NPCs carry the oath-mark.
history: 1.2.6 | A new team or guild name cannot contain < > & or a double quote; existing names are kept.
history: 1.2.5 | Team news now shows the headline and the event, and guild ranks show in each player's language while saved in English.
history: 1.2.4 | none
---
Teams support adventuring relationships and group participation. Guilds are a separate online social system. Joining one does not imply that every member is automatically following you in combat.

A new team or guild name cannot contain < > & or a double quote. The game says so, creates nothing and takes no fee. Existing team and guild names are kept.

## Guilds: cost, size and experience bonus

Guilds exist only online. Founding a guild costs {{balance:GuildCreationCost}} gold. A guild holds at most {{balance:MaxGuildMembers}} members and {{balance:MaxBankItems}} items in its bank. Each member gains {{percent:GuildXPBonusPerMember}} percent more combat experience for each member of the guild, at most {{percent:MaxGuildXPBonus}} percent: a guild of 5 or more gives the full bonus.

### Guild ranks

The ranks are Leader, Officer and Member. The Leader and Officers can take gold from the guild bank, an Officer at most {{balance:OfficerGoldWithdrawLimit}} gold in one withdrawal; a Member cannot. Only the Leader removes or promotes members.

## Team size

A team holds at most 5 members, you included. Players on the team count, and so does a dead NPC member until you remove it, unless it can never come back.

## Creating a team

Creating a team at Team Corner costs 500 gold per level of yours, at least 2,000 gold.

## Hiring NPCs and wages

At Team Corner you hire NPCs onto your team for a price and a daily wage, in single-player and online. The price, who can be hired, the recruit pool and the wages are on [party and allies](/wiki/en/combat/party/#hiring-npcs). A hired NPC carries the oath-mark of your banner: see [groups](/wiki/en/combat/groups/#the-oath-mark).

## Raising a fallen team member

Team Corner raises a fallen NPC of your team for 1,000 gold per level of the NPC. A member lost for good, by a permanent death or by old age, cannot be raised.

## Team headquarters

Online, a team can upgrade its headquarters at Team Corner. Each facility goes up to level {{balance:MaxTeamFacilityLevel}}, and the next level costs the facility's base cost times the next level squared. Base costs: Armory 5,000 gold, Barracks 5,000, Training 8,000, Vault 3,000, Infirmary 4,000. Each level gives every player on the team:

- Armory: 5 percent more damage dealt.
- Barracks: 5 percent more defence; the damage you take is divided by 1 plus 5 percent per level.
- Training: 5 percent more combat experience on your own share.
- Infirmary: 10 percent more healing from a healing potion.

## Before grouping

Recruit or invite through the available menu, and check who is actually grouped. An online group holds at most {{balance:GroupMaxSize}} players, the leader and 4 others. Ally stances and who takes orders are on [party and allies](/wiki/en/combat/party/#ally-stances).

Bring complementary actions rather than only damage. Healing, wards and tanking matter when a party is taking different kinds of pressure.

## Conditions remain personal

Grouped players keep their own Mental, Favor, resources and daily limits. A leader's Miracle use does not consume every player's daily Miracle, and a healthy leader does not protect a Broken follower from collapse.

See [groups and support](/wiki/en/combat/groups/) for combat behavior and [party and allies](/wiki/en/combat/party/) for the party itself.
