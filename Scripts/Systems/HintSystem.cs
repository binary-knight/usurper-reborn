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
        public const string HINT_FIRST_COMBAT_CLASS = "first_combat_class";
        public const string HINT_COMPANION_ALDRIC_TEASER = "companion_aldric_teaser";
        public const string HINT_COMPANION_VEX_TEASER = "companion_vex_teaser";
        public const string HINT_COMPANION_LYRIS_TEASER = "companion_lyris_teaser";
        public const string HINT_COMPANION_MIRA_TEASER = "companion_mira_teaser";

        // Hint definitions. Title and message text are resolved at display time
        // from loc keys derived from the hint ID (`hint.<id>.title` /
        // `hint.<id>.msg`), so all hint text is localized -- only the color is
        // stored here.
        private readonly Dictionary<string, HintDefinition> hints = new()
        {
            [HINT_MAIN_STREET_NAVIGATION] = new HintDefinition("bright_cyan"),
            [HINT_FIRST_DUNGEON] = new HintDefinition("bright_cyan"),
            [HINT_FIRST_COMBAT] = new HintDefinition("bright_cyan"),
            [HINT_LOW_HP] = new HintDefinition("bright_yellow"),
            [HINT_FIRST_SHOP] = new HintDefinition("bright_cyan"),
            [HINT_FIRST_LEVEL_UP] = new HintDefinition("bright_green"),
            [HINT_FIRST_SPELL] = new HintDefinition("bright_cyan"),
            [HINT_INVENTORY] = new HintDefinition("bright_cyan"),
            [HINT_SAVE_GAME] = new HintDefinition("bright_cyan"),
            [HINT_TEAM_COMBAT] = new HintDefinition("bright_green"),
            [HINT_FIRST_PURCHASE_TAX] = new HintDefinition("bright_cyan"),
            [HINT_LEVEL_MASTER] = new HintDefinition("bright_green"),
            [HINT_MANA_SPELLS] = new HintDefinition("bright_cyan"),
            [HINT_QUEST_SYSTEM] = new HintDefinition("bright_green"),
            [HINT_GETTING_STARTED] = new HintDefinition("bright_cyan"),
            [HINT_FIRST_COMBAT_CLASS] = new HintDefinition("bright_green")
        };

        /// <summary>
        /// Get a class-specific first combat tip for the player, localized.
        /// </summary>
        public static string GetClassCombatTip(CharacterClass playerClass)
        {
            string key = playerClass switch
            {
                CharacterClass.Magician => "hint.class_combat.magician",
                CharacterClass.Cleric => "hint.class_combat.cleric",
                CharacterClass.Sage => "hint.class_combat.sage",
                CharacterClass.Warrior => "hint.class_combat.warrior",
                CharacterClass.Barbarian => "hint.class_combat.barbarian",
                CharacterClass.Paladin => "hint.class_combat.paladin",
                CharacterClass.Assassin => "hint.class_combat.assassin",
                CharacterClass.Ranger => "hint.class_combat.ranger",
                CharacterClass.Jester => "hint.class_combat.jester",
                CharacterClass.Bard => "hint.class_combat.bard",
                CharacterClass.Alchemist => "hint.class_combat.alchemist",
                _ => "hint.class_combat.default"
            };
            return Loc.Get(key);
        }

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
            ShowHintBox(hintId, hint, terminal);
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
        private void ShowHintBox(string hintId, HintDefinition hint, TerminalEmulator terminal)
        {
            // Resolve localized title/message from keys derived from the hint ID.
            string title = Loc.Get($"hint.{hintId}.title");
            string message = Loc.Get($"hint.{hintId}.msg");
            string tipLabel = Loc.Get("hint.tip_label");

            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("┌─── TIP ────────────────────────────────────────────────────────────────────┐");
            }
            terminal.SetColor(hint.Color);
            if (GameConfig.ScreenReaderMode)
                terminal.WriteLine($"{tipLabel}: {title}");
            else
                terminal.WriteLine($"│ {title}");
            terminal.SetColor("white");

            // Word wrap the message to fit in the box
            var wrappedLines = WordWrap(message, 75);
            foreach (var line in wrappedLines)
            {
                if (GameConfig.ScreenReaderMode)
                    terminal.WriteLine($"  {line}");
                else
                    terminal.WriteLine($"│ {line}");
            }

            if (!GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("└────────────────────────────────────────────────────────────────────────────┘");
            }
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
            public string Color { get; }

            public HintDefinition(string color)
            {
                Color = color;
            }
        }
    }
}
