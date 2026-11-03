using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Meta-Progression System - Tracks persistent unlocks across all playthroughs
    /// Manages titles, bonuses, and replayability features
    /// </summary>
    public class MetaProgressionSystem
    {
        private static MetaProgressionSystem? _fallbackInstance;
        public static MetaProgressionSystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.MetaProgression;
                return _fallbackInstance ??= new MetaProgressionSystem();
            }
        }

        private MetaProgressionData data;
        private readonly string saveFilePath;

        public MetaProgressionSystem()
        {
            saveFilePath = Path.Combine(Environment.CurrentDirectory, "saves", "meta_progression.json");
            data = LoadData();
        }

        /// <summary>
        /// Per-account meta progression. In MUD/online mode the server process is shared
        /// across all SSH accounts, so the global meta_progression.json file would unlock
        /// prestige classes for every new account the moment ANY player on the server
        /// completes an ending. This overload scopes the file to a single account.
        /// Empty/null username falls back to the global file (single-player local mode).
        /// </summary>
        public MetaProgressionSystem(string username)
        {
            string fileName = string.IsNullOrWhiteSpace(username)
                ? "meta_progression.json"
                : $"meta_progression_{SanitizeFilename(username)}.json";
            saveFilePath = Path.Combine(Environment.CurrentDirectory, "saves", fileName);
            data = LoadData();
        }

        private static string SanitizeFilename(string raw)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw.ToLowerInvariant())
            {
                if (System.Array.IndexOf(invalid, c) < 0 && c != ' ' && c != '.') sb.Append(c);
                else sb.Append('_');
            }
            return sb.ToString();
        }

        #region Data Properties

        /// <summary>
        /// Total number of playthroughs completed
        /// </summary>
        public int TotalPlaythroughs => data.TotalPlaythroughs;

        /// <summary>
        /// Highest cycle ever reached
        /// </summary>
        public int HighestCycle => data.HighestCycle;

        /// <summary>
        /// All endings that have been achieved
        /// </summary>
        public HashSet<EndingType> UnlockedEndings => data.UnlockedEndings;

        /// <summary>
        /// All unlocks earned across playthroughs
        /// </summary>
        public HashSet<string> UnlockedBonuses => data.UnlockedBonuses;

        /// <summary>
        /// Titles earned (prefixes for character names)
        /// </summary>
        public HashSet<string> UnlockedTitles => data.UnlockedTitles;

        /// <summary>
        /// Noctura betrayal state (persists across NG+ cycles)
        /// 0=never encountered, 1=betrayed+escaped, 2=betrayed+defeated
        /// </summary>
        public int NocturaBetrayal => data.NocturaBetrayal;
        public int NocturaTimesBetrayed => data.NocturaTimesBetrayed;

        public void RecordNocturaBetrayal(bool playerWon)
        {
            data.NocturaBetrayal = playerWon ? 2 : 1;
            data.NocturaTimesBetrayed++;
            SaveData();
        }

        /// <summary>
        /// Total monsters killed across all playthroughs
        /// </summary>
        public long TotalMonstersKilled => data.TotalMonstersKilled;

        /// <summary>
        /// Total gold earned across all playthroughs
        /// </summary>
        public long TotalGoldEarned => data.TotalGoldEarned;

        /// <summary>
        /// Highest level ever achieved
        /// </summary>
        public int HighestLevelAchieved => data.HighestLevelAchieved;

        #endregion

        #region Unlock Tracking

        /// <summary>
        /// Record an ending unlock when player completes the game
        /// </summary>
        public void RecordEndingUnlock(EndingType ending, Character player)
        {
            data.TotalPlaythroughs++;
            data.UnlockedEndings.Add(ending);

            // Track best stats
            if (player.Level > data.HighestLevelAchieved)
                data.HighestLevelAchieved = player.Level;
            data.TotalMonstersKilled += player.MKills;
            data.TotalGoldEarned += player.Gold + player.BankGold;

            // Track cycle
            int currentCycle = StoryProgressionSystem.Instance.CurrentCycle;
            if (currentCycle > data.HighestCycle)
                data.HighestCycle = currentCycle;

            // Unlock ending-specific bonuses and titles
            switch (ending)
            {
                case EndingType.Usurper:
                    data.UnlockedTitles.Add("Dark Lord");
                    data.UnlockedBonuses.Add("TYRANTS_AURA");
                    data.UnlockedBonuses.Add("FEAR_THE_THRONE");
                    break;
                case EndingType.Savior:
                    data.UnlockedTitles.Add("Savior");
                    data.UnlockedBonuses.Add("HEALING_LIGHT");
                    data.UnlockedBonuses.Add("BLESSED_COMMERCE");
                    break;
                case EndingType.Defiant:
                    data.UnlockedTitles.Add("Defiant");
                    data.UnlockedBonuses.Add("MORTAL_PRIDE");
                    data.UnlockedBonuses.Add("ANCIENT_KEY");
                    break;
                case EndingType.TrueEnding:
                    data.UnlockedTitles.Add("Awakened");
                    data.UnlockedBonuses.Add("OCEANS_BLESSING");
                    data.UnlockedBonuses.Add("ARTIFACT_MEMORY");
                    data.UnlockedBonuses.Add("SEAL_RESONANCE");
                    break;
                case EndingType.Secret:
                    data.UnlockedTitles.Add("Dissolved");
                    data.UnlockedBonuses.Add("FINAL_PEACE");
                    break;
            }

            // Level-based unlocks
            if (player.Level >= 50)
                data.UnlockedBonuses.Add("VETERAN");
            if (player.Level >= 100)
                data.UnlockedBonuses.Add("MASTER");

            // Kill-based unlocks
            if (player.MKills >= 5000)
                data.UnlockedBonuses.Add("SLAYER");
            if (player.MKills >= 10000)
                data.UnlockedBonuses.Add("LEGEND_SLAYER");

            // Collection unlocks
            if (StoryProgressionSystem.Instance.CollectedSeals.Count >= 7)
                data.UnlockedBonuses.Add("SEAL_MASTER");
            if (StoryProgressionSystem.Instance.CollectedArtifacts.Count >= 7)
                data.UnlockedBonuses.Add("ARTIFACT_HUNTER");

            // Companion unlocks
            if (CompanionSystem.Instance.GetFallenCompanions().Any())
                data.UnlockedBonuses.Add("SURVIVORS_GUILT");
            if (CompanionSystem.Instance.GetActiveCompanions().Count() >= 3)
                data.UnlockedBonuses.Add("PARTY_LEADER");

            // Special unlocks for multiple endings
            if (data.UnlockedEndings.Count >= 3)
                data.UnlockedBonuses.Add("MULTIPLE_PATHS");
            if (data.UnlockedEndings.Count >= 5)
            {
                data.UnlockedBonuses.Add("TRUE_COMPLETIONIST");
                data.UnlockedTitles.Add("The Eternal");
            }

            SaveData();
            // GD.Print($"[MetaProgression] Recorded ending {ending}. Total playthroughs: {data.TotalPlaythroughs}");
        }

        /// <summary>
        /// Check if a specific bonus is unlocked
        /// </summary>
        public bool HasBonus(string bonusId)
        {
            return data.UnlockedBonuses.Contains(bonusId);
        }

        /// <summary>
        /// Check if a title is unlocked
        /// </summary>
        public bool HasTitle(string title)
        {
            return data.UnlockedTitles.Contains(title);
        }

        /// <summary>
        /// Get all unlocked titles for character creation
        /// </summary>
        public List<string> GetAvailableTitles()
        {
            var titles = new List<string> { "(None)" };
            titles.AddRange(data.UnlockedTitles);
            return titles;
        }

        #endregion

        #region NG+ Bonuses

        /// <summary>
        /// 1.3.10 (supervisor ruling 2026-11-03): the damage, healing, shop, stat, elite, seal and artifact reveal
        /// unlocks apply only to a character on cycle 2 or later (StoryProgressionSystem.CurrentCycle, per character
        /// and per session online). A new cycle 1 character made after an ending gets none of them. The unlocks that
        /// already applied on cycle 1 before 1.3.10 (Fear the Throne, Artifact Hunter, Survivor's Guilt, titles,
        /// prestige classes) are not gated here.
        /// </summary>
        private static bool InNewGamePlus => StoryProgressionSystem.Instance.CurrentCycle >= 2;

        /// <summary>
        /// Get the starting level bonus for NG+
        /// </summary>
        public int GetStartingLevelBonus()
        {
            if (HasBonus("MASTER")) return 10;
            if (HasBonus("VETERAN")) return 5;
            return 1;
        }

        /// <summary>
        /// Get XP multiplier based on unlocks. 1.3.10: Mortal Pride gives the +25 percent the Defiant ending's
        /// cycle bonus gives (its row says so); CycleSystem.CalculateCycleBonuses takes the larger of the two,
        /// so the Defiant cycle does not get it twice. Only the row's number counts; no other unlock adds XP.
        /// </summary>
        public float GetXPMultiplier()
        {
            float multiplier = 1.0f;
            if (HasBonus("MORTAL_PRIDE")) multiplier += 0.25f;
            return multiplier;
        }

        /// <summary>
        /// Get damage multiplier based on unlocks
        /// </summary>
        public float GetDamageMultiplier()
        {
            float multiplier = 1.0f;
            if (!InNewGamePlus) return multiplier;   // 1.3.10: NG+ characters only
            if (HasBonus("TYRANTS_AURA")) multiplier += 0.15f;
            return multiplier;
        }

        /// <summary>
        /// Get healing multiplier based on unlocks
        /// </summary>
        public float GetHealingMultiplier()
        {
            float multiplier = 1.0f;
            if (!InNewGamePlus) return multiplier;   // 1.3.10: NG+ characters only
            if (HasBonus("HEALING_LIGHT")) multiplier += 0.25f;
            return multiplier;
        }

        /// <summary>
        /// Fear the Throne: monsters deal 10 percent less damage (CombatEngine takes it off a monster's attack;
        /// 1.3.10: the unlock row says so). The name is kept from when the text spoke of fleeing.
        /// </summary>
        public int GetMonsterFleeReduction() => HasBonus("FEAR_THE_THRONE") ? 10 : 0;

        /// <summary>
        /// Get artifact effect multiplier (Artifact Hunter perk: +50% artifact stat bonuses)
        /// </summary>
        public float GetArtifactMultiplier() => HasBonus("ARTIFACT_HUNTER") ? 1.50f : 1.0f;

        /// <summary>
        /// Whether fallen companions may appear as ghost advisors (Survivor's Guilt perk)
        /// </summary>
        public bool HasSurvivorsGuilt => HasBonus("SURVIVORS_GUILT");

        /// <summary>
        /// Get shop discount based on unlocks
        /// </summary>
        public float GetShopDiscount()
        {
            float discount = 0f;
            if (!InNewGamePlus) return discount;   // 1.3.10: NG+ characters only
            if (HasBonus("BLESSED_COMMERCE")) discount += 0.10f;
            return discount;
        }

        /// <summary>
        /// Get stat multiplier for Ocean's Blessing
        /// </summary>
        public float GetStatMultiplier()
        {
            float multiplier = 1.0f;
            if (!InNewGamePlus) return multiplier;   // 1.3.10: NG+ characters only
            if (HasBonus("OCEANS_BLESSING")) multiplier += 0.15f;
            return multiplier;
        }

        /// <summary>
        /// Check if artifact locations should be revealed
        /// </summary>
        public bool ShouldRevealArtifactLocations()
        {
            return InNewGamePlus && HasBonus("ARTIFACT_MEMORY");   // 1.3.10: NG+ characters only
        }

        /// <summary>
        /// Check if player starts with ancient key
        /// </summary>
        public bool StartsWithAncientKey()
        {
            return HasBonus("ANCIENT_KEY");
        }

        /// <summary>
        /// Check if seals give double bonuses
        /// </summary>
        public bool SealsGiveDoubleBonus()
        {
            return InNewGamePlus && HasBonus("SEAL_RESONANCE");   // 1.3.10: NG+ characters only
        }

        /// <summary>
        /// Get rare monster spawn multiplier: Slayer makes an elite 25 percent more likely
        /// (MonsterGenerator.EliteRollChance). 1.3.10: only the row's number; no other unlock adds to it.
        /// </summary>
        public float GetRareMonsterMultiplier()
        {
            float multiplier = 1.0f;
            if (!InNewGamePlus) return multiplier;   // 1.3.10: NG+ characters only
            if (HasBonus("SLAYER")) multiplier += 0.25f;
            return multiplier;
        }

        #endregion

        #region Gallery/Stats Display

        /// <summary>
        /// Get formatted statistics for display
        /// </summary>
        public List<(string label, string value, string color)> GetFormattedStats()
        {
            var stats = new List<(string, string, string)>
            {
                ("Total Playthroughs", data.TotalPlaythroughs.ToString(), "white"),
                ("Highest Cycle", data.HighestCycle.ToString(), "bright_magenta"),
                ("Highest Level", data.HighestLevelAchieved.ToString(), "bright_cyan"),
                ("Total Monsters Killed", data.TotalMonstersKilled.ToString("N0"), "red"),
                ("Total Gold Earned", data.TotalGoldEarned.ToString("N0"), "yellow"),
                ("Endings Unlocked", $"{data.UnlockedEndings.Count}/5", "bright_green"),
                ("Titles Earned", data.UnlockedTitles.Count.ToString(), "bright_yellow"),
                ("Bonuses Unlocked", data.UnlockedBonuses.Count.ToString(), "cyan")
            };
            return stats;
        }

        /// <summary>
        /// Get ending completion status for gallery
        /// </summary>
        public List<(string name, bool unlocked, string color)> GetEndingGallery()
        {
            return new List<(string, bool, string)>
            {
                ("The Usurper (Dark Path)", UnlockedEndings.Contains(EndingType.Usurper), "dark_red"),
                ("The Savior (Light Path)", UnlockedEndings.Contains(EndingType.Savior), "bright_green"),
                ("The Defiant (Independent)", UnlockedEndings.Contains(EndingType.Defiant), "bright_yellow"),
                ("The Awakened (True Ending)", UnlockedEndings.Contains(EndingType.TrueEnding), "bright_cyan"),
                ("Dissolution (Secret)", UnlockedEndings.Contains(EndingType.Secret), "white")
            };
        }

        #endregion

        #region Persistence

        private MetaProgressionData LoadData()
        {
            try
            {
                if (File.Exists(saveFilePath))
                {
                    string json = File.ReadAllText(saveFilePath);
                    var loaded = JsonSerializer.Deserialize<MetaProgressionData>(json);
                    if (loaded != null)
                    {
                        // GD.Print($"[MetaProgression] Loaded data: {loaded.TotalPlaythroughs} playthroughs, {loaded.UnlockedEndings.Count} endings");
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance?.Log(DebugLogger.LogLevel.Error, "META", $"Failed to load meta progression: {ex.Message}");
            }

            return new MetaProgressionData();
        }

        private static readonly object _fileLock = new object();

        public void SaveData()
        {
            try
            {
                string? dir = Path.GetDirectoryName(saveFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(data, options);

                // Lock to prevent concurrent MUD sessions from overwriting each other
                lock (_fileLock)
                {
                    // Re-read and merge before writing to avoid losing other sessions' data
                    if (File.Exists(saveFilePath))
                    {
                        try
                        {
                            var diskData = JsonSerializer.Deserialize<MetaProgressionData>(File.ReadAllText(saveFilePath));
                            if (diskData != null)
                            {
                                // Merge: take the max of counters, union of sets
                                data.TotalPlaythroughs = Math.Max(data.TotalPlaythroughs, diskData.TotalPlaythroughs);
                                data.HighestCycle = Math.Max(data.HighestCycle, diskData.HighestCycle);
                                data.HighestLevelAchieved = Math.Max(data.HighestLevelAchieved, diskData.HighestLevelAchieved);
                                data.TotalMonstersKilled = Math.Max(data.TotalMonstersKilled, diskData.TotalMonstersKilled);
                                data.TotalGoldEarned = Math.Max(data.TotalGoldEarned, diskData.TotalGoldEarned);
                                foreach (var e in diskData.UnlockedEndings) data.UnlockedEndings.Add(e);
                                foreach (var b in diskData.UnlockedBonuses) data.UnlockedBonuses.Add(b);
                                foreach (var t in diskData.UnlockedTitles) data.UnlockedTitles.Add(t);
                            }
                        }
                        catch { /* disk file corrupted, overwrite it */ }
                    }

                    // Re-serialize with merged data
                    json = JsonSerializer.Serialize(data, options);
                    File.WriteAllText(saveFilePath, json);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance?.Log(DebugLogger.LogLevel.Error, "META", $"Failed to save meta progression: {ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>
    /// Data structure for meta progression persistence
    /// </summary>
    public class MetaProgressionData
    {
        public int TotalPlaythroughs { get; set; }
        public int HighestCycle { get; set; }
        public int HighestLevelAchieved { get; set; }
        public long TotalMonstersKilled { get; set; }
        public long TotalGoldEarned { get; set; }
        public HashSet<EndingType> UnlockedEndings { get; set; } = new();
        public HashSet<string> UnlockedBonuses { get; set; } = new();
        public HashSet<string> UnlockedTitles { get; set; } = new();
        public DateTime FirstPlaythrough { get; set; } = DateTime.Now;
        public DateTime LastPlaythrough { get; set; } = DateTime.Now;

        // Noctura betrayal tracking (persists across NG+ cycles)
        public int NocturaBetrayal { get; set; } = 0;     // 0=never, 1=betrayed+escaped, 2=betrayed+defeated
        public int NocturaTimesBetrayed { get; set; } = 0; // How many cycles the betrayal has occurred
    }
}
