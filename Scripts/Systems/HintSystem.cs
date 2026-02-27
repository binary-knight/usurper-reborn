using System;
using System.Collections.Generic;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Contextual hint system for new players unfamiliar with text-based games.
    /// Shows helpful tips once per player, stored in their save file.
    /// Designed especially for Steam players who may have never used a BBS.
    /// </summary>
    public class HintSystem
    {
        private static HintSystem? instance;
        public static HintSystem Instance => instance ??= new HintSystem();

        // Hint IDs - each shown only once per character
        public const string HINT_MAIN_STREET_NAVIGATION = "main_street_nav";
        public const string HINT_FIRST_DUNGEON = "first_dungeon";
        public const string HINT_FIRST_COMBAT = "first_combat";
        public const string HINT_LOW_HP = "low_hp";
        public const string HINT_FIRST_SHOP = "first_shop";
        public const string HINT_FIRST_LEVEL_UP = "first_level_up";
        public const string HINT_FIRST_SPELL = "first_spell";
        public const string HINT_INVENTORY = "inventory";
        public const string HINT_SAVE_GAME = "save_game";
        public const string HINT_TEAM_COMBAT = "team_combat";
        public const string HINT_FIRST_PURCHASE_TAX = "first_purchase_tax";
        public const string HINT_LEVEL_MASTER = "level_master";
        public const string HINT_MANA_SPELLS = "mana_spells";
        public const string HINT_QUEST_SYSTEM = "quest_system";
        public const string HINT_GETTING_STARTED = "getting_started";

        // Hint definitions
        private readonly Dictionary<string, HintDefinition> hints = new()
        {
            [HINT_MAIN_STREET_NAVIGATION] = new HintDefinition(
                "Navigation Tip",
                "Press the highlighted letter key to select an option. For example, press 'I' for the Inn.",
                "bright_cyan"
            ),
            [HINT_FIRST_DUNGEON] = new HintDefinition(
                "Dungeon Tip",
                "Explore rooms using N/S/E/W (North/South/East/West). Fight monsters to gain XP and gold.",
                "bright_cyan"
            ),
            [HINT_FIRST_COMBAT] = new HintDefinition(
                "Combat Tip",
                "Press 'A' to Attack, 'F' to Flee, 'U' to Use items, or 'S' to check your Status.",
                "bright_cyan"
            ),
            [HINT_LOW_HP] = new HintDefinition(
                "Health Warning",
                "Your HP is low! Visit the Healer (H) on Main Street, or use healing potions (U) in combat.",
                "bright_yellow"
            ),
            [HINT_FIRST_SHOP] = new HintDefinition(
                "Shop Tip",
                "Press 'B' to Buy items or 'S' to Sell. Better equipment means better combat!",
                "bright_cyan"
            ),
            [HINT_FIRST_LEVEL_UP] = new HintDefinition(
                "Level Up Available!",
                "You have enough XP to level up! Visit your Master (M) on Main Street to advance.",
                "bright_green"
            ),
            [HINT_FIRST_SPELL] = new HintDefinition(
                "Magic Tip",
                "You can cast spells in combat with 'C'. Spells use Mana but can turn the tide of battle!",
                "bright_cyan"
            ),
            [HINT_INVENTORY] = new HintDefinition(
                "Inventory Tip",
                "Press '*' on Main Street to view your inventory. You can equip dungeon loot there!",
                "bright_cyan"
            ),
            [HINT_SAVE_GAME] = new HintDefinition(
                "Save Reminder",
                "Your progress is saved automatically, but you can also save manually from the game menu.",
                "bright_cyan"
            ),
            [HINT_TEAM_COMBAT] = new HintDefinition(
                "Team Combat Tip",
                "Fighting with companions gives you a 15% bonus to XP and gold!",
                "bright_green"
            ),
            [HINT_FIRST_PURCHASE_TAX] = new HintDefinition(
                "Tax Info",
                "Purchases include the King's Tax (5%) and City Tax (2%). The City Tax goes to whichever faction controls the town.",
                "bright_cyan"
            ),
            [HINT_LEVEL_MASTER] = new HintDefinition(
                "Training Available!",
                "Visit the Level Master [M] on Main Street to spend training points on skill proficiencies and check your progress.",
                "bright_green"
            ),
            [HINT_MANA_SPELLS] = new HintDefinition(
                "Magic Tip",
                "Your class has Mana for casting spells! Visit the Magic Shop [G] to learn spells, then cast them in combat with 'C'.",
                "bright_cyan"
            ),
            [HINT_QUEST_SYSTEM] = new HintDefinition(
                "Quests Available!",
                "The Quest Hall [Q] offers bounties and missions for bonus gold and XP rewards!",
                "bright_green"
            ),
            [HINT_GETTING_STARTED] = new HintDefinition(
                "Getting Started",
                "Visit Shops [W/A] to gear up, enter the Dungeons [D] to fight monsters, complete Quests [Q] for rewards, and spend training points at the Master [M] when you level up.",
                "bright_cyan"
            )
        };

        /// <summary>
        /// Try to show a hint if the player hasn't seen it before.
        /// Returns true if hint was shown, false if already seen.
        /// </summary>
        public bool TryShowHint(string hintId, TerminalEmulator terminal, HashSet<string>? shownHints)
        {
            if (shownHints == null)
                return false;

            if (shownHints.Contains(hintId))
                return false;

            if (!hints.TryGetValue(hintId, out var hint))
                return false;

            // Mark as shown
            shownHints.Add(hintId);

            // Display the hint
            ShowHintBox(hint, terminal);
            return true;
        }

        /// <summary>
        /// Check if a hint has been shown to the player
        /// </summary>
        public bool HasSeenHint(string hintId, HashSet<string>? shownHints)
        {
            return shownHints?.Contains(hintId) ?? false;
        }

        /// <summary>
        /// Display a hint in a nice box format
        /// </summary>
        private void ShowHintBox(HintDefinition hint, TerminalEmulator terminal)
        {
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("┌─── TIP ────────────────────────────────────────────────────────────────────┐");
            terminal.SetColor(hint.Color);
            terminal.WriteLine($"│ {hint.Title}");
            terminal.SetColor("white");

            // Word wrap the message to fit in the box
            var wrappedLines = WordWrap(hint.Message, 75);
            foreach (var line in wrappedLines)
            {
                terminal.WriteLine($"│ {line}");
            }

            terminal.SetColor("gray");
            terminal.WriteLine("└────────────────────────────────────────────────────────────────────────────┘");
            terminal.WriteLine("");
        }

        /// <summary>
        /// Word wrap text to fit within a maximum width
        /// </summary>
        private List<string> WordWrap(string text, int maxWidth)
        {
            var lines = new List<string>();
            var words = text.Split(' ');
            var currentLine = "";

            foreach (var word in words)
            {
                if (currentLine.Length + word.Length + 1 <= maxWidth)
                {
                    if (currentLine.Length > 0)
                        currentLine += " ";
                    currentLine += word;
                }
                else
                {
                    if (currentLine.Length > 0)
                        lines.Add(currentLine);
                    currentLine = word;
                }
            }

            if (currentLine.Length > 0)
                lines.Add(currentLine);

            return lines;
        }

        /// <summary>
        /// Definition for a single hint
        /// </summary>
        private class HintDefinition
        {
            public string Title { get; }
            public string Message { get; }
            public string Color { get; }

            public HintDefinition(string title, string message, string color)
            {
                Title = title;
                Message = message;
                Color = color;
            }
        }
    }
}
