using UsurperRemake.Systems;
using UsurperRemake.Utils;
using System;
using System.Collections.Generic;
using System.IO;

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

    /// <summary>
    /// When set, Newsy() routes messages here instead of to file/DB.
    /// Used by world sim catch-up to collect events for the summary.
    /// </summary>
    private List<string>? _catchUpBuffer;
    private int _catchUpBufferMax = int.MaxValue;
    public void SetCatchUpBuffer(List<string> buffer, int maxSize = int.MaxValue)
    {
        lock (_newsLock)
        {
            _catchUpBuffer = buffer;
            _catchUpBufferMax = maxSize;
        }
    }
    public void ClearCatchUpBuffer()
    {
        lock (_newsLock)
        {
            _catchUpBuffer = null;
            _catchUpBufferMax = int.MaxValue;
        }
    }

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
            // During catch-up, route to buffer instead of file/DB (checked inside lock for thread safety)
            if (_catchUpBuffer != null)
            {
                if (_catchUpBuffer.Count < _catchUpBufferMax)
                    _catchUpBuffer.Add(message);
                return;
            }

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
                DebugLogger.Instance.Log(DebugLogger.LogLevel.Debug, "SYSTEM", $"Swallowed exception: {ex.Message}");
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
        string prefix = GameConfig.ScreenReaderMode ? "" : "† ";
        Newsy(prefix + Loc.Get("news.death", playerName, killerName, location));
    }

    public void WriteBirthNews(string motherName, string fatherName, string childName, bool isNPC = false)
    {
        string prefix = GameConfig.ScreenReaderMode ? "" : "♥ ";
        Newsy(prefix + Loc.Get("news.birth", motherName, fatherName, childName));
    }

    public void WriteNaturalDeathNews(string npcName, int age, string race)
    {
        string prefix = GameConfig.ScreenReaderMode ? "" : "⚱ ";
        Newsy(prefix + Loc.Get("news.natural_death", npcName, race, age));
    }

    public void WriteComingOfAgeNews(string childName, string motherName, string fatherName)
    {
        Newsy(Loc.Get("news.coming_of_age", childName, motherName, fatherName));
    }

    public void WriteBirthdayNews(string npcName, int age, string race)
    {
        string prefix = GameConfig.ScreenReaderMode ? "" : "🎂 ";
        Newsy(prefix + Loc.Get("news.birthday", npcName, race, age, GetOrdinalSuffix(age)));
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
        string prefix = GameConfig.ScreenReaderMode ? "" : "⬆ ";
        Newsy(prefix + Loc.Get("news.npc_level_up", npcName, race, className, level));
    }

    public void WriteMarriageNews(string player1Name, string player2Name, string location = "Temple")
    {
        string prefix = GameConfig.ScreenReaderMode ? "" : "♥ ";
        Newsy(prefix + Loc.Get("news.marriage", player1Name, player2Name, MarriagePlace(location)));
    }

    /// <summary>v1.2.5: the wedding place the callers pass (Church, Castle, Temple) in the writer's language; another one as given.</summary>
    internal static string MarriagePlace(string location) => location switch
    {
        "Church" => Loc.Get("news.place_church"),
        "Castle" => Loc.Get("news.place_castle"),
        "Temple" => Loc.Get("news.place_temple"),
        _ => location
    };

    public void WriteDivorceNews(string player1Name, string player2Name)
    {
        string prefix = GameConfig.ScreenReaderMode ? "" : "✗ ";
        Newsy(prefix + Loc.Get("news.divorce", player1Name, player2Name));
    }

    public void WriteAffairNews(string npcName, string loverName)
    {
        string prefix = GameConfig.ScreenReaderMode ? "" : "💋 ";
        Newsy(prefix + Loc.Get("news.affair", npcName, loverName));
    }

    public void WriteRoyalNews(string kingName, string proclamation)
    {
        string prefix = GameConfig.ScreenReaderMode ? "" : "♔ ";
        Newsy(prefix + Loc.Get("news.royal_proclaims", kingName, proclamation));
    }

    public void WriteHolyNews(string godName, string event_description)
    {
        string prefix = GameConfig.ScreenReaderMode ? "" : "✝ ";
        Newsy($"{prefix}{godName}: {event_description}");
    }

    public void WriteQuestNews(string playerName, string questDescription, bool completed = true)
    {
        string prefix = GameConfig.ScreenReaderMode ? "" : "⚔ ";
        Newsy(prefix + Loc.Get(completed ? "news.quest_completed" : "news.quest_failed", playerName, questDescription));
    }

    public void WriteTeamNews(string teamName, string event_description)
    {
        string prefix = GameConfig.ScreenReaderMode ? "" : "⚑ ";
        Newsy(prefix + Loc.Get("news.team", teamName, event_description));
    }

    public void WritePrisonNews(string playerName, string event_description)
    {
        string prefix = GameConfig.ScreenReaderMode ? "" : "⛓ ";
        Newsy($"{prefix}{playerName}: {event_description}");
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
                DebugLogger.Instance.Log(DebugLogger.LogLevel.Debug, "SYSTEM", $"Swallowed exception: {ex.Message}");
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

    // Keywords that indicate gossip-worthy NPC events (marriages, deaths, affairs, births, etc.)
    private static readonly string[] GossipKeywords = new[]
    {
        "married", "divorced", "expecting a child", "proud parents", "born",
        "passed away", "soul moves on", "come of age", "joined the realm",
        "affair", "Scandal", "polyamorous", "attacked you in the Arena",
        "Level", "birthday", "celebrates their"
    };

    /// <summary>
    /// v1.2.5: the gossip keywords: the English ones, which match news written in English, and the
    /// reader's language's ones (news.gossip_keywords, pieces of that language's news texts), which match
    /// news written in that language. News is written in the writer's language, so offline it is the reader's.
    /// </summary>
    internal static List<string> GossipKeywordsForReader()
    {
        var words = new List<string>(GossipKeywords);
        if (GameConfig.Language != "en")
            words.AddRange(Loc.Get("news.gossip_keywords").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return words;
    }

    /// <summary>
    /// Get recent gossip-worthy news items (marriages, deaths, affairs, births, level-ups, etc.)
    /// Returns items from the in-memory cache, filtered to interesting NPC events.
    /// </summary>
    public List<string> GetRecentGossip(int maxEntries = 4)
    {
        var allNews = GetTodaysNews();
        var keywords = GossipKeywordsForReader();

        var gossip = allNews
            .Where(line => keywords.Any(kw => line.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // If not enough from today's cache, try the full file
        if (gossip.Count < maxEntries)
        {
            var fileNews = ReadNews();
            var fileGossip = fileNews
                .Where(line => keywords.Any(kw => line.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                .Where(line => !gossip.Contains(line))
                .ToList();
            gossip.AddRange(fileGossip);
        }

        // Return the most recent entries
        return gossip.TakeLast(maxEntries).ToList();
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

                // Trim news file to last 100 lines
                if (File.Exists(_newsFilePath))
                {
                    var lines = File.ReadAllLines(_newsFilePath);
                    if (lines.Length > 100)
                    {
                        var trimmed = lines.Skip(lines.Length - 100).ToArray();
                        File.WriteAllLines(_newsFilePath, trimmed);
                    }
                }

                // Add a new day marker
                Newsy($"═══ {Loc.Get("news.new_day")} ═══");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.Log(DebugLogger.LogLevel.Debug, "SYSTEM", $"Swallowed exception: {ex.Message}");
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
                File.WriteAllText(_newsFilePath, $"═══ {Loc.Get("news.daily_news_title")} ═══\n");
            }
        }
        catch (Exception ex)
            {
                DebugLogger.Instance.Log(DebugLogger.LogLevel.Debug, "SYSTEM", $"Swallowed exception: {ex.Message}");
            }
    }

    private int _writesSinceTrim = 0;
    private const int MaxNewsLines = 100;
    private const int TrimCheckInterval = 50; // Check every 50 writes

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

            // Periodic trim to prevent unbounded growth
            _writesSinceTrim++;
            if (_writesSinceTrim >= TrimCheckInterval)
            {
                _writesSinceTrim = 0;
                TrimNewsFile();
            }
        }
        catch (Exception ex)
            {
                DebugLogger.Instance.Log(DebugLogger.LogLevel.Debug, "SYSTEM", $"Swallowed exception: {ex.Message}");
            }
    }

    private void TrimNewsFile()
    {
        try
        {
            if (!File.Exists(_newsFilePath)) return;
            var lines = File.ReadAllLines(_newsFilePath);
            if (lines.Length > MaxNewsLines)
            {
                var trimmed = lines.Skip(lines.Length - MaxNewsLines).ToArray();
                File.WriteAllLines(_newsFilePath, trimmed);
            }
        }
        catch { }
    }

    #endregion
}
