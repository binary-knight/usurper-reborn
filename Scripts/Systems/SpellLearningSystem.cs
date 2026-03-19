using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.UI;

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
        if (!SpellSystem.HasSpells(player))
        {
            terminal.WriteLine(Loc.Get("spell_learning.no_magic"), "red");
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
            UIHelper.WriteSectionHeader(terminal, Loc.Get("spell_learning.header"), "bright_magenta");
            terminal.WriteLine($"{Loc.Get("status.class")}: {player.ClassName} | {Loc.Get("ui.level")}: {player.Level} | {Loc.Get("ui.stat_mana")}: {player.Mana}/{player.MaxMana}", "cyan");
            terminal.WriteLine("");

            // Show current quickbar
            terminal.WriteLine(Loc.Get("spell_learning.your_quickbar"), "bright_white");
            for (int i = 0; i < 9; i++)
            {
                var slotId = player.Quickbar[i];
                if (string.IsNullOrEmpty(slotId))
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine(Loc.Get("spell_learning.empty_slot", i + 1));
                    continue;
                }

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
                        terminal.Write($" ({manaCost} MP) ");
                        terminal.SetColor("gray");
                        terminal.WriteLine(spell.Description);
                    }
                    else
                    {
                        player.Quickbar[i] = null;
                        terminal.SetColor("darkgray");
                        terminal.WriteLine(Loc.Get("spell_learning.empty_slot", i + 1));
                    }
                }
                else
                {
                    // Slot holds an ability (prestige classes share quickbar)
                    var ability = ClassAbilitySystem.GetAbility(slotId);
                    terminal.SetColor("bright_yellow");
                    terminal.Write($"  [{i + 1}] ");
                    terminal.SetColor("yellow");
                    terminal.Write($"{(ability?.Name ?? slotId),-22}");
                    terminal.SetColor("darkgray");
                    terminal.Write($"  (ability)");
                    terminal.WriteLine("");
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
                terminal.WriteLine(Loc.Get("spell_learning.known_unequipped"), "bright_white");
                for (int i = 0; i < knownUnequipped.Count; i++)
                {
                    char letter = (char)('a' + i);
                    int manaCost = SpellSystem.CalculateManaCost(knownUnequipped[i], player);
                    terminal.SetColor("darkgray");
                    terminal.Write("  [");
                    terminal.SetColor("bright_yellow");
                    terminal.Write($"{letter}");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("green");
                    terminal.Write($"{knownUnequipped[i].Name,-22} ({manaCost} MP) ");
                    terminal.SetColor("gray");
                    terminal.WriteLine(knownUnequipped[i].Description);
                }
            }

            // Show learnable but unknown spells
            var unknownLearnable = allSpells.Where(s =>
                !IsSpellKnown(player, s.Level) &&
                player.Level >= SpellSystem.GetLevelRequired(player.Class, s.Level)).ToList();

            if (unknownLearnable.Count > 0)
            {
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("spell_learning.available_learn"), "bright_white");
                foreach (var spell in unknownLearnable)
                {
                    int manaCost = SpellSystem.CalculateManaCost(spell, player);
                    string learnKey = $"L{spell.Level}";
                    terminal.SetColor("darkgray");
                    terminal.Write($"  [{learnKey,-3}] ");
                    terminal.SetColor("white");
                    terminal.Write($"{spell.Name,-22} ({manaCost} MP) ");
                    terminal.SetColor("gray");
                    terminal.WriteLine(spell.Description);
                }
            }

            // Show locked spells
            var locked = allSpells.Where(s =>
                player.Level < SpellSystem.GetLevelRequired(player.Class, s.Level)).ToList();

            if (locked.Count > 0)
            {
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("spell_learning.locked"), "darkgray");
                foreach (var spell in locked)
                {
                    int reqLevel = SpellSystem.GetLevelRequired(player.Class, spell.Level);
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($"       {spell.Name,-22} {Loc.Get("spell_learning.locked_requires", reqLevel)} - {spell.Description}");
                }
            }

            terminal.WriteLine("");
            if (GameConfig.ScreenReaderMode)
            {
                terminal.WriteLine(Loc.Get("spell_learning.menu_sr"), "bright_yellow");
                terminal.WriteLine(Loc.Get("spell_learning.menu_sr_2"), "bright_yellow");
            }
            else
            {
                terminal.WriteLine(Loc.Get("spell_learning.menu_visual"), "bright_yellow");
                terminal.WriteLine(Loc.Get("spell_learning.menu_visual_2"), "bright_yellow");
            }
            var input = await terminal.GetInput("> ");
            if (string.IsNullOrWhiteSpace(input)) continue;

            string cmd = input.Trim().ToUpper();

            if (cmd == "X") break;

            // Auto-fill quickbar
            if (cmd == "A")
            {
                GameEngine.AutoPopulateQuickbar(player);
                terminal.WriteLine(Loc.Get("spell_learning.auto_filled"), "bright_green");
                await SaveSystem.Instance.AutoSave(player);
                await Task.Delay(800);
                continue;
            }

            // Clear a slot
            if (cmd == "C")
            {
                terminal.Write(Loc.Get("spell_learning.clear_which"), "yellow");
                var clearInput = await terminal.GetInput("");
                if (int.TryParse(clearInput.Trim(), out int clearSlot) && clearSlot >= 1 && clearSlot <= 9)
                {
                    var clearedId = player.Quickbar[clearSlot - 1];
                    if (clearedId != null)
                    {
                        var lvl = SpellSystem.ParseQuickbarSpellLevel(clearedId);
                        var spellName = lvl.HasValue ? SpellSystem.GetSpellInfo(player.Class, lvl.Value)?.Name ?? clearedId : clearedId;
                        player.Quickbar[clearSlot - 1] = null;
                        terminal.WriteLine(Loc.Get("spell_learning.removed_slot", spellName, clearSlot), "cyan");
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
                    terminal.WriteLine(Loc.Get("spell_learning.invalid_spell"), "red");
                    await Task.Delay(800);
                    continue;
                }
                int reqLevel = SpellSystem.GetLevelRequired(player.Class, learnLevel);
                if (player.Level < reqLevel)
                {
                    terminal.WriteLine(Loc.Get("spell_learning.need_level", reqLevel), "red");
                    await Task.Delay(800);
                    continue;
                }
                if (IsSpellKnown(player, learnLevel))
                {
                    terminal.WriteLine(Loc.Get("spell_learning.already_known", spell.Name), "yellow");
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
                    terminal.WriteLine(Loc.Get("spell_learning.learned_slot", spell.Name, emptySlot + 1), "bright_green");
                }
                else
                {
                    terminal.WriteLine(Loc.Get("spell_learning.learned_full", spell.Name), "bright_green");
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
                    terminal.WriteLine(Loc.Get("spell_learning.dont_know"), "red");
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
                terminal.WriteLine(Loc.Get("spell_learning.forgot", spell.Name), "cyan");
                await SaveSystem.Instance.AutoSave(player);
                await Task.Delay(1000);
                continue;
            }

            // Equip to slot (1-9)
            if (int.TryParse(cmd, out int slotNum) && slotNum >= 1 && slotNum <= 9)
            {
                if (knownUnequipped.Count == 0)
                {
                    terminal.WriteLine(Loc.Get("spell_learning.no_spells_equip"), "yellow");
                    await Task.Delay(800);
                    continue;
                }

                var currentInSlot = player.Quickbar[slotNum - 1];
                if (currentInSlot != null)
                {
                    var currentLevel = SpellSystem.ParseQuickbarSpellLevel(currentInSlot);
                    var currentSpell = currentLevel.HasValue ? SpellSystem.GetSpellInfo(player.Class, currentLevel.Value) : null;
                    terminal.WriteLine(Loc.Get("spell_learning.slot_has", slotNum, currentSpell?.Name ?? currentInSlot), "cyan");
                }
                else
                {
                    terminal.WriteLine(Loc.Get("spell_learning.pick_spell", slotNum), "cyan");
                }

                for (int i = 0; i < knownUnequipped.Count; i++)
                {
                    char letter = (char)('a' + i);
                    int manaCost = SpellSystem.CalculateManaCost(knownUnequipped[i], player);
                    terminal.SetColor("darkgray");
                    terminal.Write("  [");
                    terminal.SetColor("bright_yellow");
                    terminal.Write($"{letter}");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("green");
                    terminal.WriteLine($"{knownUnequipped[i].Name,-22} ({manaCost} MP) {knownUnequipped[i].SpellType}");
                }
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_yellow");
                terminal.Write("0");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("spell_learning.cancel"));

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
                    terminal.WriteLine(Loc.Get("spell_learning.equipped_slot", chosen.Name, slotNum), "bright_green");
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
