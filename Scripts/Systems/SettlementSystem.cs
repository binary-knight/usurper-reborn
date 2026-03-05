using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems
{
    public enum SettlementBuilding
    {
        Palisade,      // Courage — defense
        Tavern,        // Sociability — attracts settlers, XP buff
        MarketStall,   // Greed — trade
        Shrine,        // Patience — healing
        Workshop,      // Ambition — repairs/identify
        Watchtower,    // Courage+Intelligence — scouting
        CouncilHall    // Intelligence — governance/gold
    }

    public enum BuildingTier
    {
        None = 0,
        Foundation = 1,
        Built = 2,
        Upgraded = 3
    }

    public enum SettlementBuffType
    {
        None = 0,
        XPBonus = 1,       // Tavern
        DefenseBonus = 2,  // Palisade
        DamageBonus = 3,   // Arena
        GoldBonus = 4,     // Thieves' Den
        TrapResist = 5,    // Prison
        LibraryXP = 6      // Library
    }

    public class BuildingState
    {
        public SettlementBuilding Type { get; set; }
        public BuildingTier Tier { get; set; } = BuildingTier.None;
        public long ResourcePool { get; set; } = 0;
    }

    /// <summary>
    /// State of an NPC-proposed building (uses same tier system as core buildings).
    /// </summary>
    public class ProposedBuildingState
    {
        public string Id { get; set; }
        public BuildingTier Tier { get; set; } = BuildingTier.None;
        public long ResourcePool { get; set; } = 0;
    }

    /// <summary>
    /// An active proposal being deliberated by settlers.
    /// </summary>
    public class ActiveProposal
    {
        public string BuildingId { get; set; }
        public string ProposerName { get; set; }
        public int SupportVotes { get; set; }
        public int OpposeVotes { get; set; }
        public Dictionary<string, int> PlayerVotes { get; set; } = new();
        public int PlayerVoteWeight => PlayerVotes.Values.Sum();
        public int TicksRemaining { get; set; }
    }

    /// <summary>
    /// Static template defining an NPC-proposable building.
    /// </summary>
    public class ProposalTemplate
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string EffectDescription { get; set; }
        public Func<PersonalityProfile, float> TraitScorer { get; set; }
        public Func<PersonalityProfile, bool> OppositionCheck { get; set; }
        public Func<SettlementState, bool> Precondition { get; set; }
    }

    public class SettlementState
    {
        public List<string> SettlerNames { get; set; } = new();
        public Dictionary<SettlementBuilding, BuildingState> Buildings { get; set; } = new();
        public SettlementBuilding? ActiveBuilding { get; set; } = null;
        public long CommunalTreasury { get; set; } = 0;
        public Dictionary<string, long> PlayerContributions { get; set; } = new();
        public bool Founded { get; set; } = false;
        public bool IsEstablished => SettlerNames.Count >= GameConfig.SettlementMinNPCs;

        // NPC-proposed buildings
        public Dictionary<string, ProposedBuildingState> ProposedBuildings { get; set; } = new();
        public ActiveProposal CurrentProposal { get; set; }
        public string ActiveProposedBuildingId { get; set; } // Which proposed building is under construction
        public Dictionary<string, int> ProposalCooldowns { get; set; } = new();
        public int ContributionBoostTicks { get; set; } // Memorial boost remaining ticks

        public SettlementState()
        {
            // Initialize all buildings at None
            foreach (SettlementBuilding b in Enum.GetValues(typeof(SettlementBuilding)))
            {
                Buildings[b] = new BuildingState { Type = b, Tier = BuildingTier.None };
            }
        }
    }

    public class SettlementSystem
    {
        private static SettlementSystem _instance;
        private static readonly object _lock = new object();
        private readonly Random random = new Random();

        public SettlementState State { get; set; } = new SettlementState();

        public static SettlementSystem Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new SettlementSystem();
                    }
                }
                return _instance;
            }
        }

        private SettlementSystem() { }

        // Trait-to-building mapping for emergent building selection
        private static readonly Dictionary<SettlementBuilding, Func<PersonalityProfile, float>> BuildingTraitMap = new()
        {
            { SettlementBuilding.Palisade, p => p.Courage },
            { SettlementBuilding.Tavern, p => p.Sociability },
            { SettlementBuilding.MarketStall, p => p.Greed },
            { SettlementBuilding.Shrine, p => p.Patience },
            { SettlementBuilding.Workshop, p => p.Ambition },
            { SettlementBuilding.Watchtower, p => (p.Courage + p.Intelligence) / 2f },
            { SettlementBuilding.CouncilHall, p => p.Intelligence }
        };

        // ───── NPC Building Proposal Catalog ─────
        private static readonly List<ProposalTemplate> ProposalCatalog = new()
        {
            new() { Id = "arena", Name = "Arena", Description = "A fighting pit where warriors test their mettle",
                EffectDescription = "+10% damage buff (5 combats)",
                TraitScorer = p => (p.Aggression + p.Courage) / 2f,
                OppositionCheck = p => p.Patience > 0.7f,
                Precondition = s => s.SettlerNames.Count >= 8 },
            new() { Id = "thieves_den", Name = "Thieves' Den", Description = "A shadowy hideout for those who work outside the law",
                EffectDescription = "+15% gold find (5 combats)",
                TraitScorer = p => (p.Greed + p.Impulsiveness) / 2f,
                OppositionCheck = p => p.Trustworthiness > 0.6f,
                Precondition = s => !s.Buildings.ContainsKey(SettlementBuilding.Watchtower) || s.Buildings[SettlementBuilding.Watchtower].Tier < BuildingTier.Built },
            new() { Id = "mystic_circle", Name = "Mystic Circle", Description = "A ring of ancient stones channeling arcane energy",
                EffectDescription = "Restore 30% max mana",
                TraitScorer = p => (p.Mysticism + p.Intelligence) / 2f,
                OppositionCheck = p => p.Aggression > 0.7f,
                Precondition = s => s.Buildings.ContainsKey(SettlementBuilding.Shrine) && s.Buildings[SettlementBuilding.Shrine].Tier >= BuildingTier.Foundation },
            new() { Id = "prison", Name = "Prison", Description = "Iron cells to hold bandits and troublemakers",
                EffectDescription = "-50% trap damage (5 combats)",
                TraitScorer = p => (p.Vengefulness + p.Loyalty) / 2f,
                OppositionCheck = p => p.Impulsiveness > 0.7f,
                Precondition = s => s.Buildings.ContainsKey(SettlementBuilding.Palisade) && s.Buildings[SettlementBuilding.Palisade].Tier >= BuildingTier.Built },
            new() { Id = "scouts_lodge", Name = "Scouts' Lodge", Description = "Rangers gather here to share intelligence on dungeon threats",
                EffectDescription = "Scout 3 dungeon floors at once",
                TraitScorer = p => (p.Caution + p.Courage) / 2f,
                OppositionCheck = p => p.Greed > 0.7f,
                Precondition = s => s.Buildings.ContainsKey(SettlementBuilding.Watchtower) && s.Buildings[SettlementBuilding.Watchtower].Tier >= BuildingTier.Foundation },
            new() { Id = "library", Name = "Library", Description = "Shelves of ancient knowledge and scholarly works",
                EffectDescription = "+5% XP buff (5 combats)",
                TraitScorer = p => (p.Intelligence + p.Patience) / 2f,
                OppositionCheck = p => p.Impulsiveness > 0.7f,
                Precondition = s => s.Buildings.ContainsKey(SettlementBuilding.CouncilHall) && s.Buildings[SettlementBuilding.CouncilHall].Tier >= BuildingTier.Foundation },
            new() { Id = "barracks", Name = "Barracks", Description = "Trained militia ready to fight alongside adventurers",
                EffectDescription = "NPC fights alongside you (3 combats)",
                TraitScorer = p => (p.Loyalty + p.Aggression) / 2f,
                OppositionCheck = p => p.Patience > 0.7f && p.Caution > 0.6f,
                Precondition = s => s.Buildings.ContainsKey(SettlementBuilding.Palisade) && s.Buildings[SettlementBuilding.Palisade].Tier >= BuildingTier.Built },
            new() { Id = "herbalist_hut", Name = "Herbalist Hut", Description = "An apothecary garden tended by patient hands",
                EffectDescription = "Free random herb (1/day)",
                TraitScorer = p => (p.Patience + p.Caution) / 2f,
                OppositionCheck = p => p.Greed > 0.7f,
                Precondition = s => s.SettlerNames.Count >= 6 },
            new() { Id = "smugglers_cache", Name = "Smugglers' Cache", Description = "A hidden stash where goods move without taxes",
                EffectDescription = "-10% shop prices (5 purchases)",
                TraitScorer = p => (p.Greed + p.Impulsiveness + (1f - p.Trustworthiness)) / 3f,
                OppositionCheck = p => p.Loyalty > 0.7f,
                Precondition = s => !s.Buildings.ContainsKey(SettlementBuilding.CouncilHall) || s.Buildings[SettlementBuilding.CouncilHall].Tier < BuildingTier.Built },
            new() { Id = "memorial", Name = "Memorial", Description = "A stone monument honoring the fallen",
                EffectDescription = "Settler contributions +25% for 10 ticks",
                TraitScorer = p => (p.Loyalty + p.Patience) / 2f,
                OppositionCheck = p => p.Greed > 0.7f,
                Precondition = s => true }, // Always available — sentiment-driven
            new() { Id = "gambling_hall", Name = "Gambling Hall", Description = "A rowdy establishment where fortunes are won and lost",
                EffectDescription = "Gamble gold (double or lose, capped)",
                TraitScorer = p => (p.Impulsiveness + p.Greed) / 2f,
                OppositionCheck = p => p.Caution > 0.7f,
                Precondition = s => s.Buildings.ContainsKey(SettlementBuilding.Tavern) && s.Buildings[SettlementBuilding.Tavern].Tier >= BuildingTier.Built },
            new() { Id = "oracles_sanctum", Name = "Oracle's Sanctum", Description = "A veiled seer whispers of what lies ahead",
                EffectDescription = "Reveal dungeon hints and lore",
                TraitScorer = p => (p.Mysticism + p.Patience) / 2f,
                OppositionCheck = p => p.Aggression > 0.6f,
                Precondition = s => s.ProposedBuildings.ContainsKey("mystic_circle") && s.ProposedBuildings["mystic_circle"].Tier >= BuildingTier.Built },
        };

        /// <summary>
        /// Called each world sim tick. Handles migration, contributions, and construction.
        /// </summary>
        public void ProcessTick(List<NPC> aliveNPCs)
        {
            if (aliveNPCs == null || aliveNPCs.Count == 0) return;

            ProcessMigration(aliveNPCs);
            ProcessDepartures(aliveNPCs);

            if (State.SettlerNames.Count == 0) return;

            ProcessGoldContributions(aliveNPCs);
            ProcessBuildingSelection(aliveNPCs);
            ProcessConstruction();
            ProcessProposalSystem(aliveNPCs);
            ProcessProposedConstruction();

            if (State.ContributionBoostTicks > 0)
                State.ContributionBoostTicks--;
        }

        private void ProcessMigration(List<NPC> aliveNPCs)
        {
            if (State.SettlerNames.Count >= GameConfig.SettlementMaxNPCs) return;

            foreach (var npc in aliveNPCs)
            {
                if (State.SettlerNames.Contains(npc.Name)) continue;
                if (npc.CurrentLocation == "Settlement") continue;
                if (npc.IsSpecialNPC) continue; // Don't steal scripted NPCs
                if (State.SettlerNames.Count >= GameConfig.SettlementMaxNPCs) break;

                var p = npc.Brain?.Personality;
                if (p == null) continue;

                // Community-minded NPCs: high sociability or loyalty, not too aggressive
                bool communityMinded = (p.Sociability > 0.4f || p.Loyalty > 0.4f) && p.Aggression < 0.6f;
                if (!communityMinded) continue;

                // Must be healthy and not have pressing enemies
                if (npc.HP < npc.MaxHP * 0.7) continue;
                if (npc.Enemies != null && npc.Enemies.Count > 0 && p.Vengefulness > 0.5f) continue;

                // Roll migration chance
                if (random.NextDouble() > GameConfig.SettlementMigrateChance) continue;

                // Migrate!
                State.SettlerNames.Add(npc.Name);
                npc.CurrentLocation = "Settlement";

                // First time reaching threshold — announce founding
                if (!State.Founded && State.IsEstablished)
                {
                    State.Founded = true;
                    NewsSystem.Instance?.Newsy(true, "A settlement has been founded beyond the city gates!");
                }

                // Announce new settler (not every one, just sometimes)
                if (random.NextDouble() < 0.3)
                {
                    NewsSystem.Instance?.Newsy(true, $"{npc.Name} has joined the Outskirts settlement.");
                }

                DebugLogger.Instance?.LogDebug("SETTLEMENT", $"{npc.Name} migrated to settlement ({State.SettlerNames.Count} settlers)");
            }
        }

        private void ProcessDepartures(List<NPC> aliveNPCs)
        {
            var toRemove = new List<string>();

            foreach (var name in State.SettlerNames)
            {
                var npc = aliveNPCs.FirstOrDefault(n => n.Name == name);
                if (npc == null)
                {
                    // NPC died or was removed
                    toRemove.Add(name);
                    continue;
                }

                var p = npc.Brain?.Personality;

                // Leave if badly wounded
                if (npc.HP < npc.MaxHP * 0.5)
                {
                    toRemove.Add(name);
                    npc.CurrentLocation = "Main Street";
                    continue;
                }

                // Leave if highly aggressive with enemies
                if (p != null && p.Aggression > 0.7f && npc.Enemies != null && npc.Enemies.Count > 0)
                {
                    toRemove.Add(name);
                    npc.CurrentLocation = "Main Street";
                    continue;
                }
            }

            foreach (var name in toRemove)
            {
                State.SettlerNames.Remove(name);
            }
        }

        private void ProcessGoldContributions(List<NPC> aliveNPCs)
        {
            // Determine which pool to contribute to
            long? targetPool = null;
            BuildingState coreBuildingState = null;
            ProposedBuildingState proposedBuildingState = null;

            if (State.ActiveBuilding != null)
                coreBuildingState = State.Buildings[State.ActiveBuilding.Value];
            else if (State.ActiveProposedBuildingId != null && State.ProposedBuildings.ContainsKey(State.ActiveProposedBuildingId))
                proposedBuildingState = State.ProposedBuildings[State.ActiveProposedBuildingId];
            else
                return;

            foreach (var name in State.SettlerNames)
            {
                var npc = aliveNPCs.FirstOrDefault(n => n.Name == name);
                if (npc == null || npc.Gold <= 100) continue;

                var p = npc.Brain?.Personality;
                float greed = p?.Greed ?? 0.5f;

                // Generous NPCs contribute more, greedy ones less
                double contribution = (1.0 - greed) * npc.Gold * GameConfig.SettlementContributeRate;
                // Memorial boost: +25% contributions
                if (State.ContributionBoostTicks > 0)
                    contribution *= 1.25;
                long amount = Math.Max(1, (long)contribution);

                // Don't drain NPC below 100 gold
                amount = Math.Min(amount, npc.Gold - 100);
                if (amount <= 0) continue;

                npc.Gold -= amount;
                if (coreBuildingState != null)
                    coreBuildingState.ResourcePool += amount;
                else if (proposedBuildingState != null)
                    proposedBuildingState.ResourcePool += amount;
            }
        }

        private void ProcessBuildingSelection(List<NPC> aliveNPCs)
        {
            if (State.ActiveBuilding != null) return;

            // Find the next building that isn't fully upgraded
            var settlers = aliveNPCs.Where(n => State.SettlerNames.Contains(n.Name) && n.Brain?.Personality != null).ToList();
            if (settlers.Count == 0) return;

            SettlementBuilding? bestBuilding = null;
            float bestScore = -1f;

            foreach (var kvp in BuildingTraitMap)
            {
                var building = kvp.Key;
                var traitGetter = kvp.Value;

                if (State.Buildings[building].Tier >= BuildingTier.Upgraded) continue;

                float totalScore = 0f;
                foreach (var settler in settlers)
                {
                    totalScore += traitGetter(settler.Brain.Personality);
                }

                if (totalScore > bestScore)
                {
                    bestScore = totalScore;
                    bestBuilding = building;
                }
            }

            if (bestBuilding != null)
            {
                State.ActiveBuilding = bestBuilding;
                DebugLogger.Instance?.LogDebug("SETTLEMENT", $"Building selected: {bestBuilding} (score: {bestScore:F1})");
            }
        }

        private void ProcessConstruction()
        {
            if (State.ActiveBuilding == null) return;

            var buildingState = State.Buildings[State.ActiveBuilding.Value];
            int nextTier = (int)buildingState.Tier + 1;
            if (nextTier > (int)BuildingTier.Upgraded) return;

            long cost = GameConfig.SettlementBuildingCosts[nextTier];
            if (buildingState.ResourcePool < cost) return;

            // Construction complete!
            buildingState.ResourcePool -= cost;
            buildingState.Tier = (BuildingTier)nextTier;

            string tierName = buildingState.Tier switch
            {
                BuildingTier.Foundation => "foundation has been laid",
                BuildingTier.Built => "has been completed",
                BuildingTier.Upgraded => "has been upgraded",
                _ => "has been built"
            };

            string buildingName = GetBuildingDisplayName(State.ActiveBuilding.Value);
            NewsSystem.Instance?.Newsy(true, $"The settlement's {buildingName} {tierName}!");

            DebugLogger.Instance?.LogDebug("SETTLEMENT", $"{buildingName} advanced to {buildingState.Tier}");

            // Clear active building — next tick will pick a new one
            State.ActiveBuilding = null;
        }

        // ───── NPC Proposal Engine ─────

        private void ProcessProposalSystem(List<NPC> aliveNPCs)
        {
            // Only activate proposals once settlement is mature enough
            int builtCount = State.Buildings.Values.Count(b => b.Tier >= BuildingTier.Foundation);
            if (builtCount < GameConfig.SettlementMinBuildingsForProposals) return;

            // Always process existing proposal deliberation (ticks should count down
            // even while buildings are under construction)
            if (State.CurrentProposal != null)
            {
                ProcessDeliberation(aliveNPCs);
                return;
            }

            // Don't generate NEW proposals while a core building or proposed building is under construction
            if (State.ActiveBuilding != null) return;
            if (State.ActiveProposedBuildingId != null) return;

            // Decay cooldowns (decrement each tick, remove when expired)
            if (State.ProposalCooldowns.Count > 0)
            {
                foreach (var key in State.ProposalCooldowns.Keys.ToList())
                    State.ProposalCooldowns[key]--;
                var expired = State.ProposalCooldowns.Where(kvp => kvp.Value <= 0).Select(kvp => kvp.Key).ToList();
                foreach (var key in expired) State.ProposalCooldowns.Remove(key);
            }

            // Try to generate a new proposal
            GenerateProposal(aliveNPCs);
        }

        private void GenerateProposal(List<NPC> aliveNPCs)
        {
            var settlers = aliveNPCs.Where(n => State.SettlerNames.Contains(n.Name) && n.Brain?.Personality != null).ToList();
            if (settlers.Count < 3) return; // Need at least 3 settlers to propose

            string bestId = null;
            float bestScore = 2.0f; // Minimum threshold
            string bestProposer = null;

            foreach (var template in ProposalCatalog)
            {
                // Skip if already fully upgraded
                if (State.ProposedBuildings.TryGetValue(template.Id, out var existing) && existing.Tier >= BuildingTier.Upgraded)
                    continue;

                // Skip if on cooldown
                if (State.ProposalCooldowns.ContainsKey(template.Id)) continue;

                // Check precondition
                if (!template.Precondition(State)) continue;

                float totalScore = 0f;
                float topIndividualScore = 0f;
                string topScorer = null;

                foreach (var settler in settlers)
                {
                    float score = template.TraitScorer(settler.Brain.Personality);
                    totalScore += score;
                    if (score > topIndividualScore)
                    {
                        topIndividualScore = score;
                        topScorer = settler.Name;
                    }
                }

                if (totalScore > bestScore)
                {
                    bestScore = totalScore;
                    bestId = template.Id;
                    bestProposer = topScorer;
                }
            }

            if (bestId != null && bestProposer != null)
            {
                var template = ProposalCatalog.First(t => t.Id == bestId);
                State.CurrentProposal = new ActiveProposal
                {
                    BuildingId = bestId,
                    ProposerName = bestProposer,
                    SupportVotes = 0,
                    OpposeVotes = 0,
                    PlayerVotes = new(),
                    TicksRemaining = GameConfig.SettlementProposalDeliberationTicks
                };

                NewsSystem.Instance?.Newsy(true, $"{bestProposer} proposes building a {template.Name} in the settlement!");
                DebugLogger.Instance?.LogDebug("SETTLEMENT", $"Proposal: {template.Name} by {bestProposer} (score: {bestScore:F1})");
            }
        }

        private void ProcessDeliberation(List<NPC> aliveNPCs)
        {
            var proposal = State.CurrentProposal;
            if (proposal == null) return;

            var template = ProposalCatalog.FirstOrDefault(t => t.Id == proposal.BuildingId);
            if (template == null) { State.CurrentProposal = null; return; }

            var settlers = aliveNPCs.Where(n => State.SettlerNames.Contains(n.Name) && n.Brain?.Personality != null).ToList();

            // Recount NPC votes each tick (settler opinions may shift as NPCs arrive/leave)
            // Player vote weight is preserved separately
            proposal.SupportVotes = 0;
            proposal.OpposeVotes = 0;
            foreach (var settler in settlers)
            {
                float traitScore = template.TraitScorer(settler.Brain.Personality);
                bool opposes = template.OppositionCheck(settler.Brain.Personality);

                if (traitScore > 0.4f && !opposes)
                    proposal.SupportVotes++;
                else if (opposes)
                    proposal.OpposeVotes++;
            }

            proposal.TicksRemaining--;

            if (proposal.TicksRemaining <= 0)
                ResolveProposal();
        }

        private void ResolveProposal()
        {
            var proposal = State.CurrentProposal;
            if (proposal == null) return;

            var template = ProposalCatalog.FirstOrDefault(t => t.Id == proposal.BuildingId);
            if (template == null) { State.CurrentProposal = null; return; }

            int totalSupport = proposal.SupportVotes + Math.Max(0, proposal.PlayerVoteWeight);
            int totalOppose = proposal.OpposeVotes + Math.Max(0, -proposal.PlayerVoteWeight);

            if (totalSupport > totalOppose)
            {
                // Proposal passes!
                if (!State.ProposedBuildings.ContainsKey(proposal.BuildingId))
                    State.ProposedBuildings[proposal.BuildingId] = new ProposedBuildingState { Id = proposal.BuildingId };

                State.ActiveProposedBuildingId = proposal.BuildingId;
                NewsSystem.Instance?.Newsy(true, $"The settlers vote to build a {template.Name}! Construction begins.");
                DebugLogger.Instance?.LogDebug("SETTLEMENT", $"Proposal passed: {template.Name} ({totalSupport} for, {totalOppose} against)");

                // Memorial special effect: boost contributions
                if (proposal.BuildingId == "memorial")
                    State.ContributionBoostTicks = 10;
            }
            else
            {
                // Proposal fails
                State.ProposalCooldowns[proposal.BuildingId] = GameConfig.SettlementProposalCooldownTicks;
                NewsSystem.Instance?.Newsy(true, $"The settlers reject the proposal to build a {template.Name}.");
                DebugLogger.Instance?.LogDebug("SETTLEMENT", $"Proposal rejected: {template.Name} ({totalSupport} for, {totalOppose} against)");
            }

            State.CurrentProposal = null;
        }

        private void ProcessProposedConstruction()
        {
            if (State.ActiveProposedBuildingId == null) return;
            if (!State.ProposedBuildings.TryGetValue(State.ActiveProposedBuildingId, out var buildingState)) return;

            int nextTier = (int)buildingState.Tier + 1;
            if (nextTier > (int)BuildingTier.Upgraded) return;

            long cost = GameConfig.SettlementBuildingCosts[nextTier];
            if (buildingState.ResourcePool < cost) return;

            // Construction complete!
            buildingState.ResourcePool -= cost;
            buildingState.Tier = (BuildingTier)nextTier;

            var template = ProposalCatalog.FirstOrDefault(t => t.Id == State.ActiveProposedBuildingId);
            string buildingName = template?.Name ?? State.ActiveProposedBuildingId;

            string tierName = buildingState.Tier switch
            {
                BuildingTier.Foundation => "foundation has been laid",
                BuildingTier.Built => "has been completed",
                BuildingTier.Upgraded => "has been upgraded",
                _ => "has been built"
            };

            NewsSystem.Instance?.Newsy(true, $"The settlement's {buildingName} {tierName}!");
            DebugLogger.Instance?.LogDebug("SETTLEMENT", $"{buildingName} advanced to {buildingState.Tier}");

            // Clear active proposed building — next tick will pick a new one
            State.ActiveProposedBuildingId = null;
        }

        /// <summary>
        /// Player endorses (+) or opposes (-) the current proposal.
        /// </summary>
        public bool VoteOnProposal(string playerName, int weight)
        {
            if (State.CurrentProposal == null) return false;
            State.CurrentProposal.PlayerVotes[playerName] = weight;
            return true;
        }

        public ActiveProposal GetCurrentProposal() => State.CurrentProposal;

        public ProposalTemplate GetProposalTemplate(string id) =>
            ProposalCatalog.FirstOrDefault(t => t.Id == id);

        public BuildingTier GetProposedBuildingTier(string id) =>
            State.ProposedBuildings.TryGetValue(id, out var state) ? state.Tier : BuildingTier.None;

        public bool HasProposedBuilding(string id, BuildingTier minTier = BuildingTier.Built) =>
            GetProposedBuildingTier(id) >= minTier;

        public List<ProposalTemplate> GetAllProposalTemplates() => ProposalCatalog;

        /// <summary>
        /// Player contributes gold to the settlement.
        /// </summary>
        public void ContributeGold(string playerName, long amount)
        {
            if (amount <= 0) return;

            if (!State.PlayerContributions.ContainsKey(playerName))
                State.PlayerContributions[playerName] = 0;
            State.PlayerContributions[playerName] += amount;

            // If there's an active building, contribute to it
            if (State.ActiveBuilding != null)
            {
                State.Buildings[State.ActiveBuilding.Value].ResourcePool += amount;
            }
            else if (State.ActiveProposedBuildingId != null && State.ProposedBuildings.ContainsKey(State.ActiveProposedBuildingId))
            {
                State.ProposedBuildings[State.ActiveProposedBuildingId].ResourcePool += amount;
            }
            else
            {
                // Otherwise add to communal treasury
                State.CommunalTreasury += amount;
            }
        }

        public int GetSettlerCount() => State.SettlerNames.Count;

        public BuildingTier GetBuildingTier(SettlementBuilding building) =>
            State.Buildings.TryGetValue(building, out var state) ? state.Tier : BuildingTier.None;

        public bool HasBuilding(SettlementBuilding building, BuildingTier minTier = BuildingTier.Built) =>
            GetBuildingTier(building) >= minTier;

        public static string GetBuildingDisplayName(SettlementBuilding building) => building switch
        {
            SettlementBuilding.Palisade => "Palisade",
            SettlementBuilding.Tavern => "Tavern",
            SettlementBuilding.MarketStall => "Market Stall",
            SettlementBuilding.Shrine => "Shrine",
            SettlementBuilding.Workshop => "Workshop",
            SettlementBuilding.Watchtower => "Watchtower",
            SettlementBuilding.CouncilHall => "Council Hall",
            _ => building.ToString()
        };

        public static string GetBuildingDescription(SettlementBuilding building) => building switch
        {
            SettlementBuilding.Palisade => "Wooden walls to defend the settlement",
            SettlementBuilding.Tavern => "A gathering place that attracts new settlers",
            SettlementBuilding.MarketStall => "A place to trade goods with settlers",
            SettlementBuilding.Shrine => "A place of healing and contemplation",
            SettlementBuilding.Workshop => "Craftsmen repair and identify equipment",
            SettlementBuilding.Watchtower => "Scouts report on dungeon dangers",
            SettlementBuilding.CouncilHall => "Settlers govern and generate income",
            _ => ""
        };

        public static string GetTierDisplayName(BuildingTier tier) => tier switch
        {
            BuildingTier.None => "Not Started",
            BuildingTier.Foundation => "Foundation",
            BuildingTier.Built => "Built",
            BuildingTier.Upgraded => "Upgraded",
            _ => "Unknown"
        };

        /// <summary>
        /// Get list of services available to the player based on built structures.
        /// </summary>
        public List<(string key, string label, SettlementBuilding building)> GetAvailableServices()
        {
            var services = new List<(string, string, SettlementBuilding)>();

            if (HasBuilding(SettlementBuilding.Tavern, BuildingTier.Built))
                services.Add(("1", "Tavern — Rest & XP Buff", SettlementBuilding.Tavern));
            if (HasBuilding(SettlementBuilding.Shrine, BuildingTier.Upgraded))
                services.Add(("2", "Shrine — Free Healing", SettlementBuilding.Shrine));
            if (HasBuilding(SettlementBuilding.MarketStall, BuildingTier.Built))
                services.Add(("3", "Market — Trade with Settlers", SettlementBuilding.MarketStall));
            if (HasBuilding(SettlementBuilding.Palisade, BuildingTier.Upgraded))
                services.Add(("4", "Palisade — Defense Buff", SettlementBuilding.Palisade));
            if (HasBuilding(SettlementBuilding.Workshop, BuildingTier.Upgraded))
                services.Add(("5", "Workshop — Identify Item", SettlementBuilding.Workshop));
            if (HasBuilding(SettlementBuilding.Watchtower, BuildingTier.Upgraded))
                services.Add(("6", "Watchtower — Scout Dungeon Floor", SettlementBuilding.Watchtower));
            if (HasBuilding(SettlementBuilding.CouncilHall, BuildingTier.Built))
                services.Add(("7", "Council Hall — Claim Gold Share", SettlementBuilding.CouncilHall));

            return services;
        }

        /// <summary>
        /// Get list of NPC-proposed building services available to the player.
        /// </summary>
        public List<(string key, string label, string buildingId)> GetProposedServices()
        {
            var services = new List<(string, string, string)>();
            int keyNum = 8; // Continue numbering after core services

            if (HasProposedBuilding("arena", BuildingTier.Upgraded))
                services.Add((keyNum++.ToString(), "Arena — Damage Buff", "arena"));
            if (HasProposedBuilding("thieves_den", BuildingTier.Upgraded))
                services.Add((keyNum++.ToString(), "Thieves' Den — Gold Find Buff", "thieves_den"));
            if (HasProposedBuilding("mystic_circle", BuildingTier.Upgraded))
                services.Add((keyNum++.ToString(), "Mystic Circle — Restore Mana", "mystic_circle"));
            if (HasProposedBuilding("prison", BuildingTier.Upgraded))
                services.Add((keyNum++.ToString(), "Prison — Trap Resistance", "prison"));
            if (HasProposedBuilding("scouts_lodge", BuildingTier.Upgraded))
                services.Add((keyNum++.ToString(), "Scouts' Lodge — Scout 3 Floors", "scouts_lodge"));
            if (HasProposedBuilding("library", BuildingTier.Upgraded))
                services.Add((keyNum++.ToString(), "Library — Knowledge Buff", "library"));
            if (HasProposedBuilding("herbalist_hut", BuildingTier.Built))
                services.Add((keyNum++.ToString(), "Herbalist Hut — Free Herb", "herbalist_hut"));
            if (HasProposedBuilding("gambling_hall", BuildingTier.Built))
                services.Add((keyNum++.ToString(), "Gambling Hall — Test Your Luck", "gambling_hall"));
            if (HasProposedBuilding("oracles_sanctum", BuildingTier.Upgraded))
                services.Add((keyNum++.ToString(), "Oracle's Sanctum — Glimpse the Future", "oracles_sanctum"));

            return services;
        }

        /// <summary>
        /// Reset for new game.
        /// </summary>
        public void Reset()
        {
            State = new SettlementState();
        }

        /// <summary>
        /// Restore from save data.
        /// </summary>
        public void RestoreFromSaveData(SettlementSaveData data)
        {
            if (data == null) return;

            State.Founded = data.Founded;
            State.SettlerNames = data.SettlerNames ?? new List<string>();
            State.CommunalTreasury = data.CommunalTreasury;
            State.PlayerContributions = data.PlayerContributions ?? new Dictionary<string, long>();
            State.ActiveBuilding = data.ActiveBuildingIndex >= 0
                ? (SettlementBuilding)data.ActiveBuildingIndex : null;

            if (data.BuildingTiers != null)
            {
                foreach (var kvp in data.BuildingTiers)
                {
                    if (Enum.TryParse<SettlementBuilding>(kvp.Key, out var building))
                    {
                        State.Buildings[building].Tier = (BuildingTier)kvp.Value;
                    }
                }
            }

            if (data.BuildingPools != null)
            {
                foreach (var kvp in data.BuildingPools)
                {
                    if (Enum.TryParse<SettlementBuilding>(kvp.Key, out var building))
                    {
                        State.Buildings[building].ResourcePool = kvp.Value;
                    }
                }
            }

            // NPC-proposed buildings
            State.ProposedBuildings.Clear();
            if (data.ProposedBuildingTiers != null)
            {
                foreach (var kvp in data.ProposedBuildingTiers)
                {
                    State.ProposedBuildings[kvp.Key] = new ProposedBuildingState
                    {
                        Id = kvp.Key,
                        Tier = (BuildingTier)kvp.Value,
                        ResourcePool = data.ProposedBuildingPools != null && data.ProposedBuildingPools.ContainsKey(kvp.Key)
                            ? data.ProposedBuildingPools[kvp.Key] : 0
                    };
                }
            }

            State.ActiveProposedBuildingId = data.ActiveProposedBuildingId;
            State.ProposalCooldowns = data.ProposalCooldowns != null ? new Dictionary<string, int>(data.ProposalCooldowns) : new();
            State.ContributionBoostTicks = data.ContributionBoostTicks;

            // Restore active proposal
            if (!string.IsNullOrEmpty(data.ActiveProposalId))
            {
                State.CurrentProposal = new ActiveProposal
                {
                    BuildingId = data.ActiveProposalId,
                    ProposerName = data.ActiveProposalProposer ?? "Unknown",
                    TicksRemaining = data.ActiveProposalTicksLeft,
                    SupportVotes = data.ActiveProposalSupport,
                    OpposeVotes = data.ActiveProposalOppose,
                    PlayerVotes = data.ActiveProposalPlayerVotes ?? new()
                };
            }
        }

        /// <summary>
        /// Export to save data.
        /// </summary>
        public SettlementSaveData ToSaveData()
        {
            return new SettlementSaveData
            {
                Founded = State.Founded,
                SettlerNames = State.SettlerNames.ToList(),
                CommunalTreasury = State.CommunalTreasury,
                PlayerContributions = new Dictionary<string, long>(State.PlayerContributions),
                ActiveBuildingIndex = State.ActiveBuilding.HasValue ? (int)State.ActiveBuilding.Value : -1,
                BuildingTiers = State.Buildings.ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => (int)kvp.Value.Tier),
                BuildingPools = State.Buildings.ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => kvp.Value.ResourcePool),
                // NPC-proposed buildings
                ProposedBuildingTiers = State.ProposedBuildings.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (int)kvp.Value.Tier),
                ProposedBuildingPools = State.ProposedBuildings.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ResourcePool),
                ActiveProposedBuildingId = State.ActiveProposedBuildingId,
                ProposalCooldowns = State.ProposalCooldowns.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                ContributionBoostTicks = State.ContributionBoostTicks,
                // Active proposal
                ActiveProposalId = State.CurrentProposal?.BuildingId,
                ActiveProposalProposer = State.CurrentProposal?.ProposerName,
                ActiveProposalTicksLeft = State.CurrentProposal?.TicksRemaining ?? 0,
                ActiveProposalSupport = State.CurrentProposal?.SupportVotes ?? 0,
                ActiveProposalOppose = State.CurrentProposal?.OpposeVotes ?? 0,
                ActiveProposalPlayerVotes = State.CurrentProposal?.PlayerVotes != null
                    ? new Dictionary<string, int>(State.CurrentProposal.PlayerVotes) : null
            };
        }
    }

    /// <summary>
    /// Serializable settlement data for save/load.
    /// </summary>
    public class SettlementSaveData
    {
        public bool Founded { get; set; }
        public List<string> SettlerNames { get; set; } = new();
        public long CommunalTreasury { get; set; }
        public Dictionary<string, long> PlayerContributions { get; set; } = new();
        public int ActiveBuildingIndex { get; set; } = -1;
        public Dictionary<string, int> BuildingTiers { get; set; } = new();
        public Dictionary<string, long> BuildingPools { get; set; } = new();
        // NPC-proposed buildings
        public Dictionary<string, int> ProposedBuildingTiers { get; set; } = new();
        public Dictionary<string, long> ProposedBuildingPools { get; set; } = new();
        public string ActiveProposedBuildingId { get; set; }
        public Dictionary<string, int> ProposalCooldowns { get; set; } = new();
        public int ContributionBoostTicks { get; set; }
        // Active proposal deliberation
        public string ActiveProposalId { get; set; }
        public string ActiveProposalProposer { get; set; }
        public int ActiveProposalTicksLeft { get; set; }
        public int ActiveProposalSupport { get; set; }
        public int ActiveProposalOppose { get; set; }
        public Dictionary<string, int> ActiveProposalPlayerVotes { get; set; }
    }
}
