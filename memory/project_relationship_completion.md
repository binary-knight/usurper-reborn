---
type: project
status: design
related: [[project_adult_children_dialogue]], [[project_alignment_rework]]
---

# Relationship / Family / Multi-Generational System Completion

What "done" looks like for the family arc -- the player-visible surface that makes the simulation worth playing through. Companion to the two parallel audits (relationship-system-reviewer, npc-system-reviewer); this doc is design, not bug triage.

## Calibration

The alignment rework's lesson applies here verbatim: most of the perceived gap is VISIBILITY, not depth. The engine already simulates lineage (Child class, FamilySystem, ConvertChildToNPC, RomanceTracker, NPCMarriageRegistry, NPC-NPC marriage and children, custody on divorce, royal blood) -- but the player never SEES that an adult NPC in the world is their kid, can't tell them apart from any other NPC, and gets no narrative payoff for the 18 in-game years of parenting they put in. The fix is the same shape as Phase 1 of the alignment rework: tag the data, surface it everywhere it touches, and only THEN add new content. Don't build slice 5 (legacy / inheritance) before slice 1 (the kid recognizes you).

## Diagnosis (per failure mode)

### F1. Adult children are amnesiac strangers -- root cause is a missing data field

`Scripts/Systems/FamilySystem.cs:291-409` (`ConvertChildToNPC`) creates a brand-new `NPC` from a graduated `Child` and copies Name / Sex / Race / Class / soul-derived alignment + personality. It carries `BirthDate` over (line 337) for lifecycle aging. It does NOT copy `Mother` / `Father` / `MotherID` / `FatherID` / `OriginalMother` / `OriginalFather` onto the new NPC, because `NPC` (and `Character`, its base at `Scripts/Core/Character.cs:1671-1684`) has nowhere to put them. There's a vestigial single-string `Character.FatherID` (line 1676) with no readers. The `Child` record IS marked `Deleted = true` (FamilySystem.cs:403) but still sits in `FamilySystem._children` -- so the lineage exists in cold storage but the warm "this is your daughter" link to the live NPC is severed at conversion. **This is a VISIBILITY problem at root -- a one-field data model gap, not missing content.**

### F2. Family-tie bounds don't exist anywhere

Three holes:
- **Player marriage**: `ChurchLocation.GetEligibleMarriageCandidates` (`Scripts/Locations/ChurchLocation.cs:1094-1124`) filters by alive / not-married / in-love. No incest check. Player can marry their own adult daughter.
- **Player intimacy / pregnancy**: `IntimacySystem.CheckForPregnancy` (line 318+) gates on opposite-sex + spouse-or-lover + child cap. No relation check. Pregnancy with own adult child works mechanically.
- **NPC-NPC marriage**: `EnhancedNPCBehaviors.AttemptNPCMarriage` (`Scripts/AI/EnhancedNPCBehaviors.cs:469-532`) and its compatibility checker `IsCompatibleForMarriage` (line 537-549) check attraction + level gap + faction + personality. No relation check. Two siblings who graduated from the same player's marriage can court each other in the world sim.

**Content gap, not visibility -- the rule doesn't exist anywhere. But it's a cheap rule because it's a single helper called from 3 known sites.**

### F3. The multi-generational arc never lands

This is the genuine CONTENT GAP. Even after F1 + F2 are fixed, the player still has no reason to CARE that they have grown kids walking around. There's no "you came home to find your daughter is a Lv.40 Paladin now," no "your son is petitioning for the throne and you've raised him good or evil," no "on your permadeath your estate passes to ___." This is the alignment-rework Phase 2/3 equivalent: ladders and reward loops that turn passive data into active gameplay.

### F4. NPC-NPC family arcs in the simulated world

`FamilySystem.CreateNPCChild` (FamilySystem.cs:576-613) registers NPC-NPC children and runs them through the same age + graduate pipeline. Same F1 root cause: the graduated NPC loses parent linkage. Cumulative effect over a long-running server: hundreds of NPCs that are mathematically related but invisibly so. **The player has no surface to see family lines in the world AT ALL -- not in /who, not in NPC `[X] Examine`, not in Castle records.**

## Design framework (yin-yang)

The mirrored fantasy for parenthood is:

- **Yin (build the dynasty)**: raise virtuous children, watch them rise to faction roles / Castle court / dungeon teammates, hand down your name, see your bloodline outlive you. Reward: legacy. Echoes the Renown / Light pole.
- **Yang (build the empire)**: raise ruthless children, deploy them as enforcers / heirs to your darker work, let them feud, watch them seize what they're owed by force. Reward: continuity of will. Echoes the Dread / Dark pole.

Both should let a player who's already done the alignment payoff slices feel that their *family* reflects who they were. The Soul system on `Child` already mechanically supports this (Soul drives adult Class selection at FamilySystem.cs:440-477 -- good kids become Paladin/Cleric/Warrior, evil kids become Assassin/Magician/Barbarian). The hooks are there; the visibility and payoff aren't.

The **center** (Balanced): the merc-family. Adult kids are independent NPCs you can recruit, not avatars of your alignment.

## Tiered recommendations

### Tier A -- Cheap visibility wins (Phase 1)

**A1. Add lineage fields to NPC + carry them through `ConvertChildToNPC`.** New on `NPC` (not `Character`, since Players don't need this -- it's NPC-side state): `string MotherName`, `string FatherName`, `string MotherID`, `string FatherID`, `DateTime? BirthDate` already exists. Optional: `bool WasRaisedByPlayer` flag set when EITHER `MotherID` or `FatherID` matches the player's `ID` at conversion time. **Save plumbing**: NPCData + WorldSimService NPC restore paths (matches the existing pattern for adding new NPC fields -- `npc-system-reviewer` will already be familiar with the chokepoints from their audit). **Cost**: 1 field-add pass + 1 save round-trip test. **Risk**: low; additive. **ROI**: every downstream slice depends on this field. **Anchor**: `Scripts/Systems/FamilySystem.cs:291-409`, NPC class declaration, NPCData class, OnlineStateManager + WorldSimService NPC serialize/restore sites.

**A2. Tag adult children visibly everywhere NPC names appear.** Once A1 lands, surface "(your daughter)" / "(your son)" / "(your child)" in:
- `[T] Talk` interaction header at BaseLocation
- `[X] Examine` NPC stats screen (TeamCorner, Inn, anywhere `team.examine_*` keys render)
- Citizen list / Fame board on Main Street
- `/who` if the player is online and the adult child is also a known NPC
- Marriage candidate list (where it'll act as a soft "don't" signal before A4's hard gate)
- Romance system display strings ("(your daughter)" tag shown next to NPC name in dating UI)

`is-your-child` check: helper `FamilySystem.IsAdultChildOf(NPC npc, Character parent)` -- matches by `npc.MotherID == parent.ID || npc.FatherID == parent.ID`, falls back to name-match for pre-A1 saves where the parent slot may have been stored as name not ID (this fallback heals during save migration). Localized via single `family.tag_your_child_*` key, one per gender + neutral. **Cost**: ~10 display sites + 4 loc keys x5 langs. **Risk**: low. **ROI**: makes the entire system visible for the first time. **Anchor**: extend the `team.examine_*` block at `TeamCornerLocation.ExamineMember`, add to `BaseLocation.GetDisplayName`-ish helper sites.

**A3. New `[L] Lineage` view at Home.** Single screen: Player at top, partners (current + ex), then children grouped by status (minor at home / minor at orphanage / minor kidnapped / minor away-at-school / adult living / adult deceased / NPC grandchildren), then surviving spouse/lover-of-child names. ASCII tree, BBS-friendly. This is the player-side version of the "family tree at Castle" that exists nowhere right now. **Cost**: one location screen + queries already available via `FamilySystem.GetChildrenOf` + NPC field lookup after A1. **Risk**: low. **ROI**: high -- this is the single page that PROVES the system is doing anything.

### Tier B -- Family-tie bounds (Phase 1, ships with A1)

**B1. Single canonical helper: `FamilySystem.GetFamilyRelation(Character a, Character b)`** returning an enum `FamilyRelation { None, Self, Parent, Child, Sibling, HalfSibling, Grandparent, Grandchild, Spouse, ExSpouse, InLawCurrent, InLawFormer, AdoptiveParent, AdoptiveChild }`. One function, exhaustive, called from every block site so the rules stay consistent. Checks against the Child registry (biological) and `OriginalMother`/`OriginalFather` (preserves adoption distinction).

**B2. Block list for marriage / intimacy / pregnancy** -- the rule:

| Relation | Block? | Refusal flavor |
|---|---|---|
| Self | yes (defense in depth) | "You contemplate yourself in the mirror. Move on." |
| Parent / Child (biological OR adoptive) | yes, hard | flavor refusal; if player insists past prompt, +500 Darkness, news entry "the kingdom whispers about ___ and their own ___" -- the "you can do it but it costs you" pattern matches how altar desecration already works |
| Grandparent / Grandchild | yes, hard | same flavor band |
| Sibling / Half-sibling | yes, soft -- N/Y confirm with -200 Chivalry penalty if they push through | "the singer's tongue, not your hand" |
| Adoptive sibling | warn only | "you grew up under the same roof but no blood" |
| Cousin | allow | not tracked at depth, would balloon the relation map |
| In-law (current spouse's parent/sibling) | warn only | factually adultery-adjacent, the existing affair/jealousy system handles consequences |
| Step-child (player's new spouse's kid from prior relationship) | allow with delay | requires `MinDaysBeforeMarriage` doubled |

Apply at: `ChurchLocation.GetEligibleMarriageCandidates` (filter, with refusal showing reason), `IntimacySystem.CheckForPregnancy` (early-return + flavor line, NOT silent), `EnhancedNPCBehaviors.IsCompatibleForMarriage` (filter, no refusal needed -- world sim just doesn't pair them). LoveCorner marriage flow and VisualNovelDialogue confession flow also need the gate.

**B3. NPC-NPC sibling check in world sim.** Same helper, applied silently at the candidates filter in `AttemptNPCMarriage` (line 491). World sim doesn't need a refusal line -- the NPC just never reaches "in love" with a sibling. **Cost**: 1 helper + 6 caller sites + ~12 loc keys. **Risk**: medium -- the helper has to handle dead/converted/adopted edge cases, and the test surface is the relationship-system-reviewer's existing audit boundary. **ROI**: closes the most-flagged "weird and unintended" complaint and gives F3's slices a clean substrate to build on (you can't have a "your adult daughter recognizes you" dialogue if she's currently your wife).

### Tier C -- Adult-child reunion (Phase 2)

The contents of [[project_adult_children_dialogue]] are the starting point. I'd extend it:

**C1. Recognition moment on first encounter.** Once a graduated-NPC adult child crosses paths with the player at any location, fire a one-time cinematic: a paragraph of flavor, the adult child says one line keyed to their Soul band (virtuous: "Father, I knew you would come back," evil: "Hello again, old man -- did you bring gold this time?"), and the relationship gets seeded at `RelationLove`-adjacent (high baseline, but not auto-Lover -- a child loving their parent is not the same record as romantic love). Stored in a new `Set<string> RecognizedChildren` on Character so it only fires once per adult child per cycle.

**C2. Adult child dialogue topics.** New chat topic slots in `VisualNovelDialogueSystem.BuildAvailableChatTopics` gated on `FamilySystem.IsAdultChildOf`: "remember when…", "how is your mother?", "what's your trade now?", "are you well?", "do you need anything?" -- each loc-keyed, each pulling from the existing Soul / personality data to vary tone. ~5-8 topics x ~3 response variants = 15-24 lines, modest authoring lift. Player gift-giving (already in dialogue system) gets a "+15 instead of +5" multiplier when given to your own kid.

**C3. Adult child recruitable to dungeon team.** Adult children appear in TeamCorner `[R] Recruit` flow tagged "(your daughter)" and -- once Renown / Dread is in play from the alignment rework -- get a discount on recruitment cost mirroring the existing band multipliers. They auto-equip class-appropriate. Their death triggers a unique grief variant (deeper than NPC-teammate grief, less than companion grief; new `GriefSystem` grief-subject type). **Cost**: existing TeamCorner recruit path + 1 tag + grief variant. **Risk**: low; reuses existing teammate pipeline. **ROI**: gives the player a *reason* to keep their kids alive into the dungeon era.

### Tier D -- The multi-generational arc payoff loop (Phase 3, ladders)

**D1. Parenthood Renown / Dread mirror -- "Patriarch / Matriarch" standing.** Derived live (no new save field) from the count + Soul-alignment of your living adult children, capped at +100 charisma equivalent. Tiers:
- **Founder** (1 adult child)
- **Patriarch / Matriarch** (3 adult children)
- **Bloodline** (5 adult children, ALL same alignment band -- pure good OR pure evil)
- **Dynasty** (5 adult children + at least 1 NPC-NPC grandchild traced through the adult-child registry)

Shown on `[%]` status. Each tier grants a small passive: faster relationship-cap-bypass on chat topics with NPCs (the Charisma-of-being-a-known-parent), a discount at the Sanctum or Dark Alley mirroring the alignment of your dynasty, +5% gold from world-sim NPC purchases (your kids spend at your shops). Mirrors the Phase 2 Dread / Renown derived-from-alignment-scale pattern at `AlignmentSystem.GetDreadTier`.

**D2. Adult children reflect on you in the world.** A "Bloodline" tagged Dark child who reaches Lv.30 in the world sim spawns rumor news ("___, child of ___, was seen at the Dark Alley last night"). A virtuous one earns Faith reputation gains for you. Real implementation is small: piggyback the existing NPC level-up news entries and gate on the new "raised by player" flag.

**D3. Adult child rises to a role.** Once an adult child hits a level threshold and their alignment matches a faction, they petition (existing `NPCPetitionSystem` is the chokepoint) to join the faction with player co-signature available. Player can use a Renown / Dread reputation cost to push them into a Castle court seat, a Faith ordination, or a Shadows enforcer slot. Big lift in pure design terms (player needs an audience flow) but reuses existing petition + Castle court + faction join machinery. **Defer to D-late** if Phase 3 budget tightens.

**D4. Inheritance on permadeath.** When the player permadies and has at least one living adult child, on the death cinematic:
- 50% of gold passes to the eldest adult child NPC (added to their `Gold`)
- Equipped artifacts pass to the highest-soul-aligned adult child (preserves the artifact in the world sim so a future NG+ run can rediscover it on the kid)
- The adult child gets a one-time +500 XP grief / determination buff
- News entry: "The estate of ___ passes to ___."
- If no adult children: existing permadeath flow unchanged.

This is the single most narratively satisfying payoff and the cheapest to build. **Cost**: ~30 lines in the permadeath path + a couple of loc strings. **Risk**: low; only fires on permadeath which is a rare event. **ROI**: turns parenthood into an actual hedge against death -- a system-shaped reason to have kids.

**D5. NG+ starting bonus from completed family arcs.** Per-cycle persistent stat: completed family arcs (an arc = raised a child to adulthood with their adult NPC reaching Lv.20+ in their lifetime) grant a small NG+ starting bonus on the next character -- a chunk of CHA, a starting gold bump, or a faction reputation seed if your last child was high-faction. Mirrors the alignment-rework cycle-bonus pattern. **Defer** to a later slice; nice but not load-bearing.

### Tier E -- NPC-NPC family arcs (Phase 3, low budget)

**E1. NPC `[X] Examine` shows lineage block.** Once A1 ships, every NPC examine screen gets a "Family" block: parents (if known and alive: tagged appropriately; if dead: "the late ___"), spouse, children. Pure data render, no new content. **Cost**: same screen as A2, add 4 lines. **Risk**: zero. **ROI**: makes the simulated world feel deep, even if the player isn't related to anyone.

**E2. Multi-generational NPC family rivalries.** When an NPC dies violently (especially permadeath via player or another NPC), their adult children gain a `KilledMyParent` memory entry naming the killer. Existing `MemorySystem` already supports this kind of entry. Surfaces as: that adult child refusing to talk to the killer, refusing to join the killer's team, attacking on sight if Dark-aligned. This is the start of an honest blood-feud system but with one mechanic. **Cost**: ~1 entry in MemorySystem + a check at NPC interaction sites. **Risk**: low. **ROI**: makes the long-running server feel ALIVE -- consequences propagate down generations.

**E3. Family tree at Castle records.** Optional `[F] Family Trees` at Castle that lists the 10 most-prolific bloodlines in the world (root NPC + descendants). Pure read-only. Reuses the Hall of Heroes / Pantheon layout precedent. **Defer**; great QoL once people are asking for it.

## Recommended first slice

**Slice 1 = Tier A + Tier B.** Ship together. They're cheap, they're risk-free (additive data + caller-site gating), they unblock every other slice in the doc, and they fix the loudest currently-broken behavior (player marrying their daughter, NPCs marrying their siblings) AND the cheapest visibility win (you finally know who your kids are when you see them).

Concrete deliverable:
1. NPC lineage fields + save round-trip + `WasRaisedByPlayer` flag (A1)
2. `ConvertChildToNPC` populates them (A1)
3. `FamilySystem.IsAdultChildOf` and `FamilySystem.GetFamilyRelation` helpers (A2, B1)
4. "(your daughter)" / "(your son)" tags at 10 display sites (A2)
5. `[L] Lineage` view at Home (A3)
6. Marriage / intimacy / NPC-NPC pairing all gated by `GetFamilyRelation` (B2, B3)
7. Save migration: pre-slice-1 saves heal by retro-walking the Child registry once on load -- for each `Deleted` Child whose recorded `Name` matches a living NPC by Name + BirthDate, retro-set the parent fields. The retro-walk already exists in similar form for the v0.61.4 "stuck echo" + v0.62 "spouse id-drift" fixes -- pattern is known.

**Then** assess. Tier C (adult-child reunion content) is the obvious Slice 2; Tier D1 + D4 (Patriarch standing + inheritance) is the obvious Slice 3 / payoff slice. E is sprinkled in as Tier-C tail content.

## Open questions for the human

1. **Adoption parity** -- the design treats adoption as equivalent to biological for the incest gate (B2). Is that right? The simulation has `OriginalMother` / `OriginalFather` preserved, so we COULD allow the player to marry their adopted child as a "you raised them but they're not yours by blood" angle. My take: block it -- the game already treats adoption as moral parenthood elsewhere (custody on divorce, alignment events). The narrative reads better.
2. **D4 inheritance to whom** -- eldest by age, or highest-Soul-alignment match? Or player-designated? I lean eldest by age (matches dynasty fiction, matches royal succession which already works that way).
3. **Sibling marriage soft vs hard block** (B2 row 4) -- "soft with 200 Chivalry hit if you push through" makes Dark playthroughs distinct (a Targaryen-shaped fantasy is genuinely Dark-coded). Or hard-block universally for simplicity. Designer call.
4. **Dynasty cross-cycle persistence** (D5) -- do completed arcs persist across NG+ as a `MetaProgressionSystem` ladder, or do they reset? Persistence makes the cycle structure pay off more, but matches the alignment rework's deliberate "everything resets" stance less cleanly.
5. **`[L] Lineage` location** -- I put it at Home (A3). Castle Records (E3) is the symmetric "the kingdom's bloodlines" view. Are they two separate features, or is `[L]` at Home strong enough as Slice 1 with E3 as a deferred public-genealogy add?

## Anchors (file:line summary)

- `Scripts/Systems/FamilySystem.cs:291-409` ConvertChildToNPC -- A1 plug-in site
- `Scripts/Systems/FamilySystem.cs:52-77` GetChildrenOf / GetChildrenOfCouple -- A3 query source
- `Scripts/Core/Child.cs:11-100` Child class -- already carries OriginalMother / OriginalFather
- `Scripts/Core/NPC.cs:34` NPC partial class -- A1 field addition
- `Scripts/Core/Character.cs:1671-1684` family fields block -- existing FatherID is the placeholder
- `Scripts/Locations/ChurchLocation.cs:1094-1124` GetEligibleMarriageCandidates -- B2 site #1
- `Scripts/Systems/IntimacySystem.cs:318` CheckForPregnancy -- B2 site #2
- `Scripts/AI/EnhancedNPCBehaviors.cs:469-549` AttemptNPCMarriage + IsCompatibleForMarriage -- B3 site
- `Scripts/AI/EnhancedNPCBehaviors.cs:1102-1244` NPCMarriageRegistry -- existing chokepoint, no changes needed for slice 1
- `Scripts/Systems/RomanceTracker.cs:11+` RomanceTracker -- AddSpouse / AddLover sites already exist; lineage tag display on partner list is A2 work
- `Scripts/Locations/TeamCornerLocation.cs` ExamineMember -- A2 / E1 lineage block insertion
- `Scripts/Locations/HomeLocation.cs` -- A3 new `[L]` menu entry
- `Scripts/Locations/CastleLocation.cs:4188` player marriage registration -- B2 confirm path
- `Scripts/Systems/SaveSystem.cs` / `Scripts/Systems/OnlineStateManager.cs` / `Scripts/Systems/WorldSimService.cs` NPC serialize/restore -- A1 plumbing

A tight Slice 1 here unblocks the rest, ships in a single release, and lets the parallel bug audits land their fixes on a substrate that finally tells the player who's whose.
