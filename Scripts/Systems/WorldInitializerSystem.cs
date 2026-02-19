using UsurperRemake.Utils;
using UsurperRemake.Systems;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// World Initializer System - Creates a living world with history before the player arrives
/// Runs a fast-forward simulation of 100+ days to establish:
/// - Teams and team hierarchies
/// - A King/Queen on the throne
/// - A team controlling the city (different from King)
/// - NPCs in guard positions
/// - Historical events, deaths, and replacements
/// </summary>
public class WorldInitializerSystem
{
    private static WorldInitializerSystem? instance;
    public static WorldInitializerSystem Instance => instance ??= new WorldInitializerSystem();

    private Random random = new();
    private bool worldInitialized = false;
    private List<string> worldHistory = new();

    // Ocean/Manwe themed team names for lore-friendly world
    public static readonly string[] OceanTeamNamesPrefixes = new[]
    {
        "The Tidal", "The Azure", "The Storm", "The Deep", "The Salt",
        "The Wave", "The Coral", "The Tempest", "The Abyssal", "The Pearl",
        "The Manwe", "The Seafoam", "The Riptide", "The Trident", "The Nautical",
        "The Leviathan", "The Kraken", "The Siren", "The Maritime", "The Oceanic"
    };

    public static readonly string[] OceanTeamNamesSuffixes = new[]
    {
        "Tide", "Current", "Mariners", "Sailors", "Corsairs",
        "Navigators", "Voyagers", "Depths", "Wanderers", "Brotherhood",
        "Covenant", "Order", "Guild", "Company", "Alliance",
        "Conclave", "Fellowship", "Syndicate", "Circle", "Legion"
    };

    // Team structure tracking
    public class TeamRecord
    {
        public string Name { get; set; } = "";
        public string FounderName { get; set; } = "";
        public int DayFounded { get; set; }
        public List<string> MemberNames { get; set; } = new();
        public bool ControlsCity { get; set; }
        public int TotalPower => MemberNames.Count; // Simplified
    }

    public List<TeamRecord> ActiveTeams { get; private set; } = new();
    public List<string> DeadNPCNames { get; private set; } = new();
    public int SimulatedDays { get; private set; } = 0;

    /// <summary>
    /// Initialize the world with simulated history
    /// </summary>
    public async Task InitializeWorld(int daysToSimulate = 100)
    {
        if (worldInitialized)
        {
            // GD.Print("[WorldInit] World already initialized");
            return;
        }

        // GD.Print($"[WorldInit] Simulating {daysToSimulate} days of world history...");

        var npcs = NPCSpawnSystem.Instance.ActiveNPCs;
        if (npcs == null || npcs.Count == 0)
        {
            GD.PrintErr("[WorldInit] No NPCs available for world simulation!");
            return;
        }

        // Phase 1: Form initial teams (Days 1-20)
        await SimulateTeamFormation(npcs, Math.Min(20, daysToSimulate / 5));

        // Phase 2: Establish initial kingship (Days 21-30)
        await SimulateKingshipEstablishment(npcs);

        // Phase 3: City control competition (Days 31-50)
        await SimulateCityControlCompetition(npcs);

        // Phase 4: Guard recruitment (Days 51-60)
        await SimulateGuardRecruitment(npcs);

        // Phase 5: General world simulation (remaining days)
        int remainingDays = daysToSimulate - 60;
        if (remainingDays > 0)
        {
            await SimulateWorldActivity(npcs, remainingDays);
        }

        // Phase 6: Generate historical news/events
        GenerateHistoricalNews();

        SimulatedDays = daysToSimulate;
        worldInitialized = true;

        // GD.Print($"[WorldInit] World initialization complete! {ActiveTeams.Count} teams, King established, city controlled");
        // GD.Print($"[WorldInit] {DeadNPCNames.Count} NPCs died during history, replaced with new adventurers");
    }

    /// <summary>
    /// Phase 1: Simulate team formation over initial days
    /// </summary>
    private async Task SimulateTeamFormation(List<NPC> npcs, int days)
    {
        // GD.Print($"[WorldInit] Simulating team formation ({days} days)...");

        int targetTeams = 5; // Create 5 teams initially
        int teamsFormed = 0;

        // Get NPCs who are natural leaders (high ambition, gang-prone)
        // Exclude story NPCs - they have special narrative roles and shouldn't lead teams
        var potentialLeaders = npcs
            .Where(n => n.IsAlive &&
                   string.IsNullOrEmpty(n.Team) &&
                   n.Level >= 5 &&
                   !n.IsStoryNPC &&
                   (n.Brain?.Personality?.Ambition > 0.5f ||
                    n.Brain?.Personality?.IsLikelyToJoinGang() == true))
            .OrderByDescending(n => n.Level + n.Charisma)
            .Take(10)
            .ToList();

        foreach (var leader in potentialLeaders)
        {
            if (teamsFormed >= targetTeams) break;
            if (!string.IsNullOrEmpty(leader.Team)) continue;

            // Form a new team with lore-friendly name
            string teamName = GenerateLoreFriendlyTeamName();
            string password = Guid.NewGuid().ToString().Substring(0, 8);

            leader.Team = teamName;
            leader.TeamPW = password;
            leader.CTurf = false;
            leader.TeamRec = 0;

            var team = new TeamRecord
            {
                Name = teamName,
                FounderName = leader.Name,
                DayFounded = random.Next(1, days + 1),
                MemberNames = new List<string> { leader.Name }
            };

            // Recruit 1-4 additional members (exclude story NPCs)
            int membersToRecruit = random.Next(1, 5);
            var recruits = npcs
                .Where(n => n.IsAlive &&
                       string.IsNullOrEmpty(n.Team) &&
                       n.Id != leader.Id &&
                       !n.IsStoryNPC &&
                       n.Brain?.Personality?.IsLikelyToJoinGang() == true)
                .OrderBy(_ => random.Next())
                .Take(membersToRecruit)
                .ToList();

            foreach (var recruit in recruits)
            {
                recruit.Team = teamName;
                recruit.TeamPW = password;
                recruit.CTurf = false;
                team.MemberNames.Add(recruit.Name);
            }

            ActiveTeams.Add(team);
            teamsFormed++;

            worldHistory.Add($"Day {team.DayFounded}: {leader.Name} founded '{teamName}' with {team.MemberNames.Count - 1} followers");
            // GD.Print($"[WorldInit] Team '{teamName}' founded by {leader.Name} with {team.MemberNames.Count} members");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Phase 2: Establish an NPC as King/Queen
    /// IMPORTANT: King cannot be on a team - must leave team to take throne
    /// </summary>
    private async Task SimulateKingshipEstablishment(List<NPC> npcs)
    {
        // GD.Print("[WorldInit] Establishing initial monarch...");

        // Find the most powerful NPC to become King
        // IMPORTANT: Exclude story NPCs (they have narrative roles and cannot become King)
        // Preference for NPCs NOT in teams (as per rules), but will make them leave if needed
        var candidates = npcs
            .Where(n => n.IsAlive && n.Level >= 10 && !n.IsStoryNPC)
            .OrderByDescending(n => n.Level * 2 + n.Strength + n.Charisma + n.Gold / 1000)
            .Take(5)
            .ToList();

        if (candidates.Count == 0)
        {
            // Fallback to any high-level NPC that isn't a story NPC
            candidates = npcs.Where(n => n.IsAlive && !n.IsStoryNPC).OrderByDescending(n => n.Level).Take(1).ToList();
        }

        var newKing = candidates.First();

        // RULE: King cannot be on a team - must abandon team first
        if (!string.IsNullOrEmpty(newKing.Team))
        {
            string oldTeam = newKing.Team;
            worldHistory.Add($"Day 25: {newKing.Name} left '{oldTeam}' to pursue the throne");

            // Remove from team
            var team = ActiveTeams.FirstOrDefault(t => t.Name == oldTeam);
            if (team != null)
            {
                team.MemberNames.Remove(newKing.Name);
            }

            newKing.Team = "";
            newKing.TeamPW = "";
            newKing.CTurf = false;
        }

        // Crown the new monarch
        newKing.King = true;
        var kingData = King.CreateNewKing(newKing.Name, CharacterAI.Computer, newKing.Sex);
        kingData.Treasury = random.Next(10000, 50000);
        kingData.TaxRate = random.Next(10, 50);
        kingData.TotalReign = random.Next(20, 50); // Already ruled for some days

        // Set the king in CastleLocation
        CastleLocation.SetKing(kingData);

        worldHistory.Add($"Day 25: {kingData.GetTitle()} {newKing.Name} claimed the throne!");
        // GD.Print($"[WorldInit] {kingData.GetTitle()} {newKing.Name} established as monarch");

        await Task.CompletedTask;
    }

    /// <summary>
    /// Phase 3: One team gains city control (must be different from King)
    /// </summary>
    private async Task SimulateCityControlCompetition(List<NPC> npcs)
    {
        // GD.Print("[WorldInit] Simulating city control competition...");

        var king = CastleLocation.GetCurrentKing();
        string kingName = king?.Name ?? "";

        // Find strongest team (that doesn't include the King)
        var eligibleTeams = ActiveTeams
            .Where(t => !t.MemberNames.Contains(kingName))
            .OrderByDescending(t => t.MemberNames.Count)
            .ToList();

        if (eligibleTeams.Count == 0)
        {
            // GD.Print("[WorldInit] No eligible teams for city control");
            return;
        }

        // Calculate team power based on member stats
        TeamRecord? strongestTeam = null;
        long highestPower = 0;

        foreach (var team in eligibleTeams)
        {
            long teamPower = 0;
            foreach (var memberName in team.MemberNames)
            {
                var member = npcs.FirstOrDefault(n => n.Name == memberName);
                if (member != null && member.IsAlive)
                {
                    teamPower += member.Level + member.Strength + member.Defence;
                }
            }

            if (teamPower > highestPower)
            {
                highestPower = teamPower;
                strongestTeam = team;
            }
        }

        if (strongestTeam != null)
        {
            strongestTeam.ControlsCity = true;

            // Set CTurf flag for all team members
            foreach (var memberName in strongestTeam.MemberNames)
            {
                var member = npcs.FirstOrDefault(n => n.Name == memberName);
                if (member != null)
                {
                    member.CTurf = true;
                    member.TeamRec = 0;
                }
            }

            worldHistory.Add($"Day 40: '{strongestTeam.Name}' seized control of the city!");
            // GD.Print($"[WorldInit] Team '{strongestTeam.Name}' controls the city");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Phase 4: Recruit NPCs as guards (both royal and bank)
    /// </summary>
    private async Task SimulateGuardRecruitment(List<NPC> npcs)
    {
        // GD.Print("[WorldInit] Recruiting guards...");

        var king = CastleLocation.GetCurrentKing();
        if (king == null) return;

        // Recruit 2-4 royal guards (NPCs not on teams)
        var guardCandidates = npcs
            .Where(n => n.IsAlive &&
                   string.IsNullOrEmpty(n.Team) &&
                   !n.King &&
                   n.Level >= 5 &&
                   n.Darkness < 200) // Not too evil for royal service
            .OrderByDescending(n => n.Level + n.Strength)
            .Take(5)
            .ToList();

        int guardsHired = 0;
        foreach (var candidate in guardCandidates)
        {
            if (guardsHired >= 3) break;

            string guardTitle = candidate.Sex == CharacterSex.Female ? "Dame" : "Sir";
            string guardName = $"{guardTitle} {candidate.Name}";

            if (king.AddGuard(guardName, CharacterAI.Computer, candidate.Sex, GameConfig.BaseGuardSalary))
            {
                guardsHired++;
                worldHistory.Add($"Day {50 + guardsHired}: {candidate.Name} joined the Royal Guard as {guardName}");
            }
        }

        // Recruit bank guards
        var bankGuardCandidates = npcs
            .Where(n => n.IsAlive &&
                   !n.BankGuard &&
                   n.Level >= 5 &&
                   n.Darkness <= 100)
            .OrderBy(_ => random.Next())
            .Take(3)
            .ToList();

        foreach (var candidate in bankGuardCandidates)
        {
            candidate.BankGuard = true;
            candidate.BankWage = 1000 + (candidate.Level * 50);
            worldHistory.Add($"Day {55}: {candidate.Name} hired as bank guard");
        }

        // GD.Print($"[WorldInit] {guardsHired} royal guards and {bankGuardCandidates.Count} bank guards hired");

        await Task.CompletedTask;
    }

    /// <summary>
    /// Phase 5: General world simulation - combat, leveling, deaths, replacements
    /// </summary>
    private async Task SimulateWorldActivity(List<NPC> npcs, int days)
    {
        // GD.Print($"[WorldInit] Simulating {days} days of world activity...");

        for (int day = 0; day < days; day++)
        {
            // Each day, NPCs have a chance to do activities
            foreach (var npc in npcs.Where(n => n.IsAlive).ToList())
            {
                // 20% chance of activity per day
                if (random.NextDouble() > 0.2) continue;

                // Determine activity
                double roll = random.NextDouble();

                if (roll < 0.4)
                {
                    // Dungeon exploration - chance of XP gain or death
                    SimulateNPCDungeonRun(npc, 60 + day);
                }
                else if (roll < 0.6)
                {
                    // Training - stat improvement
                    SimulateNPCTraining(npc);
                }
                else if (roll < 0.8)
                {
                    // Check for level up
                    CheckNPCLevelUp(npc, 60 + day);
                }
                else
                {
                    // Team activity
                    if (!string.IsNullOrEmpty(npc.Team))
                    {
                        SimulateTeamActivity(npc, 60 + day);
                    }
                }
            }

            // Replace dead NPCs periodically
            if (day % 10 == 0 && DeadNPCNames.Count > 0)
            {
                ReplaceDeadNPCs();
            }
        }

        await Task.CompletedTask;
    }

    private void SimulateNPCDungeonRun(NPC npc, int day)
    {
        // Simulate dungeon encounter
        int dungeonLevel = Math.Max(1, npc.Level + random.Next(-3, 4));

        // Generate monster power for this level
        long monsterPower = (long)(40 * dungeonLevel + Math.Pow(dungeonLevel, 1.2) * 15);
        long npcPower = npc.HP + npc.Strength + npc.WeapPow + npc.ArmPow;

        // Determine outcome
        double winChance = (double)npcPower / (npcPower + monsterPower);
        winChance = Math.Max(0.1, Math.Min(0.95, winChance)); // Clamp between 10-95%

        if (random.NextDouble() < winChance)
        {
            // Victory - gain XP and gold
            long xpGain = (long)(Math.Pow(dungeonLevel, 1.5) * 15);
            long goldGain = random.Next(dungeonLevel * 10, dungeonLevel * 50);

            npc.GainExperience(xpGain);
            npc.GainGold(goldGain);

            // Small chance of notable victory
            if (random.NextDouble() < 0.05)
            {
                worldHistory.Add($"Day {day}: {npc.Name} conquered dungeon level {dungeonLevel}");
            }
        }
        else
        {
            // Defeat - take damage, small chance of death
            long damage = random.Next((int)(npc.MaxHP / 4), (int)(npc.MaxHP / 2));
            npc.HP -= damage;

            if (npc.HP <= 0)
            {
                // NPC died
                npc.HP = 0;
                DeadNPCNames.Add(npc.Name);
                worldHistory.Add($"Day {day}: {npc.Name} was slain in the dungeon");

                // Remove from team
                if (!string.IsNullOrEmpty(npc.Team))
                {
                    var team = ActiveTeams.FirstOrDefault(t => t.Name == npc.Team);
                    team?.MemberNames.Remove(npc.Name);
                }
            }
            else
            {
                // Retreated wounded
                npc.HP = Math.Max(1, npc.HP);
            }
        }
    }

    private void SimulateNPCTraining(NPC npc)
    {
        // Small stat gain from training
        int statChoice = random.Next(4);
        switch (statChoice)
        {
            case 0: npc.Strength++; break;
            case 1: npc.Defence++; break;
            case 2: npc.Agility++; break;
            case 3: npc.MaxHP += 5; npc.HP = Math.Min(npc.HP + 5, npc.MaxHP); break;
        }
    }

    private void CheckNPCLevelUp(NPC npc, int day)
    {
        long expForNextLevel = GetExperienceForLevel(npc.Level + 1);
        if (npc.Experience >= expForNextLevel && npc.Level < 100)
        {
            npc.Level++;
            npc.MaxHP += 10 + random.Next(5, 15);
            npc.HP = npc.MaxHP;
            npc.Strength += random.Next(1, 3);
            npc.Defence += random.Next(1, 2);

            if (npc.Level % 10 == 0)
            {
                worldHistory.Add($"Day {day}: {npc.Name} achieved Level {npc.Level}");
            }
        }
    }

    private void SimulateTeamActivity(NPC npc, int day)
    {
        // Small chance of team-related events
        if (random.NextDouble() < 0.02) // 2% chance
        {
            // Team recruitment
            var teamMembers = NPCSpawnSystem.Instance.ActiveNPCs
                .Where(n => n.Team == npc.Team && n.IsAlive)
                .ToList();

            if (teamMembers.Count < 5)
            {
                // Try to recruit
                var candidate = NPCSpawnSystem.Instance.ActiveNPCs
                    .Where(n => n.IsAlive && string.IsNullOrEmpty(n.Team))
                    .OrderBy(_ => random.Next())
                    .FirstOrDefault();

                if (candidate != null && random.NextDouble() < 0.5)
                {
                    candidate.Team = npc.Team;
                    candidate.TeamPW = npc.TeamPW;
                    candidate.CTurf = npc.CTurf;

                    var team = ActiveTeams.FirstOrDefault(t => t.Name == npc.Team);
                    team?.MemberNames.Add(candidate.Name);

                    worldHistory.Add($"Day {day}: {candidate.Name} joined '{npc.Team}'");
                }
            }
        }
    }

    private void ReplaceDeadNPCs()
    {
        // Create new low-level NPCs to replace the dead
        int toReplace = Math.Min(DeadNPCNames.Count, 3);

        for (int i = 0; i < toReplace; i++)
        {
            var newNPC = CreateReplacementNPC();
            if (newNPC != null)
            {
                NPCSpawnSystem.Instance.ActiveNPCs.Add(newNPC);
                worldHistory.Add($"A new adventurer named {newNPC.Name} arrived in town");
            }
        }

        // Clear the processed dead names
        if (toReplace > 0)
        {
            DeadNPCNames.RemoveRange(0, Math.Min(toReplace, DeadNPCNames.Count));
        }
    }

    private NPC? CreateReplacementNPC()
    {
        // Gender-appropriate first names
        string[] maleFirstNames = { "Marcus", "Torval", "Bran", "Theron", "Aldric", "Gareth", "Roland", "Viktor", "Cedric", "Brennan" };
        string[] femaleFirstNames = { "Elena", "Lyra", "Mira", "Kira", "Sela", "Rowena", "Astrid", "Freya", "Isolde", "Gwyneth" };
        string[] lastNames = { "Stormwind", "Darkwater", "Ironforge", "Swiftblade", "Thornwood", "Shadowmere", "Brightblade", "Duskwalker" };

        // Pick gender first, then appropriate name
        CharacterSex sex = random.Next(2) == 0 ? CharacterSex.Male : CharacterSex.Female;
        string firstName = sex == CharacterSex.Male
            ? maleFirstNames[random.Next(maleFirstNames.Length)]
            : femaleFirstNames[random.Next(femaleFirstNames.Length)];
        string name = $"{firstName} {lastNames[random.Next(lastNames.Length)]}";

        // Start at level 1-3
        int level = random.Next(1, 4);

        var npc = new NPC
        {
            Name1 = name,
            Name2 = name,
            ID = $"npc_{name.ToLower().Replace(" ", "_")}_{random.Next(1000)}",
            Class = (CharacterClass)random.Next(1, 8),
            Race = CharacterRace.Human,
            Level = level,
            Age = random.Next(18, 35),
            Sex = sex,
            AI = CharacterAI.Computer,
            HP = 50 + level * 20,
            MaxHP = 50 + level * 20,
            Strength = 10 + level * 3,
            Defence = 8 + level * 2,
            Gold = random.Next(100, 500),
            CurrentLocation = "Main Street"
        };

        // Give basic personality with generated romance traits
        var profile = PersonalityProfile.GenerateForArchetype("adventurer");
        // Set Gender based on NPC's sex
        profile.Gender = sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male;
        npc.Personality = profile;
        npc.Brain = new NPCBrain(npc, profile);

        return npc;
    }

    private void GenerateHistoricalNews()
    {
        // Add key historical events to NewsSystem
        var king = CastleLocation.GetCurrentKing();
        if (king != null)
        {
            NewsSystem.Instance.Newsy(false, $"{king.GetTitle()} {king.Name} has ruled for {king.TotalReign} days.");
        }

        var controllingTeam = ActiveTeams.FirstOrDefault(t => t.ControlsCity);
        if (controllingTeam != null)
        {
            NewsSystem.Instance.Newsy(false, $"'{controllingTeam.Name}' controls the city.");
        }

        // Add some recent historical events
        int eventsToAdd = Math.Min(5, worldHistory.Count);
        for (int i = worldHistory.Count - eventsToAdd; i < worldHistory.Count; i++)
        {
            if (i >= 0 && i < worldHistory.Count)
            {
                NewsSystem.Instance.Newsy(false, worldHistory[i]);
            }
        }
    }

    /// <summary>
    /// Generate a lore-friendly Ocean/Manwe themed team name
    /// </summary>
    public string GenerateLoreFriendlyTeamName()
    {
        // Avoid duplicate names
        string name;
        int attempts = 0;
        do
        {
            var prefix = OceanTeamNamesPrefixes[random.Next(OceanTeamNamesPrefixes.Length)];
            var suffix = OceanTeamNamesSuffixes[random.Next(OceanTeamNamesSuffixes.Length)];
            name = $"{prefix} {suffix}";
            attempts++;
        } while (ActiveTeams.Any(t => t.Name == name) && attempts < 20);

        return name;
    }

    /// <summary>
    /// Calculate XP needed for a level (matches other formulas)
    /// </summary>
    private static long GetExperienceForLevel(int level)
    {
        if (level <= 1) return 0;
        long exp = 0;
        for (int i = 2; i <= level; i++)
        {
            exp += (long)(Math.Pow(i, 2.0) * 50);
        }
        return exp;
    }

    /// <summary>
    /// Get world history log
    /// </summary>
    public List<string> GetWorldHistory() => new List<string>(worldHistory);

    /// <summary>
    /// Check if world is initialized
    /// </summary>
    public bool IsWorldInitialized => worldInitialized;

    /// <summary>
    /// Reset world for new game
    /// </summary>
    public void ResetWorld()
    {
        worldInitialized = false;
        worldHistory.Clear();
        ActiveTeams.Clear();
        DeadNPCNames.Clear();
        SimulatedDays = 0;
        GD.Print("[WorldInit] World reset");
    }
}
