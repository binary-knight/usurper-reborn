using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Utils;
using UsurperRemake.Systems;

/// <summary>
/// Training System - D&D-style proficiency and roll mechanics
/// Players earn training points on level up and can spend them to improve abilities/spells
/// Each skill has proficiency levels that affect success chance and power
/// </summary>
public static class TrainingSystem
{
    /// <summary>
    /// Proficiency levels for abilities and spells
    /// Each level provides bonuses to rolls and effect power
    /// </summary>
    public enum ProficiencyLevel
    {
        Untrained = 0,   // -2 to rolls, 50% effect power, 25% fail chance
        Poor = 1,        // -1 to rolls, 70% effect power, 15% fail chance
        Average = 2,     // +0 to rolls, 100% effect power, 10% fail chance
        Good = 3,        // +1 to rolls, 115% effect power, 7% fail chance
        Skilled = 4,     // +2 to rolls, 130% effect power, 5% fail chance
        Expert = 5,      // +3 to rolls, 145% effect power, 3% fail chance
        Superb = 6,      // +4 to rolls, 160% effect power, 2% fail chance
        Master = 7,      // +5 to rolls, 180% effect power, 1% fail chance
        Legendary = 8    // +7 to rolls, 200% effect power, 0% fail chance
    }

    /// <summary>
    /// Get the display name for a proficiency level
    /// </summary>
    public static string GetProficiencyName(ProficiencyLevel level)
    {
        return level switch
        {
            ProficiencyLevel.Untrained => "Untrained",
            ProficiencyLevel.Poor => "Poor",
            ProficiencyLevel.Average => "Average",
            ProficiencyLevel.Good => "Good",
            ProficiencyLevel.Skilled => "Skilled",
            ProficiencyLevel.Expert => "Expert",
            ProficiencyLevel.Superb => "Superb",
            ProficiencyLevel.Master => "Master",
            ProficiencyLevel.Legendary => "Legendary",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Get color for proficiency level display
    /// </summary>
    public static string GetProficiencyColor(ProficiencyLevel level)
    {
        return level switch
        {
            ProficiencyLevel.Untrained => "dark_gray",
            ProficiencyLevel.Poor => "gray",
            ProficiencyLevel.Average => "white",
            ProficiencyLevel.Good => "green",
            ProficiencyLevel.Skilled => "bright_green",
            ProficiencyLevel.Expert => "cyan",
            ProficiencyLevel.Superb => "bright_cyan",
            ProficiencyLevel.Master => "yellow",
            ProficiencyLevel.Legendary => "bright_magenta",
            _ => "white"
        };
    }

    /// <summary>
    /// Get roll modifier for proficiency level
    /// </summary>
    public static int GetRollModifier(ProficiencyLevel level)
    {
        return level switch
        {
            ProficiencyLevel.Untrained => -2,
            ProficiencyLevel.Poor => -1,
            ProficiencyLevel.Average => 0,
            ProficiencyLevel.Good => 1,
            ProficiencyLevel.Skilled => 2,
            ProficiencyLevel.Expert => 3,
            ProficiencyLevel.Superb => 4,
            ProficiencyLevel.Master => 5,
            ProficiencyLevel.Legendary => 7,
            _ => 0
        };
    }

    /// <summary>
    /// Get effect power multiplier (1.0 = 100%)
    /// </summary>
    public static float GetEffectMultiplier(ProficiencyLevel level)
    {
        return level switch
        {
            ProficiencyLevel.Untrained => 0.50f,
            ProficiencyLevel.Poor => 0.70f,
            ProficiencyLevel.Average => 1.00f,
            ProficiencyLevel.Good => 1.15f,
            ProficiencyLevel.Skilled => 1.30f,
            ProficiencyLevel.Expert => 1.45f,
            ProficiencyLevel.Superb => 1.60f,
            ProficiencyLevel.Master => 1.80f,
            ProficiencyLevel.Legendary => 2.00f,
            _ => 1.00f
        };
    }

    /// <summary>
    /// Get failure chance (0-100%)
    /// </summary>
    public static int GetFailureChance(ProficiencyLevel level)
    {
        return level switch
        {
            ProficiencyLevel.Untrained => 25,
            ProficiencyLevel.Poor => 15,
            ProficiencyLevel.Average => 10,
            ProficiencyLevel.Good => 7,
            ProficiencyLevel.Skilled => 5,
            ProficiencyLevel.Expert => 3,
            ProficiencyLevel.Superb => 2,
            ProficiencyLevel.Master => 1,
            ProficiencyLevel.Legendary => 0,
            _ => 10
        };
    }

    /// <summary>
    /// Training points required to reach next level (progress needed)
    /// </summary>
    public static int GetPointsForNextLevel(ProficiencyLevel currentLevel)
    {
        return currentLevel switch
        {
            ProficiencyLevel.Untrained => 1,   // 1 progress to reach Poor
            ProficiencyLevel.Poor => 2,        // 2 progress to reach Average
            ProficiencyLevel.Average => 3,     // 3 progress to reach Good
            ProficiencyLevel.Good => 4,        // 4 progress to reach Skilled
            ProficiencyLevel.Skilled => 5,     // 5 progress to reach Expert
            ProficiencyLevel.Expert => 7,      // 7 progress to reach Superb
            ProficiencyLevel.Superb => 10,     // 10 progress to reach Master
            ProficiencyLevel.Master => 15,     // 15 progress to reach Legendary
            ProficiencyLevel.Legendary => 999, // Cannot improve further
            _ => 999
        };
    }

    /// <summary>
    /// Cost in training points per 1 progress point (scales with level)
    /// Higher proficiency = more expensive to train
    /// </summary>
    public static int GetTrainingCostPerPoint(ProficiencyLevel currentLevel)
    {
        return currentLevel switch
        {
            ProficiencyLevel.Untrained => 1,   // 1 training point per progress
            ProficiencyLevel.Poor => 1,        // 1 training point per progress
            ProficiencyLevel.Average => 2,     // 2 training points per progress
            ProficiencyLevel.Good => 2,        // 2 training points per progress
            ProficiencyLevel.Skilled => 3,     // 3 training points per progress
            ProficiencyLevel.Expert => 3,      // 3 training points per progress
            ProficiencyLevel.Superb => 4,      // 4 training points per progress
            ProficiencyLevel.Master => 5,      // 5 training points per progress
            ProficiencyLevel.Legendary => 999, // Cannot improve further
            _ => 999
        };
    }

    /// <summary>
    /// Calculate total training points needed to reach next level from current progress
    /// </summary>
    public static int GetTotalCostToNextLevel(ProficiencyLevel currentLevel, int currentProgress)
    {
        int progressNeeded = GetPointsForNextLevel(currentLevel) - currentProgress;
        int costPerPoint = GetTrainingCostPerPoint(currentLevel);
        return progressNeeded * costPerPoint;
    }

    /// <summary>
    /// Calculate training points earned per level
    /// Based on character class and Intelligence/Wisdom
    /// </summary>
    public static int CalculateTrainingPointsPerLevel(Character character)
    {
        // Base: 3 points per level
        int basePoints = 3;

        // Class bonuses
        int classBonus = character.Class switch
        {
            CharacterClass.Sage => 3,        // Scholars learn fastest
            CharacterClass.Magician => 2,    // Mages are quick learners
            CharacterClass.Cleric => 2,      // Divine training
            CharacterClass.Alchemist => 2,   // Studious types
            CharacterClass.Bard => 1,        // Jack of all trades
            CharacterClass.Ranger => 1,      // Wilderness training
            CharacterClass.Assassin => 1,    // Specialized training
            CharacterClass.Paladin => 1,     // Disciplined
            CharacterClass.Warrior => 0,     // Standard
            CharacterClass.Barbarian => 0,   // Instinct over training
            CharacterClass.Jester => 0,      // Learns by doing
            _ => 0
        };

        // Stat bonuses (every 20 points of Int or Wis gives +1)
        int statBonus = (int)((character.Intelligence + character.Wisdom) / 40);

        return basePoints + classBonus + statBonus;
    }

    /// <summary>
    /// Chance to improve a skill through combat use (0-100%)
    /// Lower proficiency = higher chance to learn
    /// </summary>
    public static int GetCombatImprovementChance(ProficiencyLevel level)
    {
        return level switch
        {
            ProficiencyLevel.Untrained => 15,  // 15% chance per use
            ProficiencyLevel.Poor => 10,       // 10% chance
            ProficiencyLevel.Average => 7,     // 7% chance
            ProficiencyLevel.Good => 5,        // 5% chance
            ProficiencyLevel.Skilled => 3,     // 3% chance
            ProficiencyLevel.Expert => 2,      // 2% chance
            ProficiencyLevel.Superb => 1,      // 1% chance
            ProficiencyLevel.Master => 0,      // Cannot improve through use
            ProficiencyLevel.Legendary => 0,   // Cannot improve through use
            _ => 0
        };
    }

    /// <summary>
    /// Roll a D20 with modifiers - core combat mechanic
    /// </summary>
    public static RollResult RollD20(int modifier, int targetDC, Random? random = null)
    {
        random ??= new Random();
        int roll = random.Next(1, 21); // 1-20
        int total = roll + modifier;

        return new RollResult
        {
            NaturalRoll = roll,
            Modifier = modifier,
            Total = total,
            TargetDC = targetDC,
            Success = total >= targetDC,
            IsCriticalSuccess = roll == 20,
            IsCriticalFailure = roll == 1
        };
    }

    /// <summary>
    /// Roll for ability/spell success
    /// </summary>
    public static RollResult RollAbilityCheck(
        Character caster,
        string skillId,
        int baseDC,
        Random? random = null)
    {
        random ??= new Random();

        // Get proficiency
        var proficiency = GetSkillProficiency(caster, skillId);

        // Calculate modifier
        int rollMod = GetRollModifier(proficiency);

        // Add stat modifier based on skill type
        int statMod = 0;
        if (IsSpell(skillId))
        {
            // Spells use Int or Wis
            if (caster.Class == CharacterClass.Magician)
                statMod = (int)((caster.Intelligence - 10) / 2);
            else if (caster.Class == CharacterClass.Cleric)
                statMod = (int)((caster.Wisdom - 10) / 2);
            else
                statMod = (int)(((caster.Intelligence + caster.Wisdom) / 2 - 10) / 2);
        }
        else
        {
            // Physical abilities use Str or Dex
            var ability = ClassAbilitySystem.GetAbility(skillId);
            if (ability != null && ability.Type == ClassAbilitySystem.AbilityType.Attack)
                statMod = (int)((caster.Strength - 10) / 2);
            else if (ability != null && ability.Type == ClassAbilitySystem.AbilityType.Defense)
                statMod = (int)((caster.Dexterity - 10) / 2);
            else
                statMod = (int)(((caster.Strength + caster.Dexterity) / 2 - 10) / 2);
        }

        // Level bonus (small)
        int levelMod = caster.Level / 10;

        int totalMod = rollMod + statMod + levelMod;

        // Check for flat failure chance first
        int failChance = GetFailureChance(proficiency);
        if (random.Next(100) < failChance)
        {
            // Automatic failure due to inexperience
            return new RollResult
            {
                NaturalRoll = 0,
                Modifier = totalMod,
                Total = 0,
                TargetDC = baseDC,
                Success = false,
                IsCriticalFailure = true,
                FailureReason = "Skill failure! You fumbled the ability."
            };
        }

        return RollD20(totalMod, baseDC, random);
    }

    /// <summary>
    /// Roll for attack success (player attacking monster)
    /// </summary>
    public static RollResult RollAttack(
        Character attacker,
        int targetAC,
        bool isAbility = false,
        string? abilityId = null,
        Random? random = null)
    {
        random ??= new Random();

        // Base modifier from Strength (v0.41.4: reduced from /2 to /3 to slow hit scaling)
        int statMod = (int)((attacker.Strength - 10) / 3);

        // Proficiency modifier if using trained ability
        int profMod = 0;
        if (isAbility && !string.IsNullOrEmpty(abilityId))
        {
            var proficiency = GetSkillProficiency(attacker, abilityId);
            profMod = GetRollModifier(proficiency);
        }
        else
        {
            // Basic attack - check weapon proficiency
            profMod = GetRollModifier(GetSkillProficiency(attacker, "basic_attack"));
        }

        // Level bonus
        int levelMod = attacker.Level / 5;

        // Equipment bonus (weapon quality)
        int equipMod = (int)(attacker.WeapPow / 20);

        int rawMod = statMod + profMod + levelMod + equipMod;

        // Diminishing returns: full value up to +6, third rate above that (v0.41.4: was /2).
        // Keeps hit chance meaningful across 100 levels instead of auto-hitting by level 8.
        // Raw +6 → +6, Raw +12 → +8, Raw +36 → +16, Raw +51 → +21
        int totalMod = rawMod <= 6 ? rawMod : 6 + (rawMod - 6) / 3;

        return RollD20(totalMod, targetAC, random);
    }

    /// <summary>
    /// Roll for monster attack success
    /// </summary>
    public static RollResult RollMonsterAttack(
        Monster monster,
        Character target,
        Random? random = null)
    {
        random ??= new Random();

        // Monster attack modifier (v0.41.4: Level/3 → Level/2 so monsters hit more reliably)
        int monsterMod = monster.Level / 2 + (int)((monster.Strength - 10) / 3);

        // Player's AC: DEX/3 (was /2), ArmPow/25 (was /15) to reduce armor stacking dominance
        int playerAC = 10 + (int)((target.Dexterity - 10) / 3) + (int)(target.ArmPow / 25) + (target.Level / 10);

        // Check for dodge/evasion effects
        if (target.HasStatusEffect("evasion"))
            playerAC += 10;
        if (target.HasStatusEffect("invisible"))
            playerAC += 5;

        return RollD20(monsterMod, playerAC, random);
    }

    /// <summary>
    /// Calculate the DC for an ability based on monster level
    /// </summary>
    public static int CalculateAbilityDC(int monsterLevel)
    {
        // Base DC 10, +1 per 5 monster levels
        return 10 + (monsterLevel / 5);
    }

    /// <summary>
    /// Get a character's proficiency in a skill
    /// </summary>
    public static ProficiencyLevel GetSkillProficiency(Character character, string skillId)
    {
        if (character.SkillProficiencies.TryGetValue(skillId, out var proficiency))
        {
            return proficiency;
        }

        // Default: Untrained for unknown skills, Average for class skills
        if (IsClassSkill(character.Class, skillId))
        {
            return ProficiencyLevel.Average;
        }

        return ProficiencyLevel.Untrained;
    }

    /// <summary>
    /// Set a character's proficiency in a skill
    /// </summary>
    public static void SetSkillProficiency(Character character, string skillId, ProficiencyLevel level)
    {
        character.SkillProficiencies[skillId] = level;
    }

    /// <summary>
    /// Get accumulated training progress toward next level
    /// </summary>
    public static int GetTrainingProgress(Character character, string skillId)
    {
        if (character.SkillTrainingProgress.TryGetValue(skillId, out var progress))
        {
            return progress;
        }
        return 0;
    }

    /// <summary>
    /// Add training progress to a skill (returns true if level up occurred)
    /// </summary>
    public static bool AddTrainingProgress(Character character, string skillId, int points)
    {
        var currentLevel = GetSkillProficiency(character, skillId);
        if (currentLevel >= ProficiencyLevel.Legendary)
            return false; // Already maxed

        int currentProgress = GetTrainingProgress(character, skillId);
        int requiredPoints = GetPointsForNextLevel(currentLevel);

        currentProgress += points;

        if (currentProgress >= requiredPoints)
        {
            // Level up!
            currentProgress -= requiredPoints;
            var newLevel = (ProficiencyLevel)((int)currentLevel + 1);
            SetSkillProficiency(character, skillId, newLevel);
            character.SkillTrainingProgress[skillId] = currentProgress;
            return true;
        }

        character.SkillTrainingProgress[skillId] = currentProgress;
        return false;
    }

    /// <summary>
    /// Try to improve skill through combat use
    /// </summary>
    public static bool TryImproveFromUse(Character character, string skillId, Random? random = null)
    {
        random ??= new Random();

        var currentLevel = GetSkillProficiency(character, skillId);
        int chance = GetCombatImprovementChance(currentLevel);

        if (random.Next(100) < chance)
        {
            // Gained experience! Add 1 training point worth of progress
            return AddTrainingProgress(character, skillId, 1);
        }

        return false;
    }

    /// <summary>
    /// Check if a skill is a spell (vs ability)
    /// </summary>
    public static bool IsSpell(string skillId)
    {
        // Spells are prefixed with class_spell_level format or just "spell_X"
        return skillId.StartsWith("spell_") ||
               skillId.StartsWith("cleric_") ||
               skillId.StartsWith("magician_") ||
               skillId.StartsWith("sage_");
    }

    /// <summary>
    /// Get spell skill ID
    /// </summary>
    public static string GetSpellSkillId(CharacterClass casterClass, int spellLevel)
    {
        string classPrefix = casterClass switch
        {
            CharacterClass.Cleric => "cleric",
            CharacterClass.Magician => "magician",
            CharacterClass.Sage => "sage",
            _ => "spell"
        };
        return $"{classPrefix}_spell_{spellLevel}";
    }

    /// <summary>
    /// Check if a skill is a class skill (starts at Average instead of Untrained)
    /// </summary>
    private static bool IsClassSkill(CharacterClass charClass, string skillId)
    {
        // Basic attack is always a class skill
        if (skillId == "basic_attack")
            return true;

        // Check if it's a spell for this class
        if (skillId.StartsWith("cleric_") && charClass == CharacterClass.Cleric)
            return true;
        if (skillId.StartsWith("magician_") && charClass == CharacterClass.Magician)
            return true;
        if (skillId.StartsWith("sage_") && charClass == CharacterClass.Sage)
            return true;

        // Check class abilities
        var ability = ClassAbilitySystem.GetAbility(skillId);
        if (ability != null && ability.AvailableToClasses.Contains(charClass))
            return true;

        return false;
    }

    /// <summary>
    /// Calculate the gold cost to reset training proficiencies
    /// </summary>
    public static long CalculateRespecGoldCost(Character player)
    {
        return GameConfig.RespecBaseGoldCost + (player.Level * GameConfig.RespecGoldPerLevel);
    }

    /// <summary>
    /// Calculate total training points invested in a skill above its default level.
    /// Class skills default to Average; non-class skills default to Untrained.
    /// </summary>
    public static int CalculateTotalPointsInvested(Character player, string skillId)
    {
        var currentLevel = GetSkillProficiency(player, skillId);
        var currentProgress = GetTrainingProgress(player, skillId);

        // Determine default level (class skills start at Average for free)
        var defaultLevel = IsClassSkill(player.Class, skillId)
            ? ProficiencyLevel.Average
            : ProficiencyLevel.Untrained;

        if (currentLevel <= defaultLevel && currentProgress == 0)
            return 0; // Nothing invested

        int totalCost = 0;

        // Sum cost of each completed tier above default
        for (var tier = defaultLevel; tier < currentLevel; tier++)
        {
            totalCost += GetPointsForNextLevel(tier) * GetTrainingCostPerPoint(tier);
        }

        // Add cost of partial progress at current tier
        if (currentLevel < ProficiencyLevel.Legendary)
        {
            totalCost += currentProgress * GetTrainingCostPerPoint(currentLevel);
        }

        return totalCost;
    }

    /// <summary>
    /// Reset a single skill's proficiency back to its default level.
    /// Returns the number of training points refunded.
    /// </summary>
    public static int ResetSkillProficiency(Character player, string skillId)
    {
        int pointsRefunded = CalculateTotalPointsInvested(player, skillId);
        if (pointsRefunded == 0)
            return 0;

        // Reset to default level
        var defaultLevel = IsClassSkill(player.Class, skillId)
            ? ProficiencyLevel.Average
            : ProficiencyLevel.Untrained;

        player.SkillProficiencies[skillId] = defaultLevel;
        player.SkillTrainingProgress.Remove(skillId);

        return pointsRefunded;
    }

    /// <summary>
    /// Reset ALL skill proficiencies back to defaults.
    /// Returns total training points refunded.
    /// </summary>
    public static int ResetAllProficiencies(Character player)
    {
        int totalRefunded = 0;

        // Iterate over a copy since we're modifying the dictionary
        var skillIds = new List<string>(player.SkillProficiencies.Keys);
        foreach (var skillId in skillIds)
        {
            totalRefunded += ResetSkillProficiency(player, skillId);
        }

        return totalRefunded;
    }

    /// <summary>
    /// Show the reset training submenu
    /// </summary>
    private static async Task ShowResetMenu(Character player, TerminalEmulator terminal)
    {
        long goldCost = CalculateRespecGoldCost(player);

        while (true)
        {
            terminal.ClearScreen();
            terminal.WriteLine("═══ RESET TRAINING ═══", "bright_yellow");
            terminal.WriteLine("");
            terminal.WriteLine("The Level Master leans forward, studying you carefully.", "white");
            terminal.WriteLine("");
            terminal.WriteLine("\"Ah, you seek the Unmaking? To have your training", "bright_cyan");
            terminal.WriteLine("unraveled and your potential restored? It can be done...\"", "bright_cyan");
            terminal.WriteLine("");
            terminal.WriteLine("He gestures to a shelf of shimmering silver vials.", "white");
            terminal.WriteLine("");
            terminal.WriteLine("\"The Draught of Forgetting does not come cheap.\"", "bright_cyan");
            terminal.WriteLine("");
            terminal.WriteLine($"Service fee: {goldCost:N0} gold", "yellow");
            terminal.WriteLine($"Your gold: {player.Gold:N0}", player.Gold >= goldCost ? "bright_green" : "red");
            terminal.WriteLine("");
            terminal.WriteLine("[1] Reset a single skill", "white");
            terminal.WriteLine("[2] Reset ALL skill proficiencies", "white");
            terminal.WriteLine("[X] Cancel", "yellow");
            terminal.WriteLine("");

            var input = await terminal.GetInput("> ");
            if (string.IsNullOrWhiteSpace(input) || input.Trim().ToUpper() == "X")
                return;

            if (input.Trim() == "1")
            {
                await ShowResetSingleSkillMenu(player, terminal, goldCost);
                return;
            }
            else if (input.Trim() == "2")
            {
                await ShowResetAllMenu(player, terminal, goldCost);
                return;
            }
        }
    }

    /// <summary>
    /// Show menu to reset a single skill's proficiency
    /// </summary>
    private static async Task ShowResetSingleSkillMenu(Character player, TerminalEmulator terminal, long goldCost)
    {
        // Find skills that have been trained above default
        var trainedSkills = new List<(string skillId, string skillName, int pointsInvested)>();
        var allSkills = GetTrainableSkills(player);

        foreach (var (skillId, skillName) in allSkills)
        {
            int invested = CalculateTotalPointsInvested(player, skillId);
            if (invested > 0)
            {
                trainedSkills.Add((skillId, skillName, invested));
            }
        }

        if (trainedSkills.Count == 0)
        {
            terminal.WriteLine("You have no trained skills to reset!", "yellow");
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();
        terminal.WriteLine("═══ RESET SINGLE SKILL ═══", "bright_yellow");
        terminal.WriteLine($"Service fee: {goldCost:N0} gold", "yellow");
        terminal.WriteLine("");
        terminal.WriteLine("Num  Skill                    Level         Points Invested", "cyan");
        terminal.WriteLine("─────────────────────────────────────────────────────────────", "cyan");

        int index = 1;
        foreach (var (skillId, skillName, pointsInvested) in trainedSkills)
        {
            var proficiency = GetSkillProficiency(player, skillId);
            string profName = GetProficiencyName(proficiency);
            string profColor = GetProficiencyColor(proficiency);
            terminal.WriteLine($" {index,2}  {skillName,-24} [{profColor}]{profName,-13}[/] {pointsInvested} pts");
            index++;
        }

        terminal.WriteLine("");
        terminal.WriteLine("Enter skill number to reset, or X to cancel.", "yellow");

        var input = await terminal.GetInput("> ");
        if (string.IsNullOrWhiteSpace(input) || input.Trim().ToUpper() == "X")
            return;

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= trainedSkills.Count)
        {
            var (skillId, skillName, pointsInvested) = trainedSkills[choice - 1];

            if (player.Gold < goldCost)
            {
                terminal.WriteLine($"You need {goldCost:N0} gold but only have {player.Gold:N0}!", "red");
                await Task.Delay(1500);
                return;
            }

            // Confirm
            var defaultLevel = IsClassSkill(player.Class, skillId)
                ? ProficiencyLevel.Average
                : ProficiencyLevel.Untrained;

            terminal.WriteLine("");
            terminal.WriteLine($"Reset {skillName} to {GetProficiencyName(defaultLevel)}?", "bright_yellow");
            terminal.WriteLine($"  Cost: {goldCost:N0} gold", "yellow");
            terminal.WriteLine($"  Refund: {pointsInvested} training points", "bright_green");
            terminal.WriteLine("");
            var confirm = await terminal.GetInput("Confirm? (Y/N) > ");
            if (confirm?.Trim().ToUpper() != "Y")
                return;

            // Execute reset with lore flavor
            player.Gold -= goldCost;
            int refunded = ResetSkillProficiency(player, skillId);
            player.TrainingPoints += refunded;

            terminal.ClearScreen();
            terminal.WriteLine("");
            terminal.WriteLine("The Level Master nods slowly and reaches for a vial of", "white");
            terminal.WriteLine("shimmering silver liquid on the shelf behind him.", "white");
            await Task.Delay(1500);
            terminal.WriteLine("");
            terminal.WriteLine("\"Drink this. It will feel... strange.\"", "bright_cyan");
            await Task.Delay(1500);
            terminal.WriteLine("");
            terminal.WriteLine("As the liquid touches your lips, the Level Master presses", "white");
            terminal.WriteLine("his thumb to your forehead and whispers ancient words.", "white");
            await Task.Delay(1500);
            terminal.WriteLine("");
            terminal.WriteLine($"\"Oblivius... tractum... {skillName.ToLower()}...\"", "bright_magenta");
            await Task.Delay(1500);
            terminal.WriteLine("");
            terminal.WriteLine("A chill runs through your body. The countless hours of", "white");
            terminal.WriteLine($"training in {skillName} dissolve like morning frost.", "white");
            terminal.WriteLine("Your muscles forget. Your instincts unravel.", "white");
            terminal.WriteLine("But the potential remains, waiting to be reshaped.", "white");
            await Task.Delay(2000);
            terminal.WriteLine("");
            terminal.WriteLine($"═══ SKILL RESET ═══", "bright_yellow");
            terminal.WriteLine($"{skillName} has been reset to {GetProficiencyName(defaultLevel)}.", "white");
            terminal.WriteLine($"  -{goldCost:N0} gold", "red");
            terminal.WriteLine($"  +{refunded} training points refunded", "bright_green");
            terminal.WriteLine($"  Total training points: {player.TrainingPoints}", "bright_cyan");

            await SaveSystem.Instance.AutoSave(player);
            await terminal.PressAnyKey();
        }
    }

    /// <summary>
    /// Show menu to reset all skill proficiencies
    /// </summary>
    private static async Task ShowResetAllMenu(Character player, TerminalEmulator terminal, long goldCost)
    {
        // Calculate total refund
        int totalRefund = 0;
        var allSkills = GetTrainableSkills(player);
        foreach (var (skillId, _) in allSkills)
        {
            totalRefund += CalculateTotalPointsInvested(player, skillId);
        }

        // Also check any skills in SkillProficiencies not in the trainable list
        foreach (var skillId in player.SkillProficiencies.Keys)
        {
            if (!allSkills.Any(s => s.skillId == skillId))
            {
                totalRefund += CalculateTotalPointsInvested(player, skillId);
            }
        }

        if (totalRefund == 0)
        {
            terminal.WriteLine("You have no trained skills to reset!", "yellow");
            await Task.Delay(1500);
            return;
        }

        if (player.Gold < goldCost)
        {
            terminal.WriteLine($"You need {goldCost:N0} gold but only have {player.Gold:N0}!", "red");
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();
        terminal.WriteLine("═══ RESET ALL TRAINING ═══", "bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine("This will reset ALL skill proficiencies to their defaults.", "white");
        terminal.WriteLine("");
        terminal.WriteLine($"  Cost: {goldCost:N0} gold", "yellow");
        terminal.WriteLine($"  Refund: {totalRefund} training points", "bright_green");
        terminal.WriteLine("");
        terminal.WriteLine("Are you sure? This cannot be undone!", "red");

        var confirm = await terminal.GetInput("Confirm? (Y/N) > ");
        if (confirm?.Trim().ToUpper() != "Y")
            return;

        // Execute reset with lore flavor
        player.Gold -= goldCost;
        int refunded = ResetAllProficiencies(player);
        player.TrainingPoints += refunded;

        terminal.ClearScreen();
        terminal.WriteLine("");
        terminal.WriteLine("The Level Master draws a circle of salt around you", "white");
        terminal.WriteLine("and places candles at the four cardinal points.", "white");
        await Task.Delay(1500);
        terminal.WriteLine("");
        terminal.WriteLine("\"This is no small thing you ask. To unmake all that", "bright_cyan");
        terminal.WriteLine("you have learned... it requires a deeper forgetting.\"", "bright_cyan");
        await Task.Delay(2000);
        terminal.WriteLine("");
        terminal.WriteLine("He pours an entire flask of silver liquid over your head.", "white");
        terminal.WriteLine("Both hands press against your temples.", "white");
        await Task.Delay(1500);
        terminal.WriteLine("");
        terminal.WriteLine("\"Oblivius... totalus... anima... revertum!\"", "bright_magenta");
        await Task.Delay(1500);
        terminal.WriteLine("");
        terminal.WriteLine("A wave of cold fire washes through you. Every technique,", "white");
        terminal.WriteLine("every practiced motion, every honed instinct — all of it", "white");
        terminal.WriteLine("stripped away in an instant. You gasp, feeling lighter.", "white");
        terminal.WriteLine("Empty. But full of possibility.", "white");
        await Task.Delay(2000);
        terminal.WriteLine("");
        terminal.WriteLine("The Level Master catches you as you stumble.", "white");
        terminal.WriteLine("\"Easy now. You are a blank slate once more.\"", "bright_cyan");
        await Task.Delay(1500);
        terminal.WriteLine("");
        terminal.WriteLine($"═══ ALL TRAINING RESET ═══", "bright_yellow");
        terminal.WriteLine($"All skill proficiencies have been reset.", "white");
        terminal.WriteLine($"  -{goldCost:N0} gold", "red");
        terminal.WriteLine($"  +{refunded} training points refunded", "bright_green");
        terminal.WriteLine($"  Total training points: {player.TrainingPoints}", "bright_cyan");

        await SaveSystem.Instance.AutoSave(player);
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Display training menu at Level Master
    /// </summary>
    public static async Task ShowTrainingMenu(Character player, TerminalEmulator terminal)
    {
        while (true)
        {
            terminal.ClearScreen();
            terminal.WriteLine("═══ TRAINING CENTER ═══", "bright_yellow");
            terminal.WriteLine($"Available Training Points: {player.TrainingPoints}", "bright_cyan");
            terminal.WriteLine("");

            // Get all trainable skills for this class
            var trainableSkills = GetTrainableSkills(player);

            terminal.WriteLine("Num  Skill                    Level        Progress  Cost/Pt", "cyan");
            terminal.WriteLine("─────────────────────────────────────────────────────────────", "cyan");

            int index = 1;
            foreach (var (skillId, skillName) in trainableSkills)
            {
                var proficiency = GetSkillProficiency(player, skillId);
                var progress = GetTrainingProgress(player, skillId);
                var needed = GetPointsForNextLevel(proficiency);
                var costPerPoint = GetTrainingCostPerPoint(proficiency);
                string progressStr = proficiency >= ProficiencyLevel.Legendary
                    ? "MAX"
                    : $"{progress}/{needed}";
                string costStr = proficiency >= ProficiencyLevel.Legendary
                    ? "-"
                    : costPerPoint.ToString();

                string profName = GetProficiencyName(proficiency);
                string profColor = GetProficiencyColor(proficiency);

                terminal.WriteLine($" {index,2}  {skillName,-24} [{profColor}]{profName,-12}[/] {progressStr,-9} {costStr}");
                index++;
            }

            terminal.WriteLine("");
            terminal.WriteLine("Enter skill number to train, [R] Reset training, or X to exit.", "yellow");

            var input = await terminal.GetInput("> ");
            if (string.IsNullOrWhiteSpace(input) || input.Trim().ToUpper() == "X")
                break;

            if (input.Trim().ToUpper() == "R")
            {
                await ShowResetMenu(player, terminal);
                continue;
            }

            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= trainableSkills.Count)
            {
                var (skillId, skillName) = trainableSkills[choice - 1];
                await TrainSkill(player, skillId, skillName, terminal);
            }
        }
    }

    /// <summary>
    /// Train a specific skill
    /// </summary>
    private static async Task TrainSkill(Character player, string skillId, string skillName, TerminalEmulator terminal)
    {
        var proficiency = GetSkillProficiency(player, skillId);

        if (proficiency >= ProficiencyLevel.Legendary)
        {
            terminal.WriteLine($"{skillName} is already at Legendary level!", "yellow");
            await Task.Delay(1500);
            return;
        }

        // Calculate costs
        var costPerPoint = GetTrainingCostPerPoint(proficiency);

        if (player.TrainingPoints < costPerPoint)
        {
            terminal.WriteLine($"You need at least {costPerPoint} training points to train this skill!", "red");
            terminal.WriteLine($"You only have {player.TrainingPoints} training points.", "yellow");
            await Task.Delay(1500);
            return;
        }

        // Calculate progress needed to reach next level
        var currentProgress = GetTrainingProgress(player, skillId);
        var progressNeeded = GetPointsForNextLevel(proficiency);
        var progressToNextLevel = progressNeeded - currentProgress;
        var totalCostToNextLevel = progressToNextLevel * costPerPoint;

        // Determine how many progress points the player can afford
        int maxProgressAffordable = player.TrainingPoints / costPerPoint;

        // Show options
        int progressToAdd = 1;
        int trainingPointsToSpend = costPerPoint;

        var nextLevel = (ProficiencyLevel)((int)proficiency + 1);
        string nextLevelName = GetProficiencyName(nextLevel);
        string nextLevelColor = GetProficiencyColor(nextLevel);

        terminal.WriteLine("");
        terminal.WriteLine($"Training {skillName} (Current: {GetProficiencyName(proficiency)})", "cyan");
        terminal.WriteLine($"Progress: {currentProgress}/{progressNeeded} toward [{nextLevelColor}]{nextLevelName}[/]", "white");
        terminal.WriteLine($"Cost per progress point: {costPerPoint} training points", "gray");
        terminal.WriteLine("");

        // Always show option to train 1 point
        terminal.WriteLine($"[1] Spend {costPerPoint} training point{(costPerPoint > 1 ? "s" : "")} (+1 progress)", "white");

        bool canAffordNextLevel = player.TrainingPoints >= totalCostToNextLevel;

        if (progressToNextLevel > 1)
        {
            if (canAffordNextLevel)
            {
                // Player can afford to reach next level
                terminal.WriteLine($"[M] Spend {totalCostToNextLevel} points to reach {nextLevelName} (+{progressToNextLevel} progress)", "bright_green");
            }
            else if (maxProgressAffordable > 1)
            {
                // Player can afford multiple progress but not full level
                int maxCost = maxProgressAffordable * costPerPoint;
                terminal.WriteLine($"[M] Spend {maxCost} points for +{maxProgressAffordable} progress (need {totalCostToNextLevel} total for {nextLevelName})", "yellow");
            }
        }

        terminal.WriteLine("[X] Cancel", "yellow");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("> ");
        if (string.IsNullOrWhiteSpace(choice) || choice.Trim().ToUpper() == "X")
        {
            return;
        }

        if (choice.Trim().ToUpper() == "M" && (canAffordNextLevel || maxProgressAffordable > 1))
        {
            if (canAffordNextLevel)
            {
                progressToAdd = progressToNextLevel;
                trainingPointsToSpend = totalCostToNextLevel;
            }
            else
            {
                progressToAdd = maxProgressAffordable;
                trainingPointsToSpend = maxProgressAffordable * costPerPoint;
            }
        }
        else if (choice.Trim() != "1")
        {
            return;
        }

        // Spend training points
        player.TrainingPoints -= trainingPointsToSpend;

        bool leveledUp = AddTrainingProgress(player, skillId, progressToAdd);

        if (leveledUp)
        {
            var newLevel = GetSkillProficiency(player, skillId);
            string color = GetProficiencyColor(newLevel);
            terminal.WriteLine("");
            terminal.WriteLine($"═══ SKILL IMPROVED! ═══", "bright_yellow");
            terminal.WriteLine($"{skillName} is now [{color}]{GetProficiencyName(newLevel)}[/]!", "bright_green");

            // Show new bonuses
            terminal.WriteLine($"  Roll Modifier: {GetRollModifier(newLevel):+#;-#;+0}", "cyan");
            terminal.WriteLine($"  Effect Power: {GetEffectMultiplier(newLevel) * 100:F0}%", "cyan");
            terminal.WriteLine($"  Failure Chance: {GetFailureChance(newLevel)}%", "cyan");
            terminal.WriteLine($"  (Spent {trainingPointsToSpend} training points)", "gray");
        }
        else
        {
            var progress = GetTrainingProgress(player, skillId);
            var needed = GetPointsForNextLevel(proficiency);
            terminal.WriteLine($"Training {skillName}... Progress: {progress}/{needed} (spent {trainingPointsToSpend} point{(trainingPointsToSpend > 1 ? "s" : "")})", "green");
        }

        // Auto-save after training
        await SaveSystem.Instance.AutoSave(player);

        await Task.Delay(1500);
    }

    /// <summary>
    /// Get all trainable skills for a character
    /// </summary>
    public static List<(string skillId, string skillName)> GetTrainableSkills(Character character)
    {
        var skills = new List<(string, string)>();

        // Basic attack
        skills.Add(("basic_attack", "Basic Attack"));

        // Class abilities
        var classAbilities = ClassAbilitySystem.GetClassAbilities(character.Class);
        foreach (var ability in classAbilities)
        {
            if (character.Level >= ability.LevelRequired)
            {
                skills.Add((ability.Id, ability.Name));
            }
        }

        // Spells for magic classes
        if (ClassAbilitySystem.IsSpellcaster(character.Class))
        {
            var spells = SpellSystem.GetAvailableSpells(character);
            foreach (var spell in spells)
            {
                string skillId = GetSpellSkillId(character.Class, spell.Level);
                skills.Add((skillId, spell.Name));
            }
        }

        return skills;
    }
}

/// <summary>
/// Result of a D20 roll
/// </summary>
public class RollResult
{
    public int NaturalRoll { get; set; }      // The actual die roll (1-20)
    public int Modifier { get; set; }          // Total modifier applied
    public int Total { get; set; }             // NaturalRoll + Modifier
    public int TargetDC { get; set; }          // What we needed to hit
    public bool Success { get; set; }          // Did we meet/exceed DC?
    public bool IsCriticalSuccess { get; set; } // Natural 20
    public bool IsCriticalFailure { get; set; } // Natural 1 or skill fumble
    public string FailureReason { get; set; } = "";

    /// <summary>
    /// Get damage multiplier based on roll quality
    /// </summary>
    public float GetDamageMultiplier()
    {
        if (IsCriticalSuccess) return 2.0f;  // Crit = double damage
        if (IsCriticalFailure) return 0.0f;  // Fumble = no damage
        if (!Success) return 0.0f;           // Miss = no damage

        // Bonus damage for exceeding DC significantly
        int excess = Total - TargetDC;
        if (excess >= 10) return 1.5f;       // Great hit
        if (excess >= 5) return 1.25f;       // Good hit

        return 1.0f; // Normal hit
    }

    /// <summary>
    /// Get a descriptive message for the roll
    /// </summary>
    public string GetRollDescription()
    {
        if (IsCriticalSuccess) return "CRITICAL HIT!";
        if (IsCriticalFailure) return !string.IsNullOrEmpty(FailureReason) ? FailureReason : "CRITICAL MISS!";
        if (!Success) return "Miss!";

        int excess = Total - TargetDC;
        if (excess >= 10) return "Devastating blow!";
        if (excess >= 5) return "Solid hit!";
        return "Hit!";
    }
}
