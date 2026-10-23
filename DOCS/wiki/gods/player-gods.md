---
title: Immortals, player-gods and the Pantheon
path: /wiki/en/gods/player-gods/
checked: 1.3.6
sources: Scripts/Systems/EndingsSystem.cs, Scripts/Locations/PantheonLocation.cs, Scripts/Locations/TempleLocation.cs, Scripts/Systems/GodBoonSystem.cs, Scripts/Systems/GodDomainPicker.cs, Scripts/Systems/DivineBoonRegistry.cs, Scripts/Systems/ImmortalDeedSystem.cs, Scripts/Systems/DailySystemManager.cs, Scripts/Systems/WeeklyGodSystem.cs, Scripts/Systems/FaithSystem.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/LocationManager.cs, Scripts/Locations/BaseLocation.cs, Scripts/Core/GameEngine.cs, Scripts/Server/MudChatSystem.cs, Scripts/Systems/TeamSystem.cs, Scripts/Systems/GuildSystem.cs, Scripts/Systems/SqlSaveBackend.cs, Scripts/Core/GameConfig.cs, DOCS/release-notes/RELEASE_NOTES_1.2.0.md
history: 1.3.6 | none
history: 1.3.5 | none
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | none
history: 1.3.1 | none
history: 1.3.0 | none
history: 1.2.7 | none
history: 1.2.6 | A new divine name cannot contain < > & or a double quote; divine names chosen before are kept.
history: 1.2.5 | Pantheon screens are shown in all five languages, and proclamations, blessings and renounce notices reach each player in that player's own language.
history: 1.2.4 | Renouncing releases player followers, who are told; an alt is refused ascension before it gives up its throne, team or guild; the alt slot stays after renouncing; Status shows the believer experience the game pays.
---
An immortal is a player who finished the main story and chose to ascend instead of starting a new life. The character becomes a player-god: it lives in the Pantheon, gathers believers, performs divine deeds and grants a domain boon to the players who worship it. Mortals find player-gods on the same Temple altar list as the ten Temple gods.

## How to become an immortal

Immortality is offered only once, at the end of the main story. After the final encounter, the ending and the credits, the game offers two choices in turn:

1. **Ascend to godhood.** Answer Y to "Ascend to godhood?".
2. **Begin the Eternal Cycle.** This is [New Game Plus](/wiki/en/characters/new-game-plus/): a new character on the next cycle.

You must take one of them. If you decline both, the game asks again; there is no return to ordinary mortal play with the finished character. If the connection drops before you choose, the choice is offered again at your next login.

When you ascend:

- You choose a divine name of 3 to 30 characters, without < > & or a double quote. It is the name other players see from now on.
- Your alignment as a god comes from the ending you reached: Light, Dark or Balance.
- You choose a domain, one of the ten boon domains of the Temple gods. Press Enter to decide later; the Pantheon asks again on each visit until you choose. A domain is permanent.
- If you were the monarch, you abdicate the throne. You leave your team and your guild.
- The account's alt character slot opens, and stays open for good.
- The ending counts as completed and your cycle count goes up, as it would for NG+. This keeps prestige classes and cycle bonuses for a later life.
- You go straight to the Pantheon.

Online, only your main character can ascend. An alt character is refused right after it answers Y, before it chooses a name, so it keeps its throne, team and guild. Once your main character has ascended, your account can create an alt character, also after a renounce.

## What changes for an immortal

An immortal never returns to the mortal world. Every login starts in the Pantheon, and any route to another location leads back there. There is no town, Dungeon, shop, Inn, team, guild or combat for the god itself. Q in the Pantheon leaves the game.

Online, other players see the god under its divine name followed by its rank, for example "the Lesser Spirit". On /who a player-god's row is marked with a star (the words "(Immortal)" with screen reader mode), its level column shows its god rank instead of a class, gods are listed after the admins and before mortals, and the summary line counts the immortals online.

### God rank

A god has {{balance:GodMaxLevel}} ranks, from Lesser Spirit to God. Every new immortal starts as a Lesser Spirit at rank 1. The divine experience each rank needs, the deeds it grants a day and its Favors budget:

{{table:god-ranks}}

The rank rises when you visit Manwe (V) with enough experience, one rank per visit, and that visit also refills your deeds to the new rank's daily count. The daily reset also raises the rank to the highest one your experience has reached. At the top rank Manwe has nothing more to teach.

### How a god gains divine experience

- **Believers:** at each daily reset every believer adds your rank x {{balance:GodBelieverExpPerLevel}} experience. 12 believers at rank 3 give 72 a day.
- **Divine deeds:** each deed shows what it earned (below).
- **Player followers' fights:** online, {{percent:GodBelieverKillXPPercent}} percent of the combat experience each player follower earns, at least 1, shown to them as a sacrifice to you.
- **Gold and prayer at your altar:** online, a mortal's gold offering at your Temple altar adds 1 to 6 experience by its size: 1 up to 20 gold, 2 up to 2,000, 3 up to 45,000, 4 up to 150,000, 5 up to 900,000 and 6 above. A follower's daily prayer adds 10.

## The Pantheon

The Pantheon menu:

- **S, Status.** Divine name, mortal name, alignment, domain and its boon scale, rank, experience to the next rank, believers, deeds left today, ascension date, configured boons.
- **B, Believers.** Your followers, NPCs and players, with level and class.
- **D, Divine Deeds.** The deed menu, below.
- **F, Favors.** Configure the extra boons your followers receive.
- **I, Immortals.** All player-gods ranked by divine experience.
- **N, News.** Today's news, mortal and divine.
- **C, Comment.** A message to the news; costs no deed.
- **V, Visit Manwe.** Rise a rank when your experience allows.
- **H, Hall of the Ascended.** The founder statues.
- **R, Renounce.** Give up immortality and start a new life.
- **Q, Quit.** Leave the game.

### Standing and rankings

A god's standing is the sum of its followers' Favor, plus {{balance:GodNpcFollowerStanding}} for each living NPC follower. Each desecration of its altar lowers it by {{balance:GodDesecrationStandingPenalty}} until the next weekly reset, every {{balance:GodStandingWeekDays}} days; standing never goes below 0. Temple gods and player-gods share the Temple's rankings and altars. The Immortals screen (I) ranks player-gods only, by divine experience, with rank, believers and whether each god is online.

At each weekly reset the god with the highest standing becomes the god of the week, and its followers gain {{balance:GodWeeklyXpBonusPct}} percent more combat experience until the next weekly reset. A player-god can hold that place like any Temple god.

These values change with the live world. The wiki does not export player names, follower lists or standings.

### Divine deeds

Each deed costs one of the day's deeds, which refill at the daily reset.

- **Recruit Believer.** Try to convert a mortal. A pagan, or an NPC only loosely devoted to its god, converts {{percent:GodRecruitPaganChance}} percent of the time for {{balance:GodRecruitPaganExp}} experience; a believer of another god converts 25 percent of the time for {{balance:GodRecruitStealExp}}. A player is harder to convert and pays more: the chance is {{balance:GodRecruitPlayerMultiplier}} times as high (24.75 and 18.75 percent) and the experience {{balance:GodRecruitPlayerExpMultiplier}} times (300 and 100). A failed try still uses the deed.
- **Bless Follower.** One of your followers gains a combat blessing for {{balance:GodBlessCombatDuration}} fights: {{percent:GodBlessBonusFollower}} percent more attack and defense for a Follower, {{percent:GodBlessBonusDevout}} for a Devout, {{percent:GodBlessBonusZealot}} for a Zealot or Chosen and {{percent:GodBlessBonusNpc}} for an NPC. A stronger blessing still running keeps its bonus. A player follower also gains {{balance:GodBlessFavorGain}} [Favor](/wiki/en/gods/favor/), at most {{balance:GodBlessFavorDailyCap}} a day. The bless gives you {{balance:GodBlessExp}} experience, {{balance:GodBlessPlayerExpMultiplier}} times as much (30) for a player.
- **Smite Mortal.** Strike a mortal who does not follow you for a random {{percent:GodSmiteMinPercent}} to {{percent:GodSmiteMaxPercent}} percent of their maximum HP, for {{balance:GodSmiteExp}} experience, {{balance:GodSmitePlayerExpMultiplier}} times as much (50) for a player. A smite never kills: the target keeps at least 1 HP. You cannot smite your own followers, and the same player can be smitten again only after {{balance:GodSmitePlayerCooldownMinutes}} minutes.
- **Poison Relations.** Worsen the relationship between two NPCs. It works {{percent:GodPoisonRelationshipChance}} percent of the time, for {{balance:GodPoisonRelationshipExp}} experience.
- **Free Prisoner.** Release an imprisoned NPC, for {{balance:GodFreePrisonerExp}} experience.
- **Proclamation.** Send a message to the news and, online, to every player in the game. Unlike Comment, it costs a deed and gives {{balance:GodProclamationExp}} experience.
- **Chastise Follower.** Take {{balance:GodChastiseFavorLoss}} Favor from one of your own player followers, once a day for each follower. It gives no experience.

A deed on a player follower who has left you is refused and costs nothing. A bless or chastise also reaches a player who is offline: it changes their save and the message arrives as mail. If their save cannot be reached at that moment, the deed is refused and costs nothing.

### Domain boon

Your followers receive the boon and Mental ward of your domain, the same mechanics as the Temple god of that domain: see [boons](/wiki/en/gods/boons/). Your Favor tiers apply as usual, and only Devout followers and above get the ward.

The boon's strength is a share of the Temple god's boon at the same tier: your standing from players as a percent of the strongest Temple god's, held between {{balance:GodPlayerBoonFloorPct}} and {{balance:GodPlayerBoonCapPct}} percent. A player-god with half the standing of the strongest Temple god grants 50 percent; one with more standing than every Temple god grants up to 120 percent. When no Temple god has player followers, a god with standing is at 120 percent and one without at 50. Status (S) shows the current share.

The boon fades with inactivity. After {{balance:GodPlayerInactiveDays}} days without logging in, the share drops {{balance:GodPlayerInactiveDecayPctPerDay}} points for each further day away, never below {{balance:GodPlayerBoonFloorPct}} percent. It recovers when you log in again.

### Favors

Favors (F) adds extra boons on top of the domain boon. There are 13 Favors in three groups, each with 3 tiers; a tier costs its points each, so a third tier costs three times the points of the first. Some are open only to a god of a Light, Dark or Balance alignment. Your budget is {{balance:GodBoonBudgetPerLevel}} points for each god rank, plus a concentration bonus of up to {{balance:GodBoonConcentrationMax}} points that shrinks by {{balance:GodBoonConcentrationPerBeliever}} for each believer, so a small flock gets stronger gifts. A rank 3 god with 4 believers has 30 + 12 = 42 points. Type a number to add or upgrade a boon, R and its number to remove one, and 0 or Enter to finish. Online, followers who are playing see the change at once.

{{table:divine-boons}}

A prayer at your altar doubles the larger of a follower's damage and defense Favor for a blessing of {{balance:GodPrayerBlessingCombats}} fights; see [the daily prayer blessing](/wiki/en/gods/favor/#the-daily-prayer-blessing).

## Online and single-player

Ascension works the same way in both modes. The differences are in who can follow you:

- Single-player, your believers are NPCs. The Immortals screen lists only you, deeds that target players are not available, and no player receives your domain boon or Favors.
- Online, players can worship you at the Temple, receive your domain boon and Favors, and feed you a share of their combat experience. Other immortals appear in the rankings, and your ascension is announced to everyone online.

## Renounce immortality

R in the Pantheon gives up godhood for good. You confirm twice: type YES, then type your divine name exactly.

What is lost:

- the divine name, rank, experience and deeds;
- your believers, NPCs and players, who are left without a god. A player follower in the game is told at once; one who is not is told by mail;
- the character itself: its save is replaced by a brand new character, as in NG+.

What is kept:

- your cycle count and completed endings, so the new life starts with the cycle bonuses of [New Game Plus](/wiki/en/characters/new-game-plus/);
- the account's alt character slot.

If you are the monarch when you renounce, you abdicate first; nothing is renounced if the abdication fails. The new life starts on the next cycle with the Old Gods reset, the same way NG+ starts.

:::spoiler Spoiler: endings and divine alignment
The Savior ending gives the Light alignment, the Usurper ending gives Dark, and the Defiant and True endings give Balance. On renounce, the ending recorded for the next cycle follows the same mapping back: Light counts as Savior, Dark as Usurper, Balance as Defiant.

The secret Dissolution ending offers neither choice. It deletes the save and records only the ending in your lifetime progress.

At the offer, Manwe asks whether you will transcend mortality and become a god, and lists what a god does: appear in the Temple for mortals to worship, manage followers and perform deeds, compete with other gods for believers, and rise through the divine ranks.
:::
