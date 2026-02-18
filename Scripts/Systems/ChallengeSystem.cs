using UsurperRemake.Systems;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Challenge System - Handles dynamic throne and city challenges
/// Every maintenance tick, there's a chance for NPCs to challenge for:
/// - The throne (kingship)
/// - City control (team-based)
///
/// RULES:
/// - King cannot be on a team
/// - City controller cannot also be King
/// - Throne challenges: Fight monsters first, then NPC guards, then King
/// - Losers go to prison
/// </summary>
public class ChallengeSystem
{
    private static ChallengeSystem? instance;
    public static ChallengeSystem Instance => instance ??= new ChallengeSystem();

    private Random random = new();

    // Challenge chances per maintenance tick
    public const float ThroneChallengeProbability = 0.05f;  // 5% per tick
    public const float CityChallengeProbability = 0.08f;    // 8% per tick

    // Prison sentences for failed challengers
    public const int FailedThroneChallengerSentence = 7;    // 7 days
    public const int FailedCityChallengerSentence = 3;      // 3 days

    /// <summary>
    /// Process maintenance tick challenges
    /// Called by WorldSimulator during each maintenance cycle
    /// </summary>
    public void ProcessMaintenanceChallenges()
    {
        // Check for throne challenge
        if (random.NextDouble() < ThroneChallengeProbability)
        {
            ProcessThroneChallenge();
        }

        // Check for city challenge
        if (random.NextDouble() < CityChallengeProbability)
        {
            ProcessCityChallenge();
        }
    }

    /// <summary>
    /// Process an NPC throne challenge
    /// </summary>
    private void ProcessThroneChallenge()
    {
        var king = CastleLocation.GetCurrentKing();
        if (king == null)
        {
            // Empty throne - find someone to claim it
            ClaimEmptyThrone();
            return;
        }

        // In online mode, NPCs cannot challenge a player king through simulation.
        // Player kings can only lose the throne through PvP, siege, or abdication.
        // The simulated combat uses fake stats and would unfairly depose strong player kings.
        if (king.AI == CharacterAI.Human)
        {
            return;
        }

        // Find potential challengers (high-level NPCs not in prison, not King, not story NPCs)
        // Story NPCs like The Stranger cannot become King - they have narrative roles
        var candidates = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => n.IsAlive &&
                   n.Level >= 15 &&
                   n.DaysInPrison <= 0 &&
                   !n.King &&
                   !n.IsStoryNPC &&
                   n.Brain?.Personality?.Ambition > 0.6f)
            .OrderByDescending(n => n.Level + n.Strength)
            .Take(5)
            .ToList();

        if (candidates == null || candidates.Count == 0)
        {
            // GD.Print("[Challenge] No suitable throne challengers found");
            return;
        }

        // Pick a random challenger from candidates
        var challenger = candidates[random.Next(candidates.Count)];

        // RULE: Challenger must leave team to challenge for throne
        if (!string.IsNullOrEmpty(challenger.Team))
        {
            // Will they leave their team for the throne?
            if (challenger.Brain?.Personality?.Ambition < 0.8f)
            {
                // GD.Print($"[Challenge] {challenger.Name} chose to stay with their team");
                return;
            }

            CityControlSystem.Instance.ForceLeaveTeam(challenger);
        }

        NewsSystem.Instance?.Newsy(true, $"{challenger.Name} has challenged {king.GetTitle()} {king.Name} for the throne!");
        // GD.Print($"[Challenge] {challenger.Name} challenges for the throne!");

        // Check if there are any human guards who need to be notified
        bool hasHumanGuards = king.Guards.Any(g => g.AI == CharacterAI.Human);

        if (hasHumanGuards && king.ActiveDefenseEvent == null)
        {
            // Create a pending defense event - combat is delayed to give human guards time to respond
            king.ActiveDefenseEvent = new PendingDefenseEvent
            {
                ChallengerName = challenger.Name,
                ChallengerLevel = challenger.Level,
                ChallengerTeam = challenger.Team ?? "",
                EventTime = DateTime.Now,
                TicksRemaining = 2  // Combat delayed for 2 ticks
            };

            NewsSystem.Instance?.Newsy(true, $"URGENT: Royal Guards are being summoned to defend the throne!");
            // GD.Print($"[Challenge] Defense event created - human guards will be notified");
            return;  // Don't process combat yet
        }

        // Process any pending defense event
        if (king.ActiveDefenseEvent != null)
        {
            king.ActiveDefenseEvent.TicksRemaining--;

            if (!king.ActiveDefenseEvent.IsExpired)
            {
                // Still waiting for human guards
                NewsSystem.Instance?.Newsy(false, $"{challenger.Name} awaits at the castle gates...");
                return;
            }

            // Time's up - process the challenge
            // Penalize human guards who didn't respond
            foreach (var guard in king.Guards.Where(g => g.AI == CharacterAI.Human))
            {
                if (!king.ActiveDefenseEvent.PlayerResponded)
                {
                    guard.Loyalty = Math.Max(0, guard.Loyalty - 15);
                    NewsSystem.Instance?.Newsy(false, $"Guard {guard.Name}'s loyalty questioned for failing to defend the throne!");
                }
            }

            // Clear the event
            king.ActiveDefenseEvent = null;
        }

        // Fight sequence: Monsters -> NPC Guards -> King
        bool success = SimulateThroneChallenge(challenger, king);

        if (success)
        {
            // New King!
            CrownNewKing(challenger, king);
        }
        else
        {
            // Failed - go to prison
            ImprisonChallenger(challenger, FailedThroneChallengerSentence, "Failed throne challenge");
        }
    }

    /// <summary>
    /// Simulate a throne challenge fight sequence
    /// Returns true if challenger wins
    /// </summary>
    private bool SimulateThroneChallenge(NPC challenger, King king)
    {
        long challengerHP = challenger.MaxHP;
        long challengerPower = challenger.Strength + challenger.WeapPow;
        long challengerDefence = challenger.Defence + challenger.ArmPow;

        // Phase 1: Fight monster guards
        foreach (var monster in king.MonsterGuards.ToList())
        {
            if (challengerHP <= 0) break;

            // GD.Print($"[Challenge] {challenger.Name} fights monster guard {monster.Name}");

            // Simulate combat
            while (challengerHP > 0 && monster.HP > 0)
            {
                // Challenger attacks
                long damage = Math.Max(1, challengerPower - monster.Defence);
                damage += random.Next(1, (int)Math.Max(2, challenger.WeapPow / 3));
                monster.HP -= damage;

                if (monster.HP <= 0) break;

                // Monster attacks
                long monsterDamage = Math.Max(1, monster.Strength + monster.WeapPow - challengerDefence);
                monsterDamage += random.Next(1, (int)Math.Max(2, monster.WeapPow / 3));
                challengerHP -= monsterDamage;
            }

            if (monster.HP <= 0)
            {
                king.MonsterGuards.Remove(monster);
                NewsSystem.Instance?.Newsy(true, $"{challenger.Name} slew the monster guard {monster.Name}!");
            }
        }

        if (challengerHP <= 0)
        {
            NewsSystem.Instance?.Newsy(true, $"{challenger.Name} was defeated by the monster guards!");
            return false;
        }

        // Phase 2: Fight NPC guards
        foreach (var guard in king.Guards.ToList())
        {
            if (challengerHP <= 0) break;

            // GD.Print($"[Challenge] {challenger.Name} fights guard {guard.Name}");

            // Skip human guards who aren't participating (handled separately)
            if (guard.AI == CharacterAI.Human)
            {
                continue;  // Human guards defend interactively, not in simulation
            }

            // Check for low loyalty desertion/betrayal
            if (guard.Loyalty < 30 && random.Next(100) < 30)
            {
                king.Guards.Remove(guard);
                NewsSystem.Instance?.Newsy(true, $"Cowardly guard {guard.Name} fled instead of fighting!");
                continue;
            }

            // Very low loyalty - betrayal (guard joins challenger)
            if (guard.Loyalty < 15 && random.Next(100) < 20)
            {
                king.Guards.Remove(guard);
                NewsSystem.Instance?.Newsy(true, $"BETRAYAL! Guard {guard.Name} has joined {challenger.Name}'s cause!");
                challengerHP += 100;  // Boost from having an ally
                continue;
            }

            // Look up actual NPC stats if available, otherwise use scaled defaults
            var actualNpc = NPCSpawnSystem.Instance?.GetNPCByName(guard.Name);
            long guardStr = actualNpc?.Strength ?? (50 + random.Next(50));
            long guardHP = actualNpc?.HP ?? (200 + random.Next(200));
            long guardDef = actualNpc?.Defence ?? 20;

            // Apply loyalty modifier to guard effectiveness (loyal guards fight harder)
            float loyaltyMod = guard.Loyalty / 100f;
            guardStr = (long)(guardStr * (0.5f + loyaltyMod * 0.5f));  // 50-100% effectiveness

            while (challengerHP > 0 && guardHP > 0)
            {
                // Challenger attacks
                long damage = Math.Max(1, challengerPower - guardDef);
                guardHP -= damage;

                if (guardHP <= 0) break;

                // Guard attacks
                long guardDamage = Math.Max(1, guardStr - challengerDefence);
                challengerHP -= guardDamage;
            }

            if (guardHP <= 0)
            {
                king.Guards.Remove(guard);
                NewsSystem.Instance?.Newsy(true, $"{challenger.Name} defeated guard {guard.Name}!");
            }
        }

        if (challengerHP <= 0)
        {
            NewsSystem.Instance?.Newsy(true, $"{challenger.Name} was defeated by the Royal Guards!");
            return false;
        }

        // Phase 3: Fight the King
        // GD.Print($"[Challenge] {challenger.Name} fights {king.GetTitle()} {king.Name}!");

        int kingStr = 100 + random.Next(100);
        int kingHP = 500 + random.Next(500);

        while (challengerHP > 0 && kingHP > 0)
        {
            // Challenger attacks (king uses full defense, not reduced)
            long damage = Math.Max(1, challengerPower - kingStr);
            damage += random.Next(1, (int)Math.Max(2, challenger.WeapPow / 2));
            kingHP -= (int)damage;

            if (kingHP <= 0) break;

            // King attacks
            long kingDamage = Math.Max(1, kingStr - challengerDefence);
            kingDamage += random.Next(10, 30);
            challengerHP -= kingDamage;
        }

        if (kingHP <= 0)
        {
            NewsSystem.Instance?.Newsy(true,
                $"{challenger.Name} has DEFEATED {king.GetTitle()} {king.Name} in combat!");
            return true;
        }
        else
        {
            NewsSystem.Instance?.Newsy(true,
                $"{challenger.Name} was defeated by {king.GetTitle()} {king.Name}!");
            return false;
        }
    }

    /// <summary>
    /// Crown a new King after successful challenge
    /// </summary>
    private void CrownNewKing(NPC newKing, King oldKing)
    {
        // RULE: New King cannot be on a team
        if (!string.IsNullOrEmpty(newKing.Team))
        {
            CityControlSystem.Instance.ForceLeaveTeam(newKing);
        }

        // Mark as King
        newKing.King = true;

        // Create new king data
        var kingData = King.CreateNewKing(newKing.Name, CharacterAI.Computer, newKing.Sex);
        kingData.Treasury = oldKing.Treasury / 2; // Inherits half the treasury
        kingData.TaxRate = oldKing.TaxRate;
        kingData.CityTaxPercent = oldKing.CityTaxPercent;

        // Set as current king
        CastleLocation.SetKing(kingData);

        // Find and unmark old king NPC if they exist
        var oldKingNPC = NPCSpawnSystem.Instance?.ActiveNPCs?
            .FirstOrDefault(n => n.Name == oldKing.Name);
        if (oldKingNPC != null)
        {
            oldKingNPC.King = false;
            // Old king goes to prison or flees
            ImprisonChallenger(oldKingNPC, 14, "Deposed monarch");
        }

        // If the old king was the player, clear their King flag too
        var player = GameEngine.Instance?.CurrentPlayer;
        if (player != null && player.King &&
            (player.DisplayName == oldKing.Name || player.Name2 == oldKing.Name))
        {
            player.King = false;
            GD.Print($"[ChallengeSystem] Player {player.DisplayName} deposed from throne by {newKing.Name}");
        }

        NewsSystem.Instance?.Newsy(true,
            $"ALL HAIL {kingData.GetTitle()} {newKing.Name}! A new monarch sits upon the throne!");

        // GD.Print($"[Challenge] {newKing.Name} crowned as new {kingData.GetTitle()}");
    }

    /// <summary>
    /// Claim an empty throne
    /// </summary>
    private void ClaimEmptyThrone()
    {
        // Find highest level NPC not in a team (excluding story NPCs)
        var candidates = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => n.IsAlive &&
                   n.Level >= 10 &&
                   string.IsNullOrEmpty(n.Team) &&
                   n.DaysInPrison <= 0 &&
                   !n.IsStoryNPC)
            .OrderByDescending(n => n.Level + n.Charisma)
            .Take(3)
            .ToList();

        if (candidates == null || candidates.Count == 0)
        {
            // Try someone in a team who will leave (still excluding story NPCs)
            candidates = NPCSpawnSystem.Instance?.ActiveNPCs?
                .Where(n => n.IsAlive &&
                       n.Level >= 10 &&
                       n.DaysInPrison <= 0 &&
                       !n.IsStoryNPC &&
                       n.Brain?.Personality?.Ambition > 0.7f)
                .OrderByDescending(n => n.Level)
                .Take(3)
                .ToList();
        }

        if (candidates == null || candidates.Count == 0)
        {
            // GD.Print("[Challenge] No one available to claim the empty throne");
            return;
        }

        var newKing = candidates[0];

        // Must leave team to become King
        if (!string.IsNullOrEmpty(newKing.Team))
        {
            CityControlSystem.Instance.ForceLeaveTeam(newKing);
        }

        newKing.King = true;

        var kingData = King.CreateNewKing(newKing.Name, CharacterAI.Computer, newKing.Sex);
        kingData.Treasury = random.Next(5000, 20000);
        kingData.TaxRate = random.Next(10, 30);

        CastleLocation.SetKing(kingData);

        NewsSystem.Instance?.Newsy(true,
            $"{newKing.Name} has claimed the empty throne! ALL HAIL {kingData.GetTitle()} {newKing.Name}!");

        // GD.Print($"[Challenge] {newKing.Name} claimed empty throne");
    }

    /// <summary>
    /// Process a city control challenge between teams
    /// </summary>
    private void ProcessCityChallenge()
    {
        var currentControllers = CityControlSystem.Instance.GetCityControllers();
        var controllingTeam = CityControlSystem.Instance.GetControllingTeam();

        // Get all teams
        var teams = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => !string.IsNullOrEmpty(n.Team) && n.IsAlive)
            .GroupBy(n => n.Team)
            .Where(g => g.Count() >= 2) // Only teams with 2+ members
            .ToList();

        if (teams == null || teams.Count < 2)
        {
            // GD.Print("[Challenge] Not enough teams for city challenge");
            return;
        }

        // Find a challenging team (not the current controller)
        var challengingTeams = teams
            .Where(t => t.Key != controllingTeam)
            .OrderByDescending(t => t.Sum(m => m.Level + m.Strength))
            .ToList();

        if (challengingTeams.Count == 0)
        {
            return;
        }

        // Pick a random challenger from top contenders
        var challengerGroup = challengingTeams[random.Next(Math.Min(3, challengingTeams.Count))];
        string challengerTeamName = challengerGroup.Key;

        // RULE: Check if team leader is King (invalid)
        var teamLeader = challengerGroup.OrderByDescending(n => n.Level).First();
        var king = CastleLocation.GetCurrentKing();
        if (king != null && king.Name == teamLeader.Name)
        {
            // GD.Print("[Challenge] Team's leader is King - cannot challenge for city");
            return;
        }

        NewsSystem.Instance?.Newsy(true,
            $"'{challengerTeamName}' is challenging for city control!");

        // GD.Print($"[Challenge] Team '{challengerTeamName}' challenges for city control");

        // Use CityControlSystem's challenge logic
        bool success = CityControlSystem.Instance.ChallengeForCityControl(teamLeader, challengerTeamName);

        if (!success && random.NextDouble() < 0.3) // 30% chance losers go to prison
        {
            // Imprison the team leader briefly
            ImprisonChallenger(teamLeader as NPC, FailedCityChallengerSentence, "Failed city takeover");
        }
    }

    /// <summary>
    /// Imprison a failed challenger
    /// </summary>
    private void ImprisonChallenger(NPC? npc, int days, string crime)
    {
        if (npc == null) return;

        npc.DaysInPrison = (byte)Math.Min(255, days);
        npc.CurrentLocation = "Prison";
        npc.CellDoorOpen = false;

        // Also add to King's prison record if there's a King
        var king = CastleLocation.GetCurrentKing();
        king?.ImprisonCharacter(npc.Name, days, crime);

        NewsSystem.Instance?.Newsy(true, $"{npc.Name} was thrown in prison for {days} days!");
        // GD.Print($"[Challenge] {npc.Name} imprisoned for {days} days: {crime}");
    }

    /// <summary>
    /// Allow a player to challenge for the throne
    /// Returns a detailed result of the challenge attempt
    /// </summary>
    public async Task<ThroneChallengeResult> PlayerThroneChallenge(Player player)
    {
        var result = new ThroneChallengeResult();
        var king = CastleLocation.GetCurrentKing();

        if (king == null)
        {
            result.Success = true;
            result.Message = "The throne was empty - you have claimed it!";
            return result;
        }

        // RULE: Player must leave team
        if (!string.IsNullOrEmpty(player.Team))
        {
            result.HadToLeaveTeam = true;
            player.Team = "";
            player.TeamPW = "";
            player.CTurf = false;
            result.Message = "You have left your team to pursue the crown. ";
        }

        result.FoughtMonsters = king.MonsterGuards.Count;
        result.FoughtGuards = king.Guards.Count;

        // The actual fight sequence is handled by CastleLocation
        // This method just validates the rules

        await Task.CompletedTask;
        return result;
    }

    /// <summary>
    /// Result of a throne challenge attempt
    /// </summary>
    public class ThroneChallengeResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool HadToLeaveTeam { get; set; }
        public int FoughtMonsters { get; set; }
        public int FoughtGuards { get; set; }
        public int DamageDealt { get; set; }
        public int DamageTaken { get; set; }
    }
}
