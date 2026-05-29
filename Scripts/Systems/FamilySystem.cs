using System;
using System.Collections.Generic;
using System.Linq;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Family System - Manages children, family relationships, and child aging
    /// Children age over time and eventually grow into adult NPCs
    /// </summary>
    public class FamilySystem
    {
        private static FamilySystem? _instance;
        public static FamilySystem Instance => _instance ??= new FamilySystem();

        // All children in the game world
        private List<Child> _children = new();

        // Age at which children become adults (and turn into NPCs)
        public const int ADULT_AGE = 18;

        // Days per year of aging (children age faster than real time)
        public const int DAYS_PER_YEAR = 7; // 1 week real time = 1 year in-game

        public FamilySystem()
        {
            _instance = this;
        }

        /// <summary>
        /// Get all children
        /// </summary>
        public List<Child> AllChildren => _children;

        /// <summary>
        /// Add a new child to the registry
        /// </summary>
        public void RegisterChild(Child child)
        {
            if (child == null) return;
            // v0.63.0 slice 4 (audit npc-M3): the dedup key must remain unique even
            // when MotherID and FatherID are both empty strings. Pre-fix two children
            // registered at the same DateTime.Now tick (eg twins, same-tick affair
            // pregnancy) with empty parent IDs collapsed into one. Match by names
            // as a secondary key, and Name (post-naming) as a final disambiguator.
            bool isDuplicate = _children.Any(c =>
                c.BirthDate == child.BirthDate
                && c.MotherID == child.MotherID && c.FatherID == child.FatherID
                && c.Mother == child.Mother && c.Father == child.Father
                && c.Name == child.Name);
            if (isDuplicate) return;

            _children.Add(child);

            // v0.63.0 follow-up: record FamilyMemberBorn in the memory of every
            // living relative (existing parents, siblings via shared parent name).
            // Counterpart to WorldSimulator.RecordFamilyDeath. Wrapped in try/catch
            // because RegisterChild can fire deep in load paths where WorldSimulator
            // singleton might not be fully initialized yet.
            try
            {
                var childDisplayName = !string.IsNullOrEmpty(child.Name) ? child.Name : "a newborn";
                WorldSimulator.Instance?.RecordFamilyBirth(childDisplayName, child.Mother, child.Father);
            }
            catch
            {
                // Memory wiring is best-effort; child registration itself succeeded.
            }
        }

        /// <summary>
        /// Get children belonging to a specific character (as parent)
        /// </summary>
        public List<Child> GetChildrenOf(Character parent)
        {
            if (parent == null) return new List<Child>();

            return _children.Where(c =>
                !c.Deleted &&
                (c.MotherID == parent.ID || c.FatherID == parent.ID ||
                 c.Mother == parent.Name || c.Father == parent.Name)
            ).ToList();
        }

        /// <summary>
        /// v0.63.0 slice 4 follow-up (audit npc-M2 / Finding 2): return only the
        /// children CURRENTLY in this parent's custody. After divorce,
        /// HandleDivorceCustody flips Child.MotherAccess / FatherAccess on the
        /// losing parent (FamilySystem.HandleDivorceCustody calls
        /// Child.HandleDivorceCustody at FamilySystem.cs:1101), but the
        /// Child.Mother / Father name fields stay intact for lineage queries.
        /// GetChildrenOf returns ALL biological children regardless of custody;
        /// this helper returns only the ones the parent has access to so that
        /// Character.Kids recompute after divorce reflects custody not lineage.
        /// </summary>
        public List<Child> GetCustodialChildrenOf(Character parent)
        {
            if (parent == null) return new List<Child>();
            return _children.Where(c =>
                !c.Deleted &&
                (
                    // Mother-side custody: Mother slot matches AND has access.
                    (((c.MotherID == parent.ID && !string.IsNullOrEmpty(parent.ID)) || c.Mother == parent.Name)
                        && c.MotherAccess)
                    ||
                    // Father-side custody: Father slot matches AND has access.
                    (((c.FatherID == parent.ID && !string.IsNullOrEmpty(parent.ID)) || c.Father == parent.Name)
                        && c.FatherAccess)
                )
            ).ToList();
        }

        /// <summary>
        /// Get children of a married couple
        /// </summary>
        public List<Child> GetChildrenOfCouple(Character parent1, Character parent2)
        {
            if (parent1 == null || parent2 == null) return new List<Child>();

            return _children.Where(c =>
                !c.Deleted &&
                (((c.MotherID == parent1.ID || c.Mother == parent1.Name) &&
                  (c.FatherID == parent2.ID || c.Father == parent2.Name)) ||
                 ((c.MotherID == parent2.ID || c.Mother == parent2.Name) &&
                  (c.FatherID == parent1.ID || c.Father == parent1.Name)))
            ).ToList();
        }

        #region Family Relations and Incest Gate (v0.63.0)

        // Slice 1 of relationship completion. The audit found three classes of
        // family-tie bugs all rooted in one missing helper: nothing in the
        // codebase could answer "is character A blood-related to character B?"
        // ChurchLocation.GetEligibleMarriageCandidates / CastleLocation.ProposeMarriage
        // / LoveCornerLocation.HandleMarry / RelationshipSystem.PerformMarriage
        // / VisualNovelDialogueSystem.HandleMarriageProposal / IntimacySystem
        // .CheckForPregnancy / EnhancedNPCBehaviors.IsCompatibleForMarriage
        // / WorldSimulator.FindAffairPartner all needed the answer; none had it.
        //
        // The helper is intentionally a single chokepoint so the rule set stays
        // consistent across all eight call sites (mirror of how AlignmentSystem
        // .ChangeAlignment became the single source of truth in v0.57.0).
        //
        // Adoption policy (per the design doc decision): adopted children COUNT
        // as blood for the incest gate. The game already treats adoption as
        // moral parenthood (custody on divorce, alignment events, parenting
        // scenarios). Reading the OriginalMother / OriginalFather fields lets
        // us also catch the case where someone adopts THEIR OWN biological kid
        // back from the orphanage and we still need to refuse marriage to them.
        //
        // Sibling marriage policy: HARD BLOCK for slice 1. Can soften to a
        // Targaryen-style soft block (allow with Chivalry penalty) in a future
        // slice if Dark playthroughs ask for it. Hard is the safer default.
        //
        // Self-check: included as defense in depth. PerformMarriage already
        // checks character1 == character2 but other call sites don't.

        /// <summary>
        /// How character A is related to character B from A's perspective.
        /// For example, Parent means "A is B's parent".
        /// </summary>
        public enum FamilyRelation
        {
            None,
            Self,
            Parent,
            Child,
            Sibling,        // shares both biological parents
            HalfSibling,    // shares exactly one biological parent
            Grandparent,
            Grandchild,
            AdoptiveParent,
            AdoptiveChild,
            AdoptiveSibling
        }

        /// <summary>
        /// Returns true for relations that block marriage / intimacy / pregnancy
        /// in the v0.63.0 slice 1 rule set. Adoption is treated as blood here
        /// (game already treats it as moral parenthood elsewhere). Sibling
        /// marriage is a hard block; cousins are not tracked at depth so they
        /// pass through.
        /// </summary>
        public static bool IsBlockingRelation(FamilyRelation rel)
        {
            return rel switch
            {
                FamilyRelation.Self => true,
                FamilyRelation.Parent => true,
                FamilyRelation.Child => true,
                FamilyRelation.Sibling => true,
                FamilyRelation.HalfSibling => true,
                FamilyRelation.Grandparent => true,
                FamilyRelation.Grandchild => true,
                FamilyRelation.AdoptiveParent => true,
                FamilyRelation.AdoptiveChild => true,
                FamilyRelation.AdoptiveSibling => true,
                _ => false,
            };
        }

        /// <summary>
        /// Returns the family relation of a relative to character `from`, or None
        /// if not blood-related (or adoptive-related). Handles four matchup
        /// shapes: Player-Player, Player-NPC, NPC-Player, NPC-NPC.
        ///
        /// For Player-Player and Player-NPC the canonical store is the Child
        /// registry (`_children`). For NPC-NPC we read the lineage fields on
        /// the NPCs themselves (populated at ConvertChildToNPC / OrphanBecomesNPC
        /// from v0.63.0; empty on legacy NPCs, which is fine -- the gate just
        /// won't catch incest between two pre-v0.63.0 NPCs, but new-cycle
        /// births / graduations will tag correctly going forward).
        /// </summary>
        public FamilyRelation GetFamilyRelation(Character a, Character b)
        {
            if (a == null || b == null) return FamilyRelation.None;

            // Self check (defense in depth)
            if (ReferenceEquals(a, b)) return FamilyRelation.Self;
            if (!string.IsNullOrEmpty(a.ID) && a.ID == b.ID) return FamilyRelation.Self;
            if (!string.IsNullOrEmpty(a.Name) && a.Name == b.Name && a.GetType() == b.GetType())
                return FamilyRelation.Self;

            // Parent-Child via Child registry (covers Player parent of any
            // child / adult-NPC-child). Try both directions.
            if (IsParentOfViaChildRegistry(a, b)) return FamilyRelation.Parent;
            if (IsParentOfViaChildRegistry(b, a)) return FamilyRelation.Child;

            // Parent-Child via NPC lineage fields (covers NPC parent of
            // adult-NPC-child, both populated by ConvertChildToNPC).
            if (a is NPC npcA && IsParentOfViaNPCLineage(b, npcA)) return FamilyRelation.Child;
            if (b is NPC npcB && IsParentOfViaNPCLineage(a, npcB)) return FamilyRelation.Parent;

            // Sibling check: both share at least one parent.
            var aParents = GetParentNamesOf(a);
            var bParents = GetParentNamesOf(b);
            if (aParents.Count > 0 && bParents.Count > 0)
            {
                int shared = 0;
                foreach (var p in aParents)
                    if (!string.IsNullOrEmpty(p) && bParents.Contains(p)) shared++;
                if (shared >= 2) return FamilyRelation.Sibling;
                if (shared == 1) return FamilyRelation.HalfSibling;
            }

            // Grandparent / Grandchild: check if either party appears in the
            // OTHER's grandparent set. Walk one generation up.
            foreach (var grandparentName in GetGrandparentNamesOf(a))
            {
                if (!string.IsNullOrEmpty(grandparentName) &&
                    (grandparentName == b.Name || grandparentName == GetDisplayNameOf(b)))
                    return FamilyRelation.Grandchild;
            }
            foreach (var grandparentName in GetGrandparentNamesOf(b))
            {
                if (!string.IsNullOrEmpty(grandparentName) &&
                    (grandparentName == a.Name || grandparentName == GetDisplayNameOf(a)))
                    return FamilyRelation.Grandparent;
            }

            return FamilyRelation.None;
        }

        /// <summary>
        /// Convenience: returns true if the two characters cannot marry / mate
        /// because of a family tie. The canonical incest gate.
        /// </summary>
        public bool AreBloodRelated(Character a, Character b)
        {
            return IsBlockingRelation(GetFamilyRelation(a, b));
        }

        /// <summary>
        /// Returns true if the given NPC is an adult child of the given parent.
        /// Convenience for display-tagging surfaces ("(your daughter)" markers).
        /// Matches by both ID and name to survive id-drift (v0.54.0 lesson).
        /// </summary>
        public bool IsAdultChildOf(NPC? npc, Character? parent)
        {
            if (npc == null || parent == null) return false;
            return IsParentOfViaNPCLineage(parent, npc)
                || IsParentOfViaChildRegistry(parent, npc);
        }

        /// <summary>
        /// Returns all living adult NPCs whose lineage fields point at the given
        /// parent. Combined source: NPC.MotherID/FatherID match (post-v0.63.0
        /// data) OR Child registry has a Deleted record whose Name + BirthDate
        /// match an active NPC (legacy heal path). De-duplicates by NPC.ID.
        /// </summary>
        public List<NPC> GetAdultChildrenOf(Character parent)
        {
            if (parent == null) return new List<NPC>();
            var active = NPCSpawnSystem.Instance?.ActiveNPCs;
            if (active == null || active.Count == 0) return new List<NPC>();

            var seen = new HashSet<string>();
            var results = new List<NPC>();

            // Direct lineage-field match (canonical)
            foreach (var npc in active)
            {
                if (npc == null || npc.IsDead) continue;
                if (IsParentOfViaNPCLineage(parent, npc))
                {
                    if (seen.Add(npc.ID ?? npc.Name2 ?? ""))
                        results.Add(npc);
                }
            }

            // Fallback via Child registry (covers legacy saves where lineage
            // wasn't populated at graduation). Match Deleted children to
            // living NPCs by Name + BirthDate.
            foreach (var child in _children.Where(c => c.Deleted && IsParentMatchOnChild(c, parent)))
            {
                var match = active.FirstOrDefault(n =>
                    n != null && !n.IsDead &&
                    n.Name2 == child.Name && n.BirthDate == child.BirthDate);
                if (match != null && seen.Add(match.ID ?? match.Name2 ?? ""))
                    results.Add(match);
            }

            return results;
        }

        /// <summary>
        /// Returns a localized "(your daughter)" / "(your son)" / "(your child)"
        /// display tag for an NPC who is the player's adult child, or "" if
        /// not related. Sex determines pronoun; falls back to neutral "child"
        /// if sex is unknown. Used by Talk header / examine screens / etc.
        /// </summary>
        public string GetChildTagFor(NPC? npc, Character? viewer)
        {
            if (!IsAdultChildOf(npc, viewer)) return "";
            return npc!.Sex switch
            {
                CharacterSex.Female => Loc.Get("family.tag_your_daughter"),
                CharacterSex.Male => Loc.Get("family.tag_your_son"),
                _ => Loc.Get("family.tag_your_child"),
            };
        }

        // ====================================================================
        // v0.63.0 slice 3 D1: Patriarch / Matriarch standing ladder.
        // Derived live from the player's adult children -- no new save field
        // (Phase-2 Dread / Renown precedent in v0.62.0). Tiers escalate with
        // count; the Bloodline tier additionally requires alignment purity
        // (all 5+ adult children consistently virtuous or consistently evil).
        // Each tier grants a small passive bonus surfaced on the `[%]` status
        // sheet so the player knows what their family has bought them.
        // ====================================================================

        public enum DynastyTier
        {
            None,
            Founder,           // 1+ adult children
            Patriarch,         // 3+ adult children
            Bloodline,         // 5+ adult children, all consistent soul alignment
            Dynasty,           // 5+ adult children AND >= 1 NPC-NPC grandchild
        }

        /// <summary>
        /// Compute the player's current dynasty tier from the live adult-children
        /// set. Pure read; no mutation. Returns DynastyTier.None for players who
        /// haven't graduated any kids yet (which is the common case for any
        /// character below mid-game).
        /// </summary>
        public DynastyTier GetDynastyTier(Character parent)
        {
            if (parent == null) return DynastyTier.None;
            var adults = GetAdultChildrenOf(parent);
            int count = adults.Count;
            if (count == 0) return DynastyTier.None;
            if (count < 3) return DynastyTier.Founder;
            if (count < 5) return DynastyTier.Patriarch;

            // 5+ adult children: check for alignment purity (all virtuous OR all
            // evil at graduation -- Bloodline is the "pure ___ legacy" tier).
            bool allVirtuous = adults.All(n => n.SoulAtGraduation > 100);
            bool allEvil = adults.All(n => n.SoulAtGraduation < -100);
            bool pureLine = allVirtuous || allEvil;

            // Dynasty requires Bloodline + at least one NPC-NPC grandchild.
            // A grandchild lives in the Child registry as a graduated NPC whose
            // parent name matches one of OUR adult children's display names.
            if (pureLine && HasAnyGrandchild(adults)) return DynastyTier.Dynasty;
            if (pureLine) return DynastyTier.Bloodline;

            // 5+ adults but mixed alignment: cap at Patriarch (the legacy is
            // muddled; a Bloodline reading of you doesn't fit).
            return DynastyTier.Patriarch;
        }

        /// <summary>
        /// True if any of the player's adult children has produced their OWN
        /// child (a grandchild from the player's perspective). Walks the
        /// Child registry; matches either by parent's display name (covers
        /// graduated-NPC parents) or by ID. Reads NPC.MotherName / FatherName
        /// on living adult-NPC parents too, so post-graduation NPC-NPC births
        /// also count.
        /// </summary>
        private bool HasAnyGrandchild(List<NPC> myAdultChildren)
        {
            if (myAdultChildren == null || myAdultChildren.Count == 0) return false;
            // Build the name set of the player's adult kids ONCE.
            var myNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var adult in myAdultChildren)
            {
                if (!string.IsNullOrEmpty(adult.Name2)) myNames.Add(adult.Name2);
                if (!string.IsNullOrEmpty(adult.Name1)) myNames.Add(adult.Name1);
            }

            // (a) Child registry: any non-deleted minor whose Mother / Father
            // name matches one of my adult children.
            foreach (var child in _children)
            {
                if (child.Deleted) continue;
                if (!string.IsNullOrEmpty(child.Mother) && myNames.Contains(child.Mother)) return true;
                if (!string.IsNullOrEmpty(child.Father) && myNames.Contains(child.Father)) return true;
            }
            // (b) Living NPC roster: any NPC whose MotherName / FatherName
            // matches (covers the case where the grandchild has already
            // graduated to adult-NPC status itself).
            var active = NPCSpawnSystem.Instance?.ActiveNPCs;
            if (active != null)
            {
                foreach (var npc in active)
                {
                    if (npc == null || npc.IsDead) continue;
                    if (!string.IsNullOrEmpty(npc.MotherName) && myNames.Contains(npc.MotherName)) return true;
                    if (!string.IsNullOrEmpty(npc.FatherName) && myNames.Contains(npc.FatherName)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Localized display name for the dynasty tier, gendered by player sex
        /// where the language differentiates ("Patriarch" vs "Matriarch").
        /// Returns empty string for DynastyTier.None.
        /// </summary>
        public string GetDynastyTierName(DynastyTier tier, CharacterSex sex)
        {
            if (tier == DynastyTier.None) return "";
            // Patriarch is the only tier where the noun changes by sex.
            if (tier == DynastyTier.Patriarch && sex == CharacterSex.Female)
                return Loc.Get("dynasty.tier_matriarch");
            return Loc.Get($"dynasty.tier_{tier.ToString().ToLowerInvariant()}");
        }

        /// <summary>
        /// Per-tier passive bonuses are mostly QoL flavor (faster NPC reaction,
        /// small discount at relevant shops). These are derived as a multiplier
        /// the consumers query at point of use; there's no save field to drift.
        ///   Founder:   +5% friendly-NPC starting band shift
        ///   Patriarch: +10% friendly-NPC starting band shift
        ///   Bloodline: +15% friendly-NPC starting band shift + small XP%
        ///   Dynasty:   +20% friendly-NPC starting band shift + small XP% + Gold drip
        /// Slice 3 ships the readout + the small consumer hooks for the first
        /// two; the XP and gold drip from Bloodline / Dynasty are slice-3b polish.
        /// </summary>
        public int GetDynastyReactionBonus(Character parent)
        {
            return GetDynastyTier(parent) switch
            {
                DynastyTier.Founder => 5,
                DynastyTier.Patriarch => 10,
                DynastyTier.Bloodline => 15,
                DynastyTier.Dynasty => 20,
                _ => 0,
            };
        }

        /// <summary>
        /// v0.63.1: did `killer` permanently kill one of `viewer`'s family
        /// members? Drives the family-grudge consumers: refuses to talk,
        /// refuses to join your team, attacks on sight if dark-aligned.
        /// Reads from the NPC's memory store rather than the Enemies list so
        /// it works regardless of whether the killer was stored by display
        /// name or by NPC ID. Killer matching is by display name (NPC.Name2
        /// / Character.Name); the death cascade records the same string into
        /// MemoryEvent.InvolvedCharacter, so a string compare resolves.
        /// </summary>
        public static bool HasGrudgeAgainst(NPC viewer, Character killer)
        {
            if (viewer == null || killer == null || viewer.Brain?.Memory == null) return false;

            string killerName = killer.Name2 ?? killer.Name ?? "";
            string killerName1 = killer.Name1 ?? "";
            if (string.IsNullOrEmpty(killerName) && string.IsNullOrEmpty(killerName1))
                return false;

            // Scan the family-tie memory types. Both KilledMyParent and
            // KilledMyFamily are written with the killer's display name in
            // InvolvedCharacter (WorldSimulator.RecordFamilyDeath sites).
            foreach (var mem in viewer.Brain.Memory.AllMemories)
            {
                if (mem.Type != global::MemoryType.KilledMyParent
                    && mem.Type != global::MemoryType.KilledMyFamily) continue;

                string involved = mem.InvolvedCharacter ?? "";
                if (string.IsNullOrEmpty(involved)) continue;

                if ((killerName.Length > 0 && involved == killerName)
                    || (killerName1.Length > 0 && involved == killerName1))
                    return true;
            }
            return false;
        }

        // ---- Internal helpers ----

        private static bool IsParentMatchOnChild(Child child, Character parent)
        {
            if (child == null || parent == null) return false;
            var name = parent.Name ?? "";
            var id = parent.ID ?? "";
            return (!string.IsNullOrEmpty(id) && (child.MotherID == id || child.FatherID == id))
                || (!string.IsNullOrEmpty(name) && (child.Mother == name || child.Father == name))
                || (!string.IsNullOrEmpty(name) && (child.OriginalMother == name || child.OriginalFather == name));
        }

        private bool IsParentOfViaChildRegistry(Character parent, Character maybeChild)
        {
            if (parent == null || maybeChild == null) return false;
            // Look up the Child record for maybeChild. Match by Name + BirthDate
            // when maybeChild is an NPC (set at graduation), else by Name only.
            var name = GetDisplayNameOf(maybeChild);
            if (string.IsNullOrEmpty(name)) return false;

            foreach (var child in _children)
            {
                if (child.Name != name) continue;
                if (maybeChild is NPC npc && npc.BirthDate != DateTime.MinValue
                    && child.BirthDate != DateTime.MinValue
                    && child.BirthDate != npc.BirthDate) continue;

                if (IsParentMatchOnChild(child, parent)) return true;
            }
            return false;
        }

        private static bool IsParentOfViaNPCLineage(Character parent, NPC child)
        {
            if (parent == null || child == null) return false;
            var pName = parent.Name ?? "";
            var pId = parent.ID ?? "";
            // Canonical fields
            if (!string.IsNullOrEmpty(pId) && (child.MotherID == pId || child.FatherID == pId)) return true;
            if (!string.IsNullOrEmpty(pName) && (child.MotherName == pName || child.FatherName == pName)) return true;
            // Biological preserved fields (covers adopted-back-from-orphanage case)
            if (!string.IsNullOrEmpty(pName) && (child.OriginalMotherName == pName || child.OriginalFatherName == pName)) return true;
            return false;
        }

        private List<string> GetParentNamesOf(Character ch)
        {
            var results = new List<string>(4);
            if (ch is NPC npc)
            {
                if (!string.IsNullOrEmpty(npc.MotherName)) results.Add(npc.MotherName);
                if (!string.IsNullOrEmpty(npc.FatherName)) results.Add(npc.FatherName);
                if (!string.IsNullOrEmpty(npc.OriginalMotherName) && !results.Contains(npc.OriginalMotherName))
                    results.Add(npc.OriginalMotherName);
                if (!string.IsNullOrEmpty(npc.OriginalFatherName) && !results.Contains(npc.OriginalFatherName))
                    results.Add(npc.OriginalFatherName);
            }
            // Child registry view (for Players, or NPCs whose lineage wasn't backfilled)
            var displayName = GetDisplayNameOf(ch);
            foreach (var child in _children.Where(c => c.Name == displayName))
            {
                if (!string.IsNullOrEmpty(child.Mother) && !results.Contains(child.Mother)) results.Add(child.Mother);
                if (!string.IsNullOrEmpty(child.Father) && !results.Contains(child.Father)) results.Add(child.Father);
                if (!string.IsNullOrEmpty(child.OriginalMother) && !results.Contains(child.OriginalMother)) results.Add(child.OriginalMother);
                if (!string.IsNullOrEmpty(child.OriginalFather) && !results.Contains(child.OriginalFather)) results.Add(child.OriginalFather);
            }
            return results;
        }

        private List<string> GetGrandparentNamesOf(Character ch)
        {
            var results = new List<string>(8);
            var active = NPCSpawnSystem.Instance?.ActiveNPCs;
            // For each known parent, look up THEIR parents.
            foreach (var parentName in GetParentNamesOf(ch))
            {
                if (string.IsNullOrEmpty(parentName)) continue;

                // Parent's own Child record (graduated NPC) carries grandparent names
                foreach (var child in _children.Where(c => c.Name == parentName))
                {
                    if (!string.IsNullOrEmpty(child.Mother) && !results.Contains(child.Mother)) results.Add(child.Mother);
                    if (!string.IsNullOrEmpty(child.Father) && !results.Contains(child.Father)) results.Add(child.Father);
                }

                // Parent's NPC lineage (if parent is a graduated NPC)
                var parentNpc = active?.FirstOrDefault(n => n.Name2 == parentName || n.Name1 == parentName);
                if (parentNpc != null)
                {
                    if (!string.IsNullOrEmpty(parentNpc.MotherName) && !results.Contains(parentNpc.MotherName))
                        results.Add(parentNpc.MotherName);
                    if (!string.IsNullOrEmpty(parentNpc.FatherName) && !results.Contains(parentNpc.FatherName))
                        results.Add(parentNpc.FatherName);
                }
            }
            return results;
        }

        private static string GetDisplayNameOf(Character ch)
        {
            if (ch == null) return "";
            // NPCs use Name2 as display name (Name1 may be a recruit-time alias).
            if (ch is NPC npc && !string.IsNullOrEmpty(npc.Name2)) return npc.Name2;
            return ch.Name ?? "";
        }

        #endregion

        #region Child Bonuses

        /// <summary>
        /// Get the stat bonuses a player receives from their children
        /// Children under 18 provide bonuses that scale with the number of children
        /// Bonuses are removed when children come of age
        /// </summary>
        public ChildBonuses CalculateChildBonuses(Character parent)
        {
            var children = GetChildrenOf(parent);
            var minorChildren = children.Where(c => c.Age < ADULT_AGE).ToList();

            if (minorChildren.Count == 0)
                return new ChildBonuses();

            // Base bonus per child (diminishing returns after 5 children)
            int childCount = minorChildren.Count;
            float effectiveChildren = childCount <= 5 ? childCount : 5 + (childCount - 5) * 0.5f;

            var bonuses = new ChildBonuses
            {
                ChildCount = childCount,
                // +2% XP bonus per effective child (max ~15% at 10 children)
                XPMultiplier = 1.0f + (effectiveChildren * 0.02f),
                // +50 max HP per effective child (motivation to protect family)
                BonusMaxHP = (int)(effectiveChildren * 50),
                // +5 strength per effective child (parental determination)
                BonusStrength = (int)(effectiveChildren * 5),
                // +3 charisma per effective child (family status)
                BonusCharisma = (int)(effectiveChildren * 3),
                // +100 gold income per child (family enterprise, child labor in medieval times)
                DailyGoldBonus = childCount * 100
            };

            // Royal children provide additional prestige bonuses
            int royalChildren = minorChildren.Count(c => c.Royal > 0);
            if (royalChildren > 0)
            {
                bonuses.BonusCharisma += royalChildren * 5; // Extra charisma for royal heirs
                bonuses.DailyGoldBonus += royalChildren * 500; // Royal stipend
            }

            // Children's moral influence on the parent is handled as a one-shot
            // alignment event when the child is born / reaches a milestone, NOT as a
            // continuously-applied bonus. The previous Chivalry/Darkness fields here
            // were applied via += on every RecalculateStats() call without being reset
            // to base, so they accumulated permanently and slammed both alignment scales
            // into the v0.57.12 cap. See v0.57.17 release notes: a player report of
            // Darkness racing to the 1000 cap within hours of NG+ start surfaced this.

            return bonuses;
        }

        /// <summary>
        /// Apply child bonuses to a player's stats
        /// Should be called during stat recalculation
        /// </summary>
        public void ApplyChildBonuses(Character player)
        {
            if (player == null) return;

            var bonuses = CalculateChildBonuses(player);
            if (bonuses.ChildCount == 0) return;

            // Apply stat bonuses. These ARE safe under repeated RecalculateStats()
            // calls because MaxHP/Strength/Charisma are reset to their Base* values at
            // the top of RecalculateStats() before this method runs.
            // Chivalry/Darkness are NOT reset by RecalculateStats() — they're persistent
            // alignment, not derived stats — so we no longer touch them here. Children
            // affect alignment through one-shot events, not a recalc bonus.
            player.MaxHP += bonuses.BonusMaxHP;
            player.Strength += bonuses.BonusStrength;
            player.Charisma += bonuses.BonusCharisma;

            // Note: XP multiplier and daily gold bonus are applied elsewhere
            // XP: in CombatEngine when awarding XP
            // Gold: in DailyManager during daily income calculation
        }

        /// <summary>
        /// Get the XP multiplier from children (for use in combat XP calculation)
        /// </summary>
        public float GetChildXPMultiplier(Character player)
        {
            if (player == null) return 1.0f;
            return CalculateChildBonuses(player).XPMultiplier;
        }

        /// <summary>
        /// Get daily gold bonus from children (for use in daily income)
        /// </summary>
        public int GetChildDailyGoldBonus(Character player)
        {
            if (player == null) return 0;
            return CalculateChildBonuses(player).DailyGoldBonus;
        }

        /// <summary>
        /// Disown every child currently parented by the named character. Used when
        /// the player NG+'s in online mode — the new character is a fresh life and
        /// shouldn't see kids from the previous cycle waiting at home.
        ///
        /// In single-player mode FamilySystem.Reset() wipes _children entirely, so
        /// disown is unnecessary there. In online mode children are world data
        /// shared across players (settled, registered with NPC parents, possibly
        /// adopted by other players) — we can't just delete them. Instead we clear
        /// the matching parent slot. If both parent slots are now empty, the child
        /// moves to the orphanage so the existing orphan pipeline picks them up.
        ///
        /// OriginalMother / OriginalFather are immutable (used by orphan stories
        /// for narrative continuity) so the child's biological history is preserved.
        ///
        /// Returns the number of children disowned for logging.
        /// </summary>
        public int DisownChildrenOf(string playerName)
        {
            if (string.IsNullOrEmpty(playerName)) return 0;

            var matches = _children
                .Where(c => !c.Deleted && (c.Mother == playerName || c.Father == playerName))
                .ToList();
            return DisownChildrenInner(matches, playerName, playerId: null);
        }

        /// <summary>
        /// v0.63.0 slice 4 (audit npc-N3): ID-first overload. Pre-fix, only the
        /// name overload existed and a renamed player or an NPC sharing the
        /// player's display name could trip a false match. ID-first matches
        /// the priority that the rest of the family code uses (invariant 2).
        /// </summary>
        public int DisownChildrenOf(Character player)
        {
            if (player == null) return 0;
            string playerName = player.Name ?? "";
            string playerId = player.ID ?? "";

            var matches = _children
                .Where(c => !c.Deleted && (
                    (!string.IsNullOrEmpty(playerId) && (c.MotherID == playerId || c.FatherID == playerId))
                    || c.Mother == playerName
                    || c.Father == playerName))
                .ToList();
            return DisownChildrenInner(matches, playerName, playerId);
        }

        private int DisownChildrenInner(List<Child> matches, string playerName, string? playerId)
        {
            int disowned = 0;
            int orphaned = 0;
            foreach (var child in matches)
            {
                // v0.63.0 slice 4 (audit npc-N3): ID-first match for which slot to clear.
                bool wasMother = child.Mother == playerName
                    || (!string.IsNullOrEmpty(playerId) && child.MotherID == playerId);
                bool wasFather = child.Father == playerName
                    || (!string.IsNullOrEmpty(playerId) && child.FatherID == playerId);

                if (wasMother)
                {
                    child.Mother = "";
                    child.MotherID = "";
                }
                if (wasFather)
                {
                    child.Father = "";
                    child.FatherID = "";
                }

                disowned++;

                // If both parent slots are empty AND child is still a minor, send to orphanage.
                // Adult children (Age >= ADULT_AGE) are already independent — disown is just bookkeeping.
                if (string.IsNullOrEmpty(child.Mother) && string.IsNullOrEmpty(child.Father)
                    && child.Age < ADULT_AGE
                    && child.Location != GameConfig.ChildLocationOrphanage)
                {
                    child.Location = GameConfig.ChildLocationOrphanage;
                    orphaned++;
                }
            }

            if (disowned > 0)
            {
                DebugLogger.Instance?.LogInfo("FAMILY",
                    $"NG+ disown: {disowned} children disowned by {playerName}, {orphaned} sent to orphanage");
            }

            return disowned;
        }

        #endregion

        /// <summary>
        /// Process daily aging for all children
        /// Called from MaintenanceSystem or WorldSimulator
        /// </summary>
        public void ProcessDailyAging()
        {
            var childrenToAge = _children.Where(c => !c.Deleted && c.Age < ADULT_AGE).ToList();

            foreach (var child in childrenToAge)
            {
                int previousAge = child.Age;

                // Calculate age based on hours since birth (accelerated lifecycle rate)
                var hoursSinceBirth = (DateTime.Now - child.BirthDate).TotalHours;
                child.Age = (int)(hoursSinceBirth / GameConfig.NpcLifecycleHoursPerYear);

                // Check if child just came of age
                if (previousAge < ADULT_AGE && child.Age >= ADULT_AGE)
                {
                    // Orphanage children are handled by the orphanage coming-of-age system
                    if (child.Location == GameConfig.ChildLocationOrphanage)
                        continue;
                    ConvertChildToNPC(child);
                }
            }

            // Coming-of-age retry / queue drain: any non-deleted child already at or
            // over ADULT_AGE that hasn't been converted yet. This covers two cases:
            //   1. Children frozen at 18 by the old bug (conversion was skipped at the
            //      population cap and never retried, because the loop above only ages
            //      Age < ADULT_AGE children).
            //   2. Children currently parked "away at higher learning" (Location =
            //      ChildLocationAway) waiting for population room.
            // Each tick we retry ConvertChildToNPC: if the population now has room they
            // graduate into a real world NPC (marked Deleted); if it is still at cap
            // they (re)park in the Away state. Orphanage children have their own
            // coming-of-age path and are left alone.
            var stuckAdults = _children
                .Where(c => !c.Deleted && c.Age >= ADULT_AGE
                    && c.Location != GameConfig.ChildLocationOrphanage)
                .ToList();
            foreach (var child in stuckAdults)
                ConvertChildToNPC(child);
        }

        /// <summary>
        /// Convert a child who has come of age into an NPC.
        ///
        /// v0.63.0 rewrite (relationship completion slice 1) does five things
        /// the v0.62.x version didn't:
        ///   (1) Populates lineage fields (MotherName / FatherName / MotherID
        ///       / FatherID / OriginalMotherName / OriginalFatherName /
        ///       SoulAtGraduation / WasRaisedByPlayer) so the graduated NPC
        ///       remembers who its parents were -- prerequisite for incest
        ///       gate AND for adult-child recognition dialogue.
        ///   (2) Sets Base* stats so RecalculateStats() doesn't zero the NPC
        ///       on first equip / load (the C3 pre-existing bug from the
        ///       npc-system-reviewer audit).
        ///   (3) Adds the missing CON / WIS / DEX / MaxMana stats (C4 --
        ///       graduated Magicians had Mana=0; graduated Clerics had WIS=0;
        ///       INT-scaled abilities fizzled).
        ///   (4) Disambiguates name against living NPCs to avoid silent
        ///       Name2 collisions (the M4 finding).
        ///   (5) Computes Experience from level (defense in depth -- if a
        ///       future change ever bumps starting level above 1, the field
        ///       was previously stranded at 0).
        /// </summary>
        private void ConvertChildToNPC(Child child)
        {
            // Idempotency: skip if already converted (prevents duplicate NPCs across save/load)
            if (child.Deleted) return;

            // Population cap reached: we can't add another world NPC right now, but
            // the child has still come of age. Rather than freeze them as a phantom
            // age-18 "child" (the original bug) OR delete them (which would feel like
            // the game erased the player's kid), park them in an "away" state -- they
            // have left home for higher learning / service abroad. They stay on the
            // family roster (shown as away, not as a dependent at home) and the
            // ProcessDailyAging self-heal pass retries the conversion each tick, so
            // they graduate into a real world NPC as soon as the population frees up.
            if ((NPCSpawnSystem.Instance?.ActiveNPCs.Count ?? 0) >= GameConfig.MaxNPCPopulation)
            {
                child.Location = GameConfig.ChildLocationAway;
                return;
            }
            var existingAdult = NPCSpawnSystem.Instance?.ActiveNPCs
                ?.FirstOrDefault(n => n.Name2 == child.Name && n.BirthDate == child.BirthDate);
            if (existingAdult != null) { child.Deleted = true; return; }

            // Disambiguate the display name against any living NPC. Without this,
            // a child named "Aldric Hammerhand" graduating while an immigrant NPC
            // of the same name already exists ends up with two NPCs sharing Name2 --
            // RomanceTracker / NPCMarriageRegistry name-fallback lookups (per the
            // recurring v0.54 ID-drift fix) become ambiguous.
            string displayName = NPCSpawnSystem.Instance?.DisambiguateNPCName(child.Name) ?? child.Name;

            int level = 1;
            int strength = 10 + Random.Shared.Next(5);
            int defence = 10 + Random.Shared.Next(5);
            int stamina = 10 + Random.Shared.Next(5);
            int agility = 10 + Random.Shared.Next(5);
            int dexterity = 10 + Random.Shared.Next(5);
            int constitution = 10 + Random.Shared.Next(5);
            int intelligence = 10 + Random.Shared.Next(5);
            int wisdom = 10 + Random.Shared.Next(5);
            int charisma = 10 + Random.Shared.Next(5);
            int maxHP = 100;
            // Caster classes get a real mana pool; non-casters get 0.
            int maxMana = IsCasterClass(DetermineChildClass(child)) ? 50 + intelligence * 2 : 0;

            // Create NPC from child
            var npc = new NPC
            {
                // Assign unique ID (critical for relationship/family tracking)
                ID = $"npc_{displayName.ToLower().Replace(" ", "_")}_{Guid.NewGuid().ToString("N").Substring(0, 8)}",
                Name1 = displayName,
                Name2 = displayName,
                Sex = child.Sex,
                Age = ADULT_AGE,
                Race = DetermineChildRace(child),
                Class = DetermineChildClass(child),
                Level = level,
                Experience = GameConfig.GetExperienceForLevel(level),
                HP = maxHP,
                MaxHP = maxHP,
                BaseMaxHP = maxHP,
                MaxMana = maxMana,
                BaseMaxMana = maxMana,
                Strength = strength,
                BaseStrength = strength,
                Defence = defence,
                BaseDefence = defence,
                Stamina = stamina,
                BaseStamina = stamina,
                Agility = agility,
                BaseAgility = agility,
                Dexterity = dexterity,
                BaseDexterity = dexterity,
                Constitution = constitution,
                BaseConstitution = constitution,
                Intelligence = intelligence,
                BaseIntelligence = intelligence,
                Wisdom = wisdom,
                BaseWisdom = wisdom,
                Charisma = charisma,
                BaseCharisma = charisma,
                Gold = 100,
                CurrentLocation = "Main Street",
                AI = CharacterAI.Computer,
                BirthDate = child.BirthDate, // Carry over birth date for lifecycle aging
                // Lineage fields (v0.63.0 slice 1)
                MotherName = child.Mother ?? "",
                FatherName = child.Father ?? "",
                MotherID = child.MotherID ?? "",
                FatherID = child.FatherID ?? "",
                OriginalMotherName = child.OriginalMother ?? "",
                OriginalFatherName = child.OriginalFather ?? "",
                SoulAtGraduation = child.Soul,
                WasRaisedByPlayer = IsParentSlotAPlayer(child)
            };

            // Inherit some traits based on soul
            if (child.Soul > 200)
            {
                npc.Chivalry = 50 + Random.Shared.Next(50);
                npc.Darkness = 0;
            }
            else if (child.Soul < -200)
            {
                npc.Chivalry = 0;
                npc.Darkness = 50 + Random.Shared.Next(50);
            }
            else
            {
                npc.Chivalry = 25;
                npc.Darkness = 25;
            }

            // Royal blood gives bonuses (apply to both Base and live -- they were
            // already in sync from the constructor block above).
            if (child.Royal > 0)
            {
                int charismaBonus = 10 * child.Royal;
                npc.Charisma += charismaBonus;
                npc.BaseCharisma += charismaBonus;
                npc.Gold += 1000 * child.Royal;
            }

            // Create personality based on child's soul - use GenerateForArchetype to get romance traits
            var personality = PersonalityProfile.GenerateForArchetype("commoner");
            personality.Gender = child.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male;
            // The archetype roll used the default (Male) gender, so a female who rolled the
            // same-sex band is mislabeled Gay -- reconcile to Lesbian before the diversity nudge.
            personality.SyncOrientationToGender();
            if (child.Soul > 100)
            {
                personality.Aggression = 0.2f;
                personality.Tenderness = 0.8f;  // Good kids are tender
                personality.Greed = 0.2f;
            }
            else if (child.Soul < -100)
            {
                personality.Aggression = 0.7f;
                personality.Tenderness = 0.2f;  // Bad kids are less tender
                personality.Greed = 0.7f;
            }

            npc.Personality = personality;
            npc.Brain = new NPCBrain(npc, personality);

            // Pull the new adult toward whatever diversity gap currently exists in
            // the living population. Without this nudge, every child reaching 18
            // gets the default ~95/2/3 roll from GenerateForArchetype and the
            // long-running population trends straight as the diversity-floored
            // initial roster ages out.
            NPCSpawnSystem.Instance?.NudgeLateJoinerOrientation(npc);

            // Assign faction based on class, alignment, and personality
            npc.NPCFaction = NPCSpawnSystem.Instance?.DetermineFactionForNPC(npc);

            // Register with NPC system
            NPCSpawnSystem.Instance?.AddRestoredNPC(npc);

            // Mark child as "graduated" to adult
            child.Deleted = true;

            // Generate news
            NewsSystem.Instance?.WriteComingOfAgeNews(child.Name, child.Mother, child.Father);
        }

        /// <summary>
        /// True if either parent slot points at a Character.ID that matches a
        /// known player. Used to set WasRaisedByPlayer at graduation.
        /// </summary>
        private static bool IsParentSlotAPlayer(Child child)
        {
            // Match the player-ID convention used elsewhere in the codebase.
            // Players have non-empty Character.ID that does NOT start with "npc_"
            // (NPC IDs are prefixed). This isn't perfect for hand-edited saves
            // but it's the same heuristic the rest of the family code uses.
            bool motherIsPlayer = !string.IsNullOrEmpty(child.MotherID)
                && !child.MotherID.StartsWith("npc_", StringComparison.OrdinalIgnoreCase);
            bool fatherIsPlayer = !string.IsNullOrEmpty(child.FatherID)
                && !child.FatherID.StartsWith("npc_", StringComparison.OrdinalIgnoreCase);
            return motherIsPlayer || fatherIsPlayer;
        }

        /// <summary>
        /// True if the class needs a mana pool for spells. Mirrors the gating
        /// used by SpellSystem.HasSpells for the core spellcasting classes.
        /// </summary>
        private static bool IsCasterClass(CharacterClass cls)
        {
            return cls == CharacterClass.Magician
                || cls == CharacterClass.Cleric
                || cls == CharacterClass.Sage
                || cls == CharacterClass.Paladin
                || cls == CharacterClass.Bard
                || cls == CharacterClass.MysticShaman;
        }

        /// <summary>
        /// Determine race based on parents
        /// </summary>
        private CharacterRace DetermineChildRace(Child child)
        {
            // Try to find parents and inherit race
            var mother = FindParentByID(child.MotherID) ?? FindParentByName(child.Mother);
            var father = FindParentByID(child.FatherID) ?? FindParentByName(child.Father);

            if (mother != null && father != null)
            {
                // 50/50 chance of inheriting either parent's race
                return Random.Shared.Next(2) == 0 ? mother.Race : father.Race;
            }
            else if (mother != null)
            {
                return mother.Race;
            }
            else if (father != null)
            {
                return father.Race;
            }

            return CharacterRace.Human; // Default
        }

        /// <summary>
        /// Determine class based on parents and soul
        /// </summary>
        private CharacterClass DetermineChildClass(Child child)
        {
            var random = Random.Shared;
            var race = DetermineChildRace(child);
            GameConfig.InvalidCombinations.TryGetValue(race, out var invalid);

            CharacterClass[] candidates;
            if (child.Soul > 200)
            {
                // Good children become paladins, clerics, warriors
                candidates = new[] { CharacterClass.Paladin, CharacterClass.Cleric, CharacterClass.Warrior };
            }
            else if (child.Soul < -200)
            {
                // Evil children become assassins, magicians, barbarians
                candidates = new[] { CharacterClass.Assassin, CharacterClass.Magician, CharacterClass.Barbarian };
            }
            else
            {
                // Neutral children - any base class
                var baseClasses = new List<CharacterClass> {
                    CharacterClass.Warrior, CharacterClass.Magician, CharacterClass.Assassin,
                    CharacterClass.Ranger, CharacterClass.Bard, CharacterClass.Sage,
                    CharacterClass.Cleric, CharacterClass.Alchemist, CharacterClass.Jester,
                    CharacterClass.Barbarian, CharacterClass.Paladin
                };
                // MysticShaman available for Troll, Orc, Gnoll children
                if (race == CharacterRace.Troll || race == CharacterRace.Orc || race == CharacterRace.Gnoll)
                    baseClasses.Add(CharacterClass.MysticShaman);
                candidates = baseClasses.ToArray();
            }

            // Filter out invalid race/class combos
            if (invalid != null)
                candidates = candidates.Where(c => !invalid.Contains(c)).ToArray();

            // Fallback to Warrior if all candidates invalid for this race
            return candidates.Length > 0 ? candidates[random.Next(candidates.Length)] : CharacterClass.Warrior;
        }

        /// <summary>
        /// Find parent character by ID
        /// </summary>
        private Character? FindParentByID(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            // Check NPCs
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == id);
            if (npc != null) return npc;

            // Check current player
            var player = GameEngine.Instance?.CurrentPlayer;
            if (player?.ID == id) return player;

            return null;
        }

        /// <summary>
        /// Find parent character by name
        /// </summary>
        private Character? FindParentByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            // Check NPCs
            var npc = NPCSpawnSystem.Instance?.GetNPCByName(name);
            if (npc != null) return npc;

            // Check current player
            var player = GameEngine.Instance?.CurrentPlayer;
            if (player?.Name == name || player?.Name2 == name) return player;

            return null;
        }

        /// <summary>
        /// Handle custody transfer after divorce
        /// </summary>
        public void HandleDivorceCustody(Character parent1, Character parent2, Character custodialParent)
        {
            var children = GetChildrenOfCouple(parent1, parent2);
            var losingParent = custodialParent.ID == parent1.ID ? parent2 : parent1;

            foreach (var child in children)
            {
                child.HandleDivorceCustody(losingParent, custodialParent);
            }

            if (children.Count > 0)
            {
                NewsSystem.Instance?.Newsy(true,
                    $"{custodialParent.Name} has been granted custody of {children.Count} child(ren) in the divorce.");
            }
        }

        #region NPC-NPC Children

        // Fantasy name pools for NPC children
        private static readonly string[] MaleNames = new[]
        {
            "Aldric", "Bram", "Caelum", "Dorin", "Eldric", "Fenris", "Gareth", "Hadwin",
            "Ivar", "Jorund", "Kael", "Leoric", "Magnus", "Noric", "Osric", "Perrin",
            "Quillan", "Rowan", "Soren", "Theron", "Ulric", "Varen", "Wulfric", "Xander",
            "Yorick", "Zephyr", "Alaric", "Brandt", "Cedric", "Darian", "Erland", "Finnian",
            "Gideon", "Halvar", "Iskander", "Jarek", "Korbin", "Lysander", "Malakai", "Nolan"
        };

        private static readonly string[] FemaleNames = new[]
        {
            "Aelara", "Brielle", "Calista", "Darina", "Elara", "Freya", "Gwyneth", "Helena",
            "Isolde", "Jocelyn", "Kira", "Lyria", "Mirena", "Nessa", "Orina", "Petra",
            "Rhiannon", "Seraphina", "Thalia", "Ursula", "Vesper", "Wren", "Ysolde", "Zara",
            "Astrid", "Brianna", "Celeste", "Dahlia", "Elowen", "Fiora", "Guinevere", "Hilda",
            "Ingrid", "Juliana", "Katarina", "Lucinda", "Morgana", "Nerissa", "Ondine", "Rosalind"
        };

        // Fantasy surnames for children whose fathers have no extractable surname
        // (alias NPCs, single-name NPCs, title-only NPCs, etc.)
        private static readonly string[] GeneratedSurnames = new[]
        {
            "Ashford", "Blackthorn", "Copperfield", "Dunmore", "Everhart",
            "Fairwind", "Greymane", "Holloway", "Ironwood", "Kettleburn",
            "Larkwood", "Mossgrove", "Northgate", "Oakshield", "Pennywhistle",
            "Quickwater", "Ravenswood", "Silverbrook", "Thornbury", "Underhill",
            "Whitmore", "Yarrow", "Ashwick", "Bramblewood", "Coldstream",
            "Dewberry", "Emberglow", "Foxglove", "Greystone", "Hawkridge",
            "Ivywood", "Juniper", "Kingsmill", "Larkspur", "Meadowbrook",
            "Nighthollow", "Oldfield", "Pinecrest", "Ravenscroft", "Stoneleigh"
        };

        /// <summary>
        /// Create a child from two NPC parents. Uses the existing Child class and aging system.
        /// When the child reaches ADULT_AGE (18 game-years), ConvertChildToNPC() will
        /// automatically turn them into a full NPC that joins the realm.
        /// </summary>
        public void CreateNPCChild(Character mother, Character father)
        {
            var rng = Random.Shared;
            var sex = rng.Next(2) == 0 ? CharacterSex.Male : CharacterSex.Female;

            var firstName = GenerateNPCChildFirstName(sex);
            var fatherSurname = ExtractSurname(father.Name2 ?? father.Name);
            // If father has no extractable surname, generate one deterministically
            if (fatherSurname == null)
                fatherSurname = GenerateSurnameForParent(father.Name2 ?? father.Name);
            var childName = $"{firstName} {fatherSurname}";

            var child = new Child
            {
                Name = childName,
                Mother = mother.Name2 ?? mother.Name,
                Father = father.Name2 ?? father.Name,
                MotherID = mother.ID ?? "",
                FatherID = father.ID ?? "",
                Sex = sex,
                Age = 0,
                BirthDate = DateTime.Now,
                Health = 100,
                Soul = rng.Next(-200, 201),
                Location = GameConfig.ChildLocationHome,
                Named = true,
            };

            RegisterChild(child);

            NewsSystem.Instance?.WriteBirthNews(
                mother.Name2 ?? mother.Name,
                father.Name2 ?? father.Name,
                child.Name, true);

            DebugLogger.Instance.LogInfo("LIFECYCLE",
                $"NPC child born: {child.Name} ({child.Sex}), parents: {mother.Name2} & {father.Name2}");
        }

        /// <summary>
        /// Pick a random first name for an NPC child.
        /// Surnames provide uniqueness, so first names can repeat across families.
        /// </summary>
        private string GenerateNPCChildFirstName(CharacterSex sex)
        {
            var rng = Random.Shared;
            var names = sex == CharacterSex.Female ? FemaleNames : MaleNames;
            return names[rng.Next(names.Length)];
        }

        /// <summary>
        /// Generate a deterministic surname for a parent who has no extractable one.
        /// Uses a stable hash (not GetHashCode which is randomized per-process in .NET Core).
        /// Same parent name always produces the same surname across restarts.
        /// </summary>
        private static string GenerateSurnameForParent(string parentName)
        {
            // DJB2 hash — stable and deterministic across all runs
            uint hash = 5381;
            foreach (char c in parentName)
                hash = ((hash << 5) + hash) + c;
            return GeneratedSurnames[hash % (uint)GeneratedSurnames.Length];
        }

        /// <summary>
        /// Extract a surname from an NPC name for children to inherit.
        /// Returns null for single names, titles ("The X"), epithets ("X the Y"),
        /// and alias-style names ("Nimble Nick", "Scarface Sam").
        /// Examples: "Borin Hammerhand" → "Hammerhand", "Shadow" → null,
        /// "The Stranger" → null, "Nimble Nick" → null.
        /// </summary>
        private static string? ExtractSurname(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return null;

            // Alias-style names where the last word is a first name, not a surname.
            // These are criminal/rogue NPCs known by nicknames, not real names.
            if (AliasNames.Contains(fullName)) return null;

            var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;

            // Skip title-style names: "The Stranger", "The Executioner"
            if (parts[0].Equals("The", StringComparison.OrdinalIgnoreCase)) return null;

            // Skip epithet-style names: "Vex the Merciless", "Sir Cedric the Pure"
            for (int i = 1; i < parts.Length - 1; i++)
            {
                if (parts[i].Equals("the", StringComparison.OrdinalIgnoreCase)) return null;
            }

            // Skip "Sir/Lady/Brother/Sister/Mother/Father/Master" prefix + single word
            // e.g. "Sir Galahad" has no surname, but "Aldric Stormcaller" does
            var titlePrefixes = new[] { "Sir", "Lady", "Brother", "Sister", "Mother", "Father", "Master", "Lord", "Archpriest", "Zen" };
            if (titlePrefixes.Any(t => parts[0].Equals(t, StringComparison.OrdinalIgnoreCase)))
            {
                // "Sir Darius the Lost" already caught above by "the" check
                // "Archpriest Aldwyn" — only 2 parts with title prefix, no surname
                if (parts.Length == 2) return null;
                // "Sir Borin Hammerhand" → "Hammerhand"
                return parts[^1];
            }

            // Standard "FirstName LastName" → take the last part
            return parts[^1];
        }

        // NPC names that are aliases/nicknames — the last word is a first name, not a surname.
        private static readonly HashSet<string> AliasNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Nimble Nick", "Gold Tooth Gary", "Scarface Sam", "Quicksilver Quinn",
            "Pickpocket Pete", "Slick Rick", "Lucky Lou", "Dagger Dee"
        };

        #endregion

        /// <summary>
        /// Serialize children for save system
        /// </summary>
        public List<ChildData> SerializeChildren()
        {
            return _children.Where(c => !c.Deleted).Select(c => new ChildData
            {
                Name = c.Name,
                Mother = c.Mother,
                Father = c.Father,
                MotherID = c.MotherID,
                FatherID = c.FatherID,
                OriginalMother = c.OriginalMother,
                OriginalFather = c.OriginalFather,
                Sex = (int)c.Sex,
                Age = c.Age,
                BirthDate = c.BirthDate,
                Named = c.Named,
                Location = c.Location,
                Health = c.Health,
                Soul = c.Soul,
                MotherAccess = c.MotherAccess,
                FatherAccess = c.FatherAccess,
                Kidnapped = c.Kidnapped,
                KidnapperName = c.KidnapperName,
                RansomDemanded = c.RansomDemanded,
                CursedByGod = c.CursedByGod,
                Royal = c.Royal,
                LastParentingDay = c.LastParentingDay,
                LastParentingTime = c.LastParentingTime
            }).ToList();
        }

        /// <summary>
        /// Deserialize children from save system
        /// </summary>
        public void DeserializeChildren(List<ChildData> childDataList)
        {
            _children.Clear();

            if (childDataList == null) return;

            foreach (var data in childDataList)
            {
                var child = new Child
                {
                    Name = data.Name,
                    Mother = data.Mother,
                    Father = data.Father,
                    MotherID = data.MotherID,
                    FatherID = data.FatherID,
                    OriginalMother = data.OriginalMother,
                    OriginalFather = data.OriginalFather,
                    Sex = (CharacterSex)data.Sex,
                    Age = data.Age,
                    BirthDate = data.BirthDate,
                    Named = data.Named,
                    Location = data.Location,
                    Health = data.Health,
                    Soul = data.Soul,
                    MotherAccess = data.MotherAccess,
                    FatherAccess = data.FatherAccess,
                    Kidnapped = data.Kidnapped,
                    KidnapperName = data.KidnapperName,
                    RansomDemanded = data.RansomDemanded,
                    CursedByGod = data.CursedByGod,
                    Royal = data.Royal,
                    LastParentingDay = data.LastParentingDay,
                    LastParentingTime = data.LastParentingTime
                };

                // Prevent duplicates (same birth date + same parents)
                if (!_children.Any(c => c.BirthDate == child.BirthDate && c.MotherID == child.MotherID && c.FatherID == child.FatherID))
                    _children.Add(child);
            }

            // GD.Print($"[Family] Loaded {_children.Count} children from save");

            // Migration: add father's surname to children who don't have one yet
            MigrateChildSurnames();
        }

        /// <summary>
        /// Migration: compute correct name for each child — first name + father's
        /// surname (extracted or generated). Strips all Roman numerals. Every child
        /// ends up with a two-part name. Safe to run multiple times (idempotent).
        /// </summary>
        private void MigrateChildSurnames()
        {
            foreach (var child in _children)
            {
                if (child.Deleted) continue;
                if (string.IsNullOrWhiteSpace(child.Father)) continue;

                // Extract first name (always the first word)
                var nameParts = child.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (nameParts.Length == 0) continue;
                var firstName = nameParts[0];

                // Compute the correct surname from father (extract or generate)
                var surname = ExtractSurname(child.Father) ?? GenerateSurnameForParent(child.Father);

                var correctName = $"{firstName} {surname}";

                if (!child.Name.Equals(correctName, StringComparison.Ordinal))
                {
                    var oldName = child.Name;
                    child.Name = correctName;
                    DebugLogger.Instance.LogInfo("FAMILY",
                        $"Migrated child name: '{oldName}' → '{child.Name}' (father: {child.Father})");
                }
            }
        }

        /// <summary>
        /// Reset family system (for new game)
        /// </summary>
        public void Reset()
        {
            _children.Clear();
        }

        /// <summary>
        /// Get family summary for a character
        /// </summary>
        public string GetFamilySummary(Character character)
        {
            var children = GetChildrenOf(character);
            if (children.Count == 0)
            {
                return $"{character.Name} has no children.";
            }

            var summary = $"{character.Name} has {children.Count} child(ren):\n";
            foreach (var child in children)
            {
                summary += $"  - {child.Name}, age {child.Age}, {child.GetSoulDescription()}\n";
            }
            return summary;
        }
    }

    #region Parenting Scenario System

    public enum ChildAgeGroup { Toddler, Child, Teen }

    public class ParentingChoice
    {
        public string LabelKey { get; init; } = "";
        public int SoulChange { get; init; }
        public string ResultKey { get; init; } = "";
        public bool IsVirtuous { get; init; }
        public bool IsNeutral { get; init; }
    }

    public class ParentingScenario
    {
        public string Id { get; init; } = "";
        public ChildAgeGroup AgeGroup { get; init; }
        public string DescriptionKey { get; init; } = "";
        public ParentingChoice[] Choices { get; init; } = Array.Empty<ParentingChoice>();
    }

    /// <summary>
    /// Static pool of 24 CK-style parenting scenarios (8 per age group).
    /// Each presents a moral dilemma with 3 choices that affect child soul.
    /// </summary>
    public static class ParentingScenarios
    {
        public static ChildAgeGroup GetAgeGroup(int age)
        {
            if (age <= GameConfig.ParentingToddlerMaxAge) return ChildAgeGroup.Toddler;
            if (age <= GameConfig.ParentingChildMaxAge) return ChildAgeGroup.Child;
            return ChildAgeGroup.Teen;
        }

        public static ParentingScenario GetRandomScenario(Child child)
        {
            var group = GetAgeGroup(child.Age);
            var candidates = AllScenarios.Where(s => s.AgeGroup == group).ToArray();
            return candidates[Random.Shared.Next(candidates.Length)];
        }

        /// <summary>
        /// Calculate how parent alignment modifies the soul change.
        /// Virtuous parents amplify virtuous lessons; dark parents amplify cruel ones.
        /// Mismatched alignment reduces effectiveness.
        /// </summary>
        public static int CalculateAlignmentModifier(Character parent, ParentingChoice choice)
        {
            if (choice.IsNeutral) return 0;

            int alignmentBalance = (int)Math.Clamp(parent.Chivalry - parent.Darkness, -1000, 1000);

            if (choice.IsVirtuous)
            {
                if (alignmentBalance > 0)
                    return Math.Min(GameConfig.ParentingAlignmentBonusMax, alignmentBalance / 20);
                else
                    return -Math.Min(GameConfig.ParentingAlignmentPenaltyMax, Math.Abs(alignmentBalance) / 30);
            }
            else // cruel choice
            {
                if (alignmentBalance < 0)
                    return -Math.Min(GameConfig.ParentingAlignmentBonusMax, Math.Abs(alignmentBalance) / 20); // amplify negative
                else
                    return Math.Min(GameConfig.ParentingAlignmentPenaltyMax, alignmentBalance / 30); // reduce negative
            }
        }

        // ──────────── TODDLER SCENARIOS (ages 0-4) ────────────

        private static readonly ParentingScenario[] ToddlerScenarios = new[]
        {
            new ParentingScenario
            {
                Id = "toddler_toy_stolen", AgeGroup = ChildAgeGroup.Toddler,
                DescriptionKey = "home.parenting.toddler_toy_stolen.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.toddler_toy_stolen.c1", SoulChange = 20, ResultKey = "home.parenting.toddler_toy_stolen.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_toy_stolen.c2", SoulChange = -15, ResultKey = "home.parenting.toddler_toy_stolen.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_toy_stolen.c3", SoulChange = -10, ResultKey = "home.parenting.toddler_toy_stolen.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "toddler_crying_night", AgeGroup = ChildAgeGroup.Toddler,
                DescriptionKey = "home.parenting.toddler_crying_night.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.toddler_crying_night.c1", SoulChange = 20, ResultKey = "home.parenting.toddler_crying_night.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_crying_night.c2", SoulChange = -15, ResultKey = "home.parenting.toddler_crying_night.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_crying_night.c3", SoulChange = 5, ResultKey = "home.parenting.toddler_crying_night.r3", IsNeutral = true },
                }
            },
            new ParentingScenario
            {
                Id = "toddler_sharing", AgeGroup = ChildAgeGroup.Toddler,
                DescriptionKey = "home.parenting.toddler_sharing.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.toddler_sharing.c1", SoulChange = 20, ResultKey = "home.parenting.toddler_sharing.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_sharing.c2", SoulChange = -10, ResultKey = "home.parenting.toddler_sharing.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_sharing.c3", SoulChange = -20, ResultKey = "home.parenting.toddler_sharing.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "toddler_animal", AgeGroup = ChildAgeGroup.Toddler,
                DescriptionKey = "home.parenting.toddler_animal.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.toddler_animal.c1", SoulChange = 25, ResultKey = "home.parenting.toddler_animal.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_animal.c2", SoulChange = -20, ResultKey = "home.parenting.toddler_animal.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_animal.c3", SoulChange = 0, ResultKey = "home.parenting.toddler_animal.r3", IsNeutral = true },
                }
            },
            new ParentingScenario
            {
                Id = "toddler_tantrum", AgeGroup = ChildAgeGroup.Toddler,
                DescriptionKey = "home.parenting.toddler_tantrum.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.toddler_tantrum.c1", SoulChange = 15, ResultKey = "home.parenting.toddler_tantrum.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_tantrum.c2", SoulChange = -15, ResultKey = "home.parenting.toddler_tantrum.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_tantrum.c3", SoulChange = -5, ResultKey = "home.parenting.toddler_tantrum.r3", IsNeutral = true },
                }
            },
            new ParentingScenario
            {
                Id = "toddler_stranger", AgeGroup = ChildAgeGroup.Toddler,
                DescriptionKey = "home.parenting.toddler_stranger.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.toddler_stranger.c1", SoulChange = 15, ResultKey = "home.parenting.toddler_stranger.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_stranger.c2", SoulChange = -20, ResultKey = "home.parenting.toddler_stranger.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_stranger.c3", SoulChange = 5, ResultKey = "home.parenting.toddler_stranger.r3", IsNeutral = true },
                }
            },
            new ParentingScenario
            {
                Id = "toddler_drawing", AgeGroup = ChildAgeGroup.Toddler,
                DescriptionKey = "home.parenting.toddler_drawing.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.toddler_drawing.c1", SoulChange = 20, ResultKey = "home.parenting.toddler_drawing.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_drawing.c2", SoulChange = -20, ResultKey = "home.parenting.toddler_drawing.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_drawing.c3", SoulChange = 0, ResultKey = "home.parenting.toddler_drawing.r3", IsNeutral = true },
                }
            },
            new ParentingScenario
            {
                Id = "toddler_curse_word", AgeGroup = ChildAgeGroup.Toddler,
                DescriptionKey = "home.parenting.toddler_curse_word.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.toddler_curse_word.c1", SoulChange = 15, ResultKey = "home.parenting.toddler_curse_word.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_curse_word.c2", SoulChange = -10, ResultKey = "home.parenting.toddler_curse_word.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_curse_word.c3", SoulChange = -20, ResultKey = "home.parenting.toddler_curse_word.r3" },
                }
            },
        };

        // ──────────── CHILD SCENARIOS (ages 5-12) ────────────

        private static readonly ParentingScenario[] ChildScenarios = new[]
        {
            new ParentingScenario
            {
                Id = "child_stealing", AgeGroup = ChildAgeGroup.Child,
                DescriptionKey = "home.parenting.child_stealing.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.child_stealing.c1", SoulChange = 20, ResultKey = "home.parenting.child_stealing.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_stealing.c2", SoulChange = -15, ResultKey = "home.parenting.child_stealing.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.child_stealing.c3", SoulChange = -20, ResultKey = "home.parenting.child_stealing.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "child_bully", AgeGroup = ChildAgeGroup.Child,
                DescriptionKey = "home.parenting.child_bully.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.child_bully.c1", SoulChange = 20, ResultKey = "home.parenting.child_bully.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_bully.c2", SoulChange = -10, ResultKey = "home.parenting.child_bully.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.child_bully.c3", SoulChange = 5, ResultKey = "home.parenting.child_bully.r3", IsNeutral = true },
                }
            },
            new ParentingScenario
            {
                Id = "child_lie", AgeGroup = ChildAgeGroup.Child,
                DescriptionKey = "home.parenting.child_lie.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.child_lie.c1", SoulChange = 20, ResultKey = "home.parenting.child_lie.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_lie.c2", SoulChange = -15, ResultKey = "home.parenting.child_lie.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.child_lie.c3", SoulChange = -25, ResultKey = "home.parenting.child_lie.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "child_stray", AgeGroup = ChildAgeGroup.Child,
                DescriptionKey = "home.parenting.child_stray.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.child_stray.c1", SoulChange = 20, ResultKey = "home.parenting.child_stray.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_stray.c2", SoulChange = 0, ResultKey = "home.parenting.child_stray.r2", IsNeutral = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_stray.c3", SoulChange = -25, ResultKey = "home.parenting.child_stray.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "child_fight", AgeGroup = ChildAgeGroup.Child,
                DescriptionKey = "home.parenting.child_fight.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.child_fight.c1", SoulChange = 15, ResultKey = "home.parenting.child_fight.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_fight.c2", SoulChange = -10, ResultKey = "home.parenting.child_fight.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.child_fight.c3", SoulChange = -15, ResultKey = "home.parenting.child_fight.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "child_beggar", AgeGroup = ChildAgeGroup.Child,
                DescriptionKey = "home.parenting.child_beggar.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.child_beggar.c1", SoulChange = 25, ResultKey = "home.parenting.child_beggar.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_beggar.c2", SoulChange = -20, ResultKey = "home.parenting.child_beggar.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.child_beggar.c3", SoulChange = 15, ResultKey = "home.parenting.child_beggar.r3", IsVirtuous = true },
                }
            },
            new ParentingScenario
            {
                Id = "child_broken", AgeGroup = ChildAgeGroup.Child,
                DescriptionKey = "home.parenting.child_broken.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.child_broken.c1", SoulChange = 20, ResultKey = "home.parenting.child_broken.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_broken.c2", SoulChange = -20, ResultKey = "home.parenting.child_broken.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.child_broken.c3", SoulChange = -15, ResultKey = "home.parenting.child_broken.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "child_death_question", AgeGroup = ChildAgeGroup.Child,
                DescriptionKey = "home.parenting.child_death_question.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.child_death_question.c1", SoulChange = 20, ResultKey = "home.parenting.child_death_question.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_death_question.c2", SoulChange = 5, ResultKey = "home.parenting.child_death_question.r2", IsNeutral = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_death_question.c3", SoulChange = -20, ResultKey = "home.parenting.child_death_question.r3" },
                }
            },
        };

        // ──────────── TEEN SCENARIOS (ages 13-17) ────────────

        private static readonly ParentingScenario[] TeenScenarios = new[]
        {
            new ParentingScenario
            {
                Id = "teen_guard", AgeGroup = ChildAgeGroup.Teen,
                DescriptionKey = "home.parenting.teen_guard.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.teen_guard.c1", SoulChange = 20, ResultKey = "home.parenting.teen_guard.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_guard.c2", SoulChange = 5, ResultKey = "home.parenting.teen_guard.r2", IsNeutral = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_guard.c3", SoulChange = -15, ResultKey = "home.parenting.teen_guard.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "teen_romance", AgeGroup = ChildAgeGroup.Teen,
                DescriptionKey = "home.parenting.teen_romance.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.teen_romance.c1", SoulChange = 20, ResultKey = "home.parenting.teen_romance.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_romance.c2", SoulChange = -15, ResultKey = "home.parenting.teen_romance.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.teen_romance.c3", SoulChange = -20, ResultKey = "home.parenting.teen_romance.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "teen_faith", AgeGroup = ChildAgeGroup.Teen,
                DescriptionKey = "home.parenting.teen_faith.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.teen_faith.c1", SoulChange = 20, ResultKey = "home.parenting.teen_faith.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_faith.c2", SoulChange = -15, ResultKey = "home.parenting.teen_faith.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.teen_faith.c3", SoulChange = 10, ResultKey = "home.parenting.teen_faith.r3", IsNeutral = true },
                }
            },
            new ParentingScenario
            {
                Id = "teen_drinking", AgeGroup = ChildAgeGroup.Teen,
                DescriptionKey = "home.parenting.teen_drinking.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.teen_drinking.c1", SoulChange = 15, ResultKey = "home.parenting.teen_drinking.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_drinking.c2", SoulChange = -20, ResultKey = "home.parenting.teen_drinking.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.teen_drinking.c3", SoulChange = -15, ResultKey = "home.parenting.teen_drinking.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "teen_runaway", AgeGroup = ChildAgeGroup.Teen,
                DescriptionKey = "home.parenting.teen_runaway.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.teen_runaway.c1", SoulChange = 25, ResultKey = "home.parenting.teen_runaway.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_runaway.c2", SoulChange = -20, ResultKey = "home.parenting.teen_runaway.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.teen_runaway.c3", SoulChange = -10, ResultKey = "home.parenting.teen_runaway.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "teen_dark_alley", AgeGroup = ChildAgeGroup.Teen,
                DescriptionKey = "home.parenting.teen_dark_alley.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.teen_dark_alley.c1", SoulChange = 20, ResultKey = "home.parenting.teen_dark_alley.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_dark_alley.c2", SoulChange = 5, ResultKey = "home.parenting.teen_dark_alley.r2", IsNeutral = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_dark_alley.c3", SoulChange = -25, ResultKey = "home.parenting.teen_dark_alley.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "teen_injustice", AgeGroup = ChildAgeGroup.Teen,
                DescriptionKey = "home.parenting.teen_injustice.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.teen_injustice.c1", SoulChange = 25, ResultKey = "home.parenting.teen_injustice.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_injustice.c2", SoulChange = -10, ResultKey = "home.parenting.teen_injustice.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.teen_injustice.c3", SoulChange = -25, ResultKey = "home.parenting.teen_injustice.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "teen_inheritance", AgeGroup = ChildAgeGroup.Teen,
                DescriptionKey = "home.parenting.teen_inheritance.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.teen_inheritance.c1", SoulChange = -10, ResultKey = "home.parenting.teen_inheritance.r1" },
                    new ParentingChoice { LabelKey = "home.parenting.teen_inheritance.c2", SoulChange = 20, ResultKey = "home.parenting.teen_inheritance.r2", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_inheritance.c3", SoulChange = -20, ResultKey = "home.parenting.teen_inheritance.r3" },
                }
            },
        };

        public static ParentingScenario[] AllScenarios { get; } =
            ToddlerScenarios.Concat(ChildScenarios).Concat(TeenScenarios).ToArray();
    }

    #endregion
}
