using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Data;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Global inventory system - accessible from any location via hotkey
    /// Allows viewing and managing equipped items
    /// </summary>
    public class InventorySystem
    {
        private TerminalEmulator terminal;
        private Character player;

        public InventorySystem(TerminalEmulator term, Character character)
        {
            terminal = term;
            player = character;
        }

        /// <summary>
        /// Main inventory menu - shows equipment and allows management
        /// </summary>
        public async Task ShowInventory()
        {
            bool exitInventory = false;

            while (!exitInventory)
            {
                terminal.ClearScreen();
                DisplayInventoryHeader();
                DisplayEquipmentOverview();
                DisplayInventoryMenu();

                var choice = await terminal.GetInput("Inventory: ");
                exitInventory = await ProcessInventoryChoice(choice.ToUpper().Trim());
            }
        }

        private void DisplayInventoryHeader()
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                              INVENTORY                                       ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            // Show weapon configuration
            terminal.SetColor("yellow");
            terminal.Write("Combat Style: ");
            terminal.SetColor("bright_white");
            if (player.IsTwoHanding)
                terminal.WriteLine("Two-Handed (+25% damage, -15% defense)");
            else if (player.IsDualWielding)
                terminal.WriteLine("Dual-Wield (+1 attack, -10% defense)");
            else if (player.HasShieldEquipped)
                terminal.WriteLine("Sword & Board (balanced, 20% block chance)");
            else
                terminal.WriteLine("One-Handed");
            terminal.WriteLine("");
        }

        private void DisplayEquipmentOverview()
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("═══ EQUIPPED ITEMS ═══");
            terminal.WriteLine("");

            // Weapons section
            terminal.SetColor("bright_red");
            terminal.WriteLine("[ WEAPONS ]");
            DisplaySlot("Main Hand", EquipmentSlot.MainHand, "1");
            DisplaySlot("Off Hand", EquipmentSlot.OffHand, "2");
            terminal.WriteLine("");

            // Armor section
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("[ ARMOR ]");
            DisplaySlot("Head", EquipmentSlot.Head, "3");
            DisplaySlot("Body", EquipmentSlot.Body, "4");
            DisplaySlot("Arms", EquipmentSlot.Arms, "5");
            DisplaySlot("Hands", EquipmentSlot.Hands, "6");
            DisplaySlot("Legs", EquipmentSlot.Legs, "7");
            DisplaySlot("Feet", EquipmentSlot.Feet, "8");
            DisplaySlot("Waist", EquipmentSlot.Waist, "9");
            DisplaySlot("Face", EquipmentSlot.Face, "F");
            DisplaySlot("Cloak", EquipmentSlot.Cloak, "C");
            terminal.WriteLine("");

            // Accessories section
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("[ ACCESSORIES ]");
            DisplaySlot("Neck", EquipmentSlot.Neck, "N");
            DisplaySlot("Left Ring", EquipmentSlot.LFinger, "L");
            DisplaySlot("Right Ring", EquipmentSlot.RFinger, "R");
            terminal.WriteLine("");

            // Stats summary
            DisplayStatsSummary();

            // Backpack (unequipped items)
            DisplayBackpack();
        }

        private void DisplayBackpack()
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("═══ BACKPACK ═══");

            if (player.Inventory == null || player.Inventory.Count == 0)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine("  (Empty - pick up items in the dungeon!)");
                terminal.WriteLine("");
                return;
            }

            terminal.WriteLine("");
            int index = 1;
            foreach (var item in player.Inventory.Take(20)) // Limit display to 20 items
            {
                terminal.SetColor("gray");
                terminal.Write($"  [B{index}] ");

                // Color based on item type
                string itemColor = item.Type switch
                {
                    ObjType.Weapon => "bright_red",
                    ObjType.Body or ObjType.Head or ObjType.Arms or ObjType.Legs => "bright_cyan",
                    ObjType.Shield => "cyan",
                    ObjType.Fingers or ObjType.Neck => "bright_magenta",
                    _ => "white"
                };
                terminal.SetColor(itemColor);
                terminal.Write(item.Name);

                terminal.SetColor("gray");
                terminal.Write($" - {item.Value:N0}g");

                // Show key stats
                var stats = new List<string>();
                if (item.Attack > 0) stats.Add($"Att:{item.Attack}");
                if (item.Defence > 0) stats.Add($"Def:{item.Defence}");
                if (item.Strength != 0) stats.Add($"Str:{item.Strength:+#;-#;0}");
                if (item.Dexterity != 0) stats.Add($"Dex:{item.Dexterity:+#;-#;0}");
                if (item.Wisdom != 0) stats.Add($"Wis:{item.Wisdom:+#;-#;0}");

                if (stats.Count > 0)
                {
                    terminal.SetColor("darkgray");
                    terminal.Write($" ({string.Join(", ", stats.Take(3))})");
                }

                terminal.WriteLine("");
                index++;
            }

            if (player.Inventory.Count > 20)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine($"  ... and {player.Inventory.Count - 20} more items");
            }

            terminal.WriteLine("");
        }

        private void DisplaySlot(string slotName, EquipmentSlot slot, string key)
        {
            var item = player.GetEquipment(slot);

            terminal.SetColor("gray");
            terminal.Write($"  [{key}] ");
            terminal.SetColor("white");
            terminal.Write($"{slotName,-12}: ");

            if (item != null)
            {
                // Color based on rarity
                terminal.SetColor(GetRarityColor(item.Rarity));
                terminal.Write(item.Name);

                // Show key stats
                terminal.SetColor("gray");
                var stats = GetItemStatSummary(item);
                if (!string.IsNullOrEmpty(stats))
                {
                    terminal.Write($" ({stats})");
                }
                terminal.WriteLine("");
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine("Empty");
            }
        }

        private string GetRarityColor(EquipmentRarity rarity)
        {
            return rarity switch
            {
                EquipmentRarity.Common => "white",
                EquipmentRarity.Uncommon => "green",
                EquipmentRarity.Rare => "blue",
                EquipmentRarity.Epic => "magenta",
                EquipmentRarity.Legendary => "yellow",
                EquipmentRarity.Artifact => "bright_red",
                _ => "white"
            };
        }

        private string GetItemStatSummary(Equipment item)
        {
            var stats = new List<string>();

            if (item.WeaponPower > 0) stats.Add($"WP:{item.WeaponPower}");
            if (item.ArmorClass > 0) stats.Add($"AC:{item.ArmorClass}");
            if (item.ShieldBonus > 0) stats.Add($"Block:{item.ShieldBonus}");
            if (item.StrengthBonus != 0) stats.Add($"Str:{item.StrengthBonus:+#;-#;0}");
            if (item.DexterityBonus != 0) stats.Add($"Dex:{item.DexterityBonus:+#;-#;0}");
            if (item.ConstitutionBonus != 0) stats.Add($"Con:{item.ConstitutionBonus:+#;-#;0}");
            if (item.IntelligenceBonus != 0) stats.Add($"Int:{item.IntelligenceBonus:+#;-#;0}");
            if (item.WisdomBonus != 0) stats.Add($"Wis:{item.WisdomBonus:+#;-#;0}");
            if (item.MaxHPBonus != 0) stats.Add($"HP:{item.MaxHPBonus:+#;-#;0}");
            if (item.MaxManaBonus != 0) stats.Add($"MP:{item.MaxManaBonus:+#;-#;0}");
            if (item.MagicResistance != 0) stats.Add($"MR:{item.MagicResistance:+#;-#;0}");
            if (item.CriticalChanceBonus != 0) stats.Add($"Crit:{item.CriticalChanceBonus}%");
            if (item.LifeSteal != 0) stats.Add($"LS:{item.LifeSteal}%");

            return string.Join(", ", stats.Take(4)); // Limit to 4 stats for display
        }

        private void DisplayStatsSummary()
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("═══ EQUIPMENT BONUSES ═══");

            // Calculate total bonuses from equipment
            int totalWeapPow = 0, totalArmPow = 0;
            int totalStr = 0, totalDex = 0, totalCon = 0, totalInt = 0, totalWis = 0, totalCha = 0;
            int totalMaxHP = 0, totalMaxMana = 0, totalMR = 0, totalDef = 0;

            foreach (var slot in Enum.GetValues<EquipmentSlot>())
            {
                var item = player.GetEquipment(slot);
                if (item != null)
                {
                    totalWeapPow += item.WeaponPower;
                    totalArmPow += item.ArmorClass + item.ShieldBonus;
                    totalStr += item.StrengthBonus;
                    totalDex += item.DexterityBonus;
                    totalCon += item.ConstitutionBonus;
                    totalInt += item.IntelligenceBonus;
                    totalWis += item.WisdomBonus;
                    totalCha += item.CharismaBonus;
                    totalMaxHP += item.MaxHPBonus;
                    totalMaxMana += item.MaxManaBonus;
                    totalMR += item.MagicResistance;
                    totalDef += item.DefenceBonus;
                }
            }

            terminal.SetColor("white");
            terminal.Write("Weapon Power: ");
            terminal.SetColor("bright_red");
            terminal.Write($"{totalWeapPow}");
            terminal.SetColor("white");
            terminal.Write("  |  Armor Class: ");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"{totalArmPow}");

            // Stat bonuses
            if (totalStr != 0 || totalDex != 0 || totalCon != 0)
            {
                terminal.SetColor("white");
                terminal.Write("Stats: ");
                if (totalStr != 0) { terminal.SetColor("green"); terminal.Write($"Str {totalStr:+#;-#;0}  "); }
                if (totalDex != 0) { terminal.SetColor("green"); terminal.Write($"Dex {totalDex:+#;-#;0}  "); }
                if (totalCon != 0) { terminal.SetColor("green"); terminal.Write($"Con {totalCon:+#;-#;0}  "); }
                if (totalInt != 0) { terminal.SetColor("cyan"); terminal.Write($"Int {totalInt:+#;-#;0}  "); }
                if (totalWis != 0) { terminal.SetColor("cyan"); terminal.Write($"Wis {totalWis:+#;-#;0}  "); }
                if (totalCha != 0) { terminal.SetColor("cyan"); terminal.Write($"Cha {totalCha:+#;-#;0}  "); }
                terminal.WriteLine("");
            }

            if (totalMaxHP != 0 || totalMaxMana != 0 || totalMR != 0 || totalDef != 0)
            {
                terminal.SetColor("white");
                terminal.Write("Other: ");
                if (totalMaxHP != 0) { terminal.SetColor("red"); terminal.Write($"MaxHP {totalMaxHP:+#;-#;0}  "); }
                if (totalMaxMana != 0) { terminal.SetColor("blue"); terminal.Write($"MaxMP {totalMaxMana:+#;-#;0}  "); }
                if (totalMR != 0) { terminal.SetColor("magenta"); terminal.Write($"MagicRes {totalMR:+#;-#;0}  "); }
                if (totalDef != 0) { terminal.SetColor("cyan"); terminal.Write($"Def {totalDef:+#;-#;0}  "); }
                terminal.WriteLine("");
            }

            terminal.WriteLine("");
        }

        private void DisplayInventoryMenu()
        {
            terminal.SetColor("gray");
            terminal.WriteLine("────────────────────────────────────────────────────────────────────────────────");
            terminal.SetColor("white");
            terminal.WriteLine("Options: [1-9,F,C,N,L,R] Manage Slot  |  [B#] Manage Backpack Item  |  [U]nequip All");
            terminal.WriteLine("         [D]rop Item  |  [Q]uit Inventory");
            terminal.WriteLine("");
        }

        private async Task<bool> ProcessInventoryChoice(string choice)
        {
            switch (choice)
            {
                case "Q":
                case "":
                    return true;

                case "1":
                    await ManageSlot(EquipmentSlot.MainHand);
                    break;
                case "2":
                    await ManageSlot(EquipmentSlot.OffHand);
                    break;
                case "3":
                    await ManageSlot(EquipmentSlot.Head);
                    break;
                case "4":
                    await ManageSlot(EquipmentSlot.Body);
                    break;
                case "5":
                    await ManageSlot(EquipmentSlot.Arms);
                    break;
                case "6":
                    await ManageSlot(EquipmentSlot.Hands);
                    break;
                case "7":
                    await ManageSlot(EquipmentSlot.Legs);
                    break;
                case "8":
                    await ManageSlot(EquipmentSlot.Feet);
                    break;
                case "9":
                    await ManageSlot(EquipmentSlot.Waist);
                    break;
                case "F":
                    await ManageSlot(EquipmentSlot.Face);
                    break;
                case "C":
                    await ManageSlot(EquipmentSlot.Cloak);
                    break;
                case "N":
                    await ManageSlot(EquipmentSlot.Neck);
                    break;
                case "L":
                    await ManageSlot(EquipmentSlot.LFinger);
                    break;
                case "R":
                    await ManageSlot(EquipmentSlot.RFinger);
                    break;
                case "U":
                    await UnequipAll();
                    break;
                case "D":
                    await DropItem();
                    break;
                default:
                    // Check for B# format (backpack item)
                    if (choice.StartsWith("B") && int.TryParse(choice.Substring(1), out int backpackIndex))
                    {
                        await ManageBackpackItem(backpackIndex);
                    }
                    else
                    {
                        terminal.WriteLine("Invalid choice.", "red");
                        await Task.Delay(500);
                    }
                    break;
            }

            return false;
        }

        private async Task ManageBackpackItem(int index)
        {
            if (player.Inventory == null || index < 1 || index > player.Inventory.Count)
            {
                terminal.WriteLine("Invalid item number.", "red");
                await Task.Delay(500);
                return;
            }

            var item = player.Inventory[index - 1];

            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                           MANAGE ITEM                                        ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine($"  {item.Name}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  Value: {item.Value:N0} gold");
            terminal.WriteLine($"  Type: {item.Type}");
            terminal.WriteLine("");

            // Show stats
            var stats = new List<string>();
            if (item.Attack > 0) stats.Add($"Attack: +{item.Attack}");
            if (item.Defence > 0) stats.Add($"Defence: +{item.Defence}");
            if (item.Strength != 0) stats.Add($"Strength: {item.Strength:+#;-#;0}");
            if (item.Dexterity != 0) stats.Add($"Dexterity: {item.Dexterity:+#;-#;0}");
            if (item.Wisdom != 0) stats.Add($"Wisdom: {item.Wisdom:+#;-#;0}");
            if (item.MagicProperties?.Mana != 0) stats.Add($"Mana: {item.MagicProperties.Mana:+#;-#;0}");

            if (stats.Count > 0)
            {
                terminal.SetColor("white");
                foreach (var stat in stats)
                {
                    terminal.WriteLine($"  {stat}");
                }
                terminal.WriteLine("");
            }

            terminal.SetColor("white");
            terminal.WriteLine("  [E] Equip Item");
            terminal.WriteLine("  [D] Drop Item");
            terminal.WriteLine("  [Q] Back");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Choice: ");

            switch (choice.ToUpper())
            {
                case "E":
                    await EquipFromBackpack(index - 1);
                    break;
                case "D":
                    player.Inventory.Remove(item);
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"You drop the {item.Name}.");
                    await Task.Delay(1000);
                    break;
            }
        }

        private async Task EquipFromBackpack(int itemIndex)
        {
            if (player.Inventory == null || itemIndex < 0 || itemIndex >= player.Inventory.Count)
            {
                terminal.WriteLine("Invalid item.", "red");
                await Task.Delay(1000);
                return;
            }

            var item = player.Inventory[itemIndex];

            // Determine which slot this item goes in based on ObjType
            // For magic items, use MagicType to determine the slot
            EquipmentSlot targetSlot;
            bool isMagicEquipment = false;

            if (item.Type == ObjType.Magic)
            {
                // Check if this magic item is equippable based on MagicType
                // Cast to int to avoid namespace conflicts between UsurperRemake.MagicItemType and global::MagicItemType
                targetSlot = (int)item.MagicType switch
                {
                    5 => EquipmentSlot.LFinger,   // MagicItemType.Fingers = 5
                    10 => EquipmentSlot.Neck,     // MagicItemType.Neck = 10
                    9 => EquipmentSlot.Waist,     // MagicItemType.Waist = 9
                    _ => EquipmentSlot.MainHand   // Non-equippable magic item
                };

                // Only equippable if it has a valid MagicType
                int magicType = (int)item.MagicType;
                isMagicEquipment = magicType == 5 || magicType == 10 || magicType == 9;
            }
            else
            {
                targetSlot = item.Type switch
                {
                    ObjType.Weapon => EquipmentSlot.MainHand,
                    ObjType.Shield => EquipmentSlot.OffHand,
                    ObjType.Body => EquipmentSlot.Body,
                    ObjType.Head => EquipmentSlot.Head,
                    ObjType.Arms => EquipmentSlot.Arms,
                    ObjType.Hands => EquipmentSlot.Hands,
                    ObjType.Legs => EquipmentSlot.Legs,
                    ObjType.Feet => EquipmentSlot.Feet,
                    ObjType.Waist => EquipmentSlot.Waist,
                    ObjType.Neck => EquipmentSlot.Neck,
                    ObjType.Face => EquipmentSlot.Face,
                    ObjType.Fingers => EquipmentSlot.LFinger,
                    ObjType.Abody => EquipmentSlot.Cloak,
                    _ => EquipmentSlot.MainHand // Default
                };
            }

            // Check if item type is equippable
            if (item.Type == ObjType.Food || item.Type == ObjType.Drink ||
                item.Type == ObjType.Potion || (item.Type == ObjType.Magic && !isMagicEquipment))
            {
                terminal.WriteLine("This item cannot be equipped.", "red");
                await Task.Delay(1000);
                return;
            }

            // Determine handedness for weapons (default to None for non-weapons like armor)
            WeaponHandedness handedness = WeaponHandedness.None;
            if (item.Type == ObjType.Weapon)
            {
                // Check if it's a two-handed weapon based on name
                string nameLower = item.Name.ToLower();
                if (nameLower.Contains("two-hand") || nameLower.Contains("2h") ||
                    nameLower.Contains("greatsword") || nameLower.Contains("greataxe") ||
                    nameLower.Contains("halberd") || nameLower.Contains("pike") ||
                    nameLower.Contains("longbow") || nameLower.Contains("crossbow") ||
                    nameLower.Contains("staff") || nameLower.Contains("quarterstaff"))
                {
                    handedness = WeaponHandedness.TwoHanded;
                }
                else
                {
                    handedness = WeaponHandedness.OneHanded;
                }
            }
            else if (item.Type == ObjType.Shield)
            {
                handedness = WeaponHandedness.OffHandOnly;
            }

            // For rings, ask which finger
            if (item.Type == ObjType.Fingers || (int)item.MagicType == 5)
            {
                terminal.WriteLine("");
                terminal.SetColor("cyan");
                terminal.WriteLine("Equip to which finger?");
                terminal.SetColor("white");
                terminal.WriteLine("  (L) Left finger");
                terminal.WriteLine("  (R) Right finger");
                terminal.WriteLine("  (C) Cancel");
                terminal.Write("Choice: ");
                var fingerChoice = await terminal.GetInput("");
                if (fingerChoice.ToUpper() == "R")
                    targetSlot = EquipmentSlot.RFinger;
                else if (fingerChoice.ToUpper() == "L")
                    targetSlot = EquipmentSlot.LFinger;
                else
                {
                    terminal.WriteLine("Cancelled.", "gray");
                    await Task.Delay(1000);
                    return;
                }
            }

            // Convert Item to Equipment and register in database
            var equipment = new Equipment
            {
                Name = item.Name,
                Slot = targetSlot,
                Handedness = handedness,
                WeaponPower = item.Attack,
                ArmorClass = item.Armor,
                ShieldBonus = item.Type == ObjType.Shield ? item.Armor : 0,
                DefenceBonus = item.Defence,
                StrengthBonus = item.Strength,
                DexterityBonus = item.Dexterity,
                WisdomBonus = item.Wisdom,
                CharismaBonus = item.Charisma,
                MaxHPBonus = item.HP,
                MaxManaBonus = item.Mana,
                Value = item.Value,
                IsCursed = item.IsCursed,
                Rarity = EquipmentRarity.Common
            };

            // Register in database to get an ID
            EquipmentDatabase.RegisterDynamic(equipment);

            // For one-handed weapons, ask which slot to use
            EquipmentSlot? finalSlot = null;
            if (Character.RequiresSlotSelection(equipment))
            {
                finalSlot = await PromptForWeaponSlot();
                if (finalSlot == null)
                {
                    terminal.WriteLine("Cancelled.", "gray");
                    await Task.Delay(1000);
                    return;
                }
            }

            // Equip the new item (EquipItem handles old equipment management)
            if (player.EquipItem(equipment, finalSlot, out string message))
            {
                // Remove from backpack
                player.Inventory.RemoveAt(itemIndex);

                terminal.SetColor("green");
                terminal.WriteLine($"Equipped {item.Name}.");
                if (!string.IsNullOrEmpty(message))
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(message);
                }
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine($"Cannot equip: {message}");
            }

            player.RecalculateStats();
            await Task.Delay(1500);
        }

        /// <summary>
        /// Prompt player to choose which hand to equip a one-handed weapon in
        /// </summary>
        private async Task<EquipmentSlot?> PromptForWeaponSlot()
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine("This is a one-handed weapon. Where would you like to equip it?");
            terminal.WriteLine("");

            // Show current equipment in both slots
            var mainHandItem = player.GetEquipment(EquipmentSlot.MainHand);
            var offHandItem = player.GetEquipment(EquipmentSlot.OffHand);

            terminal.SetColor("white");
            terminal.Write("  (M) Main Hand: ");
            if (mainHandItem != null)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(mainHandItem.Name);
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("Empty");
            }

            terminal.SetColor("white");
            terminal.Write("  (O) Off-Hand:  ");
            if (offHandItem != null)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(offHandItem.Name);
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("Empty");
            }

            terminal.SetColor("white");
            terminal.WriteLine("  (C) Cancel");
            terminal.WriteLine("");

            terminal.Write("Your choice: ");
            var slotChoice = await terminal.GetInput("");

            return slotChoice.ToUpper() switch
            {
                "M" => EquipmentSlot.MainHand,
                "O" => EquipmentSlot.OffHand,
                _ => null // Cancel
            };
        }

        private async Task DropItem()
        {
            if (player.Inventory == null || player.Inventory.Count == 0)
            {
                terminal.WriteLine("Your backpack is empty.", "gray");
                await Task.Delay(1000);
                return;
            }

            terminal.WriteLine("Enter item number to drop (B1, B2, etc): ", "yellow");
            var input = await terminal.GetInput("");

            if (input.ToUpper().StartsWith("B") && int.TryParse(input.Substring(1), out int index))
            {
                if (index >= 1 && index <= player.Inventory.Count)
                {
                    var item = player.Inventory[index - 1];
                    player.Inventory.RemoveAt(index - 1);
                    terminal.WriteLine($"You drop the {item.Name}.", "yellow");
                    await Task.Delay(1000);
                }
                else
                {
                    terminal.WriteLine("Invalid item number.", "red");
                    await Task.Delay(500);
                }
            }
        }

        private async Task ManageSlot(EquipmentSlot slot)
        {
            terminal.ClearScreen();

            var currentItem = player.GetEquipment(slot);
            var slotName = GetSlotDisplayName(slot);

            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"═══ {slotName.ToUpper()} SLOT ═══");
            terminal.WriteLine("");

            // Show current item
            terminal.SetColor("white");
            terminal.Write("Currently Equipped: ");
            if (currentItem != null)
            {
                terminal.SetColor(GetRarityColor(currentItem.Rarity));
                terminal.WriteLine(currentItem.Name);
                DisplayItemDetails(currentItem);
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("Nothing");
            }
            terminal.WriteLine("");

            // Show options
            terminal.SetColor("cyan");
            terminal.WriteLine("Options:");
            terminal.SetColor("white");
            if (currentItem != null)
            {
                terminal.WriteLine("  [U] Unequip this item");
            }
            terminal.WriteLine("  [Q] Return to inventory");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Choice: ");

            if (choice.ToUpper().Trim() == "U" && currentItem != null)
            {
                var unequipped = player.UnequipSlot(slot);
                if (unequipped != null)
                {
                    // Convert equipment to legacy Item and add to backpack
                    var legacyItem = new global::Item
                    {
                        Name = unequipped.Name,
                        Type = unequipped.Slot switch
                        {
                            EquipmentSlot.MainHand or EquipmentSlot.OffHand => ObjType.Weapon,
                            EquipmentSlot.Body => ObjType.Body,
                            EquipmentSlot.Head => ObjType.Head,
                            EquipmentSlot.Arms => ObjType.Arms,
                            EquipmentSlot.Legs => ObjType.Legs,
                            EquipmentSlot.Hands => ObjType.Hands,
                            EquipmentSlot.Feet => ObjType.Feet,
                            EquipmentSlot.LFinger or EquipmentSlot.RFinger => ObjType.Fingers,
                            EquipmentSlot.Neck or EquipmentSlot.Neck2 => ObjType.Neck,
                            EquipmentSlot.Cloak => ObjType.Abody,
                            EquipmentSlot.Waist => ObjType.Waist,
                            EquipmentSlot.Face => ObjType.Face,
                            _ => ObjType.Body
                        },
                        Attack = unequipped.WeaponPower,
                        Armor = unequipped.ArmorClass + unequipped.ShieldBonus,
                        Defence = unequipped.DefenceBonus,
                        Strength = unequipped.StrengthBonus,
                        Dexterity = unequipped.DexterityBonus,
                        Wisdom = unequipped.WisdomBonus,
                        HP = unequipped.MaxHPBonus,
                        Mana = unequipped.MaxManaBonus,
                        Value = unequipped.Value,
                        IsCursed = unequipped.IsCursed
                    };
                    player.Inventory.Add(legacyItem);
                    player.RecalculateStats();

                    terminal.SetColor("yellow");
                    terminal.WriteLine($"Unequipped {unequipped.Name}.");
                    terminal.SetColor("gray");
                    terminal.WriteLine("(Item returned to your backpack)");
                    await Task.Delay(1500);
                }
            }
        }

        private void DisplayItemDetails(Equipment item)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  Type: {item.Slot}  |  Rarity: {item.Rarity}");

            var stats = new List<string>();

            // Combat stats
            if (item.WeaponPower > 0) stats.Add($"Weapon Power: {item.WeaponPower}");
            if (item.ArmorClass > 0) stats.Add($"Armor Class: {item.ArmorClass}");
            if (item.ShieldBonus > 0) stats.Add($"Shield Block: {item.ShieldBonus}");

            // Attribute bonuses
            if (item.StrengthBonus != 0) stats.Add($"Strength: {item.StrengthBonus:+#;-#;0}");
            if (item.DexterityBonus != 0) stats.Add($"Dexterity: {item.DexterityBonus:+#;-#;0}");
            if (item.ConstitutionBonus != 0) stats.Add($"Constitution: {item.ConstitutionBonus:+#;-#;0}");
            if (item.IntelligenceBonus != 0) stats.Add($"Intelligence: {item.IntelligenceBonus:+#;-#;0}");
            if (item.WisdomBonus != 0) stats.Add($"Wisdom: {item.WisdomBonus:+#;-#;0}");
            if (item.CharismaBonus != 0) stats.Add($"Charisma: {item.CharismaBonus:+#;-#;0}");
            if (item.AgilityBonus != 0) stats.Add($"Agility: {item.AgilityBonus:+#;-#;0}");
            if (item.StaminaBonus != 0) stats.Add($"Stamina: {item.StaminaBonus:+#;-#;0}");

            // Other bonuses
            if (item.MaxHPBonus != 0) stats.Add($"Max HP: {item.MaxHPBonus:+#;-#;0}");
            if (item.MaxManaBonus != 0) stats.Add($"Max Mana: {item.MaxManaBonus:+#;-#;0}");
            if (item.DefenceBonus != 0) stats.Add($"Defence: {item.DefenceBonus:+#;-#;0}");
            if (item.MagicResistance != 0) stats.Add($"Magic Resist: {item.MagicResistance:+#;-#;0}");
            if (item.CriticalChanceBonus != 0) stats.Add($"Crit Chance: {item.CriticalChanceBonus}%");
            if (item.CriticalDamageBonus != 0) stats.Add($"Crit Damage: {item.CriticalDamageBonus}%");
            if (item.LifeSteal != 0) stats.Add($"Life Steal: {item.LifeSteal}%");

            if (stats.Count > 0)
            {
                terminal.SetColor("green");
                foreach (var stat in stats)
                {
                    terminal.WriteLine($"  - {stat}");
                }
            }

            // Requirements
            var reqs = new List<string>();
            if (item.MinLevel > 1) reqs.Add($"Level {item.MinLevel}");
            if (item.StrengthRequired > 0) reqs.Add($"Str {item.StrengthRequired}");
            if (item.RequiresGood) reqs.Add("Good alignment");
            if (item.RequiresEvil) reqs.Add("Evil alignment");
            if (item.IsUnique) reqs.Add("UNIQUE");

            if (reqs.Count > 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  Requires: {string.Join(", ", reqs)}");
            }

            terminal.SetColor("gray");
            terminal.WriteLine($"  Value: {item.Value:N0} gold");
        }

        private string GetSlotDisplayName(EquipmentSlot slot)
        {
            return slot switch
            {
                EquipmentSlot.MainHand => "Main Hand",
                EquipmentSlot.OffHand => "Off Hand",
                EquipmentSlot.Head => "Head",
                EquipmentSlot.Body => "Body",
                EquipmentSlot.Arms => "Arms",
                EquipmentSlot.Hands => "Hands",
                EquipmentSlot.Legs => "Legs",
                EquipmentSlot.Feet => "Feet",
                EquipmentSlot.Waist => "Waist",
                EquipmentSlot.Neck => "Neck",
                EquipmentSlot.Face => "Face",
                EquipmentSlot.Cloak => "Cloak",
                EquipmentSlot.LFinger => "Left Ring",
                EquipmentSlot.RFinger => "Right Ring",
                _ => slot.ToString()
            };
        }

        private async Task UnequipAll()
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("Unequipping all items...");

            int count = 0;
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
            {
                if (slot == EquipmentSlot.None) continue;
                var equipment = player.UnequipSlot(slot);
                if (equipment != null)
                {
                    // Convert equipment to legacy Item and add to backpack
                    var item = new global::Item
                    {
                        Name = equipment.Name,
                        Type = equipment.Slot switch
                        {
                            EquipmentSlot.MainHand or EquipmentSlot.OffHand => ObjType.Weapon,
                            EquipmentSlot.Body => ObjType.Body,
                            EquipmentSlot.Head => ObjType.Head,
                            EquipmentSlot.Arms => ObjType.Arms,
                            EquipmentSlot.Legs => ObjType.Legs,
                            EquipmentSlot.Hands => ObjType.Hands,
                            EquipmentSlot.Feet => ObjType.Feet,
                            EquipmentSlot.LFinger or EquipmentSlot.RFinger => ObjType.Fingers,
                            EquipmentSlot.Neck or EquipmentSlot.Neck2 => ObjType.Neck,
                            EquipmentSlot.Cloak => ObjType.Abody,
                            EquipmentSlot.Waist => ObjType.Waist,
                            EquipmentSlot.Face => ObjType.Face,
                            _ => ObjType.Body
                        },
                        Attack = equipment.WeaponPower,
                        Armor = equipment.ArmorClass + equipment.ShieldBonus,
                        Defence = equipment.DefenceBonus,
                        Strength = equipment.StrengthBonus,
                        Dexterity = equipment.DexterityBonus,
                        Wisdom = equipment.WisdomBonus,
                        HP = equipment.MaxHPBonus,
                        Mana = equipment.MaxManaBonus,
                        Value = equipment.Value,
                        IsCursed = equipment.IsCursed
                    };
                    player.Inventory.Add(item);
                    count++;
                }
            }

            if (count > 0)
            {
                terminal.SetColor("green");
                terminal.WriteLine($"Unequipped {count} item(s) to your backpack.");
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("No items to unequip.");
            }

            await Task.Delay(1500);
        }
    }
}
