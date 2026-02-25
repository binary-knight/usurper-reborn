using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Romance Tracker System - Tracks all romantic relationships, lovers, spouses,
    /// and intimate encounter history for the player and NPCs.
    /// </summary>
    public class RomanceTracker
    {
        private static RomanceTracker? _fallbackInstance;
        public static RomanceTracker Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.Romance;
                return _fallbackInstance ??= new RomanceTracker();
            }
        }

        // Current relationships
        public List<Lover> CurrentLovers { get; set; } = new();
        public List<Spouse> Spouses { get; set; } = new();  // Supports multiple if poly
        public List<string> FriendsWithBenefits { get; set; } = new();

        // History
        public List<string> Exes { get; set; } = new();  // Simple list of ex-lover IDs
        public List<ExSpouse> ExSpouses { get; set; } = new();  // Detailed ex-spouse records
        public List<IntimateEncounter> EncounterHistory { get; set; } = new();

        // Tracking
        public Dictionary<string, int> JealousyLevels { get; set; } = new();
        public Dictionary<string, RelationshipStructure> AgreedStructures { get; set; } = new();

        // Complex arrangements
        public List<CuckoldArrangement> CuckArrangements { get; set; } = new();
        public List<PolyNetwork> PolyNetworks { get; set; } = new();

        public RomanceTracker()
        {
            _fallbackInstance = this;
        }

        /// <summary>
        /// Reset all romance data - call when starting new game or loading different player
        /// </summary>
        public void Reset()
        {
            // Log reset with stack trace so we can see where it's being called from
            DebugLogger.Instance.LogWarning("ROMANCE", $"RESET called! Had {Spouses.Count} spouses, {CurrentLovers.Count} lovers");

            CurrentLovers.Clear();
            Spouses.Clear();
            FriendsWithBenefits.Clear();
            Exes.Clear();
            ExSpouses.Clear();
            EncounterHistory.Clear();
            JealousyLevels.Clear();
            AgreedStructures.Clear();
            CuckArrangements.Clear();
            PolyNetworks.Clear();
        }

        /// <summary>
        /// Add a new lover to the tracker
        /// </summary>
        public void AddLover(string npcId, int initialLoveLevel = 30, bool isExclusive = false)
        {
            if (CurrentLovers.Any(l => l.NPCId == npcId))
            {
                // GD.Print($"[Romance] {npcId} is already a lover");
                return;
            }

            var lover = new Lover
            {
                NPCId = npcId,
                NPCName = GetNPCName(npcId),  // Cache the name at time of adding
                LoveLevel = initialLoveLevel,
                IsExclusive = isExclusive,
                KnowsAboutOthers = !isExclusive,
                RelationshipStart = DateTime.Now
            };

            CurrentLovers.Add(lover);
            // GD.Print($"[Romance] New lover added: {npcId}");

            // Track archetype - Lover
            ArchetypeTracker.Instance.RecordRomanceEncounter(wasIntimate: false);

            // Check jealousy of other lovers
            CheckJealousyTriggers(npcId);
        }

        /// <summary>
        /// Add a spouse (marriage)
        /// </summary>
        public void AddSpouse(string npcId, bool isPolyMarriage = false)
        {
            if (Spouses.Any(s => s.NPCId == npcId))
            {
                // GD.Print($"[Romance] Already married to {npcId}");
                return;
            }

            // Check if this is allowed
            if (Spouses.Count > 0 && !isPolyMarriage)
            {
                // Check if current spouse consents to polyamory
                var currentSpouse = Spouses.FirstOrDefault();
                if (currentSpouse != null && !currentSpouse.AcceptsPolyamory)
                {
                    // GD.Print($"[Romance] Cannot marry {npcId} - current spouse does not accept polyamory");
                    return;
                }
            }

            var spouse = new Spouse
            {
                NPCId = npcId,
                NPCName = GetNPCName(npcId),  // Cache the name at time of marriage
                MarriedDate = DateTime.Now,
                LoveLevel = 20, // Start at Love level
                AcceptsPolyamory = isPolyMarriage,
                Children = 0
            };

            Spouses.Add(spouse);

            // Log marriage event
            DebugLogger.Instance.LogInfo("ROMANCE", $"MARRIED: {spouse.NPCName} (ID: {npcId}) - Total spouses: {Spouses.Count}");

            // Remove from lovers list if present
            CurrentLovers.RemoveAll(l => l.NPCId == npcId);

            // Track archetype - Marriage is a major Lover event
            ArchetypeTracker.Instance.RecordMarriage();
        }

        /// <summary>
        /// Add a friends-with-benefits relationship
        /// </summary>
        public void AddFWB(string npcId)
        {
            if (!FriendsWithBenefits.Contains(npcId))
            {
                FriendsWithBenefits.Add(npcId);
                // GD.Print($"[Romance] FWB relationship with {npcId}");
            }
        }

        /// <summary>
        /// End a relationship and move to exes
        /// </summary>
        public void EndRelationship(string npcId, string reason = "mutual")
        {
            bool wasLover = CurrentLovers.RemoveAll(l => l.NPCId == npcId) > 0;
            bool wasFWB = FriendsWithBenefits.Remove(npcId);

            if (wasLover || wasFWB)
            {
                if (!Exes.Contains(npcId))
                    Exes.Add(npcId);

                // GD.Print($"[Romance] Relationship ended with {npcId}: {reason}");
            }
        }

        /// <summary>
        /// Divorce a spouse - preserves marriage history in ExSpouses list
        /// </summary>
        public void Divorce(string npcId, string reason = "Irreconcilable differences", bool playerInitiated = true)
        {
            var spouse = Spouses.FirstOrDefault(s => s.NPCId == npcId);
            if (spouse != null)
            {
                // Create ex-spouse record to preserve marriage history
                var exSpouse = new ExSpouse
                {
                    NPCId = spouse.NPCId,
                    NPCName = !string.IsNullOrEmpty(spouse.NPCName) ? spouse.NPCName : GetNPCName(spouse.NPCId),
                    MarriedDate = spouse.MarriedDate,
                    DivorceDate = DateTime.Now,
                    ChildrenTogether = spouse.Children,
                    DivorceReason = reason,
                    PlayerInitiated = playerInitiated
                };

                // Add to ex-spouses list (check for duplicates)
                if (!ExSpouses.Any(e => e.NPCId == npcId))
                {
                    ExSpouses.Add(exSpouse);
                }

                // Remove from current spouses
                Spouses.Remove(spouse);

                // Also add to simple Exes list for backwards compatibility
                if (!Exes.Contains(npcId))
                    Exes.Add(npcId);

                // GD.Print($"[Romance] Divorced from {spouse.NPCName ?? npcId} (Reason: {reason}, Children: {spouse.Children})");
            }
        }

        /// <summary>
        /// Record an intimate encounter
        /// </summary>
        public void RecordEncounter(IntimateEncounter encounter)
        {
            EncounterHistory.Add(encounter);

            // Update love levels for participants
            foreach (var partnerId in encounter.PartnerIds)
            {
                var lover = CurrentLovers.FirstOrDefault(l => l.NPCId == partnerId);
                if (lover != null)
                {
                    lover.LoveLevel = Math.Max(10, lover.LoveLevel - 2); // Increase love (lower number = better)
                    lover.LastIntimateDate = encounter.Date;
                }

                var spouse = Spouses.FirstOrDefault(s => s.NPCId == partnerId);
                if (spouse != null)
                {
                    spouse.LastIntimateDate = encounter.Date;
                }
            }

            // Track archetype - Intimate encounter is major Lover event
            ArchetypeTracker.Instance.RecordRomanceEncounter(wasIntimate: true);

            // GD.Print($"[Romance] Intimate encounter recorded: {encounter.Type} with {string.Join(", ", encounter.PartnerIds)}");
        }

        /// <summary>
        /// Check if player is in a relationship with this NPC
        /// </summary>
        public bool IsInRelationshipWith(string npcId)
        {
            return CurrentLovers.Any(l => l.NPCId == npcId) ||
                   Spouses.Any(s => s.NPCId == npcId) ||
                   FriendsWithBenefits.Contains(npcId);
        }

        /// <summary>
        /// Check if player is in any romantic relationship (not marriage) with this NPC
        /// </summary>
        public bool IsPlayerInRelationshipWith(string npcId)
        {
            return CurrentLovers.Any(l => l.NPCId == npcId) ||
                   FriendsWithBenefits.Contains(npcId);
        }

        /// <summary>
        /// Check if player is married to this NPC
        /// </summary>
        public bool IsPlayerMarriedTo(string npcId)
        {
            return Spouses.Any(s => s.NPCId == npcId);
        }

        /// <summary>
        /// Handle spouse death - records as widow/widower, preserves memory
        /// </summary>
        public void HandleSpouseDeath(string npcId)
        {
            var spouse = Spouses.FirstOrDefault(s => s.NPCId == npcId);
            if (spouse != null)
            {
                // Create ex-spouse record to preserve marriage history (marked as death)
                var exSpouse = new ExSpouse
                {
                    NPCId = spouse.NPCId,
                    NPCName = !string.IsNullOrEmpty(spouse.NPCName) ? spouse.NPCName : GetNPCName(spouse.NPCId),
                    MarriedDate = spouse.MarriedDate,
                    DivorceDate = DateTime.Now, // Death date
                    ChildrenTogether = spouse.Children,
                    DivorceReason = "Died in combat",
                    PlayerInitiated = false
                };

                // Add to ex-spouses list
                if (!ExSpouses.Any(e => e.NPCId == npcId))
                {
                    ExSpouses.Add(exSpouse);
                }

                // Remove from current spouses
                Spouses.Remove(spouse);

                // GD.Print($"[Romance] Spouse {spouse.NPCName ?? npcId} has died. Player is now a widow/widower.");
            }
        }

        /// <summary>
        /// Get the relationship type with an NPC
        /// </summary>
        public RomanceRelationType GetRelationType(string npcId)
        {
            if (Spouses.Any(s => s.NPCId == npcId))
                return RomanceRelationType.Spouse;
            if (CurrentLovers.Any(l => l.NPCId == npcId))
                return RomanceRelationType.Lover;
            if (FriendsWithBenefits.Contains(npcId))
                return RomanceRelationType.FWB;
            if (Exes.Contains(npcId))
                return RomanceRelationType.Ex;
            return RomanceRelationType.None;
        }

        /// <summary>
        /// Check jealousy triggers when starting new relationship
        /// </summary>
        private void CheckJealousyTriggers(string newLoverId)
        {
            // Check spouses
            foreach (var spouse in Spouses)
            {
                if (!spouse.AcceptsPolyamory && !spouse.KnowsAboutOthers)
                {
                    JealousyLevels[spouse.NPCId] = Math.Min(100, (JealousyLevels.GetValueOrDefault(spouse.NPCId, 0)) + 30);
                    // GD.Print($"[Romance] {spouse.NPCId} is getting jealous!");
                }
            }

            // Check exclusive lovers
            foreach (var lover in CurrentLovers.Where(l => l.IsExclusive && l.NPCId != newLoverId))
            {
                if (!lover.KnowsAboutOthers)
                {
                    JealousyLevels[lover.NPCId] = Math.Min(100, (JealousyLevels.GetValueOrDefault(lover.NPCId, 0)) + 20);
                    // GD.Print($"[Romance] {lover.NPCId} is getting jealous!");
                }
            }
        }

        /// <summary>
        /// Process jealousy consequences - called during daily maintenance or game tick.
        /// High jealousy can lead to relationship damage, confrontations, or breakups.
        /// </summary>
        /// <param name="player">The player character (required for relationship updates)</param>
        /// <returns>List of consequence messages to display to the player</returns>
        public List<string> ProcessJealousyConsequences(Character player)
        {
            var messages = new List<string>();

            // Early return if no player - can't process relationship consequences without one
            if (player == null)
            {
                return messages;
            }

            var random = new Random();

            // Process each jealous partner
            foreach (var kvp in JealousyLevels.ToList())
            {
                string npcId = kvp.Key;
                int jealousy = kvp.Value;

                if (jealousy <= 0) continue;

                // Find the NPC
                var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == npcId);
                string npcName = npc?.Name ?? npcId;

                // Check if they're a spouse or lover
                var spouse = Spouses.FirstOrDefault(s => s.NPCId == npcId);
                var lover = CurrentLovers.FirstOrDefault(l => l.NPCId == npcId);

                if (jealousy >= 90)
                {
                    // CRITICAL: Breakup or divorce!
                    if (spouse != null)
                    {
                        messages.Add($"{npcName} has had enough of your infidelity!");
                        messages.Add($"{npcName} demands a divorce!");

                        // Remove spouse
                        Spouses.Remove(spouse);
                        Exes.Add(npcId);

                        // Severe relationship damage
                        if (npc != null && player != null)
                        {
                            RelationshipSystem.UpdateRelationship(player, npc, -1, 30, true);
                        }

                        NewsSystem.Instance.Newsy(true, $"{npcName} has divorced their partner due to infidelity!");
                        JealousyLevels.Remove(npcId);
                    }
                    else if (lover != null)
                    {
                        messages.Add($"{npcName} is heartbroken and ends things with you!");

                        CurrentLovers.Remove(lover);
                        Exes.Add(npcId);

                        if (npc != null && player != null)
                        {
                            RelationshipSystem.UpdateRelationship(player, npc, -1, 20, true);
                        }

                        JealousyLevels.Remove(npcId);
                    }
                }
                else if (jealousy >= 70)
                {
                    // High jealousy: Confrontation and major relationship damage
                    if (random.NextDouble() < 0.3)
                    {
                        messages.Add($"{npcName} confronts you about your unfaithfulness!");
                        messages.Add($"\"How could you do this to me?!\" they cry.");

                        if (npc != null && player != null)
                        {
                            RelationshipSystem.UpdateRelationship(player, npc, -1, 10, true);
                        }

                        // Small chance to calm down if they confront
                        JealousyLevels[npcId] = Math.Max(0, jealousy - 10);
                    }
                }
                else if (jealousy >= 50)
                {
                    // Medium jealousy: Suspicion and coldness
                    if (random.NextDouble() < 0.2)
                    {
                        messages.Add($"{npcName} seems distant and suspicious of you...");

                        if (npc != null && player != null)
                        {
                            RelationshipSystem.UpdateRelationship(player, npc, -1, 3, true);
                        }
                    }
                }

                // Jealousy slowly fades over time (if no new triggers)
                // Decay rate affected by difficulty: Easy = faster decay, Hard/Nightmare = slower
                if (jealousy > 0 && jealousy < 90)
                {
                    int baseDecay = 2;
                    int adjustedDecay = DifficultySystem.ApplyJealousyDecayMultiplier(baseDecay);
                    JealousyLevels[npcId] = Math.Max(0, jealousy - adjustedDecay);
                }
            }

            return messages;
        }

        /// <summary>
        /// Get the jealousy level for a specific NPC
        /// </summary>
        public int GetJealousyLevel(string npcId)
        {
            return JealousyLevels.GetValueOrDefault(npcId, 0);
        }

        /// <summary>
        /// Set up a cuckold/cuckquean arrangement
        /// </summary>
        public void SetupCuckoldArrangement(string primaryPartnerId, string thirdPartyId, bool playerIsWatching)
        {
            var arrangement = new CuckoldArrangement
            {
                PrimaryPartnerId = primaryPartnerId,
                ThirdPartyId = thirdPartyId,
                PlayerIsWatching = playerIsWatching,
                ConsentedDate = DateTime.Now,
                EncounterCount = 0
            };

            CuckArrangements.Add(arrangement);
            // GD.Print($"[Romance] Cuckold arrangement set up with {primaryPartnerId} and {thirdPartyId}");
        }

        /// <summary>
        /// Set up a poly network
        /// </summary>
        public void SetupPolyNetwork(string networkName, List<string> members)
        {
            var network = new PolyNetwork
            {
                NetworkName = networkName,
                MemberIds = new List<string>(members),
                EstablishedDate = DateTime.Now
            };

            PolyNetworks.Add(network);
            // GD.Print($"[Romance] Poly network '{networkName}' established with {members.Count} members");
        }

        /// <summary>
        /// Get total number of romantic partners (current)
        /// </summary>
        public int TotalPartnerCount => CurrentLovers.Count + Spouses.Count + FriendsWithBenefits.Count;

        /// <summary>
        /// Check if player is married
        /// </summary>
        public bool IsMarried => Spouses.Count > 0;

        /// <summary>
        /// Get primary spouse (first married)
        /// </summary>
        public Spouse? PrimarySpouse => Spouses.FirstOrDefault();

        /// <summary>
        /// Check if player has consented polyamory with a specific partner
        /// </summary>
        public bool HasPolyConsent(string npcId)
        {
            return AgreedStructures.TryGetValue(npcId, out var structure) &&
                   (structure == RelationshipStructure.Polyamorous ||
                    structure == RelationshipStructure.OpenRelationship);
        }

        /// <summary>
        /// Convert to save data format
        /// </summary>
        public UsurperRemake.Systems.RomanceTrackerData ToSaveData()
        {
            // Log what we're saving
            DebugLogger.Instance.LogDebug("ROMANCE", $"Serializing: {Spouses.Count} spouses, {CurrentLovers.Count} lovers");
            foreach (var spouse in Spouses)
            {
                DebugLogger.Instance.LogDebug("ROMANCE", $"  Spouse: {spouse.NPCName} (ID: {spouse.NPCId})");
            }

            var data = new UsurperRemake.Systems.RomanceTrackerData();

            // Convert lovers
            foreach (var lover in CurrentLovers)
            {
                data.CurrentLovers.Add(new UsurperRemake.Systems.LoverData
                {
                    NPCId = lover.NPCId,
                    NPCName = !string.IsNullOrEmpty(lover.NPCName) ? lover.NPCName : GetNPCName(lover.NPCId),  // Use cached name first
                    LoveLevel = lover.LoveLevel,
                    IsExclusive = lover.IsExclusive,
                    KnowsAboutOthers = lover.KnowsAboutOthers,
                    MetamorsList = new List<string>(lover.MetamorsList),
                    RelationshipStart = lover.RelationshipStart,
                    LastIntimateDate = lover.LastIntimateDate
                });
            }

            // Convert spouses
            foreach (var spouse in Spouses)
            {
                data.Spouses.Add(new UsurperRemake.Systems.SpouseData
                {
                    NPCId = spouse.NPCId,
                    NPCName = !string.IsNullOrEmpty(spouse.NPCName) ? spouse.NPCName : GetNPCName(spouse.NPCId),  // Use cached name first
                    MarriedDate = spouse.MarriedDate,
                    LoveLevel = spouse.LoveLevel,
                    AcceptsPolyamory = spouse.AcceptsPolyamory,
                    KnowsAboutOthers = spouse.KnowsAboutOthers,
                    Children = spouse.Children,
                    LastIntimateDate = spouse.LastIntimateDate
                });
            }

            // Copy simple lists
            data.FriendsWithBenefits = new List<string>(FriendsWithBenefits);
            data.Exes = new List<string>(Exes);

            // Convert ex-spouses
            foreach (var exSpouse in ExSpouses)
            {
                data.ExSpouses.Add(new UsurperRemake.Systems.ExSpouseData
                {
                    NPCId = exSpouse.NPCId,
                    NPCName = !string.IsNullOrEmpty(exSpouse.NPCName) ? exSpouse.NPCName : GetNPCName(exSpouse.NPCId),
                    MarriedDate = exSpouse.MarriedDate,
                    DivorceDate = exSpouse.DivorceDate,
                    ChildrenTogether = exSpouse.ChildrenTogether,
                    DivorceReason = exSpouse.DivorceReason,
                    PlayerInitiated = exSpouse.PlayerInitiated
                });
            }

            // Convert encounter history
            foreach (var encounter in EncounterHistory)
            {
                data.EncounterHistory.Add(new UsurperRemake.Systems.IntimateEncounterData
                {
                    Date = encounter.Date,
                    Location = encounter.Location,
                    PartnerIds = new List<string>(encounter.PartnerIds),
                    PartnerNames = encounter.PartnerIds.Select(id => GetNPCName(id)).ToList(),
                    Type = (int)encounter.Type,
                    Mood = (int)encounter.Mood,
                    IsFirstTime = encounter.IsFirstTime,
                    WatcherIds = new List<string>(encounter.WatcherIds),
                    Notes = encounter.Notes
                });
            }

            // Copy jealousy and structures
            data.JealousyLevels = new Dictionary<string, int>(JealousyLevels);
            foreach (var kvp in AgreedStructures)
            {
                data.AgreedStructures[kvp.Key] = (int)kvp.Value;
            }

            // Convert cuck arrangements
            foreach (var arr in CuckArrangements)
            {
                data.CuckArrangements.Add(new UsurperRemake.Systems.CuckoldArrangementData
                {
                    PrimaryPartnerId = arr.PrimaryPartnerId,
                    PrimaryPartnerName = GetNPCName(arr.PrimaryPartnerId),
                    ThirdPartyId = arr.ThirdPartyId,
                    ThirdPartyName = GetNPCName(arr.ThirdPartyId),
                    PlayerIsWatching = arr.PlayerIsWatching,
                    ConsentedDate = arr.ConsentedDate,
                    EncounterCount = arr.EncounterCount
                });
            }

            // Convert poly networks
            foreach (var network in PolyNetworks)
            {
                data.PolyNetworks.Add(new UsurperRemake.Systems.PolyNetworkData
                {
                    NetworkName = network.NetworkName,
                    MemberIds = new List<string>(network.MemberIds),
                    MemberNames = network.MemberIds.Select(id => GetNPCName(id)).ToList(),
                    EstablishedDate = network.EstablishedDate
                });
            }

            // Save conversation states (flirt progress)
            data.ConversationStates = VisualNovelDialogueSystem.Instance.GetConversationStatesForSave();

            return data;
        }

        /// <summary>
        /// Load from save data format
        /// </summary>
        public void LoadFromSaveData(UsurperRemake.Systems.RomanceTrackerData data)
        {
            if (data == null)
            {
                DebugLogger.Instance.LogWarning("ROMANCE", "LoadFromSaveData called with null data!");
                return;
            }

            // Log what we're loading
            DebugLogger.Instance.LogDebug("ROMANCE", $"Loading: {data.Spouses?.Count ?? 0} spouses, {data.CurrentLovers?.Count ?? 0} lovers");
            foreach (var spouse in data.Spouses ?? new List<SpouseData>())
            {
                DebugLogger.Instance.LogDebug("ROMANCE", $"  Spouse from save: {spouse.NPCName} (ID: {spouse.NPCId})");
            }

            // Clear existing data
            CurrentLovers.Clear();
            Spouses.Clear();
            FriendsWithBenefits.Clear();
            Exes.Clear();
            ExSpouses.Clear();
            EncounterHistory.Clear();
            JealousyLevels.Clear();
            AgreedStructures.Clear();
            CuckArrangements.Clear();
            PolyNetworks.Clear();

            // Load lovers
            foreach (var loverData in data.CurrentLovers)
            {
                CurrentLovers.Add(new Lover
                {
                    NPCId = loverData.NPCId,
                    NPCName = !string.IsNullOrEmpty(loverData.NPCName) ? loverData.NPCName : GetNPCName(loverData.NPCId),  // Use saved name or look up
                    LoveLevel = loverData.LoveLevel,
                    IsExclusive = loverData.IsExclusive,
                    KnowsAboutOthers = loverData.KnowsAboutOthers,
                    MetamorsList = new List<string>(loverData.MetamorsList),
                    RelationshipStart = loverData.RelationshipStart,
                    LastIntimateDate = loverData.LastIntimateDate
                });
            }

            // Load spouses
            foreach (var spouseData in data.Spouses)
            {
                Spouses.Add(new Spouse
                {
                    NPCId = spouseData.NPCId,
                    NPCName = !string.IsNullOrEmpty(spouseData.NPCName) ? spouseData.NPCName : GetNPCName(spouseData.NPCId),  // Use saved name or look up
                    MarriedDate = spouseData.MarriedDate,
                    LoveLevel = spouseData.LoveLevel,
                    AcceptsPolyamory = spouseData.AcceptsPolyamory,
                    KnowsAboutOthers = spouseData.KnowsAboutOthers,
                    Children = spouseData.Children,
                    LastIntimateDate = spouseData.LastIntimateDate
                });
            }

            // Load simple lists
            FriendsWithBenefits.AddRange(data.FriendsWithBenefits);
            Exes.AddRange(data.Exes);

            // Load ex-spouses
            if (data.ExSpouses != null)
            {
                foreach (var exData in data.ExSpouses)
                {
                    ExSpouses.Add(new ExSpouse
                    {
                        NPCId = exData.NPCId,
                        NPCName = !string.IsNullOrEmpty(exData.NPCName) ? exData.NPCName : GetNPCName(exData.NPCId),
                        MarriedDate = exData.MarriedDate,
                        DivorceDate = exData.DivorceDate,
                        ChildrenTogether = exData.ChildrenTogether,
                        DivorceReason = exData.DivorceReason,
                        PlayerInitiated = exData.PlayerInitiated
                    });
                }
            }

            // Load encounter history
            foreach (var encData in data.EncounterHistory)
            {
                EncounterHistory.Add(new IntimateEncounter
                {
                    Date = encData.Date,
                    Location = encData.Location,
                    PartnerIds = new List<string>(encData.PartnerIds),
                    Type = (EncounterType)encData.Type,
                    Mood = (IntimacyMood)encData.Mood,
                    IsFirstTime = encData.IsFirstTime,
                    WatcherIds = new List<string>(encData.WatcherIds),
                    Notes = encData.Notes
                });
            }

            // Load jealousy and structures
            foreach (var kvp in data.JealousyLevels)
            {
                JealousyLevels[kvp.Key] = kvp.Value;
            }
            foreach (var kvp in data.AgreedStructures)
            {
                AgreedStructures[kvp.Key] = (RelationshipStructure)kvp.Value;
            }

            // Load cuck arrangements
            foreach (var arrData in data.CuckArrangements)
            {
                CuckArrangements.Add(new CuckoldArrangement
                {
                    PrimaryPartnerId = arrData.PrimaryPartnerId,
                    ThirdPartyId = arrData.ThirdPartyId,
                    PlayerIsWatching = arrData.PlayerIsWatching,
                    ConsentedDate = arrData.ConsentedDate,
                    EncounterCount = arrData.EncounterCount
                });
            }

            // Load poly networks
            foreach (var netData in data.PolyNetworks)
            {
                PolyNetworks.Add(new PolyNetwork
                {
                    NetworkName = netData.NetworkName,
                    MemberIds = new List<string>(netData.MemberIds),
                    EstablishedDate = netData.EstablishedDate
                });
            }

            // Load conversation states (flirt progress)
            if (data.ConversationStates != null && data.ConversationStates.Count > 0)
            {
                VisualNovelDialogueSystem.Instance.LoadConversationStates(data.ConversationStates);
            }

            // Log final state after loading
            DebugLogger.Instance.LogInfo("ROMANCE", $"Loaded: {Spouses.Count} spouses, {CurrentLovers.Count} lovers, {EncounterHistory.Count} encounters");
            foreach (var spouse in Spouses)
            {
                DebugLogger.Instance.LogDebug("ROMANCE", $"  Restored spouse: {spouse.NPCName} (ID: {spouse.NPCId})");
            }
        }

        /// <summary>
        /// Helper to get NPC name from ID
        /// </summary>
        private string GetNPCName(string npcId)
        {
            var npc = UsurperRemake.Systems.NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == npcId);
            return npc?.Name ?? npcId;
        }

        /// <summary>
        /// Get spouse by NPC ID
        /// </summary>
        public Spouse? GetSpouse(string npcId)
        {
            return Spouses.FirstOrDefault(s => s.NPCId == npcId);
        }

        /// <summary>
        /// Get lover by NPC ID
        /// </summary>
        public Lover? GetLover(string npcId)
        {
            return CurrentLovers.FirstOrDefault(l => l.NPCId == npcId);
        }

        /// <summary>
        /// Add a child to a spouse
        /// </summary>
        public void AddChildToSpouse(string npcId)
        {
            var spouse = GetSpouse(npcId);
            if (spouse != null)
            {
                spouse.Children++;
                // GD.Print($"[Romance] Child added to marriage with {npcId}. Total children: {spouse.Children}");
            }
        }
    }

    /// <summary>
    /// Represents a lover relationship
    /// </summary>
    public class Lover
    {
        public string NPCId { get; set; } = "";
        public string NPCName { get; set; } = "";  // Cached name for display when NPC lookup fails
        public int LoveLevel { get; set; }
        public bool IsExclusive { get; set; }
        public bool KnowsAboutOthers { get; set; }
        public List<string> MetamorsList { get; set; } = new();  // Other partners they know about
        public DateTime RelationshipStart { get; set; }
        public DateTime? LastIntimateDate { get; set; }
    }

    /// <summary>
    /// Represents a spouse (married partner)
    /// </summary>
    public class Spouse
    {
        public string NPCId { get; set; } = "";
        public string NPCName { get; set; } = "";  // Cached name for display when NPC lookup fails
        public DateTime MarriedDate { get; set; }
        public int LoveLevel { get; set; }
        public bool AcceptsPolyamory { get; set; }
        public bool KnowsAboutOthers { get; set; }
        public int Children { get; set; }
        public DateTime? LastIntimateDate { get; set; }
    }

    /// <summary>
    /// Represents an ex-spouse (divorced partner) - preserves marriage history
    /// </summary>
    public class ExSpouse
    {
        public string NPCId { get; set; } = "";
        public string NPCName { get; set; } = "";
        public DateTime MarriedDate { get; set; }
        public DateTime DivorceDate { get; set; }
        public int ChildrenTogether { get; set; }  // Number of children from this marriage
        public string DivorceReason { get; set; } = "";  // Why they divorced
        public bool PlayerInitiated { get; set; }  // true = player asked for divorce, false = spouse left
    }

    /// <summary>
    /// Represents an intimate encounter
    /// </summary>
    public class IntimateEncounter
    {
        public DateTime Date { get; set; }
        public string Location { get; set; } = "";
        public List<string> PartnerIds { get; set; } = new();
        public EncounterType Type { get; set; }
        public IntimacyMood Mood { get; set; }
        public bool IsFirstTime { get; set; }
        public List<string> WatcherIds { get; set; } = new();  // For voyeur scenes
        public string Notes { get; set; } = "";
    }

    /// <summary>
    /// Types of intimate encounters
    /// </summary>
    public enum EncounterType
    {
        Solo,           // One-on-one
        Threesome,      // Three participants
        Group,          // More than three
        Voyeur,         // Watching others
        BeingWatched    // Being watched
    }

    /// <summary>
    /// Mood of the intimate encounter
    /// </summary>
    public enum IntimacyMood
    {
        Tender,
        Passionate,
        Rough,
        Playful,
        Kinky,
        Romantic,
        Quick
    }

    /// <summary>
    /// Cuckold/Cuckquean arrangement
    /// </summary>
    public class CuckoldArrangement
    {
        public string PrimaryPartnerId { get; set; } = "";
        public string ThirdPartyId { get; set; } = "";
        public bool PlayerIsWatching { get; set; }  // true = player watches, false = player participates
        public DateTime ConsentedDate { get; set; }
        public int EncounterCount { get; set; }
    }

    /// <summary>
    /// Polyamorous network
    /// </summary>
    public class PolyNetwork
    {
        public string NetworkName { get; set; } = "";
        public List<string> MemberIds { get; set; } = new();
        public DateTime EstablishedDate { get; set; }
    }

    /// <summary>
    /// Relationship structure agreed with partner
    /// </summary>
    public enum RelationshipStructure
    {
        Monogamous,          // Traditional exclusive
        OpenRelationship,    // Primary partner + others allowed
        Polyamorous,         // Multiple committed relationships
        CasualOnly,          // No commitment, just fun
        FriendsWithBenefits, // Physical without romance
        DontAsk              // Don't ask, don't tell
    }

    /// <summary>
    /// Romance relationship type
    /// </summary>
    public enum RomanceRelationType
    {
        None,
        Spouse,
        Lover,
        FWB,
        Ex
    }
}
