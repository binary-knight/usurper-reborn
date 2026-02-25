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
    private Random random = new Random();

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

    protected override void DisplayLocation()
    {
        if (DoorMode.IsInDoorMode) { DisplayLocationBBS(); return; }

        terminal.ClearScreen();

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"<3 LOVE STREET <3".PadLeft((77 + 17) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("The air is thick with perfume and promise. Lanterns cast a warm, inviting glow");
        terminal.WriteLine("over the cobblestones. Beautiful creatures of all races beckon from doorways,");
        terminal.WriteLine("while well-dressed patrons move between the various establishments.");
        terminal.WriteLine("");

        // Show NPCs present
        ShowNPCsInLocation();

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"-= PLEASURE AWAITS =-".PadLeft((77 + 21) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Pleasure Houses
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("1");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(" Beauty Nest     ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("2");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(" Hall of Dreams");

        terminal.WriteLine("");

        // Social Options
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("M");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(" Mingle          ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("D");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(" Take on a Date");

        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("G");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(" Gift Shop       ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("V");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(" Gossip");

        terminal.WriteLine("");

        // Potions & Return
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("L");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(" Love Potions    ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(" Return to Main Street");

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("+---------------------------------------------------------------------------+");
        terminal.WriteLine("");

        // Show gold + active potions
        terminal.SetColor("yellow");
        terminal.Write($" Gold: {currentPlayer.Gold:N0}");
        if (_charmActive || _allureActive || _passionActive)
        {
            terminal.SetColor("magenta");
            terminal.Write("  Potions:");
            if (_charmActive) terminal.Write(" [Charm]");
            if (_allureActive) terminal.Write(" [Allure]");
            if (_passionActive) terminal.Write(" [Passion]");
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
        ShowBBSHeader("LOVE STREET");

        // 1-line description
        terminal.SetColor("magenta");
        terminal.WriteLine(" Perfume and promise fill the air. Beautiful creatures beckon from doorways.");

        ShowBBSNPCs();
        terminal.WriteLine("");

        // Menu rows
        ShowBBSMenuRow(("1", "bright_red", "BeautyNest"), ("2", "bright_blue", "HallDreams"), ("M", "yellow", "Mingle"), ("D", "green", "Date"));
        ShowBBSMenuRow(("G", "cyan", "Gifts"), ("V", "magenta", "Gossip"), ("L", "cyan", "Potions"), ("R", "red", "Return"));

        // Gold + active potions
        terminal.SetColor("gray");
        terminal.Write(" Gold:");
        terminal.SetColor("yellow");
        terminal.Write($"{currentPlayer.Gold:N0}");
        if (_charmActive || _allureActive || _passionActive)
        {
            terminal.SetColor("magenta");
            if (_charmActive) terminal.Write(" [Charm]");
            if (_allureActive) terminal.Write(" [Allure]");
            if (_passionActive) terminal.Write(" [Passion]");
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
        terminal.SetColor("bright_red");
        terminal.WriteLine("+---------------------------------------------------------------------------+");
        terminal.WriteLine("|                     THE BEAUTY NEST                                         |");
        terminal.WriteLine("|              Driven by Clarissa the Half-Elf                                |");
        terminal.WriteLine("+---------------------------------------------------------------------------+");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("As you enter the worn-down old establishment at the end of Love Street,");
        terminal.WriteLine("you notice creatures of all kinds moving up and down the stairs,");
        terminal.WriteLine("each in the company of beautiful companions.");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine("A fat, ugly troll woman appears - Clarissa, the madam.");
        terminal.SetColor("yellow");
        terminal.WriteLine("\"Looking for some pleasure, handsome?\"");
        terminal.WriteLine("");

        await ShowCourtesanMenu();
    }

    private async Task ShowCourtesanMenu()
    {
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("Clarissa introduces you to the courtesans:");
        terminal.WriteLine("");

        for (int i = 0; i < Courtesans.Count; i++)
        {
            var c = Courtesans[i];
            terminal.SetColor("bright_yellow");
            terminal.Write($" {i + 1}) ");
            terminal.SetColor("bright_magenta");
            terminal.Write($"{c.Name}");
            terminal.SetColor("gray");
            terminal.Write($" ({c.Race}) - ");
            terminal.SetColor("yellow");
            terminal.Write($"{c.Price:N0} gold");

            // Disease risk indicator
            terminal.SetColor("darkgray");
            terminal.Write(" [Risk: ");
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
            terminal.WriteLine("");
        }

        terminal.SetColor("gray");
        terminal.WriteLine(" 0) Return");
        terminal.WriteLine("");

        var input = await terminal.GetInput("Choose a companion (1-9, 0 to leave): ");
        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= Courtesans.Count)
        {
            await EngageWithCourtesan(Courtesans[choice - 1]);
        }
    }

    private async Task EngageWithCourtesan(Courtesan courtesan)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"\n+---------------------------------------------------------------------------+");
        terminal.WriteLine($"  {courtesan.Name} the {courtesan.Race}");
        terminal.WriteLine($"+---------------------------------------------------------------------------+\n");

        terminal.SetColor("white");
        terminal.WriteLine(courtesan.IntroText);
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine($"\"{courtesan.Name} wants to see {courtesan.Price:N0} gold before you can proceed.\"");
        terminal.WriteLine("");

        var confirm = await terminal.GetInput($"Pay {courtesan.Name} {courtesan.Price:N0} gold? (Y/N): ");
        if (string.IsNullOrEmpty(confirm) || confirm.ToUpper() != "Y")
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"\"{courtesan.Name} shrugs. \"Your loss, honey.\"");
            await terminal.WaitForKey();
            return;
        }

        if (currentPlayer.Gold < courtesan.Price)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"\"You don't have enough gold!\" {courtesan.Name} laughs cruelly.");
            terminal.WriteLine("\"Come back when you can actually afford me, peasant!\"");
            terminal.WriteLine("");
            terminal.WriteLine("You slink away in embarrassment...");
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
        terminal.SetColor("bright_blue");
        terminal.WriteLine("+---------------------------------------------------------------------------+");
        terminal.WriteLine("|                      HALL OF DREAMS                                         |");
        terminal.WriteLine("|              Supervised by Giovanni the Gnome                               |");
        terminal.WriteLine("+---------------------------------------------------------------------------+");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("You enter the lobby of this grand and fashionable establishment.");
        terminal.WriteLine("The furnishings are expensive, giving you the feeling of a luxury hotel.");
        terminal.WriteLine("Subdued music adds to the pleasant atmosphere.");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine("A slim man dressed in black approaches.");
        terminal.SetColor("yellow");
        terminal.WriteLine("\"Hello, I am Giovanni. What can I do for you today?\"");
        terminal.WriteLine("");

        await ShowGigoloMenu();
    }

    private async Task ShowGigoloMenu()
    {
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("Giovanni introduces you to the gigolos:");
        terminal.WriteLine("");

        for (int i = 0; i < Gigolos.Count; i++)
        {
            var g = Gigolos[i];
            terminal.SetColor("bright_yellow");
            terminal.Write($" {i + 1}) ");
            terminal.SetColor("bright_blue");
            terminal.Write($"{g.Name}");
            terminal.SetColor("gray");
            terminal.Write($" ({g.Race}) - ");
            terminal.SetColor("yellow");
            terminal.Write($"{g.Price:N0} gold");

            // Disease risk indicator
            terminal.SetColor("darkgray");
            terminal.Write(" [Risk: ");
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
            terminal.WriteLine("");
        }

        terminal.SetColor("gray");
        terminal.WriteLine(" 0) Return");
        terminal.WriteLine("");

        var input = await terminal.GetInput("Choose a companion (1-9, 0 to leave): ");
        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= Gigolos.Count)
        {
            await EngageWithGigolo(Gigolos[choice - 1]);
        }
    }

    private async Task EngageWithGigolo(Gigolo gigolo)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_blue");
        terminal.WriteLine($"\n+---------------------------------------------------------------------------+");
        terminal.WriteLine($"  {gigolo.Name} the {gigolo.Race}");
        terminal.WriteLine($"+---------------------------------------------------------------------------+\n");

        terminal.SetColor("white");
        terminal.WriteLine(gigolo.IntroText);
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine($"\"{gigolo.Name} requires {gigolo.Price:N0} gold for his services.\"");
        terminal.WriteLine("");

        var confirm = await terminal.GetInput($"Pay {gigolo.Name} {gigolo.Price:N0} gold? (Y/N): ");
        if (string.IsNullOrEmpty(confirm) || confirm.ToUpper() != "Y")
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"\"{gigolo.Name} bows politely. \"Perhaps another time.\"");
            await terminal.WaitForKey();
            return;
        }

        if (currentPlayer.Gold < gigolo.Price)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"\"I'm afraid you lack the funds,\" {gigolo.Name} says with a sigh.");
            terminal.WriteLine("\"Return when your purse is heavier.\"");
            terminal.WriteLine("");
            terminal.WriteLine("You leave, disappointed...");
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
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("\n+---------------------------------------------------------------------------+");
        terminal.WriteLine("                              A Night of Passion");
        terminal.WriteLine("+---------------------------------------------------------------------------+\n");

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
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("+---------------------------------------------------------------------------+");
        terminal.WriteLine($" ** Night of Pleasure with {partnerName} **");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine($" Experience gained: {xpGained:N0}");
        terminal.SetColor("red");
        terminal.WriteLine($" Darkness points: +{darkPoints}");

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
            $"The door closes behind you with a soft click. {partnerName} approaches slowly...",
            $"Candles flicker as {partnerName} leads you to the bed...",
            $"The room smells of exotic incense. {partnerName} begins to undress...",
            $"Soft music plays as {partnerName} pulls you close...",
            $"The silk sheets rustle as you and {partnerName} embrace..."
        };
        terminal.WriteLine(openings[random.Next(openings.Length)]);
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("bright_magenta");
        var tensions = new[]
        {
            $"Your heart pounds as {partnerName}'s skilled hands begin to explore your body...",
            $"{partnerName}'s lips find yours in a passionate kiss that steals your breath...",
            $"The warmth of {partnerName}'s body against yours sends shivers down your spine...",
            $"Every touch from {partnerName} ignites fires you didn't know existed within you..."
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
            $"Hours later, you lay exhausted but satisfied. {partnerName} traces lazy patterns on your skin...",
            $"Dawn breaks through the curtains. {partnerName} sleeps peacefully beside you...",
            $"You catch your breath, every nerve still tingling from {partnerName}'s attentions...",
            $"A deep satisfaction fills you as {partnerName} whispers sweet nothings in your ear..."
        };
        terminal.WriteLine(aftermaths[random.Next(aftermaths.Length)]);
        await Task.Delay(500);
    }

    private async Task ShowBasicEncounter(string name, string race)
    {
        terminal.SetColor("white");
        var scenes = new[]
        {
            $"{name} wastes no time. The encounter is brief but intense, leaving you gasping.",
            $"There's an efficiency to {name}'s movements. Quick, skilled, satisfying.",
            $"{name} knows exactly what to do. Within the hour, you're breathless and spent.",
            $"The experience is hurried but {name}'s expertise makes every moment count."
        };
        terminal.WriteLine(scenes[random.Next(scenes.Length)]);
        terminal.WriteLine("");

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"{name} moves against you with practiced rhythm. Your moans fill the small room");
        terminal.WriteLine("as pleasure builds and crests in waves. The climax, when it comes, is sharp and sudden.");
        await Task.Delay(500);
    }

    private async Task ShowStandardEncounter(string name, string race)
    {
        terminal.SetColor("white");
        terminal.WriteLine($"{name} takes time to ensure your comfort, adjusting pillows and lighting more candles.");
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"Slowly, {name} removes your clothing piece by piece, kissing each newly exposed area.");
        terminal.WriteLine($"Your skin burns wherever {name}'s lips touch. The anticipation is exquisite torture.");
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("white");
        terminal.WriteLine($"When {name} finally joins with you, it's like fire meeting ice - explosive and");
        terminal.WriteLine("transformative. You lose yourself in sensation, crying out without shame.");
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"{name} brings you to the edge again and again, only to pull back at the last moment.");
        terminal.WriteLine("When release finally comes, it crashes through you like a thunderstorm.");
        await Task.Delay(500);
    }

    private async Task ShowHighEndEncounter(string name, string race)
    {
        terminal.SetColor("white");
        terminal.WriteLine($"{name} offers you fine wine before beginning. 'To relax,' {name} explains with a smile.");
        terminal.WriteLine("The vintage is excellent. Already, you feel warm and pliant.");
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"A sensual massage begins your evening. {name}'s oiled hands work magic on your");
        terminal.WriteLine("tired muscles, finding knots of tension and dissolving them with expert pressure.");
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("white");
        terminal.WriteLine($"The massage transitions seamlessly into something more intimate. {name}'s touches");
        terminal.WriteLine("become deliberately provocative, exploring territories that make you gasp.");
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"You are brought to the heights of ecstasy multiple times. {name} seems to know");
        terminal.WriteLine("your body better than you know it yourself, playing you like a finely tuned instrument.");
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("white");
        terminal.WriteLine($"The final release is shattering. You scream {name}'s name as waves of pleasure");
        terminal.WriteLine("crash through every fiber of your being. Time loses all meaning.");
        await Task.Delay(500);
    }

    private async Task ShowPremiumEncounter(string name, string race)
    {
        terminal.SetColor("white");
        terminal.WriteLine($"The evening begins with a private bath. {name} joins you in the steaming water,");
        terminal.WriteLine("their body pressing against yours as skilled hands wash away the world's concerns.");
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"In the bath, {name}'s hands find intimate places. You bite your lip to stifle moans");
        terminal.WriteLine("as pleasure builds in the warm water. But this is only the prelude.");
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("white");
        terminal.WriteLine($"Dried and anointed with exotic oils, you're led to a bed strewn with rose petals.");
        terminal.WriteLine($"{name} produces silk scarves. 'Trust me?' The question hangs in the air.");
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("Blindfolded, your other senses heighten impossibly. Every touch is electric,");
        terminal.WriteLine($"every whispered word from {name} sends shivers cascading through your body.");
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("white");
        terminal.WriteLine($"{name} worships your body with lips, tongue, and fingers. You lose count of how");
        terminal.WriteLine("many times you cry out in ecstasy. The night becomes an endless ocean of pleasure.");
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"When {name} finally allows the final release, it's transcendent. Your back arches,");
        terminal.WriteLine("your vision goes white, and for a moment you touch something divine.");
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("white");
        terminal.WriteLine("You float back to consciousness slowly, held in warm arms, utterly transformed.");
        terminal.WriteLine($"\"You were magnificent,\" {name} whispers. \"Come back to me soon.\"");
        await Task.Delay(500);
    }

    private async Task CheckForDisease(string partnerName, float diseaseChance)
    {
        float roll = (float)random.NextDouble();

        if (roll < diseaseChance)
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine("+---------------------------------------------------------------------------+");
            terminal.WriteLine("                         SOMETHING IS WRONG!");
            terminal.WriteLine("+---------------------------------------------------------------------------+");
            terminal.WriteLine("");
            terminal.WriteLine("As you leave, you start to feel pain in your nether regions!");
            terminal.WriteLine("By the gods! You've been infected with a venereal disease!");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine("You rush to the Healer, crying for help...");
            terminal.WriteLine("The monks give you foul-smelling potions and painful treatments.");
            terminal.WriteLine("");

            currentPlayer.HP = Math.Max(1, currentPlayer.HP / 2);
            currentPlayer.LoversBane = true;

            terminal.SetColor("bright_red");
            terminal.WriteLine("You've contracted 'Lover's Bane'!");
            terminal.WriteLine("Your HP has been halved! Visit the Healer to cure this affliction.");
            terminal.WriteLine("");

            NewsSystem.Instance.Newsy(true,
                $"{currentPlayer.Name2} contracted a disease at Love Street!",
                $"The pleasure houses claim another victim...");

            MailSystem.SendSystemMail(currentPlayer.Name2, "Disease Notice",
                "Medical Emergency",
                $"You contracted a sexual disease from {partnerName}.",
                "Seek treatment at the Healer immediately!");

            await Task.Delay(2000);
        }
    }

    private void GenerateNews(string partnerName)
    {
        if (random.Next(3) == 0)
        {
            var headlines = new[]
            {
                $"{currentPlayer.Name2} spent a steamy night at Love Street.",
                $"{currentPlayer.Name2} was seen leaving {partnerName}'s chambers.",
                $"Witnesses report {currentPlayer.Name2} at the pleasure district.",
                $"{currentPlayer.Name2} proves to be quite the romantic adventurer!"
            };
            NewsSystem.Instance.Newsy(true, headlines[random.Next(headlines.Length)]);
        }
    }

    private void NotifySpouse(string partnerName)
    {
        var spouses = RomanceTracker.Instance.Spouses;
        foreach (var spouse in spouses)
        {
            MailSystem.SendSystemMail(spouse.NPCId, "HOW COULD THEY?",
                "INFIDELITY!",
                $"{currentPlayer.Name2} has been unfaithful to you!",
                $"They enjoyed themselves with {partnerName} at Love Street!");

            RomanceTracker.Instance.JealousyLevels[spouse.NPCId] =
                Math.Min(100, RomanceTracker.Instance.JealousyLevels.GetValueOrDefault(spouse.NPCId, 0) + 20);
        }
    }

    #endregion

    #region Mingle (replaces Meet NPCs)

    private async Task Mingle()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("\n+---------------------------------------------------------------------------+");
        terminal.WriteLine("                              MINGLE");
        terminal.WriteLine("+---------------------------------------------------------------------------+\n");

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
            terminal.WriteLine("There's no one around to mingle with right now.");
            terminal.WriteLine("Try coming back later.");
            await terminal.WaitForKey();
            return;
        }

        // Show NPC list with romance-relevant info
        if (npcsHere.Count > 0)
        {
            terminal.SetColor("white");
            terminal.WriteLine("People at Love Street:\n");
        }

        for (int i = 0; i < allNPCs.Count; i++)
        {
            var npc = allNPCs[i];
            if (i == npcsHere.Count && npcsHere.Count > 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine("Others around town:\n");
            }

            var romanceType = RomanceTracker.Instance.GetRelationType(npc.ID);
            int relationLevel = RelationshipSystem.GetRelationshipStatus(currentPlayer, npc);

            terminal.SetColor("bright_yellow");
            terminal.Write($" [{i + 1}] ");
            terminal.SetColor("white");
            terminal.Write($"{npc.Name}");
            terminal.SetColor("gray");
            terminal.Write($" (Lv{npc.Level} {npc.Race} {npc.Class})");

            // Relationship tag
            string tag = romanceType switch
            {
                RomanceRelationType.Spouse => " [Spouse]",
                RomanceRelationType.Lover => " [Lover]",
                RomanceRelationType.FWB => " [FWB]",
                _ => relationLevel <= 40 ? " [Friend]" : relationLevel <= 70 ? "" : " [Wary]"
            };
            if (!string.IsNullOrEmpty(tag))
            {
                terminal.SetColor(romanceType == RomanceRelationType.Spouse ? "bright_red" :
                                  romanceType == RomanceRelationType.Lover ? "bright_magenta" :
                                  romanceType == RomanceRelationType.FWB ? "cyan" :
                                  tag == " [Friend]" ? "bright_green" : "darkgray");
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

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(" [0] Return");
        terminal.WriteLine("");

        var input = await terminal.GetInput("Who catches your eye? ");
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
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"\n+---------------------------------------------------------------------------+");
            terminal.WriteLine($"  Spending time with {npc.Name}");
            terminal.WriteLine($"+---------------------------------------------------------------------------+\n");

            // Show NPC details
            var romanceType = RomanceTracker.Instance.GetRelationType(npc.ID);
            int relationLevel = RelationshipSystem.GetRelationshipStatus(currentPlayer, npc);

            terminal.SetColor("white");
            terminal.Write($" {npc.Name}");
            terminal.SetColor("gray");
            terminal.WriteLine($" - Level {npc.Level} {npc.Race} {npc.Class}, Age {npc.Age}");

            terminal.SetColor("white");
            terminal.Write(" Relationship: ");
            terminal.SetColor(relationLevel <= 20 ? "bright_magenta" : relationLevel <= 50 ? "bright_green" : relationLevel <= 70 ? "yellow" : "red");
            terminal.WriteLine(GetRelationDescription(relationLevel));

            // Show revealed traits
            if (_revealedTraits.TryGetValue(npc.ID, out var traits) && traits.Count > 0)
            {
                terminal.SetColor("cyan");
                terminal.Write(" Traits: ");
                terminal.SetColor("white");
                terminal.WriteLine(string.Join(", ", traits));
            }

            bool alreadyFlirted = _flirtedThisVisit.Contains(npc.ID);

            terminal.WriteLine("");
            if (!alreadyFlirted)
            {
                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_magenta");
                terminal.Write("F");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine("Flirt");
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine(" [F] Flirt (already tried)");
            }

            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_green");
            terminal.Write("C");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Compliment");

            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("yellow");
            terminal.Write("B");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine($"Buy a Drink ({GameConfig.LoveStreetDrinkCost}g)");

            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_cyan");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Ask on a Date");

            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("gray");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Talk");

            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("red");
            terminal.Write("0");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Back");

            terminal.WriteLine("");

            var action = await terminal.GetInput("What do you do? ");
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
            terminal.WriteLine("\nYou've already tried flirting with them this visit.");
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
                terminal.WriteLine("The Elixir of Allure surges through you...");
            }

            RelationshipSystem.UpdateRelationship(currentPlayer, npc, 1, boost, false, true);

            // Flavor text based on personality
            var flirtResponses = new[]
            {
                $"{npc.Name} blushes and looks away with a smile.",
                $"{npc.Name} laughs warmly. \"You're quite charming, you know.\"",
                $"{npc.Name}'s eyes light up. \"Well, aren't you bold!\"",
                $"A smile plays across {npc.Name}'s lips. \"I like your style.\"",
                $"{npc.Name} leans closer. \"Tell me more about yourself...\"",
                $"{npc.Name} touches your arm gently. \"You're fun to be around.\""
            };
            terminal.SetColor("bright_green");
            terminal.WriteLine(flirtResponses[random.Next(flirtResponses.Length)]);
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"Your relationship with {npc.Name} has improved!");
        }
        else
        {
            var failResponses = new[]
            {
                $"{npc.Name} gives you an awkward smile and changes the subject.",
                $"\"That's... sweet,\" {npc.Name} says unconvincingly.",
                $"{npc.Name} raises an eyebrow. \"Nice try.\"",
                $"An uncomfortable silence follows. {npc.Name} coughs politely.",
                $"{npc.Name} pretends not to notice your advance."
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
            $"\"You have the most captivating eyes, {npc.Name}.\"",
            $"\"Your smile could light up the darkest dungeon, {npc.Name}.\"",
            $"\"I've never met anyone as interesting as you, {npc.Name}.\""
        };

        // Add race-specific
        switch (npc.Race.ToString())
        {
            case "Elf":
                compliments.Add($"\"Your elven grace is truly mesmerizing, {npc.Name}.\"");
                break;
            case "Dwarf":
                compliments.Add($"\"You've got more strength and character than most, {npc.Name}.\"");
                break;
            case "Human":
                compliments.Add($"\"There's something wonderfully warm about you, {npc.Name}.\"");
                break;
            default:
                compliments.Add($"\"You're unlike anyone I've ever met, {npc.Name}.\"");
                break;
        }

        string compliment = compliments[random.Next(compliments.Count)];

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(compliment);
        terminal.WriteLine("");

        RelationshipSystem.UpdateRelationship(currentPlayer, npc, 1, GameConfig.LoveStreetComplimentBoost, false, true);

        var reactions = new[]
        {
            $"{npc.Name} smiles appreciatively. \"How kind of you.\"",
            $"{npc.Name} nods graciously. \"You have a way with words.\"",
            $"A faint blush colors {npc.Name}'s cheeks. \"Thank you.\""
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
            terminal.WriteLine($"\nYou need {GameConfig.LoveStreetDrinkCost} gold to buy a drink.");
            await terminal.WaitForKey();
            return;
        }

        currentPlayer.Gold -= GameConfig.LoveStreetDrinkCost;
        currentPlayer.Statistics?.RecordGoldSpent(GameConfig.LoveStreetDrinkCost);

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine($"You signal the barkeep and two drinks appear. {npc.Name} accepts gratefully.");
        terminal.WriteLine("");

        RelationshipSystem.UpdateRelationship(currentPlayer, npc, 1, GameConfig.LoveStreetDrinkBoost, false, true);

        terminal.SetColor("bright_green");
        terminal.WriteLine($"Your relationship with {npc.Name} has improved!");

        // Reveal a personality trait
        var profile = npc.Brain?.Personality;
        if (profile != null)
        {
            if (!_revealedTraits.ContainsKey(npc.ID))
                _revealedTraits[npc.ID] = new HashSet<string>();

            var possible = new List<(string key, string text)>();
            if (!_revealedTraits[npc.ID].Contains("romanticism"))
                possible.Add(("romanticism", profile.Romanticism > 0.6f ? "a hopeless romantic" : profile.Romanticism > 0.3f ? "open to romance" : "quite practical about love"));
            if (!_revealedTraits[npc.ID].Contains("commitment"))
                possible.Add(("commitment", profile.Commitment > 0.6f ? "seeking commitment" : profile.Commitment > 0.3f ? "open to possibilities" : "a free spirit"));
            if (!_revealedTraits[npc.ID].Contains("sensuality"))
                possible.Add(("sensuality", profile.Sensuality > 0.6f ? "deeply passionate" : profile.Sensuality > 0.3f ? "warm and affectionate" : "reserved but genuine"));
            if (!_revealedTraits[npc.ID].Contains("preference"))
            {
                var pref = profile.RelationshipPref.ToString();
                possible.Add(("preference", $"prefers {pref.ToLower()} relationships"));
            }

            if (possible.Count > 0)
            {
                var reveal = possible[random.Next(possible.Count)];
                _revealedTraits[npc.ID].Add(reveal.key);

                terminal.WriteLine("");
                terminal.SetColor("cyan");
                terminal.WriteLine($"Over drinks, you learn that {npc.Name} is {reveal.text}.");
            }
        }

        await terminal.WaitForKey();
    }

    #endregion

    #region Dating

    private async Task TakeOnDate()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("\n+---------------------------------------------------------------------------+");
        terminal.WriteLine("                           TAKE SOMEONE ON A DATE");
        terminal.WriteLine("+---------------------------------------------------------------------------+\n");

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
            terminal.WriteLine("You don't have anyone to take on a date.");
            terminal.WriteLine("Try mingling and improving your relationships first!");
            await terminal.WaitForKey();
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine("Who would you like to take on a date?\n");

        for (int i = 0; i < potentialDates.Count; i++)
        {
            var (id, name, type) = potentialDates[i];
            terminal.SetColor("bright_yellow");
            terminal.Write($" [{i + 1}] ");
            terminal.SetColor(type == "Spouse" ? "bright_red" : type == "Lover" ? "bright_magenta" : "white");
            terminal.Write($"{name}");
            terminal.SetColor("gray");
            terminal.WriteLine($" ({type})");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(" [0] Cancel");
        terminal.WriteLine("");

        var input = await terminal.GetInput("Choose: ");
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
                terminal.WriteLine($"Could not find {selected.name} in town. They may have left.");
                await terminal.WaitForKey();
            }
        }
    }

    private async Task GoOnDate(NPC partner)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"\n+---------------------------------------------------------------------------+");
        terminal.WriteLine($"  A Date with {partner.Name}");
        terminal.WriteLine($"+---------------------------------------------------------------------------+\n");

        terminal.SetColor("white");
        terminal.WriteLine("Where would you like to take them?\n");

        terminal.WriteLine(" [1] Romantic Dinner (500 gold) - Fine dining and conversation");
        terminal.WriteLine(" [2] Moonlit Walk (free) - Stroll together and hold hands");
        terminal.WriteLine(" [3] Theater Performance (1000 gold) - Watch a show together");
        terminal.WriteLine(" [4] Picnic by the Lake (200 gold) - Quiet time in nature");
        terminal.WriteLine(" [5] Dancing at the Inn (300 gold) - Dance the night away");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(" [0] Cancel");
        terminal.WriteLine("");

        var input = await terminal.GetInput("Choose your date activity: ");
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > 5)
        {
            terminal.WriteLine("Maybe another time...", "gray");
            await terminal.WaitForKey();
            return;
        }

        long cost = choice switch { 1 => 500, 3 => 1000, 4 => 200, 5 => 300, _ => 0 };
        if (currentPlayer.Gold < cost)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"You need {cost} gold for this date activity!");
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
                terminal.WriteLine("\n                         * Romantic Dinner *\n");
                terminal.SetColor("white");
                terminal.WriteLine($"You take {partner.Name} to the finest restaurant in town.");
                terminal.WriteLine("Candlelight flickers across the table as you share stories.");
                terminal.WriteLine($"{partner.Name} laughs at your jokes, eyes sparkling.");
                terminal.WriteLine("");
                terminal.WriteLine($"\"This is wonderful,\" {partner.Name} says, reaching for your hand.");
                RelationshipSystem.UpdateRelationship(currentPlayer, partner, 1, 5, false, true);
                currentPlayer.Experience += currentPlayer.Level * 30;
                break;

            case 2:
                terminal.WriteLine("\n                         * Moonlit Walk *\n");
                terminal.SetColor("white");
                terminal.WriteLine($"Hand in hand, you and {partner.Name} stroll through the quiet streets.");
                terminal.WriteLine("The moon casts silver light on everything, making the world magical.");
                terminal.WriteLine($"{partner.Name} rests their head on your shoulder as you walk.");
                terminal.WriteLine("");
                terminal.WriteLine($"\"I could do this forever,\" {partner.Name} whispers.");
                RelationshipSystem.UpdateRelationship(currentPlayer, partner, 1, 3, false, true);
                currentPlayer.HP = Math.Min(currentPlayer.HP + currentPlayer.MaxHP / 10, currentPlayer.MaxHP);
                break;

            case 3:
                terminal.WriteLine("\n                         * Theater Performance *\n");
                terminal.SetColor("white");
                terminal.WriteLine($"You and {partner.Name} watch an enchanting performance.");
                terminal.WriteLine("During the emotional scenes, you feel their hand squeeze yours.");
                terminal.WriteLine("The music and drama move you both deeply.");
                terminal.WriteLine("");
                terminal.WriteLine($"\"Thank you for this,\" {partner.Name} says, eyes glistening.");
                RelationshipSystem.UpdateRelationship(currentPlayer, partner, 1, 6, false, true);
                currentPlayer.Experience += currentPlayer.Level * 50;
                break;

            case 4:
                terminal.WriteLine("\n                         * Picnic by the Lake *\n");
                terminal.SetColor("white");
                terminal.WriteLine($"You spread a blanket by the water's edge with {partner.Name}.");
                terminal.WriteLine("The sun sets beautifully as you share food and wine.");
                terminal.WriteLine($"{partner.Name} moves closer, seeking your warmth.");
                terminal.WriteLine("");
                terminal.WriteLine($"\"This is perfect,\" {partner.Name} murmurs contentedly.");
                RelationshipSystem.UpdateRelationship(currentPlayer, partner, 1, 4, false, true);
                currentPlayer.Mana = Math.Min(currentPlayer.Mana + currentPlayer.MaxMana / 5, currentPlayer.MaxMana);
                break;

            case 5:
                terminal.WriteLine("\n                         * Dancing at the Inn *\n");
                terminal.SetColor("white");
                terminal.WriteLine($"You lead {partner.Name} onto the dance floor at the Inn.");
                terminal.WriteLine("The music is lively, and you spin together, laughing.");
                terminal.WriteLine("As the tempo slows, you pull them close.");
                terminal.WriteLine("");
                terminal.WriteLine($"{partner.Name} gazes into your eyes. \"You dance well.\"");
                RelationshipSystem.UpdateRelationship(currentPlayer, partner, 1, 5, false, true);
                currentPlayer.Experience += currentPlayer.Level * 40;
                break;
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"Your relationship with {partner.Name} has improved!");
        terminal.WriteLine("");

        // Chance for a kiss at the end
        float kissChance = 0.4f + GetCharismaModifier();
        if (random.NextDouble() < kissChance)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"As the date ends, {partner.Name} leans in for a tender kiss.");
            terminal.WriteLine("The moment is magical...");
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
                terminal.WriteLine("\nThe Passion Potion's warmth spreads through you both...");
                terminal.WriteLine("");
            }

            terminal.ClearScreen();
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("");
            terminal.WriteLine($"{partner.Name} gazes at you meaningfully...");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine($"\"Would you like to... continue this somewhere more private?\"");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine("(Y) Accept their invitation");
            terminal.WriteLine("(N) Perhaps another time");
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
                terminal.WriteLine($"\n{partner.Name} smiles understandingly. \"Another time, then.\"");
                await terminal.WaitForKey();
            }
        }
    }

    #endregion

    #region Gift Shop

    private async Task VisitGiftShop()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("\n+---------------------------------------------------------------------------+");
        terminal.WriteLine("                           LOVE STREET GIFT SHOP");
        terminal.WriteLine("+---------------------------------------------------------------------------+\n");

        terminal.SetColor("white");
        terminal.WriteLine("\"Welcome, welcome! Looking for something special for someone special?\"\n");

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
            terminal.SetColor("bright_yellow");
            terminal.Write($" [{i + 1}] ");
            terminal.SetColor(currentPlayer.Gold >= cost ? "white" : "darkgray");
            terminal.Write($"{name}");
            terminal.SetColor("gray");
            terminal.Write($" ({cost:N0}g)");
            if (cost >= 25000)
            {
                terminal.SetColor("magenta");
                terminal.Write(" *Luxury*");
            }
            terminal.WriteLine("");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(" [0] Leave shop");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine($" Your gold: {currentPlayer.Gold:N0}");
        terminal.WriteLine("");

        var input = await terminal.GetInput("What would you like to buy? ");
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > gifts.Length)
        {
            terminal.WriteLine("\"Come back when you're ready to shop!\"", "gray");
            await terminal.WaitForKey();
            return;
        }

        var (giftName, giftCost, relationBoost) = gifts[choice - 1];

        if (currentPlayer.Gold < giftCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"\"That costs {giftCost:N0} gold! You don't have enough.\"");
            await terminal.WaitForKey();
            return;
        }

        // Show numbered list of NPCs to give to
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine($"Who should receive the {giftName}?\n");

        var knownNPCs = (NPCSpawnSystem.Instance?.ActiveNPCs ?? new List<NPC>())
            .Where(n => n.IsAlive && !n.IsDead)
            .Where(n => RelationshipSystem.GetRelationshipStatus(currentPlayer, n) < 80)
            .OrderBy(n => RelationshipSystem.GetRelationshipStatus(currentPlayer, n))
            .Take(12)
            .ToList();

        if (knownNPCs.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("You don't know anyone well enough to give gifts to.");
            terminal.WriteLine("Try meeting and talking to people first!");
            await terminal.WaitForKey();
            return;
        }

        for (int i = 0; i < knownNPCs.Count; i++)
        {
            var npc = knownNPCs[i];
            var romType = RomanceTracker.Instance.GetRelationType(npc.ID);
            string tag = romType switch
            {
                RomanceRelationType.Spouse => " (Spouse)",
                RomanceRelationType.Lover => " (Lover)",
                RomanceRelationType.FWB => " (FWB)",
                _ => ""
            };

            terminal.SetColor("bright_yellow");
            terminal.Write($" [{i + 1}] ");
            terminal.SetColor("white");
            terminal.Write($"{npc.Name}");
            terminal.SetColor("gray");
            terminal.WriteLine(tag);
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(" [0] Cancel");
        terminal.WriteLine("");

        var recipientInput = await terminal.GetInput("Give to: ");
        if (!int.TryParse(recipientInput, out int recipChoice) || recipChoice < 1 || recipChoice > knownNPCs.Count)
        {
            terminal.WriteLine("\"Changed your mind? No problem.\"", "gray");
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
            terminal.WriteLine($"  (You think {recipient.Name} would love this!)");
        else if (isAttracted && currentRelation <= 60)
            terminal.WriteLine($"  (You think {recipient.Name} would appreciate this.)");
        else if (!isAttracted)
            terminal.WriteLine($"  ({recipient.Name} might not see this as romantic...)");
        else
            terminal.WriteLine($"  ({recipient.Name} might not accept this from you...)");

        terminal.WriteLine("");
        var confirm = await terminal.GetInput($"Buy {giftName} for {recipient.Name}? ({giftCost:N0}g) (Y/N): ");
        if (string.IsNullOrEmpty(confirm) || confirm.ToUpper() != "Y")
        {
            terminal.WriteLine("\"Maybe next time!\"", "gray");
            await terminal.WaitForKey();
            return;
        }

        // Process gift
        currentPlayer.Gold -= giftCost;
        currentPlayer.Statistics?.RecordGoldSpent(giftCost);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"\nYou purchase {giftName} and present it to {recipient.Name}.");
        terminal.WriteLine("");

        if (isAttracted && currentRelation <= 60)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine($"{recipient.Name}'s eyes light up with joy!");
            terminal.WriteLine($"\"Oh, {giftName}! How thoughtful of you!\"");
            RelationshipSystem.UpdateRelationship(currentPlayer, recipient, 1, relationBoost, false, true);
        }
        else if (currentRelation <= 80)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"{recipient.Name} accepts the gift politely.");
            terminal.WriteLine($"\"Thank you, that's... nice of you.\"");
            RelationshipSystem.UpdateRelationship(currentPlayer, recipient, 1, relationBoost / 2, false, false);
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{recipient.Name} looks uncomfortable.");
            terminal.WriteLine($"\"I... can't accept this from you.\"");
            terminal.WriteLine("They walk away, leaving the gift behind.");
        }

        await terminal.WaitForKey();
    }

    #endregion

    #region Gossip (Overhauled)

    private async Task Gossip()
    {
        terminal.ClearScreen();
        terminal.SetColor("magenta");
        terminal.WriteLine("\n+---------------------------------------------------------------------------+");
        terminal.WriteLine("                           MADAME WHISPERS' GOSSIP");
        terminal.WriteLine("+---------------------------------------------------------------------------+\n");

        terminal.SetColor("gray");
        terminal.WriteLine("An ancient crone beckons you into a shadowy alcove.");
        terminal.SetColor("yellow");
        terminal.WriteLine("\"Come, come... Madame Whispers knows all the secrets of the heart.\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($" [1] Who's Together ({GameConfig.LoveStreetGossipCostBasic}g) - All the couples");
        terminal.WriteLine($" [2] Juicy Scandals ({GameConfig.LoveStreetGossipCostScandals}g) - Affairs and intrigue");
        terminal.WriteLine($" [3] Who's Available ({GameConfig.LoveStreetGossipCostBasic}g) - Singles looking for love");
        terminal.WriteLine($" [4] Investigate Someone ({GameConfig.LoveStreetGossipCostInvestigate}g) - Deep secrets");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(" [0] Leave");
        terminal.WriteLine("");

        var input = await terminal.GetInput("What secrets do you seek? ");

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
        terminal.WriteLine("\"No gold, no gossip!\" Madame Whispers cackles.");
        return false;
    }

    private async Task GossipWhoseTogether()
    {
        terminal.ClearScreen();
        terminal.SetColor("magenta");
        terminal.WriteLine("\n\"Ah, the tangled webs of love...\"\n");

        int count = 0;

        // Player marriages
        var playerRomance = RomanceTracker.Instance;
        if (playerRomance.Spouses.Count > 0)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("Your marriages:");
            terminal.SetColor("white");
            foreach (var spouse in playerRomance.Spouses)
            {
                var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouse.NPCId);
                var name = npc?.Name ?? (!string.IsNullOrEmpty(spouse.NPCName) ? spouse.NPCName : spouse.NPCId);
                var days = (DateTime.Now - spouse.MarriedDate).Days;
                terminal.WriteLine($" <3 {currentPlayer.Name2} & {name} ({days} days, {spouse.Children} children)");
                count++;
            }
            terminal.WriteLine("");
        }

        // NPC-NPC marriages from registry
        var marriages = NPCMarriageRegistry.Instance?.GetAllMarriages() ?? new List<NPCMarriageData>();
        if (marriages.Count > 0)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("NPC couples:");
            terminal.SetColor("white");
            foreach (var marriage in marriages.Take(15))
            {
                var npc1 = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == marriage.Npc1Id);
                var npc2 = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == marriage.Npc2Id);
                string name1 = npc1?.Name ?? marriage.Npc1Id;
                string name2 = npc2?.Name ?? marriage.Npc2Id;
                terminal.WriteLine($" <3 {name1} & {name2}");
                count++;
            }
        }

        if (count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("\"Not many couples in the realm at the moment... how sad!\"");
        }
    }

    private async Task GossipScandals()
    {
        terminal.ClearScreen();
        terminal.SetColor("red");
        terminal.WriteLine("\n\"Oh, you want the JUICY stuff...\"\n");

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
                string spouseName = spouse?.Name ?? "someone";

                terminal.SetColor("bright_red");
                terminal.Write(" ! ");
                terminal.SetColor("white");
                terminal.WriteLine($"Word is {marriedName} has been seen with {seducerName}");
                terminal.SetColor("gray");
                terminal.Write("   behind ");
                terminal.SetColor("yellow");
                terminal.Write(spouseName);
                terminal.SetColor("gray");
                terminal.Write("'s back!");

                if (affair.SpouseSuspicion > 60)
                {
                    terminal.SetColor("red");
                    terminal.Write(" The spouse is getting suspicious...");
                }
                if (affair.IsActive)
                {
                    terminal.SetColor("bright_red");
                    terminal.Write(" Full-blown affair!");
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
            terminal.WriteLine("Recent rumors:");
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
            terminal.WriteLine("\"Everyone's been remarkably well-behaved lately... how boring!\"");
        }
    }

    private async Task GossipWhosAvailable()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("\n\"Looking for love? Let me see who's available...\"\n");

        var playerGender = currentPlayer.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male;

        var singles = (NPCSpawnSystem.Instance?.ActiveNPCs ?? new List<NPC>())
            .Where(n => n.IsAlive && !n.IsDead)
            .Where(n => NPCMarriageRegistry.Instance?.IsMarriedToNPC(n.ID) != true)
            .Where(n => RomanceTracker.Instance.GetRelationType(n.ID) == RomanceRelationType.None)
            .Where(n => n.Brain?.Personality?.IsAttractedTo(playerGender) == true)
            .OrderBy(n => RelationshipSystem.GetRelationshipStatus(currentPlayer, n))
            .Take(10)
            .ToList();

        if (singles.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("\"Slim pickings right now, I'm afraid. Everyone's taken!\"");
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
            terminal.Write($" (Lv{npc.Level} {npc.Race} {npc.Class})");

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
        terminal.WriteLine($"\n\"That's {singles.Count} eligible soul{(singles.Count != 1 ? "s" : "")} for you, dearie!\"");
    }

    private async Task GossipInvestigate()
    {
        terminal.ClearScreen();
        terminal.SetColor("magenta");
        terminal.WriteLine("\n\"Who do you want me to look into?\"\n");

        var npcs = (NPCSpawnSystem.Instance?.ActiveNPCs ?? new List<NPC>())
            .Where(n => n.IsAlive && !n.IsDead)
            .Take(15)
            .ToList();

        if (npcs.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("\"Nobody to investigate right now...\"");
            return;
        }

        for (int i = 0; i < npcs.Count; i++)
        {
            terminal.SetColor("bright_yellow");
            terminal.Write($" [{i + 1}] ");
            terminal.SetColor("white");
            terminal.Write($"{npcs[i].Name}");
            terminal.SetColor("gray");
            terminal.WriteLine($" (Lv{npcs[i].Level} {npcs[i].Race})");
        }

        terminal.WriteLine("");
        var input = await terminal.GetInput("Investigate who? ");
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > npcs.Count)
            return;

        var npc = npcs[choice - 1];
        terminal.ClearScreen();
        terminal.SetColor("magenta");
        terminal.WriteLine($"\n\"Ah, {npc.Name}... let me dig up what I know...\"\n");

        terminal.SetColor("white");
        terminal.WriteLine($" Name: {npc.Name}");
        terminal.WriteLine($" Level {npc.Level} {npc.Race} {npc.Class}, Age {npc.Age}");
        terminal.WriteLine("");

        var profile = npc.Brain?.Personality;
        if (profile != null)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(" Personality Secrets:");
            terminal.SetColor("white");

            // Orientation
            terminal.Write("   Orientation: ");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(GetOrientationDescription(profile));

            // Romanticism
            terminal.SetColor("white");
            terminal.Write("   Romance: ");
            terminal.SetColor(profile.Romanticism > 0.6f ? "bright_magenta" : "gray");
            terminal.WriteLine(profile.Romanticism > 0.7f ? "Hopeless romantic" :
                              profile.Romanticism > 0.4f ? "Enjoys romance" : "Pragmatic about love");

            // Commitment
            terminal.SetColor("white");
            terminal.Write("   Commitment: ");
            terminal.SetColor(profile.Commitment > 0.6f ? "bright_green" : "yellow");
            terminal.WriteLine(profile.Commitment > 0.7f ? "Deeply loyal" :
                              profile.Commitment > 0.4f ? "Open to commitment" : "Values freedom");

            // Sensuality
            terminal.SetColor("white");
            terminal.Write("   Passion: ");
            terminal.SetColor(profile.Sensuality > 0.6f ? "bright_red" : "gray");
            terminal.WriteLine(profile.Sensuality > 0.7f ? "Intensely passionate" :
                              profile.Sensuality > 0.4f ? "Warm and affectionate" : "Reserved");

            // Flirtatiousness
            terminal.SetColor("white");
            terminal.Write("   Flirtiness: ");
            terminal.SetColor(profile.Flirtatiousness > 0.6f ? "bright_yellow" : "gray");
            terminal.WriteLine(profile.Flirtatiousness > 0.7f ? "Very flirtatious" :
                              profile.Flirtatiousness > 0.4f ? "Somewhat coy" : "Shy");
        }

        terminal.WriteLine("");

        // Relationship status
        terminal.SetColor("white");
        terminal.Write(" Status: ");
        bool isMarried = NPCMarriageRegistry.Instance?.IsMarriedToNPC(npc.ID) == true;
        if (isMarried)
        {
            var spouseId = NPCMarriageRegistry.Instance?.GetSpouseId(npc.ID);
            var spouse = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouseId);
            terminal.SetColor("bright_red");
            terminal.WriteLine($"Married to {spouse?.Name ?? "someone"}");
        }
        else
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("Single");
        }

        // Their opinion of player
        int relation = RelationshipSystem.GetRelationshipStatus(currentPlayer, npc);
        terminal.SetColor("white");
        terminal.Write(" Thinks of you: ");
        terminal.SetColor(relation <= 20 ? "bright_magenta" : relation <= 50 ? "bright_green" : relation <= 70 ? "yellow" : "red");
        terminal.WriteLine(GetRelationDescription(relation));

        // Compatibility
        if (profile != null)
        {
            var playerGender = currentPlayer.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male;
            bool attracted = profile.IsAttractedTo(playerGender);
            terminal.SetColor("white");
            terminal.Write(" Compatibility: ");
            if (!attracted)
            {
                terminal.SetColor("red");
                terminal.WriteLine("Not interested in your type");
            }
            else if (relation <= 30)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine("Excellent match!");
            }
            else if (relation <= 50)
            {
                terminal.SetColor("green");
                terminal.WriteLine("Good potential");
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("Needs work");
            }
        }
    }

    #endregion

    #region Love Potions (replaces Romance Stats)

    private async Task LovePotions()
    {
        terminal.ClearScreen();
        terminal.SetColor("magenta");
        terminal.WriteLine("\n+---------------------------------------------------------------------------+");
        terminal.WriteLine("                        MADAME ZARA'S LOVE POTIONS");
        terminal.WriteLine("+---------------------------------------------------------------------------+\n");

        terminal.SetColor("gray");
        terminal.WriteLine("An old witch cackles as you approach her bubbling cauldron.");
        terminal.SetColor("yellow");
        terminal.WriteLine("\"Looking for a little... magical assistance in matters of the heart?\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write($" [1] Philter of Charm ");
        terminal.SetColor("yellow");
        terminal.Write($"({GameConfig.LoveStreetCharmPotionCost}g)");
        terminal.SetColor("gray");
        terminal.Write($" - +{GameConfig.LoveStreetCharmBonus} CHA for this visit");
        if (_charmActive) { terminal.SetColor("bright_green"); terminal.Write(" [ACTIVE]"); }
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write($" [2] Elixir of Allure ");
        terminal.SetColor("yellow");
        terminal.Write($"({GameConfig.LoveStreetAllurePotionCost}g)");
        terminal.SetColor("gray");
        terminal.Write($" - Next flirt auto-succeeds");
        if (_allureActive) { terminal.SetColor("bright_green"); terminal.Write(" [ACTIVE]"); }
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write($" [3] Draught of Forgetting ");
        terminal.SetColor("yellow");
        terminal.Write($"({GameConfig.LoveStreetForgetPotionCost}g)");
        terminal.SetColor("gray");
        terminal.WriteLine($" - Reduce partner jealousy by {GameConfig.LoveStreetJealousyReduction}");

        terminal.SetColor("white");
        terminal.Write($" [4] Passion Potion ");
        terminal.SetColor("yellow");
        terminal.Write($"({GameConfig.LoveStreetPassionPotionCost}g)");
        terminal.SetColor("gray");
        terminal.Write($" - Next date guarantees intimacy");
        if (_passionActive) { terminal.SetColor("bright_green"); terminal.Write(" [ACTIVE]"); }
        terminal.WriteLine("");

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(" [5] My Romance Stats - View your romantic history");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(" [0] Leave");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine($" Your gold: {currentPlayer.Gold:N0}");
        terminal.WriteLine("");

        var input = await terminal.GetInput("What catches your eye? ");
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
            terminal.WriteLine("\n\"You already have the charm flowing through you, dear!\"");
            await terminal.WaitForKey();
            return;
        }
        if (currentPlayer.Gold < GameConfig.LoveStreetCharmPotionCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"\n\"That costs {GameConfig.LoveStreetCharmPotionCost} gold, dear.\"");
            await terminal.WaitForKey();
            return;
        }

        currentPlayer.Gold -= GameConfig.LoveStreetCharmPotionCost;
        currentPlayer.Statistics?.RecordGoldSpent(GameConfig.LoveStreetCharmPotionCost);
        _charmActive = true;

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("\nYou drink the shimmering pink liquid. Warmth spreads through you.");
        terminal.WriteLine($"You feel irresistibly charming! (+{GameConfig.LoveStreetCharmBonus} CHA for this visit)");
        await terminal.WaitForKey();
    }

    private async Task BuyAllurePotion()
    {
        if (_allureActive)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("\n\"You already have allure magic ready, dear!\"");
            await terminal.WaitForKey();
            return;
        }
        if (currentPlayer.Gold < GameConfig.LoveStreetAllurePotionCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"\n\"That costs {GameConfig.LoveStreetAllurePotionCost} gold, dear.\"");
            await terminal.WaitForKey();
            return;
        }

        currentPlayer.Gold -= GameConfig.LoveStreetAllurePotionCost;
        currentPlayer.Statistics?.RecordGoldSpent(GameConfig.LoveStreetAllurePotionCost);
        _allureActive = true;

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("\nYou drink the golden elixir. Your eyes seem to sparkle with magic.");
        terminal.WriteLine("Your next flirt will be absolutely irresistible!");
        await terminal.WaitForKey();
    }

    private async Task BuyForgetPotion()
    {
        if (currentPlayer.Gold < GameConfig.LoveStreetForgetPotionCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"\n\"That costs {GameConfig.LoveStreetForgetPotionCost} gold, dear.\"");
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
        terminal.WriteLine("\nYou pour the misty blue liquid into your partners' drinks.");
        if (reduced > 0)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine($"The jealousy of {reduced} partner{(reduced > 1 ? "s" : "")} has been soothed!");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("None of your partners were jealous. The potion was wasted.");
        }
        await terminal.WaitForKey();
    }

    private async Task BuyPassionPotion()
    {
        if (_passionActive)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("\n\"You already have passion magic ready, dear!\"");
            await terminal.WaitForKey();
            return;
        }
        if (currentPlayer.Gold < GameConfig.LoveStreetPassionPotionCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"\n\"That costs {GameConfig.LoveStreetPassionPotionCost} gold, dear.\"");
            await terminal.WaitForKey();
            return;
        }

        currentPlayer.Gold -= GameConfig.LoveStreetPassionPotionCost;
        currentPlayer.Statistics?.RecordGoldSpent(GameConfig.LoveStreetPassionPotionCost);
        _passionActive = true;

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("\nYou drink the deep red potion. Fire courses through your veins.");
        terminal.WriteLine("Your next date will end with a guaranteed invitation to intimacy!");
        await terminal.WaitForKey();
    }

    private async Task ShowRomanceStats()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("\n+---------------------------------------------------------------------------+");
        terminal.WriteLine("                           YOUR ROMANTIC LIFE");
        terminal.WriteLine("+---------------------------------------------------------------------------+\n");

        var romance = RomanceTracker.Instance;

        // Spouses
        terminal.SetColor("bright_red");
        terminal.WriteLine($" <3 SPOUSES: {romance.Spouses.Count}");
        if (romance.Spouses.Count > 0)
        {
            terminal.SetColor("white");
            foreach (var spouse in romance.Spouses)
            {
                var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouse.NPCId);
                var name = npc?.Name ?? (!string.IsNullOrEmpty(spouse.NPCName) ? spouse.NPCName : spouse.NPCId);
                var days = (DateTime.Now - spouse.MarriedDate).Days;
                terminal.WriteLine($"   - {name} (married {days} days, {spouse.Children} children)");
            }
        }
        terminal.WriteLine("");

        // Lovers
        terminal.SetColor("bright_magenta");
        terminal.WriteLine($" <3 LOVERS: {romance.CurrentLovers.Count}");
        if (romance.CurrentLovers.Count > 0)
        {
            terminal.SetColor("white");
            foreach (var lover in romance.CurrentLovers)
            {
                var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == lover.NPCId);
                var name = npc?.Name ?? (!string.IsNullOrEmpty(lover.NPCName) ? lover.NPCName : lover.NPCId);
                var days = (DateTime.Now - lover.RelationshipStart).Days;
                terminal.WriteLine($"   - {name} (together {days} days)");
            }
        }
        terminal.WriteLine("");

        // FWB
        terminal.SetColor("cyan");
        terminal.WriteLine($" ~ FRIENDS WITH BENEFITS: {romance.FriendsWithBenefits.Count}");
        terminal.WriteLine("");

        // Exes
        terminal.SetColor("gray");
        terminal.WriteLine($" X PAST RELATIONSHIPS: {romance.Exes.Count}");
        terminal.WriteLine("");

        // Summary
        terminal.SetColor("white");
        terminal.WriteLine($" Total intimate encounters: {romance.EncounterHistory.Count}");
        terminal.WriteLine($" Times married: {currentPlayer.MarriedTimes}");
        terminal.WriteLine($" Children: {currentPlayer.Children}");
        terminal.WriteLine("");

        terminal.SetColor("red");
        terminal.WriteLine($" Darkness points: {currentPlayer.Darkness}");

        await terminal.WaitForKey();
    }

    #endregion

    #region Helper Methods

    private string GetRiskLevel(float chance)
    {
        return chance switch
        {
            >= 0.30f => "HIGH",
            >= 0.20f => "Medium",
            >= 0.10f => "Low",
            _ => "Very Low"
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

    private string GetRelationDescription(int level)
    {
        return level switch
        {
            <= 10 => "Deeply in love",
            <= 25 => "Very fond",
            <= 40 => "Friendly",
            <= 50 => "Neutral",
            <= 65 => "Wary",
            <= 80 => "Dislikes you",
            _ => "Hostile"
        };
    }

    private string GetPersonalityHint(PersonalityProfile profile)
    {
        // Return the most prominent trait
        var traits = new List<(float val, string desc)>
        {
            (profile.Flirtatiousness, "flirtatious"),
            (profile.Romanticism, "romantic"),
            (profile.Sensuality, "passionate"),
            (profile.Tenderness, "gentle"),
            (profile.Commitment, "devoted"),
            (1f - profile.Commitment, "free-spirited"),
        };

        // Only consider traits above 0.5 to avoid weird descriptions
        var strong = traits.Where(t => t.val > 0.5f).OrderByDescending(t => t.val).ToList();
        if (strong.Count > 0)
            return $"seems {strong[0].desc}";

        return "seems reserved";
    }

    private string GetOrientationDescription(PersonalityProfile profile)
    {
        return profile.Orientation switch
        {
            SexualOrientation.Straight => "Prefers the opposite sex",
            SexualOrientation.Gay => "Prefers the same sex",
            SexualOrientation.Lesbian => "Prefers the same sex",
            SexualOrientation.Bisexual => "Interested in all genders",
            SexualOrientation.Pansexual => "Interested in all genders",
            SexualOrientation.Asexual => "Not interested in romance",
            SexualOrientation.Demisexual => "Needs a deep bond first",
            _ => "Hard to tell"
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
