using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Data;
using UsurperRemake.UI;

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
        private int _backpackPage = 0;
        private const int BackpackPageSize = 15;

        // Filtered inventory for specific slots
        private Dictionary<int, int>? filteredInventoryMap = null;
        private int _filteredBackpackPage = 0;
        private List<Item>? filteredInventory = null;
        private EquipmentSlot? currFilteredSlot = null;

        public InventorySystem(TerminalEmulator term, Character character)
        {
            terminal = term;
            player = character;
        }

        private void InvalidateFilteredCache()
        {
            filteredInventory = null;
            filteredInventoryMap = null;
            currFilteredSlot = null;
        }

        /// <summary>
        /// Main inventory menu - shows equipment and allows management
        /// </summary>
        public async Task ShowInventory()
        {
            // Electron graphical client — fully interactive graphical inventory
            if (GameConfig.ElectronMode)
            {
                await RunElectronInventory();
                return;
            }

            bool exitInventory = false;

            while (!exitInventory)
            {
                terminal.ClearScreen();
                DisplayInventoryHeader();
                DisplayEquipmentOverview();
                DisplayInventoryMenu();

                var choice = await terminal.GetInput(Loc.Get("inventory.prompt"));
                exitInventory = await ProcessInventoryChoice(choice.ToUpper().Trim());
            }
        }

        private static ObjType SlotToObjType(EquipmentSlot slot) => slot switch
        {
            EquipmentSlot.MainHand => ObjType.Weapon,
            EquipmentSlot.OffHand => ObjType.Shield,
            EquipmentSlot.Head => ObjType.Head,
            EquipmentSlot.Body => ObjType.Body,
            EquipmentSlot.Arms => ObjType.Arms,
            EquipmentSlot.Hands => ObjType.Hands,
            EquipmentSlot.Legs => ObjType.Legs,
            EquipmentSlot.Feet => ObjType.Feet,
            EquipmentSlot.Waist => ObjType.Waist,
            EquipmentSlot.Face => ObjType.Face,
            EquipmentSlot.Cloak => ObjType.Abody,
            EquipmentSlot.Neck => ObjType.Neck,
            EquipmentSlot.LFinger => ObjType.Fingers,
            EquipmentSlot.RFinger => ObjType.Fingers,
            _ => ObjType.Weapon
        };

        private static EquipmentSlot InferSlotFromItem(Item item) => item.Type switch
        {
            ObjType.Weapon => EquipmentSlot.MainHand,
            ObjType.Shield => EquipmentSlot.OffHand,
            ObjType.Head => EquipmentSlot.Head,
            ObjType.Body => EquipmentSlot.Body,
            ObjType.Arms => EquipmentSlot.Arms,
            ObjType.Hands => EquipmentSlot.Hands,
            ObjType.Legs => EquipmentSlot.Legs,
            ObjType.Feet => EquipmentSlot.Feet,
            ObjType.Waist => EquipmentSlot.Waist,
            ObjType.Face => EquipmentSlot.Face,
            ObjType.Abody => EquipmentSlot.Cloak,
            ObjType.Neck => EquipmentSlot.Neck,
            ObjType.Fingers => EquipmentSlot.LFinger,
            _ => EquipmentSlot.None
        };

        private void EmitInventoryEvent()
        {
            var slots = new[] {
                EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Head,
                EquipmentSlot.Body, EquipmentSlot.Arms, EquipmentSlot.Hands,
                EquipmentSlot.Legs, EquipmentSlot.Feet, EquipmentSlot.Waist,
                EquipmentSlot.Face, EquipmentSlot.Cloak, EquipmentSlot.Neck,
                EquipmentSlot.LFinger, EquipmentSlot.RFinger
            };

            var equipment = slots.Select(s =>
            {
                var item = player.GetEquipment(s);
                if (item == null) return new { slot = s.ToString(), name = "(empty)", attack = 0, defense = 0, rarity = "common", identified = true };
                return new
                {
                    slot = s.ToString(),
                    name = item.IsIdentified ? item.Name : "???",
                    attack = item.WeaponPower,
                    defense = item.ArmorClass,
                    rarity = item.Rarity.ToString().ToLower(),
                    identified = item.IsIdentified,
                };
            }).ToList();

            var backpack = player.Inventory?.Select(item => new
            {
                name = item.Name,
                type = item.Type.ToString(),
                attack = item.Attack,
                defense = item.Armor,
                value = item.Value,
            }).ToList() ?? new();

            ElectronBridge.Emit("inventory", new
            {
                playerName = player.DisplayName,
                level = player.Level,
                className = player.ClassName,
                gold = player.Gold,
                equipment,
                backpack,
                isTwoHanding = player.IsTwoHanding,
                isDualWielding = player.IsDualWielding,
                hasShield = player.HasShieldEquipped
            });
        }

        /// <summary>
        /// Fully interactive Electron inventory — no text rendering, all validation server-side.
        /// Handles EQUIP:{idx}, EQUIP:{idx}:{slot}, UNEQUIP:{slot}, DROP:{idx}, Q commands.
        /// </summary>
        private async Task RunElectronInventory()
        {
            while (true)
            {
                EmitInventoryEvent();
                var input = (await terminal.GetInput("")).Trim().ToUpper();

                if (input == "Q" || input == "") return;

                string resultMessage = "";
                string resultType = "info"; // info, success, error

                try
                {
                    if (input.StartsWith("EQUIP:"))
                    {
                        var parts = input.Substring(6).Split(':');
                        if (int.TryParse(parts[0], out var idx) && idx >= 0 && idx < player.Inventory.Count)
                        {
                            var item = player.Inventory[idx];

                            // Check for non-equippable types
                            bool isMagicEquipment = item.Type == ObjType.Magic &&
                                ((int)item.MagicType == 5 || (int)item.MagicType == 9 || (int)item.MagicType == 10);
                            if (item.Type == ObjType.Food || item.Type == ObjType.Drink ||
                                item.Type == ObjType.Potion || (item.Type == ObjType.Magic && !isMagicEquipment))
                            {
                                resultMessage = "Cannot equip this item type";
                                resultType = "error";
                            }
                            else
                            {
                                // Determine target slot
                                EquipmentSlot targetSlot;
                                if (parts.Length > 1 && Enum.TryParse<EquipmentSlot>(parts[1], out var explicitSlot))
                                    targetSlot = explicitSlot;
                                else
                                    targetSlot = InferSlotFromItem(item);

                                if (targetSlot == EquipmentSlot.None)
                                {
                                    resultMessage = "Cannot determine equipment slot";
                                    resultType = "error";
                                }
                                else
                                {
                                    // Convert Item → Equipment with full metadata
                                    GetHandedness(item, out var handedness, out var weaponType);
                                    var equipment = new Equipment
                                    {
                                        Name = item.Name,
                                        Slot = targetSlot,
                                        Handedness = handedness,
                                        WeaponType = weaponType,
                                        WeaponPower = item.Attack,
                                        ArmorClass = item.Armor,
                                        ShieldBonus = item.Type == ObjType.Shield ? item.Armor : 0,
                                        BlockChance = item.BlockChance,
                                        DefenceBonus = item.Defence,
                                        StrengthBonus = item.Strength,
                                        DexterityBonus = item.Dexterity,
                                        AgilityBonus = item.Agility,
                                        WisdomBonus = item.Wisdom,
                                        CharismaBonus = item.Charisma,
                                        MaxHPBonus = item.HP,
                                        MaxManaBonus = item.Mana,
                                        Value = item.Value,
                                        IsCursed = item.IsCursed,
                                        MinLevel = item.MinLevel,
                                        Rarity = EquipmentRarity.Common
                                    };

                                    // Transfer CON/INT from LootEffects
                                    if (item.LootEffects != null)
                                    {
                                        foreach (var (effectType, value) in item.LootEffects)
                                        {
                                            var effect = (LootGenerator.SpecialEffect)effectType;
                                            switch (effect)
                                            {
                                                case LootGenerator.SpecialEffect.Constitution: equipment.ConstitutionBonus += value; break;
                                                case LootGenerator.SpecialEffect.Intelligence: equipment.IntelligenceBonus += value; break;
                                                case LootGenerator.SpecialEffect.AllStats:
                                                    equipment.ConstitutionBonus += value;
                                                    equipment.IntelligenceBonus += value;
                                                    equipment.CharismaBonus += value;
                                                    break;
                                                case LootGenerator.SpecialEffect.BossSlayer:
                                                    equipment.HasBossSlayer = true;
                                                    break;
                                                case LootGenerator.SpecialEffect.TitanResolve:
                                                    equipment.HasTitanResolve = true;
                                                    break;
                                            }
                                        }
                                    }

                                    // Check if this needs slot selection (1H weapon or ring)
                                    bool needsSlotPick = parts.Length <= 1 &&
                                        ((item.Type == ObjType.Fingers || (int)item.MagicType == 5) ||
                                         (item.Type == ObjType.Weapon && handedness == WeaponHandedness.OneHanded));

                                    if (needsSlotPick)
                                    {
                                        // Emit slot picker request — JS will re-send with explicit slot
                                        var options = new List<object>();
                                        if (item.Type == ObjType.Fingers || (int)item.MagicType == 5)
                                        {
                                            options.Add(new { slot = "LFinger", label = "Left Finger", current = player.GetEquipment(EquipmentSlot.LFinger)?.Name ?? "(empty)" });
                                            options.Add(new { slot = "RFinger", label = "Right Finger", current = player.GetEquipment(EquipmentSlot.RFinger)?.Name ?? "(empty)" });
                                        }
                                        else
                                        {
                                            options.Add(new { slot = "MainHand", label = "Main Hand", current = player.GetEquipment(EquipmentSlot.MainHand)?.Name ?? "(empty)" });
                                            options.Add(new { slot = "OffHand", label = "Off Hand", current = player.GetEquipment(EquipmentSlot.OffHand)?.Name ?? "(empty)" });
                                        }
                                        ElectronBridge.Emit("inventory_slot_pick", new
                                        {
                                            itemIndex = idx,
                                            itemName = item.Name,
                                            options
                                        });
                                        continue; // Wait for next input (EQUIP:idx:SlotName)
                                    }

                                    // Validate with CanEquip
                                    EquipmentDatabase.RegisterDynamic(equipment);
                                    if (!equipment.CanEquip(player, out string reason))
                                    {
                                        resultMessage = reason;
                                        resultType = "error";
                                    }
                                    else if (player.EquipItem(equipment, targetSlot, out string equipMsg))
                                    {
                                        // Find and remove original Item
                                        int removeIdx = player.Inventory.IndexOf(item);
                                        if (removeIdx >= 0)
                                            player.Inventory.RemoveAt(removeIdx);
                                        player.RecalculateStats();
                                        resultMessage = $"Equipped {item.Name}" + (equipMsg != "" ? $" ({equipMsg})" : "");
                                        resultType = "success";
                                    }
                                    else
                                    {
                                        resultMessage = equipMsg != "" ? equipMsg : "Cannot equip this item";
                                        resultType = "error";
                                    }
                                }
                            }
                        }
                    }
                    else if (input.StartsWith("UNEQUIP:"))
                    {
                        var slotName = input.Substring(8);
                        if (Enum.TryParse<EquipmentSlot>(slotName, out var slot))
                        {
                            var equipped = player.GetEquipment(slot);
                            if (equipped == null)
                            {
                                // Slot might have an ID that's not in the database — force clear it
                                if (player.EquippedItems.TryGetValue(slot, out var rawId) && rawId != 0)
                                {
                                    player.EquippedItems.Remove(slot);
                                    player.RecalculateStats();
                                    resultMessage = "Slot cleared";
                                    resultType = "success";
                                }
                                else
                                {
                                    resultMessage = "Nothing equipped in that slot";
                                    resultType = "error";
                                }
                            }
                            else if (equipped.IsCursed)
                            {
                                resultMessage = "Cannot remove cursed item!";
                                resultType = "error";
                            }
                            else
                            {
                                var unequipped = player.UnequipSlot(slot);
                                if (unequipped != null)
                                {
                                    var legacyItem = player.ConvertEquipmentToLegacyItem(unequipped);
                                    player.Inventory.Add(legacyItem);
                                    player.RecalculateStats();
                                    resultMessage = $"Unequipped {unequipped.Name}";
                                    resultType = "success";
                                }
                                else
                                {
                                    // UnequipSlot cleared the slot but couldn't find the Equipment
                                    player.RecalculateStats();
                                    resultMessage = "Slot cleared (item data lost)";
                                    resultType = "success";
                                }
                            }
                        }
                    }
                    else if (input.StartsWith("DROP:"))
                    {
                        if (int.TryParse(input.Substring(5), out var idx) && idx >= 0 && idx < player.Inventory.Count)
                        {
                            var item = player.Inventory[idx];
                            if (item.IsCursed)
                            {
                                resultMessage = "Cannot drop cursed item!";
                                resultType = "error";
                            }
                            else
                            {
                                player.Inventory.RemoveAt(idx);
                                resultMessage = $"Dropped {item.Name}";
                                resultType = "success";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    resultMessage = "Error: " + ex.Message;
                    resultType = "error";
                    DebugLogger.Instance?.Log(DebugLogger.LogLevel.Error, "INVENTORY", $"Electron inventory error: {ex}");
                }

                // Send result message to JS
                if (!string.IsNullOrEmpty(resultMessage))
                {
                    ElectronBridge.Emit("inventory_result", new { message = resultMessage, type = resultType });
                }
            }
        }

        private void DisplayInventoryHeader()
        {
            UIHelper.WriteBoxHeader(terminal, Loc.Get("inventory.title"), "bright_cyan");
            terminal.WriteLine("");

            // Show weapon configuration
            terminal.SetColor("yellow");
            terminal.Write($"{Loc.Get("inventory.combat_style")}: ");
            terminal.SetColor("bright_white");
            if (player.IsTwoHanding)
                terminal.WriteLine(Loc.Get("inventory.style_two_handed"));
            else if (player.IsDualWielding)
                terminal.WriteLine(Loc.Get("inventory.style_dual_wield"));
            else if (player.HasShieldEquipped)
                terminal.WriteLine(Loc.Get("inventory.style_sword_board"));
            else
                terminal.WriteLine(Loc.Get("inventory.style_one_handed"));
            terminal.WriteLine("");
        }

        private void DisplayEquipmentOverview()
        {
            terminal.SetColor("yellow");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine($"═══ {Loc.Get("inventory.equipped_items")} ═══");
            else
                terminal.WriteLine(Loc.Get("inventory.equipped_items"));
            terminal.WriteLine("");

            // Weapons section
            terminal.SetColor("bright_red");
            terminal.WriteLine($"[ {Loc.Get("inventory.section_weapons")} ]");
            DisplaySlot(Loc.Get("inventory.slot_main_hand"), EquipmentSlot.MainHand, "1");
            DisplaySlot(Loc.Get("inventory.slot_off_hand"), EquipmentSlot.OffHand, "2");
            terminal.WriteLine("");

            // Armor section
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"[ {Loc.Get("inventory.section_armor")} ]");
            DisplaySlot(Loc.Get("inventory.slot_head"), EquipmentSlot.Head, "3");
            DisplaySlot(Loc.Get("inventory.slot_body"), EquipmentSlot.Body, "4");
            DisplaySlot(Loc.Get("inventory.slot_arms"), EquipmentSlot.Arms, "5");
            DisplaySlot(Loc.Get("inventory.slot_hands"), EquipmentSlot.Hands, "6");
            DisplaySlot(Loc.Get("inventory.slot_legs"), EquipmentSlot.Legs, "7");
            DisplaySlot(Loc.Get("inventory.slot_feet"), EquipmentSlot.Feet, "8");
            DisplaySlot(Loc.Get("inventory.slot_waist"), EquipmentSlot.Waist, "9");
            DisplaySlot(Loc.Get("inventory.slot_face"), EquipmentSlot.Face, "F");
            DisplaySlot(Loc.Get("inventory.slot_cloak"), EquipmentSlot.Cloak, "C");
            terminal.WriteLine("");

            // Accessories section
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"[ {Loc.Get("inventory.section_accessories")} ]");
            DisplaySlot(Loc.Get("inventory.slot_neck"), EquipmentSlot.Neck, "N");
            DisplaySlot(Loc.Get("inventory.slot_left_ring"), EquipmentSlot.LFinger, "L");
            DisplaySlot(Loc.Get("inventory.slot_right_ring"), EquipmentSlot.RFinger, "R");
            terminal.WriteLine("");

            // Stats summary
            DisplayStatsSummary();

            // Backpack (unequipped items)
            DisplayBackpack();
        }

        private List<Item> FilterInventoryBySlot(EquipmentSlot slot)
        {
            if (this.currFilteredSlot == slot && this.filteredInventory != null)
            {
                return this.filteredInventory;
            }

            if (this.currFilteredSlot != slot)
            {
                _filteredBackpackPage = 0;
            }

            this.filteredInventory = [];
            this.filteredInventoryMap = new Dictionary<int, int>();
            this.currFilteredSlot = slot;

            List<ObjType> types = slot switch
            {
                EquipmentSlot.Head => [ObjType.Head],
                EquipmentSlot.Body => [ObjType.Body],
                EquipmentSlot.Arms => [ObjType.Arms],
                EquipmentSlot.Hands => [ObjType.Hands],
                EquipmentSlot.LFinger => [ObjType.Fingers, ObjType.Magic],
                EquipmentSlot.RFinger => [ObjType.Fingers, ObjType.Magic],
                EquipmentSlot.Legs => [ObjType.Legs],
                EquipmentSlot.Waist => [ObjType.Waist, ObjType.Magic],
                EquipmentSlot.Neck => [ObjType.Neck, ObjType.Magic],
                EquipmentSlot.Face => [ObjType.Face],
                EquipmentSlot.MainHand => [ObjType.Weapon, ObjType.Shield],
                EquipmentSlot.OffHand => [ObjType.Weapon, ObjType.Shield],
                EquipmentSlot.Cloak => [ObjType.Abody],
                _ => [ObjType.Weapon] // Default
            };

            for (int i = 0; i < player.Inventory.Count; i++)
            {
                Item item = player.Inventory[i];
                ObjType actualType = item.Type;
                bool isItemOfType = false;

                if (item.Type != ObjType.Magic)
                {
                    isItemOfType = types.Exists((ObjType type) => item.Type == type);

                    if (isItemOfType && (item.Type == ObjType.Weapon || item.Type == ObjType.Shield))
                    {
                        WeaponHandedness handedness;
                        WeaponType weaponType;
                        GetHandedness(item, out handedness, out weaponType);
                        bool showTwoHanded = handedness == WeaponHandedness.TwoHanded && slot == EquipmentSlot.MainHand;
                        bool showOneHanded = handedness == WeaponHandedness.OneHanded || (handedness == WeaponHandedness.OffHandOnly && slot == EquipmentSlot.OffHand);
                        isItemOfType = slot == EquipmentSlot.MainHand ? (showTwoHanded || showOneHanded) : showOneHanded;
                    }
                }
                else
                {
                    // Check if this magic item is equippable based on MagicType
                    // Cast to int to avoid namespace conflicts between UsurperRemake.MagicItemType and global::MagicItemType
                    MagicItemType neededMagicType = slot switch
                    {
                        EquipmentSlot.LFinger => MagicItemType.Fingers,
                        EquipmentSlot.RFinger => MagicItemType.Fingers,
                        EquipmentSlot.Waist => MagicItemType.Waist,
                        EquipmentSlot.Neck => MagicItemType.Neck
                    };

                    // Only equippable if it has a valid MagicType
                    isItemOfType = item.MagicType == neededMagicType;
                }

                if (isItemOfType)
                {
                    filteredInventory.Add(item);
                    int currInventoryIndex = this.filteredInventoryMap.Count;
                    this.filteredInventoryMap.Add(currInventoryIndex + 1, i + 1);
                }
            }

            return filteredInventory;
        }

        private void DisplayBackpack(EquipmentSlot? slot = null)
        {
            if (player.Inventory == null || player.Inventory.Count == 0)
            {
                terminal.SetColor("yellow");
                if (!GameConfig.ScreenReaderMode)
                    terminal.WriteLine($"═══ {Loc.Get("inventory.backpack")} ═══");
                else
                    terminal.WriteLine(Loc.Get("inventory.backpack"));
                terminal.SetColor("darkgray");
                terminal.WriteLine($"  ({Loc.Get("inventory.backpack_empty")})");
                terminal.WriteLine("");
                return;
            }

            List<Item> inventory = slot != null ? FilterInventoryBySlot((EquipmentSlot)slot) : player.Inventory;

            int totalItems = inventory.Count;
            int totalPages = (totalItems + BackpackPageSize - 1) / BackpackPageSize;
            int backpackPage = 0;

            if (slot == null)
            {
                backpackPage = _backpackPage;
                _backpackPage = totalPages > 0 ? Math.Clamp(_backpackPage, 0, totalPages - 1) : 0;
            } else
            {
                backpackPage = _filteredBackpackPage;
                _filteredBackpackPage = totalPages > 0 ? Math.Clamp(_filteredBackpackPage, 0, totalPages - 1) : 0;
            }

            int startIndex = backpackPage * BackpackPageSize;
            int endIndex = Math.Min(startIndex + BackpackPageSize, totalItems);

            terminal.SetColor("yellow");
            string pageInfo = totalPages > 1 ? $" ({backpackPage + 1}/{totalPages})" : "";
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine($"═══ {Loc.Get("inventory.backpack")}{pageInfo} ═══");
            else
                terminal.WriteLine($"{Loc.Get("inventory.backpack")}{pageInfo}");

            terminal.WriteLine("");
            for (int i = startIndex; i < endIndex; i++)
            {
                var item = inventory[i];
                int displayNum = i + 1;
                terminal.SetColor("gray");
                terminal.Write($"  [B{displayNum}] ");

                if (item.IsIdentified)
                {
                    string itemColor = item.Type switch
                    {
                        ObjType.Weapon => "bright_yellow",
                        ObjType.Body or ObjType.Head or ObjType.Arms or ObjType.Legs => "bright_cyan",
                        ObjType.Shield => "cyan",
                        ObjType.Fingers or ObjType.Neck => "bright_magenta",
                        _ => "white"
                    };
                    terminal.SetColor(itemColor);
                    terminal.Write(item.Name);

                    terminal.SetColor("gray");
                    terminal.Write($" - {item.Value:N0}g");

                    var stats = new List<string>();
                    if (item.Attack > 0) stats.Add($"{Loc.Get("ui.stat_wp")}:{item.Attack}");
                    if (item.Armor > 0) stats.Add($"{Loc.Get("ui.stat_ac")}:{item.Armor}");
                    if (item.Defence > 0) stats.Add($"{Loc.Get("ui.stat_def")}:{item.Defence}");
                    if (item.Strength != 0) stats.Add($"{Loc.Get("ui.stat_str")}:{item.Strength:+#;-#;0}");
                    if (item.Dexterity != 0) stats.Add($"{Loc.Get("ui.stat_dex")}:{item.Dexterity:+#;-#;0}");
                    if (item.Wisdom != 0) stats.Add($"{Loc.Get("ui.stat_wis")}:{item.Wisdom:+#;-#;0}");
                    if (item.Agility != 0) stats.Add($"{Loc.Get("ui.stat_agi")}:{item.Agility:+#;-#;0}");
                    if (item.Charisma != 0) stats.Add($"{Loc.Get("ui.stat_cha")}:{item.Charisma:+#;-#;0}");
                    int conFromLoot = item.LootEffects?.Where(e => e.EffectType == (int)LootGenerator.SpecialEffect.Constitution).Sum(e => e.Value) ?? 0;
                    int intFromLoot = item.LootEffects?.Where(e => e.EffectType == (int)LootGenerator.SpecialEffect.Intelligence).Sum(e => e.Value) ?? 0;
                    if (conFromLoot != 0) stats.Add($"{Loc.Get("ui.stat_con")}:{conFromLoot:+#;-#;0}");
                    if (intFromLoot != 0) stats.Add($"{Loc.Get("ui.stat_int")}:{intFromLoot:+#;-#;0}");

                    if (stats.Count > 0)
                    {
                        terminal.SetColor("darkgray");
                        terminal.Write($" ({string.Join(", ", stats.Take(4))})");
                    }
                }
                else
                {
                    terminal.SetColor("magenta");
                    terminal.Write(LootGenerator.GetUnidentifiedName(item));
                    terminal.SetColor("darkgray");
                    terminal.Write(" - ???");
                }

                terminal.WriteLine("");
            }

            if (totalPages > 1)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine("");
                terminal.Write("  ");
                if (backpackPage > 0)
                    terminal.Write($"[<] {Loc.Get("inventory.prev_page")}  ");
                if (backpackPage < totalPages - 1)
                    terminal.Write($"[>] {Loc.Get("inventory.next_page")}");
                terminal.WriteLine("");
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

                // Show armor weight class tag
                if (item.WeightClass != ArmorWeightClass.None && slot.IsArmorSlot())
                {
                    terminal.SetColor(item.WeightClass.GetWeightColor());
                    terminal.Write($" [{item.WeightClass}]");
                }
                terminal.WriteLine("");
            }
            else
            {
                // Check if off-hand is empty because of a two-handed weapon
                if (slot == EquipmentSlot.OffHand)
                {
                    var mainHand = player.GetEquipment(EquipmentSlot.MainHand);
                    if (mainHand?.Handedness == WeaponHandedness.TwoHanded)
                    {
                        terminal.SetColor("darkgray");
                        terminal.WriteLine(Loc.Get("inventory.using_2h"));
                        return;
                    }
                }
                terminal.SetColor("darkgray");
                terminal.WriteLine(Loc.Get("ui.empty"));
            }
        }

        private string GetRarityColor(EquipmentRarity rarity)
        {
            return rarity switch
            {
                EquipmentRarity.Common => "white",
                EquipmentRarity.Uncommon => "green",
                EquipmentRarity.Rare => "cyan",
                EquipmentRarity.Epic => "magenta",
                EquipmentRarity.Legendary => "yellow",
                EquipmentRarity.Artifact => "bright_yellow",
                _ => "white"
            };
        }

        private string GetItemStatSummary(Equipment item)
        {
            var stats = new List<string>();

            if (item.WeaponPower > 0) stats.Add($"{Loc.Get("ui.stat_wp")}:{item.WeaponPower}");
            if (item.ArmorClass > 0) stats.Add($"{Loc.Get("ui.stat_ac")}:{item.ArmorClass}");
            if (item.ShieldBonus > 0) stats.Add($"{Loc.Get("ui.stat_block")}:{item.ShieldBonus}");
            if (item.StrengthBonus != 0) stats.Add($"{Loc.Get("ui.stat_str")}:{item.StrengthBonus:+#;-#;0}");
            if (item.DexterityBonus != 0) stats.Add($"{Loc.Get("ui.stat_dex")}:{item.DexterityBonus:+#;-#;0}");
            if (item.ConstitutionBonus != 0) stats.Add($"{Loc.Get("ui.stat_con")}:{item.ConstitutionBonus:+#;-#;0}");
            if (item.IntelligenceBonus != 0) stats.Add($"{Loc.Get("ui.stat_int")}:{item.IntelligenceBonus:+#;-#;0}");
            if (item.WisdomBonus != 0) stats.Add($"{Loc.Get("ui.stat_wis")}:{item.WisdomBonus:+#;-#;0}");
            if (item.CharismaBonus != 0) stats.Add($"{Loc.Get("ui.stat_cha")}:{item.CharismaBonus:+#;-#;0}");
            if (item.AgilityBonus != 0) stats.Add($"{Loc.Get("ui.stat_agi")}:{item.AgilityBonus:+#;-#;0}");
            if (item.MaxHPBonus != 0) stats.Add($"{Loc.Get("ui.stat_hp")}:{item.MaxHPBonus:+#;-#;0}");
            if (item.MaxManaBonus != 0) stats.Add($"{Loc.Get("ui.stat_mp")}:{item.MaxManaBonus:+#;-#;0}");
            if (item.DefenceBonus != 0) stats.Add($"{Loc.Get("ui.stat_def")}:{item.DefenceBonus:+#;-#;0}");
            if (item.StaminaBonus != 0) stats.Add($"{Loc.Get("ui.stat_sta")}:{item.StaminaBonus:+#;-#;0}");
            if (item.MagicResistance != 0) stats.Add($"{Loc.Get("ui.stat_mr")}:{item.MagicResistance:+#;-#;0}");
            if (item.CriticalChanceBonus != 0) stats.Add($"{Loc.Get("ui.stat_crit")}:{item.CriticalChanceBonus}%");
            if (item.LifeSteal != 0) stats.Add($"{Loc.Get("ui.stat_ls")}:{item.LifeSteal}%");

            return string.Join(", ", stats.Take(4)); // Limit to 4 stats for display
        }

        private void DisplayStatsSummary()
        {
            terminal.SetColor("yellow");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine($"═══ {Loc.Get("inventory.equipment_bonuses")} ═══");
            else
                terminal.WriteLine(Loc.Get("inventory.equipment_bonuses"));

            // Calculate total bonuses from equipment
            int totalWeapPow = 0, totalArmPow = 0;
            int totalStr = 0, totalDex = 0, totalAgi = 0, totalCon = 0, totalInt = 0, totalWis = 0, totalCha = 0;
            int totalMaxHP = 0, totalMaxMana = 0, totalMR = 0, totalDef = 0, totalSta = 0;

            foreach (var slot in Enum.GetValues<EquipmentSlot>())
            {
                var item = player.GetEquipment(slot);
                if (item != null)
                {
                    totalWeapPow += item.WeaponPower;
                    totalArmPow += item.ArmorClass + item.ShieldBonus;
                    totalStr += item.StrengthBonus;
                    totalDex += item.DexterityBonus;
                    totalAgi += item.AgilityBonus;
                    totalCon += item.ConstitutionBonus;
                    totalInt += item.IntelligenceBonus;
                    totalWis += item.WisdomBonus;
                    totalCha += item.CharismaBonus;
                    totalMaxHP += item.MaxHPBonus;
                    totalMaxMana += item.MaxManaBonus;
                    totalMR += item.MagicResistance;
                    totalDef += item.DefenceBonus;
                    totalSta += item.StaminaBonus;
                }
            }

            terminal.SetColor("white");
            terminal.Write($"{Loc.Get("ui.weapon_power")}: ");
            terminal.SetColor("bright_red");
            terminal.Write($"{totalWeapPow}");
            terminal.SetColor("white");
            terminal.Write($"  |  {Loc.Get("ui.armor_class")}: ");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"{totalArmPow}");

            // Stat bonuses
            if (totalStr != 0 || totalDex != 0 || totalAgi != 0 || totalCon != 0 ||
                totalInt != 0 || totalWis != 0 || totalCha != 0)
            {
                terminal.SetColor("white");
                terminal.Write($"{Loc.Get("inventory.stats")}: ");
                if (totalStr != 0) { terminal.SetColor("green"); terminal.Write($"Str {totalStr:+#;-#;0}  "); }
                if (totalDex != 0) { terminal.SetColor("green"); terminal.Write($"Dex {totalDex:+#;-#;0}  "); }
                if (totalAgi != 0) { terminal.SetColor("green"); terminal.Write($"Agi {totalAgi:+#;-#;0}  "); }
                if (totalCon != 0) { terminal.SetColor("green"); terminal.Write($"Con {totalCon:+#;-#;0}  "); }
                if (totalInt != 0) { terminal.SetColor("cyan"); terminal.Write($"Int {totalInt:+#;-#;0}  "); }
                if (totalWis != 0) { terminal.SetColor("cyan"); terminal.Write($"Wis {totalWis:+#;-#;0}  "); }
                if (totalCha != 0) { terminal.SetColor("cyan"); terminal.Write($"Cha {totalCha:+#;-#;0}  "); }
                terminal.WriteLine("");
            }

            if (totalMaxHP != 0 || totalMaxMana != 0 || totalMR != 0 || totalDef != 0 || totalSta != 0)
            {
                terminal.SetColor("white");
                terminal.Write($"{Loc.Get("inventory.other")}: ");
                if (totalMaxHP != 0) { terminal.SetColor("red"); terminal.Write($"MaxHP {totalMaxHP:+#;-#;0}  "); }
                if (totalMaxMana != 0) { terminal.SetColor("blue"); terminal.Write($"MaxMP {totalMaxMana:+#;-#;0}  "); }
                if (totalMR != 0) { terminal.SetColor("magenta"); terminal.Write($"MagicRes {totalMR:+#;-#;0}  "); }
                if (totalDef != 0) { terminal.SetColor("cyan"); terminal.Write($"Def {totalDef:+#;-#;0}  "); }
                if (totalSta != 0) { terminal.SetColor("yellow"); terminal.Write($"Sta {totalSta:+#;-#;0}  "); }
                terminal.WriteLine("");
            }

            terminal.WriteLine("");
        }

        private void DisplayInventoryMenu()
        {
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("────────────────────────────────────────────────────────────────────────────────");
            }
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inventory.options_line1"));
            terminal.WriteLine(Loc.Get("inventory.options_line2"));
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
                case "<":
                case ",":
                    if (_backpackPage > 0) _backpackPage--;
                    break;
                case ">":
                case ".":
                    {
                        int totalPages = player.Inventory != null
                            ? (player.Inventory.Count + BackpackPageSize - 1) / BackpackPageSize
                            : 1;
                        if (_backpackPage < totalPages - 1) _backpackPage++;
                    }
                    break;
                default:
                    // Check for B# format (backpack item)
                    if (choice.StartsWith("B") && int.TryParse(choice.Substring(1), out int backpackIndex))
                    {
                        await ManageBackpackItem(backpackIndex);
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("inventory.invalid_choice"), "red");
                        await Task.Delay(500);
                    }
                    break;
            }

            return false;
        }

        private async Task ManageBackpackItem(int index, EquipmentSlot? slot = null)
        {
            if (player.Inventory == null || index < 1 || index > player.Inventory.Count)
            {
                terminal.WriteLine(Loc.Get("inventory.invalid_item"), "red");
                await Task.Delay(500);
                return;
            }

            var item = player.Inventory[index - 1];

            terminal.ClearScreen();
            UIHelper.WriteBoxHeader(terminal, Loc.Get("inventory.manage_item"), "bright_cyan");
            terminal.WriteLine("");

            if (item.IsIdentified)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {item.Name}");
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("inventory.value")}: {item.Value:N0} {Loc.Get("ui.gold_word")}");
                terminal.WriteLine($"  {Loc.Get("inventory.type")}: {item.Type}");
                terminal.WriteLine("");

                // Show stats
                var stats = new List<string>();
                if (item.Attack > 0) stats.Add($"{Loc.Get("ui.weapon_power")}: +{item.Attack}");
                if (item.Armor > 0) stats.Add($"{Loc.Get("ui.armor_class")}: +{item.Armor}");
                if (item.Defence > 0) stats.Add($"{Loc.Get("ui.stat_defense")}: +{item.Defence}");
                if (item.Strength != 0) stats.Add($"{Loc.Get("ui.stat_strength")}: {item.Strength:+#;-#;0}");
                if (item.Dexterity != 0) stats.Add($"{Loc.Get("ui.stat_dexterity")}: {item.Dexterity:+#;-#;0}");
                if (item.Wisdom != 0) stats.Add($"{Loc.Get("ui.stat_wisdom")}: {item.Wisdom:+#;-#;0}");
                if (item.Agility != 0) stats.Add($"{Loc.Get("ui.stat_agi")}: {item.Agility:+#;-#;0}");
                if (item.Charisma != 0) stats.Add($"{Loc.Get("ui.stat_cha")}: {item.Charisma:+#;-#;0}");
                // CON and INT are stored in LootEffects, not as direct Item properties
                int detailCon = item.LootEffects?.Where(e => e.EffectType == (int)LootGenerator.SpecialEffect.Constitution).Sum(e => e.Value) ?? 0;
                int detailInt = item.LootEffects?.Where(e => e.EffectType == (int)LootGenerator.SpecialEffect.Intelligence).Sum(e => e.Value) ?? 0;
                if (detailCon != 0) stats.Add($"{Loc.Get("ui.stat_con")}: {detailCon:+#;-#;0}");
                if (detailInt != 0) stats.Add($"{Loc.Get("ui.stat_int")}: {detailInt:+#;-#;0}");
                if (item.HP != 0) stats.Add($"{Loc.Get("ui.stat_hp")}: {item.HP:+#;-#;0}");
                if (item.MagicProperties?.Mana != 0) stats.Add($"{Loc.Get("ui.stat_mana")}: {item.MagicProperties.Mana:+#;-#;0}");

                if (stats.Count > 0)
                {
                    terminal.SetColor("white");
                    foreach (var stat in stats)
                    {
                        terminal.WriteLine($"  {stat}");
                    }
                    terminal.WriteLine("");
                }
            }
            else
            {
                terminal.SetColor("magenta");
                terminal.WriteLine($"  {LootGenerator.GetUnidentifiedName(item)}");
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("inventory.properties_unknown")}");
                terminal.WriteLine("");
            }

            terminal.SetColor("white");
            if (item.IsIdentified)
            {
                terminal.WriteLine($"  [E] {Loc.Get("inventory.equip_item")}");
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  [E] {Loc.Get("inventory.equip_identify_first")}");
                terminal.SetColor("white");
            }
            terminal.WriteLine($"  [D] {Loc.Get("inventory.drop_item")}");
            terminal.WriteLine($"  [Q] {Loc.Get("inventory.back")}");
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("ui.choice"));

            switch (choice.ToUpper())
            {
                case "E":
                    if (!item.IsIdentified)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("inventory.cant_equip_unidentified"));
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("inventory.visit_magic_shop"));
                        await Task.Delay(2000);
                        break;
                    }
                    await EquipFromBackpack(index - 1, slot);
                    break;
                case "D":
                    if (item.IsCursed)
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("inventory.cursed_cant_drop", item.Name));
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("inventory.visit_healer_curse"));
                        await Task.Delay(2000);
                    }
                    else
                    {
                        string dropName = item.IsIdentified ? item.Name : LootGenerator.GetUnidentifiedName(item);
                        player.Inventory.Remove(item);
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("inventory.dropped_item", dropName));
                        await Task.Delay(1000);
                    }
                    break;
            }

            // Inventory may have changed — invalidate filtered cache
            InvalidateFilteredCache();
        }

        private async Task EquipFromBackpack(int itemIndex, EquipmentSlot? slot = null)
        {
            if (player.Inventory == null || itemIndex < 0 || itemIndex >= player.Inventory.Count)
            {
                terminal.WriteLine(Loc.Get("inventory.invalid_item"), "red");
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
                terminal.WriteLine(Loc.Get("inventory.cant_equip"), "red");
                await Task.Delay(1000);
                return;
            }

            WeaponHandedness handedness;
            WeaponType weaponType;
            GetHandedness(item, out handedness, out weaponType);

            // For rings, ask which finger
            if (slot == null && (item.Type == ObjType.Fingers || (int)item.MagicType == 5))
            {
                terminal.WriteLine("");
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("inventory.which_finger"));
                terminal.SetColor("white");
                terminal.WriteLine($"  (L) {Loc.Get("inventory.left_finger")}");
                terminal.WriteLine($"  (R) {Loc.Get("inventory.right_finger")}");
                terminal.WriteLine($"  (C) {Loc.Get("ui.cancel")}");
                terminal.Write(Loc.Get("ui.choice"));
                var fingerChoice = await terminal.GetInput("");
                if (fingerChoice.ToUpper() == "R")
                    targetSlot = EquipmentSlot.RFinger;
                else if (fingerChoice.ToUpper() == "L")
                    targetSlot = EquipmentSlot.LFinger;
                else
                {
                    terminal.WriteLine(Loc.Get("ui.cancelled"), "gray");
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
                WeaponType = weaponType,
                WeaponPower = item.Attack,
                ArmorClass = item.Armor,
                ShieldBonus = item.Type == ObjType.Shield ? item.Armor : 0,
                DefenceBonus = item.Defence,
                StrengthBonus = item.Strength,
                DexterityBonus = item.Dexterity,
                AgilityBonus = item.Agility,
                WisdomBonus = item.Wisdom,
                CharismaBonus = item.Charisma,
                MaxHPBonus = item.HP,
                MaxManaBonus = item.Mana,
                Value = item.Value,
                IsCursed = item.IsCursed,
                MinLevel = item.MinLevel,
                Rarity = EquipmentRarity.Common
            };

            // Transfer CON/INT from LootEffects (these stats are stored there, not as direct Item properties)
            if (item.LootEffects != null)
            {
                foreach (var (effectType, value) in item.LootEffects)
                {
                    var effect = (LootGenerator.SpecialEffect)effectType;
                    switch (effect)
                    {
                        case LootGenerator.SpecialEffect.Constitution: equipment.ConstitutionBonus += value; break;
                        case LootGenerator.SpecialEffect.Intelligence: equipment.IntelligenceBonus += value; break;
                        case LootGenerator.SpecialEffect.AllStats:
                            equipment.ConstitutionBonus += value;
                            equipment.IntelligenceBonus += value;
                            equipment.CharismaBonus += value;
                            break;
                        case LootGenerator.SpecialEffect.BossSlayer:
                            equipment.HasBossSlayer = true;
                            break;
                        case LootGenerator.SpecialEffect.TitanResolve:
                            equipment.HasTitanResolve = true;
                            break;
                    }
                }
            }

            // Register in database to get an ID
            EquipmentDatabase.RegisterDynamic(equipment);

            // For one-handed weapons, ask which slot to use
            EquipmentSlot? finalSlot = null;
            if (slot == null && Character.RequiresSlotSelection(equipment))
            {
                finalSlot = await PromptForWeaponSlot();
                if (finalSlot == null)
                {
                    terminal.WriteLine(Loc.Get("ui.cancelled"), "gray");
                    await Task.Delay(1000);
                    return;
                }
            }

            // Show comparison with currently equipped item
            EquipmentSlot compareSlot = finalSlot ?? targetSlot;
            var currentEquip = player.GetEquipment(compareSlot);

            if (currentEquip != null)
            {
                string slotDisplayName = GetSlotDisplayName(compareSlot);

                terminal.WriteLine("");
                if (!GameConfig.ScreenReaderMode)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("  ─────────────────────────────────────");
                }
                terminal.SetColor("white");
                terminal.WriteLine($"  {Loc.Get("inventory.comparison")} ({slotDisplayName}):");
                terminal.SetColor("cyan");
                terminal.WriteLine($"  {Loc.Get("inventory.currently_equipped")}: {currentEquip.Name}");

                // Compare primary stat
                if (item.Type == ObjType.Weapon)
                {
                    int currentPower = currentEquip.WeaponPower;
                    int newPower = equipment.WeaponPower;
                    int diff = newPower - currentPower;
                    terminal.SetColor("white");
                    terminal.Write($"  Attack: {currentPower} -> {newPower} ");
                    if (diff > 0) { terminal.SetColor("bright_green"); terminal.WriteLine($"(+{diff} UPGRADE)"); }
                    else if (diff < 0) { terminal.SetColor("red"); terminal.WriteLine($"({diff} downgrade)"); }
                    else { terminal.SetColor("yellow"); terminal.WriteLine("(same)"); }
                }
                else if (item.Type == ObjType.Fingers || item.Type == ObjType.Neck ||
                         (item.Type == ObjType.Magic && ((int)item.MagicType == 5 || (int)item.MagicType == 10)))
                {
                    int currentStatTotal = currentEquip.StrengthBonus + currentEquip.DexterityBonus +
                        currentEquip.WisdomBonus + currentEquip.MaxHPBonus + currentEquip.MaxManaBonus +
                        currentEquip.DefenceBonus + currentEquip.AgilityBonus + currentEquip.ConstitutionBonus +
                        currentEquip.IntelligenceBonus + currentEquip.CharismaBonus;
                    int newStatTotal = equipment.StrengthBonus + equipment.DexterityBonus +
                        equipment.WisdomBonus + equipment.MaxHPBonus + equipment.MaxManaBonus +
                        equipment.DefenceBonus + equipment.AgilityBonus + equipment.ConstitutionBonus +
                        equipment.IntelligenceBonus + equipment.CharismaBonus;
                    int diff = newStatTotal - currentStatTotal;
                    terminal.SetColor("white");
                    terminal.Write($"  Stat Total: {currentStatTotal} -> {newStatTotal} ");
                    if (diff > 0) { terminal.SetColor("bright_green"); terminal.WriteLine($"(+{diff} UPGRADE)"); }
                    else if (diff < 0) { terminal.SetColor("red"); terminal.WriteLine($"({diff} downgrade)"); }
                    else { terminal.SetColor("yellow"); terminal.WriteLine("(same)"); }
                }
                else
                {
                    int currentAC = currentEquip.ArmorClass;
                    int newAC = equipment.ArmorClass;
                    int diff = newAC - currentAC;
                    terminal.SetColor("white");
                    terminal.Write($"  Armor: {currentAC} -> {newAC} ");
                    if (diff > 0) { terminal.SetColor("bright_green"); terminal.WriteLine($"(+{diff} UPGRADE)"); }
                    else if (diff < 0) { terminal.SetColor("red"); terminal.WriteLine($"({diff} downgrade)"); }
                    else { terminal.SetColor("yellow"); terminal.WriteLine("(same)"); }
                }

                // Compare bonus stats
                var currentBonuses = new List<string>();
                var newBonuses = new List<string>();
                if (currentEquip.StrengthBonus != 0) currentBonuses.Add($"Str {currentEquip.StrengthBonus:+#;-#;0}");
                if (currentEquip.DexterityBonus != 0) currentBonuses.Add($"Dex {currentEquip.DexterityBonus:+#;-#;0}");
                if (currentEquip.AgilityBonus != 0) currentBonuses.Add($"Agi {currentEquip.AgilityBonus:+#;-#;0}");
                if (currentEquip.ConstitutionBonus != 0) currentBonuses.Add($"Con {currentEquip.ConstitutionBonus:+#;-#;0}");
                if (currentEquip.IntelligenceBonus != 0) currentBonuses.Add($"Int {currentEquip.IntelligenceBonus:+#;-#;0}");
                if (currentEquip.WisdomBonus != 0) currentBonuses.Add($"Wis {currentEquip.WisdomBonus:+#;-#;0}");
                if (currentEquip.CharismaBonus != 0) currentBonuses.Add($"Cha {currentEquip.CharismaBonus:+#;-#;0}");
                if (currentEquip.MaxHPBonus != 0) currentBonuses.Add($"HP {currentEquip.MaxHPBonus:+#;-#;0}");
                if (currentEquip.MaxManaBonus != 0) currentBonuses.Add($"Mana {currentEquip.MaxManaBonus:+#;-#;0}");
                if (currentEquip.DefenceBonus != 0) currentBonuses.Add($"Def {currentEquip.DefenceBonus:+#;-#;0}");
                if (equipment.StrengthBonus != 0) newBonuses.Add($"Str {equipment.StrengthBonus:+#;-#;0}");
                if (equipment.DexterityBonus != 0) newBonuses.Add($"Dex {equipment.DexterityBonus:+#;-#;0}");
                if (equipment.AgilityBonus != 0) newBonuses.Add($"Agi {equipment.AgilityBonus:+#;-#;0}");
                if (equipment.ConstitutionBonus != 0) newBonuses.Add($"Con {equipment.ConstitutionBonus:+#;-#;0}");
                if (equipment.IntelligenceBonus != 0) newBonuses.Add($"Int {equipment.IntelligenceBonus:+#;-#;0}");
                if (equipment.WisdomBonus != 0) newBonuses.Add($"Wis {equipment.WisdomBonus:+#;-#;0}");
                if (equipment.CharismaBonus != 0) newBonuses.Add($"Cha {equipment.CharismaBonus:+#;-#;0}");
                if (equipment.MaxHPBonus != 0) newBonuses.Add($"HP {equipment.MaxHPBonus:+#;-#;0}");
                if (equipment.MaxManaBonus != 0) newBonuses.Add($"Mana {equipment.MaxManaBonus:+#;-#;0}");
                if (equipment.DefenceBonus != 0) newBonuses.Add($"Def {equipment.DefenceBonus:+#;-#;0}");

                if (currentBonuses.Count > 0 || newBonuses.Count > 0)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(currentBonuses.Count > 0
                        ? $"  Current bonuses: {string.Join(", ", currentBonuses)}"
                        : "  Current bonuses: (none)");
                    terminal.WriteLine(newBonuses.Count > 0
                        ? $"  New bonuses: {string.Join(", ", newBonuses)}"
                        : "  New bonuses: (none)");
                }

                if (!GameConfig.ScreenReaderMode)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("  ─────────────────────────────────────");
                }
                terminal.WriteLine("");

                terminal.SetColor("white");
                terminal.Write(Loc.Get("inventory.equip_confirm"));
                var confirm = await terminal.GetInput("");
                if (confirm.ToUpper() != "Y")
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("ui.cancelled"));
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
                terminal.WriteLine(Loc.Get("inventory.equipped", item.Name));
                if (!string.IsNullOrEmpty(message))
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(message);
                }
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("inventory.cannot_equip", message));
            }

            player.RecalculateStats();
            await Task.Delay(1500);
        }

        private static void GetHandedness(Item item, out WeaponHandedness handedness, out WeaponType weaponType)
        {
            // Determine handedness for weapons (default to None for non-weapons like armor)
            handedness = WeaponHandedness.None;
            weaponType = WeaponType.None;
            if (item.Type == ObjType.Weapon)
            {
                // First, look up in the equipment database by name to get correct handedness
                var knownEquip = EquipmentDatabase.GetByName(item.Name);
                if (knownEquip != null && (knownEquip.Handedness == WeaponHandedness.OneHanded || knownEquip.Handedness == WeaponHandedness.TwoHanded))
                {
                    handedness = knownEquip.Handedness;
                    weaponType = knownEquip.WeaponType;
                }
                else
                {
                    // Fallback: guess from name
                    string nameLower = item.Name.ToLower();
                    if (nameLower.Contains("two-hand") || nameLower.Contains("2h") ||
                        nameLower.Contains("greatsword") || nameLower.Contains("greataxe") ||
                        nameLower.Contains("halberd") || nameLower.Contains("pike") ||
                        nameLower.Contains("longbow") || nameLower.Contains("crossbow") ||
                        nameLower.Contains("staff") || nameLower.Contains("quarterstaff") ||
                        nameLower.Contains("maul") || nameLower.Contains("spear") ||
                        nameLower.Contains("glaive") || nameLower.Contains("bardiche") ||
                        nameLower.Contains("lance") || nameLower.Contains("voulge"))
                    {
                        handedness = WeaponHandedness.TwoHanded;
                    }
                    else
                    {
                        handedness = WeaponHandedness.OneHanded;
                    }
                }
            }
            else if (item.Type == ObjType.Shield)
            {
                handedness = WeaponHandedness.OffHandOnly;
            }
        }

        /// <summary>
        /// Prompt player to choose which hand to equip a one-handed weapon in
        /// </summary>
        private async Task<EquipmentSlot?> PromptForWeaponSlot()
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("inventory.one_handed_where"));
            terminal.WriteLine("");

            // Show current equipment in both slots
            var mainHandItem = player.GetEquipment(EquipmentSlot.MainHand);
            var offHandItem = player.GetEquipment(EquipmentSlot.OffHand);

            terminal.SetColor("white");
            terminal.Write($"  (M) {Loc.Get("inventory.slot_main_hand")}: ");
            if (mainHandItem != null)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(mainHandItem.Name);
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("ui.empty"));
            }

            terminal.SetColor("white");
            terminal.Write($"  (O) {Loc.Get("inventory.slot_off_hand")}:  ");
            if (offHandItem != null)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(offHandItem.Name);
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("ui.empty"));
            }

            terminal.SetColor("white");
            terminal.WriteLine($"  (C) {Loc.Get("ui.cancel")}");
            terminal.WriteLine("");

            terminal.Write(Loc.Get("ui.your_choice"));
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
                terminal.WriteLine(Loc.Get("inventory.backpack_empty"), "gray");
                await Task.Delay(1000);
                return;
            }

            terminal.WriteLine(Loc.Get("inventory.enter_drop_number"), "yellow");
            var input = await terminal.GetInput("");

            if (input.ToUpper().StartsWith("B") && int.TryParse(input.Substring(1), out int index))
            {
                if (index >= 1 && index <= player.Inventory.Count)
                {
                    var item = player.Inventory[index - 1];
                    if (item.IsCursed)
                    {
                        terminal.WriteLine(Loc.Get("inventory.cursed_cant_drop", item.Name), "red");
                        terminal.WriteLine(Loc.Get("inventory.visit_healer_curse"), "gray");
                        await Task.Delay(2000);
                        return;
                    }
                    player.Inventory.RemoveAt(index - 1);
                    terminal.WriteLine(Loc.Get("inventory.dropped_item", item.Name), "yellow");
                    await Task.Delay(1000);
                }
                else
                {
                    terminal.WriteLine(Loc.Get("inventory.invalid_item"), "red");
                    await Task.Delay(500);
                }
            }
        }

        private async Task HandleUnequipItem(EquipmentSlot slot)
        {
            var currentItem = player.GetEquipment(slot);

            if (currentItem == null)
            {
                return;
            }

            if (currentItem.IsCursed)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("inventory.cursed_cant_unequip", currentItem.Name));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("inventory.visit_healer_curse"));
                await Task.Delay(2000);
                return;
            }
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
                terminal.WriteLine(Loc.Get("inventory.unequipped", unequipped.Name));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("inventory.returned_backpack"));
                await Task.Delay(1500);
            }
        }

        private async Task ProcessSlotInventoryChoice(EquipmentSlot slot, string choice)
        {
            switch (choice)
            {
                case "U":
                    HandleUnequipItem(slot);
                    InvalidateFilteredCache();
                    break;
                case "<":
                case ",":
                    {
                        if (_filteredBackpackPage > 0) _filteredBackpackPage--;
                        ManageSlot(slot);
                    }
                    break;
                case ">":
                case ".":
                    {
                        int totalPages = filteredInventory != null
                            ? (filteredInventory.Count + BackpackPageSize - 1) / BackpackPageSize
                            : 1;
                        if (_filteredBackpackPage < totalPages - 1) _filteredBackpackPage++;
                        ManageSlot(slot);
                    }
                    break;
                case "Q":
                    return;
                default:
                    // Check for B# format (backpack item)
                    if (choice.StartsWith("B") && int.TryParse(choice.Substring(1), out int backpackIndex))
                    {
                        int index = filteredInventoryMap[backpackIndex];
                        await ManageBackpackItem(index, slot);
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("inventory.invalid_choice"), "red");
                        await Task.Delay(500);
                    }
                    break;
            }
        }

        private async Task ManageSlot(EquipmentSlot slot)
        {
            terminal.ClearScreen();

            var currentItem = player.GetEquipment(slot);
            var slotName = GetSlotDisplayName(slot);

            terminal.SetColor("bright_yellow");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine($"═══ {slotName.ToUpper()} SLOT ═══");
            else
                terminal.WriteLine($"{slotName.ToUpper()} SLOT");
            terminal.WriteLine("");

            // Show current item
            terminal.SetColor("white");
            terminal.Write($"{Loc.Get("inventory.currently_equipped")}: ");
            if (currentItem != null)
            {
                terminal.SetColor(GetRarityColor(currentItem.Rarity));
                terminal.WriteLine(currentItem.Name);
                DisplayItemDetails(currentItem);
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("inventory.nothing"));
            }
            terminal.WriteLine("");

            DisplayBackpack(slot);

            // Show options
            terminal.SetColor("cyan");
            terminal.WriteLine($"{Loc.Get("inventory.options")}:");
            terminal.SetColor("white");
            if (currentItem != null)
            {
                terminal.WriteLine($"  {Loc.Get("inventory.slot.equip")} {Loc.Get("inventory.unequip_item")}");
            } else
            {
                terminal.WriteLine($"  {Loc.Get("inventory.slot.equip")}");
            }
            terminal.WriteLine($"  {Loc.Get("inventory.slot.options_line2")}");
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("ui.choice"));
            await ProcessSlotInventoryChoice(slot, choice.ToUpper().Trim());
        }

        private void DisplayItemDetails(Equipment item)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  Type: {item.Slot}  |  Rarity: {item.Rarity}");

            var stats = new List<string>();

            // Combat stats
            if (item.WeaponPower > 0) stats.Add($"{Loc.Get("ui.weapon_power")}: {item.WeaponPower}");
            if (item.ArmorClass > 0) stats.Add($"{Loc.Get("ui.armor_class")}: {item.ArmorClass}");
            if (item.ShieldBonus > 0) stats.Add($"{Loc.Get("ui.shield_block")}: {item.ShieldBonus}");

            // Attribute bonuses
            if (item.StrengthBonus != 0) stats.Add($"{Loc.Get("ui.stat_strength")}: {item.StrengthBonus:+#;-#;0}");
            if (item.DexterityBonus != 0) stats.Add($"{Loc.Get("ui.stat_dexterity")}: {item.DexterityBonus:+#;-#;0}");
            if (item.ConstitutionBonus != 0) stats.Add($"{Loc.Get("ui.stat_constitution")}: {item.ConstitutionBonus:+#;-#;0}");
            if (item.IntelligenceBonus != 0) stats.Add($"{Loc.Get("ui.stat_intelligence")}: {item.IntelligenceBonus:+#;-#;0}");
            if (item.WisdomBonus != 0) stats.Add($"{Loc.Get("ui.stat_wisdom")}: {item.WisdomBonus:+#;-#;0}");
            if (item.CharismaBonus != 0) stats.Add($"{Loc.Get("ui.stat_charisma")}: {item.CharismaBonus:+#;-#;0}");
            if (item.AgilityBonus != 0) stats.Add($"{Loc.Get("ui.stat_agility")}: {item.AgilityBonus:+#;-#;0}");
            if (item.StaminaBonus != 0) stats.Add($"{Loc.Get("ui.stat_stamina")}: {item.StaminaBonus:+#;-#;0}");

            // Other bonuses
            if (item.MaxHPBonus != 0) stats.Add($"{Loc.Get("ui.max_hp")}: {item.MaxHPBonus:+#;-#;0}");
            if (item.MaxManaBonus != 0) stats.Add($"{Loc.Get("ui.max_mana")}: {item.MaxManaBonus:+#;-#;0}");
            if (item.DefenceBonus != 0) stats.Add($"{Loc.Get("ui.stat_defense")}: {item.DefenceBonus:+#;-#;0}");
            if (item.MagicResistance != 0) stats.Add($"{Loc.Get("ui.magic_resist")}: {item.MagicResistance:+#;-#;0}");
            if (item.CriticalChanceBonus != 0) stats.Add($"{Loc.Get("ui.crit_chance")}: {item.CriticalChanceBonus}%");
            if (item.CriticalDamageBonus != 0) stats.Add($"{Loc.Get("ui.crit_damage")}: {item.CriticalDamageBonus}%");
            if (item.LifeSteal != 0) stats.Add($"{Loc.Get("ui.life_steal")}: {item.LifeSteal}%");

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
            if (item.MinLevel > 1) reqs.Add($"{Loc.Get("inventory.req_level")} {item.MinLevel}");
            if (item.StrengthRequired > 0) reqs.Add($"{Loc.Get("ui.stat_str")} {item.StrengthRequired}");
            if (item.RequiresGood) reqs.Add(Loc.Get("inventory.req_good"));
            if (item.RequiresEvil) reqs.Add(Loc.Get("inventory.req_evil"));
            if (item.IsUnique) reqs.Add(Loc.Get("inventory.req_unique"));

            if (reqs.Count > 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("inventory.requires")}: {string.Join(", ", reqs)}");
            }

            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("inventory.value")}: {item.Value:N0} {Loc.Get("ui.gold_word")}");
        }

        private string GetSlotDisplayName(EquipmentSlot slot)
        {
            return slot switch
            {
                EquipmentSlot.MainHand => Loc.Get("inventory.slot_main_hand"),
                EquipmentSlot.OffHand => Loc.Get("inventory.slot_off_hand"),
                EquipmentSlot.Head => Loc.Get("inventory.slot_head"),
                EquipmentSlot.Body => Loc.Get("inventory.slot_body"),
                EquipmentSlot.Arms => Loc.Get("inventory.slot_arms"),
                EquipmentSlot.Hands => Loc.Get("inventory.slot_hands"),
                EquipmentSlot.Legs => Loc.Get("inventory.slot_legs"),
                EquipmentSlot.Feet => Loc.Get("inventory.slot_feet"),
                EquipmentSlot.Waist => Loc.Get("inventory.slot_waist"),
                EquipmentSlot.Neck => Loc.Get("inventory.slot_neck"),
                EquipmentSlot.Face => Loc.Get("inventory.slot_face"),
                EquipmentSlot.Cloak => Loc.Get("inventory.slot_cloak"),
                EquipmentSlot.LFinger => Loc.Get("inventory.slot_left_ring"),
                EquipmentSlot.RFinger => Loc.Get("inventory.slot_right_ring"),
                _ => slot.ToString()
            };
        }

        private async Task UnequipAll()
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inventory.unequipping_all"));

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
                terminal.WriteLine(Loc.Get("inventory.unequipped_count", count));
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("inventory.no_items_unequip"));
            }

            await Task.Delay(1500);
        }
    }
}
