using UsurperRemake.BBS;
using UsurperRemake.Utils;
using UsurperRemake.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UsurperRemake.Locations;

/// <summary>
/// Love Street - Adult entertainment district combining the Beauty Nest (brothel for men),
/// Hall of Dreams (gigolos for women), and NPC dating/romance features.
/// Based on Pascal WHORES.PAS and GIGOLOC.PAS with expanded modern features.
/// v0.41.3: Overhauled Mingle, Gossip, Love Potions, and Gift Shop systems.
/// </summary>
public class LoveStreetLocation : BaseLocation
{
    private Random random = Random.Shared;

    // Per-visit state (cleared on each EnterLocation)
    private HashSet<string> _flirtedThisVisit = new();
    private bool _charmActive;
    private bool _allureActive;
    private bool _passionActive;

    // Per-session cosmetic state (not saved — traits revealed by buying drinks)
    private Dictionary<string, HashSet<string>> _revealedTraits = new();

    // Courtesans (female workers) - based on Pascal WHORES.PAS
    private static readonly List<Courtesan> Courtesans = new()
    {
        new Courtesan("Elly", "Mutant", 500, 0.35f, "A strange mix between races - exotic and dangerous.",
            "The mutant woman leads you to a small, dimly lit room upstairs. Her eyes glow faintly in the darkness as she approaches..."),
        new Courtesan("Lusha", "Troll", 2000, 0.30f, "An experienced troll woman with dark, weathered skin.",
            "Lusha takes your hand with surprising gentleness and leads you to her chambers. Despite her rough exterior, there's a practiced grace to her movements..."),
        new Courtesan("Irma", "Gnoll", 5000, 0.25f, "A young gnoll with nervous energy and exotic features.",
            "The gnoll girl leads you upstairs with quick, darting movements. Her fur is soft and warm as she draws close..."),
        new Courtesan("Elynthia", "Dwarf", 10000, 0.20f, "A middle-aged dwarven woman with knowing eyes.",
            "Elynthia gives you a tired but genuine smile. 'Let mama show you how it's done,' she says, leading you to a surprisingly cozy room..."),
        new Courtesan("Melissa", "Elf", 20000, 0.15f, "A beautiful elf maiden with sad, distant eyes.",
            "Melissa moves with ethereal grace, her long silver hair catching the candlelight. There's an ancient sadness in her eyes as she leads you to her chambers..."),
        new Courtesan("Seraphina", "Human", 30000, 0.12f, "A stunning redhead with fiery passion in her eyes.",
            "Seraphina's eyes burn with intensity as she takes your hand. 'Tonight, you're mine,' she whispers, pulling you toward the velvet curtains..."),
        new Courtesan("Sonya", "Elf", 40000, 0.10f, "A voluptuous elf woman in her prime, radiating sensuality.",
            "Sonya's curves are legendary in the district. She circles you slowly, appraising, before leading you to a room filled with silk and incense..."),
        new Courtesan("Arabella", "Human", 70000, 0.08f, "A breathtaking beauty that makes hearts stop.",
            "Arabella is perfection incarnate. Every movement is calculated to enchant. She leads you to the finest room, lit by a hundred candles..."),
        new Courtesan("Loretta", "Elf", 100000, 0.05f, "The legendary Elf Princess - the crown jewel of Love Street.",
            "Loretta, the Princess of Pleasure, graces you with her presence. Her touch is electric, her beauty beyond words. This night will change you forever...")
    };

    // Gigolos (male workers) - based on Pascal GIGOLOC.PAS
    private static readonly List<Gigolo> Gigolos = new()
    {
        new Gigolo("Signori", "Human", 500, 0.35f, "A slender, effeminate young man with delicate features.",
            "Signori leads you to a small but clean room. His touch is surprisingly skilled as he helps you relax..."),
        new Gigolo("Tod", "Human", 2000, 0.30f, "A muscular blonde viking type from the Northern lands.",
            "Tod's chamber is dominated by an enormous bed. He wastes no time, his powerful hands surprisingly gentle..."),
        new Gigolo("Mbuto", "Human", 5000, 0.25f, "A dark, muscular man with an air of mystery.",
            "Mbuto leads you to a candlelit cell. He locks the door and extinguishes the candles, plunging you into darkness filled with anticipation..."),
        new Gigolo("Merson", "Human", 10000, 0.20f, "A battle-scarred gladiator with intense eyes.",
            "Merson's room is spartan, like the warrior he is. But his touch reveals layers of tenderness beneath the hardened exterior..."),
        new Gigolo("Brian", "Human", 20000, 0.15f, "A fallen prince with divine looks and refined manners.",
            "Brian treats you like royalty, his noble bearing making you feel like the only person in the world..."),
        new Gigolo("Rasputin", "Human", 30000, 0.12f, "A mysterious mage who never removes his top hat.",
            "Rasputin offers you a strange potion. 'To enhance the experience,' he says with a knowing smile. The night becomes dreamlike..."),
        new Gigolo("Manhio", "Elf", 40000, 0.10f, "A tall, elegant elf aristocrat with centuries of experience.",
            "Manhio's chambers smell of exotic incense. His elven touch is unlike anything human, transcendent and electric..."),
        new Gigolo("Jake", "Human", 70000, 0.08f, "A rugged ranger in his prime, adored by many.",
            "Jake's wild nature comes through in passionate waves. The experience is primal, untamed, unforgettable..."),
        new Gigolo("Banco", "Human", 100000, 0.05f, "The Lord of Jah - legendary lover whose skills are whispered of in awe.",
            "Banco, the Lord of Pleasure himself, honors you with his attention. What follows defies description...")
    };

    public LoveStreetLocation() : base(
        GameLocation.LoveCorner,
        "Love Street",
        "The infamous pleasure district where desires are fulfilled for a price."
    ) { }

    public override async Task EnterLocation(Character player, TerminalEmulator term)
    {
        _flirtedThisVisit.Clear();
        _charmActive = false;
        _allureActive = false;
        _passionActive = false;
        await base.EnterLocation(player, term);
    }

    protected override void SetupLocation()
    {
        PossibleExits = new List<GameLocation> { GameLocation.MainStreet };
        LocationActions = new List<string>
        {
            "Visit the Pleasure Houses",
            "Mingle with NPCs",
            "Take someone on a date",
            "Gift Shop",
            "Gossip",
            "Love Potions",
            "Return to Main Street"
        };
    }

    protected override string GetMudPromptName() => "Love Street";

    private void DisplayLocationSR()
    {
        terminal.ClearScreen();
        terminal.WriteLine(Loc.Get("love_street.sr_title"));
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("love_street.sr_desc"));
        terminal.WriteLine("");

        ShowNPCsInLocation();

        terminal.WriteLine(Loc.Get("love_street.sr_pleasure"));
        WriteSRMenuOption("1", Loc.Get("love_street.beauty_nest"));
        WriteSRMenuOption("2", Loc.Get("love_street.hall_dreams"));
        terminal.WriteLine("");

        terminal.WriteLine(Loc.Get("love_street.sr_social"));
        WriteSRMenuOption("M", Loc.Get("love_street.mingle"));
        WriteSRMenuOption("D", Loc.Get("love_street.date"));
        WriteSRMenuOption("G", Loc.Get("love_street.gift_shop"));
        WriteSRMenuOption("V", Loc.Get("love_street.gossip"));
        WriteSRMenuOption("L", Loc.Get("love_street.love_potions"));
        terminal.WriteLine("");

        WriteSRMenuOption("R", Loc.Get("love_street.return"));
        terminal.WriteLine("");

        terminal.WriteLine($"{Loc.Get("ui.gold")}: {currentPlayer.Gold:N0}");
        if (_charmActive || _allureActive || _passionActive)
        {
            var potions = new List<string>();
            if (_charmActive) potions.Add(Loc.Get("love_street.potion_charm"));
            if (_allureActive) potions.Add(Loc.Get("love_street.potion_allure"));
            if (_passionActive) potions.Add(Loc.Get("love_street.potion_passion"));
            terminal.WriteLine(Loc.Get("love_street.active_potions", string.Join(", ", potions)));
        }
        terminal.WriteLine("");
    }

    protected override void DisplayLocation()
    {
        if (IsScreenReader) { DisplayLocationSR(); return; }
        if (IsBBSSession) { DisplayLocationBBS(); return; }

        terminal.ClearScreen();

        WriteBoxHeader(Loc.Get("love_street.header_visual"), "bright_magenta", 77);
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.desc1"));
        terminal.WriteLine(Loc.Get("love_street.desc2"));
        terminal.WriteLine(Loc.Get("love_street.desc3"));
        terminal.WriteLine("");

        // Show NPCs present
        ShowNPCsInLocation();

        WriteBoxHeader(Loc.Get("love_street.pleasure"), "bright_cyan", 77);
        terminal.WriteLine("");

        // Pleasure Houses
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("1");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("love_street.menu_beauty"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("2");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.menu_hall"));

        terminal.WriteLine("");

        // Social Options
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("M");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("love_street.menu_mingle"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("D");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.menu_date"));

        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("G");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("love_street.menu_gift"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("V");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.menu_gossip"));

        terminal.WriteLine("");

        // Potions & Return
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("L");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("love_street.menu_potions"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.menu_return"));

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("+---------------------------------------------------------------------------+");
        terminal.WriteLine("");

        // Show gold + active potions
        terminal.SetColor("yellow");
        terminal.Write($" {Loc.Get("ui.gold")}: {currentPlayer.Gold:N0}");
        if (_charmActive || _allureActive || _passionActive)
        {
            terminal.SetColor("magenta");
            terminal.Write(Loc.Get("love_street.potions_label"));
            if (_charmActive) terminal.Write(Loc.Get("love_street.potion_charm_tag"));
            if (_allureActive) terminal.Write(Loc.Get("love_street.potion_allure_tag"));
            if (_passionActive) terminal.Write(Loc.Get("love_street.potion_passion_tag"));
        }
        terminal.WriteLine("");
        terminal.WriteLine("");
    }

    /// <summary>
    /// Compact BBS display for 80x25 terminals.
    /// </summary>
    private void DisplayLocationBBS()
    {
        terminal.ClearScreen();
        ShowBBSHeader(Loc.Get("love_street.header"));

        // 1-line description
        terminal.SetColor("magenta");
        terminal.WriteLine(Loc.Get("love_street.bbs_desc"));

        ShowBBSNPCs();
        terminal.WriteLine("");

        // Menu rows
        ShowBBSMenuRow(("1", "bright_red", "BeautyNest"), ("2", "bright_blue", "HallDreams"), ("M", "yellow", "Mingle"), ("D", "green", "Date"));
        ShowBBSMenuRow(("G", "cyan", "Gifts"), ("V", "magenta", "Gossip"), ("L", "cyan", "Potions"), ("R", "red", "Return"));

        // Gold + active potions
        terminal.SetColor("gray");
        terminal.Write($" {Loc.Get("ui.gold")}:");
        terminal.SetColor("yellow");
        terminal.Write($"{currentPlayer.Gold:N0}");
        if (_charmActive || _allureActive || _passionActive)
        {
            terminal.SetColor("magenta");
            if (_charmActive) terminal.Write(Loc.Get("love_street.potion_charm_tag"));
            if (_allureActive) terminal.Write(Loc.Get("love_street.potion_allure_tag"));
            if (_passionActive) terminal.Write(Loc.Get("love_street.potion_passion_tag"));
        }
        terminal.WriteLine("");

        ShowBBSFooter();
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        // Handle global quick commands first
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        if (string.IsNullOrWhiteSpace(choice))
            return false;

        var upperChoice = choice.ToUpper().Trim();

        switch (upperChoice)
        {
            case "1":
                await VisitBeautyNest();
                return false;

            case "2":
                await VisitHallOfDreams();
                return false;

            case "M":
                await Mingle();
                return false;

            case "D":
                await TakeOnDate();
                return false;

            case "G":
                await VisitGiftShop();
                return false;

            case "V":
                await Gossip();
                return false;

            case "L":
                await LovePotions();
                return false;

            case "R":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            default:
                return await base.ProcessChoice(choice);
        }
    }

    #region Beauty Nest (Courtesans)

    private async Task VisitBeautyNest()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("love_street.beauty_nest_header"), "bright_red", 77);
        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("love_street.beauty_run_by"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.beauty_desc1"));
        terminal.WriteLine(Loc.Get("love_street.beauty_desc2"));
        terminal.WriteLine(Loc.Get("love_street.beauty_desc3"));
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("love_street.beauty_madam"));
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("love_street.beauty_looking"));
        terminal.WriteLine("");

        await ShowCourtesanMenu();
    }

    private async Task ShowCourtesanMenu()
    {
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("love_street.beauty_introduces"));
        terminal.WriteLine("");

        for (int i = 0; i < Courtesans.Count; i++)
        {
            var c = Courtesans[i];
            if (IsScreenReader)
            {
                WriteSRMenuOption($"{i + 1}", $"{c.Name} ({c.Race}), {c.Price:N0}g, Risk: {GetRiskLevel(c.DiseaseChance)}");
                terminal.WriteLine($"    {c.Description}");
            }
            else
            {
                terminal.SetColor("bright_yellow");
                terminal.Write($" {i + 1}) ");
                terminal.SetColor("bright_magenta");
                terminal.Write($"{c.Name}");
                terminal.SetColor("gray");
                terminal.Write($" ({c.Race}) - ");
                terminal.SetColor("yellow");
                terminal.Write($"{c.Price:N0} {Loc.Get("ui.gold_word")}");

                // Disease risk indicator
                terminal.SetColor("darkgray");
                terminal.Write($" [{Loc.Get("love_street.risk")}: ");
                if (c.DiseaseChance >= 0.30f)
                    terminal.SetColor("red");
                else if (c.DiseaseChance >= 0.15f)
                    terminal.SetColor("yellow");
                else
                    terminal.SetColor("green");
                terminal.Write(GetRiskLevel(c.DiseaseChance));
                terminal.SetColor("darkgray");
                terminal.WriteLine("]");

                terminal.SetColor("white");
                terminal.WriteLine($"    {c.Description}");
            }
            terminal.WriteLine("");
        }

        WriteSRMenuOption("0", Loc.Get("love_street.return"));
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("love_street.choose_companion"));
        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= Courtesans.Count)
        {
            await EngageWithCourtesan(Courtesans[choice - 1]);
        }
    }

    private async Task EngageWithCourtesan(Courtesan courtesan)
    {
        terminal.ClearScreen();
        terminal.WriteLine("");
        WriteBoxHeader($"{courtesan.Name} the {courtesan.Race}", "bright_magenta", 77);
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(courtesan.IntroText);
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("love_street.courtesan_wants_gold", courtesan.Name, $"{courtesan.Price:N0}"));
        terminal.WriteLine("");

        var confirm = await terminal.GetInput(Loc.Get("love_street.pay_confirm", courtesan.Name, $"{courtesan.Price:N0}"));
        if (string.IsNullOrEmpty(confirm) || confirm.ToUpper() != "Y")
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("love_street.shrug_loss", courtesan.Name));
            await terminal.WaitForKey();
            return;
        }

        if (currentPlayer.Gold < courtesan.Price)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("ui.not_enough_gold_courtesan", courtesan.Name));
            terminal.WriteLine(Loc.Get("love_street.cant_afford_courtesan"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("love_street.slink_away"));
            await terminal.WaitForKey();
            return;
        }

        // Process payment
        currentPlayer.Gold -= courtesan.Price;
        currentPlayer.Statistics?.RecordGoldSpent(courtesan.Price);

        // Show the intimate encounter
        await ShowIntimateEncounter(courtesan.Name, courtesan.Race, courtesan.Price, courtesan.DiseaseChance);
    }

    #endregion

    #region Hall of Dreams (Gigolos)

    private async Task VisitHallOfDreams()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("love_street.hall_dreams_header"), "bright_blue", 77);
        terminal.SetColor("bright_blue");
        terminal.WriteLine(Loc.Get("love_street.hall_run_by"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.hall_desc1"));
        terminal.WriteLine(Loc.Get("love_street.hall_desc2"));
        terminal.WriteLine(Loc.Get("love_street.hall_desc3"));
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("love_street.hall_giovanni"));
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("love_street.hall_hello"));
        terminal.WriteLine("");

        await ShowGigoloMenu();
    }

    private async Task ShowGigoloMenu()
    {
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("love_street.hall_introduces"));
        terminal.WriteLine("");

        for (int i = 0; i < Gigolos.Count; i++)
        {
            var g = Gigolos[i];
            if (IsScreenReader)
            {
                WriteSRMenuOption($"{i + 1}", $"{g.Name} ({g.Race}), {g.Price:N0}g, Risk: {GetRiskLevel(g.DiseaseChance)}");
                terminal.WriteLine($"    {g.Description}");
            }
            else
            {
                terminal.SetColor("bright_yellow");
                terminal.Write($" {i + 1}) ");
                terminal.SetColor("bright_blue");
                terminal.Write($"{g.Name}");
                terminal.SetColor("gray");
                terminal.Write($" ({g.Race}) - ");
                terminal.SetColor("yellow");
                terminal.Write($"{g.Price:N0} {Loc.Get("ui.gold_word")}");

                // Disease risk indicator
                terminal.SetColor("darkgray");
                terminal.Write($" [{Loc.Get("love_street.risk")}: ");
                if (g.DiseaseChance >= 0.30f)
                    terminal.SetColor("red");
                else if (g.DiseaseChance >= 0.15f)
                    terminal.SetColor("yellow");
                else
                    terminal.SetColor("green");
                terminal.Write(GetRiskLevel(g.DiseaseChance));
                terminal.SetColor("darkgray");
                terminal.WriteLine("]");

                terminal.SetColor("white");
                terminal.WriteLine($"    {g.Description}");
            }
            terminal.WriteLine("");
        }

        WriteSRMenuOption("0", Loc.Get("love_street.return"));
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("love_street.choose_companion"));
        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= Gigolos.Count)
        {
            await EngageWithGigolo(Gigolos[choice - 1]);
        }
    }

    private async Task EngageWithGigolo(Gigolo gigolo)
    {
        terminal.ClearScreen();
        terminal.WriteLine("");
        WriteBoxHeader($"{gigolo.Name} the {gigolo.Race}", "bright_blue", 77);
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(gigolo.IntroText);
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("love_street.gigolo_wants_gold", gigolo.Name, $"{gigolo.Price:N0}"));
        terminal.WriteLine("");

        var confirm = await terminal.GetInput(Loc.Get("love_street.pay_confirm", gigolo.Name, $"{gigolo.Price:N0}"));
        if (string.IsNullOrEmpty(confirm) || confirm.ToUpper() != "Y")
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("love_street.gigolo_bow", gigolo.Name));
            await terminal.WaitForKey();
            return;
        }

        if (currentPlayer.Gold < gigolo.Price)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("love_street.gigolo_lack_funds", gigolo.Name));
            terminal.WriteLine(Loc.Get("love_street.gigolo_heavier"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("love_street.leave_disappointed"));
            await terminal.WaitForKey();
            return;
        }

        // Process payment
        currentPlayer.Gold -= gigolo.Price;
        currentPlayer.Statistics?.RecordGoldSpent(gigolo.Price);

        // Show the intimate encounter
        await ShowIntimateEncounter(gigolo.Name, gigolo.Race, gigolo.Price, gigolo.DiseaseChance);
    }

    #endregion

    #region Intimate Encounter System

    private async Task ShowIntimateEncounter(string partnerName, string race, long price, float diseaseChance)
    {
        terminal.ClearScreen();
        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("love_street.night_passion"), "bright_magenta", 77);
        terminal.WriteLine("");

        // Generate encounter based on price tier
        await GenerateIntimateScene(partnerName, race, price);

        // Calculate rewards
        int baseXp = price switch
        {
            <= 1000 => random.Next(5, 30),
            <= 5000 => random.Next(10, 50),
            <= 20000 => random.Next(25, 100),
            <= 50000 => random.Next(50, 150),
            _ => random.Next(100, 300)
        };
        long xpGained = baseXp * currentPlayer.Level;

        int darkPoints = price switch
        {
            <= 1000 => random.Next(15, 45),
            <= 5000 => random.Next(25, 85),
            <= 20000 => random.Next(50, 200),
            <= 50000 => random.Next(100, 300),
            _ => random.Next(150, 650)
        };

        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("love_street.night_pleasure_with", partnerName), "bright_yellow");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.xp_gained", $"{xpGained:N0}"));
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("love_street.dark_points", darkPoints));

        currentPlayer.Experience += xpGained;
        GiveDarkness(currentPlayer, darkPoints);

        // Check for disease
        await CheckForDisease(partnerName, diseaseChance);

        // Record the encounter in RomanceTracker
        RecordPaidEncounter(partnerName, price);

        // News system
        GenerateNews(partnerName);

        // Notify spouse if married
        NotifySpouse(partnerName);

        await terminal.WaitForKey();
    }

    private void RecordPaidEncounter(string partnerName, long price)
    {
        var encounter = new IntimateEncounter
        {
            Date = DateTime.Now,
            Location = "Love Street",
            PartnerIds = new List<string> { $"courtesan_{partnerName}" },
            Type = EncounterType.Solo,
            Mood = IntimacyMood.Quick,
            IsFirstTime = false
        };
        RomanceTracker.Instance.RecordEncounter(encounter);
        currentPlayer.Darkness += 1;
    }

    private async Task GenerateIntimateScene(string partnerName, string race, long price)
    {
        terminal.SetColor("white");

        var openings = new[]
        {
            Loc.Get("love_street.scene_opening1", partnerName),
            Loc.Get("love_street.scene_opening2", partnerName),
            Loc.Get("love_street.scene_opening3", partnerName),
            Loc.Get("love_street.scene_opening4", partnerName),
            Loc.Get("love_street.scene_opening5", partnerName)
        };
        terminal.WriteLine(openings[random.Next(openings.Length)]);
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("bright_magenta");
        var tensions = new[]
        {
            Loc.Get("love_street.scene_tension1", partnerName),
            Loc.Get("love_street.scene_tension2", partnerName),
            Loc.Get("love_street.scene_tension3", partnerName),
            Loc.Get("love_street.scene_tension4", partnerName)
        };
        terminal.WriteLine(tensions[random.Next(tensions.Length)]);
        terminal.WriteLine("");
        await Task.Delay(1000);

        if (price >= 50000)
            await ShowPremiumEncounter(partnerName, race);
        else if (price >= 20000)
            await ShowHighEndEncounter(partnerName, race);
        else if (price >= 5000)
            await ShowStandardEncounter(partnerName, race);
        else
            await ShowBasicEncounter(partnerName, race);

        terminal.WriteLine("");
        terminal.SetColor("gray");
        var aftermaths = new[]
        {
            Loc.Get("love_street.scene_aftermath1", partnerName),
            Loc.Get("love_street.scene_aftermath2", partnerName),
            Loc.Get("love_street.scene_aftermath3", partnerName),
            Loc.Get("love_street.scene_aftermath4", partnerName)
        };
        terminal.WriteLine(aftermaths[random.Next(aftermaths.Length)]);
        await Task.Delay(500);
    }

    private async Task ShowBasicEncounter(string name, string race)
    {
        terminal.SetColor("white");
        var scenes = new[]
        {
            Loc.Get("love_street.basic_scene1", name),
            Loc.Get("love_street.basic_scene2", name),
            Loc.Get("love_street.basic_scene3", name),
            Loc.Get("love_street.basic_scene4", name)
        };
        terminal.WriteLine(scenes[random.Next(scenes.Length)]);
        terminal.WriteLine("");

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("love_street.basic_climax1", name));
        terminal.WriteLine(Loc.Get("love_street.basic_climax2"));
        await Task.Delay(500);
    }

    private async Task ShowStandardEncounter(string name, string race)
    {
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.standard_scene1", name));
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("love_street.standard_scene2", name));
        terminal.WriteLine(Loc.Get("love_street.standard_scene3", name));
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.standard_scene4", name));
        terminal.WriteLine(Loc.Get("love_street.standard_scene5"));
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("love_street.standard_scene6", name));
        terminal.WriteLine(Loc.Get("love_street.standard_scene7"));
        await Task.Delay(500);
    }

    private async Task ShowHighEndEncounter(string name, string race)
    {
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.highend_scene1", name));
        terminal.WriteLine(Loc.Get("love_street.highend_scene2"));
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("love_street.highend_scene3", name));
        terminal.WriteLine(Loc.Get("love_street.highend_scene4"));
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.highend_scene5", name));
        terminal.WriteLine(Loc.Get("love_street.highend_scene6"));
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("love_street.highend_scene7", name));
        terminal.WriteLine(Loc.Get("love_street.highend_scene8"));
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.highend_scene9", name));
        terminal.WriteLine(Loc.Get("love_street.highend_scene10"));
        await Task.Delay(500);
    }

    private async Task ShowPremiumEncounter(string name, string race)
    {
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.premium_scene1", name));
        terminal.WriteLine(Loc.Get("love_street.premium_scene2"));
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("love_street.premium_scene3", name));
        terminal.WriteLine(Loc.Get("love_street.premium_scene4"));
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.premium_scene5"));
        terminal.WriteLine(Loc.Get("love_street.premium_scene6", name));
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("love_street.premium_scene7"));
        terminal.WriteLine(Loc.Get("love_street.premium_scene8", name));
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.premium_scene9", name));
        terminal.WriteLine(Loc.Get("love_street.premium_scene10"));
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("love_street.premium_scene11", name));
        terminal.WriteLine(Loc.Get("love_street.premium_scene12"));
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.premium_scene13"));
        terminal.WriteLine(Loc.Get("love_street.premium_scene14", name));
        await Task.Delay(500);
    }

    private async Task CheckForDisease(string partnerName, float diseaseChance)
    {
        float roll = (float)random.NextDouble();

        if (roll < diseaseChance)
        {
            terminal.WriteLine("");
            WriteBoxHeader(Loc.Get("love_street.something_wrong"), "red", 77);
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("love_street.disease_pain"));
            terminal.WriteLine(Loc.Get("love_street.disease_infected"));
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("love_street.disease_rush"));
            terminal.WriteLine(Loc.Get("love_street.disease_treatments"));
            terminal.WriteLine("");

            currentPlayer.HP = Math.Max(1, currentPlayer.HP / 2);
            currentPlayer.LoversBane = true;

            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("love_street.disease_contracted"));
            terminal.WriteLine(Loc.Get("love_street.disease_hp_halved"));
            terminal.WriteLine("");

            NewsSystem.Instance.Newsy(true,
                Loc.Get("love_street.news_disease", currentPlayer.Name2),
                Loc.Get("love_street.news_disease_sub"));

            MailSystem.SendSystemMail(currentPlayer.Name2, Loc.Get("love_street.mail_disease_subject"),
                Loc.Get("love_street.mail_disease_title"),
                Loc.Get("love_street.mail_disease_body", partnerName),
                Loc.Get("love_street.mail_disease_urgent"));

            await Task.Delay(2000);
        }
    }

    private void GenerateNews(string partnerName)
    {
        if (random.Next(3) == 0)
        {
            var headlines = new[]
            {
                Loc.Get("love_street.news_steamy", currentPlayer.Name2),
                Loc.Get("love_street.news_leaving", currentPlayer.Name2, partnerName),
                Loc.Get("love_street.news_witnesses", currentPlayer.Name2),
                Loc.Get("love_street.news_romantic", currentPlayer.Name2)
            };
            NewsSystem.Instance.Newsy(true, headlines[random.Next(headlines.Length)]);
        }
    }

    private void NotifySpouse(string partnerName)
    {
        var spouses = RomanceTracker.Instance.Spouses;
        foreach (var spouse in spouses)
        {
            MailSystem.SendSystemMail(spouse.NPCId, Loc.Get("love_street.mail_infidelity_subject"),
                Loc.Get("love_street.mail_infidelity_title"),
                Loc.Get("love_street.mail_infidelity_body", currentPlayer.Name2),
                Loc.Get("love_street.mail_infidelity_detail", partnerName));

            RomanceTracker.Instance.JealousyLevels[spouse.NPCId] =
                Math.Min(100, RomanceTracker.Instance.JealousyLevels.GetValueOrDefault(spouse.NPCId, 0) + 20);
        }
    }

    #endregion

    #region Mingle (replaces Meet NPCs)

    private async Task Mingle()
    {
        terminal.ClearScreen();
        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("love_street.mingle_header"), "bright_magenta", 77);
        terminal.WriteLine("");

        // Get NPCs at Love Street first
        var npcsHere = GetLiveNPCsAtLocation();
        var allNPCs = new List<NPC>(npcsHere);

        // Pad with compatible NPCs from other locations if needed
        if (allNPCs.Count < GameConfig.LoveStreetMaxMingleNPCs)
        {
            var playerGender = currentPlayer.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male;
            var hereIds = new HashSet<string>(allNPCs.Select(n => n.ID));

            var extras = (NPCSpawnSystem.Instance?.ActiveNPCs ?? new List<NPC>())
                .Where(n => n.IsAlive && !n.IsDead && !hereIds.Contains(n.ID))
                .Where(n => IsPlayerAttractedTo(n)) // Filter by player orientation
                .Where(n => n.Brain?.Personality?.IsAttractedTo(playerGender) == true ||
                            RelationshipSystem.GetRelationshipStatus(currentPlayer, n) <= 40)
                .OrderBy(n => RelationshipSystem.GetRelationshipStatus(currentPlayer, n))
                .Take(GameConfig.LoveStreetMaxMingleNPCs - allNPCs.Count)
                .ToList();

            if (extras.Count > 0)
            {
                allNPCs.AddRange(extras);
            }
        }

        if (allNPCs.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("love_street.mingle_nobody"));
            terminal.WriteLine(Loc.Get("love_street.mingle_try_later"));
            await terminal.WaitForKey();
            return;
        }

        // Show NPC list with romance-relevant info
        if (npcsHere.Count > 0)
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("love_street.mingle_people"));
        }

        for (int i = 0; i < allNPCs.Count; i++)
        {
            var npc = allNPCs[i];
            if (i == npcsHere.Count && npcsHere.Count > 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("love_street.mingle_others"));
            }

            var romanceType = RomanceTracker.Instance.GetRelationType(npc.ID);
            int relationLevel = RelationshipSystem.GetRelationshipStatus(currentPlayer, npc);

            if (IsScreenReader)
            {
                string tag = romanceType switch
                {
                    RomanceRelationType.Spouse => $" ({Loc.Get("love_street.tag_spouse")})",
                    RomanceRelationType.Lover => $" ({Loc.Get("love_street.tag_lover")})",
                    RomanceRelationType.FWB => $" ({Loc.Get("love_street.tag_fwb")})",
                    _ => relationLevel <= 40 ? $" ({Loc.Get("love_street.tag_friend")})" : relationLevel <= 70 ? "" : $" ({Loc.Get("love_street.tag_wary")})"
                };
                var profile = npc.Brain?.Personality;
                string hint = profile != null ? $", {GetPersonalityHint(profile)}" : "";
                WriteSRMenuOption($"{i + 1}", $"{npc.Name}, Lv{npc.Level} {npc.Race} {npc.ClassName}{tag}{hint}");
            }
            else
            {
                terminal.SetColor("bright_yellow");
                terminal.Write($" [{i + 1}] ");
                terminal.SetColor("white");
                terminal.Write($"{npc.Name}");
                terminal.SetColor("gray");
                terminal.Write($" (Lv{npc.Level} {npc.Race} {npc.ClassName})");

                // Relationship tag
                string tag = romanceType switch
                {
                    RomanceRelationType.Spouse => $" [{Loc.Get("love_street.tag_spouse")}]",
                    RomanceRelationType.Lover => $" [{Loc.Get("love_street.tag_lover")}]",
                    RomanceRelationType.FWB => $" [{Loc.Get("love_street.tag_fwb")}]",
                    _ => relationLevel <= 40 ? $" [{Loc.Get("love_street.tag_friend")}]" : relationLevel <= 70 ? "" : $" [{Loc.Get("love_street.tag_wary")}]"
                };
                if (!string.IsNullOrEmpty(tag))
                {
                    terminal.SetColor(romanceType == RomanceRelationType.Spouse ? "bright_red" :
                                      romanceType == RomanceRelationType.Lover ? "bright_magenta" :
                                      romanceType == RomanceRelationType.FWB ? "cyan" :
                                      relationLevel <= 40 ? "bright_green" : "darkgray");
                    terminal.Write(tag);
                }

                // Personality hint
                var profile = npc.Brain?.Personality;
                if (profile != null)
                {
                    terminal.SetColor("darkgray");
                    terminal.Write($" - {GetPersonalityHint(profile)}");
                }

                terminal.WriteLine("");
            }
        }

        terminal.WriteLine("");
        WriteSRMenuOption("0", Loc.Get("love_street.return"));
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("love_street.who_catches_eye"));
        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= allNPCs.Count)
        {
            await MingleWithNPC(allNPCs[choice - 1]);
        }
    }

    private async Task MingleWithNPC(NPC npc)
    {
        npc.IsInConversation = true; // Protect from world sim during romantic interaction
        try
        {
        bool stayInMenu = true;
        while (stayInMenu)
        {
            terminal.ClearScreen();
            terminal.WriteLine("");
            WriteBoxHeader(Loc.Get("love_street.spending_time", npc.Name), "bright_magenta", 77);
            terminal.WriteLine("");

            // Show NPC details
            var romanceType = RomanceTracker.Instance.GetRelationType(npc.ID);
            int relationLevel = RelationshipSystem.GetRelationshipStatus(currentPlayer, npc);

            terminal.SetColor("white");
            terminal.Write($" {npc.Name}");
            terminal.SetColor("gray");
            terminal.WriteLine($" - {Loc.Get("love_street.level_info", npc.Level, npc.Race, npc.Class, npc.Age)}");

            terminal.SetColor("white");
            terminal.Write($" {Loc.Get("love_street.relationship")}: ");
            terminal.SetColor(relationLevel <= 20 ? "bright_magenta" : relationLevel <= 50 ? "bright_green" : relationLevel <= 70 ? "yellow" : "red");
            terminal.WriteLine(GetRelationDescription(relationLevel));

            // Show revealed traits
            if (_revealedTraits.TryGetValue(npc.ID, out var traits) && traits.Count > 0)
            {
                terminal.SetColor("cyan");
                terminal.Write($" {Loc.Get("love_street.traits")}: ");
                terminal.SetColor("white");
                terminal.WriteLine(string.Join(", ", traits));
            }

            bool alreadyFlirted = _flirtedThisVisit.Contains(npc.ID);

            terminal.WriteLine("");
            WriteSRMenuOption("F", alreadyFlirted ? Loc.Get("love_street.flirt_tried") : Loc.Get("love_street.flirt"));
            WriteSRMenuOption("C", Loc.Get("love_street.compliment"));
            WriteSRMenuOption("B", Loc.Get("love_street.buy_drink", GameConfig.LoveStreetDrinkCost));
            WriteSRMenuOption("D", Loc.Get("love_street.ask_date"));
            WriteSRMenuOption("T", Loc.Get("love_street.talk"));
            WriteSRMenuOption("0", Loc.Get("ui.cancel"));

            terminal.WriteLine("");

            var action = await terminal.GetInput(Loc.Get("love_street.what_do"));
            switch (action?.ToUpper())
            {
                case "F":
                    await FlirtWithNPC(npc);
                    break;
                case "C":
                    await ComplimentNPC(npc);
                    break;
                case "B":
                    await BuyDrinkForNPC(npc);
                    break;
                case "D":
                    await GoOnDate(npc);
                    stayInMenu = false;
                    break;
                case "T":
                    await VisualNovelDialogueSystem.Instance.StartConversation(currentPlayer, npc, terminal);
                    stayInMenu = false;
                    break;
                case "0":
                    stayInMenu = false;
                    break;
            }
        }
        }
        finally { npc.IsInConversation = false; }
    }

    private async Task FlirtWithNPC(NPC npc)
    {
        if (_flirtedThisVisit.Contains(npc.ID))
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("love_street.mingle_already_flirted"));
            await terminal.WaitForKey();
            return;
        }

        _flirtedThisVisit.Add(npc.ID);

        terminal.WriteLine("");

        var profile = npc.Brain?.Personality;
        var playerGender = currentPlayer.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male;
        bool isAttracted = profile?.IsAttractedTo(playerGender) ?? true;
        bool npcIsMarried = NPCMarriageRegistry.Instance?.IsMarriedToNPC(npc.ID) == true;
        bool npcHasLover = RomanceTracker.Instance.GetRelationType(npc.ID) == RomanceRelationType.Lover;
        int relationLevel = RelationshipSystem.GetRelationshipStatus(currentPlayer, npc);

        float receptiveness = profile?.GetFlirtReceptiveness(relationLevel, isAttracted, npcIsMarried, npcHasLover) ?? 0.3f;

        // Apply CHA modifier
        float chaBonus = ((currentPlayer.Charisma + (_charmActive ? GameConfig.LoveStreetCharmBonus : 0)) - 10) * 0.02f;
        receptiveness += chaBonus;
        receptiveness = Math.Clamp(receptiveness, 0.05f, 0.90f);

        // Allure potion = guaranteed success
        bool success = _allureActive || (random.NextDouble() < receptiveness);

        if (success)
        {
            int boost = _allureActive ? GameConfig.LoveStreetAllureFlirtBoost : GameConfig.LoveStreetFlirtBoost;
            if (_allureActive)
            {
                _allureActive = false;
                terminal.SetColor("magenta");
                terminal.WriteLine(Loc.Get("love_street.allure_surges"));
            }

            RelationshipSystem.UpdateRelationship(currentPlayer, npc, 1, boost, false, true);

            // Flavor text based on personality
            var flirtResponses = new[]
            {
                Loc.Get("love_street.flirt_success1", npc.Name),
                Loc.Get("love_street.flirt_success2", npc.Name),
                Loc.Get("love_street.flirt_success3", npc.Name),
                Loc.Get("love_street.flirt_success4", npc.Name),
                Loc.Get("love_street.flirt_success5", npc.Name),
                Loc.Get("love_street.flirt_success6", npc.Name)
            };
            terminal.SetColor("bright_green");
            terminal.WriteLine(flirtResponses[random.Next(flirtResponses.Length)]);
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("love_street.relation_improved", npc.Name));
        }
        else
        {
            var failResponses = new[]
            {
                Loc.Get("love_street.flirt_fail1", npc.Name),
                Loc.Get("love_street.flirt_fail2", npc.Name),
                Loc.Get("love_street.flirt_fail3", npc.Name),
                Loc.Get("love_street.flirt_fail4", npc.Name),
                Loc.Get("love_street.flirt_fail5", npc.Name)
            };
            terminal.SetColor("yellow");
            terminal.WriteLine(failResponses[random.Next(failResponses.Length)]);
        }

        await terminal.WaitForKey();
    }

    private async Task ComplimentNPC(NPC npc)
    {
        terminal.WriteLine("");

        // Race/class-specific compliments
        var compliments = new List<string>
        {
            Loc.Get("love_street.compliment1", npc.Name),
            Loc.Get("love_street.compliment2", npc.Name),
            Loc.Get("love_street.compliment3", npc.Name)
        };

        // Add race-specific
        switch (npc.Race.ToString())
        {
            case "Elf":
                compliments.Add(Loc.Get("love_street.compliment_elf", npc.Name));
                break;
            case "Dwarf":
                compliments.Add(Loc.Get("love_street.compliment_dwarf", npc.Name));
                break;
            case "Human":
                compliments.Add(Loc.Get("love_street.compliment_human", npc.Name));
                break;
            default:
                compliments.Add(Loc.Get("love_street.compliment_other", npc.Name));
                break;
        }

        string compliment = compliments[random.Next(compliments.Count)];

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(compliment);
        terminal.WriteLine("");

        RelationshipSystem.UpdateRelationship(currentPlayer, npc, 1, GameConfig.LoveStreetComplimentBoost, false, true);

        var reactions = new[]
        {
            Loc.Get("love_street.compliment_react1", npc.Name),
            Loc.Get("love_street.compliment_react2", npc.Name),
            Loc.Get("love_street.compliment_react3", npc.Name)
        };
        terminal.SetColor("bright_green");
        terminal.WriteLine(reactions[random.Next(reactions.Length)]);

        await terminal.WaitForKey();
    }

    private async Task BuyDrinkForNPC(NPC npc)
    {
        if (currentPlayer.Gold < GameConfig.LoveStreetDrinkCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("love_street.need_drink_gold", GameConfig.LoveStreetDrinkCost));
            await terminal.WaitForKey();
            return;
        }

        currentPlayer.Gold -= GameConfig.LoveStreetDrinkCost;
        currentPlayer.Statistics?.RecordGoldSpent(GameConfig.LoveStreetDrinkCost);

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("love_street.buy_drinks", npc.Name));
        terminal.WriteLine("");

        RelationshipSystem.UpdateRelationship(currentPlayer, npc, 1, GameConfig.LoveStreetDrinkBoost, false, true);

        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("love_street.relation_improved", npc.Name));

        // Reveal a personality trait
        var profile = npc.Brain?.Personality;
        if (profile != null)
        {
            if (!_revealedTraits.ContainsKey(npc.ID))
                _revealedTraits[npc.ID] = new HashSet<string>();

            var possible = new List<(string key, string text)>();
            if (!_revealedTraits[npc.ID].Contains("romanticism"))
                possible.Add(("romanticism", profile.Romanticism > 0.6f ? Loc.Get("love_street.reveal_hopeless_romantic") : profile.Romanticism > 0.3f ? Loc.Get("love_street.reveal_open_romance") : Loc.Get("love_street.reveal_practical")));
            if (!_revealedTraits[npc.ID].Contains("commitment"))
                possible.Add(("commitment", profile.Commitment > 0.6f ? Loc.Get("love_street.reveal_seeking_commitment") : profile.Commitment > 0.3f ? Loc.Get("love_street.reveal_open_possibilities") : Loc.Get("love_street.reveal_free_spirit")));
            if (!_revealedTraits[npc.ID].Contains("sensuality"))
                possible.Add(("sensuality", profile.Sensuality > 0.6f ? Loc.Get("love_street.reveal_deeply_passionate") : profile.Sensuality > 0.3f ? Loc.Get("love_street.reveal_warm_affectionate") : Loc.Get("love_street.reveal_reserved_genuine")));
            if (!_revealedTraits[npc.ID].Contains("preference"))
            {
                var pref = profile.RelationshipPref.ToString();
                possible.Add(("preference", Loc.Get("love_street.reveal_prefers", pref.ToLower())));
            }

            if (possible.Count > 0)
            {
                var reveal = possible[random.Next(possible.Count)];
                _revealedTraits[npc.ID].Add(reveal.key);

                terminal.WriteLine("");
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("love_street.over_drinks", npc.Name, reveal.text));
            }
        }

        await terminal.WaitForKey();
    }

    #endregion

    #region Dating

    private async Task TakeOnDate()
    {
        terminal.ClearScreen();
        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("love_street.take_date_header"), "bright_magenta", 77);
        terminal.WriteLine("");

        // Get potential dates (lovers, spouses, or NPCs with good relations)
        var romance = RomanceTracker.Instance;
        var potentialDates = new List<(string id, string name, string type)>();

        foreach (var spouse in romance.Spouses)
        {
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouse.NPCId);
            var name = npc?.Name ?? (!string.IsNullOrEmpty(spouse.NPCName) ? spouse.NPCName : spouse.NPCId);
            potentialDates.Add((spouse.NPCId, name, "Spouse"));
        }

        foreach (var lover in romance.CurrentLovers)
        {
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == lover.NPCId);
            var name = npc?.Name ?? (!string.IsNullOrEmpty(lover.NPCName) ? lover.NPCName : lover.NPCId);
            potentialDates.Add((lover.NPCId, name, "Lover"));
        }

        var friendlyNpcs = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => n.IsAlive && !n.IsDead)
            .Where(n => RelationshipSystem.GetRelationshipStatus(currentPlayer, n) <= 40)
            .Where(n => !potentialDates.Any(p => p.id == n.ID))
            .Take(5)
            .ToList() ?? new List<NPC>();

        foreach (var npc in friendlyNpcs)
        {
            potentialDates.Add((npc.ID, npc.Name, "Friend"));
        }

        if (potentialDates.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("love_street.no_date_partner"));
            terminal.WriteLine(Loc.Get("love_street.no_date_hint"));
            await terminal.WaitForKey();
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.who_date"));

        for (int i = 0; i < potentialDates.Count; i++)
        {
            var (id, name, type) = potentialDates[i];
            WriteSRMenuOption($"{i + 1}", $"{name} ({type})");
        }

        terminal.WriteLine("");
        WriteSRMenuOption("0", Loc.Get("ui.cancel"));
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("ui.choice"));
        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= potentialDates.Count)
        {
            var selected = potentialDates[choice - 1];
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == selected.id);
            if (npc == null)
            {
                npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n =>
                    n.Name.Equals(selected.name, StringComparison.OrdinalIgnoreCase) ||
                    n.Name2.Equals(selected.name, StringComparison.OrdinalIgnoreCase));
            }
            if (npc != null)
            {
                await GoOnDate(npc);
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("love_street.could_not_find", selected.name));
                await terminal.WaitForKey();
            }
        }
    }

    private async Task GoOnDate(NPC partner)
    {
        terminal.ClearScreen();
        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("love_street.date_with", partner.Name), "bright_magenta", 77);
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.where_take"));
        terminal.WriteLine("");

        WriteSRMenuOption("1", Loc.Get("love_street.date_dinner"));
        WriteSRMenuOption("2", Loc.Get("love_street.date_walk"));
        WriteSRMenuOption("3", Loc.Get("love_street.date_theater"));
        WriteSRMenuOption("4", Loc.Get("love_street.date_picnic"));
        WriteSRMenuOption("5", Loc.Get("love_street.date_dancing"));
        WriteSRMenuOption("0", Loc.Get("ui.cancel"));
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("love_street.choose_date_activity"));
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > 5)
        {
            terminal.WriteLine(Loc.Get("love_street.maybe_another_time"), "gray");
            await terminal.WaitForKey();
            return;
        }

        long cost = choice switch { 1 => 500, 3 => 1000, 4 => 200, 5 => 300, _ => 0 };
        if (currentPlayer.Gold < cost)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("love_street.need_date_gold", cost));
            await terminal.WaitForKey();
            return;
        }

        currentPlayer.Gold -= cost;
        if (cost > 0) currentPlayer.Statistics?.RecordGoldSpent(cost);

        await ProcessDateActivity(partner, choice);
    }

    private async Task ProcessDateActivity(NPC partner, int activity)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");

        switch (activity)
        {
            case 1:
                terminal.WriteLine($"\n                         {Loc.Get("love_street.date_dinner_title")}\n");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("love_street.date_dinner1", partner.Name));
                terminal.WriteLine(Loc.Get("love_street.date_dinner2"));
                terminal.WriteLine(Loc.Get("love_street.date_dinner3", partner.Name));
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("love_street.date_dinner4", partner.Name));
                RelationshipSystem.UpdateRelationship(currentPlayer, partner, 1, 5, false, true);
                currentPlayer.Experience += currentPlayer.Level * 30;
                break;

            case 2:
                terminal.WriteLine($"\n                         {Loc.Get("love_street.date_walk_title")}\n");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("love_street.date_walk1", partner.Name));
                terminal.WriteLine(Loc.Get("love_street.date_walk2"));
                terminal.WriteLine(Loc.Get("love_street.date_walk3", partner.Name));
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("love_street.date_walk4", partner.Name));
                RelationshipSystem.UpdateRelationship(currentPlayer, partner, 1, 3, false, true);
                currentPlayer.HP = Math.Min(currentPlayer.HP + currentPlayer.MaxHP / 10, currentPlayer.MaxHP);
                break;

            case 3:
                terminal.WriteLine($"\n                         {Loc.Get("love_street.date_theater_title")}\n");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("love_street.date_theater1", partner.Name));
                terminal.WriteLine(Loc.Get("love_street.date_theater2"));
                terminal.WriteLine(Loc.Get("love_street.date_theater3"));
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("love_street.date_theater4", partner.Name));
                RelationshipSystem.UpdateRelationship(currentPlayer, partner, 1, 6, false, true);
                currentPlayer.Experience += currentPlayer.Level * 50;
                break;

            case 4:
                terminal.WriteLine($"\n                         {Loc.Get("love_street.date_picnic_title")}\n");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("love_street.date_picnic1", partner.Name));
                terminal.WriteLine(Loc.Get("love_street.date_picnic2"));
                terminal.WriteLine(Loc.Get("love_street.date_picnic3", partner.Name));
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("love_street.date_picnic4", partner.Name));
                RelationshipSystem.UpdateRelationship(currentPlayer, partner, 1, 4, false, true);
                currentPlayer.Mana = Math.Min(currentPlayer.Mana + currentPlayer.MaxMana / 5, currentPlayer.MaxMana);
                break;

            case 5:
                terminal.WriteLine($"\n                         {Loc.Get("love_street.date_dancing_title")}\n");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("love_street.date_dance1", partner.Name));
                terminal.WriteLine(Loc.Get("love_street.date_dance2"));
                terminal.WriteLine(Loc.Get("love_street.date_dance3"));
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("love_street.date_dance4", partner.Name));
                RelationshipSystem.UpdateRelationship(currentPlayer, partner, 1, 5, false, true);
                currentPlayer.Experience += currentPlayer.Level * 40;
                break;
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("love_street.date_relation_improved", partner.Name));
        terminal.WriteLine("");

        // Chance for a kiss at the end
        float kissChance = 0.4f + GetCharismaModifier();
        if (random.NextDouble() < kissChance)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("love_street.date_kiss", partner.Name));
            terminal.WriteLine(Loc.Get("love_street.date_magical"));
            RelationshipSystem.UpdateRelationship(currentPlayer, partner, 1, 2, false, true);
        }

        await terminal.WaitForKey();

        // Check for intimacy invitation
        var romanceType = RomanceTracker.Instance.GetRelationType(partner.ID);
        int relationLevel = RelationshipSystem.GetRelationshipStatus(currentPlayer, partner);

        bool intimacyAvailable = _passionActive ||
            romanceType == RomanceRelationType.Lover ||
            romanceType == RomanceRelationType.Spouse ||
            romanceType == RomanceRelationType.FWB ||
            (relationLevel <= 20 && random.NextDouble() < 0.5);

        if (intimacyAvailable)
        {
            if (_passionActive)
            {
                _passionActive = false;
                terminal.ClearScreen();
                terminal.SetColor("magenta");
                terminal.WriteLine(Loc.Get("love_street.passion_warmth"));
                terminal.WriteLine("");
            }

            terminal.ClearScreen();
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("love_street.gaze_meaningful", partner.Name));
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("love_street.continue_private"));
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("love_street.accept_invitation"));
            terminal.WriteLine(Loc.Get("love_street.perhaps_another"));
            terminal.WriteLine("");

            var response = await terminal.GetKeyInput();
            if (response.ToUpper() == "Y")
            {
                await IntimacySystem.Instance.StartIntimateScene(
                    currentPlayer,
                    partner,
                    terminal
                );
            }
            else
            {
                terminal.SetColor("white");
                terminal.WriteLine($"\n{Loc.Get("love_street.another_time", partner.Name)}");
                await terminal.WaitForKey();
            }
        }
    }

    #endregion

    #region Gift Shop

    private async Task VisitGiftShop()
    {
        terminal.ClearScreen();
        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("love_street.gift_shop"), "bright_yellow", 77);
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.gift_welcome"));

        var gifts = new (string name, long cost, int boost)[]
        {
            ("Red Roses", 100, 3),
            ("Box of Chocolates", 200, 4),
            ("Bottle of Fine Wine", 500, 6),
            ("Exotic Perfume", 1000, 8),
            ("Silver Necklace", 2000, 10),
            ("Diamond Ring", 10000, 20),
            ("Enchanted Locket", 25000, 25),
            ("Moonstone Tiara", 50000, 30),
            ("Star of Eternity", 100000, 40)
        };

        for (int i = 0; i < gifts.Length; i++)
        {
            var (name, cost, boost) = gifts[i];
            string luxury = cost >= 25000 ? $" ({Loc.Get("love_street.luxury")})" : "";
            string afford = currentPlayer.Gold < cost ? $" ({Loc.Get("love_street.cant_afford")})" : "";
            WriteSRMenuOption($"{i + 1}", $"{name}, {cost:N0}g{luxury}{afford}");
        }

        terminal.WriteLine("");
        WriteSRMenuOption("0", Loc.Get("ui.leave_shop"));
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("love_street.gift_your_gold", $"{currentPlayer.Gold:N0}"));
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("love_street.what_buy"));
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > gifts.Length)
        {
            terminal.WriteLine(Loc.Get("love_street.come_back_shop"), "gray");
            await terminal.WaitForKey();
            return;
        }

        var (giftName, giftCost, relationBoost) = gifts[choice - 1];

        if (currentPlayer.Gold < giftCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("love_street.gift_too_expensive", $"{giftCost:N0}"));
            await terminal.WaitForKey();
            return;
        }

        // Show numbered list of NPCs to give to
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.gift_who_receive", giftName));

        var knownNPCs = (NPCSpawnSystem.Instance?.ActiveNPCs ?? new List<NPC>())
            .Where(n => n.IsAlive && !n.IsDead)
            .Where(n => RelationshipSystem.GetRelationshipStatus(currentPlayer, n) < 80)
            .OrderBy(n => RelationshipSystem.GetRelationshipStatus(currentPlayer, n))
            .Take(12)
            .ToList();

        if (knownNPCs.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("love_street.gift_no_one"));
            terminal.WriteLine(Loc.Get("love_street.gift_meet_people"));
            await terminal.WaitForKey();
            return;
        }

        for (int i = 0; i < knownNPCs.Count; i++)
        {
            var npc = knownNPCs[i];
            var romType = RomanceTracker.Instance.GetRelationType(npc.ID);
            string tag = romType switch
            {
                RomanceRelationType.Spouse => $" ({Loc.Get("love_street.tag_spouse")})",
                RomanceRelationType.Lover => $" ({Loc.Get("love_street.tag_lover")})",
                RomanceRelationType.FWB => $" ({Loc.Get("love_street.tag_fwb")})",
                _ => ""
            };

            WriteSRMenuOption($"{i + 1}", $"{npc.Name}{tag}");
        }

        terminal.WriteLine("");
        WriteSRMenuOption("0", Loc.Get("ui.cancel"));
        terminal.WriteLine("");

        var recipientInput = await terminal.GetInput(Loc.Get("love_street.give_to"));
        if (!int.TryParse(recipientInput, out int recipChoice) || recipChoice < 1 || recipChoice > knownNPCs.Count)
        {
            terminal.WriteLine(Loc.Get("love_street.changed_mind"), "gray");
            await terminal.WaitForKey();
            return;
        }

        var recipient = knownNPCs[recipChoice - 1];

        // Show reaction preview
        int currentRelation = RelationshipSystem.GetRelationshipStatus(currentPlayer, recipient);
        bool isAttracted = recipient.Brain?.Personality?.IsAttractedTo(
            currentPlayer.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male) ?? true;

        terminal.WriteLine("");
        terminal.SetColor("gray");
        if (isAttracted && currentRelation <= 40)
            terminal.WriteLine(Loc.Get("love_street.gift_would_love", recipient.Name));
        else if (isAttracted && currentRelation <= 60)
            terminal.WriteLine(Loc.Get("love_street.gift_appreciate", recipient.Name));
        else if (!isAttracted)
            terminal.WriteLine(Loc.Get("love_street.gift_not_romantic", recipient.Name));
        else
            terminal.WriteLine(Loc.Get("love_street.gift_not_accept", recipient.Name));

        terminal.WriteLine("");
        var confirm = await terminal.GetInput(Loc.Get("love_street.gift_buy_confirm", giftName, recipient.Name, $"{giftCost:N0}"));
        if (string.IsNullOrEmpty(confirm) || confirm.ToUpper() != "Y")
        {
            terminal.WriteLine(Loc.Get("love_street.maybe_next_time"), "gray");
            await terminal.WaitForKey();
            return;
        }

        // Process gift
        currentPlayer.Gold -= giftCost;
        currentPlayer.Statistics?.RecordGoldSpent(giftCost);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("love_street.gift_purchase", giftName, recipient.Name));
        terminal.WriteLine("");

        if (isAttracted && currentRelation <= 60)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("love_street.gift_eyes_light", recipient.Name));
            terminal.WriteLine(Loc.Get("love_street.gift_reaction_love", giftName));
            RelationshipSystem.UpdateRelationship(currentPlayer, recipient, 1, relationBoost, false, true);
        }
        else if (currentRelation <= 80)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("love_street.gift_accepts_politely", recipient.Name));
            terminal.WriteLine(Loc.Get("love_street.gift_reaction_polite"));
            RelationshipSystem.UpdateRelationship(currentPlayer, recipient, 1, relationBoost / 2, false, false);
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("love_street.gift_uncomfortable", recipient.Name));
            terminal.WriteLine(Loc.Get("love_street.gift_reaction_reject"));
            terminal.WriteLine(Loc.Get("love_street.gift_walks_away"));
        }

        await terminal.WaitForKey();
    }

    #endregion

    #region Gossip (Overhauled)

    private async Task Gossip()
    {
        terminal.ClearScreen();
        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("love_street.gossip_header"), "magenta", 77);
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("love_street.gossip_crone"));
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("love_street.gossip_welcome"));
        terminal.WriteLine("");

        WriteSRMenuOption("1", Loc.Get("love_street.gossip_menu_together", GameConfig.LoveStreetGossipCostBasic));
        WriteSRMenuOption("2", Loc.Get("love_street.gossip_menu_scandals", GameConfig.LoveStreetGossipCostScandals));
        WriteSRMenuOption("3", Loc.Get("love_street.gossip_menu_available", GameConfig.LoveStreetGossipCostBasic));
        WriteSRMenuOption("4", Loc.Get("love_street.gossip_menu_investigate", GameConfig.LoveStreetGossipCostInvestigate));
        WriteSRMenuOption("0", Loc.Get("ui.leave"));
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("love_street.gossip_seek"));

        switch (input)
        {
            case "1":
                if (SpendGossipGold(GameConfig.LoveStreetGossipCostBasic))
                    await GossipWhoseTogether();
                break;
            case "2":
                if (SpendGossipGold(GameConfig.LoveStreetGossipCostScandals))
                    await GossipScandals();
                break;
            case "3":
                if (SpendGossipGold(GameConfig.LoveStreetGossipCostBasic))
                    await GossipWhosAvailable();
                break;
            case "4":
                if (SpendGossipGold(GameConfig.LoveStreetGossipCostInvestigate))
                    await GossipInvestigate();
                break;
        }

        await terminal.WaitForKey();
    }

    private bool SpendGossipGold(long cost)
    {
        if (currentPlayer.Gold >= cost)
        {
            currentPlayer.Gold -= cost;
            currentPlayer.Statistics?.RecordGoldSpent(cost);
            return true;
        }
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("love_street.gossip_no_gold"));
        return false;
    }

    private async Task GossipWhoseTogether()
    {
        terminal.ClearScreen();
        terminal.SetColor("magenta");
        terminal.WriteLine(Loc.Get("love_street.gossip_tangled"));

        int count = 0;

        // Player marriages
        var playerRomance = RomanceTracker.Instance;
        if (playerRomance.Spouses.Count > 0)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("love_street.gossip_marriages"));
            terminal.SetColor("white");
            foreach (var spouse in playerRomance.Spouses)
            {
                var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouse.NPCId);
                var name = npc?.Name ?? (!string.IsNullOrEmpty(spouse.NPCName) ? spouse.NPCName : spouse.NPCId);
                var days = spouse.MarriedGameDay > 0
                    ? Math.Max(0, DailySystemManager.Instance.CurrentDay - spouse.MarriedGameDay)
                    : (DateTime.Now - spouse.MarriedDate).Days; // Fallback for old saves
                terminal.WriteLine(Loc.Get("love_street.gossip_marriage_line", currentPlayer.Name2, name, days, spouse.Children));
                count++;
            }
            terminal.WriteLine("");
        }

        // NPC-NPC marriages from registry
        var marriages = NPCMarriageRegistry.Instance?.GetAllMarriages() ?? new List<NPCMarriageData>();
        if (marriages.Count > 0)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("love_street.gossip_npc_couples"));
            terminal.SetColor("white");
            foreach (var marriage in marriages.Take(15))
            {
                var npc1 = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == marriage.Npc1Id);
                var npc2 = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == marriage.Npc2Id);
                string name1 = npc1?.Name ?? marriage.Npc1Id;
                string name2 = npc2?.Name ?? marriage.Npc2Id;
                terminal.WriteLine(Loc.Get("love_street.gossip_npc_couple", name1, name2));
                count++;
            }
        }

        if (count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("love_street.gossip_no_couples"));
        }
    }

    private async Task GossipScandals()
    {
        terminal.ClearScreen();
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("love_street.gossip_juicy"));

        var allAffairs = NPCMarriageRegistry.Instance?.GetAllAffairs() ?? new List<AffairState>();
        var juicy = allAffairs.Where(a => a.SpouseSuspicion > 0 || a.IsActive).ToList();

        int count = 0;

        if (juicy.Count > 0)
        {
            terminal.SetColor("white");
            foreach (var affair in juicy.Take(10))
            {
                var married = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == affair.MarriedNpcId);
                var seducer = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == affair.SeducerId);
                string marriedName = married?.Name ?? affair.MarriedNpcId;
                string seducerName = seducer?.Name ?? affair.SeducerId;

                // Find the spouse
                string spouseId = NPCMarriageRegistry.Instance?.GetSpouseId(affair.MarriedNpcId) ?? "";
                var spouse = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouseId);
                string spouseName = spouse?.Name ?? Loc.Get("love_street.someone");

                terminal.SetColor("bright_red");
                terminal.Write(" ! ");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("love_street.gossip_affair", marriedName, seducerName));
                terminal.SetColor("gray");
                terminal.Write($"   {Loc.Get("love_street.gossip_behind")} ");
                terminal.SetColor("yellow");
                terminal.Write(spouseName);
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("love_street.gossip_back"));

                if (affair.SpouseSuspicion > 60)
                {
                    terminal.SetColor("red");
                    terminal.Write($" {Loc.Get("love_street.gossip_suspicious")}");
                }
                if (affair.IsActive)
                {
                    terminal.SetColor("bright_red");
                    terminal.Write($" {Loc.Get("love_street.gossip_full_affair")}");
                }
                terminal.WriteLine("");
                terminal.WriteLine("");
                count++;
            }
        }

        // Also check recent Love Street news for color
        var recentNews = NewsSystem.Instance.GetTodaysNews(GameConfig.NewsCategory.General);
        var scandalous = recentNews.Where(n =>
            n.Contains("Love Street") || n.Contains("unfaithful") ||
            n.Contains("disease") || n.Contains("affair")).Take(5).ToList();

        if (scandalous.Count > 0)
        {
            terminal.SetColor("magenta");
            terminal.WriteLine(Loc.Get("love_street.gossip_recent"));
            terminal.SetColor("gray");
            foreach (var news in scandalous)
            {
                terminal.WriteLine($" - {news}");
                count++;
            }
        }

        if (count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("love_street.gossip_no_scandals"));
        }
    }

    private async Task GossipWhosAvailable()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("love_street.gossip_available"));

        var playerGender = currentPlayer.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male;

        // Asexual players get a message instead of singles list
        if (currentPlayer.Orientation == SexualOrientation.Asexual)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("love_street.gossip_asexual"));
            return;
        }

        var singles = (NPCSpawnSystem.Instance?.ActiveNPCs ?? new List<NPC>())
            .Where(n => n.IsAlive && !n.IsDead)
            .Where(n => NPCMarriageRegistry.Instance?.IsMarriedToNPC(n.ID) != true)
            .Where(n => RomanceTracker.Instance.GetRelationType(n.ID) == RomanceRelationType.None)
            .Where(n => n.Brain?.Personality?.IsAttractedTo(playerGender) == true)
            .Where(n => IsPlayerAttractedTo(n)) // Also filter by player's orientation
            .OrderBy(n => RelationshipSystem.GetRelationshipStatus(currentPlayer, n))
            .Take(10)
            .ToList();

        if (singles.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("love_street.gossip_no_singles"));
            return;
        }

        terminal.SetColor("white");
        foreach (var npc in singles)
        {
            int relation = RelationshipSystem.GetRelationshipStatus(currentPlayer, npc);
            var profile = npc.Brain?.Personality;

            terminal.SetColor("bright_yellow");
            terminal.Write($" * ");
            terminal.SetColor("white");
            terminal.Write($"{npc.Name}");
            terminal.SetColor("gray");
            terminal.Write($" (Lv{npc.Level} {npc.Race} {npc.ClassName})");

            if (profile != null)
            {
                terminal.SetColor("darkgray");
                terminal.Write($" - {GetPersonalityHint(profile)}");
            }

            terminal.SetColor(relation <= 40 ? "bright_green" : relation <= 60 ? "yellow" : "gray");
            terminal.Write($" [{GetRelationDescription(relation)}]");
            terminal.WriteLine("");
        }

        terminal.SetColor("gray");
        terminal.WriteLine($"\n{Loc.Get("love_street.gossip_eligible_count", singles.Count)}");
    }

    private async Task GossipInvestigate()
    {
        terminal.ClearScreen();
        terminal.SetColor("magenta");
        terminal.WriteLine(Loc.Get("love_street.gossip_who_investigate"));

        var npcs = (NPCSpawnSystem.Instance?.ActiveNPCs ?? new List<NPC>())
            .Where(n => n.IsAlive && !n.IsDead)
            .Take(15)
            .ToList();

        if (npcs.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("love_street.gossip_nobody"));
            return;
        }

        for (int i = 0; i < npcs.Count; i++)
        {
            WriteSRMenuOption($"{i + 1}", $"{npcs[i].Name}, Lv{npcs[i].Level} {npcs[i].Race}");
        }

        terminal.WriteLine("");
        var input = await terminal.GetInput(Loc.Get("love_street.investigate_who"));
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > npcs.Count)
            return;

        var npc = npcs[choice - 1];
        terminal.ClearScreen();
        terminal.SetColor("magenta");
        terminal.WriteLine(Loc.Get("love_street.gossip_dig_up", npc.Name));

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.gossip_name", npc.Name));
        terminal.WriteLine(Loc.Get("love_street.gossip_level_info", npc.Level, npc.Race, npc.Class, npc.Age));
        terminal.WriteLine("");

        var profile = npc.Brain?.Personality;
        if (profile != null)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("love_street.gossip_personality"));
            terminal.SetColor("white");

            // Orientation
            terminal.Write($"   {Loc.Get("love_street.label_orientation")}: ");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(GetOrientationDescription(profile));

            // Romanticism
            terminal.SetColor("white");
            terminal.Write($"   {Loc.Get("love_street.label_romance")}: ");
            terminal.SetColor(profile.Romanticism > 0.6f ? "bright_magenta" : "gray");
            terminal.WriteLine(profile.Romanticism > 0.7f ? Loc.Get("love_street.trait_hopeless_romantic") :
                              profile.Romanticism > 0.4f ? Loc.Get("love_street.trait_enjoys_romance") : Loc.Get("love_street.trait_pragmatic"));

            // Commitment
            terminal.SetColor("white");
            terminal.Write($"   {Loc.Get("love_street.label_commitment")}: ");
            terminal.SetColor(profile.Commitment > 0.6f ? "bright_green" : "yellow");
            terminal.WriteLine(profile.Commitment > 0.7f ? Loc.Get("love_street.trait_deeply_loyal") :
                              profile.Commitment > 0.4f ? Loc.Get("love_street.trait_open_commitment") : Loc.Get("love_street.trait_values_freedom"));

            // Sensuality
            terminal.SetColor("white");
            terminal.Write($"   {Loc.Get("love_street.label_passion")}: ");
            terminal.SetColor(profile.Sensuality > 0.6f ? "bright_red" : "gray");
            terminal.WriteLine(profile.Sensuality > 0.7f ? Loc.Get("love_street.trait_intensely_passionate") :
                              profile.Sensuality > 0.4f ? Loc.Get("love_street.trait_warm_affectionate") : Loc.Get("love_street.trait_reserved"));

            // Flirtatiousness
            terminal.SetColor("white");
            terminal.Write($"   {Loc.Get("love_street.label_flirtiness")}: ");
            terminal.SetColor(profile.Flirtatiousness > 0.6f ? "bright_yellow" : "gray");
            terminal.WriteLine(profile.Flirtatiousness > 0.7f ? Loc.Get("love_street.trait_very_flirtatious") :
                              profile.Flirtatiousness > 0.4f ? Loc.Get("love_street.trait_somewhat_coy") : Loc.Get("love_street.trait_shy"));
        }

        terminal.WriteLine("");

        // Relationship status
        terminal.SetColor("white");
        terminal.Write($" {Loc.Get("love_street.label_status")}: ");
        bool isMarried = NPCMarriageRegistry.Instance?.IsMarriedToNPC(npc.ID) == true;
        if (isMarried)
        {
            var spouseId = NPCMarriageRegistry.Instance?.GetSpouseId(npc.ID);
            var spouse = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouseId);
            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("love_street.gossip_married_to", spouse?.Name ?? "someone"));
        }
        else
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("love_street.gossip_single"));
        }

        // Their opinion of player
        int relation = RelationshipSystem.GetRelationshipStatus(currentPlayer, npc);
        terminal.SetColor("white");
        terminal.Write($" {Loc.Get("love_street.label_thinks_of_you")}: ");
        terminal.SetColor(relation <= 20 ? "bright_magenta" : relation <= 50 ? "bright_green" : relation <= 70 ? "yellow" : "red");
        terminal.WriteLine(GetRelationDescription(relation));

        // Compatibility
        if (profile != null)
        {
            var playerGender = currentPlayer.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male;
            bool npcAttracted = profile.IsAttractedTo(playerGender);
            bool playerAttracted = IsPlayerAttractedTo(npc);
            bool attracted = npcAttracted && playerAttracted;
            terminal.SetColor("white");
            terminal.Write($" {Loc.Get("love_street.label_compatibility")}: ");
            if (!attracted)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("love_street.gossip_not_interested"));
            }
            else if (relation <= 30)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("love_street.gossip_excellent"));
            }
            else if (relation <= 50)
            {
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("love_street.gossip_good"));
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("love_street.gossip_needs_work"));
            }
        }
    }

    #endregion

    #region Love Potions (replaces Romance Stats)

    private async Task LovePotions()
    {
        terminal.ClearScreen();
        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("love_street.potions_header"), "magenta", 77);
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("love_street.potion_crone"));
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("love_street.potion_welcome"));
        terminal.WriteLine("");

        WriteSRMenuOption("1", Loc.Get("love_street.potion_menu_charm", GameConfig.LoveStreetCharmPotionCost, GameConfig.LoveStreetCharmBonus) + (_charmActive ? $" ({Loc.Get("love_street.active")})" : ""));
        WriteSRMenuOption("2", Loc.Get("love_street.potion_menu_allure", GameConfig.LoveStreetAllurePotionCost) + (_allureActive ? $" ({Loc.Get("love_street.active")})" : ""));
        WriteSRMenuOption("3", Loc.Get("love_street.potion_menu_forget", GameConfig.LoveStreetForgetPotionCost, GameConfig.LoveStreetJealousyReduction));
        WriteSRMenuOption("4", Loc.Get("love_street.potion_menu_passion", GameConfig.LoveStreetPassionPotionCost) + (_passionActive ? $" ({Loc.Get("love_street.active")})" : ""));
        WriteSRMenuOption("5", Loc.Get("love_street.romance_stats"));
        WriteSRMenuOption("0", Loc.Get("ui.leave"));
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("love_street.potion_your_gold", $"{currentPlayer.Gold:N0}"));
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("love_street.what_catches_eye"));
        switch (input)
        {
            case "1":
                await BuyCharmPotion();
                break;
            case "2":
                await BuyAllurePotion();
                break;
            case "3":
                await BuyForgetPotion();
                break;
            case "4":
                await BuyPassionPotion();
                break;
            case "5":
                await ShowRomanceStats();
                break;
        }
    }

    private async Task BuyCharmPotion()
    {
        if (_charmActive)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("love_street.potion_already_charm"));
            await terminal.WaitForKey();
            return;
        }
        if (currentPlayer.Gold < GameConfig.LoveStreetCharmPotionCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("love_street.potion_charm_cost", GameConfig.LoveStreetCharmPotionCost));
            await terminal.WaitForKey();
            return;
        }

        currentPlayer.Gold -= GameConfig.LoveStreetCharmPotionCost;
        currentPlayer.Statistics?.RecordGoldSpent(GameConfig.LoveStreetCharmPotionCost);
        _charmActive = true;

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("love_street.potion_charm_drink"));
        terminal.WriteLine(Loc.Get("love_street.potion_charm_effect", GameConfig.LoveStreetCharmBonus));
        await terminal.WaitForKey();
    }

    private async Task BuyAllurePotion()
    {
        if (_allureActive)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("love_street.potion_already_allure"));
            await terminal.WaitForKey();
            return;
        }
        if (currentPlayer.Gold < GameConfig.LoveStreetAllurePotionCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("love_street.potion_allure_cost", GameConfig.LoveStreetAllurePotionCost));
            await terminal.WaitForKey();
            return;
        }

        currentPlayer.Gold -= GameConfig.LoveStreetAllurePotionCost;
        currentPlayer.Statistics?.RecordGoldSpent(GameConfig.LoveStreetAllurePotionCost);
        _allureActive = true;

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("love_street.potion_allure_drink"));
        terminal.WriteLine(Loc.Get("love_street.potion_allure_effect"));
        await terminal.WaitForKey();
    }

    private async Task BuyForgetPotion()
    {
        if (currentPlayer.Gold < GameConfig.LoveStreetForgetPotionCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("love_street.potion_forget_cost", GameConfig.LoveStreetForgetPotionCost));
            await terminal.WaitForKey();
            return;
        }

        currentPlayer.Gold -= GameConfig.LoveStreetForgetPotionCost;
        currentPlayer.Statistics?.RecordGoldSpent(GameConfig.LoveStreetForgetPotionCost);

        // Reduce jealousy on all partners
        var jealousy = RomanceTracker.Instance.JealousyLevels;
        int reduced = 0;
        foreach (var key in jealousy.Keys.ToList())
        {
            if (jealousy[key] > 0)
            {
                jealousy[key] = Math.Max(0, jealousy[key] - GameConfig.LoveStreetJealousyReduction);
                reduced++;
            }
        }

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("love_street.potion_forget_drink"));
        if (reduced > 0)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("love_street.potion_forget_effect", reduced, reduced > 1 ? "s" : ""));
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("love_street.potion_forget_wasted"));
        }
        await terminal.WaitForKey();
    }

    private async Task BuyPassionPotion()
    {
        if (_passionActive)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("love_street.potion_already_passion"));
            await terminal.WaitForKey();
            return;
        }
        if (currentPlayer.Gold < GameConfig.LoveStreetPassionPotionCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("love_street.potion_passion_cost", GameConfig.LoveStreetPassionPotionCost));
            await terminal.WaitForKey();
            return;
        }

        currentPlayer.Gold -= GameConfig.LoveStreetPassionPotionCost;
        currentPlayer.Statistics?.RecordGoldSpent(GameConfig.LoveStreetPassionPotionCost);
        _passionActive = true;

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("love_street.potion_passion_drink"));
        terminal.WriteLine(Loc.Get("love_street.potion_passion_effect"));
        await terminal.WaitForKey();
    }

    private async Task ShowRomanceStats()
    {
        terminal.ClearScreen();
        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("love_street.romantic_life"), "bright_magenta", 77);
        terminal.WriteLine("");

        var romance = RomanceTracker.Instance;

        // Spouses
        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("love_street.summary_spouses", romance.Spouses.Count));
        if (romance.Spouses.Count > 0)
        {
            terminal.SetColor("white");
            foreach (var spouse in romance.Spouses)
            {
                var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouse.NPCId);
                var name = npc?.Name ?? (!string.IsNullOrEmpty(spouse.NPCName) ? spouse.NPCName : spouse.NPCId);
                var days = spouse.MarriedGameDay > 0
                    ? Math.Max(0, DailySystemManager.Instance.CurrentDay - spouse.MarriedGameDay)
                    : (DateTime.Now - spouse.MarriedDate).Days; // Fallback for old saves
                terminal.WriteLine(Loc.Get("love_street.summary_spouse_line", name, days, spouse.Children));
            }
        }
        terminal.WriteLine("");

        // Lovers
        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("love_street.summary_lovers", romance.CurrentLovers.Count));
        if (romance.CurrentLovers.Count > 0)
        {
            terminal.SetColor("white");
            foreach (var lover in romance.CurrentLovers)
            {
                var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == lover.NPCId);
                var name = npc?.Name ?? (!string.IsNullOrEmpty(lover.NPCName) ? lover.NPCName : lover.NPCId);
                var days = (DateTime.Now - lover.RelationshipStart).Days;
                terminal.WriteLine(Loc.Get("love_street.summary_lover_line", name, days));
            }
        }
        terminal.WriteLine("");

        // FWB
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("love_street.summary_fwb", romance.FriendsWithBenefits.Count));
        terminal.WriteLine("");

        // Exes
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("love_street.summary_exes", romance.Exes.Count));
        terminal.WriteLine("");

        // Summary
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("love_street.summary_encounters", romance.EncounterHistory.Count));
        terminal.WriteLine(Loc.Get("love_street.summary_married_times", currentPlayer.MarriedTimes));
        terminal.WriteLine(Loc.Get("love_street.summary_children", currentPlayer.Children));
        terminal.WriteLine("");

        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("love_street.summary_darkness", currentPlayer.Darkness));

        await terminal.WaitForKey();
    }

    #endregion

    #region Helper Methods

    private string GetRiskLevel(float chance)
    {
        return chance switch
        {
            >= 0.30f => Loc.Get("love_street.risk_high"),
            >= 0.20f => Loc.Get("love_street.risk_medium"),
            >= 0.10f => Loc.Get("love_street.risk_low"),
            _ => Loc.Get("love_street.risk_very_low")
        };
    }

    private float GetCharismaModifier()
    {
        long charisma = currentPlayer.Charisma + (_charmActive ? GameConfig.LoveStreetCharmBonus : 0);
        if (charisma >= 20) return 0.25f;
        if (charisma >= 16) return 0.15f;
        if (charisma >= 12) return 0.05f;
        if (charisma >= 8) return -0.05f;
        return -0.20f;
    }

    private void GiveDarkness(Character player, int amount)
    {
        player.Darkness += amount;
    }

    /// <summary>
    /// Check if the player is attracted to an NPC based on player orientation
    /// </summary>
    private bool IsPlayerAttractedTo(NPC npc)
    {
        var npcGender = npc.Brain?.Personality?.Gender ?? (npc.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male);
        var playerGender = currentPlayer.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male;

        return currentPlayer.Orientation switch
        {
            SexualOrientation.Straight => (playerGender == GenderIdentity.Male && PersonalityProfile.IsFemalePresenting(npcGender)) ||
                                          (playerGender == GenderIdentity.Female && PersonalityProfile.IsMalePresenting(npcGender)),
            SexualOrientation.Gay or SexualOrientation.Lesbian =>
                                     (playerGender == GenderIdentity.Male && PersonalityProfile.IsMalePresenting(npcGender)) ||
                                     (playerGender == GenderIdentity.Female && PersonalityProfile.IsFemalePresenting(npcGender)),
            SexualOrientation.Bisexual or SexualOrientation.Pansexual or SexualOrientation.Demisexual => true,
            SexualOrientation.Asexual => false,
            _ => true
        };
    }

    private string GetRelationDescription(int level)
    {
        return level switch
        {
            <= 10 => Loc.Get("love_street.rel_deeply_in_love"),
            <= 25 => Loc.Get("love_street.rel_very_fond"),
            <= 40 => Loc.Get("love_street.rel_friendly"),
            <= 50 => Loc.Get("love_street.rel_neutral"),
            <= 65 => Loc.Get("love_street.rel_wary"),
            <= 80 => Loc.Get("love_street.rel_dislikes"),
            _ => Loc.Get("love_street.rel_hostile")
        };
    }

    private string GetPersonalityHint(PersonalityProfile profile)
    {
        // Return the most prominent trait
        var traits = new List<(float val, string desc)>
        {
            (profile.Flirtatiousness, Loc.Get("love_street.hint_flirtatious")),
            (profile.Romanticism, Loc.Get("love_street.hint_romantic")),
            (profile.Sensuality, Loc.Get("love_street.hint_passionate")),
            (profile.Tenderness, Loc.Get("love_street.hint_gentle")),
            (profile.Commitment, Loc.Get("love_street.hint_devoted")),
            (1f - profile.Commitment, Loc.Get("love_street.hint_free_spirited")),
        };

        // Only consider traits above 0.5 to avoid weird descriptions
        var strong = traits.Where(t => t.val > 0.5f).OrderByDescending(t => t.val).ToList();
        if (strong.Count > 0)
            return Loc.Get("love_street.seems_trait", strong[0].desc);

        return Loc.Get("love_street.seems_reserved");
    }

    private string GetOrientationDescription(PersonalityProfile profile)
    {
        return profile.Orientation switch
        {
            SexualOrientation.Straight => Loc.Get("love_street.orient_straight"),
            SexualOrientation.Gay => Loc.Get("love_street.orient_same_sex"),
            SexualOrientation.Lesbian => Loc.Get("love_street.orient_same_sex"),
            SexualOrientation.Bisexual => Loc.Get("love_street.orient_all"),
            SexualOrientation.Pansexual => Loc.Get("love_street.orient_all"),
            SexualOrientation.Asexual => Loc.Get("love_street.orient_asexual"),
            SexualOrientation.Demisexual => Loc.Get("love_street.orient_demisexual"),
            _ => Loc.Get("love_street.orient_unknown")
        };
    }

    #endregion
}

#region Data Classes

public class Courtesan
{
    public string Name { get; }
    public string Race { get; }
    public long Price { get; }
    public float DiseaseChance { get; }
    public string Description { get; }
    public string IntroText { get; }

    public Courtesan(string name, string race, long price, float diseaseChance, string description, string introText)
    {
        Name = name;
        Race = race;
        Price = price;
        DiseaseChance = diseaseChance;
        Description = description;
        IntroText = introText;
    }
}

public class Gigolo
{
    public string Name { get; }
    public string Race { get; }
    public long Price { get; }
    public float DiseaseChance { get; }
    public string Description { get; }
    public string IntroText { get; }

    public Gigolo(string name, string race, long price, float diseaseChance, string description, string introText)
    {
        Name = name;
        Race = race;
        Price = price;
        DiseaseChance = diseaseChance;
        Description = description;
        IntroText = introText;
    }
}

#endregion
