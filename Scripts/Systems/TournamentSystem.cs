using UsurperRemake.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Tournament/Competition System - Complete implementation based on Pascal COMPWAR.PAS, GYM.PAS, CHALLENG.PAS
/// Handles organized competitions, tug-of-war, automated tournaments, and all competition-related functionality
/// Direct Pascal compatibility with exact function preservation
/// </summary>
public class TournamentSystem
{
    private NewsSystem newsSystem;
    // MailSystem is static - no need to instantiate
    private CombatEngine combatEngine;
    private TeamSystem teamSystem;
    private RelationshipSystem relationshipSystem;
    
    // Pascal constants from GYM.PAS
    private const int MaxTugTeamMembers = GameConfig.MaxTeamMembers; // 5 members per team
    private const int MaxBouts = 79; // max "pull-ropes" before it's called a draw
    private const int MaxTeamPower = 100; // starting team power for tug-of-war
    private const char PullKey = 'P'; // key to start tug-of-war
    
    // Experience rewards from Pascal GYM.PAS
    private const int TugWinExperience = 250; // level * 250 for winners
    private const int TugDrawExperience = 150; // level * 150 for draws
    private const int TugLossExperience = 0; // no experience for losers
    
    // Tournament types
    public enum TournamentType
    {
        TugOfWar,           // GYM.PAS tug-of-war competition
        SingleElimination,  // Standard bracket tournament
        RoundRobin,         // Everyone fights everyone
        TeamBattle,         // Team vs team (using TeamSystem)
        AutoTournament      // Automated NPC tournament
    }
    
    // Tournament status
    public enum TournamentStatus
    {
        Setup,              // Setting up teams/participants
        InProgress,         // Tournament running
        Completed,          // Tournament finished
        Cancelled           // Tournament cancelled
    }
    
    public TournamentSystem()
    {
        newsSystem = NewsSystem.Instance;
        combatEngine = new CombatEngine();
        teamSystem = new TeamSystem();
        relationshipSystem = RelationshipSystem.Instance;
    }
    
    #region Pascal Tug-of-War Implementation - GYM.PAS
    
    /// <summary>
    /// Create tug-of-war competition - Pascal GYM.PAS tug-of-war functionality
    /// </summary>
    public async Task<TournamentResult> CreateTugOfWarCompetition(Character organizer, List<Character> homeTeam, List<Character> awayTeam)
    {
        if (organizer.GymSessions <= 0)
        {
            return new TournamentResult { Success = false, Message = "No gym sessions left!" };
        }
        
        // Validate teams
        if (homeTeam.Count == 0 || awayTeam.Count == 0)
        {
            return new TournamentResult { Success = false, Message = "Both teams must have at least one member!" };
        }
        
        if (homeTeam.Count > MaxTugTeamMembers || awayTeam.Count > MaxTugTeamMembers)
        {
            return new TournamentResult { Success = false, Message = $"Teams cannot exceed {MaxTugTeamMembers} members!" };
        }
        
        // Check relations for team members (Pascal relation checking)
        foreach (var member in homeTeam.Skip(1)) // Skip organizer
        {
            if (!CheckTugOfWarRelation(organizer, member))
            {
                return new TournamentResult { Success = false, Message = $"{member.Name2} refuses to fight for your cause due to bad relations!" };
            }
        }
        
        // Send mail invitations (Pascal mail system)
        await SendTugOfWarInvitations(organizer, homeTeam, awayTeam);
        
        // Consume gym session
        organizer.GymSessions--;
        
        // Conduct tug-of-war
        var result = await ConductTugOfWar(organizer, homeTeam, awayTeam);
        
        return result;
    }
    
    /// <summary>
    /// Check relation for tug-of-war participation - Pascal GYM.PAS social_relation logic
    /// </summary>
    private bool CheckTugOfWarRelation(Character organizer, Character participant)
    {
        // Get relation between organizer and participant
        var relation = relationshipSystem.GetRelation(organizer.Name2, participant.Name2);
        
        // Pascal relation constants: Enemy=100, Anger=90, Hate=110
        return relation.Relation2 < GameConfig.RelationAnger; // Not enemy, angry, or hate
    }
    
    /// <summary>
    /// Send tug-of-war invitations - Pascal GYM.PAS mail system
    /// </summary>
    private async Task SendTugOfWarInvitations(Character organizer, List<Character> homeTeam, List<Character> awayTeam)
    {
        // Mail home team members (excluding organizer)
        foreach (var member in homeTeam.Skip(1))
        {
            var homeTeamList = string.Join("\n            ", homeTeam.Select(m => m.Name2));
            var awayTeamList = string.Join("\n            ", awayTeam.Select(m => m.Name2));
            
            mailSystem.SendMail(
                member.Name2,
                $"{GameConfig.NewsColorRoyal}Tug-of-War Competition{GameConfig.NewsColorDefault}",
                $"{GameConfig.NewsColorPlayer}{organizer.Name2}{GameConfig.NewsColorDefault} invited you to a Tug-of-War Competition!",
                $"You were selected to fight in {GameConfig.NewsColorPlayer}{organizer.Name2}{GameConfig.NewsColorDefault}'s Team.",
                $"Your Team: {GameConfig.NewsColorPlayer}{homeTeamList}{GameConfig.NewsColorDefault}",
                $"Other Team: {GameConfig.NewsColorPlayer}{awayTeamList}{GameConfig.NewsColorDefault}"
            );
        }
        
        // Mail away team members
        foreach (var member in awayTeam)
        {
            var homeTeamList = string.Join("\n            ", homeTeam.Select(m => m.Name2));
            var awayTeamList = string.Join("\n            ", awayTeam.Select(m => m.Name2));
            
            mailSystem.SendMail(
                member.Name2,
                $"{GameConfig.NewsColorRoyal}Tug-of-War Competition{GameConfig.NewsColorDefault}",
                $"{GameConfig.NewsColorPlayer}{organizer.Name2}{GameConfig.NewsColorDefault} invited you to a Tug-of-War Competition!",
                $"You were selected to fight against {GameConfig.NewsColorPlayer}{organizer.Name2}{GameConfig.NewsColorDefault}'s Team.",
                $"Your Team: {GameConfig.NewsColorPlayer}{awayTeamList}{GameConfig.NewsColorDefault}",
                $"Other Team: {GameConfig.NewsColorPlayer}{homeTeamList}{GameConfig.NewsColorDefault}"
            );
        }
    }
    
    /// <summary>
    /// Conduct tug-of-war competition - Pascal GYM.PAS rope pulling mechanics
    /// </summary>
    private async Task<TournamentResult> ConductTugOfWar(Character organizer, List<Character> homeTeam, List<Character> awayTeam)
    {
        var result = new TournamentResult
        {
            Type = TournamentType.TugOfWar,
            Organizer = organizer.Name2,
            Participants = homeTeam.Concat(awayTeam).Select(p => p.Name2).ToList()
        };
        
        // Initialize team powers (Pascal team power calculation)
        int team1Power = CalculateTeamPower(homeTeam);
        int team2Power = CalculateTeamPower(awayTeam);
        
        int bout = 0;
        var rounds = new List<TugRound>();
        
        // Tug-of-war loop (Pascal repeat-until logic)
        while (team1Power > 0 && team2Power > 0 && bout < MaxBouts)
        {
            bout++;
            
            // Pull rope once (Pascal pull_rope_once)
            var roundResult = PullRopeOnce(homeTeam, awayTeam, ref team1Power, ref team2Power, bout);
            rounds.Add(roundResult);
            
            // Add delay for dramatic effect
            await Task.Delay(500);
        }
        
        // Determine result
        TugResult tugResult;
        if (bout >= MaxBouts)
        {
            tugResult = TugResult.Draw;
            result.Winner = "Draw";
        }
        else if (team1Power <= 0)
        {
            tugResult = TugResult.AwayWin;
            result.Winner = awayTeam[0].Name2;
        }
        else
        {
            tugResult = TugResult.HomeWin;
            result.Winner = homeTeam[0].Name2;
        }
        
        result.TugRounds = rounds;
        result.Success = true;
        
        // Process results (Pascal experience and mail distribution)
        await ProcessTugOfWarResults(organizer, homeTeam, awayTeam, tugResult, bout);
        
        return result;
    }
    
    /// <summary>
    /// Calculate team power - Pascal team strength calculation
    /// </summary>
    private int CalculateTeamPower(List<Character> team)
    {
        // Base power calculation from Pascal
        int totalPower = 0;
        
        foreach (var member in team)
        {
            // Pascal power calculation: level + strength + some randomness
            int memberPower = (int)(member.Level + member.Strength) / team.Count;
            totalPower += memberPower;
        }
        
        return Math.Max(MaxTeamPower, totalPower); // Ensure minimum starting power
    }
    
    /// <summary>
    /// Pull rope once - Pascal GYM.PAS pull_rope_once procedure
    /// </summary>
    private TugRound PullRopeOnce(List<Character> homeTeam, List<Character> awayTeam, ref int team1Power, ref int team2Power, int roundNumber)
    {
        var random = new Random();
        
        // Calculate pull strength for each team (Pascal logic)
        int team1Pull = 0;
        int team2Pull = 0;
        
        foreach (var member in homeTeam)
        {
            int memberPull = random.Next(1, (int)member.Strength + 1);
            team1Pull += memberPull;
        }
        
        foreach (var member in awayTeam)
        {
            int memberPull = random.Next(1, (int)member.Strength + 1);
            team2Pull += memberPull;
        }
        
        // Apply pull result
        int powerShift = Math.Abs(team1Pull - team2Pull) / 10;
        
        if (team1Pull > team2Pull)
        {
            team2Power -= powerShift;
            team2Power = Math.Max(0, team2Power);
        }
        else if (team2Pull > team1Pull)
        {
            team1Power -= powerShift;
            team1Power = Math.Max(0, team1Power);
        }
        
        return new TugRound
        {
            RoundNumber = roundNumber,
            Team1Pull = team1Pull,
            Team2Pull = team2Pull,
            Team1Power = team1Power,
            Team2Power = team2Power,
            Winner = team1Pull > team2Pull ? "Home" : (team2Pull > team1Pull ? "Away" : "Tie")
        };
    }
    
    /// <summary>
    /// Process tug-of-war results - Pascal GYM.PAS reward and mail distribution
    /// </summary>
    private async Task ProcessTugOfWarResults(Character organizer, List<Character> homeTeam, List<Character> awayTeam, TugResult result, int rounds)
    {
        // News coverage (Pascal newsy calls)
        string newsHeader = "Tug-of-War Competition";
        string newsMessage = $"{GameConfig.NewsColorPlayer}{organizer.Name2}{GameConfig.NewsColorDefault} arranged a TUG-of-War Competition.";
        
        switch (result)
        {
            case TugResult.Draw:
                newsMessage += " The competition ended in a DRAW after both teams were exhausted.";
                await ProcessDrawResults(homeTeam, awayTeam, rounds);
                break;
                
            case TugResult.HomeWin:
                newsMessage += $" {GameConfig.NewsColorPlayer}{organizer.Name2}{GameConfig.NewsColorDefault}'s team won!";
                await ProcessWinLossResults(homeTeam, awayTeam, rounds, true);
                break;
                
            case TugResult.AwayWin:
                newsMessage += $" {GameConfig.NewsColorPlayer}{organizer.Name2}{GameConfig.NewsColorDefault}'s team lost!";
                await ProcessWinLossResults(awayTeam, homeTeam, rounds, false);
                break;
        }
        
        newsSystem.Newsy($"{newsHeader}: {newsMessage}", false, GameConfig.NewsCategory.General);
    }
    
    /// <summary>
    /// Process draw results - Pascal GYM.PAS draw handling
    /// </summary>
    private async Task ProcessDrawResults(List<Character> homeTeam, List<Character> awayTeam, int rounds)
    {
        // Award experience to all participants (Pascal level * 150)
        await AwardTugExperience(homeTeam.Concat(awayTeam).ToList(), TugDrawExperience, "DRAW", rounds);
    }
    
    /// <summary>
    /// Process win/loss results - Pascal GYM.PAS win/loss handling
    /// </summary>
    private async Task ProcessWinLossResults(List<Character> winners, List<Character> losers, int rounds, bool organizerWon)
    {
        // Award experience to winners (Pascal level * 250)
        await AwardTugExperience(winners, TugWinExperience, "WON", rounds);
        
        // No experience for losers (Pascal: 0 experience)
        await AwardTugExperience(losers, TugLossExperience, "LOST", rounds);
    }
    
    /// <summary>
    /// Award tug-of-war experience - Pascal GYM.PAS experience distribution
    /// </summary>
    private async Task AwardTugExperience(List<Character> participants, int experienceMultiplier, string result, int rounds)
    {
        foreach (var participant in participants)
        {
            if (experienceMultiplier > 0)
            {
                long experienceGained = participant.Level * experienceMultiplier;
                participant.Experience += experienceGained;
                
                // Mail participant about results
                string mailSubject = $"{GameConfig.NewsColorRoyal}Competition {result}{GameConfig.NewsColorDefault}";
                string mailMessage = experienceMultiplier == TugDrawExperience
                    ? $"The Tug-of-War ended in a {result}. You earned {GameConfig.NewsColorPlayer}{experienceGained:N0}{GameConfig.NewsColorDefault} experience points!"
                    : result == "WON"
                        ? $"The Tug-of-War was {result} by your team! You earned {GameConfig.NewsColorPlayer}{experienceGained:N0}{GameConfig.NewsColorDefault} experience points!"
                        : $"The Tug-of-War ended in a defeat for your team. You earned {GameConfig.NewsColorDeath}no{GameConfig.NewsColorDefault} experience points.";
                        
                mailMessage += $" The game ended after {GameConfig.NewsColorPlayer}{rounds}{GameConfig.NewsColorDefault} rounds.";
                
                mailSystem.SendMail(participant.Name2, mailSubject, mailMessage);
            }
            else
            {
                // Send loss mail with no experience
                string mailSubject = $"{GameConfig.NewsColorDeath}Competition {result}{GameConfig.NewsColorDefault}";
                string mailMessage = $"The Tug-of-War ended in a defeat for your team. You earned {GameConfig.NewsColorDeath}no{GameConfig.NewsColorDefault} experience points.";
                mailMessage += $" The game was lost after {GameConfig.NewsColorPlayer}{rounds}{GameConfig.NewsColorDefault} rounds.";
                
                mailSystem.SendMail(participant.Name2, mailSubject, mailMessage);
            }
        }
    }
    
    #endregion
    
    #region Pascal Computer vs Computer Tournaments - COMPWAR.PAS
    
    /// <summary>
    /// Create automated tournament - Pascal COMPWAR.PAS computer_computer functionality
    /// </summary>
    public async Task<TournamentResult> CreateAutomatedTournament(List<Character> participants, TournamentType type)
    {
        var result = new TournamentResult
        {
            Type = type,
            Participants = participants.Select(p => p.Name2).ToList(),
            Status = TournamentStatus.InProgress
        };
        
        switch (type)
        {
            case TournamentType.SingleElimination:
                result = await ConductSingleEliminationTournament(participants);
                break;
                
            case TournamentType.RoundRobin:
                result = await ConductRoundRobinTournament(participants);
                break;
                
            case TournamentType.AutoTournament:
                result = await ConductAutomatedTournament(participants);
                break;
                
            default:
                result.Success = false;
                result.Message = "Unsupported tournament type";
                break;
        }
        
        return result;
    }
    
    /// <summary>
    /// Conduct single elimination tournament
    /// </summary>
    private async Task<TournamentResult> ConductSingleEliminationTournament(List<Character> participants)
    {
        var result = new TournamentResult
        {
            Type = TournamentType.SingleElimination,
            Participants = participants.Select(p => p.Name2).ToList()
        };
        
        var currentRound = participants.ToList();
        var rounds = new List<TournamentRound>();
        int roundNumber = 1;
        
        while (currentRound.Count > 1)
        {
            var roundResult = new TournamentRound { RoundNumber = roundNumber, Matches = new List<TournamentMatch>() };
            var nextRound = new List<Character>();
            
            // Pair up fighters
            for (int i = 0; i < currentRound.Count; i += 2)
            {
                if (i + 1 < currentRound.Count)
                {
                    var match = await ConductAutomatedMatch(currentRound[i], currentRound[i + 1]);
                    roundResult.Matches.Add(match);
                    
                    // Winner advances
                    var winner = match.Winner == currentRound[i].Name2 ? currentRound[i] : currentRound[i + 1];
                    nextRound.Add(winner);
                }
                else
                {
                    // Bye - player advances automatically
                    nextRound.Add(currentRound[i]);
                }
            }
            
            rounds.Add(roundResult);
            currentRound = nextRound;
            roundNumber++;
            
            // News coverage for each round
            newsSystem.Newsy($"Tournament Round {roundNumber - 1}: Round {roundNumber - 1} of the tournament has concluded with {nextRound.Count} fighters remaining!",
                false, GameConfig.NewsCategory.General);
        }
        
        result.TournamentRounds = rounds;
        result.Winner = currentRound.Count > 0 ? currentRound[0].Name2 : "No Winner";
        result.Success = true;
        result.Status = TournamentStatus.Completed;
        
        // Final news coverage
        newsSystem.Newsy($"Tournament Champion! {GameConfig.NewsColorPlayer}{result.Winner}{GameConfig.NewsColorDefault} has won the tournament!",
            false, GameConfig.NewsCategory.General);
        
        return result;
    }
    
    /// <summary>
    /// Conduct round robin tournament
    /// </summary>
    private async Task<TournamentResult> ConductRoundRobinTournament(List<Character> participants)
    {
        var result = new TournamentResult
        {
            Type = TournamentType.RoundRobin,
            Participants = participants.Select(p => p.Name2).ToList()
        };
        
        var wins = new Dictionary<string, int>();
        var matches = new List<TournamentMatch>();
        
        // Initialize win counters
        foreach (var participant in participants)
        {
            wins[participant.Name2] = 0;
        }
        
        // Everyone fights everyone else
        for (int i = 0; i < participants.Count; i++)
        {
            for (int j = i + 1; j < participants.Count; j++)
            {
                var match = await ConductAutomatedMatch(participants[i], participants[j]);
                matches.Add(match);
                
                // Track wins
                wins[match.Winner]++;
            }
        }
        
        // Determine winner by most wins
        var winner = wins.OrderByDescending(kvp => kvp.Value).First();
        
        result.Matches = matches;
        result.Winner = winner.Key;
        result.Success = true;
        result.Status = TournamentStatus.Completed;
        
        // News coverage
        newsSystem.Newsy($"Round Robin Champion! {GameConfig.NewsColorPlayer}{winner}{GameConfig.NewsColorDefault} has won the round robin tournament!",
            false, GameConfig.NewsCategory.General);
        
        return result;
    }
    
    /// <summary>
    /// Conduct automated tournament - Pascal COMPWAR.PAS logic
    /// </summary>
    private async Task<TournamentResult> ConductAutomatedTournament(List<Character> participants)
    {
        // This uses simplified automated logic for NPC tournaments
        var result = new TournamentResult
        {
            Type = TournamentType.AutoTournament,
            Participants = participants.Select(p => p.Name2).ToList()
        };
        
        var random = new Random();
        
        // Determine winner based on level and some randomness (Pascal logic)
        var weightedParticipants = participants.Select(p => new
        {
            Character = p,
            Weight = p.Level + p.Strength + p.Defence + random.Next(-10, 11)
        }).OrderByDescending(p => p.Weight).ToList();
        
        result.Winner = weightedParticipants.First().Character.Name2;
        result.Success = true;
        result.Status = TournamentStatus.Completed;
        
        // Simple news coverage
        newsSystem.Newsy($"Automated Tournament Complete! The automated tournament has concluded. {GameConfig.NewsColorPlayer}{result.Winner}{GameConfig.NewsColorDefault} is the champion!",
            false, GameConfig.NewsCategory.General);
        
        return result;
    }
    
    /// <summary>
    /// Conduct automated match - Pascal COMPWAR.PAS computer_computer procedure
    /// </summary>
    private async Task<TournamentMatch> ConductAutomatedMatch(Character fighter1, Character fighter2)
    {
        var match = new TournamentMatch
        {
            Fighter1 = fighter1.Name2,
            Fighter2 = fighter2.Name2
        };
        
        // Reset fighters for battle (Pascal logic)
        fighter1.HP = fighter1.MaxHP;
        fighter2.HP = fighter2.MaxHP;
        fighter1.Casted = false;
        fighter2.Casted = false;
        fighter1.Absorb = 0;
        fighter2.Absorb = 0;
        fighter1.Punch = 0;
        fighter2.Punch = 0;
        
        // Conduct automated battle using CombatEngine
        var battleResult = await combatEngine.ConductPlayerVsPlayerCombat(fighter1, fighter2, true); // fastgame = true
        
        match.Winner = battleResult.Winner?.Name2 ?? "Draw";
        match.Rounds = battleResult.Rounds;
        
        return match;
    }
    
    #endregion
    
    #region Utility Functions
    
    /// <summary>
    /// Check if character can participate in tournament
    /// </summary>
    public bool CanParticipateInTournament(Character character, TournamentType type)
    {
        if (!character.Allowed || character.Deleted || character.HP <= 0)
            return false;
            
        switch (type)
        {
            case TournamentType.TugOfWar:
                return character.GymSessions > 0 || character == GetCharacterByName(character.Name2); // Organizer can always participate
                
            default:
                return true;
        }
    }
    
    /// <summary>
    /// Get character by name (utility function)
    /// </summary>
    private Character GetCharacterByName(string name)
    {
        // TODO: Implement character lookup from game files
        return null;
    }
    
    /// <summary>
    /// Get all characters available for tournaments
    /// </summary>
    public List<Character> GetAvailableTournamentParticipants()
    {
        // TODO: Load from USERS.DAT and NPCS.DAT
        return new List<Character>();
    }
    
    #endregion
    
    #region Data Structures
    
    public class TournamentResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public TournamentType Type { get; set; }
        public string Organizer { get; set; } = "";
        public List<string> Participants { get; set; } = new List<string>();
        public string Winner { get; set; } = "";
        public TournamentStatus Status { get; set; } = TournamentStatus.Setup;
        
        // For different tournament types
        public List<TugRound> TugRounds { get; set; } = new List<TugRound>();
        public List<TournamentRound> TournamentRounds { get; set; } = new List<TournamentRound>();
        public List<TournamentMatch> Matches { get; set; } = new List<TournamentMatch>();
    }
    
    public class TugRound
    {
        public int RoundNumber { get; set; }
        public int Team1Pull { get; set; }
        public int Team2Pull { get; set; }
        public int Team1Power { get; set; }
        public int Team2Power { get; set; }
        public string Winner { get; set; } = "";
    }
    
    public class TournamentRound
    {
        public int RoundNumber { get; set; }
        public List<TournamentMatch> Matches { get; set; } = new List<TournamentMatch>();
    }
    
    public class TournamentMatch
    {
        public string Fighter1 { get; set; } = "";
        public string Fighter2 { get; set; } = "";
        public string Winner { get; set; } = "";
        public int Rounds { get; set; } = 0;
    }
    
    public enum TugResult
    {
        HomeWin,
        AwayWin,
        Draw
    }
    
    #endregion
} 
