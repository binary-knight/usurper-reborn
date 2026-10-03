using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// World Event System - Creates random daily events that affect the entire game world
    /// Events include: King's proclamations, economy fluctuations, plagues, festivals, wars
    /// Based on classic Usurper's dynamic world events from the Pascal code
    /// </summary>
    public class WorldEventSystem
    {
        private static WorldEventSystem _instance;
        public static WorldEventSystem Instance => _instance ??= new WorldEventSystem();

        private Random _random = Random.Shared;
        private List<WorldEvent> _activeEvents = new();
        private int _lastEventDay = 0;

        // Global modifiers applied by active events
        public float GlobalPriceModifier { get; private set; } = 1.0f;
        public float GlobalXPModifier { get; private set; } = 1.0f;
        public float GlobalGoldModifier { get; private set; } = 1.0f;
        public int GlobalStatModifier { get; private set; } = 0;
        public bool PlaguActive { get; private set; } = false;
        public bool WarActive { get; private set; } = false;
        public bool FestivalActive { get; private set; } = false;
        public string CurrentKingDecree { get; private set; } = "";

        /// <summary>
        /// Event types that can occur in the world
        /// </summary>
        public enum EventType
        {
            // King's Proclamations
            KingTaxIncrease,
            KingTaxDecrease,
            KingBounty,
            KingPardon,
            KingWarDeclaration,
            KingPeaceTreaty,
            KingFestivalDecree,
            KingMartialLaw,

            // Economy Events
            EconomyBoom,
            EconomyRecession,
            MerchantCaravan,
            BanditRaid,
            GoldRush,
            Inflation,

            // Plague/Disease Events
            PlagueOutbreak,
            PlagueEnds,
            CursedLand,
            BlessedRain,

            // Festival Events
            HarvestFestival,
            MidsummerCelebration,
            WinterSolstice,
            TournamentDay,
            HolyDay,

            // War/Conflict Events
            WarBegins,
            WarEnds,
            MonsterInvasion,
            DemonPortal,
            DragonSighting,
            BanditLordRises,

            // Misc Events
            EclipseDarkness,
            MeteorShower,
            AncientRelicFound,
            ProphetArrives,

            // v1.1.4: a world boss fell; +10 percent XP for a day (append-only)
            WorldBossVictory
        }

        /// <summary>
        /// Represents an active world event
        /// </summary>
        public class WorldEvent
        {
            public EventType Type { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public int DaysRemaining { get; set; }
            public int StartDay { get; set; }
            public Dictionary<string, float> Effects { get; set; } = new();
        }

        /// <summary>v1.2.5: the key id of an event type (KingTaxIncrease: king_tax_increase).</summary>
        internal static string EventKeyId(EventType type) =>
            System.Text.RegularExpressions.Regex.Replace(type.ToString(), "(?<!^)([A-Z])", "_$1").ToLowerInvariant();

        /// <summary>v1.2.5: an event's key for its title, description or decree; the world boss victory keeps its own.</summary>
        internal static string EventKey(EventType type, string part) => type == EventType.WorldBossVictory
            ? $"world_boss.victory_event_{part}"
            : $"world_event.{EventKeyId(type)}.{part}";

        /// <summary>
        /// v1.2.5: the text stored on an event (title, description, decree): English, as before 1.2.5, so saves,
        /// the shared world state and the web payload do not change with the writer's language.
        /// </summary>
        internal static string StoredText(EventType type, string part) => Loc.GetIn("en", EventKey(type, part));

        /// <summary>v1.2.5: an event's title in the reader's language (shown from its type, not the stored text).</summary>
        internal static string TitleLabel(WorldEvent evt) => Loc.Has(EventKey(evt.Type, "title")) ? Loc.Get(EventKey(evt.Type, "title")) : evt.Title;

        /// <summary>v1.2.5: an event's description in the reader's language.</summary>
        internal static string DescriptionLabel(WorldEvent evt) => Loc.Has(EventKey(evt.Type, "desc")) ? Loc.Get(EventKey(evt.Type, "desc")) : evt.Description;

        /// <summary>
        /// v1.2.5: a stored decree in the reader's language when it is one of the decrees CreateEvent stores
        /// (matched against their English text); any other is shown as stored.
        /// </summary>
        internal static string DecreeLabel(string? stored)
        {
            foreach (EventType type in Enum.GetValues(typeof(EventType)))
            {
                string key = EventKey(type, "decree");
                if (Loc.HasIn("en", key) && Loc.GetIn("en", key) == stored) return Loc.Get(key);
            }
            return stored ?? "";
        }

        /// <summary>v1.2.5: the events screen title, centred over the 39-column rule as the English one was.</summary>
        internal static string ScreenTitleRow(string title) =>
            new string(' ', Math.Max(0, 11 + (12 - title.Length) / 2)) + title;

        /// <summary>
        /// Process daily events - called during daily reset
        /// </summary>
        public async Task ProcessDailyEvents(int currentDay)
        {
            // Don't process if we already did today
            if (currentDay == _lastEventDay) return;
            _lastEventDay = currentDay;

            // Decrement duration on active events
            UpdateActiveEvents();

            // Chance for new event (30% per day)
            if (_random.Next(100) < 30)
            {
                var newEvent = GenerateRandomEvent(currentDay);
                if (newEvent != null)
                {
                    ActivateEvent(newEvent);
                }
            }

            // Recalculate global modifiers
            RecalculateGlobalModifiers();

            // Generate distant world news (flavor from regions the player never visits)
            GenerateDistantWorldNews(currentDay);

            await Task.CompletedTask;
        }

        /// <summary>
        /// Update active events, removing expired ones
        /// </summary>
        private void UpdateActiveEvents()
        {
            var expiredEvents = new List<WorldEvent>();

            foreach (var evt in _activeEvents)
            {
                evt.DaysRemaining--;
                if (evt.DaysRemaining <= 0)
                {
                    expiredEvents.Add(evt);
                    OnEventEnds(evt);
                }
            }

            foreach (var evt in expiredEvents)
            {
                _activeEvents.Remove(evt);
            }
        }

        /// <summary>
        /// Handle event ending
        /// </summary>
        private void OnEventEnds(WorldEvent evt)
        {
            var news = NewsSystem.Instance;
            if (news != null)
            {
                news.Newsy(true, Loc.Get("world_event.news_ended", TitleLabel(evt)));
            }

            // Special handling for certain event types
            switch (evt.Type)
            {
                case EventType.PlagueOutbreak:
                    PlaguActive = false;
                    news?.Newsy(true, Loc.Get("world_event.news_plague_subsided"));
                    break;
                case EventType.WarBegins:
                case EventType.KingWarDeclaration:
                    WarActive = false;
                    news?.Newsy(true, Loc.Get("world_event.news_peace_returned"));
                    break;
                case EventType.HarvestFestival:
                case EventType.MidsummerCelebration:
                case EventType.WinterSolstice:
                case EventType.KingFestivalDecree:
                    FestivalActive = false;
                    break;
            }
        }

        /// <summary>
        /// Generate a random event based on current world state
        /// </summary>
        private WorldEvent GenerateRandomEvent(int currentDay)
        {
            // Weight event types based on current state
            var possibleEvents = new List<EventType>();

            // Always possible events
            possibleEvents.AddRange(new[]
            {
                EventType.KingTaxIncrease, EventType.KingTaxDecrease,
                EventType.KingBounty, EventType.MerchantCaravan,
                EventType.EconomyBoom, EventType.EconomyRecession,
                EventType.AncientRelicFound, EventType.ProphetArrives
            });

            // Seasonal events based on day
            int season = (currentDay % 365) / 91; // 0-3 for seasons
            switch (season)
            {
                case 0: // Spring
                    possibleEvents.Add(EventType.BlessedRain);
                    possibleEvents.Add(EventType.MidsummerCelebration);
                    break;
                case 1: // Summer
                    possibleEvents.Add(EventType.TournamentDay);
                    possibleEvents.Add(EventType.DragonSighting);
                    break;
                case 2: // Fall
                    possibleEvents.Add(EventType.HarvestFestival);
                    possibleEvents.Add(EventType.BanditRaid);
                    break;
                case 3: // Winter
                    possibleEvents.Add(EventType.WinterSolstice);
                    possibleEvents.Add(EventType.CursedLand);
                    break;
            }

            // War-related events if no war active
            if (!WarActive)
            {
                possibleEvents.Add(EventType.KingWarDeclaration);
                possibleEvents.Add(EventType.WarBegins);
                possibleEvents.Add(EventType.MonsterInvasion);
            }

            // Plague events - outbreak if no plague active, end if plague active
            if (!PlaguActive)
            {
                possibleEvents.Add(EventType.PlagueOutbreak);
            }
            else
            {
                // Plague can end after it's been active
                possibleEvents.Add(EventType.PlagueEnds);
            }

            // Festival events if no festival active
            if (!FestivalActive)
            {
                possibleEvents.Add(EventType.KingFestivalDecree);
                possibleEvents.Add(EventType.HolyDay);
            }

            // Rare events (add with lower probability)
            if (_random.Next(100) < 10)
            {
                possibleEvents.Add(EventType.DemonPortal);
                possibleEvents.Add(EventType.EclipseDarkness);
                possibleEvents.Add(EventType.MeteorShower);
            }

            // Select random event
            var selectedType = possibleEvents[_random.Next(possibleEvents.Count)];
            return CreateEvent(selectedType, currentDay);
        }

        /// <summary>
        /// Create an event of the specified type
        /// </summary>
        private WorldEvent CreateEvent(EventType type, int currentDay)
        {
            var evt = new WorldEvent
            {
                Type = type,
                StartDay = currentDay,
                Effects = new Dictionary<string, float>()
            };

            switch (type)
            {
                // === KING'S PROCLAMATIONS ===
                case EventType.KingTaxIncrease:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(5, 15);
                    evt.Effects["price"] = 1.2f;
                    CurrentKingDecree = StoredText(type, "decree");
                    break;

                case EventType.KingTaxDecrease:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(5, 10);
                    evt.Effects["price"] = 0.85f;
                    CurrentKingDecree = StoredText(type, "decree");
                    break;

                case EventType.KingBounty:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(7, 14);
                    evt.Effects["gold"] = 1.5f;
                    CurrentKingDecree = StoredText(type, "decree");
                    break;

                case EventType.KingPardon:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(3, 7);
                    CurrentKingDecree = StoredText(type, "decree");
                    break;

                case EventType.KingWarDeclaration:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(14, 30);
                    evt.Effects["xp"] = 1.25f;
                    WarActive = true;
                    CurrentKingDecree = StoredText(type, "decree");
                    break;

                case EventType.KingPeaceTreaty:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(10, 20);
                    evt.Effects["price"] = 0.9f;
                    evt.Effects["gold"] = 1.2f;
                    WarActive = false;
                    CurrentKingDecree = StoredText(type, "decree");
                    break;

                case EventType.KingFestivalDecree:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(3, 7);
                    evt.Effects["xp"] = 1.1f;
                    evt.Effects["gold"] = 1.1f;
                    FestivalActive = true;
                    CurrentKingDecree = StoredText(type, "decree");
                    break;

                case EventType.KingMartialLaw:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(5, 10);
                    CurrentKingDecree = StoredText(type, "decree");
                    break;

                // === ECONOMY EVENTS ===
                case EventType.EconomyBoom:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(5, 12);
                    evt.Effects["price"] = 0.75f;
                    evt.Effects["gold"] = 1.2f;
                    break;

                case EventType.EconomyRecession:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(7, 14);
                    evt.Effects["price"] = 1.3f;
                    evt.Effects["gold"] = 0.8f;
                    break;

                case EventType.MerchantCaravan:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(3, 7);
                    evt.Effects["price"] = 0.8f;
                    break;

                case EventType.BanditRaid:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(3, 7);
                    evt.Effects["price"] = 1.25f;
                    break;

                case EventType.GoldRush:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(5, 10);
                    evt.Effects["gold"] = 1.5f;
                    break;

                case EventType.Inflation:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(5, 10);
                    evt.Effects["price"] = 2.0f;
                    break;

                // === PLAGUE/DISEASE EVENTS ===
                case EventType.PlagueOutbreak:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(10, 20);
                    evt.Effects["stat"] = -2f; // Stat penalty
                    PlaguActive = true;
                    break;

                case EventType.PlagueEnds:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(3, 7);
                    evt.Effects["price"] = 0.5f; // Healing price discount
                    PlaguActive = false;
                    break;

                case EventType.CursedLand:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(5, 10);
                    evt.Effects["stat"] = -3f;
                    break;

                case EventType.BlessedRain:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(3, 5);
                    evt.Effects["stat"] = 1f;
                    break;

                // === FESTIVAL EVENTS ===
                case EventType.HarvestFestival:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(3, 7);
                    evt.Effects["xp"] = 1.15f;
                    evt.Effects["price"] = 0.8f;
                    FestivalActive = true;
                    break;

                case EventType.MidsummerCelebration:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(3, 5);
                    evt.Effects["xp"] = 1.2f;
                    evt.Effects["gold"] = 1.1f;
                    FestivalActive = true;
                    break;

                case EventType.WinterSolstice:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(3, 5);
                    evt.Effects["gold"] = 1.3f;
                    FestivalActive = true;
                    break;

                case EventType.TournamentDay:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(1, 3);
                    evt.Effects["xp"] = 1.3f;
                    FestivalActive = true;
                    break;

                case EventType.HolyDay:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = 1;
                    FestivalActive = true;
                    break;

                // === WAR/CONFLICT EVENTS ===
                case EventType.WarBegins:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(14, 30);
                    evt.Effects["xp"] = 1.25f;
                    evt.Effects["gold"] = 1.2f;
                    WarActive = true;
                    break;

                case EventType.WarEnds:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(5, 10);
                    evt.Effects["xp"] = 1.1f;
                    WarActive = false;
                    break;

                case EventType.MonsterInvasion:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(5, 10);
                    evt.Effects["xp"] = 1.5f;
                    WarActive = true;
                    break;

                case EventType.DemonPortal:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(3, 7);
                    evt.Effects["xp"] = 1.75f;
                    evt.Effects["stat"] = -1f;
                    WarActive = true;
                    break;

                case EventType.DragonSighting:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(5, 10);
                    evt.Effects["gold"] = 1.5f;
                    break;

                case EventType.BanditLordRises:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(7, 14);
                    evt.Effects["price"] = 1.2f;
                    break;

                // === MISC EVENTS ===
                case EventType.EclipseDarkness:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(1, 3);
                    break;

                case EventType.MeteorShower:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(1, 3);
                    evt.Effects["xp"] = 1.2f;
                    break;

                case EventType.AncientRelicFound:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(5, 10);
                    break;

                case EventType.ProphetArrives:
                    evt.Title = StoredText(type, "title");
                    evt.Description = StoredText(type, "desc");
                    evt.DaysRemaining = _random.Next(3, 7);
                    evt.Effects["xp"] = 1.1f;
                    break;

                case EventType.WorldBossVictory:
                    evt.Title = Loc.Get("world_boss.victory_event_title");
                    evt.Description = Loc.Get("world_boss.victory_event_desc");
                    evt.DaysRemaining = 1;
                    evt.Effects["xp"] = 1.1f;
                    break;
            }

            return evt;
        }

        /// <summary>
        /// Activate a new event
        /// </summary>
        private void ActivateEvent(WorldEvent evt)
        {
            _activeEvents.Add(evt);

            // Generate news
            var news = NewsSystem.Instance;
            if (news != null)
            {
                news.Newsy(true, Loc.Get("world_event.news_event", TitleLabel(evt), DescriptionLabel(evt)));

                // Add king's decree if applicable
                if (!string.IsNullOrEmpty(CurrentKingDecree) && evt.Type.ToString().StartsWith("King"))
                {
                    news.Newsy(true, DecreeLabel(CurrentKingDecree));
                }
            }

            // GD.Print($"[WorldEvent] Activated: {evt.Title} ({evt.DaysRemaining} days)");
        }

        /// <summary>
        /// Recalculate global modifiers from all active events
        /// </summary>
        private void RecalculateGlobalModifiers()
        {
            // Reset to defaults
            GlobalPriceModifier = 1.0f;
            GlobalXPModifier = 1.0f;
            GlobalGoldModifier = 1.0f;
            GlobalStatModifier = 0;

            // Apply all active event effects
            foreach (var evt in _activeEvents)
            {
                if (evt.Effects.TryGetValue("price", out float priceEffect))
                    GlobalPriceModifier *= priceEffect;

                if (evt.Effects.TryGetValue("xp", out float xpEffect))
                    GlobalXPModifier *= xpEffect;

                if (evt.Effects.TryGetValue("gold", out float goldEffect))
                    GlobalGoldModifier *= goldEffect;

                if (evt.Effects.TryGetValue("stat", out float statEffect))
                    GlobalStatModifier += (int)statEffect;
            }

            // Clamp modifiers to reasonable ranges
            GlobalPriceModifier = Math.Max(0.25f, Math.Min(4.0f, GlobalPriceModifier));
            GlobalXPModifier = Math.Max(0.5f, Math.Min(3.0f, GlobalXPModifier));
            GlobalGoldModifier = Math.Max(0.25f, Math.Min(3.0f, GlobalGoldModifier));
            GlobalStatModifier = Math.Max(-10, Math.Min(10, GlobalStatModifier));
        }

        /// <summary>
        /// Get adjusted price with world event modifiers
        /// </summary>
        public long GetAdjustedPrice(long basePrice)
        {
            return (long)(basePrice * GlobalPriceModifier);
        }

        /// <summary>
        /// Get adjusted XP with world event modifiers
        /// </summary>
        public long GetAdjustedXP(long baseXP)
        {
            return (long)(baseXP * GlobalXPModifier);
        }

        /// <summary>
        /// Get adjusted gold with world event modifiers
        /// </summary>
        public long GetAdjustedGold(long baseGold)
        {
            return (long)(baseGold * GlobalGoldModifier);
        }

        // v1.1.12: the world event's own share of a reward, for the combat bonus line.
        public long GetWorldEventXPBonus(long baseXP) => GetAdjustedXP(baseXP) - baseXP;
        public long GetWorldEventGoldBonus(long baseGold) => GetAdjustedGold(baseGold) - baseGold;

        /// <summary>
        /// Check if a location is accessible based on current events
        /// </summary>
        public (bool accessible, string reason) IsLocationAccessible(string locationName)
        {
            // Martial law closes Dark Alley
            foreach (var evt in _activeEvents)
            {
                if (evt.Type == EventType.KingMartialLaw)
                {
                    if (locationName.ToLower().Contains("dark") || locationName.ToLower().Contains("alley"))
                    {
                        return (false, Loc.Get("world_event.martial_law_closed"));
                    }
                }
            }

            return (true, null);
        }

        /// <summary>
        /// v1.1.15: a world disaster for Mental witnessing: plague, cursed land, a bandit raid, a
        /// monster invasion, a demon portal or a dragon sighting.
        /// </summary>
        public static bool IsDisaster(EventType type) => type is EventType.PlagueOutbreak or EventType.CursedLand
            or EventType.BanditRaid or EventType.MonsterInvasion or EventType.DemonPortal or EventType.DragonSighting;

        /// <summary>v1.1.15: true while any active event is a disaster (IsDisaster).</summary>
        public bool HasActiveDisaster => _activeEvents.Any(e => IsDisaster(e.Type));

        /// <summary>
        /// Check if player should take plague damage
        /// </summary>
        public bool ShouldTakePlagueDamage()
        {
            if (!PlaguActive) return false;
            return _random.Next(100) < 15; // 15% chance per action during plague
        }

        /// <summary>
        /// Get plague damage amount
        /// </summary>
        public int GetPlagueDamage(int maxHP)
        {
            return Math.Max(1, maxHP / 20); // 5% of max HP
        }

        /// <summary>
        /// Get all active events
        /// </summary>
        public List<WorldEvent> GetActiveEvents()
        {
            return new List<WorldEvent>(_activeEvents);
        }

        /// <summary>
        /// Display current world status to terminal
        /// </summary>
        public void DisplayWorldStatus(TerminalEmulator terminal)
        {
            terminal.SetColor("bright_cyan");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("═══════════════════════════════════════");
            terminal.WriteLine(ScreenTitleRow(Loc.Get("world_event.screen_title")));
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("═══════════════════════════════════════");
            terminal.WriteLine("");

            if (_activeEvents.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("world_event.none_active")}");
            }
            else
            {
                foreach (var evt in _activeEvents)
                {
                    // Color based on event type
                    string color = evt.Type switch
                    {
                        EventType.PlagueOutbreak or EventType.CursedLand => "red",
                        EventType.WarBegins or EventType.MonsterInvasion or EventType.DemonPortal => "bright_red",
                        EventType.HarvestFestival or EventType.MidsummerCelebration or EventType.WinterSolstice => "bright_green",
                        EventType.KingTaxIncrease or EventType.Inflation => "yellow",
                        EventType.EconomyBoom or EventType.GoldRush => "bright_yellow",
                        _ => "white"
                    };

                    terminal.SetColor(color);
                    UsurperRemake.UI.UIHelper.WriteWrapped(terminal, $"* {TitleLabel(evt)}", "  ");
                    terminal.SetColor("gray");
                    UsurperRemake.UI.UIHelper.WriteWrapped(terminal, DescriptionLabel(evt), "    ");
                    terminal.SetColor("dark_gray");
                    terminal.WriteLine($"    {Loc.Get("world_event.days_remaining", evt.DaysRemaining)}");
                    terminal.WriteLine("");
                }
            }

            // Show global modifiers if not default
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("world_event.modifiers"));
            terminal.SetColor("white");

            if (Math.Abs(GlobalPriceModifier - 1.0f) > 0.01f)
            {
                string priceColor = GlobalPriceModifier > 1.0f ? "red" : "green";
                terminal.SetColor(priceColor);
                terminal.WriteLine($"  {Loc.Get("world_event.mod_prices", $"{(GlobalPriceModifier > 1.0f ? "+" : "")}{((GlobalPriceModifier - 1.0f) * 100):F0}")}");
            }

            if (Math.Abs(GlobalXPModifier - 1.0f) > 0.01f)
            {
                string xpColor = GlobalXPModifier > 1.0f ? "green" : "red";
                terminal.SetColor(xpColor);
                terminal.WriteLine($"  {Loc.Get("world_event.mod_xp", $"{(GlobalXPModifier > 1.0f ? "+" : "")}{((GlobalXPModifier - 1.0f) * 100):F0}")}");
            }

            if (Math.Abs(GlobalGoldModifier - 1.0f) > 0.01f)
            {
                string goldColor = GlobalGoldModifier > 1.0f ? "bright_yellow" : "red";
                terminal.SetColor(goldColor);
                terminal.WriteLine($"  {Loc.Get("world_event.mod_gold", $"{(GlobalGoldModifier > 1.0f ? "+" : "")}{((GlobalGoldModifier - 1.0f) * 100):F0}")}");
            }

            if (GlobalStatModifier != 0)
            {
                string statColor = GlobalStatModifier > 0 ? "green" : "red";
                terminal.SetColor(statColor);
                terminal.WriteLine($"  {Loc.Get("world_event.mod_stats", $"{(GlobalStatModifier > 0 ? "+" : "")}{GlobalStatModifier}")}");
            }

            // Show king's decree if any
            if (!string.IsNullOrEmpty(CurrentKingDecree))
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("world_event.royal_decree"));
                terminal.SetColor("yellow");
                UsurperRemake.UI.UIHelper.WriteWrapped(terminal, $"\"{DecreeLabel(CurrentKingDecree)}\"", "  ");
            }

            terminal.SetColor("white");
        }

        /// <summary>
        /// Force an event for testing or special circumstances
        /// </summary>
        public void ForceEvent(EventType type, int day)
        {
            var evt = CreateEvent(type, day);
            if (evt != null)
            {
                ActivateEvent(evt);
                RecalculateGlobalModifiers();
            }
        }

        /// <summary>
        /// Clear all events (for new game)
        /// </summary>
        public void ClearAllEvents()
        {
            _activeEvents.Clear();
            _lastEventDay = 0;
            GlobalPriceModifier = 1.0f;
            GlobalXPModifier = 1.0f;
            GlobalGoldModifier = 1.0f;
            GlobalStatModifier = 0;
            PlaguActive = false;
            WarActive = false;
            FestivalActive = false;
            CurrentKingDecree = "";
        }

        /// <summary>
        /// Restore world events from save data
        /// </summary>
        public void RestoreFromSaveData(List<WorldEventData> savedEvents, int currentDay)
        {
            ClearAllEvents();
            _lastEventDay = currentDay;

            if (savedEvents == null || savedEvents.Count == 0)
            {
                // GD.Print("[WorldEvent] No saved events to restore");
                return;
            }

            foreach (var eventData in savedEvents)
            {
                // Handle global state entry
                if (eventData.Type == "GlobalState")
                {
                    if (eventData.Parameters.TryGetValue("PlaguActive", out var plague))
                        PlaguActive = ConvertToBoolean(plague);
                    if (eventData.Parameters.TryGetValue("WarActive", out var war))
                        WarActive = ConvertToBoolean(war);
                    if (eventData.Parameters.TryGetValue("FestivalActive", out var festival))
                        FestivalActive = ConvertToBoolean(festival);
                    if (!string.IsNullOrEmpty(eventData.Description))
                        CurrentKingDecree = eventData.Description;
                    continue;
                }

                // Parse event type
                if (!Enum.TryParse<EventType>(eventData.Type, out var eventType))
                {
                    // GD.Print($"[WorldEvent] Unknown event type: {eventData.Type}");
                    continue;
                }

                // Get days remaining
                int daysRemaining = 1;
                if (eventData.Parameters.TryGetValue("DaysRemaining", out var days))
                    daysRemaining = ConvertToInt32(days);

                int startDay = currentDay;
                if (eventData.Parameters.TryGetValue("StartDay", out var start))
                    startDay = ConvertToInt32(start);

                // Create the event
                var evt = new WorldEvent
                {
                    Type = eventType,
                    Title = eventData.Title,
                    Description = eventData.Description,
                    DaysRemaining = daysRemaining,
                    StartDay = startDay,
                    Effects = new Dictionary<string, float>()
                };

                // Restore effects
                foreach (var param in eventData.Parameters)
                {
                    if (param.Key.StartsWith("Effect_"))
                    {
                        string effectKey = param.Key.Substring(7); // Remove "Effect_" prefix
                        evt.Effects[effectKey] = ConvertToSingle(param.Value);
                    }
                }

                _activeEvents.Add(evt);
                // GD.Print($"[WorldEvent] Restored: {evt.Title} ({evt.DaysRemaining} days remaining)");
            }

            // Recalculate modifiers from restored events
            RecalculateGlobalModifiers();
            // GD.Print($"[WorldEvent] Restored {_activeEvents.Count} active events");
        }

        /// <summary>
        /// Helper to convert object (possibly JsonElement) to int
        /// </summary>
        private int ConvertToInt32(object value)
        {
            if (value is System.Text.Json.JsonElement jsonElement)
            {
                return jsonElement.GetInt32();
            }
            return Convert.ToInt32(value);
        }

        /// <summary>
        /// Helper to convert object (possibly JsonElement) to bool
        /// </summary>
        private bool ConvertToBoolean(object value)
        {
            if (value is System.Text.Json.JsonElement jsonElement)
            {
                return jsonElement.GetBoolean();
            }
            return Convert.ToBoolean(value);
        }

        /// <summary>
        /// Helper to convert object (possibly JsonElement) to float
        /// </summary>
        private float ConvertToSingle(object value)
        {
            if (value is System.Text.Json.JsonElement jsonElement)
            {
                return jsonElement.GetSingle();
            }
            return Convert.ToSingle(value);
        }

        // ═══════════════════════════════════════════════════════════════
        // DISTANT WORLD NEWS — flavor events from regions the player
        // never visits, making the world feel larger
        // ═══════════════════════════════════════════════════════════════

        private static readonly string[] DistantRegions = {
            "Ashenmoor", "the Northern Reaches", "the Iron Coast", "the Verdant Expanse",
            "Crownhaven", "the Sunken Isles", "Duskhollow", "Stormbreak"
        };

        private readonly List<string> _recentNewsCategories = new();
        private int _lastDistantNewsDay = 0;

        // v1.2.5: each category is the prefix of its keys (world_event.distant_<category>_<n>, {0} a region,
        // {1} a second region); the prefix is also the id the repeat tracking compares.
        private static readonly (string Category, int Count)[] DistantNewsTemplates =
        {
            ("world_event.distant_war", 7), ("world_event.distant_trade", 7), ("world_event.distant_plague", 5),
            ("world_event.distant_discovery", 6), ("world_event.distant_political", 6),
            ("world_event.distant_disaster", 6), ("world_event.distant_monster", 7),
        };

        // v1.2.5: world_event.distant_player_<n>, {0} a region, {1} the player
        private const string PlayerReferencedPrefix = "world_event.distant_player";
        private const int PlayerReferencedCount = 7;

        /// <summary>
        /// Generate 1-2 flavor news items from distant regions the player never visits.
        /// Called from ProcessDailyEvents.
        /// </summary>
        public void GenerateDistantWorldNews(int currentDay)
        {
            if (currentDay == _lastDistantNewsDay) return;
            _lastDistantNewsDay = currentDay;

            var news = NewsSystem.Instance;
            if (news == null) return;

            int count = _random.Next(1, 3); // 1-2 news items

            for (int i = 0; i < count; i++)
            {
                string? message = GenerateOneDistantNews();
                if (message != null)
                {
                    string starPrefix = GameConfig.ScreenReaderMode ? "" : "☆ ";
                    news.Newsy($"{starPrefix}{message}");
                }
            }
        }

        private string? GenerateOneDistantNews()
        {
            // 15% chance of player-referenced news if any players exist
            if (_random.Next(100) < 15)
            {
                var playerNews = TryGeneratePlayerReferencedNews();
                if (playerNews != null) return playerNews;
            }

            // Pick a category, avoiding recent repeats
            string category;
            int attempts = 0;
            do
            {
                var pool = DistantNewsTemplates[_random.Next(DistantNewsTemplates.Length)];
                category = pool.Category;
                if (!_recentNewsCategories.Contains(category) || attempts > 10)
                {
                    var (region1, region2) = PickRegions();
                    string result = Loc.Get($"{category}_{_random.Next(1, pool.Count + 1)}", region1, region2);
                    TrackCategory(category);
                    return result;
                }
                attempts++;
            } while (attempts <= 15);

            return null;
        }

        /// <summary>
        /// Set by game systems when a notable player name is available for news references.
        /// </summary>
        public string? NotablePlayerName { get; set; }

        private string? TryGeneratePlayerReferencedNews()
        {
            var playerName = NotablePlayerName;
            if (string.IsNullOrEmpty(playerName)) return null;

            var (region1, _) = PickRegions();
            return Loc.Get($"{PlayerReferencedPrefix}_{_random.Next(1, PlayerReferencedCount + 1)}", region1, playerName);
        }

        /// <summary>Two different distant regions (proper names, not translated).</summary>
        private (string, string) PickRegions()
        {
            string region1 = DistantRegions[_random.Next(DistantRegions.Length)];
            string region2;
            do { region2 = DistantRegions[_random.Next(DistantRegions.Length)]; }
            while (region2 == region1);
            return (region1, region2);
        }

        private void TrackCategory(string category)
        {
            _recentNewsCategories.Add(category);
            while (_recentNewsCategories.Count > 5)
                _recentNewsCategories.RemoveAt(0);
        }
    }
}
