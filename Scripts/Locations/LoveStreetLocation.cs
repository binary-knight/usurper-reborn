using UsurperRemake.BBS;
using UsurperRemake.Utils;
using UsurperRemake.Systems;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UsurperRemake.Locations;

/// <summary>
/// Love Street - Adult entertainment district combining the Beauty Nest (brothel for men),
/// Hall of Dreams (gigolos for women), and NPC dating/romance features.
/// Based on Pascal WHORES.PAS and GIGOLOC.PAS with expanded modern features.
/// </summary>
public class LoveStreetLocation : BaseLocation
{
    private Random random = new Random();

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

    protected override void SetupLocation()
    {
        PossibleExits = new List<GameLocation> { GameLocation.MainStreet };
        LocationActions = new List<string>
        {
            "Visit the Pleasure Houses",
            "Meet NPCs",
            "Take someone on a date",
            "Gift Shop",
            "Gossip Corner",
            "View Romance Stats",
            "Return to Main Street"
        };
    }

    protected override void DisplayLocation()
    {
        if (DoorMode.IsInDoorMode) { DisplayLocationBBS(); return; }

        terminal.ClearScreen();

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                           <3 LOVE STREET <3                                  ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
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
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("║                        -= PLEASURE AWAITS =-                                ║");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╠═════════════════════════════════════════════════════════════════════════════╣");
        terminal.WriteLine("");

        // Pleasure Houses
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_red");
        terminal.Write("1");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(" Beauty Nest     ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_blue");
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
        terminal.Write("N");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(" Meet NPCs       ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_green");
        terminal.Write("D");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(" Take on a Date");

        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_cyan");
        terminal.Write("G");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(" Gift Shop       ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("magenta");
        terminal.Write("V");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(" Gossip Corner");

        terminal.WriteLine("");

        // Status
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("cyan");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(" Romance Stats   ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("gray");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(" Return to Main Street");

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Show gold
        terminal.SetColor("yellow");
        terminal.WriteLine($" Gold: {currentPlayer.Gold:N0}");
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
        ShowBBSMenuRow(("1", "bright_red", "BeautyNest"), ("2", "bright_blue", "HallDreams"), ("N", "yellow", "MeetNPCs"), ("D", "green", "Date"));
        ShowBBSMenuRow(("G", "cyan", "GiftShop"), ("V", "magenta", "Gossip"), ("S", "cyan", "RomanceStats"), ("R", "red", "Return"));

        // Gold
        terminal.SetColor("gray");
        terminal.Write(" Gold:");
        terminal.SetColor("yellow");
        terminal.WriteLine($"{currentPlayer.Gold:N0}");

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

            case "N":
                await MeetNPCs();
                return false;

            case "D":
                await TakeOnDate();
                return false;

            case "G":
                await VisitGiftShop();
                return false;

            case "V":
                await VisitGossipCorner();
                return false;

            case "S":
                await ShowRomanceStats();
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
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                     THE BEAUTY NEST                                         ║");
        terminal.WriteLine("║              Driven by Clarissa the Half-Elf                                ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
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
        terminal.WriteLine($"\n═══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine($"                         {courtesan.Name} the {courtesan.Race}");
        terminal.WriteLine($"═══════════════════════════════════════════════════════════════════════════════\n");

        terminal.SetColor("white");
        terminal.WriteLine(courtesan.IntroText);
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine($"\"{courtesan.Name} wants to see {courtesan.Price:N0} gold before you can proceed.\"");
        terminal.WriteLine("");

        var confirm = await terminal.GetInput($"Pay {courtesan.Name} {courtesan.Price:N0} gold? (Y/N): ");
        if (confirm.ToUpper() != "Y")
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

        // Show the intimate encounter
        await ShowIntimateEncounter(courtesan.Name, courtesan.Race, courtesan.Price, courtesan.DiseaseChance);
    }

    #endregion

    #region Hall of Dreams (Gigolos)

    private async Task VisitHallOfDreams()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_blue");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                      HALL OF DREAMS                                         ║");
        terminal.WriteLine("║              Supervised by Giovanni the Gnome                               ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
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
        terminal.WriteLine($"\n═══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine($"                         {gigolo.Name} the {gigolo.Race}");
        terminal.WriteLine($"═══════════════════════════════════════════════════════════════════════════════\n");

        terminal.SetColor("white");
        terminal.WriteLine(gigolo.IntroText);
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine($"\"{gigolo.Name} requires {gigolo.Price:N0} gold for his services.\"");
        terminal.WriteLine("");

        var confirm = await terminal.GetInput($"Pay {gigolo.Name} {gigolo.Price:N0} gold? (Y/N): ");
        if (confirm.ToUpper() != "Y")
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

        // Show the intimate encounter
        await ShowIntimateEncounter(gigolo.Name, gigolo.Race, gigolo.Price, gigolo.DiseaseChance);
    }

    #endregion

    #region Intimate Encounter System

    private async Task ShowIntimateEncounter(string partnerName, string race, long price, float diseaseChance)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("\n═══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine("                              A Night of Passion");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════════════════════\n");

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
        terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
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
        await GenerateNews(partnerName);

        // Notify spouse if married
        await NotifySpouse(partnerName);

        await terminal.WaitForKey();
    }

    /// <summary>
    /// Record a paid encounter in the RomanceTracker for statistics and history
    /// </summary>
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

        // Track total spending on pleasure (could be used for achievements, stats)
        currentPlayer.Darkness += 1; // Small extra darkness for paid encounters
    }

    private async Task GenerateIntimateScene(string partnerName, string race, long price)
    {
        terminal.SetColor("white");

        // Opening based on setting
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

        // Building tension
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

        // Main event - detailed based on price tier
        if (price >= 50000)
        {
            await ShowPremiumEncounter(partnerName, race);
        }
        else if (price >= 20000)
        {
            await ShowHighEndEncounter(partnerName, race);
        }
        else if (price >= 5000)
        {
            await ShowStandardEncounter(partnerName, race);
        }
        else
        {
            await ShowBasicEncounter(partnerName, race);
        }

        // Aftermath
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
            terminal.WriteLine("═══════════════════════════════════════════════════════════════════════════════");
            terminal.WriteLine("                         SOMETHING IS WRONG!");
            terminal.WriteLine("═══════════════════════════════════════════════════════════════════════════════");
            terminal.WriteLine("");
            terminal.WriteLine("As you leave, you start to feel pain in your nether regions!");
            terminal.WriteLine("By the gods! You've been infected with a venereal disease!");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine("You rush to the Healer, crying for help...");
            terminal.WriteLine("The monks give you foul-smelling potions and painful treatments.");
            terminal.WriteLine("");

            // Apply disease effects
            currentPlayer.HP = Math.Max(1, currentPlayer.HP / 2);
            currentPlayer.LoversBane = true;

            terminal.SetColor("bright_red");
            terminal.WriteLine("You've contracted 'Lover's Bane'!");
            terminal.WriteLine("Your HP has been halved! Visit the Healer to cure this affliction.");
            terminal.WriteLine("");

            // News about it
            NewsSystem.Instance.Newsy(true,
                $"{currentPlayer.Name2} contracted a disease at Love Street!",
                $"The pleasure houses claim another victim...");

            // Mail the player
            MailSystem.SendSystemMail(currentPlayer.Name2, "Disease Notice",
                "Medical Emergency",
                $"You contracted a sexual disease from {partnerName}.",
                "Seek treatment at the Healer immediately!");

            await Task.Delay(2000);
        }
    }

    private async Task GenerateNews(string partnerName)
    {
        // Random news about the visit (33% chance)
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

    private async Task NotifySpouse(string partnerName)
    {
        var spouses = RomanceTracker.Instance.Spouses;
        foreach (var spouse in spouses)
        {
            // Send mail to spouse about infidelity
            MailSystem.SendSystemMail(spouse.NPCId, "HOW COULD THEY?",
                "INFIDELITY!",
                $"{currentPlayer.Name2} has been unfaithful to you!",
                $"They enjoyed themselves with {partnerName} at Love Street!");

            // Increase jealousy
            RomanceTracker.Instance.JealousyLevels[spouse.NPCId] =
                Math.Min(100, RomanceTracker.Instance.JealousyLevels.GetValueOrDefault(spouse.NPCId, 0) + 20);
        }
    }

    #endregion

    #region NPC Dating & Romance

    private async Task MeetNPCs()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("\n═══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine("                           MEET THE LOCALS");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════════════════════\n");

        // Get NPCs currently at Love Street
        var npcsHere = NPCSpawnSystem.Instance.ActiveNPCs?
            .Where(n => n.CurrentLocation == "Love Street" || n.CurrentLocation == "Love Corner")
            .ToList() ?? new List<NPC>();

        if (npcsHere.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("There are no adventurers here at the moment.");
            terminal.WriteLine("Try coming back later, or look for someone on Main Street.");
            await terminal.WaitForKey();
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine("The following adventurers are here looking for company:\n");

        for (int i = 0; i < npcsHere.Count; i++)
        {
            var npc = npcsHere[i];
            var relationship = RelationshipSystem.GetRelationshipStatus(currentPlayer, npc);
            var romanceType = RomanceTracker.Instance.GetRelationType(npc.ID);

            terminal.SetColor("bright_yellow");
            terminal.Write($" [{i + 1}] ");
            terminal.SetColor("white");
            terminal.Write($"{npc.Name}");
            terminal.SetColor("gray");
            terminal.Write($" - Level {npc.Level} {npc.Race} {npc.Class}");

            if (romanceType != RomanceRelationType.None)
            {
                terminal.SetColor("bright_magenta");
                terminal.Write($" [{romanceType}]");
            }
            terminal.WriteLine("");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(" [0] Return");
        terminal.WriteLine("");

        var input = await terminal.GetInput("Choose someone to approach: ");
        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= npcsHere.Count)
        {
            var selectedNpc = npcsHere[choice - 1];
            await InteractWithNPC(selectedNpc);
        }
    }

    protected override async Task InteractWithNPC(NPC npc)
    {
        // Use the visual novel dialogue system for full romance interactions
        await VisualNovelDialogueSystem.Instance.StartConversation(currentPlayer, npc, terminal);
    }

    private async Task TakeOnDate()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("\n═══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine("                           TAKE SOMEONE ON A DATE");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════════════════════\n");

        // Get potential dates (lovers, spouses, or NPCs with good relations)
        var romance = RomanceTracker.Instance;
        var potentialDates = new List<(string id, string name, string type)>();

        // Add spouses
        foreach (var spouse in romance.Spouses)
        {
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouse.NPCId);
            // Use cached name if NPC lookup fails
            var name = npc?.Name ?? (!string.IsNullOrEmpty(spouse.NPCName) ? spouse.NPCName : spouse.NPCId);
            potentialDates.Add((spouse.NPCId, name, "Spouse"));
        }

        // Add lovers
        foreach (var lover in romance.CurrentLovers)
        {
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == lover.NPCId);
            // Use cached name if NPC lookup fails
            var name = npc?.Name ?? (!string.IsNullOrEmpty(lover.NPCName) ? lover.NPCName : lover.NPCId);
            potentialDates.Add((lover.NPCId, name, "Lover"));
        }

        // Add friendly NPCs
        var friendlyNpcs = NPCSpawnSystem.Instance?.ActiveNPCs?
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
            terminal.WriteLine("Try meeting people and improving your relationships first!");
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
            // Try to find NPC by ID first, then fall back to name match
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == selected.id);
            if (npc == null)
            {
                // Fall back to matching by name (handles saved data with different ID formats)
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
        terminal.WriteLine($"\n═══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine($"                      A Date with {partner.Name}");
        terminal.WriteLine($"═══════════════════════════════════════════════════════════════════════════════\n");

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

        await ProcessDateActivity(partner, choice);
    }

    private async Task ProcessDateActivity(NPC partner, int activity)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");

        switch (activity)
        {
            case 1: // Dinner
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

            case 2: // Moonlit Walk
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

            case 3: // Theater
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

            case 4: // Picnic
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

            case 5: // Dancing
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

        // If relationship is good enough, offer intimacy option
        var romanceType = RomanceTracker.Instance.GetRelationType(partner.ID);
        int relationLevel = RelationshipSystem.GetRelationshipStatus(currentPlayer, partner);

        if (romanceType == RomanceRelationType.Lover ||
            romanceType == RomanceRelationType.Spouse ||
            romanceType == RomanceRelationType.FWB ||
            (relationLevel <= 20 && random.NextDouble() < 0.5))
        {
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
                // Use the IntimacySystem for the intimate encounter
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
        terminal.WriteLine("\n═══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine("                           LOVE STREET GIFT SHOP");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════════════════════\n");

        terminal.SetColor("white");
        terminal.WriteLine("\"Welcome, welcome! Looking for something special for someone special?\"\n");

        terminal.WriteLine(" [1] Red Roses (100 gold) - Classic and romantic");
        terminal.WriteLine(" [2] Box of Chocolates (200 gold) - Sweet and thoughtful");
        terminal.WriteLine(" [3] Bottle of Fine Wine (500 gold) - For a special occasion");
        terminal.WriteLine(" [4] Silver Necklace (2,000 gold) - Elegant and meaningful");
        terminal.WriteLine(" [5] Diamond Ring (10,000 gold) - For someone truly special");
        terminal.WriteLine(" [6] Exotic Perfume (1,000 gold) - Enchanting and alluring");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(" [0] Leave shop");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine($"Your gold: {currentPlayer.Gold:N0}");
        terminal.WriteLine("");

        var input = await terminal.GetInput("What would you like to buy? ");
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > 6)
        {
            terminal.WriteLine("\"Come back when you're ready to shop!\"", "gray");
            await terminal.WaitForKey();
            return;
        }

        var gifts = new[]
        {
            ("Red Roses", 100L, 3),
            ("Box of Chocolates", 200L, 4),
            ("Bottle of Fine Wine", 500L, 6),
            ("Silver Necklace", 2000L, 10),
            ("Diamond Ring", 10000L, 20),
            ("Exotic Perfume", 1000L, 8)
        };

        var (giftName, cost, relationBoost) = gifts[choice - 1];

        if (currentPlayer.Gold < cost)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"\"That costs {cost} gold! You don't have enough.\"");
            await terminal.WaitForKey();
            return;
        }

        // Ask who to give it to
        terminal.WriteLine("");
        var recipient = await terminal.GetInput("Who would you like to give this to? (enter name): ");

        if (string.IsNullOrWhiteSpace(recipient))
        {
            terminal.WriteLine("\"Changed your mind? No problem.\"", "gray");
            await terminal.WaitForKey();
            return;
        }

        // Find the recipient
        var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(
            n => n.Name.Equals(recipient, StringComparison.OrdinalIgnoreCase) ||
                 n.Name2.Equals(recipient, StringComparison.OrdinalIgnoreCase));

        if (npc == null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"Could not find anyone named '{recipient}'.");
            await terminal.WaitForKey();
            return;
        }

        // Process gift
        currentPlayer.Gold -= cost;

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"\nYou purchase {giftName} and present it to {npc.Name}.");
        terminal.WriteLine("");

        // Check their reaction
        int currentRelation = RelationshipSystem.GetRelationshipStatus(currentPlayer, npc);
        bool isAttracted = npc.Brain?.Personality?.IsAttractedTo(
            currentPlayer.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male) ?? true;

        if (isAttracted && currentRelation <= 60)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine($"{npc.Name}'s eyes light up with joy!");
            terminal.WriteLine($"\"Oh, {giftName}! How thoughtful of you!\"");
            RelationshipSystem.UpdateRelationship(currentPlayer, npc, 1, relationBoost, false, true);
        }
        else if (currentRelation <= 80)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"{npc.Name} accepts the gift politely.");
            terminal.WriteLine($"\"Thank you, that's... nice of you.\"");
            RelationshipSystem.UpdateRelationship(currentPlayer, npc, 1, relationBoost / 2, false, false);
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{npc.Name} looks uncomfortable.");
            terminal.WriteLine($"\"I... can't accept this from you.\"");
            terminal.WriteLine("They walk away, leaving the gift behind.");
        }

        await terminal.WaitForKey();
    }

    #endregion

    #region Gossip Corner

    private async Task VisitGossipCorner()
    {
        terminal.ClearScreen();
        terminal.SetColor("magenta");
        terminal.WriteLine("\n═══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine("                           THE GOSSIP CORNER");
        terminal.WriteLine("                      Madame Whispers Knows All");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════════════════════\n");

        terminal.SetColor("gray");
        terminal.WriteLine("An ancient crone beckons you into a shadowy alcove.");
        terminal.SetColor("yellow");
        terminal.WriteLine("\"Come, come... Madame Whispers knows all the secrets of the heart.\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(" [1] Who is seeing whom? (100 gold) - Current romances");
        terminal.WriteLine(" [2] Recent marriages (50 gold) - Wedding announcements");
        terminal.WriteLine(" [3] Scandalous affairs (200 gold) - Who's being naughty");
        terminal.WriteLine(" [4] About a specific person (500 gold) - Deep investigation");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(" [0] Leave");
        terminal.WriteLine("");

        var input = await terminal.GetInput("What secrets do you seek? ");

        switch (input)
        {
            case "1":
                if (currentPlayer.Gold >= 100)
                {
                    currentPlayer.Gold -= 100;
                    await ShowCurrentRomances();
                }
                else
                {
                    terminal.WriteLine("\"No gold, no gossip!\"", "red");
                }
                break;

            case "2":
                if (currentPlayer.Gold >= 50)
                {
                    currentPlayer.Gold -= 50;
                    await ShowRecentMarriages();
                }
                else
                {
                    terminal.WriteLine("\"No gold, no gossip!\"", "red");
                }
                break;

            case "3":
                if (currentPlayer.Gold >= 200)
                {
                    currentPlayer.Gold -= 200;
                    await ShowAffairs();
                }
                else
                {
                    terminal.WriteLine("\"No gold, no gossip!\"", "red");
                }
                break;

            case "4":
                if (currentPlayer.Gold >= 500)
                {
                    var name = await terminal.GetInput("Whose secrets do you seek? ");
                    currentPlayer.Gold -= 500;
                    await InvestigatePerson(name);
                }
                else
                {
                    terminal.WriteLine("\"No gold, no gossip!\"", "red");
                }
                break;
        }

        await terminal.WaitForKey();
    }

    private Task ShowCurrentRomances()
    {
        terminal.SetColor("magenta");
        terminal.WriteLine("\n\"Ah, the tangled webs of love...\"\n");
        terminal.SetColor("white");

        var npcs = NPCSpawnSystem.Instance?.ActiveNPCs?.ToList() ?? new List<NPC>();
        int romanceCount = 0;

        // Show player's romances
        var playerRomance = RomanceTracker.Instance;
        if (playerRomance.Spouses.Count > 0)
        {
            foreach (var spouse in playerRomance.Spouses)
            {
                var npc = npcs.FirstOrDefault(n => n.ID == spouse.NPCId);
                var name = npc?.Name ?? (!string.IsNullOrEmpty(spouse.NPCName) ? spouse.NPCName : spouse.NPCId);
                terminal.WriteLine($"<3 {currentPlayer.Name2} is married to {name}");
                romanceCount++;
            }
        }
        if (playerRomance.CurrentLovers.Count > 0)
        {
            foreach (var lover in playerRomance.CurrentLovers)
            {
                var npc = npcs.FirstOrDefault(n => n.ID == lover.NPCId);
                var name = npc?.Name ?? (!string.IsNullOrEmpty(lover.NPCName) ? lover.NPCName : lover.NPCId);
                terminal.WriteLine($"<3 {currentPlayer.Name2} is involved with {name}");
                romanceCount++;
            }
        }

        if (romanceCount == 0)
        {
            terminal.WriteLine("\"Not much happening in the romance department lately...\"");
        }

        return Task.CompletedTask;
    }

    private Task ShowRecentMarriages()
    {
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("\n\"Recent wedding bells...\"\n");
        terminal.SetColor("white");

        var playerRomance = RomanceTracker.Instance;
        if (playerRomance.Spouses.Count > 0)
        {
            foreach (var spouse in playerRomance.Spouses)
            {
                var daysSince = (DateTime.Now - spouse.MarriedDate).Days;
                var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouse.NPCId);
                var name = npc?.Name ?? (!string.IsNullOrEmpty(spouse.NPCName) ? spouse.NPCName : spouse.NPCId);
                terminal.WriteLine($"<3 {currentPlayer.Name2} married {name} ({daysSince} days ago)");
            }
        }
        else
        {
            terminal.WriteLine("\"No recent marriages to report...\"");
        }

        return Task.CompletedTask;
    }

    private Task ShowAffairs()
    {
        terminal.SetColor("red");
        terminal.WriteLine("\n\"Scandalous behavior, you say?\"\n");
        terminal.SetColor("white");

        var recentNews = NewsSystem.Instance.GetTodaysNews(GameConfig.NewsCategory.General);
        var scandalous = recentNews.Where(n =>
            n.Contains("Love Street") ||
            n.Contains("unfaithful") ||
            n.Contains("disease") ||
            n.Contains("Beauty Nest") ||
            n.Contains("Hall of Dreams")).Take(5).ToList();

        if (scandalous.Count > 0)
        {
            foreach (var news in scandalous)
            {
                terminal.WriteLine($"- {news}");
            }
        }
        else
        {
            terminal.WriteLine("\"Everyone's been remarkably well-behaved lately... how boring!\"");
        }

        return Task.CompletedTask;
    }

    private Task InvestigatePerson(string name)
    {
        var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(
            n => n.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

        if (npc == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"\"I don't know anyone by that name...\"");
            return Task.CompletedTask;
        }

        terminal.SetColor("magenta");
        terminal.WriteLine($"\n\"Ah, {npc.Name}... let me see what I know...\"\n");
        terminal.SetColor("white");

        terminal.WriteLine($"Name: {npc.Name}");
        terminal.WriteLine($"Level {npc.Level} {npc.Race} {npc.Class}");
        terminal.WriteLine($"Age: {npc.Age}");
        terminal.WriteLine("");

        var profile = npc.Brain?.Personality;
        if (profile != null)
        {
            terminal.WriteLine($"Orientation: {profile.Orientation}");
            terminal.WriteLine($"Romanticism: {(profile.Romanticism > 0.7f ? "Very romantic" : profile.Romanticism > 0.3f ? "Moderate" : "Practical")}");
            terminal.WriteLine($"Commitment: {(profile.Commitment > 0.7f ? "Seeks commitment" : profile.Commitment > 0.3f ? "Open to options" : "Prefers freedom")}");
        }

        int relation = RelationshipSystem.GetRelationshipStatus(currentPlayer, npc);
        terminal.WriteLine("");
        terminal.WriteLine($"Their opinion of you: {GetRelationDescription(relation)}");

        return Task.CompletedTask;
    }

    #endregion

    #region Romance Stats

    private async Task ShowRomanceStats()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("\n═══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine("                           YOUR ROMANTIC LIFE");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════════════════════\n");

        var romance = RomanceTracker.Instance;

        // Spouses
        terminal.SetColor("bright_red");
        terminal.WriteLine($"<3 SPOUSES: {romance.Spouses.Count}");
        if (romance.Spouses.Count > 0)
        {
            terminal.SetColor("white");
            foreach (var spouse in romance.Spouses)
            {
                var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouse.NPCId);
                // Use cached name if NPC lookup fails
                var name = npc?.Name ?? (!string.IsNullOrEmpty(spouse.NPCName) ? spouse.NPCName : spouse.NPCId);
                var days = (DateTime.Now - spouse.MarriedDate).Days;
                terminal.WriteLine($"  - {name} (married {days} days, {spouse.Children} children)");
            }
        }
        terminal.WriteLine("");

        // Lovers
        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"<3 LOVERS: {romance.CurrentLovers.Count}");
        if (romance.CurrentLovers.Count > 0)
        {
            terminal.SetColor("white");
            foreach (var lover in romance.CurrentLovers)
            {
                var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == lover.NPCId);
                // Use cached name if NPC lookup fails
                var name = npc?.Name ?? (!string.IsNullOrEmpty(lover.NPCName) ? lover.NPCName : lover.NPCId);
                var days = (DateTime.Now - lover.RelationshipStart).Days;
                terminal.WriteLine($"  - {name} (together {days} days)");
            }
        }
        terminal.WriteLine("");

        // FWB
        terminal.SetColor("cyan");
        terminal.WriteLine($"~ FRIENDS WITH BENEFITS: {romance.FriendsWithBenefits.Count}");
        terminal.WriteLine("");

        // Exes
        terminal.SetColor("gray");
        terminal.WriteLine($"X PAST RELATIONSHIPS: {romance.Exes.Count}");
        terminal.WriteLine("");

        // Encounters
        terminal.SetColor("white");
        terminal.WriteLine($"Total intimate encounters: {romance.EncounterHistory.Count}");
        terminal.WriteLine($"Times married: {currentPlayer.MarriedTimes}");
        terminal.WriteLine($"Children: {currentPlayer.Children}");
        terminal.WriteLine("");

        // Darkness from carnal activities
        terminal.SetColor("red");
        terminal.WriteLine($"Darkness points: {currentPlayer.Darkness}");

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
        long charisma = currentPlayer.Charisma;
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
            <= 10 => "Deeply in love with you",
            <= 25 => "Very fond of you",
            <= 40 => "Friendly",
            <= 50 => "Neutral",
            <= 65 => "Wary of you",
            <= 80 => "Dislikes you",
            _ => "Despises you"
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
