using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
                TryRoyalPetition,
                TryDyingWish,
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
                case PetitionType.RoyalPetition:
                    await ExecuteRoyalPetition(npc, player, terminal);
                    break;
                case PetitionType.DyingWish:
                    await ExecuteDyingWish(npc, player, terminal);
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
                ? Loc.Get("petition.betrayal.trouble_affair", spouseName)
                : Loc.Get("petition.betrayal.trouble_cold", spouseName);

            terminal.ClearScreen();
            UIHelper.DrawBoxTop(terminal, Loc.Get("petition.betrayal.header"), "bright_magenta");
            UIHelper.DrawBoxEmpty(terminal, "bright_magenta");
            UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.betrayal.approaches", petitioner.Name2)}", "bright_magenta", "white");
            UIHelper.DrawBoxEmpty(terminal, "bright_magenta");
            UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.betrayal.need_help", player.Name2)}", "bright_magenta", "bright_cyan");
            UIHelper.DrawBoxLine(terminal, $"  {troubleDescription}", "bright_magenta", "cyan");
            UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.betrayal.only_trust")}", "bright_magenta", "cyan");
            UIHelper.DrawBoxEmpty(terminal, "bright_magenta");
            UIHelper.DrawBoxSeparator(terminal, "bright_magenta");
            UIHelper.DrawMenuOption(terminal, "C", Loc.Get("petition.betrayal.option_counsel"), "bright_magenta", "bright_yellow", "white");
            UIHelper.DrawMenuOption(terminal, "F", Loc.Get("petition.betrayal.option_confront", spouseName), "bright_magenta", "bright_yellow", "white");
            UIHelper.DrawMenuOption(terminal, "E", Loc.Get("petition.betrayal.option_exploit"), "bright_magenta", "bright_yellow", "red");
            UIHelper.DrawMenuOption(terminal, "R", Loc.Get("petition.betrayal.option_refuse"), "bright_magenta", "bright_yellow", "gray");
            UIHelper.DrawBoxBottom(terminal, "bright_magenta");

            var choice = await terminal.GetInput($"\n  {Loc.Get("petition.betrayal.prompt")}");

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
                    terminal.WriteLine($"\n  {Loc.Get("petition.betrayal.refuse_line")}");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {Loc.Get("petition.betrayal.refuse_walks_away", petitioner.Name2)}");
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
                terminal.WriteLine($"\n  {Loc.Get("petition.counsel.success_thinks", petitioner.Name2)}");
                terminal.WriteLine($"  {Loc.Get("petition.counsel.success_talk", spouse.Name2)}");
                terminal.SetColor("white");
                terminal.WriteLine($"  {Loc.Get("petition.counsel.success_looks_better", petitioner.Name2)}");

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
                terminal.WriteLine($"\n  {Loc.Get("petition.counsel.fail_flat", petitioner.Name2)}");
                terminal.SetColor("white");
                terminal.WriteLine($"  {Loc.Get("petition.counsel.fail_not_helpful")}");

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
            terminal.WriteLine($"\n  {Loc.Get("petition.confront.find_confront", spouse.Name2)}");

            if (spouseHasAffair)
                terminal.WriteLine($"  {Loc.Get("petition.confront.affair_accusation", petitioner.Name2)}");
            else
                terminal.WriteLine($"  {Loc.Get("petition.confront.changed_accusation", petitioner.Name2)}");

            // CHA check for peaceful resolution
            int peaceChance = Math.Min(70, 25 + (int)(player.Charisma * 2));
            bool peaceful = _random.Next(100) < peaceChance;

            if (peaceful)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"\n  {Loc.Get("petition.confront.peaceful_taken_aback", spouse.Name2)}");
                terminal.WriteLine($"  {Loc.Get("petition.confront.peaceful_right")}");

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
                terminal.WriteLine($"\n  {Loc.Get("petition.confront.hostile_narrow", spouse.Name2)}");
                terminal.WriteLine($"  {Loc.Get("petition.confront.hostile_shoves", spouse.Name2)}");

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
            terminal.WriteLine($"\n  {Loc.Get("petition.exploit.opportunity", petitioner.Name2)}");
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("petition.exploit.forget", spouse.Name2)}");
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {Loc.Get("petition.exploit.someone_like_me")}");

            // CHA check for seduction
            int seduceChance = Math.Min(70, 20 + (int)(player.Charisma * 2));
            bool success = _random.Next(100) < seduceChance;

            if (success)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine($"\n  {Loc.Get("petition.exploit.success_eyes_widen", petitioner.Name2)}");
                terminal.WriteLine($"  {Loc.Get("petition.exploit.success_maybe_right")}");

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
                terminal.WriteLine($"\n  {Loc.Get("petition.exploit.fail_recoils", petitioner.Name2)}");
                terminal.WriteLine($"  {Loc.Get("petition.exploit.fail_that_what")}");
                terminal.SetColor("white");
                terminal.WriteLine($"  {Loc.Get("petition.exploit.fail_storms_off", petitioner.Name2)}");

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
            UIHelper.DrawBoxTop(terminal, Loc.Get("petition.romance.header"), "bright_magenta");
            UIHelper.DrawBoxEmpty(terminal, "bright_magenta");
            UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.romance.pulls_aside", suitor.Name2)}", "bright_magenta", "white");
            UIHelper.DrawBoxEmpty(terminal, "bright_magenta");
            UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.romance.confidence", player.Name2)}", "bright_magenta", "bright_cyan");
            UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.romance.feelings", crushName)}", "bright_magenta", "cyan");
            UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.romance.good_word")}", "bright_magenta", "cyan");
            UIHelper.DrawBoxEmpty(terminal, "bright_magenta");
            UIHelper.DrawBoxSeparator(terminal, "bright_magenta");
            UIHelper.DrawMenuOption(terminal, "W", Loc.Get("petition.romance.option_wingman", suitor.Name2), "bright_magenta", "bright_yellow", "white");
            UIHelper.DrawMenuOption(terminal, "S", Loc.Get("petition.romance.option_sabotage", crushName), "bright_magenta", "bright_yellow", "red");
            UIHelper.DrawMenuOption(terminal, "H", Loc.Get("petition.romance.option_honest"), "bright_magenta", "bright_yellow", "white");
            UIHelper.DrawMenuOption(terminal, "R", Loc.Get("petition.romance.option_refuse"), "bright_magenta", "bright_yellow", "gray");
            UIHelper.DrawBoxBottom(terminal, "bright_magenta");

            var choice = await terminal.GetInput($"\n  {Loc.Get("petition.romance.prompt")}");
            var crushNpc = NPCSpawnSystem.Instance?.GetNPCByName(crushName);

            switch (choice.ToUpper())
            {
                case "W": // Wingman
                    int wingmanChance = Math.Min(75, 35 + (int)(player.Charisma * 2));
                    bool wingmanSuccess = _random.Next(100) < wingmanChance;

                    if (wingmanSuccess && crushNpc != null)
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"\n  {Loc.Get("petition.wingman.success_speak", crushName, suitor.Name2)}");
                        terminal.WriteLine($"  {Loc.Get("petition.wingman.success_noticed", suitor.Name2)}");

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
                        terminal.WriteLine($"\n  {Loc.Get("petition.wingman.success_overjoyed", suitor.Name2, reward)}");

                        player.Chivalry += 3;
                        NewsSystem.Instance?.Newsy($"{player.Name2} played matchmaker for {suitor.Name2} and {crushName}.");
                    }
                    else
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"\n  {Loc.Get("petition.wingman.fail_not_interested", suitor.Name2, crushName)}");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  {Loc.Get("petition.wingman.fail_not_type")}");

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
                        terminal.WriteLine($"\n  {Loc.Get("petition.sabotage.tell_trouble", crushName, suitor.Name2)}");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  {Loc.Get("petition.sabotage.thanks_warning")}");

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
                            terminal.WriteLine($"\n  {Loc.Get("petition.sabotage.discovered", suitor.Name2)}");
                            terminal.WriteLine($"  {Loc.Get("petition.sabotage.discovered_reaction")}");

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
                    terminal.WriteLine($"\n  {Loc.Get("petition.honest.advice", crushName)}");
                    terminal.WriteLine($"  {Loc.Get("petition.honest.courage")}");
                    terminal.SetColor("white");
                    terminal.WriteLine($"\n  {Loc.Get("petition.honest.thanks", suitor.Name2)}");

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
                    terminal.WriteLine($"\n  {Loc.Get("petition.romance.refuse_line")}");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {Loc.Get("petition.romance.refuse_disappointed", suitor.Name2)}");
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
                exName = Loc.Get("petition.custody.their_former_spouse");
            }

            var children = FamilySystem.Instance?.GetChildrenOf(petitioner) ?? new List<Child>();
            int childCount = children.Count;

            terminal.ClearScreen();
            UIHelper.DrawBoxTop(terminal, Loc.Get("petition.custody.header"), "bright_yellow");
            UIHelper.DrawBoxEmpty(terminal, "bright_yellow");
            UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.custody.approaches", petitioner.Name2)}", "bright_yellow", "white");
            UIHelper.DrawBoxEmpty(terminal, "bright_yellow");
            UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.custody.need_authority", player.Name2)}", "bright_yellow", "bright_cyan");
            UIHelper.DrawBoxLine(terminal, $"  {Loc.Get(childCount == 1 ? "petition.custody.wont_let_see_child" : "petition.custody.wont_let_see_children", exName)}", "bright_yellow", "cyan");
            UIHelper.DrawBoxLine(terminal, $"  {Loc.Get(childCount == 1 ? "petition.custody.want_part_life" : "petition.custody.want_part_lives")}", "bright_yellow", "cyan");
            UIHelper.DrawBoxEmpty(terminal, "bright_yellow");
            UIHelper.DrawBoxSeparator(terminal, "bright_yellow");
            UIHelper.DrawMenuOption(terminal, "M", Loc.Get("petition.custody.option_mediate"), "bright_yellow", "bright_cyan", "white");
            UIHelper.DrawMenuOption(terminal, "P", Loc.Get("petition.custody.option_side_petitioner", petitioner.Name2), "bright_yellow", "bright_cyan", "white");
            if (exSpouse != null)
                UIHelper.DrawMenuOption(terminal, "X", Loc.Get("petition.custody.option_side_ex", exName), "bright_yellow", "bright_cyan", "white");
            UIHelper.DrawMenuOption(terminal, "I", Loc.Get("petition.custody.option_stay_out"), "bright_yellow", "bright_cyan", "gray");
            UIHelper.DrawBoxBottom(terminal, "bright_yellow");

            var choice = await terminal.GetInput($"\n  {Loc.Get("petition.custody.prompt")}");

            switch (choice.ToUpper())
            {
                case "M": // Mediate
                    int mediateChance = Math.Min(70, 30 + (int)(player.Charisma * 2));
                    bool mediateSuccess = _random.Next(100) < mediateChance;

                    if (mediateSuccess)
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"\n  {Loc.Get("petition.mediate.success_arrangement")}");
                        terminal.WriteLine($"  {Loc.Get(childCount == 1 ? "petition.mediate.success_shared_child" : "petition.mediate.success_shared_children")}");

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
                        terminal.WriteLine($"\n  {Loc.Get("petition.mediate.success_thanks", reward)}");
                        NewsSystem.Instance?.Newsy($"{player.Name2} successfully mediated a custody dispute between {petitioner.Name2} and {exName}.");
                    }
                    else
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"\n  {Loc.Get("petition.mediate.fail_refuse")}");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  {Loc.Get("petition.mediate.fail_shout")}");

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
                    terminal.WriteLine($"\n  {Loc.Get("petition.side.advocate", petitioner.Name2)}");

                    petitioner.Memory?.RecordEvent(new MemoryEvent
                    {
                        Type = MemoryType.Defended,
                        Description = $"{player.Name2} took my side in the custody dispute",
                        InvolvedCharacter = player.Name2, Importance = 0.8f, EmotionalImpact = 0.5f
                    });

                    if (exSpouse != null)
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine($"  {Loc.Get("petition.side.ex_glares", exName)}");
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
                    terminal.WriteLine($"\n  {Loc.Get("petition.side.believe_ex", exName)}");
                    terminal.SetColor("red");
                    terminal.WriteLine($"  {Loc.Get("petition.side.devastated", petitioner.Name2)}");

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
                    terminal.WriteLine($"\n  {Loc.Get("petition.custody.ignore_line")}");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {Loc.Get("petition.custody.ignore_walks_away", petitioner.Name2)}");
                    break;
            }

            await terminal.PressAnyKey();
        }

        #endregion

        #region Petition 4: Royal Petition

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
                    petitionText = Loc.Get("petition.royal.tax_text", king.TaxRate);
                    break;
                case 1: // Justice
                    var aggressor = NPCSpawnSystem.Instance?.ActiveNPCs?
                        .FirstOrDefault(n => !n.IsDead && n != petitioner &&
                            (n.Memory?.GetCharacterImpression(petitioner.Name2) ?? 0f) < -0.3f);
                    string aggressorName = aggressor?.Name2 ?? Loc.Get("petition.royal.justice_scoundrel");
                    petitionType = "justice";
                    petitionText = Loc.Get("petition.royal.justice_text", aggressorName);
                    break;
                case 2: // Monster threat
                    petitionType = "monster";
                    petitionText = Loc.Get("petition.royal.monster_text");
                    break;
                default: // Marriage blessing
                    var partner = NPCSpawnSystem.Instance?.ActiveNPCs?
                        .FirstOrDefault(n => !n.IsDead && !n.Married && n != petitioner &&
                            (n.Memory?.GetCharacterImpression(petitioner.Name2) ?? 0f) > 0.3f);
                    string partnerName = partner?.Name2 ?? Loc.Get("petition.royal.marriage_beloved");
                    petitionType = "marriage";
                    petitionText = Loc.Get("petition.royal.marriage_text", partnerName);
                    break;
            }

            terminal.ClearScreen();
            UIHelper.DrawBoxTop(terminal, Loc.Get("petition.royal.header"), "bright_yellow");
            UIHelper.DrawBoxEmpty(terminal, "bright_yellow");
            UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.royal.kneels", petitioner.Name2)}", "bright_yellow", "white");
            UIHelper.DrawBoxEmpty(terminal, "bright_yellow");
            UIHelper.DrawBoxLine(terminal, $"  \"{petitionText}\"", "bright_yellow", "bright_cyan");
            UIHelper.DrawBoxEmpty(terminal, "bright_yellow");
            UIHelper.DrawBoxSeparator(terminal, "bright_yellow");

            switch (petitionType)
            {
                case "tax":
                    long relief = king.TaxRate * 5; // 5 days of tax relief cost
                    UIHelper.DrawMenuOption(terminal, "G", Loc.Get("petition.royal.tax_grant", relief), "bright_yellow", "bright_cyan", "bright_green");
                    UIHelper.DrawMenuOption(terminal, "D", Loc.Get("petition.royal.tax_deny"), "bright_yellow", "bright_cyan", "red");
                    UIHelper.DrawMenuOption(terminal, "H", Loc.Get("petition.royal.tax_halve"), "bright_yellow", "bright_cyan", "white");
                    break;
                case "justice":
                    UIHelper.DrawMenuOption(terminal, "J", Loc.Get("petition.royal.justice_investigate"), "bright_yellow", "bright_cyan", "bright_green");
                    UIHelper.DrawMenuOption(terminal, "D", Loc.Get("petition.royal.justice_dismiss"), "bright_yellow", "bright_cyan", "red");
                    UIHelper.DrawMenuOption(terminal, "C", Loc.Get("petition.royal.justice_compensate"), "bright_yellow", "bright_cyan", "white");
                    break;
                case "monster":
                    long guardCost = 500;
                    UIHelper.DrawMenuOption(terminal, "S", Loc.Get("petition.royal.monster_send", guardCost), "bright_yellow", "bright_cyan", "bright_green");
                    UIHelper.DrawMenuOption(terminal, "P", Loc.Get("petition.royal.monster_promise"), "bright_yellow", "bright_cyan", "white");
                    UIHelper.DrawMenuOption(terminal, "D", Loc.Get("petition.royal.monster_dismiss"), "bright_yellow", "bright_cyan", "red");
                    break;
                case "marriage":
                    UIHelper.DrawMenuOption(terminal, "B", Loc.Get("petition.royal.marriage_bless"), "bright_yellow", "bright_cyan", "bright_green");
                    UIHelper.DrawMenuOption(terminal, "D", Loc.Get("petition.royal.marriage_deny"), "bright_yellow", "bright_cyan", "red");
                    break;
            }

            UIHelper.DrawBoxBottom(terminal, "bright_yellow");
            var choice = await terminal.GetInput($"\n  {Loc.Get("petition.royal.prompt")}");

            // Process ruling
            switch (petitionType)
            {
                case "tax":
                    if (choice.ToUpper() == "G")
                    {
                        long cost = king.TaxRate * 5;
                        king.Treasury -= cost;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"\n  {Loc.Get("petition.royal.tax_grant_result", cost)}");
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
                        terminal.WriteLine($"\n  {Loc.Get("petition.royal.tax_halve_result", cost)}");
                        player.Chivalry += 2;
                        NewsSystem.Instance?.Newsy($"King {player.Name2} offered partial tax relief to {petitioner.Name2}.");
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine($"\n  {Loc.Get("petition.royal.tax_deny_result")}");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  {Loc.Get("petition.royal.tax_deny_muttering", petitioner.Name2)}");
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
                        terminal.WriteLine($"\n  {Loc.Get("petition.royal.justice_investigate_result")}");
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
                        terminal.WriteLine($"\n  {Loc.Get("petition.royal.justice_compensate_result", comp)}");
                        petitioner.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Helped, Description = $"King {player.Name2} compensated me for my losses",
                            InvolvedCharacter = player.Name2, Importance = 0.6f, EmotionalImpact = 0.3f
                        });
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine($"\n  {Loc.Get("petition.royal.justice_dismiss_result")}");
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
                        terminal.WriteLine($"\n  {Loc.Get("petition.royal.monster_send_result")}");
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
                        terminal.WriteLine($"\n  {Loc.Get("petition.royal.monster_promise_result")}");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  {Loc.Get("petition.royal.monster_promise_inspired")}");
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
                        terminal.WriteLine($"\n  {Loc.Get("petition.royal.monster_dismiss_result")}");
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
                        terminal.WriteLine($"\n  {Loc.Get("petition.royal.marriage_bless_result")}");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  {Loc.Get("petition.royal.marriage_bless_joy", petitioner.Name2)}");
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
                        terminal.WriteLine($"\n  {Loc.Get("petition.royal.marriage_deny_result")}");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  {Loc.Get("petition.royal.marriage_deny_crushed", petitioner.Name2)}");
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
            UIHelper.DrawBoxTop(terminal, Loc.Get("petition.dying.header"), "magenta");
            UIHelper.DrawBoxEmpty(terminal, "magenta");
            UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.dying.approaches", elder.Name2, elder.Age)}", "magenta", "white");
            UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.dying.old_tired")}", "magenta", "gray");
            UIHelper.DrawBoxEmpty(terminal, "magenta");

            switch (wishRoll)
            {
                case 0: // Legacy — deliver message
                    var recipient = NPCSpawnSystem.Instance?.ActiveNPCs?
                        .FirstOrDefault(n => !n.IsDead && n != elder && n.Name2 != player.Name2);
                    string recipientName = recipient?.Name2 ?? Loc.Get("petition.dying.someone_special");

                    UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.dying.legacy_not_long", player.Name2)}", "magenta", "bright_cyan");
                    UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.dying.legacy_tell", recipientName)}", "magenta", "cyan");
                    UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.dying.legacy_know")}", "magenta", "cyan");
                    UIHelper.DrawBoxEmpty(terminal, "magenta");
                    UIHelper.DrawBoxSeparator(terminal, "magenta");
                    UIHelper.DrawMenuOption(terminal, "P", Loc.Get("petition.dying.legacy_option_promise"), "magenta", "bright_yellow", "bright_green");
                    UIHelper.DrawMenuOption(terminal, "D", Loc.Get("petition.dying.legacy_option_decline"), "magenta", "bright_yellow", "gray");
                    UIHelper.DrawBoxBottom(terminal, "magenta");

                    var legacyChoice = await terminal.GetInput($"\n  {Loc.Get("petition.dying.legacy_prompt")}");
                    if (legacyChoice.ToUpper() == "P")
                    {
                        long inheritance = 500 + elder.Level * 50;
                        player.Gold += inheritance;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"\n  {Loc.Get("petition.dying.legacy_thanks", player.Name2, race)}");
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"  {Loc.Get("petition.dying.legacy_gold", elder.Name2, inheritance)}");
                        terminal.SetColor("gray");
                        terminal.WriteLine($"  {Loc.Get("petition.dying.legacy_savings")}");

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
                        terminal.WriteLine($"\n  {Loc.Get("petition.dying.legacy_decline", elder.Name2)}");
                    }
                    break;

                case 1: // Confession
                    UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.dying.confession_intro")}", "magenta", "bright_cyan");
                    UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.dying.confession_secret")}", "magenta", "cyan");

                    // Generate a random confession
                    int confessionType = _random.Next(3);
                    string confession = confessionType switch
                    {
                        0 => Loc.Get("petition.dying.confession_gold"),
                        1 => Loc.Get("petition.dying.confession_affair"),
                        _ => Loc.Get("petition.dying.confession_plot")
                    };

                    UIHelper.DrawBoxLine(terminal, $"  {confession}", "magenta", "bright_yellow");
                    UIHelper.DrawBoxEmpty(terminal, "magenta");
                    UIHelper.DrawBoxSeparator(terminal, "magenta");
                    UIHelper.DrawMenuOption(terminal, "L", Loc.Get("petition.dying.confession_option_listen"), "magenta", "bright_yellow", "bright_green");
                    UIHelper.DrawMenuOption(terminal, "D", Loc.Get("petition.dying.confession_option_grave"), "magenta", "bright_yellow", "gray");
                    UIHelper.DrawBoxBottom(terminal, "magenta");

                    var confChoice = await terminal.GetInput($"\n  {Loc.Get("petition.dying.confession_prompt")}");
                    if (confChoice.ToUpper() == "L")
                    {
                        if (confessionType == 0)
                        {
                            long stash = 1000 + elder.Level * 100;
                            player.Gold += stash;
                            terminal.SetColor("yellow");
                            terminal.WriteLine($"\n  {Loc.Get("petition.dying.confession_gold_found", stash)}");
                        }
                        else
                        {
                            terminal.SetColor("bright_cyan");
                            terminal.WriteLine($"\n  {Loc.Get("petition.dying.confession_listen")}");
                            terminal.SetColor("white");
                            terminal.WriteLine($"  {Loc.Get("petition.dying.confession_valuable")}");
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
                        terminal.WriteLine($"\n  {Loc.Get("petition.dying.confession_decline", elder.Name2)}");
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
                        protectedName = Loc.Get("petition.dying.protect_friends");
                        protectedType = "friends";
                    }

                    UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.dying.protect_watch_over", protectedName)}", "magenta", "bright_cyan");
                    UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.dying.protect_alone")}", "magenta", "cyan");
                    UIHelper.DrawBoxEmpty(terminal, "magenta");
                    UIHelper.DrawBoxSeparator(terminal, "magenta");
                    UIHelper.DrawMenuOption(terminal, "P", Loc.Get("petition.dying.protect_option_promise", protectedName), "magenta", "bright_yellow", "bright_green");
                    UIHelper.DrawMenuOption(terminal, "D", Loc.Get("petition.dying.protect_option_cant"), "magenta", "bright_yellow", "gray");
                    UIHelper.DrawBoxBottom(terminal, "magenta");

                    var protChoice = await terminal.GetInput($"\n  {Loc.Get("petition.dying.protect_prompt")}");
                    if (protChoice.ToUpper() == "P")
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"\n  {Loc.Get("petition.dying.protect_grateful", elder.Name2)}");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  {Loc.Get("petition.dying.protect_knowing", protectedName)}");

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
                        terminal.WriteLine($"  {Loc.Get("petition.dying.protect_gold", elder.Name2, gift)}");

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
                        terminal.WriteLine($"\n  {Loc.Get("petition.dying.protect_decline")}");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  {Loc.Get("petition.dying.protect_shuffles", elder.Name2)}");
                    }
                    break;
            }

            await terminal.PressAnyKey();
        }

        #endregion

        #region Petition 7: Missing Person (Removed — quest completion didn't restore NPCs to town)
        // Missing person quests removed in v0.52.7: the quest auto-completed when reaching
        // the target dungeon floor, but never restored the "missing" NPC's location, leaving
        // them permanently invisible at all town locations even after quest turn-in.
        #endregion

        #region Petition 8: Rivalry Report

        private async Task ExecuteRivalryReport(NPC warner, Character player, TerminalEmulator terminal)
        {
            terminal.ClearScreen();
            UIHelper.DrawBoxTop(terminal, Loc.Get("petition.warning.header"), "bright_cyan");
            UIHelper.DrawBoxEmpty(terminal, "bright_cyan");
            UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.warning.pulls_corner", warner.Name2)}", "bright_cyan", "white");
            UIHelper.DrawBoxEmpty(terminal, "bright_cyan");
            UIHelper.DrawBoxLine(terminal, $"  {Loc.Get("petition.warning.like_you", player.Name2)}", "bright_cyan", "bright_yellow");

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
                    warningText = Loc.Get("petition.warning.court_plot");
                    threatDetail = Loc.Get("petition.warning.court_plot_detail", plot.PlotType, plot.Progress);
                }
                else
                {
                    warningText = Loc.Get("petition.warning.court_rumblings");
                    threatDetail = Loc.Get("petition.warning.court_dissent");
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
                    warningText = Loc.Get("petition.warning.rival_team", teamName, teamSize, maxLevel);
                    threatDetail = Loc.Get("petition.warning.rival_team_detail", teamName, teamSize, maxLevel);
                }
                else
                {
                    warningText = Loc.Get("petition.warning.general");
                    threatDetail = Loc.Get("petition.warning.general_detail");
                }
            }

            UIHelper.DrawBoxLine(terminal, $"  {warningText}", "bright_cyan", "cyan");
            UIHelper.DrawBoxEmpty(terminal, "bright_cyan");
            UIHelper.DrawBoxSeparator(terminal, "bright_cyan");
            UIHelper.DrawMenuOption(terminal, "T", Loc.Get("petition.warning.option_tell_more"), "bright_cyan", "bright_yellow", "bright_green");
            UIHelper.DrawMenuOption(terminal, "A", Loc.Get("petition.warning.option_handle"), "bright_cyan", "bright_yellow", "white");
            UIHelper.DrawMenuOption(terminal, "D", Loc.Get("petition.warning.option_dismiss"), "bright_cyan", "bright_yellow", "gray");
            UIHelper.DrawBoxBottom(terminal, "bright_cyan");

            var choice = await terminal.GetInput($"\n  {Loc.Get("petition.warning.prompt")}");

            switch (choice.ToUpper())
            {
                case "T": // Get details
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine($"\n  {Loc.Get("petition.warning.shares_everything", warner.Name2)}");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {Loc.Get("petition.warning.intel", threatDetail)}");

                    if (isCourtPlot)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"\n  {Loc.Get("petition.warning.court_hint")}");
                    }

                    player.Experience += 50 + player.Level * 5;
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"\n  {Loc.Get("petition.warning.xp_gained", 50 + player.Level * 5)}");

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
                    terminal.WriteLine($"\n  {Loc.Get("petition.warning.acknowledge", player.Name2)}");
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  {Loc.Get("petition.warning.acknowledge_slips", warner.Name2)}");

                    warner.Memory?.RecordEvent(new MemoryEvent
                    {
                        Type = MemoryType.SocialInteraction,
                        Description = $"Warned {player.Name2} about threats, they acknowledged",
                        InvolvedCharacter = player.Name2, Importance = 0.5f, EmotionalImpact = 0.2f
                    });
                    break;

                default: // Dismiss
                    terminal.SetColor("gray");
                    terminal.WriteLine($"\n  {Loc.Get("petition.warning.dismiss_shrug", warner.Name2)}");
                    break;
            }

            await terminal.PressAnyKey();
        }

        #endregion
    }
}
