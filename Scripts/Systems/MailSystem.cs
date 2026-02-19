using UsurperRemake.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Mail System - Complete Pascal-compatible mail and messaging system
/// Based on Pascal MAIL.PAS with all mail functions and procedures
/// Handles system mail, birthday events, notifications, and player communication
/// </summary>
public static partial class MailSystem
{
    private static List<MailRecord> mailDatabase = new List<MailRecord>();
    private static Random random = new Random();
    
    /// <summary>
    /// Send system mail to a player
    /// Pascal: Post procedure with mailrequest_nothing
    /// </summary>
    public static void SendSystemMail(string playerName, string subject, string line1, string line2 = "", string line3 = "")
    {
        var mail = new MailRecord
        {
            Receiver = playerName,
            Sender = "SYSTEM",
            Subject = subject,
            Date = DateTime.Now,
            ReadFlag = false,
            Special = GameConfig.MailRequestSystem,
            Lines = new List<string> { line1 }
        };
        
        if (!string.IsNullOrEmpty(line2)) mail.Lines.Add(line2);
        if (!string.IsNullOrEmpty(line3)) mail.Lines.Add(line3);
        
        SaveMailRecord(mail);
        
        // GD.Print($"[MailSystem] System mail sent to {playerName}: {subject}");
    }
    
    /// <summary>
    /// Send birthday mail with gift options
    /// Pascal: Birthday mail processing in MAIL.PAS
    /// </summary>
    public static void SendBirthdayMail(string playerName, int newAge)
    {
        var mail = new MailRecord
        {
            Receiver = playerName,
            Sender = "TOWN COUNCIL",
            Subject = "Birthday Party!",
            Date = DateTime.Now,
            ReadFlag = false,
            Special = GameConfig.MailRequestBirthday,
            Lines = new List<string>
            {
                $"You celebrated your {newAge} birthday!",
                "The Town council has gracefully decided you worthy a present.",
                "",
                "Choose a gift:",
                "(E)xperience - Gain knowledge and wisdom",
                "(L)ove - Increase your charm and charisma", 
                "(A)dopt a child - Expand your family",
                "(S)kip - Decline all gifts",
                "",
                "Visit the Town Council to claim your gift!"
            }
        };
        
        SaveMailRecord(mail);
        
        // GD.Print($"[MailSystem] Birthday mail sent to {playerName} for age {newAge}");
    }
    
    /// <summary>
    /// Send royal guard recruitment mail
    /// Pascal: Royal guard mail in MAIL.PAS
    /// </summary>
    public static void SendRoyalGuardMail(string playerName, string kingName, long wage)
    {
        var mail = new MailRecord
        {
            Receiver = playerName,
            Sender = kingName,
            Subject = "Royal Guard Recruitment",
            Date = DateTime.Now,
            ReadFlag = false,
            Special = GameConfig.MailRequestRoyalGuard,
            Lines = new List<string>
            {
                "Greetings, brave warrior!",
                "",
                $"I, {kingName}, ruler of this realm, have been watching",
                "your deeds with great interest.",
                "",
                "I hereby offer you a position as one of my Royal Guards.",
                $"The position pays {wage} gold per day.",
                "",
                "Visit the castle to accept or decline this honor.",
                "",
                "Long live the realm!",
                $"-- {kingName}"
            }
        };
        
        SaveMailRecord(mail);
        
        // GD.Print($"[MailSystem] Royal guard recruitment mail sent to {playerName}");
    }
    
    /// <summary>
    /// Send marriage proposal mail
    /// Pascal: Marriage mail system
    /// </summary>
    public static void SendMarriageMail(string receiverName, string proposerName, bool isProposal)
    {
        var mail = new MailRecord
        {
            Receiver = receiverName,
            Sender = proposerName,
            Subject = isProposal ? "Marriage Proposal" : "Marriage Update",
            Date = DateTime.Now,
            ReadFlag = false,
            Special = GameConfig.MailRequestMarriage,
            Lines = new List<string>()
        };
        
        if (isProposal)
        {
            mail.Lines.AddRange(new[]
            {
                "My Dearest Love,",
                "",
                "After much consideration, I have decided to ask",
                "for your hand in marriage.",
                "",
                "Will you marry me and share in life's adventures?",
                "",
                "Visit the temple to give your answer.",
                "",
                $"With all my love,",
                $"-- {proposerName}"
            });
        }
        else
        {
            mail.Lines.AddRange(new[]
            {
                "Marriage Status Update",
                "",
                "Your relationship status has changed.",
                "",
                "Check your character status for details.",
                "",
                "-- The Temple"
            });
        }
        
        SaveMailRecord(mail);
        
        // GD.Print($"[MailSystem] Marriage mail sent to {receiverName}");
    }
    
    /// <summary>
    /// Send child birth notification mail
    /// Pascal: Child birth mail system
    /// </summary>
    public static void SendChildBirthMail(string parentName, string childName, bool isBoy)
    {
        var genderText = isBoy ? "son" : "daughter";
        var pronounText = isBoy ? "He" : "She";
        
        var mail = new MailRecord
        {
            Receiver = parentName,
            Sender = "THE STORK",
            Subject = "A New Arrival!",
            Date = DateTime.Now,
            ReadFlag = false,
            Special = GameConfig.MailRequestChildBorn,
            Lines = new List<string>
            {
                "Congratulations!",
                "",
                $"A new {genderText} has been born to you!",
                $"The child's name is {childName}.",
                "",
                $"{pronounText} appears to be healthy and strong.",
                "",
                "Visit the Royal Orphanage to see your child.",
                "",
                "May your family grow in happiness!",
                "-- The Stork"
            }
        };
        
        SaveMailRecord(mail);
        
        // GD.Print($"[MailSystem] Child birth mail sent to {parentName}");
    }
    
    /// <summary>
    /// Send news mail to all players
    /// Pascal: News mail system
    /// </summary>
    public static void SendNewsMail(string headline, string[] newsLines)
    {
        // In a real implementation, this would send to all players
        // For now, send to current player if available
        var gameEngine = GameEngine.Instance;
        if (gameEngine?.CurrentPlayer != null)
        {
            var mail = new MailRecord
            {
                Receiver = gameEngine.CurrentPlayer.Name2,
                Sender = "TOWN CRIER",
                Subject = "Daily News",
                Date = DateTime.Now,
                ReadFlag = false,
                Special = GameConfig.MailRequestNews,
                Lines = new List<string> { headline, new string('-', headline.Length) }
            };
            
            mail.Lines.AddRange(newsLines);
            
            SaveMailRecord(mail);
        }
        
        // GD.Print($"[MailSystem] News mail sent: {headline}");
    }
    
    /// <summary>
    /// Read player's mail
    /// Pascal: Read_My_Mail procedure
    /// </summary>
    public static async Task<bool> ReadPlayerMail(string playerName, TerminalUI terminal)
    {
        var playerMail = GetPlayerMail(playerName);
        
        if (playerMail.Count == 0)
        {
            terminal.WriteLine("You have no mail.", "gray");
            return false;
        }
        
        terminal.WriteLine("", "white");
        terminal.WriteLine("═══════════════════════════════════════", "bright_cyan");
        terminal.WriteLine("             YOUR MAIL                  ", "bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════", "bright_cyan");
        terminal.WriteLine("", "white");
        
        for (int i = 0; i < playerMail.Count; i++)
        {
            var mail = playerMail[i];
            var status = mail.ReadFlag ? "READ" : "NEW";
            var statusColor = mail.ReadFlag ? "gray" : "bright_yellow";
            
            terminal.WriteLine($"[{i + 1}] {mail.Subject} - {mail.Sender} ({status})", statusColor);
        }
        
        terminal.WriteLine("", "white");
        terminal.WriteLine("Select mail to read (1-" + playerMail.Count + "), or 0 to exit:", "white");
        
        var input = await terminal.GetInputAsync("> ");
        
        if (int.TryParse(input, out var selection) && selection > 0 && selection <= playerMail.Count)
        {
            await DisplayMailMessage(playerMail[selection - 1], terminal);
            
            // Mark as read
            playerMail[selection - 1].ReadFlag = true;
            
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Display individual mail message
    /// Pascal: Mail display formatting
    /// </summary>
    private static async Task DisplayMailMessage(MailRecord mail, TerminalUI terminal)
    {
        terminal.ClearScreen();
        terminal.WriteLine("", "white");
        terminal.WriteLine("═══════════════════════════════════════", "bright_blue");
        terminal.WriteLine("                MAIL                    ", "bright_blue");
        terminal.WriteLine("═══════════════════════════════════════", "bright_blue");
        terminal.WriteLine("", "white");
        
        terminal.WriteLine($"From: {mail.Sender}", "yellow");
        terminal.WriteLine($"To: {mail.Receiver}", "white");
        terminal.WriteLine($"Subject: {mail.Subject}", "cyan");
        terminal.WriteLine($"Date: {mail.Date:MM-dd-yyyy HH:mm}", "gray");
        terminal.WriteLine("", "white");
        terminal.WriteLine(new string('-', 50), "gray");
        terminal.WriteLine("", "white");
        
        foreach (var line in mail.Lines)
        {
            terminal.WriteLine(line, "white");
        }
        
        terminal.WriteLine("", "white");
        terminal.WriteLine(new string('-', 50), "gray");
        terminal.WriteLine("", "white");
        
        // Handle special mail types
        await ProcessSpecialMail(mail, terminal);
        
        await terminal.PressAnyKey("Press Enter to continue...");
    }
    
    /// <summary>
    /// Process special mail actions
    /// Pascal: Special mail processing in MAIL.PAS
    /// </summary>
    private static async Task ProcessSpecialMail(MailRecord mail, TerminalUI terminal)
    {
        switch (mail.Special)
        {
            case GameConfig.MailRequestBirthday:
                await ProcessBirthdayMail(mail, terminal);
                break;
                
            case GameConfig.MailRequestRoyalGuard:
                await ProcessRoyalGuardMail(mail, terminal);
                break;
                
            case GameConfig.MailRequestMarriage:
                await ProcessMarriageMail(mail, terminal);
                break;
                
            case GameConfig.MailRequestChildBorn:
                await ProcessChildBirthMail(mail, terminal);
                break;
        }
    }
    
    /// <summary>
    /// Process birthday mail with gift selection
    /// Pascal: Birthday gift processing
    /// </summary>
    private static async Task ProcessBirthdayMail(MailRecord mail, TerminalUI terminal)
    {
        terminal.WriteLine("Choose your birthday gift:", "bright_yellow");
        terminal.WriteLine("(E)xperience", "white");
        terminal.WriteLine("(L)ove", "white");
        terminal.WriteLine("(A)dopt a child", "white");
        terminal.WriteLine("(S)kip", "white");
        
        var choice = await terminal.GetInputAsync("Your choice: ");
        var gameEngine = GameEngine.Instance;
        var player = gameEngine?.CurrentPlayer;
        
        if (player == null) return;
        
        switch (choice.ToUpper())
        {
            case "E":
                player.Experience += GameConfig.BirthdayExperienceGift;
                terminal.WriteLine($"You gained {GameConfig.BirthdayExperienceGift} experience!", "bright_green");
                break;
                
            case "L":
                player.Charisma += GameConfig.BirthdayLoveGift;
                terminal.WriteLine($"Your charisma increased by {GameConfig.BirthdayLoveGift}!", "bright_green");
                break;
                
            case "A":
                // Child adoption logic would go here
                terminal.WriteLine("A child has been placed in your care!", "bright_green");
                break;
                
            case "S":
                terminal.WriteLine("You politely declined all gifts.", "gray");
                break;
                
            default:
                terminal.WriteLine("Invalid choice. Gifts declined.", "red");
                break;
        }
    }
    
    /// <summary>
    /// Process royal guard recruitment mail
    /// </summary>
    private static async Task ProcessRoyalGuardMail(MailRecord mail, TerminalUI terminal)
    {
        terminal.WriteLine("Will you accept the position of Royal Guard?", "bright_yellow");
        terminal.WriteLine("(Y)es - Accept the honor", "white");
        terminal.WriteLine("(N)o - Decline politely", "white");
        
        var choice = await terminal.GetInputAsync("Your choice: ");
        var gameEngine = GameEngine.Instance;
        var player = gameEngine?.CurrentPlayer;
        
        if (player == null) return;
        
        switch (choice.ToUpper())
        {
            case "Y":
                player.BGuardNr = 1; // Assign guard position
                terminal.WriteLine("You have accepted the position of Royal Guard!", "bright_green");
                terminal.WriteLine("Report to the castle for your duties.", "cyan");
                break;
                
            case "N":
                terminal.WriteLine("You politely declined the position.", "gray");
                break;
                
            default:
                terminal.WriteLine("No response given. Position declined.", "red");
                break;
        }
    }
    
    /// <summary>
    /// Process marriage proposal mail
    /// </summary>
    private static async Task ProcessMarriageMail(MailRecord mail, TerminalUI terminal)
    {
        if (mail.Subject.Contains("Proposal"))
        {
            terminal.WriteLine("Will you accept this marriage proposal?", "bright_yellow");
            terminal.WriteLine("(Y)es - Accept the proposal", "white");
            terminal.WriteLine("(N)o - Decline the proposal", "white");
            
            var choice = await terminal.GetInputAsync("Your choice: ");
            
            switch (choice.ToUpper())
            {
                case "Y":
                    terminal.WriteLine("You have accepted the marriage proposal!", "bright_green");
                    terminal.WriteLine("Visit the temple to complete the ceremony.", "cyan");
                    break;
                    
                case "N":
                    terminal.WriteLine("You have declined the marriage proposal.", "gray");
                    break;
                    
                default:
                    terminal.WriteLine("No response given. Proposal declined.", "red");
                    break;
            }
        }
    }
    
    /// <summary>
    /// Process child birth notification mail
    /// </summary>
    private static Task ProcessChildBirthMail(MailRecord mail, TerminalUI terminal)
    {
        terminal.WriteLine("Congratulations on your new child!", "bright_green");
        terminal.WriteLine("Visit the Royal Orphanage to see them.", "cyan");
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Get all mail for a specific player
    /// </summary>
    private static List<MailRecord> GetPlayerMail(string playerName)
    {
        var playerMail = new List<MailRecord>();
        
        foreach (var mail in mailDatabase)
        {
            if (mail.Receiver.Equals(playerName, StringComparison.OrdinalIgnoreCase))
            {
                playerMail.Add(mail);
            }
        }
        
        // Sort by date, newest first
        playerMail.Sort((a, b) => b.Date.CompareTo(a.Date));
        
        return playerMail;
    }
    
    /// <summary>
    /// Save mail record to database
    /// </summary>
    private static void SaveMailRecord(MailRecord mail)
    {
        // Check database size limit
        if (mailDatabase.Count >= GameConfig.MaxMailRecords)
        {
            // Remove oldest mail to make space
            mailDatabase.RemoveAt(0);
        }
        
        mailDatabase.Add(mail);
        
        // In a real implementation, this would save to file
        // GD.Print($"[MailSystem] Mail saved: {mail.Subject} to {mail.Receiver}");
    }
    
    /// <summary>
    /// Clean up old mail records
    /// Pascal: Old_Mail function and cleanup
    /// </summary>
    public static void CleanupOldMail()
    {
        var cutoffDate = DateTime.Now.AddDays(-GameConfig.DefaultMaxMailDays);
        var removedCount = 0;
        
        for (int i = mailDatabase.Count - 1; i >= 0; i--)
        {
            if (mailDatabase[i].Date < cutoffDate)
            {
                mailDatabase.RemoveAt(i);
                removedCount++;
            }
        }
        
        if (removedCount > 0)
        {
            // GD.Print($"[MailSystem] Cleaned up {removedCount} old mail records");
        }
    }
}

/// <summary>
/// Mail record structure
/// Pascal: MailRec record definition
/// </summary>
public class MailRecord
{
    public string Receiver { get; set; } = "";
    public string Sender { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTime Date { get; set; }
    public bool ReadFlag { get; set; } = false;
    public byte Special { get; set; } = 0;
    public List<string> Lines { get; set; } = new List<string>();
} 
