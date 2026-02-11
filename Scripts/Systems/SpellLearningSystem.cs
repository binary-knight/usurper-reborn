using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Utils;
using UsurperRemake.Systems;

/// <summary>
/// Spell learning and quickbar equip interface at the Level Master.
/// Players learn spells and equip them to quickbar slots 1-9 for combat use.
/// </summary>
public static class SpellLearningSystem
{
    /// <summary>
    /// Interactive UI that shows all spells with learn/equip functionality.
    /// </summary>
    public static async Task ShowSpellLearningMenu(Character player, TerminalEmulator terminal)
    {
        if (terminal == null || player == null) return;
        if (player.Class != CharacterClass.Cleric && player.Class != CharacterClass.Magician && player.Class != CharacterClass.Sage)
        {
            terminal.WriteLine("Only magical professions can learn spells here.", "red");
            await Task.Delay(1000);
            return;
        }

        // Ensure quickbar is initialized
        if (player.Quickbar == null || player.Quickbar.Count < 9)
        {
            player.Quickbar = new List<string?>(new string?[9]);
        }

        while (true)
        {
            terminal.ClearScreen();
            terminal.WriteLine("═══ SPELL LIBRARY ═══", "bright_magenta");
            terminal.WriteLine($"Class: {player.Class} | Level: {player.Level} | Mana: {player.Mana}/{player.MaxMana}", "cyan");
            terminal.WriteLine("");

            // Show current quickbar
            terminal.WriteLine("  Your Quickbar:", "bright_white");
            for (int i = 0; i < 9; i++)
            {
                var slotId = player.Quickbar[i];
                var spellLevel = SpellSystem.ParseQuickbarSpellLevel(slotId);
                if (spellLevel.HasValue)
                {
                    var spell = SpellSystem.GetSpellInfo(player.Class, spellLevel.Value);
                    if (spell != null)
                    {
                        int manaCost = SpellSystem.CalculateManaCost(spell, player);
                        terminal.SetColor("bright_yellow");
                        terminal.Write($"  [{i + 1}] ");
                        terminal.SetColor("yellow");
                        terminal.Write($"{spell.Name,-22}");
                        terminal.SetColor("cyan");
                        terminal.WriteLine($" ({manaCost} MP) {spell.SpellType}");
                    }
                    else
                    {
                        player.Quickbar[i] = null;
                        terminal.SetColor("darkgray");
                        terminal.WriteLine($"  [{i + 1}] --- empty ---");
                    }
                }
                else
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($"  [{i + 1}] --- empty ---");
                }
            }

            // Get ALL spells for this class
            var allSpells = SpellSystem.GetAllSpellsForClass(player.Class).OrderBy(s => s.Level).ToList();
            var equippedLevels = new HashSet<int>();
            for (int i = 0; i < 9; i++)
            {
                var lvl = SpellSystem.ParseQuickbarSpellLevel(player.Quickbar[i]);
                if (lvl.HasValue) equippedLevels.Add(lvl.Value);
            }

            // Show known but unequipped spells
            var knownUnequipped = allSpells.Where(s =>
                IsSpellKnown(player, s.Level) && !equippedLevels.Contains(s.Level)).ToList();

            terminal.WriteLine("");
            if (knownUnequipped.Count > 0)
            {
                terminal.WriteLine("  Known Spells (not on quickbar):", "bright_white");
                for (int i = 0; i < knownUnequipped.Count; i++)
                {
                    char letter = (char)('a' + i);
                    int manaCost = SpellSystem.CalculateManaCost(knownUnequipped[i], player);
                    terminal.SetColor("green");
                    terminal.WriteLine($"  [{letter}] {knownUnequipped[i].Name,-22} ({manaCost} MP) Lv{knownUnequipped[i].Level} {knownUnequipped[i].SpellType}");
                }
            }

            // Show learnable but unknown spells
            var unknownLearnable = allSpells.Where(s =>
                !IsSpellKnown(player, s.Level) &&
                player.Level >= SpellSystem.GetLevelRequired(player.Class, s.Level)).ToList();

            if (unknownLearnable.Count > 0)
            {
                terminal.WriteLine("");
                terminal.WriteLine("  Available to Learn:", "bright_white");
                foreach (var spell in unknownLearnable)
                {
                    int manaCost = SpellSystem.CalculateManaCost(spell, player);
                    terminal.SetColor("white");
                    terminal.WriteLine($"      {spell.Name,-22} ({manaCost} MP) Lv{spell.Level} - Type 'L{spell.Level}' to learn");
                }
            }

            // Show locked spells
            var locked = allSpells.Where(s =>
                player.Level < SpellSystem.GetLevelRequired(player.Class, s.Level)).ToList();

            if (locked.Count > 0)
            {
                terminal.WriteLine("");
                terminal.WriteLine("  Locked (level too low):", "darkgray");
                foreach (var spell in locked)
                {
                    int reqLevel = SpellSystem.GetLevelRequired(player.Class, spell.Level);
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($"      {spell.Name,-22} Requires Lv{reqLevel}");
                }
            }

            terminal.WriteLine("");
            terminal.WriteLine("[1-9] Equip/change slot  [C] Clear slot  [A] Auto-fill", "yellow");
            terminal.WriteLine("[L#] Learn spell (e.g. L5)  [F#] Forget spell  [X] Exit", "yellow");
            var input = await terminal.GetInput("> ");
            if (string.IsNullOrWhiteSpace(input)) continue;

            string cmd = input.Trim().ToUpper();

            if (cmd == "X") break;

            // Auto-fill quickbar
            if (cmd == "A")
            {
                GameEngine.AutoPopulateQuickbar(player);
                terminal.WriteLine("Quickbar auto-filled!", "bright_green");
                await SaveSystem.Instance.AutoSave(player);
                await Task.Delay(800);
                continue;
            }

            // Clear a slot
            if (cmd == "C")
            {
                terminal.Write("Clear which slot? (1-9): ", "yellow");
                var clearInput = await terminal.GetInput("");
                if (int.TryParse(clearInput.Trim(), out int clearSlot) && clearSlot >= 1 && clearSlot <= 9)
                {
                    var clearedId = player.Quickbar[clearSlot - 1];
                    if (clearedId != null)
                    {
                        var lvl = SpellSystem.ParseQuickbarSpellLevel(clearedId);
                        var spellName = lvl.HasValue ? SpellSystem.GetSpellInfo(player.Class, lvl.Value)?.Name ?? clearedId : clearedId;
                        player.Quickbar[clearSlot - 1] = null;
                        terminal.WriteLine($"Removed {spellName} from slot {clearSlot}.", "cyan");
                        await SaveSystem.Instance.AutoSave(player);
                        await Task.Delay(800);
                    }
                }
                continue;
            }

            // Learn spell: L# (e.g. L5)
            if (cmd.StartsWith("L") && int.TryParse(cmd.Substring(1), out int learnLevel))
            {
                var spell = allSpells.FirstOrDefault(s => s.Level == learnLevel);
                if (spell == null)
                {
                    terminal.WriteLine("Invalid spell level!", "red");
                    await Task.Delay(800);
                    continue;
                }
                int reqLevel = SpellSystem.GetLevelRequired(player.Class, learnLevel);
                if (player.Level < reqLevel)
                {
                    terminal.WriteLine($"You need to be level {reqLevel} to learn this spell!", "red");
                    await Task.Delay(800);
                    continue;
                }
                if (IsSpellKnown(player, learnLevel))
                {
                    terminal.WriteLine($"You already know {spell.Name}!", "yellow");
                    await Task.Delay(800);
                    continue;
                }
                EnsureSpellSlot(player, learnLevel);
                player.Spell[learnLevel - 1][0] = true;
                // Auto-add to first empty quickbar slot
                string newQbId = SpellSystem.GetQuickbarId(learnLevel);
                int emptySlot = player.Quickbar.IndexOf(null);
                if (emptySlot >= 0)
                {
                    player.Quickbar[emptySlot] = newQbId;
                    terminal.WriteLine($"You have learned {spell.Name}! (added to slot {emptySlot + 1})", "bright_green");
                }
                else
                {
                    terminal.WriteLine($"You have learned {spell.Name}! (quickbar full - equip manually)", "bright_green");
                }
                await SaveSystem.Instance.AutoSave(player);
                await Task.Delay(1000);
                continue;
            }

            // Forget spell: F# (e.g. F5)
            if (cmd.StartsWith("F") && int.TryParse(cmd.Substring(1), out int forgetLevel))
            {
                var spell = allSpells.FirstOrDefault(s => s.Level == forgetLevel);
                if (spell == null || !IsSpellKnown(player, forgetLevel))
                {
                    terminal.WriteLine("You don't know that spell!", "red");
                    await Task.Delay(800);
                    continue;
                }
                player.Spell[forgetLevel - 1][0] = false;
                // Also remove from quickbar if equipped
                string qbId = SpellSystem.GetQuickbarId(forgetLevel);
                for (int i = 0; i < 9; i++)
                {
                    if (player.Quickbar[i] == qbId)
                        player.Quickbar[i] = null;
                }
                terminal.WriteLine($"You forget {spell.Name}.", "cyan");
                await SaveSystem.Instance.AutoSave(player);
                await Task.Delay(1000);
                continue;
            }

            // Equip to slot (1-9)
            if (int.TryParse(cmd, out int slotNum) && slotNum >= 1 && slotNum <= 9)
            {
                if (knownUnequipped.Count == 0)
                {
                    terminal.WriteLine("No known spells available to equip! Learn more spells first.", "yellow");
                    await Task.Delay(800);
                    continue;
                }

                var currentInSlot = player.Quickbar[slotNum - 1];
                if (currentInSlot != null)
                {
                    var currentLevel = SpellSystem.ParseQuickbarSpellLevel(currentInSlot);
                    var currentSpell = currentLevel.HasValue ? SpellSystem.GetSpellInfo(player.Class, currentLevel.Value) : null;
                    terminal.WriteLine($"Slot {slotNum} has: {currentSpell?.Name ?? currentInSlot}. Pick a replacement:", "cyan");
                }
                else
                {
                    terminal.WriteLine($"Pick a spell for slot {slotNum}:", "cyan");
                }

                for (int i = 0; i < knownUnequipped.Count; i++)
                {
                    char letter = (char)('a' + i);
                    int manaCost = SpellSystem.CalculateManaCost(knownUnequipped[i], player);
                    terminal.SetColor("green");
                    terminal.WriteLine($"  [{letter}] {knownUnequipped[i].Name,-22} ({manaCost} MP) {knownUnequipped[i].SpellType}");
                }
                terminal.SetColor("gray");
                terminal.WriteLine("  [0] Cancel");

                var pick = await terminal.GetInput("> ");
                if (string.IsNullOrWhiteSpace(pick) || pick.Trim() == "0") continue;

                char pickChar = pick.Trim().ToLower()[0];
                int pickIndex = pickChar - 'a';
                if (pickIndex >= 0 && pickIndex < knownUnequipped.Count)
                {
                    var chosen = knownUnequipped[pickIndex];
                    string qbId = SpellSystem.GetQuickbarId(chosen.Level);

                    // If this spell is already in another slot, clear that slot
                    for (int i = 0; i < 9; i++)
                    {
                        if (player.Quickbar[i] == qbId)
                            player.Quickbar[i] = null;
                    }

                    player.Quickbar[slotNum - 1] = qbId;
                    terminal.WriteLine($"Equipped {chosen.Name} to slot {slotNum}!", "bright_green");
                    await SaveSystem.Instance.AutoSave(player);
                    await Task.Delay(800);
                }
                continue;
            }
        }
    }

    /// <summary>
    /// Check if a spell is known by the player
    /// </summary>
    private static bool IsSpellKnown(Character player, int spellLevel)
    {
        int idx = spellLevel - 1;
        return player.Spell != null &&
               idx >= 0 &&
               idx < player.Spell.Count &&
               player.Spell[idx].Count > 0 &&
               player.Spell[idx][0];
    }

    /// <summary>
    /// Ensure the Spell array has enough entries for the given level
    /// </summary>
    private static void EnsureSpellSlot(Character player, int spellLevel)
    {
        while (player.Spell == null || player.Spell.Count <= spellLevel - 1)
        {
            if (player.Spell == null) player.Spell = new List<List<bool>>();
            player.Spell.Add(new List<bool> { false });
        }
        if (player.Spell[spellLevel - 1].Count == 0)
        {
            player.Spell[spellLevel - 1].Add(false);
        }
    }
}
