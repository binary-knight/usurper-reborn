# v0.61.5 -- Beta

Hotfix on top of v0.61.4. Two new features driven by player requests, a comprehensive two-pass localization-gap audit that fixed 15 separate untranslated surfaces a player flagged during Hungarian-session captures, and a small targeted balance pass driven by production combat telemetry.

## Balance: targeted fixes from production telemetry

Player report (Lv.42 Abysswarden): "Abysswarden is too weak, dies too easy, hard to progress with."

Pulled the production `combat_events` data (965 combats across all classes). Overall picture: 97.6% win rate, 12% of combats end in a single round, late-game players (floor 30+) take literal zero damage on most floors. So lowering global difficulty would push the mid/late game further into trivial. But the data did surface two specific items worth fixing now.

**Abysswarden Constitution growth.** The class's data line was actually thriving (98.6% win rate, 1 death in 71 fights, 2nd-highest avg damage dealt of any class at 13,946). But avg damage TAKEN per win was 108 -- 4x higher than Warrior (26), Sage (29), or Magician (24). Only Barbarian (98) was close. Looking at per-level stat growth in `LevelMasterLocation.cs` ApplyClassStatIncreases:

| Class | CON/lv | HP/lv |
| --- | --- | --- |
| Tidesworn | +4 | +13 |
| Cyclebreaker | +3 | +9 |
| **Abysswarden** | **+2** | **+8** |
| Voidreaver | 0 | +5 |

Abysswarden was the lowest CON growth of any prestige melee, despite running an ability kit that costs 25-130 stamina per fire (CON drives stamina regen). The 4x-incoming-damage telemetry confirmed players feel squishier in practice even if death rate stays low because they have the lifesteal kit to compensate. Bumped `BaseConstitution += 2` -> `+= 3` to match Cyclebreaker's CON growth. Existing Abysswarden characters won't retroactively gain stats, but new characters and existing characters' future level-ups benefit. Doesn't touch the damage profile (still 2nd-highest in the game) so doesn't disrupt the kit's identity.

**Giant Spider WebTrap stun chance.** Production telemetry showed Floor 8 Giant Spider at 3 deaths in 5 encounters (60% death rate) vs the same monster at Floor 5 with 0 deaths in 5 encounters. The difference was `WebTrap` -- the spider's signature ability with 45% chance to stun the target for 2 rounds while skipping its own normal attack. At 45%, almost every combat saw a stun proc, and the 2-round stun gave the spider two free hits against players who weren't yet equipped to absorb them. Reduced `StatusChance = 45` -> `30` in `MonsterAbilities.cs:WebTrap`. The stun mechanic is preserved (2 rounds is the signature; reducing duration would gut the ability), but the proc rate drops to one fight in three rather than nearly one in two. Floor-8 spider should drop to roughly the Floor-5 spider's threat level.

**Not fixed in this release** but flagged from the same telemetry pass: late-game monster offense (avg damage taken at floor 30+ is literally 0 on most floors -- monsters can't keep up with player gear), and the slow-grind low-tier champions (Bone Lord 13.3 rounds, Crypt Warden 11.3, Skull Revenant 12, Orc Warchief 15.5) that take 10-15 rounds to grind through at floors 5-7 without meaningful threat. Both warrant a larger pass than this hotfix.

**Investigated and ruled out:** Paladin's 7.5% death rate (vs everyone else at 0-3%) was a single player's bad run at Lv.6 -- all 3 Paladin deaths were the same character (Platinum Paladin), including a Lv-1 Pixie killing them at floor 1 in 1 round (probably a quit-mid-combat artifact). Sample too small to call a class problem.

## NPC behavior: progression-loop unblock

Production `npc_decision_log` telemetry (174,535 decisions across 196 unique NPCs over the v0.61.x window) showed the heuristic NPC AI's progression loop was broken in two compounding ways: low-level teams were dying so fast they never accumulated XP, and the action-weighting picker was starving the progression actions (train / levelup) that convert successful runs into stat gains.

**Team dungeon death rate by NPC level band:**

| Band | Attempts | Wins | Deaths | Death % |
| --- | --- | --- | --- | --- |
| Lv 1-4 | 51 | 0 | 51 | **100%** |
| Lv 5-9 | 1,069 | 7 | 895 | **84%** |
| Lv 10-19 | 3,023 | 6 | 2,310 | **76%** |
| Lv 20-39 | 945 | 0 | 531 | 56% |
| Lv 40+ | 8,742 | 6 | 1,532 | 18% |

The v0.61.2/3 self-preservation gates worked at the high end (40+ band at 18% death, with healthy flee/completed distribution) but didn't help at the low end where teams were getting wiped at 76-100%. The wounded-leader gate (70% HP) and the level-pick formula (`avgLevel - 5 to avgLevel - 3` plus Courage/Ambition push-up) gave low-level teams a target floor that was a coin flip against monsters they couldn't beat.

**Progression-action starvation:** train at 1.1% of actions (1,886 events), levelup at 0.1% (112 events across the whole 196-NPC population), shop at 0.4%, bank at 0%. The v0.61.3 rebalance bumped these weights but the income pipeline upstream was broken: only 19 team_dungeon wins and 199 solo dungeon wins in 174k decisions, so NPCs were gold-starved and XP-starved and couldn't trigger the levelup eligibility check that the weighting reform was designed to favor. Even the busiest "trainer" in the dataset (Hadwin Ravenscroft, Jester) was training 2.6% of his actions across 929 events.

**Six tweaks in `WorldSimulator.cs`:**

1. **New gate 0 in `NPCTeamDungeonRun`:** block team dungeon attempts for NPCs below Lv 5 entirely. They're 100% dying. Routes them to the Inn with the "telling the team they aren't ready yet" activity, logs `aborted_underleveled` for telemetry tracking.
2. **Split team_dungeon level target by team avg level.** Sub-20 teams now pick `avgLevel - 7 + random(0,2)` (was `avgLevel - 5 + random(0,3)` plus Courage/Ambition push-ups). The Courage/Ambition bonus is skipped at low levels because a brave low-level leader just walks into worse monsters with the same low-level kit. 20+ teams keep the v0.61.3 pick.
3. **Wounded-leader HP gate raised 0.7 -> 0.8** in `NPCTeamDungeonRun`. At 71% HP the leader was walking into the AoE damage spread with too little margin; 80% gives a real buffer.
4. **Base `train` weight 0.25 -> 0.50** in `ProcessNPCActivities`. Doubles the picker's preference for the gym; should bring train share to ~2-3% of NPC actions.
5. **Train gold gate 50 -> 20.** The gym charges scale with level; 50g was a real barrier for early-career NPCs with empty pockets. Lowered to 20g so low-level NPCs can still train.
6. **`levelup` weight 0.80 -> 2.0** when XP threshold is met. The 0.80 value v0.61.3 set was still losing to the sum of all other weights when an NPC was eligible; 2.0 makes it dominate the picker so eligible NPCs almost always level up rather than wander to the inn instead.

Expected effect: team dungeon death rate at Lv 1-19 should drop substantially (the meat-grinder bands should look closer to the 40+ band's 18%). Trains per NPC should approximately double. Levelups across the population should jump from 112 to something more in line with the population's accumulated XP. Will validate against the next telemetry pull.

**Not fixed but flagged for follow-up:** 60 of 196 NPCs are stuck at exactly Lv 50, which looks like a starting-level cluster rather than a behavior problem -- worth checking `NPCSpawnSystem` for an immigrant default. The whole `dark_alley` flow (19.6% of NPC actions, zero deaths, +79k gold gained across 34k events) reads like it has no friction at all -- worth a separate audit.



## Feature: examine team members (Team Corner)

Player request: "Need to add a way for players to view stats about each of their team mates, their age, sex, race, class, alignment, attitude, etc, etc."

`TeamCornerLocation.ExamineMember` (`[X] Examine` from the Team Corner menu) was previously a short stat block -- name, HP / MP, level, the six core stats. Useful for combat tuning, useless for the kind of question the player was actually asking ("who is this person on my team, and how do they relate to me and the world?"). The Team Corner is where you decide who to recruit, sack, or promote; the examine view should support that decision, not just confirm their HP.

Expanded to six section blocks rendered in order:

- **Identity.** Class (localized via `GameConfig.GetLocalizedClassName`), specialization role if assigned, race, sex, age, level.
- **Alignment & faith.** Localized alignment label via `AlignmentSystem.Instance.GetAlignmentDisplay(member)` (uses the v0.57.0 paired-movement display), raw Chivalry / Darkness values, faction membership if any, worshipped god if any.
- **Attitude.** Dominant emotion from the NPC's `EmotionalState.GetDominantEmotion()` rendered as a mood word (calm / hostile / friendly / etc.), plus impression-of-player from `member.Brain.Memory.GetCharacterImpression(currentPlayer.Name2)` mapped to a labeled relationship band (loyal / friendly / neutral / wary / hostile).
- **Personality.** Top 3 deviated traits from `PersonalityProfile` (Aggression, Greed, Courage, Loyalty, Vengefulness, Sociability, Ambition, Trustworthiness, Caution) with intensity adverbs (mildly / somewhat / very) based on how far the trait deviates from neutral. Skipped if no Brain / Personality exists.
- **Social.** Marriage status (married to X / unmarried) and number of biological children. Reads `RomanceTracker` for the marriage check and `FamilySystem.GetChildrenOf(member.Name)` for the child count.
- **Combat.** Existing HP / MP / stamina / six-stat block.

37 new `team.examine_*` loc keys covering section headers, trait adverbs, all 9 personality trait labels, the 5 impression states, the 5 alignment surfaces, mood neutral fallback. Translated into all 5 languages (185 new translations total).

## Feature: fallen-comrade inheritance

Player request: "When an NPC dies of old age, if they are on a team, all the equipment should go to the team leader if it is a player."

Pascal-era Usurper let NPC teammates accumulate equipment and gold across world-sim ticks but provided no mechanism for returning that capital to the player when the NPC eventually died of old age in `WorldSimulator.ProcessNPCAging` -- the equipment and gold simply evaporated into the void with the NPC entry. Players who invested time gearing up a recruited NPC teammate had no recourse when the world sim aged them out.

New `pending_inheritance` SQLite table (online mode only):

```sql
CREATE TABLE IF NOT EXISTS pending_inheritance (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    player_username TEXT NOT NULL,
    source_npc_name TEXT NOT NULL,
    item_json TEXT NOT NULL,
    gold_amount INTEGER DEFAULT 0,
    created_at TEXT DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS idx_pending_inheritance_player ON pending_inheritance(player_username);
```

Four new `SqlSaveBackend` methods provide the queue contract:

- `Task<string?> GetTeamLeaderUsername(string teamName)` -- resolves a team name to the player who created it (`player_teams.created_by`), returns null if the team has no player leader.
- `bool QueueInheritance(playerUsername, sourceNpcName, itemJson, goldAmount = 0)` -- inserts one row per item plus one final row for the gold lump sum.
- `List<(long Id, string SourceNpc, string ItemJson, long Gold)> GetPendingInheritance(playerUsername)` -- fetches all pending rows for a player.
- `void ClearInheritance(IEnumerable<long> ids)` -- deletes by row ID after successful delivery.

**Death-side wiring.** `WorldSimulator.ProcessNPCAging` was the only path for old-age NPC death (HP <= 0 from combat already routes through different cleanup). After the existing aging-death block, new `BequeathItemsToTeamLeader(npc)` runs:

1. Skip immediately if not online mode or NPC has no team string.
2. Resolve the team leader via `GetTeamLeaderUsername`. Skip if the team has no player owner.
3. Walk `deceased.EquippedItems` dict. For each equipped slot, resolve the item ID via `global::EquipmentDatabase.GetById(id)`, convert back to a legacy `Item` via the public `Character.ConvertEquipmentToLegacyItem(equipment)` wrapper, serialize via `System.Text.Json` with `JsonNamingPolicy.CamelCase` + `IncludeFields = true` (matching the save backend's existing conventions), queue.
4. Same loop over `deceased.Inventory` for any items the NPC was carrying.
5. Final row queues `deceased.Gold` as a gold-only entry (item_json = null).
6. `DebugLogger.LogInfo` records the total queued count for post-mortem auditing.

**Delivery-side wiring.** `GameEngine.LoadSaveByFileName` already runs `ShowWhileYouWereGone` for online players after world-state restore. New `DeliverPendingInheritance(sqlBackend)` runs right after that:

1. Resolve the current player's DB key via `SessionContext.Current?.Username` (online auth) → `DoorMode.GetPlayerName()?.ToLowerInvariant()` (BBS / online fallback) → `currentPlayer.Name2?.ToLowerInvariant()` (single-player fallback).
2. Fetch all pending rows for that username.
3. Group by `SourceNpc` for narration ("From Aldric: 3 item(s) + 4,200 gold.").
4. Render a "FALLEN COMRADE'S BEQUEST" section with one line per NPC and a totals footer.
5. Deserialize each item, append to `currentPlayer.Inventory` (50-item cap; overflow items stay queued for next login when slots free up, with a yellow "X item(s) could not fit" notice so the player knows).
6. Add the gold sum to `currentPlayer.Gold`.
7. `ClearInheritance(deliveredIds)` removes successfully-delivered rows. Overflow items remain queued.

The whole feature is online-only. Single-player NPCs aren't real persistent entities in the same way (the player save serializes the NPC list directly), so there's no equivalent loss for single-player to fix.

4 new `engine.inheritance_*` loc keys (header, per-NPC line, totals, overflow notice). Translated into all 5 languages.

## Localization: comprehensive gap pass

Player started running localization captures during a Hungarian session and flagged 15 distinct untranslated surfaces across two reports. Each one was its own contained bug; the overall surface area covers combat, romance, marriage, equipment management, NPC display, and the MUD prompt -- enough breadth that fixing them piecemeal would have churned the same files multiple times, so they're all in this release.

### Combat: monster death flavor pool

Player report: "The '* falls in combat!' message is not translated."

`CombatMessages.GetDeathMessage(name, color)` had a 4-variant English pool (`{name} has been slain!` / `collapses in defeat!` / `has been defeated!` / `falls in combat!`) picked at random whenever a monster died. The death-flavor variants were hardcoded in C# string literals. Fix: extracted to `combat.death_msg_1` through `combat.death_msg_4` and made the picker pick the loc key index rather than the string itself. Added `using UsurperRemake.Systems;` so the file can resolve `Loc.Get`. Localized into all 5 languages (20 new translations across the variant pool).

### Combat: victory and multi-kill banners

Player report: "The 'Victory!' message is not translated. Neither double kill triple kill stuff like that."

`CombatMessages.GetVictoryMessage(monstersDefeated)` had hardcoded "Victory!" / "Double kill!" / "Triple kill!" / "Quadra kill!" / "PENTA KILL!" / "{N} MONSTERS SLAIN!" -- six surface variants. Fix: `combat.victory_solo`, `_double`, `_triple`, `_quadra`, `_penta`, `_many`. Same treatment as the death pool.

### Combat: fallen-in-combat broadcasts

Three additional fallen-in-combat / fallen-in-battle sites were hardcoded English in `CombatEngine.cs`:

- Line 1613 (`Char.Combat.End` room broadcast when an online player dies): "{X} has fallen in combat!"
- Line 18621 (companion-death banner displayed to group followers): "═══ {X} has fallen in battle! ═══"
- Line 18648 (inline grouped-player death message): "  {X} has fallen in battle!"

Plus `GameEngine.cs:6987` posted a news event with "{X} has fallen in battle and was carried back to the Inn." -- not strictly the same string but the same shape and the same untranslated bug. All four extracted to `combat.player_fallen_room`, `combat.npc_fallen_battle_banner`, `combat.npc_fallen_battle_inline`, and `engine.news_carried_to_inn`.

### Class names displayed via `xxx.Class` ToString

Player report: "When someone challenges you to a duel ... when they challenge you again with a grudge you do, but that class is not translated. You see like: '8. szintű assassin.'"

The duel-grudge flow at `StreetEncounterSystem.ExecuteGrudgeConfrontation` (line 2241) was already routing the message text through `Loc.Get("street_encounter.grudge.thinks_better", grudgeNpc.Level, grudgeNpc.Class)`, so the surrounding text DID translate. But `grudgeNpc.Class` is a `CharacterClass` enum value, and `string.Format` calls `.ToString()` on it implicitly -- producing the raw English enum name ("Assassin" / "MysticShaman"). Hungarian players saw "8. szintű Assassin" -- Hungarian level prefix plus English class name.

Same bug -- the v0.61.4 audit found 14 sites in `SaveInfo.ClassName` display and fixed the character-select screen via `GetLocalizedClassNameFromString`, but a programmatic audit for the wider pattern (`Loc.Get(..., x.Class)`) caught 20 more sites this release was missing:

| Site | File |
| --- | --- |
| Hostile encounter stats display | StreetEncounterSystem.cs |
| Generic NPC challenge info | StreetEncounterSystem.cs |
| Grudge "thinks better" | StreetEncounterSystem.cs |
| Grudge enraged stats banner | StreetEncounterSystem.cs |
| Witness stats (2 sites) | StreetEncounterSystem.cs |
| Throne challenger confronts | StreetEncounterSystem.cs |
| Arena opponent face-off | AnchorRoadLocation.cs |
| Prisoner info display | AnchorRoadLocation.cs |
| Love Street partner listing | LoveStreetLocation.cs |
| Love Street gossip listing | LoveStreetLocation.cs |
| Inn NPC class line | InnLocation.cs |
| **Crystal ball NPC entry** | LevelMasterLocation.cs |
| **Crystal ball class reveal** | LevelMasterLocation.cs |
| Home equip NPC level | HomeLocation.cs |
| Home equip target stats | HomeLocation.cs |
| Main Street attack target info | MainStreetLocation.cs |
| Dungeon mercenary class | DungeonLocation.cs |
| Castle mercenary entry | CastleLocation.cs |
| Castle mercenary role line | CastleLocation.cs |
| Castle siege player display | CastleLocation.cs |
| Team Corner examine level | TeamCornerLocation.cs |
| Ending final-stats screen | EndingsSystem.cs |
| Ending legacy hero description | EndingsSystem.cs |
| Loot drop spell-req warning | CombatEngine.cs |
| Item cannot-equip class reason (x2) | Items.cs |

All 20 sites now wrap the `.Class` argument with `GameConfig.GetLocalizedClassName(...)`. The same audit caught the crystal ball entries (player report #8 from the first capture: "The class names in the crystal ball are not translated") so that's covered in the same pass.

### Child interactions: rename and spend time

Player report: "When you spend time with your kid, the two options like rename and spend time are not translated."

`HomeLocation.cs:1971-1973` had hardcoded "Spend time" / "Rename {child.Name}" menu labels, plus the rename flow's "Current name:", input prompt, success message, and "Name unchanged" line. Six new `home.child_*` keys covering all six surfaces.

### Equip displacement messages

Player report: "When you equip stuff, the 'moved x to inventory' message is not translated."

`Character.EquipItem` had 13 hardcoded English strings: "Moved {X} to inventory.", "Moved {X} to off-hand.", "Equipped {X} in main hand", "Equipped {X} in {slot}", plus five validation errors (one-handed-only, two-handed-must-mainhand, shields-must-offhand, rings-finger-only, item-belongs-in-slot) and one fallback ("your two-handed weapon"). All extracted to `equip.moved_to_inventory` / `equip.moved_to_offhand` / `equip.equipped_in_slot` / etc.

### Equipment slot names in compare window

Player report: "When you find an equipment, the comparing window doesn't translate the equipment slots. I see stuff like: 'druidah main hand helye.' or 'Druidah body helye.'"

The Hungarian loc key `combat.comparison_header` is `"{0} {1} helye."` ({0} = character name, {1} = slot name). The slot name was being constructed via a hardcoded English switch in `CombatEngine.ShowEquipmentComparison` (lines 10338-10346) that mapped EquipmentSlot enum values to "Main Hand", "Off Hand", "Left Ring", "Right Ring", "Necklace", and for all other slots fell through to `targetSlot.ToString()` -- so Hungarian players saw "druidah main hand helye." (Hungarian template, English slot name).

Fix: new `GameConfig.GetLocalizedSlotName(EquipmentSlot)` helper reading `equip.slot.{Slot}` keys. 16 slot keys defined (all `EquipmentSlot` enum values including `None` and `Neck2` for forward-compat). The switch in `ShowEquipmentComparison` collapsed to a single call to the helper. Same helper now also feeds the `equip.rings_finger_only` / `equip.item_belongs_in` validation messages in `Character.EquipItem`, so slot names are consistent across the compare-window UI and the equip error messages.

### Location names in MUD prompt

Player report: "The location names in the status bar are not translated."

MUD clients (Mudlet, TinTin++, etc.) get a streaming prompt at the bottom of the screen like `[100hp 50mp] Inn | look to redraw > `. The location name (`Inn`, `Anchor Road`, `Magic Shop`, etc.) came from `BaseLocation.GetMudPromptName()` -- a virtual method overridden in 12 location subclasses as hardcoded English literals (`=> "Anchor Road"`, `=> "Magic Shop"`, etc.). The default implementation in `BaseLocation` stripped "Location" from the class name (e.g. `InnLocation` → "Inn"), also English.

Fix: `GetMudPromptName()` in the base class now defaults to `GetLocationName(LocationId)`, which since v0.61.2 routes through `Loc.Get($"location.name.{LocationId}")` -- and every `GameLocation` enum value already has a fully-localized `location.name.*` key in all 5 languages. The 12 hardcoded overrides were redundant duplication; removed. Only `DungeonLocation` keeps an override, since the dungeon prompt has a dynamic floor-number suffix; rewritten to use new `dungeon.mud_prompt` loc key (`"Dungeon Fl.{0}"` in English, `"Tömlöc {0}. szint"` in Hungarian, etc.).

### Chat-topic player lines

Player report: "Some of the deep talk options aren't translated. I see stuff like 'where are you from originally?' I think these are only the first slot options, the others are good."

`VisualNovelDialogueSystem.BuildAvailableChatTopics` builds a dynamic list of chat-topic options the player can pick during a conversation. The list is filtered by NPC class (warriors get weapon questions, mages get spell questions, etc.) and personality (high-Romanticism NPCs unlock "Do you believe in true love?"). The player's selected option becomes the `Text` displayed in the conversation menu via `ConversationOption.Text = chatTopic.PlayerLine`. All 28 `PlayerLine = "..."` values were hardcoded English literals.

The player's diagnosis ("first slot options") was correct -- `GetPersonalQuestion` and `GetFlirtLine` (which feed the second and third menu slots) were already routed through `Loc.Get("dialogue.vn.personal.*")` and `Loc.Get("dialogue.vn.flirt.*")` keys, but the chat-topic slot was raw English.

Fix: programmatically rewrote each `PlayerLine = "English text"` to `PlayerLine = Loc.Get("dialogue.vn.chat_topic.{TopicId}")` -- the `TopicId` is already on the `ChatTopic` struct and provides a natural keying scheme (`warrior_training`, `mage_magic`, `origins`, `hobbies`, etc.). 28 new English keys + 28 each in hu / es / fr / it (140 translations).

### Compliment / confession / kiss / proposition narrative lines

Three more player reports surfaced 12 distinct hardcoded English narrative lines across `VisualNovelDialogueSystem`'s `HandleComplimentOption` / `HandleConfessionOption` / `HandleIntimateOption` / `HandlePropositionOption` methods:

- `dialogue.compliment_reply` ("That's very kind of you. Thank you.")
- `dialogue.confession_reciprocate` (`*takes your hand* "I... I've felt the same way. I was afraid to say it."`)
- `dialogue.confession_need_time` ("I... I don't know what to say. This is sudden. Can I think about it?")
- `dialogue.confession_rejected` ("I'm flattered, truly. But... I don't feel the same way. I'm sorry.")
- `dialogue.kiss_part` (`*After a long moment, you reluctantly part*`)
- `dialogue.kiss_never_tire` ("I never tire of that...")
- `dialogue.first_kiss_prose_1` / `_2` / `_reaction` -- the three-line first-kiss prose block
- `dialogue.kiss_pushed_away` / `dialogue.kiss_slow_down` -- the first-kiss-rebuff branch
- `dialogue.prop_accept_takes_hand` (`*takes your hand* "I thought you'd never ask..."`)
- `dialogue.prop_too_early` ("That's... forward. We're not at that point yet.")

The audit also caught 4 spouse-jealousy lines (back-out / vows / not-ready / afraid) in `HandleSpouseSituationOption` and 2 provocation reactions (threat / dismiss) in `HandleProvocationOption` that were the same shape.

### Hungarian pronoun bleed in intimate scenes

Player report: "The pronouns are bleeding through in to the translation in sexual encounters: 'She feléd fordul.' My suggestion, at least for Hungarian, is to remove the pronouns. We don't really use them. Not in this context."

`IntimacySystem.cs` had 13 method bodies each declaring `string gender = partner.Sex == CharacterSex.Female ? "she" : "he";` (or capitalized `genderCap`, or possessive `their`), then passing those English values as format args into `Loc.Get("intimacy.X", ...)` calls. For `intimacy.anticipation_turns` specifically, the Hungarian loc key is `"{0} feléd fordul, szemei sötétek a vágytól."` and `{0}` was being filled with the English "She" or "He" -- producing "She feléd fordul..." in a Hungarian session.

Architectural fix:

1. **Localized pronoun helpers in `GameConfig`.** `GetLocalizedSubjectPronoun(CharacterSex sex)` returns the localized subject pronoun from `ui.pronoun_subject_female`/`_male`. Same shape for `GetLocalizedPossessivePronoun`. Hungarian / Spanish / Italian return empty strings (pro-drop languages don't use a subject pronoun -- verb conjugation carries the subject). French returns `"Elle"` / `"Il"` (French requires a subject pronoun). English returns `"She"` / `"He"` (unchanged).
2. **Sanitize helper.** `GameConfig.CleanFormat(raw)` collapses runs of whitespace to a single space, strips leading whitespace, and capitalizes the first letter. So a Hungarian translation `"{0} feléd fordul..."` with `{0}` substituted as `""` produces `" feléd fordul..."` → cleaned to `"Feléd fordul..."`. Same handles ` {1} fejét` (possessive in middle) → ` fejét` after collapse.
3. **Wrap every `Get("intimacy.X", ...)` call.** A balanced-paren-aware script wrapped 145 call sites with `GameConfig.CleanFormat(Get("intimacy.X", ...))`.
4. **Pronoun assignments.** All 20 `gender`/`genderCap`/`their` declarations across 8 method bodies in `IntimacySystem.cs` now use `GameConfig.GetLocalizedSubjectPronoun(partner.Sex)` / `GetLocalizedPossessivePronoun(partner.Sex)` (with `.ToLowerInvariant()` where the legacy variable was lowercase).

Net effect: English sessions render `"She turns to face you..."` (unchanged). Hungarian sessions render `"Feléd fordul..."` (no pronoun, capitalized verb). French sessions render `"Elle se tourne vers toi..."` (localized pronoun). Spanish / Italian (pro-drop) render `"Se gira para mirarte..."` / `"Si volta verso di te..."` with the leading space collapsed away.

4 new `ui.pronoun_*` loc keys per language.

### Marriage proposal NPC reactions

Player report: "When you ask someone to marry their reaction is not translated."

`HandleProposalOption` had three branches (accepted / not-ready-yet / rejected) and each branch had 1-2 hardcoded narrative lines:

- Accepted: `*{name} throws their arms around you*` + `"YES! A thousand times YES! I will marry you!"`
- Not ready: two-line spoken response about not being ready for marriage yet.
- Rejected: two-line spoken response about not being able to commit.

6 new `dialogue.propose_*` keys covering all three branches.

### "The kingdom celebrates your union!" + wedding ceremony pool

Player report: "The message 'The kingdom celebrates your union!' is not translated."

`GameConfig.WeddingCeremonyMessages` was a 10-entry `string[]` of hardcoded English variants ("The priest says a few holy words and you are married!", etc.) read by 4 separate call sites (ChurchLocation, LoveCornerLocation, RelationshipSystem, VisualNovelDialogueSystem). The array stays as the English fallback; new `GameConfig.GetWeddingCeremonyMessages()` helper returns the array with each entry resolved via `Loc.Get($"wedding.ceremony_msg_{i + 1}")` -- so the call sites get a localized array for the active session language with English fallback per-entry. All 4 callers migrated.

Same fix folded in `ChurchLocation.cs:1060` which built a news entry with hardcoded `"Wedding Bells"` title, `"{player} married {npc} in a beautiful ceremony!"` body, and `"The whole kingdom celebrates this union!"` footer -- extracted to `church.wedding_news_title` / `_body` / `_footer`.

10 wedding ceremony keys + 3 news keys = 13 new translations per language.

### Home rest status "today"

Player report: "The status message which shows your rest numbers at home is not properly translated. It shows: 'Pihenés: 1/1 today (25%) | Italok: 23'. Either remove today or translate it as 'ma'."

`HomeLocation.cs:176` had `terminal.Write($"{restsLeft}/{maxRests} today ({recoveryPct}%)")` -- the surrounding labels (`Pihenés:`, `Italok:`) were already localized, but the body of the line had a hardcoded English "today". Extracted to `home.stat_rest_remaining` with format args `{0}/{1}` rest counts and `{2}` recovery percent. Hungarian renders as `"{0}/{1} ma ({2}%)"`.

## Localization roll-up

- **141 new English keys** added across the audit.
- **115 keys** translated into each of hu / es / fr / it (the 16 `ui.pronoun_*` and `equip.slot.None`/`Neck2` keys differ per language by design, so they're not a 1:1 translation count).
- **All 5 languages** verified to have matching format-arg placeholder sets via a regex-based audit script.
- **Zero em-dashes (U+2014)** introduced in any new key (per project convention -- two ASCII hyphens always).
- Existing translations untouched.

## Files changed

- `Scripts/Core/GameConfig.cs` -- new `GetLocalizedSlotName(EquipmentSlot)`, `GetLocalizedSubjectPronoun(CharacterSex)`, `GetLocalizedSubjectPronounCap(CharacterSex)`, `GetLocalizedPossessivePronoun(CharacterSex)`, `CleanFormat(string)`, `GetWeddingCeremonyMessages()` helpers. Version bumped 0.61.4 -> 0.61.5.
- `Scripts/Locations/LevelMasterLocation.cs` -- Abysswarden `BaseConstitution += 2` -> `+= 3`.
- `Scripts/Systems/MonsterAbilities.cs` -- WebTrap `StatusChance = 45` -> `30`.
- `Scripts/Systems/WorldSimulator.cs` -- NPC behavior: `NPCTeamDungeonRun` gets Lv-5 gate + split level-target formula + wounded-leader HP gate 0.7->0.8; `ProcessNPCActivities` train weight 0.25->0.50, train gold gate 50->20, levelup weight 0.80->2.0.
- `Scripts/Core/Character.cs` -- 13 hardcoded equip messages (`Moved X to inventory`, validation errors, `Equipped X in {slot}`) replaced with `Loc.Get` + slot helper.
- `Scripts/Core/GameEngine.cs` -- new `DeliverPendingInheritance(SqlSaveBackend)` method called from `LoadSaveByFileName` after `ShowWhileYouWereGone`. `engine.news_carried_to_inn` loc fix.
- `Scripts/Systems/SqlSaveBackend.cs` -- new `pending_inheritance` table + 4 backend methods (`GetTeamLeaderUsername`, `QueueInheritance`, `GetPendingInheritance`, `ClearInheritance`).
- `Scripts/Systems/WorldSimulator.cs` -- new `BequeathItemsToTeamLeader(NPC)` method called from `ProcessNPCAging` old-age branch.
- `Scripts/Systems/CombatMessages.cs` -- `GetDeathMessage` / `GetVictoryMessage` route through Loc.Get keys; `using UsurperRemake.Systems;` added.
- `Scripts/Systems/CombatEngine.cs` -- 4 fallen-in-combat / fallen-in-battle broadcasts localized; `ShowEquipmentComparison` slot dictionary collapsed to `GetLocalizedSlotName`; spell-req warning wraps `.Class` with localized helper.
- `Scripts/Systems/StreetEncounterSystem.cs` -- 6 sites wrap `.Class` with `GetLocalizedClassName`.
- `Scripts/Systems/VisualNovelDialogueSystem.cs` -- 28 `PlayerLine` literals routed through `dialogue.vn.chat_topic.{TopicId}`; 12 narrative dialogue lines + 6 proposal reactions + 4 spouse-jealousy lines + 2 provocation lines extracted to `dialogue.*` keys; wedding ceremony messages migrated to `GetWeddingCeremonyMessages()`.
- `Scripts/Systems/IntimacySystem.cs` -- 20 pronoun assignments wired through localized helpers; 145 `Get("intimacy.X")` calls wrapped with `GameConfig.CleanFormat`.
- `Scripts/Systems/EndingsSystem.cs` -- 2 sites wrap `.Class` with localized helper.
- `Scripts/Core/Items.cs` -- 2 `cannot_use_class` messages wrap `.Class`.
- `Scripts/Locations/BaseLocation.cs` -- `GetMudPromptName()` default routes through `GetLocationName(LocationId)`.
- `Scripts/Locations/DungeonLocation.cs` -- `GetMudPromptName` uses `dungeon.mud_prompt` loc key; one site wraps `.Class`.
- `Scripts/Locations/TeamCornerLocation.cs` -- `ExamineMember` expanded to six section blocks; class wrap.
- `Scripts/Locations/HomeLocation.cs` -- child interaction options localized; rest-status "today" localized; 2 sites wrap `.Class`.
- `Scripts/Locations/ChurchLocation.cs` -- wedding news entry routed through `church.wedding_news_*` keys; ceremony pool migrated.
- `Scripts/Locations/LoveCornerLocation.cs` / `Scripts/Systems/RelationshipSystem.cs` -- wedding ceremony pool migrated.
- `Scripts/Locations/AnchorRoadLocation.cs` / `LoveStreetLocation.cs` / `InnLocation.cs` / `LevelMasterLocation.cs` / `MainStreetLocation.cs` / `CastleLocation.cs` -- 12 sites wrap `.Class` with localized helper; 12 `GetMudPromptName` overrides removed.
- 12 location subclass `.cs` files -- removed redundant `GetMudPromptName() => "<English>"` overrides.
- `Localization/en.json` -- 141 new keys total (chat-topic player lines, equip messages, slot names, pronoun helpers, death / victory pools, dialogue narrative lines, proposal reactions, wedding ceremony pool, wedding news, home rest status, MUD prompt, inheritance, team examine, child interactions).
- `Localization/hu.json` / `es.json` / `fr.json` / `it.json` -- matching translations.
