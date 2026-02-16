using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using UsurperRemake.UI;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// NPC Petition System — NPCs proactively approach players with problems, requests,
    /// and opportunities based on actual world simulation state.
    /// Bridges the gap between the NPC world (marriages, affairs, deaths, factions) and player gameplay.
    /// </summary>
    public class NPCPetitionSystem
    {
        private static NPCPetitionSystem? _instance;
        public static NPCPetitionSystem Instance => _instance ??= new NPCPetitionSystem();

        private readonly Random _random = new();

        // Per-player rate limiting state (thread-safe for MUD mode)
        private class PlayerPetitionState
        {
            public int LocationChangesSinceLastPetition;
            public DateTime LastPetitionTime = DateTime.MinValue;
            public int PetitionsThisSession;
            public HashSet<string> PetitionedNPCs = new();
        }
        private readonly Dictionary<string, PlayerPetitionState> _playerStates = new();

        private PlayerPetitionState GetPlayerState(string playerName)
        {
            if (!_playerStates.TryGetValue(playerName, out var state))
            {
                state = new PlayerPetitionState();
                _playerStates[playerName] = state;
            }
            return state;
        }

        // Shared cooldown with consequence encounters (v0.30.8)
        public static DateTime LastWorldEncounterTime { get; set; } = DateTime.MinValue;

        public enum PetitionType
        {
            TroubledMarriage,
            MatchmakerRequest,
            CustodyDispute,
            FactionMission,
            RoyalPetition,
            DyingWish,
            MissingPerson,
            RivalryReport
        }

        /// <summary>
        /// Called once per location entry, after narrative encounters.
        /// Scans world state for petition-worthy situations and presents one if found.
        /// </summary>
        public async Task CheckForPetition(Character player, GameLocation location, TerminalEmulator terminal)
        {
            var state = GetPlayerState(player.Name2);
            state.LocationChangesSinceLastPetition++;

            // Rate limiting checks (per-player)
            if (state.PetitionsThisSession >= GameConfig.PetitionMaxPerSession)
                return;

            if (state.LocationChangesSinceLastPetition < GameConfig.PetitionMinLocationChanges)
                return;

            if ((DateTime.Now - state.LastPetitionTime).TotalMinutes < GameConfig.PetitionMinRealMinutes)
                return;

            // Shared cooldown with consequence encounters
            if ((DateTime.Now - LastWorldEncounterTime).TotalMinutes < GameConfig.PetitionMinRealMinutes)
                return;

            // Skip safe zones
            if (location == GameLocation.Home || location == GameLocation.Bank ||
                location == GameLocation.Church)
                return;

            // Base chance check
            if (_random.NextDouble() > GameConfig.PetitionBaseChance)
                return;

            // Try to find a valid petition based on world state
            var petition = FindBestPetition(player, location);
            if (petition == null)
                return;

            // Fire the petition
            state.LocationChangesSinceLastPetition = 0;
            state.LastPetitionTime = DateTime.Now;
            LastWorldEncounterTime = DateTime.Now;
            state.PetitionsThisSession++;

            await ExecutePetition(petition.Value.type, petition.Value.npc, player, terminal);
        }

        /// <summary>
        /// Reset session tracking (called on game load)
        /// </summary>
        public void ResetSession()
        {
            // Legacy: clear all player states (single-player mode)
            _playerStates.Clear();
        }

        public void ResetSession(string playerName)
        {
            _playerStates.Remove(playerName);
        }

        #region Petition Selection

        private (PetitionType type, NPC npc)? FindBestPetition(Character player, GameLocation location)
        {
            var npcs = NPCSpawnSystem.Instance?.ActiveNPCs;
            if (npcs == null || npcs.Count == 0) return null;

            // Shuffle petition types to vary encounters
            var petitionChecks = new List<Func<Character, GameLocation, List<NPC>, (PetitionType, NPC)?>>
            {
                TryTroubledMarriage,
                TryMatchmakerRequest,
                TryCustodyDispute,
                TryFactionMission,
                TryRoyalPetition,
                TryDyingWish,
                TryMissingPerson,
                TryRivalryReport
            };

            // Shuffle for variety
            for (int i = petitionChecks.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (petitionChecks[i], petitionChecks[j]) = (petitionChecks[j], petitionChecks[i]);
            }

            foreach (var check in petitionChecks)
            {
                var result = check(player, location, npcs);
                if (result != null)
                    return result;
            }

            return null;
        }

        private bool IsEligiblePetitioner(NPC npc, Character player)
        {
            if (npc.IsDead) return false;
            if (GetPlayerState(player.Name2).PetitionedNPCs.Contains(npc.Name2)) return false;
            if (npc.Memory == null) return false;
            // Must have some awareness of the player (not a total stranger)
            return true;
        }

        #endregion

        #region Petition Type Checks

        private (PetitionType, NPC)? TryTroubledMarriage(Character player, GameLocation location, List<NPC> npcs)
        {
            var registry = NPCMarriageRegistry.Instance;
            if (registry == null) return null;

            var marriages = registry.GetAllMarriages();
            foreach (var marriage in marriages)
            {
                var npc1 = NPCSpawnSystem.Instance?.GetNPCByName(marriage.Npc1Id) ??
                           npcs.FirstOrDefault(n => n.ID == marriage.Npc1Id || n.Name2 == marriage.Npc1Id);
                var npc2 = NPCSpawnSystem.Instance?.GetNPCByName(marriage.Npc2Id) ??
                           npcs.FirstOrDefault(n => n.ID == marriage.Npc2Id || n.Name2 == marriage.Npc2Id);

                if (npc1 == null || npc2 == null) continue;

                // Check both spouses for trouble
                foreach (var (petitioner, spouse) in new[] { (npc1, npc2), (npc2, npc1) })
                {
                    if (!IsEligiblePetitioner(petitioner, player)) continue;

                    // Petitioner must trust the player
                    float impression = petitioner.Memory.GetCharacterImpression(player.Name2);
                    if (impression < 0.3f) continue;

                    // Check for trouble: spouse having affair with another NPC, or hostile spouse
                    bool spouseHasAffair = false;
                    var affairs = registry.GetAllAffairs();
                    foreach (var affair in affairs)
                    {
                        if (affair.MarriedNpcId == spouse.ID || affair.MarriedNpcId == spouse.Name2)
                        {
                            spouseHasAffair = true;
                            break;
                        }
                    }

                    float spouseImpression = spouse.Memory?.GetCharacterImpression(petitioner.Name2) ?? 0f;
                    bool spouseHostile = spouseImpression < -0.3f;

                    if (spouseHasAffair || spouseHostile)
                    {
                        GetPlayerState(player.Name2).PetitionedNPCs.Add(petitioner.Name2);
                        return (PetitionType.TroubledMarriage, petitioner);
                    }
                }
            }

            return null;
        }

        private (PetitionType, NPC)? TryMatchmakerRequest(Character player, GameLocation location, List<NPC> npcs)
        {
            foreach (var npc in npcs)
            {
                if (!IsEligiblePetitioner(npc, player)) continue;
                if (npc.Married) continue;

                // NPC must be friendly with player
                float playerImpression = npc.Memory.GetCharacterImpression(player.Name2);
                if (playerImpression < 0.4f) continue;

                // Find if NPC has a crush on another unmarried NPC
                var impressions = npc.Brain?.Memory?.CharacterImpressions;
                if (impressions == null) continue;

                foreach (var kvp in impressions)
                {
                    if (kvp.Value < 0.5f) continue;
                    if (kvp.Key == player.Name2) continue;

                    var crushTarget = NPCSpawnSystem.Instance?.GetNPCByName(kvp.Key);
                    if (crushTarget == null || crushTarget.IsDead || crushTarget.Married) continue;

                    // Check target doesn't reciprocate (otherwise they'd get together on their own)
                    float reciprocation = crushTarget.Memory?.GetCharacterImpression(npc.Name2) ?? 0f;
                    if (reciprocation > 0.4f) continue;

                    GetPlayerState(player.Name2).PetitionedNPCs.Add(npc.Name2);
                    return (PetitionType.MatchmakerRequest, npc);
                }
            }

            return null;
        }

        private (PetitionType, NPC)? TryCustodyDispute(Character player, GameLocation location, List<NPC> npcs)
        {
            foreach (var npc in npcs)
            {
                if (!IsEligiblePetitioner(npc, player)) continue;
                if (npc.Married) continue; // Must be divorced/single now

                float impression = npc.Memory.GetCharacterImpression(player.Name2);
                if (impression < 0.2f) continue;

                // Check if NPC has children
                var children = FamilySystem.Instance?.GetChildrenOf(npc);
                if (children == null || children.Count == 0) continue;

                // Check if NPC was recently married (has MarriedTimes > 0 but isn't married now = divorced)
                if (npc.MarriedTimes <= 0) continue;

                GetPlayerState(player.Name2).PetitionedNPCs.Add(npc.Name2);
                return (PetitionType.CustodyDispute, npc);
            }

            return null;
        }

        private (PetitionType, NPC)? TryFactionMission(Character player, GameLocation location, List<NPC> npcs)
        {
            var factionSystem = FactionSystem.Instance;
            if (factionSystem == null) return null;

            // Find a faction-aligned NPC to approach the player
            Faction? targetFaction = null;

            if (factionSystem.PlayerFaction == null)
            {
                // Recruit player to a faction based on alignment
                if (player.Chivalry > 300) targetFaction = Faction.TheCrown;
                else if (player.Darkness > 150) targetFaction = Faction.TheShadows;
                else if (player.Wisdom > 20) targetFaction = Faction.TheFaith;
            }
            else
            {
                // Promotion mission for existing faction member
                targetFaction = factionSystem.PlayerFaction;
            }

            if (targetFaction == null) return null;

            // Find NPC belonging to target faction
            var factionNpc = npcs.FirstOrDefault(n =>
                IsEligiblePetitioner(n, player) &&
                n.NPCFaction == targetFaction &&
                n.Level >= 5);

            if (factionNpc == null) return null;

            GetPlayerState(player.Name2).PetitionedNPCs.Add(factionNpc.Name2);
            return (PetitionType.FactionMission, factionNpc);
        }

        private (PetitionType, NPC)? TryRoyalPetition(Character player, GameLocation location, List<NPC> npcs)
        {
            if (!player.King) return null;
            if (location != GameLocation.Castle && location != GameLocation.MainStreet) return null;

            // Find a random NPC to petition the king
            var petitioners = npcs.Where(n =>
                IsEligiblePetitioner(n, player) &&
                (n.Memory?.GetCharacterImpression(player.Name2) ?? 0f) > -0.3f &&
                n.Level >= 3).ToList();

            if (petitioners.Count == 0) return null;

            var petitioner = petitioners[_random.Next(petitioners.Count)];
            GetPlayerState(player.Name2).PetitionedNPCs.Add(petitioner.Name2);
            return (PetitionType.RoyalPetition, petitioner);
        }

        private (PetitionType, NPC)? TryDyingWish(Character player, GameLocation location, List<NPC> npcs)
        {
            foreach (var npc in npcs)
            {
                if (!IsEligiblePetitioner(npc, player)) continue;

                float impression = npc.Memory.GetCharacterImpression(player.Name2);
                if (impression < 0.2f) continue;

                // Check if NPC is near end of life
                var race = npc.Race;
                if (!GameConfig.RaceLifespan.TryGetValue(race, out int maxAge)) continue;

                if (npc.Age < maxAge - 5) continue; // Must be within 5 years of max

                GetPlayerState(player.Name2).PetitionedNPCs.Add(npc.Name2);
                return (PetitionType.DyingWish, npc);
            }

            return null;
        }

        private (PetitionType, NPC)? TryMissingPerson(Character player, GameLocation location, List<NPC> npcs)
        {
            // Get player's active quests to prevent duplicates
            var activeQuests = QuestSystem.GetPlayerQuests(player.Name2);

            foreach (var npc in npcs)
            {
                if (!IsEligiblePetitioner(npc, player)) continue;

                float impression = npc.Memory.GetCharacterImpression(player.Name2);
                if (impression < 0.0f) continue;

                // Check if NPC's spouse or close friend is dead (non-aged death)
                string spouseName = RelationshipSystem.GetSpouseName(npc);
                if (!string.IsNullOrEmpty(spouseName))
                {
                    var spouse = NPCSpawnSystem.Instance?.GetNPCByName(spouseName, includeDead: true);
                    if (spouse != null && spouse.IsDead && !spouse.IsAgedDeath)
                    {
                        // Skip if player already has an active quest for this missing NPC
                        if (activeQuests.Any(q => q.TargetNPCName.Equals(spouseName, StringComparison.OrdinalIgnoreCase)))
                            continue;
                        GetPlayerState(player.Name2).PetitionedNPCs.Add(npc.Name2);
                        return (PetitionType.MissingPerson, npc);
                    }
                }

                // Check for dead friends (high impression NPCs)
                var friendImpressions = npc.Brain?.Memory?.CharacterImpressions;
                if (friendImpressions != null)
                {
                    foreach (var kvp in friendImpressions)
                    {
                        if (kvp.Value < 0.5f) continue;
                        var friend = NPCSpawnSystem.Instance?.GetNPCByName(kvp.Key, includeDead: true);
                        if (friend != null && friend.IsDead && !friend.IsAgedDeath)
                        {
                            // Skip if player already has an active quest for this missing NPC
                            if (activeQuests.Any(q => q.TargetNPCName.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase)))
                                continue;
                            GetPlayerState(player.Name2).PetitionedNPCs.Add(npc.Name2);
                            return (PetitionType.MissingPerson, npc);
                        }
                    }
                }
            }

            return null;
        }

        private (PetitionType, NPC)? TryRivalryReport(Character player, GameLocation location, List<NPC> npcs)
        {
            // Find a friendly NPC who could warn the player
            var friendlyNpcs = npcs.Where(n =>
                IsEligiblePetitioner(n, player) &&
                (n.Memory?.GetCharacterImpression(player.Name2) ?? 0f) > 0.3f).ToList();

            if (friendlyNpcs.Count == 0) return null;

            // Check for threats: powerful NPC teams near player's level, or court plots
            bool hasThreat = false;

            // Court intrigue against player-king
            if (player.King)
            {
                var king = CastleLocation.GetCurrentKing();
                if (king?.ActivePlots?.Count > 0)
                    hasThreat = true;
            }

            // Powerful NPC teams
            if (!hasThreat)
            {
                var rivalTeams = npcs.Where(n => !n.IsDead && !string.IsNullOrEmpty(n.Team) &&
                    Math.Abs(n.Level - player.Level) <= 15 && n.Level >= 10).ToList();
                if (rivalTeams.Count >= 2) hasThreat = true;
            }

            if (!hasThreat) return null;

            var warner = friendlyNpcs[_random.Next(friendlyNpcs.Count)];
            GetPlayerState(player.Name2).PetitionedNPCs.Add(warner.Name2);
            return (PetitionType.RivalryReport, warner);
        }

        #endregion

        #region Petition Execution

        private async Task ExecutePetition(PetitionType type, NPC npc, Character player, TerminalEmulator terminal)
        {
            switch (type)
            {
                case PetitionType.TroubledMarriage:
                    await ExecuteTroubledMarriage(npc, player, terminal);
                    break;
                case PetitionType.MatchmakerRequest:
                    await ExecuteMatchmakerRequest(npc, player, terminal);
                    break;
                case PetitionType.CustodyDispute:
                    await ExecuteCustodyDispute(npc, player, terminal);
                    break;
                case PetitionType.FactionMission:
                    await ExecuteFactionMission(npc, player, terminal);
                    break;
                case PetitionType.RoyalPetition:
                    await ExecuteRoyalPetition(npc, player, terminal);
                    break;
                case PetitionType.DyingWish:
                    await ExecuteDyingWish(npc, player, terminal);
                    break;
                case PetitionType.MissingPerson:
                    await ExecuteMissingPerson(npc, player, terminal);
                    break;
                case PetitionType.RivalryReport:
                    await ExecuteRivalryReport(npc, player, terminal);
                    break;
            }
        }

        #endregion

        #region Petition 1: Troubled Marriage

        private async Task ExecuteTroubledMarriage(NPC petitioner, Character player, TerminalEmulator terminal)
        {
            // Find the spouse
            string spouseName = RelationshipSystem.GetSpouseName(petitioner);
            var spouse = NPCSpawnSystem.Instance?.GetNPCByName(spouseName);

            if (spouse == null || string.IsNullOrEmpty(spouseName))
            {
                // Spouse died or disappeared since check — skip gracefully
                return;
            }

            // Determine what's wrong
            bool spouseHasAffair = false;
            var affairs = NPCMarriageRegistry.Instance?.GetAllAffairs();
            if (affairs != null)
            {
                spouseHasAffair = affairs.Any(a =>
                    a.MarriedNpcId == spouse.ID || a.MarriedNpcId == spouse.Name2);
            }

            string troubleDescription = spouseHasAffair
                ? $"I think {spouseName} is seeing someone else behind my back."
                : $"{spouseName} has grown cold and cruel. I don't know what to do.";

            terminal.ClearScreen();
            UIHelper.DrawBoxTop(terminal, "A TROUBLED SOUL", "bright_magenta");
            UIHelper.DrawBoxEmpty(terminal, "bright_magenta");
            UIHelper.DrawBoxLine(terminal, $"  {petitioner.Name2} approaches you with tears in their eyes.", "bright_magenta", "white");
            UIHelper.DrawBoxEmpty(terminal, "bright_magenta");
            UIHelper.DrawBoxLine(terminal, $"  \"{player.Name2}, I need your help. Please.\"", "bright_magenta", "bright_cyan");
            UIHelper.DrawBoxLine(terminal, $"  \"{troubleDescription}\"", "bright_magenta", "cyan");
            UIHelper.DrawBoxLine(terminal, $"  \"You're the only one I can trust with this.\"", "bright_magenta", "cyan");
            UIHelper.DrawBoxEmpty(terminal, "bright_magenta");
            UIHelper.DrawBoxSeparator(terminal, "bright_magenta");
            UIHelper.DrawMenuOption(terminal, "C", "Counsel them — offer advice", "bright_magenta", "bright_yellow", "white");
            UIHelper.DrawMenuOption(terminal, "F", $"Confront {spouseName} directly", "bright_magenta", "bright_yellow", "white");
            UIHelper.DrawMenuOption(terminal, "E", "Exploit their vulnerability", "bright_magenta", "bright_yellow", "red");
            UIHelper.DrawMenuOption(terminal, "R", "Refuse to get involved", "bright_magenta", "bright_yellow", "gray");
            UIHelper.DrawBoxBottom(terminal, "bright_magenta");

            var choice = await terminal.GetInput("\n  What do you do? ");

            switch (choice.ToUpper())
            {
                case "C": // Counsel
                    await CounselTroubledSpouse(petitioner, spouse, player, terminal);
                    break;
                case "F": // Confront
                    await ConfrontTroubledSpouse(petitioner, spouse, player, terminal, spouseHasAffair);
                    break;
                case "E": // Exploit
                    await ExploitTroubledSpouse(petitioner, spouse, player, terminal);
                    break;
                default: // Refuse
                    terminal.SetColor("gray");
                    terminal.WriteLine("\n  \"I'm sorry, this isn't my business.\"");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {petitioner.Name2} nods sadly and walks away.");
                    petitioner.Memory?.RecordEvent(new MemoryEvent
                    {
                        Type = MemoryType.SocialInteraction,
                        Description = $"{player.Name2} refused to help with my marriage troubles",
                        InvolvedCharacter = player.Name2,
                        Importance = 0.3f,
                        EmotionalImpact = -0.1f
                    });
                    await terminal.PressAnyKey();
                    break;
            }
        }

        private async Task CounselTroubledSpouse(NPC petitioner, NPC spouse, Character player, TerminalEmulator terminal)
        {
            // CHA check: 30 + CHA*2, capped at 75%
            int successChance = Math.Min(75, 30 + (int)(player.Charisma * 2));
            bool success = _random.Next(100) < successChance;

            if (success)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"\n  {petitioner.Name2} thinks about what you said.");
                terminal.WriteLine($"  \"Youre right. I gotta talk to {spouse.Name2}. For real this time.\"");
                terminal.SetColor("white");
                terminal.WriteLine($"  {petitioner.Name2} looks a little better. Not great, but better.");

                // Improve petitioner-spouse relationship
                RelationshipSystem.UpdateRelationship(petitioner, spouse, 1, 3, overrideMaxFeeling: true);

                // Petitioner grateful to player
                petitioner.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.Helped,
                    Description = $"{player.Name2} gave wise counsel about my marriage",
                    InvolvedCharacter = player.Name2,
                    Importance = 0.7f,
                    EmotionalImpact = 0.3f
                });

                NewsSystem.Instance?.Newsy($"{player.Name2} helped {petitioner.Name2} through a difficult time.");
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"\n  Your advice falls flat. {petitioner.Name2} looks unconvinced.");
                terminal.SetColor("white");
                terminal.WriteLine($"  \"That's... not helpful at all. Maybe I was wrong to ask you.\"");

                petitioner.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.SocialInteraction,
                    Description = $"{player.Name2} tried to help but gave poor advice",
                    InvolvedCharacter = player.Name2,
                    Importance = 0.3f,
                    EmotionalImpact = -0.15f
                });
            }

            await terminal.PressAnyKey();
        }

        private async Task ConfrontTroubledSpouse(NPC petitioner, NPC spouse, Character player,
            TerminalEmulator terminal, bool spouseHasAffair)
        {
            terminal.SetColor("white");
            terminal.WriteLine($"\n  You find {spouse.Name2} and confront them.");

            if (spouseHasAffair)
                terminal.WriteLine($"  \"I know what you're doing to {petitioner.Name2}. It ends now.\"");
            else
                terminal.WriteLine($"  \"{petitioner.Name2} tells me you've changed. What's going on?\"");

            // CHA check for peaceful resolution
            int peaceChance = Math.Min(70, 25 + (int)(player.Charisma * 2));
            bool peaceful = _random.Next(100) < peaceChance;

            if (peaceful)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"\n  {spouse.Name2} is taken aback by your directness.");
                terminal.WriteLine($"  \"You're right. I've been a fool. I'll make things right.\"");

                // Improve their marriage
                RelationshipSystem.UpdateRelationship(petitioner, spouse, 1, 5, overrideMaxFeeling: true);
                RelationshipSystem.UpdateRelationship(spouse, petitioner, 1, 5, overrideMaxFeeling: true);

                // Both NPCs grateful
                petitioner.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.Saved,
                    Description = $"{player.Name2} confronted my spouse and saved my marriage",
                    InvolvedCharacter = player.Name2,
                    Importance = 0.9f,
                    EmotionalImpact = 0.5f
                });
                spouse.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.Helped,
                    Description = $"{player.Name2} talked some sense into me",
                    InvolvedCharacter = player.Name2,
                    Importance = 0.6f,
                    EmotionalImpact = 0.2f
                });

                player.Chivalry += 5;
                NewsSystem.Instance?.Newsy($"{player.Name2} intervened to save the marriage of {petitioner.Name2} and {spouse.Name2}.");
            }
            else
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine($"\n  {spouse.Name2}'s eyes narrow. \"Mind your own business!\"");
                terminal.WriteLine($"  {spouse.Name2} shoves you and storms off.");

                // Spouse hostile to player
                spouse.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.Threatened,
                    Description = $"{player.Name2} confronted me about my marriage",
                    InvolvedCharacter = player.Name2,
                    Importance = 0.7f,
                    EmotionalImpact = -0.4f
                });

                // Petitioner still grateful for trying
                petitioner.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.Defended,
                    Description = $"{player.Name2} stood up for me against {spouse.Name2}",
                    InvolvedCharacter = player.Name2,
                    Importance = 0.7f,
                    EmotionalImpact = 0.3f
                });

                NewsSystem.Instance?.Newsy($"{player.Name2} confronted {spouse.Name2} about their treatment of {petitioner.Name2}.");
            }

            await terminal.PressAnyKey();
        }

        private async Task ExploitTroubledSpouse(NPC petitioner, NPC spouse, Character player, TerminalEmulator terminal)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"\n  You see an opportunity in {petitioner.Name2}'s vulnerability.");
            terminal.SetColor("white");
            terminal.WriteLine($"  \"Forget about {spouse.Name2}. You deserve someone who appreciates you...\"");
            terminal.SetColor("yellow");
            terminal.WriteLine($"  \"Someone like me.\"");

            // CHA check for seduction
            int seduceChance = Math.Min(70, 20 + (int)(player.Charisma * 2));
            bool success = _random.Next(100) < seduceChance;

            if (success)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine($"\n  {petitioner.Name2}'s eyes widen... then soften.");
                terminal.WriteLine($"  \"Maybe you're right. Maybe I've been looking for comfort in the wrong place.\"");

                // Start affair pathway — boost relationship significantly
                RelationshipSystem.UpdateRelationship(player, petitioner, 1, 8, overrideMaxFeeling: true);
                RelationshipSystem.UpdateRelationship(petitioner, (Character)player, 1, 8, overrideMaxFeeling: true);

                petitioner.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.SocialInteraction,
                    Description = $"{player.Name2} seduced me while I was vulnerable",
                    InvolvedCharacter = player.Name2,
                    Importance = 0.9f,
                    EmotionalImpact = 0.4f
                });

                // Spouse becomes hostile to player when they find out
                spouse.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.Betrayed,
                    Description = $"{player.Name2} seduced my spouse {petitioner.Name2}",
                    InvolvedCharacter = player.Name2,
                    Importance = 0.9f,
                    EmotionalImpact = -0.8f
                });

                player.Darkness += 15;
                NewsSystem.Instance?.Newsy($"Scandalous rumors swirl about {player.Name2} and {petitioner.Name2}...");
            }
            else
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine($"\n  {petitioner.Name2} recoils in disgust.");
                terminal.WriteLine($"  \"Is THAT what this was about? I thought you were my friend!\"");
                terminal.SetColor("white");
                terminal.WriteLine($"  {petitioner.Name2} storms off, furious.");

                petitioner.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.Betrayed,
                    Description = $"{player.Name2} tried to take advantage of my vulnerability",
                    InvolvedCharacter = player.Name2,
                    Importance = 0.8f,
                    EmotionalImpact = -0.6f
                });

                player.Darkness += 5;
            }

            await terminal.PressAnyKey();
        }

        #endregion

        #region Petition 2: Matchmaker Request

        private async Task ExecuteMatchmakerRequest(NPC suitor, Character player, TerminalEmulator terminal)
        {
            // Find the crush target
            var impressions = suitor.Brain?.Memory?.CharacterImpressions;
            if (impressions == null) return;

            string? crushName = null;
            foreach (var kvp in impressions)
            {
                if (kvp.Value >= 0.5f && kvp.Key != player.Name2)
                {
                    var target = NPCSpawnSystem.Instance?.GetNPCByName(kvp.Key);
                    if (target != null && !target.IsDead && !target.Married)
                    {
                        crushName = kvp.Key;
                        break;
                    }
                }
            }

            if (crushName == null) return;

            terminal.ClearScreen();
            UIHelper.DrawBoxTop(terminal, "MATTERS OF THE HEART", "bright_magenta");
            UIHelper.DrawBoxEmpty(terminal, "bright_magenta");
            UIHelper.DrawBoxLine(terminal, $"  {suitor.Name2} pulls you aside, looking nervous.", "bright_magenta", "white");
            UIHelper.DrawBoxEmpty(terminal, "bright_magenta");
            UIHelper.DrawBoxLine(terminal, $"  \"{player.Name2}, can I tell you something in confidence?\"", "bright_magenta", "bright_cyan");
            UIHelper.DrawBoxLine(terminal, $"  \"I... I have feelings for {crushName}. But they barely notice me.\"", "bright_magenta", "cyan");
            UIHelper.DrawBoxLine(terminal, $"  \"Could you put in a good word for me? Maybe help us meet?\"", "bright_magenta", "cyan");
            UIHelper.DrawBoxEmpty(terminal, "bright_magenta");
            UIHelper.DrawBoxSeparator(terminal, "bright_magenta");
            UIHelper.DrawMenuOption(terminal, "W", $"Play wingman for {suitor.Name2}", "bright_magenta", "bright_yellow", "white");
            UIHelper.DrawMenuOption(terminal, "S", $"Sabotage — warn {crushName} away", "bright_magenta", "bright_yellow", "red");
            UIHelper.DrawMenuOption(terminal, "H", "Give honest advice about their chances", "bright_magenta", "bright_yellow", "white");
            UIHelper.DrawMenuOption(terminal, "R", "Refuse to get involved", "bright_magenta", "bright_yellow", "gray");
            UIHelper.DrawBoxBottom(terminal, "bright_magenta");

            var choice = await terminal.GetInput("\n  What do you do? ");
            var crushNpc = NPCSpawnSystem.Instance?.GetNPCByName(crushName);

            switch (choice.ToUpper())
            {
                case "W": // Wingman
                    int wingmanChance = Math.Min(75, 35 + (int)(player.Charisma * 2));
                    bool wingmanSuccess = _random.Next(100) < wingmanChance;

                    if (wingmanSuccess && crushNpc != null)
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"\n  You speak to {crushName} about {suitor.Name2}'s qualities.");
                        terminal.WriteLine($"  \"Really? I never noticed before... Tell {suitor.Name2} I'd love to talk.\"");

                        crushNpc.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.SocialInteraction,
                            Description = $"{player.Name2} introduced me to {suitor.Name2}",
                            InvolvedCharacter = suitor.Name2,
                            Importance = 0.6f,
                            EmotionalImpact = 0.3f
                        });

                        // Boost crush's impression of suitor
                        if (crushNpc.Brain?.Memory != null)
                        {
                            var impressionDict = crushNpc.Brain.Memory.CharacterImpressions;
                            if (impressionDict.ContainsKey(suitor.Name2))
                                impressionDict[suitor.Name2] = Math.Min(1.0f, impressionDict[suitor.Name2] + 0.3f);
                            else
                                impressionDict[suitor.Name2] = 0.3f;
                        }

                        suitor.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Helped,
                            Description = $"{player.Name2} helped me get closer to {crushName}",
                            InvolvedCharacter = player.Name2,
                            Importance = 0.8f,
                            EmotionalImpact = 0.5f
                        });

                        long reward = 100 + suitor.Level * 20;
                        player.Gold += reward;
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"\n  {suitor.Name2} is overjoyed! They give you {reward} gold as thanks.");

                        player.Chivalry += 3;
                        NewsSystem.Instance?.Newsy($"{player.Name2} played matchmaker for {suitor.Name2} and {crushName}.");
                    }
                    else
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"\n  You try to talk up {suitor.Name2}, but {crushName} isn't interested.");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  \"Sorry, but they're just not my type.\"");

                        suitor.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Helped,
                            Description = $"{player.Name2} tried to help me with {crushName} but it didn't work",
                            InvolvedCharacter = player.Name2,
                            Importance = 0.5f,
                            EmotionalImpact = 0.1f
                        });
                    }
                    break;

                case "S": // Sabotage
                    if (crushNpc != null)
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine($"\n  You tell {crushName} that {suitor.Name2} is trouble.");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  \"Thanks for the warning. I'll steer clear of them.\"");

                        // Crush now avoids suitor
                        if (crushNpc.Brain?.Memory != null)
                        {
                            var impressionDict = crushNpc.Brain.Memory.CharacterImpressions;
                            impressionDict[suitor.Name2] = Math.Max(-1.0f,
                                (impressionDict.GetValueOrDefault(suitor.Name2, 0f)) - 0.5f);
                        }

                        player.Darkness += 10;

                        // Risk of discovery
                        if (_random.Next(100) < 30)
                        {
                            terminal.SetColor("bright_red");
                            terminal.WriteLine($"\n  Word gets back to {suitor.Name2} about what you did...");
                            terminal.WriteLine($"  \"You SABOTAGED me?! I thought we were friends!\"");

                            suitor.Memory?.RecordEvent(new MemoryEvent
                            {
                                Type = MemoryType.Betrayed,
                                Description = $"{player.Name2} sabotaged my chance with {crushName}",
                                InvolvedCharacter = player.Name2,
                                Importance = 0.9f,
                                EmotionalImpact = -0.7f
                            });

                            NewsSystem.Instance?.Newsy($"{suitor.Name2} discovered {player.Name2}'s betrayal!");
                        }
                    }
                    break;

                case "H": // Honest advice
                    terminal.SetColor("cyan");
                    terminal.WriteLine($"\n  \"Look, I'll be honest with you. {crushName} might not feel the same way.\"");
                    terminal.WriteLine($"  \"But you should tell them yourself. That takes real courage.\"");
                    terminal.SetColor("white");
                    terminal.WriteLine($"\n  {suitor.Name2} nods thoughtfully. \"You're right. Thank you for being straight with me.\"");

                    suitor.Memory?.RecordEvent(new MemoryEvent
                    {
                        Type = MemoryType.Helped,
                        Description = $"{player.Name2} gave honest advice about my feelings for {crushName}",
                        InvolvedCharacter = player.Name2,
                        Importance = 0.4f,
                        EmotionalImpact = 0.15f
                    });
                    break;

                default: // Refuse
                    terminal.SetColor("gray");
                    terminal.WriteLine($"\n  \"Sorry, I'm not really the matchmaker type.\"");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {suitor.Name2} looks disappointed but understands.");
                    break;
            }

            await terminal.PressAnyKey();
        }

        #endregion

        #region Petition 3: Custody Dispute

        private async Task ExecuteCustodyDispute(NPC petitioner, Character player, TerminalEmulator terminal)
        {
            // Find the ex-spouse (was married, now isn't)
            var impressions = petitioner.Brain?.Memory?.CharacterImpressions;
            string? exName = null;
            NPC? exSpouse = null;

            if (impressions != null)
            {
                // Look for NPCs the petitioner has strong negative feelings about (likely ex)
                foreach (var kvp in impressions.OrderBy(k => k.Value))
                {
                    var candidate = NPCSpawnSystem.Instance?.GetNPCByName(kvp.Key);
                    if (candidate != null && !candidate.IsDead && candidate.MarriedTimes > 0 && !candidate.Married)
                    {
                        exName = kvp.Key;
                        exSpouse = candidate;
                        break;
                    }
                }
            }

            if (exName == null)
            {
                exName = "their former spouse";
            }

            var children = FamilySystem.Instance?.GetChildrenOf(petitioner) ?? new List<Child>();
            int childCount = children.Count;

            terminal.ClearScreen();
            UIHelper.DrawBoxTop(terminal, "CUSTODY DISPUTE", "bright_yellow");
            UIHelper.DrawBoxEmpty(terminal, "bright_yellow");
            UIHelper.DrawBoxLine(terminal, $"  {petitioner.Name2} approaches, visibly distressed.", "bright_yellow", "white");
            UIHelper.DrawBoxEmpty(terminal, "bright_yellow");
            UIHelper.DrawBoxLine(terminal, $"  \"{player.Name2}, I need someone with authority to help me.\"", "bright_yellow", "bright_cyan");
            UIHelper.DrawBoxLine(terminal, $"  \"Since the divorce, {exName} won't let me see my {(childCount == 1 ? "child" : "children")}.\"", "bright_yellow", "cyan");
            UIHelper.DrawBoxLine(terminal, $"  \"I just want to be part of their {(childCount == 1 ? "life" : "lives")}. Please help.\"", "bright_yellow", "cyan");
            UIHelper.DrawBoxEmpty(terminal, "bright_yellow");
            UIHelper.DrawBoxSeparator(terminal, "bright_yellow");
            UIHelper.DrawMenuOption(terminal, "M", "Mediate between both parents", "bright_yellow", "bright_cyan", "white");
            UIHelper.DrawMenuOption(terminal, "P", $"Side with {petitioner.Name2}", "bright_yellow", "bright_cyan", "white");
            if (exSpouse != null)
                UIHelper.DrawMenuOption(terminal, "X", $"Side with {exName}", "bright_yellow", "bright_cyan", "white");
            UIHelper.DrawMenuOption(terminal, "I", "Stay out of it", "bright_yellow", "bright_cyan", "gray");
            UIHelper.DrawBoxBottom(terminal, "bright_yellow");

            var choice = await terminal.GetInput("\n  What do you do? ");

            switch (choice.ToUpper())
            {
                case "M": // Mediate
                    int mediateChance = Math.Min(70, 30 + (int)(player.Charisma * 2));
                    bool mediateSuccess = _random.Next(100) < mediateChance;

                    if (mediateSuccess)
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"\n  You bring both parties together and work out an arrangement.");
                        terminal.WriteLine($"  Both parents agree to shared custody. The {(childCount == 1 ? "child is" : "children are")} relieved.");

                        petitioner.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Saved,
                            Description = $"{player.Name2} mediated my custody dispute successfully",
                            InvolvedCharacter = player.Name2, Importance = 0.9f, EmotionalImpact = 0.5f
                        });
                        if (exSpouse != null)
                        {
                            exSpouse.Memory?.RecordEvent(new MemoryEvent
                            {
                                Type = MemoryType.Helped,
                                Description = $"{player.Name2} mediated the custody dispute fairly",
                                InvolvedCharacter = player.Name2, Importance = 0.6f, EmotionalImpact = 0.2f
                            });
                        }

                        player.Chivalry += 8;
                        long reward = 200 + petitioner.Level * 30;
                        player.Gold += reward;
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"\n  Both parents thank you. You receive {reward} gold.");
                        NewsSystem.Instance?.Newsy($"{player.Name2} successfully mediated a custody dispute between {petitioner.Name2} and {exName}.");
                    }
                    else
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"\n  Despite your best efforts, both parents refuse to compromise.");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  \"You don't understand!\" they both shout. The meeting ends in tears.");

                        petitioner.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.SocialInteraction,
                            Description = $"{player.Name2} tried to mediate but it didn't work",
                            InvolvedCharacter = player.Name2, Importance = 0.4f, EmotionalImpact = -0.1f
                        });
                    }
                    break;

                case "P": // Side with petitioner
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine($"\n  You advocate strongly for {petitioner.Name2}'s parental rights.");

                    petitioner.Memory?.RecordEvent(new MemoryEvent
                    {
                        Type = MemoryType.Defended,
                        Description = $"{player.Name2} took my side in the custody dispute",
                        InvolvedCharacter = player.Name2, Importance = 0.8f, EmotionalImpact = 0.5f
                    });

                    if (exSpouse != null)
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine($"  {exName} glares at you. \"You'll regret taking sides.\"");
                        exSpouse.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Betrayed,
                            Description = $"{player.Name2} sided against me in my custody dispute",
                            InvolvedCharacter = player.Name2, Importance = 0.7f, EmotionalImpact = -0.5f
                        });
                    }

                    NewsSystem.Instance?.Newsy($"{player.Name2} sided with {petitioner.Name2} in a custody dispute with {exName}.");
                    break;

                case "X" when exSpouse != null: // Side with ex
                    terminal.SetColor("cyan");
                    terminal.WriteLine($"\n  After hearing both sides, you believe {exName} has the right of it.");
                    terminal.SetColor("red");
                    terminal.WriteLine($"  {petitioner.Name2} is devastated. \"I thought you'd understand...\"");

                    petitioner.Memory?.RecordEvent(new MemoryEvent
                    {
                        Type = MemoryType.Betrayed,
                        Description = $"{player.Name2} sided against me in my custody dispute",
                        InvolvedCharacter = player.Name2, Importance = 0.8f, EmotionalImpact = -0.5f
                    });
                    exSpouse.Memory?.RecordEvent(new MemoryEvent
                    {
                        Type = MemoryType.Defended,
                        Description = $"{player.Name2} supported me in the custody dispute",
                        InvolvedCharacter = player.Name2, Importance = 0.7f, EmotionalImpact = 0.4f
                    });

                    NewsSystem.Instance?.Newsy($"{player.Name2} sided with {exName} against {petitioner.Name2} in a custody dispute.");
                    break;

                default: // Ignore
                    terminal.SetColor("gray");
                    terminal.WriteLine($"\n  \"I'm sorry, but this is a family matter. I can't intervene.\"");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {petitioner.Name2} walks away, looking defeated.");
                    break;
            }

            await terminal.PressAnyKey();
        }

        #endregion

        #region Petition 4: Faction Mission

        private async Task ExecuteFactionMission(NPC factionNpc, Character player, TerminalEmulator terminal)
        {
            var faction = factionNpc.NPCFaction ?? Faction.TheCrown;
            string factionName = faction switch
            {
                Faction.TheCrown => "The Crown",
                Faction.TheShadows => "The Shadows",
                Faction.TheFaith => "The Faith",
                _ => "our organization"
            };

            bool isRecruit = FactionSystem.Instance?.PlayerFaction != faction;

            // Pick a mission based on faction
            string missionDesc;
            string missionTarget;
            int goldReward;

            // Collect NPC names already targeted by the player's active quests to avoid duplicates
            var existingTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (player is Player p2)
            {
                foreach (var q in p2.ActiveQuests)
                {
                    if (!string.IsNullOrEmpty(q.TargetNPCName))
                        existingTargets.Add(q.TargetNPCName);
                }
            }

            switch (faction)
            {
                case Faction.TheCrown:
                    var criminal = NPCSpawnSystem.Instance?.ActiveNPCs?
                        .Where(n => !n.IsDead && n.Darkness > 100 && n.Level >= 5
                            && !existingTargets.Contains(n.Name2))
                        .OrderBy(_ => _random.Next())
                        .FirstOrDefault();
                    missionTarget = criminal?.Name2 ?? "a known criminal";
                    missionDesc = $"A villain named {missionTarget} has been terrorizing honest citizens. Bring them to justice.";
                    goldReward = 300 + player.Level * 25;
                    break;
                case Faction.TheShadows:
                    int shadowRank = FactionSystem.Instance?.FactionRank ?? 0;
                    bool isAssassination = shadowRank >= GameConfig.AssassinContractMinRank
                        && _random.NextDouble() < GameConfig.AssassinContractChance;

                    if (isAssassination)
                    {
                        var teamName = (player as Player)?.TeamName;
                        var target = NPCSpawnSystem.Instance?.ActiveNPCs?
                            .Where(n => !n.IsDead && n.Level >= 5 && !n.IsStoryNPC && !n.King
                                && (string.IsNullOrEmpty(teamName) || n.TeamName != teamName)
                                && !existingTargets.Contains(n.Name2))
                            .OrderBy(_ => _random.Next())
                            .FirstOrDefault();

                        if (target != null)
                        {
                            missionTarget = target.Name2;
                            missionDesc = $"We need {target.Name2} taken care of. Permanently.";
                            goldReward = 500 + player.Level * 40;
                            if (player.Class == CharacterClass.Assassin)
                                goldReward = (int)(goldReward * (1.0f + GameConfig.AssassinClassGoldBonus));
                        }
                        else
                        {
                            missionTarget = "a valuable contact";
                            missionDesc = "We need someone with your skills for a delicate retrieval. Details on acceptance.";
                            goldReward = 400 + player.Level * 30;
                        }
                    }
                    else
                    {
                        missionTarget = "a valuable contact";
                        missionDesc = "We need someone with your skills for a delicate retrieval. Details on acceptance.";
                        goldReward = 400 + player.Level * 30;
                    }
                    break;
                default: // Faith
                    var lostSoul = NPCSpawnSystem.Instance?.ActiveNPCs?
                        .Where(n => !n.IsDead && n.Darkness > 50 && n.Level >= 3
                            && !existingTargets.Contains(n.Name2))
                        .OrderBy(_ => _random.Next())
                        .FirstOrDefault();
                    missionTarget = lostSoul?.Name2 ?? "a lost soul";
                    missionDesc = $"{missionTarget} has strayed from the path. Bring them back to the light.";
                    goldReward = 250 + player.Level * 20;
                    break;
            }

            terminal.ClearScreen();
            UIHelper.DrawBoxTop(terminal, "FACTION APPROACH", "bright_cyan");
            UIHelper.DrawBoxEmpty(terminal, "bright_cyan");
            UIHelper.DrawBoxLine(terminal, $"  {factionNpc.Name2} catches your eye and approaches purposefully.", "bright_cyan", "white");
            UIHelper.DrawBoxEmpty(terminal, "bright_cyan");

            if (isRecruit)
            {
                UIHelper.DrawBoxLine(terminal, $"  \"I represent {factionName}. We've been watching you, {player.Name2}.\"", "bright_cyan", "bright_yellow");
                UIHelper.DrawBoxLine(terminal, $"  \"Weve noticed what youve been doing. We like it.\"", "bright_cyan", "cyan");
            }
            else
            {
                UIHelper.DrawBoxLine(terminal, $"  \"{factionName} has a job for you, {player.Name2}.\"", "bright_cyan", "bright_yellow");
            }

            UIHelper.DrawBoxLine(terminal, $"  \"{missionDesc}\"", "bright_cyan", "cyan");
            UIHelper.DrawBoxLine(terminal, $"  \"Reward: {goldReward} gold and our gratitude.\"", "bright_cyan", "yellow");
            UIHelper.DrawBoxEmpty(terminal, "bright_cyan");
            UIHelper.DrawBoxSeparator(terminal, "bright_cyan");
            UIHelper.DrawMenuOption(terminal, "A", "Accept the mission", "bright_cyan", "bright_yellow", "bright_green");
            UIHelper.DrawMenuOption(terminal, "D", "Decline respectfully", "bright_cyan", "bright_yellow", "gray");
            UIHelper.DrawBoxBottom(terminal, "bright_cyan");

            var choice = await terminal.GetInput("\n  Your response? ");

            if (choice.ToUpper() == "A")
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"\n  \"Excellent. {factionName} won't forget this.\"");

                // Give half the reward as advance payment
                int advancePayment = goldReward / 2;
                player.Gold += advancePayment;
                terminal.SetColor("yellow");
                terminal.WriteLine($"  You receive {advancePayment} gold as advance payment.");
                terminal.SetColor("white");
                terminal.WriteLine($"  The remaining {goldReward - advancePayment} gold will be paid on completion.");

                // Create a real quest
                var quest = QuestSystem.CreateFactionMission(player, faction, missionTarget, missionDesc, goldReward);
                if (quest != null)
                {
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine($"\n  New quest added: {quest.Title}");
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  Complete the objective and turn in at the Quest Hall for your reward.");
                }

                if (isRecruit)
                {
                    var factionSystem = FactionSystem.Instance;
                    if (factionSystem != null && factionSystem.PlayerFaction == null)
                    {
                        terminal.SetColor("bright_cyan");
                        terminal.WriteLine($"\n  \"You should think about joining {factionName}. We could use you.\"");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  (Visit the faction headquarters to officially join.)");
                    }
                }

                factionNpc.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.Helped,
                    Description = $"{player.Name2} accepted a mission for {factionName}",
                    InvolvedCharacter = player.Name2, Importance = 0.7f, EmotionalImpact = 0.4f
                });

                NewsSystem.Instance?.Newsy($"{player.Name2} undertook a mission for {factionName}.");
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"\n  \"Suit yourself. You know where to find us.\"");
                terminal.SetColor("white");
                terminal.WriteLine($"  {factionNpc.Name2} nods and disappears into the crowd.");
            }

            await terminal.PressAnyKey();
        }

        #endregion

        #region Petition 5: Royal Petition

        private async Task ExecuteRoyalPetition(NPC petitioner, Character player, TerminalEmulator terminal)
        {
            var king = CastleLocation.GetCurrentKing();
            if (king == null) return;

            // Choose petition type based on world state
            int petitionRoll = _random.Next(4);
            string petitionType;
            string petitionText;

            switch (petitionRoll)
            {
                case 0: // Tax relief
                    petitionType = "tax";
                    petitionText = $"Your Majesty, the tax of {king.TaxRate} gold per day is crushing us. My family can barely eat.";
                    break;
                case 1: // Justice
                    var aggressor = NPCSpawnSystem.Instance?.ActiveNPCs?
                        .FirstOrDefault(n => !n.IsDead && n != petitioner &&
                            (n.Memory?.GetCharacterImpression(petitioner.Name2) ?? 0f) < -0.3f);
                    string aggressorName = aggressor?.Name2 ?? "a scoundrel";
                    petitionType = "justice";
                    petitionText = $"Your Majesty, {aggressorName} attacked me and stole my belongings. I demand justice!";
                    break;
                case 2: // Monster threat
                    petitionType = "monster";
                    petitionText = "Your Majesty, creatures from the dungeon have been spotted near the outskirts. We need protection!";
                    break;
                default: // Marriage blessing
                    var partner = NPCSpawnSystem.Instance?.ActiveNPCs?
                        .FirstOrDefault(n => !n.IsDead && !n.Married && n != petitioner &&
                            (n.Memory?.GetCharacterImpression(petitioner.Name2) ?? 0f) > 0.3f);
                    string partnerName = partner?.Name2 ?? "my beloved";
                    petitionType = "marriage";
                    petitionText = $"Your Majesty, {partnerName} and I wish to marry. We seek the King's blessing.";
                    break;
            }

            terminal.ClearScreen();
            UIHelper.DrawBoxTop(terminal, "ROYAL PETITION", "bright_yellow");
            UIHelper.DrawBoxEmpty(terminal, "bright_yellow");
            UIHelper.DrawBoxLine(terminal, $"  {petitioner.Name2} kneels before you.", "bright_yellow", "white");
            UIHelper.DrawBoxEmpty(terminal, "bright_yellow");
            UIHelper.DrawBoxLine(terminal, $"  \"{petitionText}\"", "bright_yellow", "bright_cyan");
            UIHelper.DrawBoxEmpty(terminal, "bright_yellow");
            UIHelper.DrawBoxSeparator(terminal, "bright_yellow");

            switch (petitionType)
            {
                case "tax":
                    long relief = king.TaxRate * 5; // 5 days of tax relief cost
                    UIHelper.DrawMenuOption(terminal, "G", $"Grant relief (-{relief}g from treasury)", "bright_yellow", "bright_cyan", "bright_green");
                    UIHelper.DrawMenuOption(terminal, "D", "Deny the request", "bright_yellow", "bright_cyan", "red");
                    UIHelper.DrawMenuOption(terminal, "H", "Halve their tax temporarily", "bright_yellow", "bright_cyan", "white");
                    break;
                case "justice":
                    UIHelper.DrawMenuOption(terminal, "J", "Order an investigation", "bright_yellow", "bright_cyan", "bright_green");
                    UIHelper.DrawMenuOption(terminal, "D", "Dismiss the complaint", "bright_yellow", "bright_cyan", "red");
                    UIHelper.DrawMenuOption(terminal, "C", "Offer compensation from treasury", "bright_yellow", "bright_cyan", "white");
                    break;
                case "monster":
                    long guardCost = 500;
                    UIHelper.DrawMenuOption(terminal, "S", $"Send guards (-{guardCost}g)", "bright_yellow", "bright_cyan", "bright_green");
                    UIHelper.DrawMenuOption(terminal, "P", "Promise to investigate personally", "bright_yellow", "bright_cyan", "white");
                    UIHelper.DrawMenuOption(terminal, "D", "Dismiss — the walls will hold", "bright_yellow", "bright_cyan", "red");
                    break;
                case "marriage":
                    UIHelper.DrawMenuOption(terminal, "B", "Grant the royal blessing", "bright_yellow", "bright_cyan", "bright_green");
                    UIHelper.DrawMenuOption(terminal, "D", "Deny the blessing", "bright_yellow", "bright_cyan", "red");
                    break;
            }

            UIHelper.DrawBoxBottom(terminal, "bright_yellow");
            var choice = await terminal.GetInput("\n  Your ruling, Majesty? ");

            // Process ruling
            switch (petitionType)
            {
                case "tax":
                    if (choice.ToUpper() == "G")
                    {
                        long cost = king.TaxRate * 5;
                        king.Treasury -= cost;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"\n  You grant tax relief. The people cheer! (-{cost}g from treasury)");
                        player.Chivalry += 5;
                        petitioner.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Saved, Description = $"King {player.Name2} granted me tax relief",
                            InvolvedCharacter = player.Name2, Importance = 0.8f, EmotionalImpact = 0.6f
                        });
                        NewsSystem.Instance?.Newsy($"King {player.Name2} granted tax relief to {petitioner.Name2}. The people approve!");
                    }
                    else if (choice.ToUpper() == "H")
                    {
                        long cost = king.TaxRate * 2;
                        king.Treasury -= cost;
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"\n  A fair compromise. (-{cost}g from treasury)");
                        player.Chivalry += 2;
                        NewsSystem.Instance?.Newsy($"King {player.Name2} offered partial tax relief to {petitioner.Name2}.");
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine($"\n  \"The tax stands. The kingdom needs every coin.\"");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  {petitioner.Name2} leaves, muttering under their breath.");
                        player.Darkness += 3;
                        petitioner.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Insulted, Description = $"King {player.Name2} denied my plea for tax relief",
                            InvolvedCharacter = player.Name2, Importance = 0.6f, EmotionalImpact = -0.4f
                        });
                        NewsSystem.Instance?.Newsy($"King {player.Name2} denied tax relief to {petitioner.Name2}. Grumbling grows.");
                    }
                    break;

                case "justice":
                    if (choice.ToUpper() == "J")
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"\n  \"Justice shall be done. Guards, investigate this matter!\"");
                        player.Chivalry += 5;
                        petitioner.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Defended, Description = $"King {player.Name2} ordered justice for my complaint",
                            InvolvedCharacter = player.Name2, Importance = 0.8f, EmotionalImpact = 0.5f
                        });
                        NewsSystem.Instance?.Newsy($"King {player.Name2} ordered an investigation on behalf of {petitioner.Name2}.");
                    }
                    else if (choice.ToUpper() == "C")
                    {
                        long comp = 200 + petitioner.Level * 20;
                        king.Treasury -= comp;
                        player.Gold -= Math.Min(comp / 2, player.Gold);
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"\n  You offer {comp}g from the treasury as compensation.");
                        petitioner.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Helped, Description = $"King {player.Name2} compensated me for my losses",
                            InvolvedCharacter = player.Name2, Importance = 0.6f, EmotionalImpact = 0.3f
                        });
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine($"\n  \"I cannot act on hearsay alone. Bring evidence.\"");
                        petitioner.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Insulted, Description = $"King {player.Name2} dismissed my complaint",
                            InvolvedCharacter = player.Name2, Importance = 0.5f, EmotionalImpact = -0.3f
                        });
                    }
                    break;

                case "monster":
                    if (choice.ToUpper() == "S")
                    {
                        king.Treasury -= 500;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"\n  You dispatch guards to secure the outskirts. (-500g from treasury)");
                        player.Chivalry += 5;
                        petitioner.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Saved, Description = $"King {player.Name2} sent guards to protect us",
                            InvolvedCharacter = player.Name2, Importance = 0.8f, EmotionalImpact = 0.5f
                        });
                        NewsSystem.Instance?.Newsy($"King {player.Name2} deployed guards to protect citizens from dungeon creatures.");
                    }
                    else if (choice.ToUpper() == "P")
                    {
                        terminal.SetColor("bright_cyan");
                        terminal.WriteLine($"\n  \"I will look into this personally. The realm is under my protection.\"");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  The people are inspired by your courage!");
                        player.Chivalry += 8;
                        petitioner.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Defended, Description = $"King {player.Name2} promised to personally protect us",
                            InvolvedCharacter = player.Name2, Importance = 0.9f, EmotionalImpact = 0.6f
                        });
                        NewsSystem.Instance?.Newsy($"King {player.Name2} vows to personally deal with the dungeon threat!");
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine($"\n  \"The walls of the city are strong enough.\"");
                        player.Darkness += 3;
                        petitioner.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Abandoned, Description = $"King {player.Name2} ignored our plea for protection",
                            InvolvedCharacter = player.Name2, Importance = 0.7f, EmotionalImpact = -0.5f
                        });
                        NewsSystem.Instance?.Newsy($"King {player.Name2} dismissed reports of dungeon creatures. Citizens are worried.");
                    }
                    break;

                case "marriage":
                    if (choice.ToUpper() == "B")
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"\n  \"I grant the royal blessing upon this union! May it be long and prosperous.\"");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  {petitioner.Name2} beams with joy.");
                        player.Chivalry += 3;
                        petitioner.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Helped, Description = $"King {player.Name2} blessed my marriage",
                            InvolvedCharacter = player.Name2, Importance = 0.8f, EmotionalImpact = 0.6f
                        });
                        NewsSystem.Instance?.Newsy($"King {player.Name2} blessed the marriage of {petitioner.Name2}. Celebrations ensued!");
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine($"\n  \"No. The crown says no.\"");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  {petitioner.Name2} looks crushed.");
                        player.Darkness += 3;
                        petitioner.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Insulted, Description = $"King {player.Name2} denied my marriage blessing",
                            InvolvedCharacter = player.Name2, Importance = 0.7f, EmotionalImpact = -0.5f
                        });
                        NewsSystem.Instance?.Newsy($"King {player.Name2} denied a marriage blessing to {petitioner.Name2}. People whisper of tyranny.");
                    }
                    break;
            }

            await terminal.PressAnyKey();
        }

        #endregion

        #region Petition 6: Dying Wish

        private async Task ExecuteDyingWish(NPC elder, Character player, TerminalEmulator terminal)
        {
            var race = elder.Race;
            int maxAge = GameConfig.RaceLifespan.GetValueOrDefault(race, 75);
            int yearsLeft = maxAge - elder.Age;

            // Pick a dying wish type
            int wishRoll = _random.Next(4);

            terminal.ClearScreen();
            UIHelper.DrawBoxTop(terminal, "A FINAL REQUEST", "magenta");
            UIHelper.DrawBoxEmpty(terminal, "magenta");
            UIHelper.DrawBoxLine(terminal, $"  {elder.Name2}, aged {elder.Age}, approaches slowly.", "magenta", "white");
            UIHelper.DrawBoxLine(terminal, $"  They look old. Really old. And tired.", "magenta", "gray");
            UIHelper.DrawBoxEmpty(terminal, "magenta");

            switch (wishRoll)
            {
                case 0: // Legacy — deliver message
                    var recipient = NPCSpawnSystem.Instance?.ActiveNPCs?
                        .FirstOrDefault(n => !n.IsDead && n != elder && n.Name2 != player.Name2);
                    string recipientName = recipient?.Name2 ?? "someone special";

                    UIHelper.DrawBoxLine(terminal, $"  \"Im not gonna be around much longer, {player.Name2}.\"", "magenta", "bright_cyan");
                    UIHelper.DrawBoxLine(terminal, $"  \"When Im gone, tell {recipientName} I forgave them.\"", "magenta", "cyan");
                    UIHelper.DrawBoxLine(terminal, $"  \"Theyll know what its about.\"", "magenta", "cyan");
                    UIHelper.DrawBoxEmpty(terminal, "magenta");
                    UIHelper.DrawBoxSeparator(terminal, "magenta");
                    UIHelper.DrawMenuOption(terminal, "P", "Promise to deliver the message", "magenta", "bright_yellow", "bright_green");
                    UIHelper.DrawMenuOption(terminal, "D", "Decline gently", "magenta", "bright_yellow", "gray");
                    UIHelper.DrawBoxBottom(terminal, "magenta");

                    var legacyChoice = await terminal.GetInput("\n  Your answer? ");
                    if (legacyChoice.ToUpper() == "P")
                    {
                        long inheritance = 500 + elder.Level * 50;
                        player.Gold += inheritance;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"\n  \"Thank you, {player.Name2}. You've given an old {race} peace.\"");
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"  {elder.Name2} presses {inheritance} gold into your hands.");
                        terminal.SetColor("gray");
                        terminal.WriteLine($"  \"My savings. Take em. I wont need em where Im going.\"");

                        elder.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Saved,
                            Description = $"{player.Name2} promised to carry my final message",
                            InvolvedCharacter = player.Name2, Importance = 1.0f, EmotionalImpact = 0.8f
                        });

                        if (recipient != null)
                        {
                            recipient.Memory?.RecordEvent(new MemoryEvent
                            {
                                Type = MemoryType.Helped,
                                Description = $"{player.Name2} delivered a message of forgiveness from {elder.Name2}",
                                InvolvedCharacter = player.Name2, Importance = 0.7f, EmotionalImpact = 0.4f
                            });
                        }

                        player.Chivalry += 10;
                        NewsSystem.Instance?.Newsy($"{player.Name2} honored the dying wish of {elder.Name2}.");
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine($"\n  {elder.Name2} nods slowly. \"Fair enough. Cant blame you.\"");
                    }
                    break;

                case 1: // Confession
                    UIHelper.DrawBoxLine(terminal, $"  \"Before I go, I must confess something.\"", "magenta", "bright_cyan");
                    UIHelper.DrawBoxLine(terminal, $"  \"I've kept a secret for years...\"", "magenta", "cyan");

                    // Generate a random confession
                    int confessionType = _random.Next(3);
                    string confession = confessionType switch
                    {
                        0 => $"\"I buried a stash of gold beneath the old oak near the inn. Take it.\"",
                        1 => $"\"I had an affair years ago. The child... they never knew who their real parent was.\"",
                        _ => $"\"I overheard something in the castle. There are those who plot against the throne.\""
                    };

                    UIHelper.DrawBoxLine(terminal, $"  {confession}", "magenta", "bright_yellow");
                    UIHelper.DrawBoxEmpty(terminal, "magenta");
                    UIHelper.DrawBoxSeparator(terminal, "magenta");
                    UIHelper.DrawMenuOption(terminal, "L", "Listen carefully", "magenta", "bright_yellow", "bright_green");
                    UIHelper.DrawMenuOption(terminal, "D", "\"Take your secrets to the grave.\"", "magenta", "bright_yellow", "gray");
                    UIHelper.DrawBoxBottom(terminal, "magenta");

                    var confChoice = await terminal.GetInput("\n  Your response? ");
                    if (confChoice.ToUpper() == "L")
                    {
                        if (confessionType == 0)
                        {
                            long stash = 1000 + elder.Level * 100;
                            player.Gold += stash;
                            terminal.SetColor("yellow");
                            terminal.WriteLine($"\n  Following the elder's directions, you find {stash} gold buried in a cache!");
                        }
                        else
                        {
                            terminal.SetColor("bright_cyan");
                            terminal.WriteLine($"\n  You listen intently, committing every detail to memory.");
                            terminal.SetColor("white");
                            terminal.WriteLine($"  This information could be valuable...");
                            player.Experience += 100 + elder.Level * 10;
                        }

                        elder.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Helped,
                            Description = $"{player.Name2} listened to my final confession",
                            InvolvedCharacter = player.Name2, Importance = 0.9f, EmotionalImpact = 0.5f
                        });
                        NewsSystem.Instance?.Newsy($"The elderly {elder.Name2} shared a deathbed confession with {player.Name2}.");
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine($"\n  {elder.Name2} sighs. \"Perhaps that's for the best.\"");
                    }
                    break;

                default: // Protection request
                    string protectedName = "";
                    string protectedType = "";
                    var elderSpouse = NPCSpawnSystem.Instance?.GetNPCByName(RelationshipSystem.GetSpouseName(elder));
                    var elderChildren = FamilySystem.Instance?.GetChildrenOf(elder);

                    if (elderSpouse != null && !elderSpouse.IsDead)
                    {
                        protectedName = elderSpouse.Name2;
                        protectedType = "spouse";
                    }
                    else if (elderChildren != null && elderChildren.Count > 0)
                    {
                        protectedName = elderChildren[0].Name;
                        protectedType = "child";
                    }
                    else
                    {
                        protectedName = "my friends";
                        protectedType = "friends";
                    }

                    UIHelper.DrawBoxLine(terminal, $"  \"When I'm gone, please watch over {protectedName} for me.\"", "magenta", "bright_cyan");
                    UIHelper.DrawBoxLine(terminal, $"  \"I dont want them to be alone.\"", "magenta", "cyan");
                    UIHelper.DrawBoxEmpty(terminal, "magenta");
                    UIHelper.DrawBoxSeparator(terminal, "magenta");
                    UIHelper.DrawMenuOption(terminal, "P", $"\"I'll watch over {protectedName}. You have my word.\"", "magenta", "bright_yellow", "bright_green");
                    UIHelper.DrawMenuOption(terminal, "D", "\"I can't make that promise.\"", "magenta", "bright_yellow", "gray");
                    UIHelper.DrawBoxBottom(terminal, "magenta");

                    var protChoice = await terminal.GetInput("\n  Your answer? ");
                    if (protChoice.ToUpper() == "P")
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"\n  {elder.Name2}'s face lights up with gratitude.");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  \"Thank you. Knowing {protectedName} won't be alone... that's all I needed.\"");

                        // Boost impression with protected person
                        if (protectedType == "spouse" && elderSpouse != null)
                        {
                            elderSpouse.Memory?.RecordEvent(new MemoryEvent
                            {
                                Type = MemoryType.Helped,
                                Description = $"{player.Name2} promised {elder.Name2} to watch over me",
                                InvolvedCharacter = player.Name2, Importance = 0.8f, EmotionalImpact = 0.5f
                            });
                        }

                        long gift = 300 + elder.Level * 30;
                        player.Gold += gift;
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"  {elder.Name2} gives you {gift} gold. \"For your trouble.\"");

                        player.Chivalry += 8;
                        elder.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Saved,
                            Description = $"{player.Name2} promised to protect my loved ones",
                            InvolvedCharacter = player.Name2, Importance = 1.0f, EmotionalImpact = 0.8f
                        });
                        NewsSystem.Instance?.Newsy($"{player.Name2} vowed to protect the family of the aging {elder.Name2}.");
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine($"\n  \"Yeah. I figured youd say that.\"");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  {elder.Name2} shuffles away, looking smaller than before.");
                    }
                    break;
            }

            await terminal.PressAnyKey();
        }

        #endregion

        #region Petition 7: Missing Person

        private async Task ExecuteMissingPerson(NPC petitioner, Character player, TerminalEmulator terminal)
        {
            // Find who's missing
            string missingName = "";
            string relationship = "";
            bool isDead = false;

            string spouseName = RelationshipSystem.GetSpouseName(petitioner);
            if (!string.IsNullOrEmpty(spouseName))
            {
                var spouse = NPCSpawnSystem.Instance?.GetNPCByName(spouseName, includeDead: true);
                if (spouse != null && spouse.IsDead && !spouse.IsAgedDeath)
                {
                    missingName = spouseName;
                    relationship = "spouse";
                    isDead = true;
                }
            }

            if (string.IsNullOrEmpty(missingName))
            {
                // Check friends
                var friendImpressions = petitioner.Brain?.Memory?.CharacterImpressions;
                if (friendImpressions != null)
                {
                    foreach (var kvp in friendImpressions.Where(k => k.Value >= 0.5f))
                    {
                        var friend = NPCSpawnSystem.Instance?.GetNPCByName(kvp.Key, includeDead: true);
                        if (friend != null && friend.IsDead && !friend.IsAgedDeath)
                        {
                            missingName = kvp.Key;
                            relationship = "dear friend";
                            isDead = true;
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(missingName)) return;

            terminal.ClearScreen();
            UIHelper.DrawBoxTop(terminal, "MISSING PERSON", "bright_red");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            UIHelper.DrawBoxLine(terminal, $"  {petitioner.Name2} grabs your arm, eyes wide with worry.", "bright_red", "white");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            UIHelper.DrawBoxLine(terminal, $"  \"Please, {player.Name2}! Have you seen {missingName}?\"", "bright_red", "bright_cyan");
            UIHelper.DrawBoxLine(terminal, $"  \"My {relationship}... they've been gone for days. No one's seen them.\"", "bright_red", "cyan");
            UIHelper.DrawBoxLine(terminal, $"  \"I'm terrified something terrible has happened.\"", "bright_red", "cyan");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            UIHelper.DrawBoxSeparator(terminal, "bright_red");

            if (isDead)
            {
                UIHelper.DrawMenuOption(terminal, "T", "Break the news gently", "bright_red", "bright_yellow", "white");
                UIHelper.DrawMenuOption(terminal, "B", "Break the news bluntly", "bright_red", "bright_yellow", "gray");
                UIHelper.DrawMenuOption(terminal, "L", "Lie — \"I'm sure they're fine\"", "bright_red", "bright_yellow", "red");
                UIHelper.DrawMenuOption(terminal, "I", "\"I'll investigate what happened\"", "bright_red", "bright_yellow", "bright_green");
            }
            else
            {
                UIHelper.DrawMenuOption(terminal, "H", "Help search for them", "bright_red", "bright_yellow", "bright_green");
                UIHelper.DrawMenuOption(terminal, "R", "\"I haven't seen them, sorry\"", "bright_red", "bright_yellow", "gray");
            }

            UIHelper.DrawBoxBottom(terminal, "bright_red");
            var choice = await terminal.GetInput("\n  What do you do? ");

            if (isDead)
            {
                switch (choice.ToUpper())
                {
                    case "T": // Gentle truth
                        terminal.SetColor("white");
                        terminal.WriteLine($"\n  You take {petitioner.Name2}'s hands gently.");
                        terminal.WriteLine($"  \"I'm so sorry. {missingName}... they didn't make it.\"");
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"  {petitioner.Name2} collapses into tears. But they thank you for your honesty.");

                        petitioner.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Helped,
                            Description = $"{player.Name2} gently told me the truth about {missingName}'s death",
                            InvolvedCharacter = player.Name2, Importance = 0.9f, EmotionalImpact = 0.3f
                        });

                        player.Chivalry += 5;
                        NewsSystem.Instance?.Newsy($"{player.Name2} compassionately broke the news of {missingName}'s death to {petitioner.Name2}.");
                        break;

                    case "B": // Blunt truth
                        terminal.SetColor("white");
                        terminal.WriteLine($"\n  \"{missingName} is dead. I'm sorry, but there's no easy way to say it.\"");
                        terminal.SetColor("gray");
                        terminal.WriteLine($"  {petitioner.Name2} staggers back, face white with shock.");

                        petitioner.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.SocialInteraction,
                            Description = $"{player.Name2} told me bluntly about {missingName}'s death",
                            InvolvedCharacter = player.Name2, Importance = 0.7f, EmotionalImpact = -0.1f
                        });
                        break;

                    case "L": // Lie
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"\n  \"I saw {missingName} just the other day. They seemed fine.\"");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  {petitioner.Name2} looks relieved. \"Oh thank goodness! Maybe they just needed space.\"");
                        terminal.SetColor("gray");
                        terminal.WriteLine($"  The truth will come out eventually...");

                        player.Darkness += 5;
                        petitioner.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.SocialInteraction,
                            Description = $"{player.Name2} told me {missingName} was fine (was it true?)",
                            InvolvedCharacter = player.Name2, Importance = 0.5f, EmotionalImpact = 0.2f
                        });
                        break;

                    case "I": // Investigate
                        // Check if player already has max quests
                        if (player is Player investigator && investigator.ActiveQuests.Count >= GameConfig.MaxActiveQuests)
                        {
                            terminal.SetColor("red");
                            terminal.WriteLine($"\n  You already have too many active quests to take this on.");
                            terminal.SetColor("gray");
                            terminal.WriteLine($"  Complete or abandon a quest first.");
                            break;
                        }

                        terminal.SetColor("bright_cyan");
                        terminal.WriteLine($"\n  \"I'll find out what happened to {missingName}. I promise.\"");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  {petitioner.Name2} looks desperate. \"Please, {player.Name2}. Just find out what happened.\"");

                        // Small gold advance for expenses (real reward comes from quest completion)
                        long expenseGold = 50 + player.Level * 10;
                        player.Gold += expenseGold;
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"\n  {petitioner.Name2} presses {expenseGold} gold into your hands for expenses.");

                        // Create investigation quest — reach the dungeon floor where they went missing, kill monsters there
                        int investigateFloor = Math.Max(1, Math.Min(player.Level + 5, 100));
                        int monstersToKill = 3 + player.Level / 10; // 3-13 monsters

                        var investigateQuest = new Quest
                        {
                            Title = $"Investigate {missingName}'s Disappearance",
                            Initiator = petitioner.Name2,
                            QuestType = QuestType.SingleQuest,
                            QuestTarget = QuestTarget.RescueNPC,
                            Difficulty = (byte)Math.Min(4, Math.Max(1, player.Level / 15 + 1)),
                            Comment = $"{petitioner.Name2} begged you to find out what happened to {missingName}.",
                            Date = DateTime.Now,
                            MinLevel = 1,
                            MaxLevel = 9999,
                            DaysToComplete = 14,
                            Reward = 3,
                            RewardType = QuestRewardType.Money,
                            TargetNPCName = missingName,
                            Occupier = player.Name2,
                            OccupierRace = player.Race,
                            OccupierSex = (byte)((int)player.Sex),
                            OccupiedDays = 0,
                            OfferedTo = player.Name2
                        };

                        investigateQuest.Objectives.Add(new QuestObjective(
                            QuestObjectiveType.TalkToNPC,
                            $"Find {missingName} or learn their fate",
                            1,
                            missingName,
                            missingName
                        ));

                        investigateQuest.Objectives.Add(new QuestObjective(
                            QuestObjectiveType.ReachDungeonFloor,
                            $"Search dungeon floor {investigateFloor} where {missingName} was last seen",
                            investigateFloor,
                            "",
                            $"Floor {investigateFloor}"
                        ) { IsOptional = true });

                        investigateQuest.Objectives.Add(new QuestObjective(
                            QuestObjectiveType.KillMonsters,
                            $"Slay {monstersToKill} monsters to uncover what happened",
                            monstersToKill,
                            "",
                            "Monsters"
                        ) { IsOptional = true });

                        QuestSystem.AddQuestToDatabase(investigateQuest);
                        if (player is Player p)
                        {
                            p.ActiveQuests.Add(investigateQuest);
                        }

                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"\n  New Quest: {investigateQuest.Title}");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  — Find {missingName} (talk to them if alive)");
                        terminal.WriteLine($"  — OR search dungeon floor {investigateFloor} and slay {monstersToKill} monsters");

                        petitioner.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Saved,
                            Description = $"{player.Name2} promised to investigate {missingName}'s disappearance",
                            InvolvedCharacter = player.Name2, Importance = 0.9f, EmotionalImpact = 0.5f
                        });

                        player.Chivalry += 8;
                        NewsSystem.Instance?.Newsy($"{player.Name2} is investigating the disappearance of {missingName} for {petitioner.Name2}.");
                        break;

                    default:
                        terminal.SetColor("gray");
                        terminal.WriteLine($"\n  \"I haven't seen them. I'm sorry.\"");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  {petitioner.Name2} nods numbly and continues searching.");
                        break;
                }
            }
            else
            {
                if (choice.ToUpper() == "H")
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"\n  \"I'll help you look. Let's find {missingName}.\"");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {petitioner.Name2} cant even speak. Just nods.");

                    petitioner.Memory?.RecordEvent(new MemoryEvent
                    {
                        Type = MemoryType.Helped,
                        Description = $"{player.Name2} helped search for {missingName}",
                        InvolvedCharacter = player.Name2, Importance = 0.7f, EmotionalImpact = 0.4f
                    });

                    player.Chivalry += 3;
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"\n  \"I'm sorry, I haven't seen them.\"");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {petitioner.Name2} nods sadly and moves on.");
                }
            }

            await terminal.PressAnyKey();
        }

        #endregion

        #region Petition 8: Rivalry Report

        private async Task ExecuteRivalryReport(NPC warner, Character player, TerminalEmulator terminal)
        {
            terminal.ClearScreen();
            UIHelper.DrawBoxTop(terminal, "A FRIENDLY WARNING", "bright_cyan");
            UIHelper.DrawBoxEmpty(terminal, "bright_cyan");
            UIHelper.DrawBoxLine(terminal, $"  {warner.Name2} pulls you into a quiet corner.", "bright_cyan", "white");
            UIHelper.DrawBoxEmpty(terminal, "bright_cyan");
            UIHelper.DrawBoxLine(terminal, $"  \"{player.Name2}, I like you. So I'm telling you this as a friend.\"", "bright_cyan", "bright_yellow");

            // Determine what to warn about
            bool isCourtPlot = false;
            string warningText;
            string threatDetail;

            if (player.King)
            {
                var king = CastleLocation.GetCurrentKing();
                if (king?.ActivePlots?.Count > 0)
                {
                    isCourtPlot = true;
                    var plot = king.ActivePlots[0];
                    warningText = "\"There are whispers in the court. Someone is plotting against you.\"";
                    threatDetail = $"Plot type: {plot.PlotType}, Progress: {plot.Progress}%";
                }
                else
                {
                    warningText = "\"I've heard rumblings. Some NPCs are unhappy with your reign.\"";
                    threatDetail = "General dissent detected among court members.";
                }
            }
            else
            {
                // Find threatening NPC team
                var npcs = NPCSpawnSystem.Instance?.ActiveNPCs ?? new List<NPC>();
                var rivalTeam = npcs.Where(n => !n.IsDead && !string.IsNullOrEmpty(n.Team) &&
                    Math.Abs(n.Level - player.Level) <= 15 && n.Level >= 10)
                    .GroupBy(n => n.Team)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();

                if (rivalTeam != null)
                {
                    string teamName = rivalTeam.Key;
                    int teamSize = rivalTeam.Count();
                    int maxLevel = rivalTeam.Max(n => n.Level);
                    warningText = $"\"A group called '{teamName}' has been growing in power. {teamSize} members, led by someone level {maxLevel}.\"";
                    threatDetail = $"Team '{teamName}': {teamSize} members, highest level {maxLevel}";
                }
                else
                {
                    warningText = "\"Watch your back. Not everyone in this town is friendly.\"";
                    threatDetail = "General warning about hostile NPCs.";
                }
            }

            UIHelper.DrawBoxLine(terminal, $"  {warningText}", "bright_cyan", "cyan");
            UIHelper.DrawBoxEmpty(terminal, "bright_cyan");
            UIHelper.DrawBoxSeparator(terminal, "bright_cyan");
            UIHelper.DrawMenuOption(terminal, "T", "\"Tell me everything you know.\"", "bright_cyan", "bright_yellow", "bright_green");
            UIHelper.DrawMenuOption(terminal, "A", "\"I'll handle it. Thank you.\"", "bright_cyan", "bright_yellow", "white");
            UIHelper.DrawMenuOption(terminal, "D", "\"I'm not worried.\"", "bright_cyan", "bright_yellow", "gray");
            UIHelper.DrawBoxBottom(terminal, "bright_cyan");

            var choice = await terminal.GetInput("\n  Your response? ");

            switch (choice.ToUpper())
            {
                case "T": // Get details
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine($"\n  {warner.Name2} leans in close and shares everything they know.");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  Intel: {threatDetail}");

                    if (isCourtPlot)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"\n  \"Check the Court Politics menu in the Castle for more details.\"");
                    }

                    player.Experience += 50 + player.Level * 5;
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"\n  You learned something useful. (+{50 + player.Level * 5} XP)");

                    warner.Memory?.RecordEvent(new MemoryEvent
                    {
                        Type = MemoryType.Helped,
                        Description = $"Warned {player.Name2} about threats",
                        InvolvedCharacter = player.Name2, Importance = 0.7f, EmotionalImpact = 0.3f
                    });

                    NewsSystem.Instance?.Newsy($"{warner.Name2} shared intelligence with {player.Name2} about growing threats.");
                    break;

                case "A": // Acknowledge
                    terminal.SetColor("white");
                    terminal.WriteLine($"\n  \"Good. Be careful out there, {player.Name2}.\"");
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  {warner.Name2} nods and slips back into the crowd.");

                    warner.Memory?.RecordEvent(new MemoryEvent
                    {
                        Type = MemoryType.SocialInteraction,
                        Description = $"Warned {player.Name2} about threats, they acknowledged",
                        InvolvedCharacter = player.Name2, Importance = 0.5f, EmotionalImpact = 0.2f
                    });
                    break;

                default: // Dismiss
                    terminal.SetColor("gray");
                    terminal.WriteLine($"\n  {warner.Name2} shrugs. \"Suit yourself. Don't say I didn't warn you.\"");
                    break;
            }

            await terminal.PressAnyKey();
        }

        #endregion
    }
}
