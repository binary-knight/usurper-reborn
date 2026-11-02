using System;
using System.Collections.Generic;
using System.Linq;

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
        public const string HINT_LIVES = "lives"; // v1.1.12
        public const string HINT_AWAKENING = "awakening"; // v1.1.12

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
            [HINT_FIRST_COMBAT_CLASS] = new HintDefinition("bright_green"),
            [HINT_LIVES] = new HintDefinition("bright_yellow"),
            [HINT_AWAKENING] = new HintDefinition("bright_cyan")
        };

        /// <summary>
        /// 1.3.9 (play test B4): the quickbar entries each class's first battle tip names, in the order its text names
        /// them. The tip takes each entry's key from the quickbar the combat menu shows, so it never names a slot the
        /// entry is not in (a Paladin's bar is [1] Power Strike, [2] Lay on Hands, while the tip used to say the reverse).
        /// </summary>
        internal static readonly Dictionary<CharacterClass, (string TipKey, string[] SlotIds)> ClassCombatTips = new()
        {
            [CharacterClass.Magician] = ("hint.class_combat.magician", new[] { "spell:1" }),
            [CharacterClass.Cleric] = ("hint.class_combat.cleric", new[] { "spell:1" }),
            [CharacterClass.Sage] = ("hint.class_combat.sage", new[] { "spell:1" }),
            [CharacterClass.Warrior] = ("hint.class_combat.warrior", new[] { "power_strike" }),
            [CharacterClass.Barbarian] = ("hint.class_combat.barbarian", new[] { "power_strike" }),
            [CharacterClass.Paladin] = ("hint.class_combat.paladin", new[] { "lay_on_hands", "power_strike" }),
            [CharacterClass.Assassin] = ("hint.class_combat.assassin", new[] { "backstab" }),
            [CharacterClass.Ranger] = ("hint.class_combat.ranger", new[] { "precise_shot" }),
            [CharacterClass.Jester] = ("hint.class_combat.jester", new[] { "mock" }),
            [CharacterClass.Bard] = ("hint.class_combat.bard", new[] { "mock" }),
            [CharacterClass.Alchemist] = ("hint.class_combat.alchemist", new[] { "throw_bomb" }),
        };

        /// <summary>
        /// 1.3.9 (play test B4): a class's first battle tip, localized, with the keys of the menu shown.
        /// <paramref name="quickbarKeyOf"/> gives the key the combat menu shows for a quickbar entry, or null when the
        /// entry is not on the bar. When an entry the tip names is not on the bar, the tip names no quickbar key:
        /// the general tip if the bar holds anything, else the attack tip alone.
        /// </summary>
        public static string GetClassCombatTip(CharacterClass playerClass, Func<string, string?> quickbarKeyOf, bool hasQuickbar)
        {
            if (ClassCombatTips.TryGetValue(playerClass, out var tip))
            {
                var keys = tip.SlotIds.Select(quickbarKeyOf).ToArray();
                if (keys.All(k => !string.IsNullOrEmpty(k)))
                    return Loc.Get(tip.TipKey, keys.Cast<object>().ToArray());
            }
            return Loc.Get(hasQuickbar ? "hint.class_combat.default" : "hint.class_combat.attack_only");
        }

        /// <summary>
        /// Try to show a hint if the player hasn't seen it before.
        /// Returns true if hint was shown, false if already seen.
        /// </summary>
        public bool TryShowHint(string hintId, TerminalEmulator terminal, HashSet<string>? shownHints, params object[] msgArgs)
            => ShowOnce(hintId, terminal, shownHints, null, msgArgs);

        /// <summary>
        /// 1.3.9: the same, with the message read from <paramref name="msgKey"/> in place of hint.&lt;id&gt;.msg, for a
        /// hint whose text depends on the screen it describes (the first combat tip names [I] only where the combat
        /// menu shows it).
        /// </summary>
        public bool TryShowHintWithMessage(string hintId, string msgKey, TerminalEmulator terminal, HashSet<string>? shownHints)
            => ShowOnce(hintId, terminal, shownHints, null, Array.Empty<object>(), msgKey);

        /// <summary>
        /// 2.0: the same, for a player: a hint whose text names Main Street keys is shown with the keys of the
        /// player's layout (MainStreetLocation.StreetTextKey). A null player shows nothing, as a null set does.
        /// </summary>
        public bool TryShowHint(string hintId, TerminalEmulator terminal, Character? player, params object[] msgArgs)
            => player != null && ShowOnce(hintId, terminal, player.HintsShown, player, msgArgs);

        private bool ShowOnce(string hintId, TerminalEmulator terminal, HashSet<string>? shownHints, Character? player, object[] msgArgs, string? msgKey = null)
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
            ShowHintBox(hintId, hint, terminal, msgArgs, player, msgKey);
            return true;
        }

        /// <summary>
        /// v1.1.12: the lives hint, once per character. Lives exist only in online permadeath mode
        /// (PermadeathHelper, Character.RaiseLevel and the Rite of Return share this gate).
        /// </summary>
        public bool TryShowLivesHint(Character player, TerminalEmulator terminal)
        {
            if (!UsurperRemake.BBS.DoorMode.IsOnlineMode || !GameConfig.OnlinePermadeathEnabled)
                return false;
            return TryShowHint(HINT_LIVES, terminal, player.HintsShown,
                Math.Max(0, player.Resurrections), Math.Max(1, player.MaxResurrections));
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
        private void ShowHintBox(string hintId, HintDefinition hint, TerminalEmulator terminal, object[]? msgArgs = null, Character? player = null, string? msgKeyOverride = null)
        {
            // Resolve localized title/message from keys derived from the hint ID.
            string title = Loc.Get($"hint.{hintId}.title");
            // 2.0: a message that names Main Street keys follows the player's layout (classic without a player)
            string msgKey = msgKeyOverride ?? MainStreetLocation.StreetTextKey($"hint.{hintId}.msg", player);
            string message = msgArgs != null && msgArgs.Length > 0
                ? Loc.Get(msgKey, msgArgs)
                : Loc.Get(msgKey);
            string tipLabel = Loc.Get("hint.tip_label");

            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(BoxTop(tipLabel));
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

        /// <summary>v1.2.5: the tip box's top border with the tip label in the reader's language, 78 columns as before.</summary>
        internal static string BoxTop(string label)
        {
            string head = $"\u250c\u2500\u2500\u2500 {label} ";
            return head + new string('\u2500', Math.Max(0, 77 - head.Length)) + "\u2510";
        }

        /// <summary>
        /// Word wrap text to fit within a maximum width
        /// </summary>
        internal static List<string> WordWrap(string text, int maxWidth)   // 1.3.9: static, the combat tips wrap with it too
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
