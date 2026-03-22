using UsurperRemake.Utils;
using UsurperRemake.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Team/Gang Warfare System - Complete implementation based on Pascal GANGWARS.PAS, TCORNER.PAS, AUTOGANG.PAS
/// Handles team creation, management, combat, territory control, and all team-related functionality
/// Direct Pascal compatibility with exact function preservation
/// </summary>
public partial class TeamSystem
{
    private NewsSystem newsSystem;
    // MailSystem is static - no need to instantiate
    private CombatEngine combatEngine;
    private RelationshipSystem relationshipSystem;
    
    // Pascal constants from INIT.PAS
    private const int MaxTeamMembers = GameConfig.MaxTeamMembers; // global_maxteammembers = 5
    private const string TeamRecordFile = GameConfig.DataDir + "TEAMREC.DAT";
    private const string TeamRecordText = GameConfig.DataDir + "TEAMREC.TXT";
    
    // Team battle headers from AUTOGANG.PAS
    private readonly string[] GangWarHeaders = 
    {
        "Gang War!",
        "Team Bash!",
        "Team War!",
        "Turf War!",
        "Gang Fight!",
        "Rival Gangs Clash!"
    };
    
    public TeamSystem()
    {
        newsSystem = NewsSystem.Instance;
        combatEngine = new CombatEngine();
        relationshipSystem = RelationshipSystem.Instance;
    }
    
    #region Pascal Function Implementation - TCORNER.PAS
    
    /// <summary>
    /// Create a new team - Pascal TCORNER.PAS team creation functionality
    /// </summary>
    public bool CreateTeam(Character player, string teamName, string password)
    {
        if (string.IsNullOrEmpty(teamName) || teamName.Length > 40)
            return false;
            
        if (string.IsNullOrEmpty(password) || password.Length > 20)
            return false;
            
        if (!string.IsNullOrEmpty(player.Team))
            return false; // Already in a team
            
        // Check if team name already exists
        if (DoesTeamExist(teamName))
            return false;
            
        // Set team properties
        player.Team = teamName;
        player.TeamPW = password;
        player.CTurf = false; // No town control initially
        player.TeamRec = 0;   // No record yet
        
        // News announcement
        newsSystem.WriteTeamNews("New Gang Formed!", 
            $"{GameConfig.NewsColorPlayer}{player.Name2}{GameConfig.NewsColorDefault} formed the gang {GameConfig.NewsColorHighlight}{teamName}{GameConfig.NewsColorDefault}!");
            
        return true;
    }
    
    /// <summary>
    /// Join existing team - Pascal TCORNER.PAS team joining functionality
    /// </summary>
    public bool JoinTeam(Character player, string teamName, string password)
    {
        if (!string.IsNullOrEmpty(player.Team))
            return false; // Already in a team
            
        // Find team leader to verify password
        var teamLeader = GetTeamLeader(teamName);
        if (teamLeader == null)
            return false;
            
        if (teamLeader.TeamPW != password)
            return false; // Wrong password
            
        // Check if team is full
        if (GetTeamMemberCount(teamName) >= MaxTeamMembers)
            return false;
            
        // Transfer team status from leader to new member
        TransferTeamStatus(teamLeader, player);
        
        // News announcement
        newsSystem.WriteTeamNews("Gang Recruitment!",
            $"{GameConfig.NewsColorPlayer}{player.Name2}{GameConfig.NewsColorDefault} joined {GameConfig.NewsColorHighlight}{teamName}{GameConfig.NewsColorDefault}!");
            
        // Notify team members
        NotifyTeamMembers(teamName, $"{player.Name2} has joined the team!", player.Name2);
        
        return true;
    }
    
    /// <summary>
    /// Quit team - Pascal TCORNER.PAS team quitting functionality  
    /// </summary>
    public bool QuitTeam(Character player)
    {
        if (string.IsNullOrEmpty(player.Team))
            return false;
            
        string oldTeam = player.Team;
        bool hadTurf = player.CTurf;
        
        // Remove player from team
        player.Team = "";
        player.TeamPW = "";
        player.CTurf = false;
        player.TeamRec = 0;
        player.GymOwner = 0;
        
        // Check if team still has members
        int remainingMembers = GetTeamMemberCount(oldTeam);
        
        if (remainingMembers == 0)
        {
            // Team dissolved — clean up city control
            if (hadTurf)
            {
                CityControlSystem.Instance.RemoveCityControl(oldTeam);
            }

            newsSystem.WriteTeamNews("Gang Dissolved!",
                $"Gang {GameConfig.NewsColorHighlight}{oldTeam}{GameConfig.NewsColorDefault} has been disbanded!");
        }
        else
        {
            // Notify remaining team members
            NotifyTeamMembers(oldTeam, $"{player.Name2} has left the team!", player.Name2);

            newsSystem.WriteTeamNews("Gang Deserter!",
                $"{GameConfig.NewsColorPlayer}{player.Name2}{GameConfig.NewsColorDefault} left {GameConfig.NewsColorHighlight}{oldTeam}{GameConfig.NewsColorDefault}!");
        }
        
        return true;
    }
    
    /// <summary>
    /// Sack team member - Pascal TCORNER.PAS member removal functionality
    /// </summary>
    public bool SackTeamMember(Character leader, string memberName)
    {
        if (string.IsNullOrEmpty(leader.Team))
            return false;
            
        var member = GetCharacterByName(memberName);
        if (member == null || member.Team != leader.Team)
            return false;
            
        if (member.Name2 == leader.Name2)
            return false; // Can't sack yourself
            
        string teamName = member.Team;
        bool memberHadTurf = member.CTurf;

        // Remove member
        member.Team = "";
        member.TeamPW = "";
        member.CTurf = false;
        member.TeamRec = 0;
        member.GymOwner = 0;

        // If sacked member had CTurf, check if remaining members still have it
        // If no one else has CTurf, the team loses city control
        if (memberHadTurf)
        {
            var remainingMembers = GetTeamMembers(teamName);
            bool anyoneHasTurf = remainingMembers.Any(m => m.CTurf);
            if (!anyoneHasTurf && remainingMembers.Count > 0)
            {
                // No remaining member has CTurf — city control is lost
                CityControlSystem.Instance.RemoveCityControl(teamName);
            }
            else if (remainingMembers.Count == 0)
            {
                CityControlSystem.Instance.RemoveCityControl(teamName);
            }
        }

        // Mail the sacked member
        MailSystem.SendMail(member.Name2, "Team", 
            $"You were {GameConfig.NewsColorDeath}sacked{GameConfig.NewsColorDefault} from the team by {GameConfig.NewsColorPlayer}{leader.Name2}{GameConfig.NewsColorDefault}!");
        
        // Notify team members
        NotifyTeamMembers(teamName, $"{leader.Name2} sacked {memberName} from the team!", leader.Name2);
        
        // News announcement
        newsSystem.WriteTeamNews($"Internal Gang Turbulence!",
            $"{GameConfig.NewsColorPlayer}{leader.Name2}{GameConfig.NewsColorDefault} sacked {GameConfig.NewsColorPlayer}{memberName}{GameConfig.NewsColorDefault} from {GameConfig.NewsColorHighlight}{teamName}{GameConfig.NewsColorDefault}!");
        
        return true;
    }
    
    /// <summary>
    /// Send message to team - Pascal TCORNER.PAS team communication
    /// </summary>
    public void SendTeamMessage(Character player, string message)
    {
        if (string.IsNullOrEmpty(player.Team))
            return;
            
        var teamMembers = GetTeamMembers(player.Team);
        foreach (var member in teamMembers)
        {
            if (member.Name2 != player.Name2)
            {
                MailSystem.SendMail(member.Name2, "Team Message", 
                    $"From {GameConfig.NewsColorPlayer}{player.Name2}{GameConfig.NewsColorDefault}: {message}");
            }
        }
    }
    
    #endregion
    
    #region Pascal Function Implementation - GANGWARS.PAS
    
    /// <summary>
    /// Main gang wars function - Direct implementation of Pascal GANGWARS.PAS Gang_Wars procedure
    /// </summary>
    public async Task<bool> GangWars(Character attacker, string opponentTeam, bool turfWar)
    {
        if (string.IsNullOrEmpty(attacker.Team))
            return false;
            
        if (attacker.TFights <= 0)
            return false;
            
        // Check if opponent team can fight
        var opponentMembers = GetActiveCombatMembers(opponentTeam);
        if (opponentMembers.Count == 0)
            return false;
            
        // Load attacking team members
        var attackingMembers = GetActiveCombatMembers(attacker.Team);
        if (attackingMembers.Count == 0)
            return false;
            
        attacker.TFights--;
        
        // Territory control check
        if (turfWar)
        {
            var turfController = GetTurfController();
            if (turfController == null)
            {
                // Easy takeover - no one controls town
                await EasyTownTakeover(attacker, opponentTeam);
                return true;
            }
            
            opponentTeam = turfController.Team;
            opponentMembers = GetActiveCombatMembers(opponentTeam);
            
            if (opponentMembers.Count == 0)
            {
                // Defenders are dead/imprisoned
                await EasyTownTakeover(attacker, opponentTeam);
                return true;
            }
        }
        
        // Prepare for battle
        var attackerTeam = PrepareTeamForBattle(attackingMembers, attacker.Team);
        var defenderTeam = PrepareTeamForBattle(opponentMembers, opponentTeam);
        
        // Battle announcement
        string header = GangWarHeaders[Random.Shared.Next(GangWarHeaders.Length)];
        newsSystem.WriteTeamNews(header,
            $"{GameConfig.NewsColorHighlight}{attacker.Team}{GameConfig.NewsColorDefault} challenged {GameConfig.NewsColorHighlight}{opponentTeam}{GameConfig.NewsColorDefault}!");
        
        // Conduct team battle
        var battleResult = await ConductTeamBattle(attackerTeam, defenderTeam, turfWar);
        
        // Process battle results
        await ProcessBattleResults(battleResult, attacker, opponentTeam, turfWar);
        
        return true;
    }
    
    /// <summary>
    /// Easy town takeover when defenders can't fight - Pascal GANGWARS.PAS functionality
    /// </summary>
    private async Task EasyTownTakeover(Character attacker, string opponentTeam)
    {
        // Set territory control
        SetRemoveTurfFlags(attacker, opponentTeam, 1); // 1 = mail about easy takeover
        
        newsSystem.WriteTeamNews("Gang Takeover!",
            $"{GameConfig.NewsColorHighlight}{attacker.Team}{GameConfig.NewsColorDefault} took over the town without bloodshed.");
            
        newsSystem.Newsy($"{GameConfig.NewsColorPlayer}{attacker.Name2}{GameConfig.NewsColorDefault} led their team to this victory. The old rulers, {GameConfig.NewsColorDeath}{opponentTeam}{GameConfig.NewsColorDefault} were unable to put up a fight.",
            false, GameConfig.NewsCategory.General);
    }
    
    /// <summary>
    /// Set/Remove turf flags - Pascal GANGWARS.PAS SetRemove_TurfFlags procedure
    /// </summary>
    private void SetRemoveTurfFlags(Character winner, string loserTeam, int mailType)
    {
        // Set winner's team flags (only if alive and not imprisoned)
        bool winnerPermaDead = winner is NPC npcWinner && npcWinner.IsDead;
        if (winner.IsAlive && !winnerPermaDead && winner.DaysInPrison <= 0)
        {
            winner.CTurf = true;
            winner.TeamRec = 0; // Reset record counter
        }

        var winningTeam = GetTeamMembers(winner.Team);
        foreach (var member in winningTeam)
        {
            if (member.Name2 != winner.Name2)
            {
                bool memberPermaDead = member is NPC npcW && npcW.IsDead;
                // Only grant CTurf to alive, non-imprisoned members
                if (member.IsAlive && !memberPermaDead && member.DaysInPrison <= 0)
                {
                    member.CTurf = true;
                    member.TeamRec = 0;
                }

                // Only mail alive members
                if (member.IsAlive && !memberPermaDead)
                {
                    string mailSubject = "Town Control!";
                    string mailMessage = mailType == 1
                        ? $"{winner.Name2} led your team to a glorious victory! The opponents were not able to defend the Town. You are in charge now!"
                        : $"{winner.Name2} led your team to a glorious victory! {loserTeam} put up a fight, but was not able to defend their turf. You are in charge now!";

                    MailSystem.SendMail(member.Name2, mailSubject, mailMessage);
                }
            }
        }

        // Remove loser's team flags
        var losingTeam = GetTeamMembers(loserTeam);
        foreach (var member in losingTeam)
        {
            member.CTurf = false;

            // Only mail alive members
            bool loserPermaDead = member is NPC npcL && npcL.IsDead;
            if (member.IsAlive && !loserPermaDead)
            {
                string lossSubject = $"{GameConfig.NewsColorDeath}Lost Town Control!{GameConfig.NewsColorDefault}";
                string lossMessage = mailType == 1
                    ? $"{winner.Name2} led their team to a victory against your gang! Your team was not ready to meet them! The Town is no longer yours..."
                    : $"{winner.Name2} led their team to a victory against your bunch! Your team was not able to fend off the attack! The Town is no longer yours...";

                MailSystem.SendMail(member.Name2, lossSubject, lossMessage);
            }
        }
    }
    
    #endregion
    
    #region Pascal Function Implementation - AUTOGANG.PAS
    
    /// <summary>
    /// Automated gang warfare - Pascal AUTOGANG.PAS Auto_Gangwar procedure
    /// </summary>
    public async Task AutoGangWar(string gang1, string gang2)
    {
        var team1Members = GetActiveCombatMembers(gang1);
        var team2Members = GetActiveCombatMembers(gang2);
        
        if (team1Members.Count == 0 || team2Members.Count == 0)
            return;
            
        // Check for turf war
        bool turfWar = team1Members.Any(m => m.CTurf) || team2Members.Any(m => m.CTurf);
        
        // Battle announcement
        string header = GangWarHeaders[Random.Shared.Next(GangWarHeaders.Length)];
        string announcement = $"{GameConfig.NewsColorHighlight}{gang1}{GameConfig.NewsColorDefault} challenged {GameConfig.NewsColorHighlight}{gang2}{GameConfig.NewsColorDefault}";
        string turfMessage = turfWar ? "A challenge for Town Control!" : "";
        
        newsSystem.Newsy($"{header}: {announcement} {turfMessage}", false, GameConfig.NewsCategory.General);
        
        // Conduct automated battle
        var team1Prepared = PrepareTeamForBattle(team1Members, gang1);
        var team2Prepared = PrepareTeamForBattle(team2Members, gang2);
        
        var battleResult = await ConductAutomatedTeamBattle(team1Prepared, team2Prepared, turfWar);
        
        // Process results
        await ProcessAutoBattleResults(battleResult, gang1, gang2, turfWar);
    }
    
    #endregion
    
    #region Team Battle System
    
    /// <summary>
    /// Prepare team for battle - load and organize members
    /// </summary>
    private List<Character> PrepareTeamForBattle(List<Character> members, string teamName)
    {
        var preparedTeam = new List<Character>();
        
        foreach (var member in members.Take(MaxTeamMembers))
        {
            // Reset HP for battle
            member.HP = member.MaxHP;
            
            // Check inventory and equipment
            CheckInventory(member);
            
            preparedTeam.Add(member);
        }
        
        // Shuffle team for varied battles (Pascal shuffling logic)
        ShuffleTeam(preparedTeam);
        
        return preparedTeam;
    }
    
    /// <summary>
    /// Conduct team vs team battle
    /// </summary>
    private async Task<TeamBattleResult> ConductTeamBattle(List<Character> team1, List<Character> team2, bool turfWar)
    {
        var result = new TeamBattleResult
        {
            Team1 = team1[0].Team,
            Team2 = team2[0].Team,
            TurfWar = turfWar,
            Rounds = new List<BattleRound>()
        };
        
        int round = 1;
        
        while (HasAliveMembersBoth(team1, team2) && round <= 10)
        {
            var roundResult = await ConductBattleRound(team1, team2, round);
            result.Rounds.Add(roundResult);
            
            // Check for battle end conditions
            if (!HasAliveMembers(team1))
            {
                result.Winner = team2[0].Team;
                result.Loser = team1[0].Team;
                break;
            }
            else if (!HasAliveMembers(team2))
            {
                result.Winner = team1[0].Team;
                result.Loser = team2[0].Team;
                break;
            }
            
            round++;
        }
        
        // If no clear winner after max rounds, determine by remaining HP
        if (string.IsNullOrEmpty(result.Winner))
        {
            long team1HP = team1.Sum(m => Math.Max(0, m.HP));
            long team2HP = team2.Sum(m => Math.Max(0, m.HP));
            
            if (team1HP > team2HP)
            {
                result.Winner = team1[0].Team;
                result.Loser = team2[0].Team;
            }
            else
            {
                result.Winner = team2[0].Team;
                result.Loser = team1[0].Team;
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Conduct single battle round between teams
    /// </summary>
    private async Task<BattleRound> ConductBattleRound(List<Character> team1, List<Character> team2, int roundNumber)
    {
        var round = new BattleRound { RoundNumber = roundNumber, Battles = new List<SingleBattle>() };
        
        // Pair up fighters for this round
        var pairs = CreateBattlePairs(team1, team2);
        
        foreach (var pair in pairs)
        {
            if (pair.Fighter1.HP > 0 && pair.Fighter2.HP > 0)
            {
                var battle = await combatEngine.ConductPlayerVsPlayerCombat(pair.Fighter1, pair.Fighter2, false);
                
                round.Battles.Add(new SingleBattle
                {
                    Fighter1 = pair.Fighter1.Name2,
                    Fighter2 = pair.Fighter2.Name2,
                    Winner = battle.Winner?.Name2 ?? "Draw",
                    Damage1 = (int)Math.Max(0, pair.Fighter1.MaxHP - pair.Fighter1.HP),
                    Damage2 = (int)Math.Max(0, pair.Fighter2.MaxHP - pair.Fighter2.HP)
                });
                
                // Update kill statistics
                if (pair.Fighter1.HP <= 0)
                {
                    relationshipSystem.UpdateKillStats(pair.Fighter2, pair.Fighter1);
                }
                else if (pair.Fighter2.HP <= 0)
                {
                    relationshipSystem.UpdateKillStats(pair.Fighter1, pair.Fighter2);
                }
            }
        }
        
        return round;
    }
    
    #endregion
    
    #region Utility Functions
    
    /// <summary>
    /// Get team members who can participate in combat
    /// </summary>
    private List<Character> GetActiveCombatMembers(string teamName)
    {
        var allCharacters = GetAllCharacters();
        return allCharacters.Where(c =>
            c.Team == teamName &&
            c.HP > 0 &&
            c.IsAlive &&
            !(c is NPC npcCheck && npcCheck.IsDead) &&
            c.Allowed &&
            c.Location != GameConfig.OfflineLocationPrison &&
            c.DaysInPrison <= 0 &&
            !c.Deleted).ToList();
    }
    
    /// <summary>
    /// Get character controlling the turf/town
    /// </summary>
    private Character GetTurfController()
    {
        var allCharacters = GetAllCharacters();
        return allCharacters.FirstOrDefault(c => c.CTurf && !string.IsNullOrEmpty(c.Team)
            && c.IsAlive && !(c is NPC npc && npc.IsDead) && c.DaysInPrison <= 0);
    }
    
    /// <summary>
    /// Check if team exists
    /// </summary>
    private bool DoesTeamExist(string teamName)
    {
        var allCharacters = GetAllCharacters();
        return allCharacters.Any(c => c.Team == teamName && !c.Deleted);
    }
    
    /// <summary>
    /// Get team leader (first active member found)
    /// </summary>
    private Character GetTeamLeader(string teamName)
    {
        var allCharacters = GetAllCharacters();
        return allCharacters.FirstOrDefault(c => c.Team == teamName && !c.Deleted);
    }
    
    /// <summary>
    /// Get team member count
    /// </summary>
    private int GetTeamMemberCount(string teamName)
    {
        var allCharacters = GetAllCharacters();
        return allCharacters.Count(c => c.Team == teamName && !c.Deleted);
    }
    
    /// <summary>
    /// Get all team members
    /// </summary>
    private List<Character> GetTeamMembers(string teamName)
    {
        var allCharacters = GetAllCharacters();
        return allCharacters.Where(c => c.Team == teamName && !c.Deleted).ToList();
    }
    
    /// <summary>
    /// Transfer team status from one member to another - Pascal TCORNER.PAS functionality
    /// New joiners inherit CTurf only if their team currently controls the city.
    /// TeamRec starts at 0 for new members.
    /// </summary>
    private void TransferTeamStatus(Character from, Character to)
    {
        to.Team = from.Team;
        to.TeamPW = from.TeamPW;
        // Only grant CTurf if the team actually controls the city right now
        var controllingTeam = CityControlSystem.Instance.GetControllingTeam();
        to.CTurf = !string.IsNullOrEmpty(controllingTeam) && controllingTeam == from.Team;
        to.TeamRec = 0;
    }
    
    /// <summary>
    /// Notify team members of events
    /// </summary>
    private void NotifyTeamMembers(string teamName, string message, string excludePlayer = "")
    {
        var teamMembers = GetTeamMembers(teamName);
        foreach (var member in teamMembers)
        {
            if (member.Name2 != excludePlayer)
            {
                // Send online message if possible, otherwise mail
                MailSystem.SendMail(member.Name2, "Team Update", message);
            }
        }
    }
    
    /// <summary>
    /// Shuffle team for varied battles - Pascal shuffling logic from GANGWARS.PAS
    /// </summary>
    private void ShuffleTeam(List<Character> team)
    {
        for (int i = 0; i < team.Count * 2; i++)
        {
            int x = Random.Shared.Next(team.Count);
            int y = Random.Shared.Next(team.Count);
            if (x != y && x < team.Count && y < team.Count)
            {
                (team[x], team[y]) = (team[y], team[x]);
            }
        }
    }
    
    /// <summary>
    /// Check character inventory for battle - Pascal functionality
    /// Ensures equipment bonuses are properly applied before combat
    /// </summary>
    private void CheckInventory(Character character)
    {
        // Recalculate stats to ensure equipment bonuses are applied
        // This mirrors the Pascal check_inventory that recalculates WeapPow/ArmPow
        character.RecalculateStats();
    }
    
    /// <summary>
    /// Get character by name
    /// </summary>
    private Character GetCharacterByName(string name)
    {
        var allCharacters = GetAllCharacters();
        return allCharacters.FirstOrDefault(c => c.Name2 == name && !c.Deleted);
    }
    
    /// <summary>
    /// Get all characters in the game (NPCs from NPCSpawnSystem + any players)
    /// </summary>
    private List<Character> GetAllCharacters()
    {
        var characters = new List<Character>();

        // Get all active NPCs from NPCSpawnSystem
        var npcs = NPCSpawnSystem.Instance?.ActiveNPCs;
        if (npcs != null)
        {
            characters.AddRange(npcs);
        }


        return characters;
    }
    
    /// <summary>
    /// Check if team has alive members
    /// </summary>
    private bool HasAliveMembers(List<Character> team)
    {
        return team.Any(m => m.HP > 0);
    }
    
    /// <summary>
    /// Check if both teams have alive members
    /// </summary>
    private bool HasAliveMembersBoth(List<Character> team1, List<Character> team2)
    {
        return HasAliveMembers(team1) && HasAliveMembers(team2);
    }
    
    /// <summary>
    /// Create battle pairs for round
    /// </summary>
    private List<BattlePair> CreateBattlePairs(List<Character> team1, List<Character> team2)
    {
        var pairs = new List<BattlePair>();
        var aliveTeam1 = team1.Where(m => m.HP > 0).ToList();
        var aliveTeam2 = team2.Where(m => m.HP > 0).ToList();
        
        int maxPairs = Math.Min(aliveTeam1.Count, aliveTeam2.Count);
        
        for (int i = 0; i < maxPairs; i++)
        {
            pairs.Add(new BattlePair
            {
                Fighter1 = aliveTeam1[i],
                Fighter2 = aliveTeam2[i]
            });
        }
        
        return pairs;
    }
    
    /// <summary>
    /// Conduct automated team battle (for NPC vs NPC)
    /// </summary>
    private async Task<TeamBattleResult> ConductAutomatedTeamBattle(List<Character> team1, List<Character> team2, bool turfWar)
    {
        // Simplified automated battle for NPCs
        var result = new TeamBattleResult
        {
            Team1 = team1[0].Team,
            Team2 = team2[0].Team,
            TurfWar = turfWar
        };

        // Determine winner based on team power
        long team1Power = team1.Sum(m => m.Level + m.Strength + m.Defence);
        long team2Power = team2.Sum(m => m.Level + m.Strength + m.Defence);

        // Add some randomness
        team1Power += Random.Shared.Next(-50, 51);
        team2Power += Random.Shared.Next(-50, 51);
        
        if (team1Power > team2Power)
        {
            result.Winner = team1[0].Team;
            result.Loser = team2[0].Team;
        }
        else
        {
            result.Winner = team2[0].Team;
            result.Loser = team1[0].Team;
        }
        
        return result;
    }
    
    /// <summary>
    /// Process battle results
    /// </summary>
    private async Task ProcessBattleResults(TeamBattleResult result, Character attacker, string defenderTeam, bool turfWar)
    {
        if (result.Winner == attacker.Team)
        {
            // Attacker won
            newsSystem.WriteTeamNews("Gang Victory!",
                $"{GameConfig.NewsColorHighlight}{result.Winner}{GameConfig.NewsColorDefault} defeated {GameConfig.NewsColorDeath}{result.Loser}{GameConfig.NewsColorDefault}!");
            
            if (turfWar)
            {
                SetRemoveTurfFlags(attacker, defenderTeam, 2); // 2 = mail about contested takeover
            }
        }
        else
        {
            // Attacker lost
            newsSystem.WriteTeamNews("Gang Repelled!",
                $"{GameConfig.NewsColorHighlight}{result.Winner}{GameConfig.NewsColorDefault} successfully defended against {GameConfig.NewsColorDeath}{result.Loser}{GameConfig.NewsColorDefault}!");
        }
    }
    
    /// <summary>
    /// Process automated battle results
    /// </summary>
    private async Task ProcessAutoBattleResults(TeamBattleResult result, string gang1, string gang2, bool turfWar)
    {
        newsSystem.WriteTeamNews("Automated Gang Battle!",
            $"{GameConfig.NewsColorHighlight}{result.Winner}{GameConfig.NewsColorDefault} defeated {GameConfig.NewsColorDeath}{result.Loser}{GameConfig.NewsColorDefault} in automated combat!");
        
        if (turfWar)
        {
            // Handle turf transfer in automated battles
            var winningTeam = GetTeamMembers(result.Winner);
            var losingTeam = GetTeamMembers(result.Loser);

            foreach (var member in winningTeam)
            {
                // Only grant CTurf to alive, non-imprisoned members
                bool isPermaDead = member is NPC npcAuto && npcAuto.IsDead;
                if (member.IsAlive && !isPermaDead && member.DaysInPrison <= 0)
                {
                    member.CTurf = true;
                    member.TeamRec = 0;
                }
            }

            foreach (var member in losingTeam)
            {
                member.CTurf = false;
            }
        }
    }
    
    #endregion
    
    #region Data Structures
    
    public class TeamBattleResult
    {
        public string Team1 { get; set; }
        public string Team2 { get; set; }
        public string Winner { get; set; }
        public string Loser { get; set; }
        public bool TurfWar { get; set; }
        public List<BattleRound> Rounds { get; set; } = new List<BattleRound>();
    }
    
    public class BattleRound
    {
        public int RoundNumber { get; set; }
        public List<SingleBattle> Battles { get; set; } = new List<SingleBattle>();
    }
    
    public class SingleBattle
    {
        public string Fighter1 { get; set; }
        public string Fighter2 { get; set; }
        public string Winner { get; set; }
        public int Damage1 { get; set; }
        public int Damage2 { get; set; }
    }
    
    public class BattlePair
    {
        public Character Fighter1 { get; set; }
        public Character Fighter2 { get; set; }
    }
    
    #endregion
} 
