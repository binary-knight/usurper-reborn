using UsurperRemake.Utils;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using FileAccess = Godot.FileAccess;
using System.Text;
using System.Globalization;
using System.Linq;

/// <summary>
/// Simplified News System - Single unified news feed
/// Consolidates all world events, combat, marriages, etc. into one news stream
/// </summary>
public partial class NewsSystem
{
    private static NewsSystem _instance;
    private readonly List<string> _todaysNews = new();
    private readonly object _newsLock = new object();
    private string _newsFilePath;

    /// <summary>
    /// Optional callback for persisting news to a database.
    /// Set by WorldSimService to route NPC activities to SQLite for the website activity feed.
    /// </summary>
    public static Action<string>? DatabaseCallback { get; set; }

    public static NewsSystem Instance
    {
        get
        {
            if (_instance == null)
                _instance = new NewsSystem();
            return _instance;
        }
    }

    private NewsSystem()
    {
        // Single news file in the scores directory
        _newsFilePath = Path.Combine(GameConfig.ScoreDir, "NEWS.txt");
        InitializeNewsFile();
    }

    /// <summary>
    /// Write a news entry (primary method - all other methods route here)
    /// </summary>
    public void Newsy(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        lock (_newsLock)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm");
                string formattedMessage = $"[{timestamp}] {message}";

                // Add to today's cache
                _todaysNews.Add(formattedMessage);

                // Keep cache manageable (last 100 entries)
                if (_todaysNews.Count > 100)
                    _todaysNews.RemoveAt(0);

                // Write to file
                AppendToNewsFile(formattedMessage);

                // Persist to database if callback is set (WorldSimService)
                DatabaseCallback?.Invoke(message);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"NewsSystem error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Overload for Pascal compatibility (newsToAnsi parameter ignored)
    /// </summary>
    public void Newsy(bool newsToAnsi, string message)
    {
        Newsy(message);
    }

    /// <summary>
    /// Overload for Pascal compatibility with header
    /// </summary>
    public void Newsy(bool newsToAnsi, string header, string message)
    {
        var combined = string.IsNullOrEmpty(header) ? message : $"{header} {message}";
        Newsy(combined);
    }

    /// <summary>
    /// Overload for Pascal compatibility with header and extra
    /// </summary>
    public void Newsy(bool newsToAnsi, string header, string message, string extra)
    {
        var combined = string.Join(" ", new[] { header, message, extra }.Where(s => !string.IsNullOrEmpty(s)));
        Newsy(combined);
    }

    /// <summary>
    /// Overload with category (category now ignored - all news unified)
    /// </summary>
    public void Newsy(string message, bool newsToAnsi, GameConfig.NewsCategory category)
    {
        Newsy(message);
    }

    /// <summary>
    /// Generic news writer (category ignored - routed to unified feed)
    /// </summary>
    public void GenericNews(GameConfig.NewsCategory newsType, bool newsToAnsi, string message)
    {
        Newsy(message);
    }

    /// <summary>
    /// Write news with category prefix (simplified)
    /// </summary>
    public void WriteNews(GameConfig.NewsCategory category, string message, bool includeTime = true)
    {
        Newsy(message);
    }

    // === Specialized news methods (all route to unified Newsy) ===

    public void WriteDeathNews(string playerName, string killerName, string location)
    {
        Newsy($"† {playerName} was slain by {killerName} at {location}!");
    }

    public void WriteBirthNews(string motherName, string fatherName, string childName, bool isNPC = false)
    {
        Newsy($"♥ {motherName} and {fatherName} are proud parents of {childName}!");
    }

    public void WriteNaturalDeathNews(string npcName, int age, string race)
    {
        Newsy($"⚱ {npcName}, a {race} of {age} years, has passed away peacefully. The soul moves on...");
    }

    public void WriteComingOfAgeNews(string childName, string motherName, string fatherName)
    {
        Newsy($"{childName}, child of {motherName} and {fatherName}, has come of age and joined the realm!");
    }

    public void WriteBirthdayNews(string npcName, int age, string race)
    {
        Newsy($"🎂 {npcName} the {race} celebrates their {age}{GetOrdinalSuffix(age)} birthday!");
    }

    private static string GetOrdinalSuffix(int number)
    {
        int lastTwo = number % 100;
        if (lastTwo >= 11 && lastTwo <= 13) return "th";
        return (number % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th"
        };
    }

    public void WriteNPCLevelUpNews(string npcName, int level, string className, string race)
    {
        Newsy($"⬆ {npcName} the {race} {className} has achieved Level {level}!");
    }

    public void WriteMarriageNews(string player1Name, string player2Name, string location = "Temple")
    {
        Newsy($"♥ {player1Name} and {player2Name} were married at the {location}!");
    }

    public void WriteDivorceNews(string player1Name, string player2Name)
    {
        Newsy($"✗ {player1Name} and {player2Name} have divorced!");
    }

    public void WriteAffairNews(string npcName, string loverName)
    {
        Newsy($"💋 Scandal! {npcName} and {loverName} are having a secret affair!");
    }

    public void WriteRoyalNews(string kingName, string proclamation)
    {
        Newsy($"♔ King {kingName} proclaims: {proclamation}");
    }

    public void WriteHolyNews(string godName, string event_description)
    {
        Newsy($"✝ {godName}: {event_description}");
    }

    public void WriteQuestNews(string playerName, string questDescription, bool completed = true)
    {
        string status = completed ? "completed" : "failed";
        Newsy($"⚔ {playerName} {status} quest: {questDescription}");
    }

    public void WriteTeamNews(string teamName, string event_description)
    {
        Newsy($"⚑ Team {teamName}: {event_description}");
    }

    public void WritePrisonNews(string playerName, string event_description)
    {
        Newsy($"⛓ {playerName}: {event_description}");
    }

    /// <summary>
    /// Read all news entries
    /// </summary>
    public List<string> ReadNews()
    {
        var news = new List<string>();

        try
        {
            if (File.Exists(_newsFilePath))
            {
                var lines = File.ReadAllLines(_newsFilePath);
                news.AddRange(lines.Where(l => !string.IsNullOrWhiteSpace(l)));
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Error reading news: {ex.Message}");
        }

        return news;
    }

    /// <summary>
    /// Read news (category parameter kept for compatibility but ignored)
    /// </summary>
    public List<string> ReadNews(GameConfig.NewsCategory category, bool readAnsi = true)
    {
        return ReadNews();
    }

    /// <summary>
    /// Get today's cached news
    /// </summary>
    public List<string> GetTodaysNews()
    {
        lock (_newsLock)
        {
            return new List<string>(_todaysNews);
        }
    }

    /// <summary>
    /// Get today's cached news (category parameter kept for compatibility)
    /// </summary>
    public List<string> GetTodaysNews(GameConfig.NewsCategory category)
    {
        return GetTodaysNews();
    }

    /// <summary>
    /// Daily maintenance - clear old news, keep file manageable
    /// </summary>
    public void ProcessDailyNewsMaintenance()
    {
        lock (_newsLock)
        {
            try
            {
                // Clear today's cache
                _todaysNews.Clear();

                // Trim news file to last 200 lines
                if (File.Exists(_newsFilePath))
                {
                    var lines = File.ReadAllLines(_newsFilePath);
                    if (lines.Length > 200)
                    {
                        var trimmed = lines.Skip(lines.Length - 200).ToArray();
                        File.WriteAllLines(_newsFilePath, trimmed);
                    }
                }

                // Add a new day marker
                Newsy("═══ New Day ═══");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"News maintenance error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Get news statistics
    /// </summary>
    public Dictionary<string, object> GetNewsStatistics()
    {
        var stats = new Dictionary<string, object>();

        lock (_newsLock)
        {
            stats["TodayCount"] = _todaysNews.Count;

            if (File.Exists(_newsFilePath))
            {
                try
                {
                    stats["TotalCount"] = File.ReadAllLines(_newsFilePath).Length;
                }
                catch
                {
                    stats["TotalCount"] = 0;
                }
            }
            else
            {
                stats["TotalCount"] = 0;
            }
        }

        return stats;
    }

    #region Private Methods

    private void InitializeNewsFile()
    {
        try
        {
            string directory = Path.GetDirectoryName(_newsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(_newsFilePath))
            {
                File.WriteAllText(_newsFilePath, "═══ Usurper Daily News ═══\n");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Error initializing news file: {ex.Message}");
        }
    }

    private void AppendToNewsFile(string message)
    {
        try
        {
            string directory = Path.GetDirectoryName(_newsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var stream = new FileStream(_newsFilePath, FileMode.Append, System.IO.FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                writer.WriteLine(message);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Error writing news: {ex.Message}");
        }
    }

    #endregion
}
