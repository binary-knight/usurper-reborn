# v0.63.1 -- Bloodlines hotfix

A follow-up to the v0.63.0 ship. One menu-visibility bug, two half-shipped features that turned out to have no downstream consumers, and the explanation for one design-by-omission that read as a bug.

## Bloodlines of the Realm was invisible from the Castle menu

The `[Z] Bloodlines of the Realm` action shipped with v0.63.0 worked correctly when pressed, but it was never actually rendered in any of the Castle menu displays. So even though the case handler existed and the family-tree screen was fully functional, no one could tell the option was there.

Now fixed across all four Castle display variants: the visual exterior screen commoners see, the visual interior screen the king sees, the BBS-compact exterior, and the BBS-compact interior. The commoner hotkey, which had landed on `F` while the king's was on `Z`, is now `Z` in both flows so muscle memory is consistent regardless of whether you're on the throne.

The king's `[Y] Sponsor Heir to Court` action was also missing from the BBS-compact king display while present in the visual one. That row now shows both `[Y] Sponsor Heir` and `[Z] Bloodlines` together.

New localized menu label: "Bloodlines of the Realm" across English, Spanish, French, Hungarian, and Italian.

## Dynasty standing now does something

The v0.63.0 release notes promised each Dynasty tier grants a reaction bonus (+5% Founder, +10% Patriarch / Matriarch, +15% Bloodline, +20% Dynasty). The function that computes the bonus was implemented and the standing tier shows up correctly on the `[%]` Status display, but it turned out the function had zero callers anywhere in the codebase. The bonus existed on paper and nowhere else.

Now wired into `AlignmentSystem.ApplyVeloraReactionBonus`, the canonical reaction-modifier pipeline. The Dynasty bonus stacks multiplicatively alongside Veloura shrine attunement and Cave Spider pet penalties, applied on top of the alignment-compatibility base modifier. A Patriarch / Matriarch interacting with an aligned good NPC now sees a 65% reaction bonus instead of 50%; a Dynasty-tier player sees 80%.

## Family-grudge NPCs now actually react to you

The v0.63.0 release notes promised that an NPC whose parent, sibling, or child the player killed would refuse to talk, refuse to join the player's team, and attack on sight if dark-aligned. The memory was being written into every relative's memory store on the death cascade, and the killer's name was going into their `Enemies` list, but a code-wide grep for downstream consumers of either signal returned zero hits. The "multi-generational rivalries" feature was a write-only pipeline.

Now wired with three consumers:

**Refuses to talk.** When you press `[T] Talk` on an NPC who has lost a family member to you, the conversation refuses to open: *"{name}'s eyes go hard at the sight of you. They turn their back and walk away."* followed by *"Some blood debts do not forget."* This runs after the adult-child recognition check (so your own kids don't refuse you, even if they nominally have grudges) and before the Dread flee check. Story NPCs and the king are exempt to keep quest flow unblocked.

**Refuses to join your team.** The Team Corner recruit list now sends any grudge-bearing NPC straight into the `Refused` band regardless of their friendship score, so they show up in the candidate list with a "won't join" tag instead of an unrealistic recruitable cost.

**Attacks on sight if dark-aligned.** Street encounters now scan the living NPC roster for any dark-aligned grudge-bearer in your level range, and route them in as the hostile NPC encounter ahead of the random-evil-thug pool. When they appear, their hostile phrase is replaced with a family-revenge line: *"You killed my blood. Now I take yours."* / *"My family does not forget. Neither do I."* The fight uses normal CombatEngine, so if you kill the avenger you've permadied them like any other NPC and their relatives now hate you too.

A new helper `FamilySystem.HasGrudgeAgainst(viewer, killer)` is the single source of truth for the grudge check. It reads the `KilledMyParent` and `KilledMyFamily` memory types directly rather than the `Enemies` list, which sidesteps an inconsistency where everything else in the codebase stores NPC IDs in `Enemies` while the family-death writes were storing display names.

Five new revenge-phrase loc keys per language plus two grudge-refusal lines, all translated into Spanish, French, Hungarian, and Italian.

## Dynasty standing is empty until you actually have a grown child

Not a bug, but worth calling out because someone asked: the Dynasty standing line on `[%]` Status only appears once you have at least one living adult child. If you haven't raised a kid to 18 yet, the line correctly does not render. This matches the Sellsword Hall standing, which also only appears once you've completed at least one mercenary contract. The release notes called this out, but it's easy to read as a bug when the line you expected to see is missing.

For context: a child is born at intimacy scenes (Home, Love Corner, etc.), grows up over in-game time, and graduates at 18 to become an adult NPC. Once that happens, the Dynasty line appears and the reaction bonus starts applying.

## Files changed

- `Scripts/Core/GameConfig.cs` -- version bump.
- `Scripts/Locations/CastleLocation.cs` -- four display blocks updated, commoner case handler hotkey changed from F to Z.
- `Scripts/Systems/AlignmentSystem.cs` -- Dynasty reaction bonus folded into `ApplyVeloraReactionBonus`.
- `Scripts/Systems/FamilySystem.cs` -- new `HasGrudgeAgainst` helper.
- `Scripts/Locations/BaseLocation.cs` -- grudge gate added to `InteractWithNPC` between recognition and Dread checks.
- `Scripts/Systems/TeamSystem.cs` -- grudge gate added to `GetRecruitmentBand`.
- `Scripts/Systems/StreetEncounterSystem.cs` -- grudge-revenge priority scan in `FindHostileNPC`, family-revenge phrase pool in `GetHostilePhrase`.
- `Localization/en.json` / `es.json` / `fr.json` / `hu.json` / `it.json` -- 8 new keys (1 Bloodlines menu label + 2 grudge-refusal lines + 5 revenge phrases).

## Build status

- Build clean.
- 753/753 tests passing.
