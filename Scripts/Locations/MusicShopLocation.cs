using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Systems;
using UsurperRemake.BBS;

/// <summary>
/// The Music Shop - run by Cadence, apprentice to Melodia the Songweaver
/// Provides instrument sales (Bard-only), performance buffs (all classes),
/// companion recruitment (Melodia), and Old God lore songs
/// </summary>
public class MusicShopLocation : BaseLocation
{
    private int currentPage = 0;
    private const int ItemsPerPage = 15;

    // Melodia state helpers — determines shop dialogue and who's running things
    private enum MelodiaState { InShop, Adventuring, Dead }
    private MelodiaState GetMelodiaState()
    {
        var cs = CompanionSystem.Instance;
        if (cs == null) return MelodiaState.InShop;
        if (!cs.IsCompanionAlive(CompanionId.Melodia)) return MelodiaState.Dead;
        if (cs.IsCompanionRecruited(CompanionId.Melodia)) return MelodiaState.Adventuring;
        return MelodiaState.InShop;
    }

    // Old God lore song data
    private static readonly (OldGodType God, string Title, string Color, string[] Verses)[] LoreSongs = new[]
    {
        (OldGodType.Maelketh, "The Broken Blade's Lament", "red", new[]
        {
            "Before the sundering, he was honor's champion,",
            "His blade rang true for justice and for right.",
            "But when corruption's whisper found him willing,",
            "The War God's steel turned black as endless night.",
            "Now rage is all he knows, and blood his anthem —",
            "A warrior who forgot what he once fought."
        }),
        (OldGodType.Veloura, "Whispers of the Veil", "magenta", new[]
        {
            "She wove the threads that bound all hearts together,",
            "The goddess born of love's eternal flame.",
            "Yet love, when twisted, turns to thorns and poison —",
            "And Veloura forgot her very name.",
            "Behind the veil she fades, a ghost of passion,",
            "Still reaching for a warmth she cannot claim."
        }),
        (OldGodType.Thorgrim, "The Hammer's Judgment", "bright_yellow", new[]
        {
            "His laws were carved in stone before the mountains,",
            "His judgment absolute, his verdict fair.",
            "But justice without mercy breeds a tyrant —",
            "And Thorgrim's hammer fell beyond repair.",
            "Now every soul is guilty in his courtroom,",
            "And innocence a word he will not spare."
        }),
        (OldGodType.Noctura, "Shadow's Lullaby", "gray", new[]
        {
            "She walks between the spaces, never resting,",
            "The shadow that remembers every light.",
            "Not evil, not benevolent — just watching,",
            "A witness to the endless dance of night.",
            "Perhaps she is the wisest of the seven,",
            "For she alone still questions wrong and right."
        }),
        (OldGodType.Aurelion, "Dawn's Last Light", "bright_yellow", new[]
        {
            "He was the morning star, the hope of heaven,",
            "His light could heal the wounds that darkness made.",
            "But even suns must dim when left unaided —",
            "And Aurelion's dawn began to fade.",
            "He dreams of sunrise still, though bound in twilight,",
            "A dying god who prays he might be saved."
        }),
        (OldGodType.Terravok, "The Mountain's Memory", "green", new[]
        {
            "He held the world upon his ancient shoulders,",
            "The bedrock underneath all living things.",
            "When mountains crumble, even gods grow weary —",
            "And Terravok forgot what silence brings.",
            "He sleeps beneath the stone, a dormant giant,",
            "Still dreaming of the songs the deep earth sings."
        }),
        (OldGodType.Manwe, "The Ocean's Dream", "bright_cyan", new[]
        {
            "Before the seven, there was only Ocean —",
            "One vast and endless sea without a shore.",
            "He dreamed of waves, of separateness, of longing,",
            "And from that dream came everything and more.",
            "The Creator weeps for children he imprisoned,",
            "The dreamer who forgot what dreams are for.",
            "But every wave returns unto the Ocean —",
            "And every song must end where it began."
        }),
    };

    public MusicShopLocation() : base(
        GameLocation.MusicShop,
        "Music Shop",
        "You enter the Music Shop. Instruments line the walls and the air hums with faint melodies.")
    {
    }

    protected override string GetMudPromptName() => "Music Shop";

    protected override void DisplayLocation()
    {
        if (IsBBSSession && currentPlayer != null)
        {
            DisplayLocationBBS();
            return;
        }

        terminal.ClearScreen();
        if (currentPlayer == null) return;

        var state = GetMelodiaState();

        // Header box — matches Weapon/Armor/Healer pattern
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"MELODIA'S MUSIC SHOP".PadLeft((77 + 20) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Room description — changes based on Melodia's state
        terminal.SetColor("gray");
        terminal.WriteLine("Instruments of every kind hang from the walls and ceiling — lutes, lyres,");
        terminal.WriteLine("harps, drums, and things you have no name for. The air itself seems to");
        terminal.WriteLine("hum faintly, as if the instruments are whispering to one another.");
        terminal.WriteLine("");

        switch (state)
        {
            case MelodiaState.InShop:
                terminal.SetColor("white");
                terminal.WriteLine("Melodia sits at the back, tuning a lute. Her apprentice Cadence");
                terminal.WriteLine("stands behind the counter, polishing a silver flute.");
                terminal.SetColor("cyan");
                terminal.WriteLine("Cadence smiles. \"Welcome! Looking to buy, or just to listen?\"");
                break;
            case MelodiaState.Adventuring:
                terminal.SetColor("white");
                terminal.WriteLine("Cadence stands behind the counter, running the shop with quiet");
                terminal.WriteLine("confidence. A hand-painted sign reads: \"Melodia is out on an");
                terminal.WriteLine("adventure. All services still available — she taught me well!\"");
                terminal.SetColor("cyan");
                terminal.WriteLine("\"She talks about you, you know,\" Cadence says with a grin.");
                break;
            case MelodiaState.Dead:
                terminal.SetColor("white");
                terminal.WriteLine("Cadence stands behind the counter alone. A dried flower rests");
                terminal.WriteLine("beside a framed sketch of Melodia on the wall.");
                terminal.SetColor("gray");
                terminal.WriteLine("\"She'd want me to keep the music going,\" Cadence says quietly.");
                terminal.SetColor("cyan");
                terminal.WriteLine("\"So that's what I do. What can I play for you?\"");
                break;
        }
        terminal.WriteLine("");

        ShowNPCsInLocation();

        // Gold display — matches other shops
        terminal.SetColor("white");
        terminal.Write("You have ");
        terminal.SetColor("yellow");
        terminal.Write(FormatNumber(currentPlayer.Gold));
        terminal.SetColor("white");
        terminal.WriteLine(" gold crowns.");

        // Bard discount notice
        if (currentPlayer.Class == CharacterClass.Bard)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("  As a fellow musician, you receive a 25% discount on performances.");
        }
        terminal.WriteLine("");

        // Menu options
        terminal.SetColor("cyan");
        terminal.WriteLine("Services:");
        terminal.WriteLine("");

        // Buy Instruments
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("B");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        if (currentPlayer.Class == CharacterClass.Bard)
        {
            terminal.SetColor("white");
            terminal.WriteLine("Buy Instruments");
        }
        else
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("Buy Instruments (Bards only)");
        }

        // Hire a Performance
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("P");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write("Hire a Performance");
        terminal.SetColor("gray");
        terminal.WriteLine("  — combat buffs for 5 fights");

        // Talk to Melodia — only visible when she's physically in the shop
        if (state == MelodiaState.InShop)
        {
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            if (currentPlayer.Level >= 20)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine("Talk to Melodia  (she seems interested in your adventures)");
            }
            else
            {
                terminal.SetColor("white");
                terminal.WriteLine("Talk to Melodia");
            }
        }

        // Lore Songs
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("L");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("magenta");
        terminal.Write("Lore Songs of the Old Gods");
        terminal.SetColor("gray");
        terminal.WriteLine("  — ancient ballads");

        // Return
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("Q");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("Return to Main Street");

        terminal.WriteLine("");
    }

    private void DisplayLocationBBS()
    {
        terminal.ClearScreen();
        ShowBBSHeader("MELODIA'S MUSIC SHOP");

        var state = GetMelodiaState();
        terminal.SetColor("gray");
        switch (state)
        {
            case MelodiaState.InShop:
                terminal.Write(" Cadence minds the counter. Melodia tunes a lute in back. ");
                break;
            case MelodiaState.Adventuring:
                terminal.Write(" Cadence runs the shop. Melodia is out adventuring. ");
                break;
            case MelodiaState.Dead:
                terminal.Write(" Cadence keeps the music going in Melodia's memory. ");
                break;
        }
        terminal.SetColor("yellow");
        terminal.Write(FormatNumber(currentPlayer.Gold));
        terminal.SetColor("gray");
        terminal.WriteLine(" gold.");

        ShowBBSNPCs();
        terminal.WriteLine("");

        bool isBard = currentPlayer.Class == CharacterClass.Bard;
        ShowBBSMenuRow(
            ("B", isBard ? "bright_yellow" : "darkgray", isBard ? "uy Instruments" : "uy Instr (Bard)"),
            ("P", "bright_yellow", "erformance"),
            ("L", "bright_yellow", "ore Songs"));

        if (state == MelodiaState.InShop)
            ShowBBSMenuRow(("T", currentPlayer.Level >= 20 ? "bright_green" : "white", "alk to Melodia"), ("Q", "bright_red", "uit"));
        else
            ShowBBSMenuRow(("Q", "bright_red", "eturn"));

        ShowBBSFooter();
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        switch (choice.ToUpper())
        {
            case "B":
                await BuyInstruments();
                return false;

            case "P":
                await HirePerformance();
                return false;

            case "T":
                await RecruitMelodia();
                return false;

            case "L":
                await ShowLoreSongs();
                return false;

            case "Q":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            default:
                // Handle numeric input for instrument purchasing
                if (int.TryParse(choice, out int itemNum) && itemNum >= 1)
                {
                    await BuyInstrumentByNumber(itemNum);
                    return false;
                }
                return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // BUY INSTRUMENTS (Bard-only)
    // ═══════════════════════════════════════════════════════════════

    private async Task BuyInstruments()
    {
        if (currentPlayer.Class != CharacterClass.Bard)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("");
            string shopkeep = GetMelodiaState() == MelodiaState.InShop ? "Melodia" : "Cadence";
            terminal.WriteLine($"{shopkeep} shakes her head gently.");
            terminal.SetColor("cyan");
            terminal.WriteLine("\"These instruments require a bard's touch to truly sing. Perhaps a performance instead?\"");
            await terminal.PressAnyKey();
            return;
        }

        var instruments = EquipmentDatabase.GetShopWeapons(WeaponHandedness.OneHanded)
            .Where(w => w.WeaponType == WeaponType.Instrument)
            .ToList();

        int playerLevel = currentPlayer.Level;
        instruments = instruments.Where(i => i.MinLevel <= playerLevel + 15 && i.MinLevel >= Math.Max(1, playerLevel - 20)).ToList();

        if (instruments.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("");
            terminal.WriteLine("\"I'm afraid I don't have any instruments suitable for your level right now.\"");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("");
        terminal.WriteLine("═══ Instruments ═══");
        terminal.WriteLine("");

        // Show current weapon
        var currentWeapon = currentPlayer.GetEquipment(EquipmentSlot.MainHand);
        if (currentWeapon != null)
        {
            terminal.SetColor("cyan");
            terminal.Write("Current: ");
            terminal.SetColor("bright_white");
            terminal.Write(currentWeapon.Name);
            terminal.SetColor("gray");
            terminal.WriteLine($" (Pow:{currentWeapon.WeaponPower}, Value:{FormatNumber(currentWeapon.Value)})");
            terminal.WriteLine("");
        }

        // Paginate
        int startIndex = currentPage * ItemsPerPage;
        var pageItems = instruments.Skip(startIndex).Take(ItemsPerPage).ToList();
        int totalPages = (instruments.Count + ItemsPerPage - 1) / ItemsPerPage;

        terminal.SetColor("gray");
        terminal.WriteLine($"Page {currentPage + 1}/{totalPages} - {instruments.Count} instruments available");
        terminal.WriteLine("");

        terminal.SetColor("bright_blue");
        terminal.WriteLine("  #   Name                        Lvl  Pow  Price       Bonus");
        terminal.SetColor("darkgray");
        terminal.WriteLine("────────────────────────────────────────────────────────────────");

        int num = 1;
        foreach (var item in pageItems)
        {
            bool canAfford = currentPlayer.Gold >= item.Value;
            bool meetsLevel = currentPlayer.Level >= item.MinLevel;
            bool canBuy = canAfford && meetsLevel;

            terminal.SetColor(canBuy ? "bright_cyan" : "darkgray");
            terminal.Write($"{num,3}. ");

            terminal.SetColor(canBuy ? "white" : "darkgray");
            terminal.Write($"{item.Name,-26}");

            if (item.MinLevel > 1)
            {
                terminal.SetColor(!meetsLevel ? "red" : (canBuy ? "bright_cyan" : "darkgray"));
                terminal.Write($"{item.MinLevel,3}  ");
            }
            else
            {
                terminal.SetColor(canBuy ? "bright_cyan" : "darkgray");
                terminal.Write($"{"—",3}  ");
            }

            terminal.SetColor(canBuy ? "bright_cyan" : "darkgray");
            terminal.Write($"{item.WeaponPower,4}  ");

            terminal.SetColor(canBuy ? "yellow" : "darkgray");
            terminal.Write($"{FormatNumber(item.Value),10}  ");

            // Show bonus stats
            var bonuses = new List<string>();
            if (item.CharismaBonus > 0) bonuses.Add($"CHA+{item.CharismaBonus}");
            if (item.WisdomBonus > 0) bonuses.Add($"WIS+{item.WisdomBonus}");
            if (item.MaxManaBonus > 0) bonuses.Add($"Mana+{item.MaxManaBonus}");
            if (item.MaxHPBonus > 0) bonuses.Add($"HP+{item.MaxHPBonus}");
            terminal.SetColor(canBuy ? "bright_green" : "darkgray");
            terminal.WriteLine(bonuses.Count > 0 ? string.Join(" ", bonuses) : "");

            num++;
        }

        terminal.SetColor("gray");
        terminal.WriteLine("");
        terminal.Write("Enter # to buy");
        if (totalPages > 1)
        {
            terminal.Write(", [N]ext/[P]rev page");
        }
        terminal.WriteLine(", or [Q]uit:");

        string input = await terminal.GetInput("Your choice: ");
        if (string.IsNullOrEmpty(input)) return;

        string upper = input.ToUpper();
        if (upper == "N" && currentPage < totalPages - 1)
        {
            currentPage++;
            await BuyInstruments();
            return;
        }
        if (upper == "P" && currentPage > 0)
        {
            currentPage--;
            await BuyInstruments();
            return;
        }
        if (upper == "Q") return;

        if (int.TryParse(input, out int selection) && selection >= 1 && selection <= pageItems.Count)
        {
            await PurchaseInstrument(pageItems[selection - 1]);
        }

        currentPage = 0;
    }

    private async Task BuyInstrumentByNumber(int itemNum)
    {
        if (currentPlayer.Class != CharacterClass.Bard) return;

        var instruments = EquipmentDatabase.GetShopWeapons(WeaponHandedness.OneHanded)
            .Where(w => w.WeaponType == WeaponType.Instrument)
            .ToList();
        int playerLevel = currentPlayer.Level;
        instruments = instruments.Where(i => i.MinLevel <= playerLevel + 15 && i.MinLevel >= Math.Max(1, playerLevel - 20)).ToList();

        if (itemNum > instruments.Count) return;
        await PurchaseInstrument(instruments[itemNum - 1]);
    }

    private async Task PurchaseInstrument(Equipment item)
    {
        if (currentPlayer.Level < item.MinLevel)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"You need to be level {item.MinLevel} to use this instrument.");
            await terminal.PressAnyKey();
            return;
        }

        // Calculate tax
        var (kingTax, cityTax, totalCost) = CityControlSystem.CalculateTaxedPrice(item.Value);

        if (currentPlayer.Gold < totalCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"You need {FormatNumber(totalCost)} gold but only have {FormatNumber(currentPlayer.Gold)}!");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("bright_yellow");
        terminal.Write($"\nBuy {item.Name} for ");
        terminal.SetColor("yellow");
        terminal.Write(FormatNumber(totalCost));
        if (kingTax > 0 || cityTax > 0)
        {
            terminal.SetColor("gray");
            terminal.Write($" (incl. tax)");
        }
        terminal.WriteLine(" gold? [Y/N]");
        string confirm = await terminal.GetInput("Your choice: ");
        if (confirm?.ToUpper() != "Y") return;

        currentPlayer.Gold -= totalCost;
        currentPlayer.Statistics?.RecordPurchase(totalCost);

        // Process city tax
        CityControlSystem.Instance.ProcessSaleTax(item.Value);

        // Equip directly (instruments are one-handed, MainHand slot)
        if (currentPlayer.EquipItem(item, null, out string message))
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine($"\nYou purchased and equipped {item.Name}!");
            if (!string.IsNullOrEmpty(message))
            {
                terminal.SetColor("gray");
                terminal.WriteLine(message);
            }
            currentPlayer.RecalculateStats();
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"\nYou purchased {item.Name}!");
        }

        terminal.SetColor("cyan");
        string seller = GetMelodiaState() == MelodiaState.InShop ? "Melodia" : "Cadence";
        terminal.WriteLine($"{seller} nods approvingly. \"A fine choice! May it sing true in battle.\"");
        await terminal.PressAnyKey();
    }

    // ═══════════════════════════════════════════════════════════════
    // HIRE A PERFORMANCE (All players — combat buffs)
    // ═══════════════════════════════════════════════════════════════

    // Performance song lyrics — a mix of funny tavern songs and dramatic lore ballads.
    // Each song type has multiple possible performances chosen at random.
    // Intro text uses {PERFORMER} placeholder — replaced at runtime with "Melodia" or "Cadence".
    private static readonly (string Title, string Intro, string Color, string[] Verses)[] WarMarchSongs = new[]
    {
        ("The Ballad of Tully's Anvil", "{PERFORMER} grins and strikes a thundering chord.", "red", new[]
        {
            "Old Tully swung his hammer down upon the goblin's head,",
            "The goblin said, 'That's rather rude!' — and then the goblin's dead.",
            "He forged a sword so sharp it cut the wind clean in two,",
            "And sold it for a pittance to a hero just like you!",
            "So lift your blade and steady now, remember Tully's art —",
            "A good smith makes the weapon, but the fighter makes the heart."
        }),
        ("The March of the Last Legion", "{PERFORMER}'s voice drops low and the room grows still.", "red", new[]
        {
            "They marched at dawn, five hundred strong, through Maelketh's burning gate,",
            "No prayers upon their lips — just steel, and the refusal to be late.",
            "The War God's horde outnumbered them a dozen men to one,",
            "But not a single soldier turned to face the rising sun.",
            "They held the line until the dusk, and when the field was clear,",
            "Just thirty stood — but thirty was enough to break the fear.",
            "So when the darkness presses close and courage starts to fade,",
            "Remember those who held the line and were not afraid."
        }),
        ("The Orc Who Couldn't Count", "{PERFORMER} stifles a laugh before even starting.", "red", new[]
        {
            "An orc walked into battle with his club raised way up high,",
            "He counted all his enemies — there were two! (There were five.)",
            "'I'll smash the first!' he bellowed. 'Then I'll smash the other one!'",
            "The three he didn't count for had already turned to run.",
            "The moral of this story? Doesn't matter if you're thick —",
            "Just hit hard enough and fast enough, and arithmetic won't stick!"
        }),
    };

    private static readonly (string Title, string Intro, string Color, string[] Verses)[] IronLullabySongs = new[]
    {
        ("The Shield-Mother's Promise", "{PERFORMER} plays a slow, gentle melody that feels like armor settling into place.", "bright_cyan", new[]
        {
            "Before you were a warrior, before you held a sword,",
            "Someone stood between you and the world you can't afford.",
            "A mother, or a stranger, or a wall of ancient stone —",
            "Something said, 'Not yet. Not here. You will not fall alone.'",
            "That promise lives inside your skin, beneath your blood and bone.",
            "So let the monsters come. You carry iron you have always known."
        }),
        ("Jadu's Lament (The Healer's Burden)", "{PERFORMER}'s tone turns bittersweet.", "bright_cyan", new[]
        {
            "Old Jadu mends what others break, from sunrise until dark,",
            "He's patched up every fool who thought they'd fight a dragon on a lark.",
            "\"Why do they always come back worse?\" he mutters to his tea.",
            "\"I fixed that arm LAST Tuesday. Now they've gone and lost a knee.\"",
            "But still he heals, and still he waits, because he knows the truth:",
            "The world needs someone stubborn enough to keep stitching up its youth."
        }),
        ("The Wall That Would Not Break", "{PERFORMER} closes her eyes and plays from memory.", "bright_cyan", new[]
        {
            "They built a wall before the pass when Thorgrim's army came,",
            "Not stone — just farmers, merchants, and a blacksmith who was lame.",
            "No shields, no proper armor, just their bodies and their will.",
            "The army hit like thunder, but the wall was standing still.",
            "Three days they held with bleeding hands and backs against the rock.",
            "And on the fourth, the army left. They couldn't break the lock.",
            "Don't tell me walls need mortar. All a wall needs is the choice",
            "To plant your feet and say 'No more' in a steady voice."
        }),
    };

    private static readonly (string Title, string Intro, string Color, string[] Verses)[] FortuneSongs = new[]
    {
        ("The Merchant Prince of Anchor Road", "{PERFORMER} winks and plays a jaunty, jingling melody.", "bright_green", new[]
        {
            "There once was a man on Anchor Road who sold you what you've got,",
            "He'd buy it back for half the price and sell it for a lot.",
            "His pockets clinked, his coffers sang, his vault was never bare —",
            "He even charged the king a fee for breathing castle air!",
            "\"The secret,\" said the merchant prince, \"is simple as can be:",
            "Find what people think they need, and charge a modest fee.",
            "And if they haven't got the gold? Why, sell them debt instead!\"",
            "He died the richest man in town. (Nobody mourned. He's dead.)"
        }),
        ("The Dragon's Accountant", "{PERFORMER} plays a playful tune with a mischievous grin.", "bright_green", new[]
        {
            "A dragon sat upon his gold and counted every coin,",
            "He sorted them by vintage, weight, and kingdom of their join.",
            "A thief crept in at midnight with a sack and trembling nerve,",
            "The dragon said, 'You're off by three. Sit down. I'll teach you curves.'",
            "By morning light the thief was gone — with more gold than he'd planned.",
            "It turns out dragons tip quite well when someone understands."
        }),
        ("Gold Remembers", "{PERFORMER}'s voice takes on an ancient, knowing tone.", "bright_green", new[]
        {
            "Every coin you find was lost by someone, once upon a time.",
            "A soldier's final payment, or a bribe, or wedding chime.",
            "Gold remembers every hand that held it, spent it, stole —",
            "It carries all their stories pressed into its little soul.",
            "So when you loot the fallen, spare a moment if you can.",
            "That gold served someone else before it came to serve your plan."
        }),
    };

    private static readonly (string Title, string Intro, string Color, string[] Verses)[] BattleHymnSongs = new[]
    {
        ("The Seven Who Stood at the Gate", "{PERFORMER} stands, and her voice fills every corner of the room.", "magenta", new[]
        {
            "Before the Old Gods fell, before the dreaming and the dark,",
            "Seven heroes stood before Manwe's gate and left their mark.",
            "Not warriors all — a baker's son, a priest who'd lost her faith,",
            "A thief who'd sworn off stealing, and a knight who'd seen a wraith.",
            "A farmer with a pitchfork and a scholar with a pen,",
            "And one whose name is lost to us — they say she'll come again.",
            "They didn't win. They couldn't win. The Ocean swallowed all.",
            "But they stood there. That's what matters.",
            "They stood there, and stood tall."
        }),
        ("Melodia's Own", "{MELODIA_OWN_INTRO}", "magenta", new[]
        {
            "We were young and very stupid and we thought we'd never die,",
            "We laughed at every warning sign and never wondered why.",
            "The dungeon took them one by one — first Gareth, then the rest.",
            "Only one made it out alive. The one most blessed.",
            "Or cursed. She was never sure which.",
            "",
            "She kept their swords. She kept this shop. She played so she'd remember.",
            "And every hero who walked through that door — she'd see them there.",
            "Still laughing. Still not knowing what's ahead.",
            "Be better than they were."
        }),
        ("The Hymn of the Sleeping Earth", "{PERFORMER} plays deep, resonant notes that you feel in your chest.", "magenta", new[]
        {
            "Terravok sleeps beneath the mountain, dreaming slow and deep,",
            "His heartbeat is the earthquake, and his breath the caverns' keep.",
            "They say if all the Old Gods fall, the mountain falls as well —",
            "And everything we've built on top goes tumbling down to hell.",
            "But I don't think that's true. I think the mountain holds because",
            "We stand on it. We fight on it. We give the bedrock cause.",
            "So sharpen blade and steady shield. The earth won't let you sink.",
            "Not while you've got the nerve to stand, and the stubbornness to think."
        }),
    };

    private async Task HirePerformance()
    {
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("");
        terminal.WriteLine("═══ Hire a Performance ═══");
        var state = GetMelodiaState();
        string performer = state == MelodiaState.InShop ? "Melodia" : "Cadence";

        terminal.SetColor("gray");
        terminal.WriteLine($"{performer} can play you a song whose power lingers for 5 combats.");
        terminal.WriteLine("Each performance tells a different tale from the history of the realm.");
        if (currentPlayer.HasActiveSongBuff)
        {
            terminal.SetColor("yellow");
            string currentSong = currentPlayer.SongBuffType switch
            {
                1 => "War March",
                2 => "Lullaby of Iron",
                3 => "Fortune's Tune",
                4 => "Battle Hymn",
                _ => "Unknown"
            };
            terminal.WriteLine($"Active buff: {currentSong} ({currentPlayer.SongBuffCombats} combats remaining)");
            terminal.SetColor("gray");
            terminal.WriteLine("A new performance will replace the current one.");
        }
        terminal.WriteLine("");

        bool isBard = currentPlayer.Class == CharacterClass.Bard;
        float discount = isBard ? 0.75f : 1.0f;

        // Song 1: War March
        long warMarchPrice = (long)((200 + currentPlayer.Level * 10) * discount);
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("1");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("red");
        terminal.Write("War March");
        terminal.SetColor("gray");
        terminal.Write(" — +15% attack damage");
        terminal.SetColor("yellow");
        terminal.WriteLine($"  {FormatNumber(warMarchPrice)} gold");

        // Song 2: Lullaby of Iron
        long ironPrice = (long)((200 + currentPlayer.Level * 10) * discount);
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("2");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("bright_cyan");
        terminal.Write("Lullaby of Iron");
        terminal.SetColor("gray");
        terminal.Write(" — +15% defense");
        terminal.SetColor("yellow");
        terminal.WriteLine($"  {FormatNumber(ironPrice)} gold");

        // Song 3: Fortune's Tune
        long fortunePrice = (long)((300 + currentPlayer.Level * 15) * discount);
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("3");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("bright_green");
        terminal.Write("Fortune's Tune");
        terminal.SetColor("gray");
        terminal.Write(" — +25% gold from kills");
        terminal.SetColor("yellow");
        terminal.WriteLine($"  {FormatNumber(fortunePrice)} gold");

        // Song 4: Battle Hymn
        long hymnPrice = (long)((400 + currentPlayer.Level * 20) * discount);
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("4");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("magenta");
        terminal.Write("Battle Hymn");
        terminal.SetColor("gray");
        terminal.Write(" — +10% attack AND +10% defense");
        terminal.SetColor("yellow");
        terminal.WriteLine($"  {FormatNumber(hymnPrice)} gold");

        if (isBard)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("\n  * Bard discount: 25% off all performances!");
        }

        terminal.SetColor("gray");
        terminal.WriteLine("\nEnter song # or [Q]uit:");

        string input = await terminal.GetInput("Your choice: ");
        if (string.IsNullOrEmpty(input) || input.ToUpper() == "Q") return;

        int songType = 0;
        long price = 0;
        float value1 = 0f;
        float value2 = 0f;
        string songName = "";
        (string Title, string Intro, string Color, string[] Verses)[] songPool;

        switch (input)
        {
            case "1":
                songType = 1; price = warMarchPrice; value1 = GameConfig.SongWarMarchBonus;
                songName = "War March"; songPool = WarMarchSongs;
                break;
            case "2":
                songType = 2; price = ironPrice; value1 = GameConfig.SongIronLullabyBonus;
                songName = "Lullaby of Iron"; songPool = IronLullabySongs;
                break;
            case "3":
                songType = 3; price = fortunePrice; value1 = GameConfig.SongFortuneBonus;
                songName = "Fortune's Tune"; songPool = FortuneSongs;
                break;
            case "4":
                songType = 4; price = hymnPrice;
                value1 = GameConfig.SongBattleHymnBonus; value2 = GameConfig.SongBattleHymnBonus;
                songName = "Battle Hymn"; songPool = BattleHymnSongs;
                break;
            default:
                return;
        }

        if (currentPlayer.Gold < price)
        {
            terminal.SetColor("red");
            terminal.WriteLine("You don't have enough gold for this performance.");
            await terminal.PressAnyKey();
            return;
        }

        currentPlayer.Gold -= price;
        currentPlayer.Statistics?.RecordGoldSpent(price);
        currentPlayer.SongBuffType = songType;
        currentPlayer.SongBuffCombats = GameConfig.SongBuffDuration;
        currentPlayer.SongBuffValue = value1;
        currentPlayer.SongBuffValue2 = value2;

        // Pick a random performance from the pool
        var rng = new Random();
        var song = songPool[rng.Next(songPool.Length)];

        // Resolve the intro text — replace performer name and handle special cases
        string introText = song.Intro.Replace("{PERFORMER}", performer);
        if (introText == "{MELODIA_OWN_INTRO}")
        {
            introText = state == MelodiaState.InShop
                ? "Melodia hesitates, then plays something she hasn't played in a long time."
                : state == MelodiaState.Adventuring
                    ? "Cadence picks up the lute carefully. \"Melodia wrote this one. She taught it to me before she left.\""
                    : "Cadence's hands tremble slightly. \"This was hers. The last song she taught me.\"";
        }

        // Play the performance scene
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(introText);
        await Task.Delay(600);
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"\n  \"{song.Title}\"");
        await Task.Delay(400);
        terminal.WriteLine("");

        // Sing the verses with atmospheric pacing
        terminal.SetColor(song.Color);
        foreach (var verse in song.Verses)
        {
            if (string.IsNullOrEmpty(verse))
            {
                terminal.WriteLine("");
                await Task.Delay(300);
            }
            else
            {
                terminal.WriteLine($"  {verse}");
                await Task.Delay(350);
            }
        }

        await Task.Delay(400);
        terminal.SetColor("gray");
        terminal.WriteLine("");
        terminal.WriteLine("The last notes fade. The room is quiet for a moment.");
        await Task.Delay(300);
        terminal.SetColor("bright_green");
        terminal.WriteLine($"The {songName} stirs something in you. (+{(int)(value1 * 100)}% for {GameConfig.SongBuffDuration} combats)");
        await terminal.PressAnyKey();
    }

    // ═══════════════════════════════════════════════════════════════
    // RECRUIT MELODIA (Companion)
    // ═══════════════════════════════════════════════════════════════

    private async Task RecruitMelodia()
    {
        var state = GetMelodiaState();

        if (state == MelodiaState.Adventuring)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("\nMelodia is already traveling with you!");
            terminal.SetColor("cyan");
            terminal.WriteLine("Cadence waves from behind the counter. \"I've got things handled here!\"");
            await terminal.PressAnyKey();
            return;
        }

        if (state == MelodiaState.Dead)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("\nCadence glances at the portrait on the wall and looks away.");
            await terminal.PressAnyKey();
            return;
        }

        if (currentPlayer.Level < 20)
        {
            terminal.SetColor("white");
            terminal.WriteLine("");
            terminal.WriteLine("Melodia looks up from her lute and gives you a curious smile.");
            await Task.Delay(300);
            terminal.SetColor("cyan");
            if (currentPlayer.Level < 5)
            {
                terminal.WriteLine("\"You're new around here, aren't you? The dungeon hasn't had");
                terminal.WriteLine("a chance to leave its mark on you yet.\"");
                await Task.Delay(300);
                terminal.SetColor("gray");
                terminal.WriteLine("She plucks a gentle chord.");
                terminal.SetColor("cyan");
                terminal.WriteLine("\"Come back when you've seen a bit more of the world.\"");
            }
            else if (currentPlayer.Level < 10)
            {
                terminal.WriteLine("\"You're starting to carry yourself like someone who's been");
                terminal.WriteLine("down there. I can see it in the way you walk — a little more");
                terminal.WriteLine("careful, a little less certain.\"");
                await Task.Delay(300);
                terminal.SetColor("gray");
                terminal.WriteLine("She tilts her head, studying you.");
                terminal.SetColor("cyan");
                terminal.WriteLine("\"Keep at it. You've got the look of someone with a story");
                terminal.WriteLine("worth writing.\"");
            }
            else
            {
                terminal.WriteLine("\"You've been busy. I hear things, you know — the walls have");
                terminal.WriteLine("ears, and so do the instruments.\"");
                await Task.Delay(300);
                terminal.SetColor("gray");
                terminal.WriteLine("Her fingers find a melancholy tune on the lute strings.");
                terminal.SetColor("cyan");
                terminal.WriteLine("\"I used to do what you do. Part of me misses it terribly.\"");
                await Task.Delay(300);
                terminal.WriteLine("She shakes her head. \"Not yet. But soon, I think.\"");
            }
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("");
        terminal.WriteLine("Melodia sets down her lute and looks at you intently.");
        await Task.Delay(500);

        terminal.SetColor("cyan");
        terminal.WriteLine("\n\"I've heard whispers of your adventures through the dungeon.");
        await Task.Delay(300);
        terminal.WriteLine("The monsters you've faced, the gods you've encountered...\"");
        await Task.Delay(500);
        terminal.WriteLine("\nShe leans forward, her eyes bright with curiosity.");
        await Task.Delay(300);
        terminal.WriteLine("\"I used to travel with adventurers, you know. Before they fell.");
        await Task.Delay(300);
        terminal.WriteLine("I chronicled their deeds in song. Every victory, every loss.\"");
        await Task.Delay(500);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("\n\"Tell me — would you let a bard tag along? I promise I'm more");
        terminal.WriteLine("useful than I look. My songs can heal, inspire, and... well,");
        terminal.WriteLine("the right discordant note can shatter a monster's focus.\"");
        await Task.Delay(300);

        terminal.SetColor("gray");
        terminal.WriteLine("\nShe glances at Cadence, who nods eagerly.");
        terminal.SetColor("cyan");
        terminal.WriteLine("\"Don't worry about the shop. Cadence has been ready to run");
        terminal.WriteLine("this place on her own for a while now. Haven't you, dear?\"");
        terminal.SetColor("gray");
        terminal.WriteLine("Cadence grins. \"Go! I'll keep the instruments in tune.\"");
        await Task.Delay(300);

        terminal.SetColor("white");
        terminal.WriteLine("\nWould you like Melodia to join your party? [Y/N]");
        string input = await terminal.GetInput("Your choice: ");

        if (input?.ToUpper() == "Y")
        {
            bool success = await CompanionSystem.Instance.RecruitCompanion(CompanionId.Melodia, currentPlayer, terminal);
            if (success)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine("\nMelodia beams and grabs her instrument.");
                terminal.SetColor("cyan");
                terminal.WriteLine("\"This is going to make the best song yet! Let's go!\"");
                terminal.SetColor("gray");
                terminal.WriteLine("Cadence waves from behind the counter as you leave together.");
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("\nYour party is currently full. Visit the Inn to manage companions.");
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("\nMelodia nods understandingly.");
            terminal.SetColor("cyan");
            terminal.WriteLine("\"The offer stands whenever you're ready. I'll be here.\"");
        }
        await terminal.PressAnyKey();
    }

    // ═══════════════════════════════════════════════════════════════
    // LORE SONGS OF THE OLD GODS
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowLoreSongs()
    {
        var state = GetMelodiaState();

        terminal.SetColor("magenta");
        terminal.WriteLine("");
        terminal.WriteLine("═══ Lore Songs of the Old Gods ═══");
        terminal.SetColor("gray");
        if (state == MelodiaState.Dead)
            terminal.WriteLine("Ancient ballads Melodia passed down to Cadence before she fell.");
        else if (state == MelodiaState.Adventuring)
            terminal.WriteLine("Ancient ballads Melodia taught Cadence before leaving on her adventure.");
        else
            terminal.WriteLine("Ancient ballads about the seven gods who shaped this world.");
        terminal.WriteLine("Songs unlock as you encounter their subjects in the dungeon.");
        terminal.WriteLine("");

        var story = StoryProgressionSystem.Instance;
        int availableCount = 0;

        for (int i = 0; i < LoreSongs.Length; i++)
        {
            var (god, title, color, _) = LoreSongs[i];
            bool unlocked = IsGodSongUnlocked(god, story);

            terminal.SetColor("bright_yellow");
            terminal.Write($" [{i + 1}] ");

            if (unlocked)
            {
                terminal.SetColor(color);
                terminal.Write(title);
                bool heard = currentPlayer.HeardLoreSongs.Contains((int)god);
                if (!heard)
                {
                    terminal.SetColor("bright_green");
                    terminal.Write(" (NEW)");
                }
                terminal.WriteLine("");
                availableCount++;
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine("??? — You haven't encountered this being yet.");
            }
        }

        if (availableCount == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("\nNo songs available yet. Venture deeper into the dungeon...");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("gray");
        terminal.WriteLine("\nEnter song # to listen, or [Q]uit:");
        string input = await terminal.GetInput("Your choice: ");
        if (string.IsNullOrEmpty(input) || input.ToUpper() == "Q") return;

        if (int.TryParse(input, out int selection) && selection >= 1 && selection <= LoreSongs.Length)
        {
            var (god, title, color, verses) = LoreSongs[selection - 1];
            if (IsGodSongUnlocked(god, story))
            {
                await PlayLoreSong(god, title, color, verses);
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine("\nThis song has not yet been unlocked.");
                await terminal.PressAnyKey();
            }
        }
    }

    private bool IsGodSongUnlocked(OldGodType god, StoryProgressionSystem story)
    {
        if (!story.OldGodStates.TryGetValue(god, out var state)) return false;
        return state.Status == GodStatus.Defeated ||
               state.Status == GodStatus.Saved ||
               state.Status == GodStatus.Allied;
    }

    private async Task PlayLoreSong(OldGodType god, string title, string color, string[] verses)
    {
        terminal.WriteLine("");
        terminal.SetColor(color);
        terminal.WriteLine($"═══ {title} ═══");
        terminal.SetColor("gray");
        terminal.WriteLine("");

        foreach (var verse in verses)
        {
            terminal.SetColor(color);
            terminal.WriteLine($"  {verse}");
            await Task.Delay(600);
        }

        terminal.SetColor("gray");
        terminal.WriteLine("");

        // Grant awakening on first listen of each god's song
        bool firstTime = !currentPlayer.HeardLoreSongs.Contains((int)god);
        if (firstTime)
        {
            currentPlayer.HeardLoreSongs.Add((int)god);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("Something stirs within you... a deeper understanding of the gods.");

            try
            {
                // GainInsight grants real awakening points (unlike ExperienceMoment which is one-time-only)
                OceanPhilosophySystem.Instance?.GainInsight(1);
            }
            catch { }
        }

        await terminal.PressAnyKey();
    }

    private string FormatNumber(long value)
    {
        if (value >= 1_000_000) return $"{value / 1_000_000.0:F1}M";
        if (value >= 1_000) return $"{value / 1_000.0:F1}K";
        return value.ToString();
    }
}
