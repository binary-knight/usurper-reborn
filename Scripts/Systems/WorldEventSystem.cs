using System;
using System.Collections.Generic;
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
            ProphetArrives
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
                news.Newsy(true, $"The {evt.Title} has ended.");
            }

            // Special handling for certain event types
            switch (evt.Type)
            {
                case EventType.PlagueOutbreak:
                    PlaguActive = false;
                    news?.Newsy(true, "The plague has finally subsided. The healers rejoice!");
                    break;
                case EventType.WarBegins:
                case EventType.KingWarDeclaration:
                    WarActive = false;
                    news?.Newsy(true, "Peace has returned to the realm.");
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
                    evt.Title = "Royal Tax Increase";
                    evt.Description = "The King has raised taxes! Shop prices increase by 20%.";
                    evt.DaysRemaining = _random.Next(5, 15);
                    evt.Effects["price"] = 1.2f;
                    CurrentKingDecree = "By Royal Decree: Taxes are raised to fund the kingdom's defense!";
                    break;

                case EventType.KingTaxDecrease:
                    evt.Title = "Royal Tax Relief";
                    evt.Description = "The King has lowered taxes! Shop prices decrease by 15%.";
                    evt.DaysRemaining = _random.Next(5, 10);
                    evt.Effects["price"] = 0.85f;
                    CurrentKingDecree = "By Royal Decree: The crown grants tax relief to loyal subjects!";
                    break;

                case EventType.KingBounty:
                    evt.Title = "Royal Bounty";
                    evt.Description = "The King offers bounties on monsters! +50% gold from combat.";
                    evt.DaysRemaining = _random.Next(7, 14);
                    evt.Effects["gold"] = 1.5f;
                    CurrentKingDecree = "By Royal Decree: Bounties offered for slaying monsters!";
                    break;

                case EventType.KingPardon:
                    evt.Title = "Royal Pardon";
                    evt.Description = "The King pardons minor crimes. Guards are more lenient.";
                    evt.DaysRemaining = _random.Next(3, 7);
                    CurrentKingDecree = "By Royal Decree: A royal pardon is granted for minor offenses!";
                    break;

                case EventType.KingWarDeclaration:
                    evt.Title = "Declaration of War";
                    evt.Description = "The King declares war! Combat XP +25%, but danger increases.";
                    evt.DaysRemaining = _random.Next(14, 30);
                    evt.Effects["xp"] = 1.25f;
                    WarActive = true;
                    CurrentKingDecree = "By Royal Decree: War is declared against the Northern Hordes!";
                    break;

                case EventType.KingPeaceTreaty:
                    evt.Title = "Peace Treaty";
                    evt.Description = "The King signs a peace treaty. Trade flourishes!";
                    evt.DaysRemaining = _random.Next(10, 20);
                    evt.Effects["price"] = 0.9f;
                    evt.Effects["gold"] = 1.2f;
                    WarActive = false;
                    CurrentKingDecree = "By Royal Decree: Peace has been achieved with our enemies!";
                    break;

                case EventType.KingFestivalDecree:
                    evt.Title = "Royal Festival";
                    evt.Description = "The King declares a festival! All gains increased by 10%.";
                    evt.DaysRemaining = _random.Next(3, 7);
                    evt.Effects["xp"] = 1.1f;
                    evt.Effects["gold"] = 1.1f;
                    FestivalActive = true;
                    CurrentKingDecree = "By Royal Decree: Let there be celebration throughout the land!";
                    break;

                case EventType.KingMartialLaw:
                    evt.Title = "Martial Law";
                    evt.Description = "The King declares martial law. Dark Alley is closed!";
                    evt.DaysRemaining = _random.Next(5, 10);
                    CurrentKingDecree = "By Royal Decree: Martial law is in effect. Lawbreakers will be punished!";
                    break;

                // === ECONOMY EVENTS ===
                case EventType.EconomyBoom:
                    evt.Title = "Economic Boom";
                    evt.Description = "Trade is flourishing! Shop prices drop 25%, gold +20%.";
                    evt.DaysRemaining = _random.Next(5, 12);
                    evt.Effects["price"] = 0.75f;
                    evt.Effects["gold"] = 1.2f;
                    break;

                case EventType.EconomyRecession:
                    evt.Title = "Economic Recession";
                    evt.Description = "Hard times have come. Prices rise 30%, gold rewards drop 20%.";
                    evt.DaysRemaining = _random.Next(7, 14);
                    evt.Effects["price"] = 1.3f;
                    evt.Effects["gold"] = 0.8f;
                    break;

                case EventType.MerchantCaravan:
                    evt.Title = "Merchant Caravan";
                    evt.Description = "A wealthy caravan arrives! Shops have rare items at 20% off.";
                    evt.DaysRemaining = _random.Next(3, 7);
                    evt.Effects["price"] = 0.8f;
                    break;

                case EventType.BanditRaid:
                    evt.Title = "Bandit Raid";
                    evt.Description = "Bandits raid the trade routes! Prices rise 25%.";
                    evt.DaysRemaining = _random.Next(3, 7);
                    evt.Effects["price"] = 1.25f;
                    break;

                case EventType.GoldRush:
                    evt.Title = "Gold Rush";
                    evt.Description = "Gold discovered in the mines! +50% gold from all sources.";
                    evt.DaysRemaining = _random.Next(5, 10);
                    evt.Effects["gold"] = 1.5f;
                    break;

                case EventType.Inflation:
                    evt.Title = "Inflation Crisis";
                    evt.Description = "Gold becomes worthless! Prices double.";
                    evt.DaysRemaining = _random.Next(5, 10);
                    evt.Effects["price"] = 2.0f;
                    break;

                // === PLAGUE/DISEASE EVENTS ===
                case EventType.PlagueOutbreak:
                    evt.Title = "Plague Outbreak";
                    evt.Description = "A terrible plague sweeps the land! Max HP reduced by 20%.";
                    evt.DaysRemaining = _random.Next(10, 20);
                    evt.Effects["stat"] = -2f; // Stat penalty
                    PlaguActive = true;
                    break;

                case EventType.PlagueEnds:
                    evt.Title = "Plague Subsides";
                    evt.Description = "The plague has ended! Healers offer discounts.";
                    evt.DaysRemaining = _random.Next(3, 7);
                    evt.Effects["price"] = 0.5f; // Healing price discount
                    PlaguActive = false;
                    break;

                case EventType.CursedLand:
                    evt.Title = "Cursed Land";
                    evt.Description = "Dark magic curses the land. -10% to all stats.";
                    evt.DaysRemaining = _random.Next(5, 10);
                    evt.Effects["stat"] = -3f;
                    break;

                case EventType.BlessedRain:
                    evt.Title = "Blessed Rain";
                    evt.Description = "Holy rain falls! +5% to all stats, healing is free.";
                    evt.DaysRemaining = _random.Next(3, 5);
                    evt.Effects["stat"] = 1f;
                    break;

                // === FESTIVAL EVENTS ===
                case EventType.HarvestFestival:
                    evt.Title = "Harvest Festival";
                    evt.Description = "Celebrate the harvest! Food is cheap, XP +15%.";
                    evt.DaysRemaining = _random.Next(3, 7);
                    evt.Effects["xp"] = 1.15f;
                    evt.Effects["price"] = 0.8f;
                    FestivalActive = true;
                    break;

                case EventType.MidsummerCelebration:
                    evt.Title = "Midsummer Celebration";
                    evt.Description = "The sun shines bright! +20% XP, +10% gold.";
                    evt.DaysRemaining = _random.Next(3, 5);
                    evt.Effects["xp"] = 1.2f;
                    evt.Effects["gold"] = 1.1f;
                    FestivalActive = true;
                    break;

                case EventType.WinterSolstice:
                    evt.Title = "Winter Solstice";
                    evt.Description = "The longest night! Darkness creatures are stronger but drop more gold.";
                    evt.DaysRemaining = _random.Next(3, 5);
                    evt.Effects["gold"] = 1.3f;
                    FestivalActive = true;
                    break;

                case EventType.TournamentDay:
                    evt.Title = "Tournament Day";
                    evt.Description = "Warriors compete for glory! +30% combat XP.";
                    evt.DaysRemaining = _random.Next(1, 3);
                    evt.Effects["xp"] = 1.3f;
                    FestivalActive = true;
                    break;

                case EventType.HolyDay:
                    evt.Title = "Holy Day";
                    evt.Description = "The gods are honored! Temple services are free.";
                    evt.DaysRemaining = 1;
                    FestivalActive = true;
                    break;

                // === WAR/CONFLICT EVENTS ===
                case EventType.WarBegins:
                    evt.Title = "War Erupts";
                    evt.Description = "War breaks out! Combat is more dangerous but rewarding.";
                    evt.DaysRemaining = _random.Next(14, 30);
                    evt.Effects["xp"] = 1.25f;
                    evt.Effects["gold"] = 1.2f;
                    WarActive = true;
                    break;

                case EventType.WarEnds:
                    evt.Title = "War Ends";
                    evt.Description = "Peace returns. Veterans receive bonuses.";
                    evt.DaysRemaining = _random.Next(5, 10);
                    evt.Effects["xp"] = 1.1f;
                    WarActive = false;
                    break;

                case EventType.MonsterInvasion:
                    evt.Title = "Monster Invasion";
                    evt.Description = "Monsters pour from the dungeon! +50% XP, danger increases.";
                    evt.DaysRemaining = _random.Next(5, 10);
                    evt.Effects["xp"] = 1.5f;
                    WarActive = true;
                    break;

                case EventType.DemonPortal:
                    evt.Title = "Demon Portal Opens";
                    evt.Description = "A portal to the abyss! Demons roam the land. +75% XP.";
                    evt.DaysRemaining = _random.Next(3, 7);
                    evt.Effects["xp"] = 1.75f;
                    evt.Effects["stat"] = -1f;
                    WarActive = true;
                    break;

                case EventType.DragonSighting:
                    evt.Title = "Dragon Sighting";
                    evt.Description = "A dragon has been seen! Great treasure awaits the brave.";
                    evt.DaysRemaining = _random.Next(5, 10);
                    evt.Effects["gold"] = 1.5f;
                    break;

                case EventType.BanditLordRises:
                    evt.Title = "Bandit Lord Rises";
                    evt.Description = "A bandit lord threatens the roads! Travel is dangerous.";
                    evt.DaysRemaining = _random.Next(7, 14);
                    evt.Effects["price"] = 1.2f;
                    break;

                // === MISC EVENTS ===
                case EventType.EclipseDarkness:
                    evt.Title = "Solar Eclipse";
                    evt.Description = "Darkness covers the land! Dark magic +30%, holy magic -30%.";
                    evt.DaysRemaining = _random.Next(1, 3);
                    break;

                case EventType.MeteorShower:
                    evt.Title = "Meteor Shower";
                    evt.Description = "Stars fall from the sky! Magical items are enhanced.";
                    evt.DaysRemaining = _random.Next(1, 3);
                    evt.Effects["xp"] = 1.2f;
                    break;

                case EventType.AncientRelicFound:
                    evt.Title = "Ancient Relic Discovered";
                    evt.Description = "Adventurers unearth an ancient relic! Magic shop has rare items.";
                    evt.DaysRemaining = _random.Next(5, 10);
                    break;

                case EventType.ProphetArrives:
                    evt.Title = "Prophet Arrives";
                    evt.Description = "A mysterious prophet speaks of doom and glory.";
                    evt.DaysRemaining = _random.Next(3, 7);
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
                news.Newsy(true, evt.Title + ": " + evt.Description);

                // Add king's decree if applicable
                if (!string.IsNullOrEmpty(CurrentKingDecree) && evt.Type.ToString().StartsWith("King"))
                {
                    news.Newsy(true, CurrentKingDecree);
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
                        return (false, "The Dark Alley is closed under martial law!");
                    }
                }
            }

            return (true, null);
        }

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
            terminal.WriteLine("           WORLD EVENTS");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("═══════════════════════════════════════");
            terminal.WriteLine("");

            if (_activeEvents.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  No major events affecting the realm.");
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
                    terminal.WriteLine($"  * {evt.Title}");
                    terminal.SetColor("gray");
                    terminal.WriteLine($"    {evt.Description}");
                    terminal.SetColor("dark_gray");
                    terminal.WriteLine($"    ({evt.DaysRemaining} days remaining)");
                    terminal.WriteLine("");
                }
            }

            // Show global modifiers if not default
            terminal.SetColor("cyan");
            terminal.WriteLine("Current Modifiers:");
            terminal.SetColor("white");

            if (Math.Abs(GlobalPriceModifier - 1.0f) > 0.01f)
            {
                string priceColor = GlobalPriceModifier > 1.0f ? "red" : "green";
                terminal.SetColor(priceColor);
                terminal.WriteLine($"  Prices: {(GlobalPriceModifier > 1.0f ? "+" : "")}{((GlobalPriceModifier - 1.0f) * 100):F0}%");
            }

            if (Math.Abs(GlobalXPModifier - 1.0f) > 0.01f)
            {
                string xpColor = GlobalXPModifier > 1.0f ? "green" : "red";
                terminal.SetColor(xpColor);
                terminal.WriteLine($"  Experience: {(GlobalXPModifier > 1.0f ? "+" : "")}{((GlobalXPModifier - 1.0f) * 100):F0}%");
            }

            if (Math.Abs(GlobalGoldModifier - 1.0f) > 0.01f)
            {
                string goldColor = GlobalGoldModifier > 1.0f ? "bright_yellow" : "red";
                terminal.SetColor(goldColor);
                terminal.WriteLine($"  Gold: {(GlobalGoldModifier > 1.0f ? "+" : "")}{((GlobalGoldModifier - 1.0f) * 100):F0}%");
            }

            if (GlobalStatModifier != 0)
            {
                string statColor = GlobalStatModifier > 0 ? "green" : "red";
                terminal.SetColor(statColor);
                terminal.WriteLine($"  Stats: {(GlobalStatModifier > 0 ? "+" : "")}{GlobalStatModifier}");
            }

            // Show king's decree if any
            if (!string.IsNullOrEmpty(CurrentKingDecree))
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("Royal Decree:");
                terminal.SetColor("yellow");
                terminal.WriteLine($"  \"{CurrentKingDecree}\"");
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

        private static readonly (string Category, string[] Templates)[] DistantNewsTemplates = new[]
        {
            ("War", new[] {
                "Border skirmishes between {REGION1} and {REGION2} have intensified.",
                "The armies of {REGION1} march south — refugees flood the roads.",
                "A ceasefire was declared between {REGION1} and {REGION2}.",
                "Mercenary companies from {REGION1} are recruiting. The pay is good.",
                "Raiders from {REGION1} burned a border village near {REGION2}.",
                "A legendary general has risen in {REGION1}, rallying troops for war.",
                "The siege of {REGION1} enters its third month. Supplies run low.",
            }),
            ("Trade", new[] {
                "A merchant caravan from {REGION1} arrived bearing exotic spices and silks.",
                "Trade routes through {REGION1} have been disrupted by bandits.",
                "The price of iron has tripled — {REGION1}'s mines have flooded.",
                "A rare shipment of enchanted ore from {REGION1} reached port.",
                "Merchants from {REGION1} speak of a boom in the gem trade.",
                "A trade embargo between {REGION1} and {REGION2} has collapsed local markets.",
                "Smugglers from {REGION1} peddle forbidden alchemical reagents.",
            }),
            ("Plague", new[] {
                "A mysterious sickness spreads through {REGION1}. Healers are baffled.",
                "Healers from {REGION1} report the plague has been contained at last.",
                "Travelers from {REGION1} carry a strange coughing illness.",
                "A quarantine has been declared around {REGION1}. No one enters or leaves.",
                "Priests in {REGION1} claim divine intervention cured the blight.",
            }),
            ("Discovery", new[] {
                "Explorers in {REGION1} discovered ruins predating the Old Gods.",
                "A new dungeon entrance was found beneath the mountains of {REGION1}.",
                "Ancient texts from {REGION1} mention a forgotten eighth Old God.",
                "Miners in {REGION1} broke through into an underground city of unknown origin.",
                "A scholar from {REGION1} claims to have deciphered the language of the Seals.",
                "Artifacts of immense power were unearthed in {REGION1}.",
            }),
            ("Political", new[] {
                "The High Council of {REGION1} has elected a new Chancellor.",
                "Civil unrest in {REGION1} — the governor has been deposed.",
                "An alliance between {REGION1} and {REGION2} shakes the balance of power.",
                "The throne of {REGION1} sits empty. Three claimants vie for the crown.",
                "A beloved queen of {REGION1} was assassinated. The realm mourns.",
                "Diplomats from {REGION1} arrived seeking an alliance against {REGION2}.",
            }),
            ("Disaster", new[] {
                "A great earthquake struck {REGION1}. Ships lost at sea.",
                "Wildfire consumed half of {REGION1}. The forests burn for weeks.",
                "Floods devastated the lowlands of {REGION1}. Thousands displaced.",
                "A volcano erupted near {REGION1}, blanketing the sky in ash.",
                "Famine grips {REGION1} after a season of failed harvests.",
                "A tidal wave struck the coast of {REGION1}. Entire villages washed away.",
            }),
            ("Monster", new[] {
                "A dragon was sighted over {REGION1}. Livestock vanishes nightly.",
                "Sea serpents plague the waters around {REGION1}.",
                "A horde of undead emerged from {REGION1}'s ancient burial grounds.",
                "A pack of dire wolves terrorizes travelers between {REGION1} and {REGION2}.",
                "Something ancient stirs beneath {REGION1}. The ground trembles at night.",
                "A giant has been spotted wandering the highlands of {REGION1}.",
                "Hunters from {REGION1} report sightings of creatures that shouldn't exist.",
            }),
        };

        private static readonly string[] PlayerReferencedTemplates = new[]
        {
            "Bards in {REGION1} sing of {PLAYER}'s exploits in the deep dungeons.",
            "A merchant in {REGION1} sells maps claiming to lead to {PLAYER}'s hidden treasure.",
            "The fame of {PLAYER} has reached even {REGION1}.",
            "Scholars in {REGION1} debate {PLAYER}'s encounters with the Old Gods.",
            "Children in {REGION1} play at being {PLAYER}, the dungeon delver.",
            "A bounty hunter from {REGION1} seeks to test their mettle against {PLAYER}.",
            "Tavern tales from {REGION1} grow more fantastical about {PLAYER} with each telling.",
        };

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
                    string template = pool.Templates[_random.Next(pool.Templates.Length)];
                    string result = ReplaceRegionTokens(template);
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

            var template = PlayerReferencedTemplates[_random.Next(PlayerReferencedTemplates.Length)];
            return ReplaceRegionTokens(template).Replace("{PLAYER}", playerName);
        }

        private string ReplaceRegionTokens(string template)
        {
            string region1 = DistantRegions[_random.Next(DistantRegions.Length)];
            string region2;
            do { region2 = DistantRegions[_random.Next(DistantRegions.Length)]; }
            while (region2 == region1);

            return template.Replace("{REGION1}", region1).Replace("{REGION2}", region2);
        }

        private void TrackCategory(string category)
        {
            _recentNewsCategories.Add(category);
            while (_recentNewsCategories.Count > 5)
                _recentNewsCategories.RemoveAt(0);
        }
    }
}
