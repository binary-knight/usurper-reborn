# v0.63.0 -- Bloodlines

## TL;DR

- Adult children remember who their parents are. You can see them at Home, talk to them on the street, examine them at Team Corner, and recruit them to your team at a steep discount.
- A one-time recognition moment plays the first time you cross paths with one of your grown kids. The tone shifts based on how you raised them.
- Five new conversation topics for adult children: "Remember when you were small?", "How is your mother / father?", "What's your trade now?", and others. Each one has different responses depending on how you raised the child.
- Family overrides everything. A child you raised won't flee from you even if you've earned a Dread reputation that makes lesser NPCs scatter on sight. Romance verbs are hidden from the menu entirely for adult children, not just refused when you press them.
- New "Dynasty" reputation track derived live from your adult children: Founder (1+), Patriarch / Matriarch (3+), Bloodline (5+ all consistent alignment), Dynasty (5+ with grandchildren). Shows on `[%]` Status next to Dread / Renown / Sellsword Hall. Each tier grants a reaction bonus.
- Inheritance on permadeath. When your run ends for good, before the cinematic wipes you from the world, 50% of your gold and an XP determination buff pass to your eldest adult child. A news entry posts. You see one last cinematic line acknowledging the legacy.
- New royal action `[Y] Sponsor Heir to Court` at the Castle lets a player-king elevate an eligible grown child into a court position. Track depends on alignment: Faith ordination (virtuous), Shadow Spymaster (evil), or Crown Advisor (neither). Costs 50 Fame.
- New `[Z] Bloodlines of the Realm` at the Castle shows the top 10 family lines in the world by descendant count. Read-only, visible to anyone.
- NPC children remember if you killed their parent. They refuse to talk to you, refuse to join your team, and attack on sight if dark-aligned.
- NG+ carries forward. Each adult child you raised to level 20 grants +5 starting Charisma in your next character, capped at +25.
- Incest gate added at every marriage, intimacy, and pregnancy entry point. Eight code paths covered: the Church marriage flow, Castle royal proposals, Love Corner, dialogue marriage proposals, pregnancy checks, NPC compatibility checks, and the affair-partner picker in the world simulator. Each refusal is localized with a relation-specific flavor line.
- The Castle royal marriage flow was bypassing every guard (no age check, no already-married check, no incest check). Now refuses up-front for any of those.
- Love Corner's "name doesn't resolve to an NPC" fallback used to create a phantom marriage to a raw text string with no real spouse behind it. Now refunds and refuses.
- Three pre-existing graduation bugs fixed in passing: adult NPCs were spawning with all stats secretly set to 0 (zeroed by the first stat recalculation because the Base values were never set), graduated Magicians had 0 mana so their spells fizzled, graduated female orphans rolling the same-sex orientation band stayed mislabeled "Gay" because the orientation reconciliation step never ran.

---

## What it means at the table

A long-cycle player who raised four or five kids across years of play sees them graduate at 18 and disappear into a sea of indistinguishable NPCs. The Soul rating you shaped over hundreds of parenting choices vanishes. The kid you raised right doesn't recognize you when you cross paths. The kid you raised wrong doesn't bear you a grudge. There's no payoff at the end. There's no story to tell.

This release makes that payoff exist. The next time you walk into Main Street as a long-cycle player, you might run into one of your kids and have a reunion scene. You might talk to them about what they remember of growing up. You might look at the `[%]` Status sheet and see "Patriarch -- 4 grown children walk the world." You might die at level 60 and have the news feed announce that your son inherited your estate, instead of just announcing you died.

The simulation was already doing the work. It just wasn't telling anyone.

---

## The reunion moment

The first time you walk into `[T] Talk` with one of your grown children, a one-time cinematic plays before the normal conversation begins. The scene is keyed to the child's Soul at graduation -- a snapshot taken the moment they turned 18, so it reflects the parenting choices you actually made, not whatever alignment drift they picked up as an adult.

If you raised them virtuously, the reunion is warm. *"You see them across the street and the years collapse. The little hand you used to hold became this hand."* They speak first: *"Father... mother... you came back."*

If you raised them neutral, it's bittersweet. *"It takes a moment. They've grown into a stranger's face -- and then the eyes settle on you, and you know each other again."* They speak: *"It's been a long time. You look older. We both do."*

If you raised them evil, it's cold. *"You recognize them a half-beat before they recognize you. Whatever you taught them about the world, they learned it well. Their eyes are colder than you remembered."* They speak: *"So. You're still alive. I wondered if I'd ever see you again."*

After the cinematic, the relationship seeds at a parental band so the next time you talk it isn't stranger-grade. The cinematic plays exactly once per child per cycle. It survives save and reload. NG+ resets it so the next life meets its kids fresh.

Family overrides Dread. A dark-aligned player with a Nightmare-tier reputation that makes lesser NPCs flee on sight will not make their own kid flee. The recognition check runs before the Dread flee check. Family wins.

---

## Adult-child conversation

When you talk to one of your grown children, the Chat slot swaps to a parent-flavored topic pool. You can ask:

- "Remember when you were small?"
- "How is your mother / father?"
- "What's your trade now? How do you fill your days?"
- "Are you well? Truly well?"
- "Do you need anything? Coin, a word in the right ear?"

Each topic has three tone-keyed responses and two variants per tone, so you cycle through plenty of unique answers before any repeat. A virtuously-raised daughter answering "Remember when you were small?" might say: *"I remember everything. Even the things you don't think I do. The lullabies. The way you used to read by candlelight when you thought I was asleep."* A child raised toward evil might give a short laugh with no warmth in it: *"Oh, I remember. Don't worry about that."*

The flirt, intimate, proposition, propose, and confess menu options are hidden entirely when you're talking to an adult child. Not just refused if you press them: removed from the menu so they aren't an option at all. The compliment and personal-question slots still work, so you can tell your daughter she has her mother's eyes, or ask her how her life has been.

---

## Dynasty standing

A new reputation tier appears in your `[%]` Status display, alongside Dread / Renown / Sellsword Hall. It's derived live from your adult-children set -- no save field needed, no opt-in required:

| Tier | Requirement | Reaction bonus |
|---|---|---|
| **Founder** | 1+ living adult children | +5% |
| **Patriarch / Matriarch** | 3+ living adult children | +10% |
| **Bloodline** | 5+ adult children, all raised to the same alignment band (all virtuous OR all evil at graduation) | +15% |
| **Dynasty** | 5+ adult children + Bloodline purity + at least one grandchild | +20% |

The Patriarch tier reads as Matriarch when the player is female. The Bloodline and Dynasty tiers reward consistency: a player who consistently raised their kids the same way preserves the moral arc of their parenting as a real standing, instead of averaging it out to mush with one outlier.

The standing is empty for players who haven't raised a kid to adulthood. Like the Sellsword Hall standing, the family lane is opt-in by design.

---

## Inheritance on permadeath

When your character runs out of resurrections and the soft-mode revival doesn't save you, the death cinematic now includes an inheritance beat before the Veil closes:

- **50% of your current gold** passes to the eldest living adult child (highest age, name as tiebreak).
- **+500 XP** as a determination buff -- *your parent's death moves you*.
- News entry: *"The estate of {player} passes to {heir}. ({gold} inherited.)"*

The dying character sees a dedicated line in the cinematic: *"Your estate passes to {heir}. ({gold} gold delivered.)"* Then: *"Whatever {heir} becomes, a piece of you walks the world in them."* Then the existing erasure beat continues.

If you have no living adult children at the time of death, the inheritance step short-circuits silently. The regular permadeath cinematic runs unchanged.

The inheritance distributes exactly once per character. If you happen to use the 7-day `/restore` window to bring the character back, the restored save reflects the post-distribution state, so a second permadeath can't double-grant the heir.

---

## Adult children at Team Corner

The `[R] Recruit NPC` flow now treats adult children as their own band. They sort to the top of the recruit list above your friends, render in bright magenta, get a `family` relationship tag, and cost half the base recruit fee:

| Band | Cost | Tag |
|---|---|---|
| **Family** (adult child) | **50%** | `family` |
| Friend (Friendship / Trust / Respect) | 75% / 85% / 95% | `friend` |
| Neutral | 100% | `neutral` |
| Rival (Suspicious / Anger) | 115% / 140% | `rival` |
| Refused (Enemy / Hate) | -- | `won't join` |

Blood ties beat friendship ties: an adult child you're temporarily estranged from still gets the Family band. The family discount is unconditional.

---

## NPCs reflect on you

Adult children you raised who reach public milestone levels (10, 20, 30, 50, 75) now spawn a follow-up news entry tying their accomplishment back to you as their parent. The tone splits on how you raised them:

- **Virtuous:** *"Rumors reach the city: {child}, child of {parent}, has earned a name at level {N} for honest work and a steady hand. The parent's craft, carried forward."*
- **Neutral:** *"Word spreads: {child}, child of {parent}, has reached level {N}. The world remembers their parent at the sound of it."*
- **Evil:** *"Rumors reach the city: {child}, child of {parent}, is whispered of in darker rooms at level {N}. Whatever the parent taught, it took root."*

Only fires for adult children you actually raised (not the ones who graduated into the world independently). Only fires at the five milestone levels, so the news feed doesn't get noisy.

---

## NG+ Charisma bonus

A new lifetime counter tracks how many adult children you've raised to level 20 across your entire playthrough history. Each completed family arc grants +5 starting Charisma in your next cycle's character, capped at +25 (five arcs).

Carryover survives NG+ via the same pattern that already preserves your Sellsword contracts completed and lifetime charity donations. Your last life's dynasty makes your next life's character a more compelling presence -- a real combat-meaningful bonus for the classes that scale on Charisma (Bard, Jester, Wavecaller, Tidesworn), and a real social bonus for everyone else.

---

## Royal sponsorship

If you've taken the throne, a new royal action `[Y] Sponsor Heir to Court` lets you elevate one of your grown children into a court position. The track is decided by the child's alignment:

- **Virtuous** (Chivalry > 200) -- Faith ordination. The child joins the Faith faction and takes the Royal Chaplain court seat.
- **Evil** (Darkness > 200) -- Shadow Spymaster. The child joins the Shadows faction and takes the Royal Spymaster seat.
- **Neither** -- Crown Advisor. The child joins the Crown faction and takes the Royal Advisor seat.

Costs 50 Fame, so you can't pad the court arbitrarily. Only eligible adult children (level 20 or higher, not already in court, not already faction-aligned) appear in the picker. The appointed child joins with a high baseline loyalty (family loyalty is high) and the news feed announces the appointment.

---

## Bloodlines of the Realm

A new `[Z] Bloodlines of the Realm` action at the Castle (visible to royalty and commoners alike) shows the top 10 family lines in the world by descendant count. It's a read-only display, mirroring the existing Hall of Heroes and Pantheon screens.

---

## When your kids kill their parent's killer

NPCs now remember who killed their parent. When an NPC dies (either naturally or by combat), every other NPC whose lineage points at the deceased records two memories: that they witnessed the death of a family member, and -- if there was a killer -- who that killer was. The killer also goes into their `Enemies` list.

Downstream consequences fire through the existing memory and enemy systems:

- The adult child refuses to talk to the killer.
- The adult child refuses to join the killer's team.
- If the adult child is dark-aligned, they attack on sight.

This means a player who builds an empire by killing the dynasty heads of a long-running rival family is now going to face a wave of children with grudges in the next generation. The simulated world has a memory now.

---

## The incest gate

A new helper computes the family relationship between any two characters and returns one of: None, Self, Parent, Child, Sibling, Half-Sibling, Grandparent, Grandchild, Adoptive Parent, Adoptive Child, Adoptive Sibling. Anything except None blocks marriage, intimacy, and pregnancy.

The gate is wired into eight code paths:

- The Church marriage flow (filters candidates and refuses at the altar).
- The Castle royal marriage flow (was bypassing every guard, now refuses up-front for any reason).
- The Castle royal candidate filter (hides blood relatives from the picker).
- The Love Corner marriage flow.
- The Visual Novel dialogue marriage proposal.
- The pregnancy check (defense in depth against any legacy spouse/lover record from before lineage data existed).
- The NPC marriage compatibility check (silent NPC-to-NPC filter, since the world sim doesn't surface refusal flavor).
- The NPC affair partner picker.

Each refusal is localized with a relation-specific flavor line, keeping the tone solemn instead of preachy or clinical. Adoption is treated as blood for the gate, which matches how the game already treats adoption as moral parenthood elsewhere. Sibling marriage is a hard block. Cousins are not tracked.

---

## Polish folded in from v0.62.1

The 0.62.1 release was tagged but never shipped. Its five fixes ride along here:

**"A Ooze" / "A Imp" article bug.** Encounter messages were emitting "A Ooze attacks!", "A Imp guards the boss room", "A Orc traveler arrived" -- hardcoded "A" ignoring English vowel rules. A new helper picks the right article (a / an) with the standard exception lists: silent-h words like "honest" / "hour" / "heir" still take "an", yu-sound words like "user" / "unicorn" / "European" take "a". Eleven message sites fixed, including the high-frequency monster-attack flavor. Non-English templates were left alone because each language has its own article system (Spanish gendered, French gendered, Italian gender plus initial-letter, Hungarian vowel-keyed) already baked into the localized text. Also picked up a related double-determiner bug: the hostile faction line was emitting "A The Crown sympathizer" because faction names already start with "The"; now it just reads "The Crown sympathizer".

**Equipment comparison stats showed up in random order.** A loot-drop comparison screen could list "current: Int, Def, Wis" against "new: Def, Int, Wis" with no consistency. The cause was four different stat-ordering conventions across the codebase. Every stat-bonus list now sorts alphabetically before rendering -- twelve display sites fixed in one sweep. The three intentionally-compact summaries (compact inventory view, magic shop accessory mini-view, status mini-view) kept their priority order so the most important stats stay visible.

**Team Corner showed options that did nothing.** `[A] Apply to join a team` stayed in the menu after you joined a team, and pressing it printed "you're already in {team}" and bounced. It was a duplicate of `[J] Join` with a placeholder comment from v0.49. Now removed. The rest of the menu now gates by team state: Create / Join only show when you're not in a team; Quit / Recruit / Sack / Equip / Specialize / Message / Resurrect / Password / Examine / Your Status only show when you are in a team.

**Team Corner showed online-only features in single-player.** `[P] Password` (locks the team against cross-session join attempts -- meaningless when there's only one session) and `[M] Send Message` (also a stub, even in online mode) were both showing in single-player. Now gated so they only appear online.

**Dev debug screens were shipping to players.** Two visible debug surfaces had escaped to production: an inline `[DEBUG] Relationship Stats:` block at the top of every NPC conversation showing raw Relation Level integers and Flirt Receptiveness percentages, and a `[9] (DEBUG) View personality traits` menu entry that dumped raw 0.00-1.00 trait floats. Both removed. The information that's genuinely useful to a player (orientation, relationship band, romance status, abstracted personality, abstracted flirt receptiveness) now surfaces through the Level Master's Crystal Ball as an in-fiction oracle reading: *"They count you a friend"* instead of *"Relation Level: 40"*; *"They would welcome your advances"* instead of *"Flirt Receptiveness: 65%"*; *"Brave to the point of recklessness"* instead of *"Courage: 0.82"*.

---

## Bug-fix pass against the romance and family backlog

The relationship system and NPC pipeline both had a long backlog of small bugs that had been accumulating across several releases. This release closes 27 of them. Many are individually small. The full list:

### Romance, marriage, and intimacy fixes

- **Permadied lovers stayed in your Current Lovers list forever, and jealousy entries kept ticking against dead NPCs.** Now when an NPC permadies, the spouse / lover / friend-with-benefits / jealousy entries all clean up immediately. Affair records walk both sides of the affair key, so it doesn't matter whether the dead NPC was the married one or the seducer.
- **Spouse death didn't clear jealousy entries.** The jealousy loop could keep ticking against a dead spouse and emit phantom divorce news.
- **`PerformMarriage` was triple-registering player-NPC marriages.** Copy-paste cruft from a fix two years ago meant the same marriage was being recorded three times. Collapsed to a single dispatch.
- **Pregnancy from a player-side intimate scene wasn't recording the father.** A v0.54 invariant that protects affair pregnancies from being clobbered during divorce was silently broken because the father field was never being set in the first place. Now set at every intimate scene.
- **The jealousy loop could trigger divorce news against a dead NPC.** Defense-in-depth null and IsDead guards added at the top of the loop.
- **Castle divorce could leave the marriage half-finished if the spouse NPC reference had drifted.** The flow now resolves the NPC by ID first, then by name, and even when the live NPC reference can't be resolved at all, the underlying marriage record still moves correctly.
- **You could end up with an unlimited number of lovers.** Now capped at five.
- **Affairs that ended in marriage were silently aborting because the old marriage record was still in the way.** The affair-to-marriage flow now divorces the old spouse before trying to marry the new partner, so the already-married guard doesn't trip on a stale record.
- **Faction-tension dialogue flavor was firing against your spouse in non-online contexts.** The romance check was reading from a place that wasn't always populated.
- **Flirt success count could double-bump in a single flirt action** depending on which success branch fired.
- **The dialogue layer could operate against a permadied NPC** if a menu render raced the death cascade. Now guarded.
- **Love Corner divorce had the same NPC-resolution bug as Castle divorce.** Same fix: ID-first lookup, and the underlying marriage record cleans up even when the live NPC reference is gone.
- **Love Corner marriage could deduct gold before checking whether you were already married.** Now refuses up-front.
- **Reading your spouse's name was secretly mutating relation records to clean up dead-spouse state.** This fired side-effects from places that should have been pure reads (the BBS status line, the `/who` list, dialogue render hooks). Now a pure read. The dead-spouse cleanup is an explicit step that runs at login.
- **The "you accepted my confession" state could get overwritten** by a subsequent state-write pass.
- **Affair-to-marriage didn't clear the jilted spouse's jealousy** against the affair partner, so the jealousy loop kept ticking against a relationship that no longer existed.

### NPC, family, and graduation fixes

- **When an NPC permadied in combat, spouse bereavement wasn't firing.** The check was gated on a flag (`IsPermaDead`) that was permanently disabled per a v0.53.11 cleanup, so the bereavement code path had been unreachable. Fixed.
- **Child custody after divorce wasn't recomputing the parent's child count correctly.** The first-pass fix overcorrected by reading all biological children regardless of who got custody, so both parents reported 100% of shared kids post-divorce. Now honors the custody-access flags correctly: each parent reports only the kids who live with them.
- **Two newborns named at the exact same tick could collapse into one record** if both had empty parent IDs. The dedup key now includes parent names too.
- **`Child.IsParent` was dead code** with zero callers. Removed.
- **The online dashboard summary showed "Unknown" for children at Kidnapped or Away status.** Now correctly distinguishes Home, Orphanage, Kidnapped, and Away.
- **Newborns didn't get a surname until a deserialization migration caught them on next load.** Now the surname is set when the child is named, not after.
- **A new ID-first overload of "disown children of"** was added. The name-based overload is preserved for backwards compatibility.
- **Late-pickup orphans were spawning as Human regardless of parental race.** Now uses the parental-race determination that the rest of the pipeline already had.
- **NG+ disown wasn't immediately persisting to the shared world state**, so a concurrent world-sim tick could re-attach the disowned children. Now persists immediately.
- **NPC aging could push a race to extinction** if the population was already at the permadeath floor. Now consults the race floor and extends the lifespan by a week if the race is on the brink.
- **Widow cleanup wasn't clearing the legacy `Married` flag**, so the player ended up half-married. Some quest and dialogue paths consult that flag independently, so the inconsistency could surface later.

### The fixes that came up during testing

After the main pass, I re-tested everything and caught two regressions that hadn't been there before:

- The affair-to-marriage flow was silently aborting. The fix routes through `PerformMarriage` correctly, but the underlying relation record hadn't been cleared off "married" before the guard checked. Fixed by explicitly divorcing the old marriage before clearing the spouse name.
- The custody fix was over-reporting. Both parents were reading their child count from the "all biological kids" query instead of the "kids who live with me" query. Fixed with a new custody-aware helper that honors the access flags.

Both regressions were caught and fixed before this release shipped.

### Polish items wrapped up in the same pass

Three more loose ends that came up during the audit and got closed before shipping:

- **The "you have too many lovers" message now surfaces to the player.** The lover cap (max 5) was being enforced silently. Now every flirt / confession / affair flow that tries to add a new lover and gets refused tells the player why: *"{name} would be yours, but your heart already carries too many lovers. The bond falters before it begins."* Confession scenes still register the emotional acceptance; only the formal lover relationship is what the cap blocks. Affair-divorce scenes that hit the cap write a different news entry that reflects what actually happened: the affair partner left their spouse but did not stay with the player.
- **The Church / LoveCorner vs Castle marriage inconsistency is closed.** A player could marry their own teammate at Church or LoveCorner, but the Castle royal marriage flow refused them: the candidate filter excluded any NPC on any team. Now the Castle royal flow allows the player's own teammate as a candidate while still filtering out NPCs sworn to a different team. Consistent with the Church/LoveCorner behavior, and the political-marriage framing is preserved (rival-team-loyal NPCs are still refused).
- **Family-tie memory wiring extended.** The earlier work added two new memory types for family-tie death events but only wired the parent-died case to children. Now the death walk covers every relation: when an NPC dies, every living parent, child, and sibling records a "lost family member" memory; if there was a killer, every relative records a killed-my-family memory naming the killer, and the killer goes into each relative's `Enemies` list. Two new memory types added (`KilledMyFamily` for sibling/parent loss, `FamilyMemberBorn` for births). A counterpart birth handler now records a positive memory in every living parent and sibling when a new child is born. The downstream behavior (refusing to talk to the killer, refusing to join the killer's team, attacking on sight if dark) now fires for sibling deaths and parent-grief on a child death, not just child-grief on a parent death.

---

## Graduation pipeline fixes (the bugs we caught in passing)

Three pre-existing bugs in the child-to-NPC graduation pipeline were caught while building the lineage system, and fixed inline because they sit in the same code paths:

**Graduated adults were spawning with secretly-zeroed stats.** The graduation code set the live stat values but never set the underlying Base values. The first time anything recalculated stats (which happens on level-up, equipment change, status effect), the Base values reset to zero and zeroed everything else. Result: a graduated adult NPC had real stats at the moment of graduation and 0-stat husk values forever after. Both `ConvertChildToNPC` and `OrphanBecomesNPC` now populate the Base values alongside the live ones.

**Graduated Magicians had 0 mana. Graduated Clerics had 0 wisdom.** The graduation code was setting strength and a few core stats but skipping Constitution, Wisdom, Dexterity, and Max Mana entirely. So a kid who graduated and became a Magician had zero mana to cast spells with, and a kid who became a Cleric had zero wisdom so their heals scaled catastrophically. Now both methods populate all nine core stats plus Max HP and class-appropriate Max Mana (caster classes get a base mana pool scaled by Intelligence).

**Orphan graduates weren't getting orientation reconciliation or faction assignment.** A female orphan who rolled the same-sex orientation band stayed mislabeled "Gay" instead of being reconciled to "Lesbian" -- the orientation-to-gender sync that the v0.61.7 release added never reached this code path. And orphan graduates never got a faction assignment at all, while immigrant NPCs did. Both now run.

---

## Name-clash handling

Children who graduate now route their display name through the same Roman-numeral disambiguation that immigrant NPCs already used. If a kid named "Aldric" graduates while another NPC named Aldric already exists, the graduate becomes "Aldric II". Promoted the disambiguation helper to public so all three creation paths (immigrant, child graduate, orphan graduate) share one canonical check.

---

## Save compatibility

Pre-v0.63.0 saves heal themselves on next login. The lineage fields on existing NPCs are filled in by walking the Child registry on load: for each graduated child record, the corresponding adult NPC's lineage fields get populated from the child's parent records. The backfill is idempotent (safe to run every login), runs after the family registry has been populated, and logs a per-run summary.

The save round-trip is exercised by automated tests for the new lineage fields, the recognition / inheritance state, and the NG+ family-arc carryover.

---

## Build status

- Build clean.
- 753 tests passing.
- The cleanup pass was tested end-to-end, and two regressions caught during re-testing were fixed before shipping.
