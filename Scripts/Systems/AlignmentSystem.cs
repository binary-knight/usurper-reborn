using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Alignment System - Makes Chivalry and Darkness meaningful throughout the game
    /// Affects: Shop prices, NPC reactions, location access, quest availability, combat bonuses
    /// Based on Usurper's original alignment mechanics from Pascal code
    /// </summary>
    public class AlignmentSystem
    {
        private static AlignmentSystem? _fallbackInstance;
        public static AlignmentSystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.Alignment;
                return _fallbackInstance ??= new AlignmentSystem();
            }
        }

        private Random _random = new Random();

        /// <summary>
        /// Alignment categories based on Chivalry vs Darkness
        /// </summary>
        public enum AlignmentType
        {
            Holy,           // Very high chivalry, no darkness
            Good,           // High chivalry
            Neutral,        // Balanced or low both
            Dark,           // High darkness
            Evil            // Very high darkness, no chivalry
        }

        /// <summary>
        /// Get a character's alignment type
        /// </summary>
        public AlignmentType GetAlignment(Character character)
        {
            long chivalry = character.Chivalry;
            long darkness = character.Darkness;
            long diff = chivalry - darkness;

            if (chivalry >= 800 && darkness < 100) return AlignmentType.Holy;
            if (darkness >= 800 && chivalry < 100) return AlignmentType.Evil;
            if (diff >= 400) return AlignmentType.Good;
            if (diff <= -400) return AlignmentType.Dark;
            return AlignmentType.Neutral;
        }

        /// <summary>
        /// Get alignment display string with color
        /// </summary>
        public (string text, string color) GetAlignmentDisplay(Character character)
        {
            var alignment = GetAlignment(character);
            return alignment switch
            {
                AlignmentType.Holy => ("Holy", "bright_yellow"),
                AlignmentType.Good => ("Good", "bright_green"),
                AlignmentType.Neutral => ("Neutral", "gray"),
                AlignmentType.Dark => ("Dark", "red"),
                AlignmentType.Evil => ("Evil", "bright_red"),
                _ => ("Unknown", "white")
            };
        }

        /// <summary>
        /// Get price modifier based on alignment and shop type
        /// Holy/Good get better prices at legitimate shops, Dark/Evil at shady ones
        /// </summary>
        public float GetPriceModifier(Character character, bool isShadyShop)
        {
            var alignment = GetAlignment(character);

            if (isShadyShop)
            {
                // Shady shops favor dark characters
                return alignment switch
                {
                    AlignmentType.Holy => 1.5f,      // 50% markup for holy characters
                    AlignmentType.Good => 1.25f,    // 25% markup for good characters
                    AlignmentType.Neutral => 1.0f,  // Normal price
                    AlignmentType.Dark => 0.9f,     // 10% discount
                    AlignmentType.Evil => 0.75f,    // 25% discount for evil
                    _ => 1.0f
                };
            }
            else
            {
                // Legitimate shops favor good characters
                return alignment switch
                {
                    AlignmentType.Holy => 0.8f,     // 20% discount for holy
                    AlignmentType.Good => 0.9f,    // 10% discount for good
                    AlignmentType.Neutral => 1.0f, // Normal price
                    AlignmentType.Dark => 1.15f,   // 15% markup
                    AlignmentType.Evil => 1.4f,    // 40% markup for evil
                    _ => 1.0f
                };
            }
        }

        /// <summary>
        /// Check if character can access a location based on alignment
        /// </summary>
        public (bool canAccess, string reason) CanAccessLocation(Character character, GameLocation location)
        {
            var alignment = GetAlignment(character);

            switch (location)
            {
                case GameLocation.Church:
                case GameLocation.Temple:
                    if (alignment == AlignmentType.Evil)
                        return (false, "The holy wards repel your dark presence!");
                    if (alignment == AlignmentType.Dark && character.Darkness > 600)
                        return (false, "The priests eye you suspiciously and bar the door.");
                    break;

                case GameLocation.DarkAlley:
                case GameLocation.Darkness:
                    // Anyone can enter, but good characters get warned
                    if (alignment == AlignmentType.Holy)
                        return (true, "You feel your holy aura dimming as you enter this wretched place...");
                    break;
            }

            return (true, null);
        }

        /// <summary>
        /// Get NPC reaction modifier based on alignment compatibility
        /// </summary>
        public float GetNPCReactionModifier(Character player, NPC npc)
        {
            var playerAlignment = GetAlignment(player);
            bool npcIsEvil = npc.Darkness > npc.Chivalry + 200;
            bool npcIsGood = npc.Chivalry > npc.Darkness + 200;

            // Similar alignments get along better
            if ((playerAlignment == AlignmentType.Holy || playerAlignment == AlignmentType.Good) && npcIsGood)
                return 1.5f; // 50% better reactions

            if ((playerAlignment == AlignmentType.Evil || playerAlignment == AlignmentType.Dark) && npcIsEvil)
                return 1.5f; // Evil characters respect each other

            // Opposite alignments clash
            if ((playerAlignment == AlignmentType.Holy || playerAlignment == AlignmentType.Good) && npcIsEvil)
                return 0.5f; // 50% worse reactions

            if ((playerAlignment == AlignmentType.Evil || playerAlignment == AlignmentType.Dark) && npcIsGood)
                return 0.5f;

            return 1.0f; // Neutral reactions
        }

        /// <summary>
        /// Get combat modifier based on alignment
        /// </summary>
        public (float attackMod, float defenseMod) GetCombatModifiers(Character character)
        {
            var alignment = GetAlignment(character);

            return alignment switch
            {
                // Holy: Bonus vs evil enemies, slight defense boost
                AlignmentType.Holy => (1.0f, 1.1f),
                // Good: Balanced slight bonuses
                AlignmentType.Good => (1.05f, 1.05f),
                // Neutral: No bonuses
                AlignmentType.Neutral => (1.0f, 1.0f),
                // Dark: Attack boost, slight defense penalty
                AlignmentType.Dark => (1.1f, 0.95f),
                // Evil: Strong attack, defense penalty
                AlignmentType.Evil => (1.2f, 0.9f),
                _ => (1.0f, 1.0f)
            };
        }

        /// <summary>
        /// Apply alignment change with news generation
        /// </summary>
        public void ModifyAlignment(Character character, int chivalryChange, int darknessChange, string reason)
        {
            character.Chivalry = Math.Max(0, Math.Min(1000, character.Chivalry + chivalryChange));
            character.Darkness = Math.Max(0, Math.Min(1000, character.Darkness + darknessChange));

            // Generate news for significant alignment shifts
            if (Math.Abs(chivalryChange) >= 20 || Math.Abs(darknessChange) >= 20)
            {
                var newsSystem = NewsSystem.Instance;
                if (newsSystem != null)
                {
                    if (chivalryChange >= 20)
                        newsSystem.Newsy(true, $"{character.Name} performed a noble deed: {reason}");
                    else if (darknessChange >= 20)
                        newsSystem.Newsy(true, $"{character.Name} committed a dark act: {reason}");
                }
            }
        }

        /// <summary>
        /// Get special abilities available based on alignment
        /// </summary>
        public List<string> GetAlignmentAbilities(Character character)
        {
            var abilities = new List<string>();
            var alignment = GetAlignment(character);

            switch (alignment)
            {
                case AlignmentType.Holy:
                    abilities.Add("Divine Protection - 10% damage reduction from evil creatures");
                    abilities.Add("Holy Smite - Extra damage vs undead/demons");
                    abilities.Add("Blessed Aura - Nearby allies gain +2 defense");
                    abilities.Add("Temple Sanctuary - Free healing at temples");
                    break;

                case AlignmentType.Good:
                    abilities.Add("Righteous Fury - +10% damage vs evil creatures");
                    abilities.Add("Merchant's Trust - 10% discount at shops");
                    abilities.Add("Guard's Respect - Guards help you in combat");
                    break;

                case AlignmentType.Neutral:
                    abilities.Add("Diplomatic Immunity - No alignment restrictions");
                    abilities.Add("Balanced Path - Access all locations freely");
                    break;

                case AlignmentType.Dark:
                    abilities.Add("Shadow Strike - +15% critical hit chance");
                    abilities.Add("Fear Aura - Enemies may flee in terror");
                    abilities.Add("Black Market Access - Better deals in shady shops");
                    break;

                case AlignmentType.Evil:
                    abilities.Add("Soul Drain - Heal 10% of damage dealt");
                    abilities.Add("Terror Incarnate - +20% damage, enemies may flee");
                    abilities.Add("Dark Pact - Demons may aid you in battle");
                    abilities.Add("Criminal Respect - Thieves won't target you");
                    break;
            }

            return abilities;
        }

        /// <summary>
        /// Check for random alignment-based events
        /// </summary>
        public async Task<bool> CheckAlignmentEvent(Character player, TerminalEmulator terminal)
        {
            var alignment = GetAlignment(player);

            // 5% chance of alignment event
            if (_random.Next(100) >= 5) return false;

            switch (alignment)
            {
                case AlignmentType.Holy:
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("A warm light surrounds you...");
                    terminal.SetColor("white");
                    terminal.WriteLine("The gods smile upon your righteousness!");
                    player.HP = Math.Min(player.MaxHP, player.HP + player.MaxHP / 10);
                    terminal.WriteLine($"(+{player.MaxHP / 10} HP restored)");
                    await Task.Delay(2000);
                    return true;

                case AlignmentType.Good:
                    if (_random.Next(2) == 0)
                    {
                        terminal.SetColor("green");
                        terminal.WriteLine("A grateful merchant approaches you...");
                        terminal.SetColor("white");
                        int gold = _random.Next(20, 100);
                        terminal.WriteLine($"\"You helped me once. Take this as thanks!\" (+{gold} gold)");
                        player.Gold += gold;
                        await Task.Delay(2000);
                        return true;
                    }
                    break;

                case AlignmentType.Dark:
                    if (_random.Next(2) == 0)
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("A shadowy figure whispers to you...");
                        terminal.SetColor("gray");
                        terminal.WriteLine("\"I know of treasure in the dungeon. Seek the third level...\"");
                        player.Experience += 50;
                        terminal.WriteLine("(+50 XP from forbidden knowledge)");
                        await Task.Delay(2000);
                        return true;
                    }
                    break;

                case AlignmentType.Evil:
                    terminal.SetColor("bright_red");
                    terminal.WriteLine("Dark energy courses through your veins!");
                    terminal.SetColor("red");
                    terminal.WriteLine("Your wickedness empowers you...");
                    player.Strength += 1;
                    terminal.WriteLine("(+1 Strength temporarily)");
                    await Task.Delay(2000);
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Display alignment status to player
        /// </summary>
        public void DisplayAlignmentStatus(Character character, TerminalEmulator terminal)
        {
            var (text, color) = GetAlignmentDisplay(character);
            var alignment = GetAlignment(character);

            terminal.SetColor("bright_cyan");
            terminal.WriteLine("═══════════════════════════════════════");
            terminal.WriteLine("         ALIGNMENT STATUS");
            terminal.WriteLine("═══════════════════════════════════════");
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.Write("Chivalry: ");
            terminal.SetColor("bright_green");
            terminal.Write($"{character.Chivalry}");
            terminal.SetColor("gray");
            terminal.WriteLine("/1000");

            terminal.SetColor("yellow");
            terminal.Write("Darkness: ");
            terminal.SetColor("red");
            terminal.Write($"{character.Darkness}");
            terminal.SetColor("gray");
            terminal.WriteLine("/1000");

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.Write("Alignment: ");
            terminal.SetColor(color);
            terminal.WriteLine(text);

            // Show alignment bar
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.Write("Holy ");
            terminal.SetColor("bright_green");
            int chivBars = (int)Math.Min(10, character.Chivalry / 100);
            int darkBars = (int)Math.Min(10, character.Darkness / 100);
            terminal.Write(new string('█', chivBars));
            terminal.SetColor("gray");
            terminal.Write(new string('░', 10 - chivBars));
            terminal.Write(" | ");
            terminal.SetColor("red");
            terminal.Write(new string('█', darkBars));
            terminal.SetColor("gray");
            terminal.Write(new string('░', 10 - darkBars));
            terminal.WriteLine(" Evil");

            // Show abilities
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine("Alignment Abilities:");
            terminal.SetColor("white");
            foreach (var ability in GetAlignmentAbilities(character))
            {
                terminal.WriteLine($"  - {ability}");
            }
        }

        /// <summary>
        /// Actions that affect alignment
        /// </summary>
        public static class Actions
        {
            // Good actions
            public const string HelpedBeggar = "helped_beggar";
            public const string DonatedToTemple = "donated_temple";
            public const string DefendedInnocent = "defended_innocent";
            public const string SparedEnemy = "spared_enemy";
            public const string CompletedHolyQuest = "holy_quest";

            // Evil actions
            public const string MurderedInnocent = "murdered_innocent";
            public const string StoleFromMerchant = "stole_merchant";
            public const string BetrayedAlly = "betrayed_ally";
            public const string UsedDarkMagic = "dark_magic";
            public const string ServedDemon = "served_demon";
        }

        /// <summary>
        /// Process an alignment action
        /// </summary>
        public void ProcessAction(Character character, string action)
        {
            switch (action)
            {
                // Good actions
                case Actions.HelpedBeggar:
                    ModifyAlignment(character, 10, 0, "gave alms to the poor");
                    break;
                case Actions.DonatedToTemple:
                    ModifyAlignment(character, 25, -5, "donated generously to the temple");
                    break;
                case Actions.DefendedInnocent:
                    ModifyAlignment(character, 30, 0, "defended an innocent");
                    break;
                case Actions.SparedEnemy:
                    ModifyAlignment(character, 15, -10, "showed mercy to a defeated foe");
                    break;
                case Actions.CompletedHolyQuest:
                    ModifyAlignment(character, 50, -20, "completed a holy quest");
                    break;

                // Evil actions
                case Actions.MurderedInnocent:
                    ModifyAlignment(character, -30, 40, "murdered an innocent");
                    break;
                case Actions.StoleFromMerchant:
                    ModifyAlignment(character, -15, 20, "stole from a merchant");
                    break;
                case Actions.BetrayedAlly:
                    ModifyAlignment(character, -25, 30, "betrayed an ally");
                    break;
                case Actions.UsedDarkMagic:
                    ModifyAlignment(character, -10, 25, "used forbidden dark magic");
                    break;
                case Actions.ServedDemon:
                    ModifyAlignment(character, -50, 50, "made a pact with darkness");
                    break;
            }
        }
    }
}
