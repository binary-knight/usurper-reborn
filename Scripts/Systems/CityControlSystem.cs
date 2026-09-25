using UsurperRemake.Systems;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// City Control System - Manages team control of the city
/// The team that controls the city receives:
/// - Discounts at shops (percentage off purchases)
/// - Share of sales tax (set by the King)
/// - Prestige and influence
///
/// IMPORTANT RULES:
/// - The King cannot be on a team (must leave team to become King)
/// - City controller and throne controller must be DIFFERENT entities
/// </summary>
public class CityControlSystem
{
    private static CityControlSystem? instance;
    public static CityControlSystem Instance => instance ??= new CityControlSystem();

    private Random random = new();

    // City control bonuses
    public const float ShopDiscountPercent = 10f;     // 10% discount at all shops
    public const int MinTaxSharePercent = 1;          // Minimum tax share (1%)
    public const int MaxTaxSharePercent = 10;         // Maximum tax share (10%)

    /// <summary>
    /// Get the team currently controlling the city (CTurf = true)
    /// Checks both NPCs and the current player.
    /// Filters out dead and imprisoned NPCs — they cannot hold city control.
    /// </summary>
    public string? GetControllingTeam()
    {
        // Check if the player controls the city
        var player = GameEngine.Instance?.CurrentPlayer;
        if (player != null && player.CTurf && !string.IsNullOrEmpty(player.Team) && player.HP > 0)
            return player.Team;

        var npcs = NPCSpawnSystem.Instance?.ActiveNPCs;
        if (npcs == null) return null;

        var controller = npcs.FirstOrDefault(n => n.CTurf && !string.IsNullOrEmpty(n.Team)
            && n.IsAlive && !n.IsDead && n.DaysInPrison <= 0);
        return controller?.Team;
    }

    /// <summary>
    /// Get all NPC members of the city-controlling team.
    /// Note: Use GetCityControlLeader() to get the actual leader (may be the player).
    /// </summary>
    public List<NPC> GetCityControllers()
    {
        var controllingTeam = GetControllingTeam();
        if (string.IsNullOrEmpty(controllingTeam)) return new List<NPC>();

        return NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => n.Team == controllingTeam && n.IsAlive && !n.IsDead && n.DaysInPrison <= 0)
            .ToList() ?? new List<NPC>();
    }

    /// <summary>
    /// Get the actual team leader - the highest level member including the player.
    /// Returns (name, bankGold, isPlayer) for the leader.
    /// </summary>
    public (string Name, long BankGold, bool IsPlayer) GetCityControlLeader()
    {
        var controllingTeam = GetControllingTeam();
        if (string.IsNullOrEmpty(controllingTeam))
            return ("None", 0, false);

        var controllers = GetCityControllers();
        var topNpc = controllers.OrderByDescending(n => n.Level).FirstOrDefault();

        // Check if the player is on the controlling team
        var player = GameEngine.Instance?.CurrentPlayer;
        bool playerOnTeam = player != null &&
            !string.IsNullOrEmpty(player.Team) &&
            player.Team == controllingTeam &&
            player.HP > 0;

        if (playerOnTeam)
        {
            // Player is the leader if they're the highest level member
            if (topNpc == null || player!.Level >= topNpc.Level)
                return (player!.Name2 ?? player.Name1, player.BankGold, true);
        }

        if (topNpc != null)
            return (topNpc.Name, topNpc.BankGold, false);

        return ("None", 0, false);
    }

    /// <summary>
    /// Check if a character is a member of the city-controlling team
    /// </summary>
    public bool IsCharacterCityController(Character character)
    {
        if (character == null || string.IsNullOrEmpty(character.Team))
            return false;

        return character.CTurf;
    }

    /// <summary>
    /// Calculate shop discount for a character
    /// </summary>
    public float GetShopDiscount(Character buyer)
    {
        if (IsCharacterCityController(buyer))
        {
            return ShopDiscountPercent / 100f; // 10% = 0.10
        }

        return 0f; // No discount
    }

    /// <summary>
    /// Apply discount to a price for city controllers
    /// </summary>
    public long ApplyDiscount(long originalPrice, Character buyer)
    {
        if (!IsCharacterCityController(buyer))
            return originalPrice;

        float discount = GetShopDiscount(buyer);
        long discountAmount = (long)(originalPrice * discount);

        return Math.Max(1, originalPrice - discountAmount);
    }

    /// <summary>
    /// Calculate and distribute city tax share from a sale
    /// Called when any shop makes a sale
    /// </summary>
    /// <summary>v1.1.13: the king's sales tax into the stored treasury (the shared court when online).</summary>
    internal static async System.Threading.Tasks.Task<bool> AddSalesTaxAsync(long kingShare)
    {
        try
        {
            bool ok = await CastleLocation.CourtChangeAsync(CastleLocation.TreasuryOsm(), court => { court.Treasury += kingShare; return true; });
            if (!ok) UsurperRemake.Systems.DebugLogger.Instance.LogWarning("TAX", $"Sales tax of {kingShare:N0} not added: the royal court kept changing or has no king.");
            return ok;
        }
        catch (Exception ex)
        {
            UsurperRemake.Systems.DebugLogger.Instance.LogError("TAX", $"Sales tax failed: {ex.Message}");
            return false;
        }
    }

    public void ProcessSaleTax(long saleAmount)
    {
        var king = CastleLocation.GetCurrentKing();
        if (king == null) return;

        // King's sales tax - goes directly to the royal treasury
        int kingTaxPercent = king.KingTaxPercent;
        if (kingTaxPercent > 0)
        {
            // Minimum 1 gold tax to match what the buyer was charged
            long kingShare = Math.Max(1, (saleAmount * kingTaxPercent) / 100);
            // v1.1.13: into the stored treasury as one guarded court change (not awaited: the sale is done);
            // the day's takings are counted in memory for the reports only; the daily reset does not credit them
            _ = AddSalesTaxAsync(kingShare);
            king.DailyTaxRevenue += kingShare;
        }

        // City controller's share - deposited to team leader's bank account
        int cityTaxPercent = king.CityTaxPercent;
        if (cityTaxPercent <= 0) return;

        // Minimum 1 gold tax to match what the buyer was charged
        long cityShare = Math.Max(1, (saleAmount * cityTaxPercent) / 100);

        // Find the team leader (highest level member, including the player)
        var leader = GetCityControlLeader();
        if (leader.Name == "None") return;

        if (leader.IsPlayer)
        {
            // Deposit to the player's bank account
            var player = GameEngine.Instance?.CurrentPlayer;
            if (player != null)
            {
                player.BankGold += cityShare;
                // v0.57.10: track tax income so the Town Hall menu can show
                // players what holding turf actually earned them. Weekly
                // counter resets on Monday daily reset; lifetime counter
                // accumulates forever.
                player.CityTaxEarnedThisWeek += cityShare;
                player.CityTaxEarnedLifetime += cityShare;
            }
        }
        else
        {
            // Deposit to the NPC leader's bank account
            var controllers = GetCityControllers();
            var npcLeader = controllers.OrderByDescending(n => n.Level).FirstOrDefault();
            if (npcLeader != null)
                npcLeader.BankGold += cityShare;
        }

        king.DailyCityTaxRevenue += cityShare;
    }

    /// <summary>
    /// Attempt to challenge for city control
    /// Returns true if the challenge was successful
    /// </summary>
    public bool ChallengeForCityControl(Character challenger, string challengerTeam)
    {
        if (string.IsNullOrEmpty(challengerTeam))
        {
            // GD.Print("[CityControl] Challenge failed: Challenger must be in a team");
            return false;
        }

        // RULE: King cannot be on a team / control the city
        var king = CastleLocation.GetCurrentKing();
        if (king != null && king.Name == challenger.Name2)
        {
            // GD.Print("[CityControl] Challenge failed: King cannot control the city");
            return false;
        }

        var currentControllers = GetCityControllers();
        var controllingTeam = GetControllingTeam();

        // If no one controls the city, easy takeover
        if (string.IsNullOrEmpty(controllingTeam) || currentControllers.Count == 0)
        {
            TransferCityControl(challengerTeam);
            NewsSystem.Instance?.Newsy(true, $"'{challengerTeam}' has taken control of the city!");
            return true;
        }

        // Get challenger's team members
        var challengingTeam = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => n.Team == challengerTeam && n.IsAlive && !n.IsDead && n.DaysInPrison <= 0)
            .ToList() ?? new List<NPC>();

        // Also include the player if they're on the challenging team
        var player = GameEngine.Instance?.CurrentPlayer;
        long challengerPlayerPower = 0;
        if (player != null && player.Team == challengerTeam && player.HP > 0)
            challengerPlayerPower = player.Level + player.Strength + player.Defence;

        if (challengingTeam.Count == 0 && challengerPlayerPower == 0)
        {
            return false;
        }

        // Calculate team power — include player on both sides
        long challengerPower = challengingTeam.Sum(m => m.Level + m.Strength + m.Defence) + challengerPlayerPower;
        long defenderPower = currentControllers.Sum(m => m.Level + m.Strength + m.Defence);

        // Include player in defender power if they're on the defending team
        if (player != null && player.Team == controllingTeam && player.HP > 0)
            defenderPower += player.Level + player.Strength + player.Defence;

        // Defender bonus: 10% home turf advantage
        defenderPower = (long)(defenderPower * 1.10);

        // Add randomness (+-20%)
        challengerPower += random.Next((int)(-challengerPower * 0.2), (int)(challengerPower * 0.2));
        defenderPower += random.Next((int)(-defenderPower * 0.2), (int)(defenderPower * 0.2));

        if (challengerPower > defenderPower)
        {
            // Challengers win!
            TransferCityControl(challengerTeam);
            NewsSystem.Instance?.Newsy(true,
                $"'{challengerTeam}' defeated '{controllingTeam}' and now controls the city!");
            return true;
        }
        else
        {
            // Defenders hold
            NewsSystem.Instance?.Newsy(true,
                $"'{controllingTeam}' successfully defended the city against '{challengerTeam}'!");
            return false;
        }
    }

    /// <summary>
    /// Transfer city control to a new team
    /// </summary>
    private void TransferCityControl(string newControllingTeam)
    {
        var npcs = NPCSpawnSystem.Instance?.ActiveNPCs;
        if (npcs == null) return;

        // Remove control from old team
        foreach (var npc in npcs.Where(n => n.CTurf))
        {
            npc.CTurf = false;
        }

        // Also clear player's CTurf if their team lost control
        var player = GameEngine.Instance?.CurrentPlayer;
        if (player != null && player.CTurf && player.Team != newControllingTeam)
        {
            player.CTurf = false;
        }

        // Give control to new team (only alive, non-dead, non-imprisoned members)
        foreach (var npc in npcs.Where(n => n.Team == newControllingTeam && n.IsAlive && !n.IsDead && n.DaysInPrison <= 0))
        {
            npc.CTurf = true;
            npc.TeamRec = 0;
        }

        // If the player is on the new controlling team, give them CTurf too
        if (player != null && player.Team == newControllingTeam)
        {
            player.CTurf = true;
        }
    }

    /// <summary>
    /// Remove city control from a team (e.g., when team disbands)
    /// </summary>
    public void RemoveCityControl(string teamName)
    {
        var npcs = NPCSpawnSystem.Instance?.ActiveNPCs;
        if (npcs == null) return;

        foreach (var npc in npcs.Where(n => n.Team == teamName))
        {
            npc.CTurf = false;
        }

        // Also clear player's CTurf if they're on the disbanded team
        var player = GameEngine.Instance?.CurrentPlayer;
        if (player != null && player.CTurf && player.Team == teamName)
        {
            player.CTurf = false;
        }

        NewsSystem.Instance?.Newsy(true, $"The city is no longer under team control!");
    }

    /// <summary>
    /// Check if a King challenge conflicts with city control rules
    /// King cannot be on a team - return true if they need to leave their team first
    /// </summary>
    public bool WouldViolateKingTeamRule(Character potentialKing)
    {
        return !string.IsNullOrEmpty(potentialKing.Team);
    }

    /// <summary>
    /// Force a character to leave their team (for becoming King)
    /// </summary>
    public void ForceLeaveTeam(Character character)
    {
        if (string.IsNullOrEmpty(character.Team)) return;

        string oldTeam = character.Team;

        character.Team = "";
        character.TeamPW = "";
        character.CTurf = false;
        character.TeamRec = 0;

        NewsSystem.Instance?.Newsy(true, $"{character.Name2} has left '{oldTeam}'.");
        // GD.Print($"[CityControl] {character.Name2} forced to leave team '{oldTeam}'");
    }

    /// <summary>
    /// Get city control status message for display
    /// </summary>
    public string GetCityStatusMessage()
    {
        var controllingTeam = GetControllingTeam();

        if (string.IsNullOrEmpty(controllingTeam))
        {
            return Loc.Get("city.no_control");
        }

        var info = GetCityControlInfo();
        return Loc.Get("city.team_controls", controllingTeam, info.MemberCount);
    }

    /// <summary>
    /// Get detailed city control info (includes the player if they're on the controlling team)
    /// </summary>
    public (string TeamName, int MemberCount, long TotalPower) GetCityControlInfo()
    {
        var controllingTeam = GetControllingTeam();
        if (string.IsNullOrEmpty(controllingTeam))
            return ("None", 0, 0);

        var controllers = GetCityControllers();
        int memberCount = controllers.Count;
        long power = controllers.Sum(m => m.Level + m.Strength + m.Defence);

        // Include the player in count and power if they're on the controlling team
        var player = GameEngine.Instance?.CurrentPlayer;
        if (player != null &&
            !string.IsNullOrEmpty(player.Team) &&
            player.Team == controllingTeam &&
            player.HP > 0)
        {
            memberCount++;
            power += player.Level + player.Strength + player.Defence;
        }

        return (controllingTeam, memberCount, power);
    }

    /// <summary>
    /// Calculate the total price including tax on top of the base price.
    /// Tax is added ON TOP — the buyer pays more than the listed price.
    /// King's tax goes to the royal treasury, city tax goes to the controlling team.
    /// </summary>
    public static (long kingTax, long cityTax, long total) CalculateTaxedPrice(long basePrice)
    {
        var king = CastleLocation.GetCurrentKing();
        if (king == null)
            return (0, 0, basePrice);

        // Minimum 1 gold tax when rate is active, so even cheap items show tax
        long kingTax = king.KingTaxPercent > 0 ? Math.Max(1, (basePrice * king.KingTaxPercent) / 100) : 0;
        long cityTax = king.CityTaxPercent > 0 ? Math.Max(1, (basePrice * king.CityTaxPercent) / 100) : 0;

        return (kingTax, cityTax, basePrice + kingTax + cityTax);
    }

    /// <summary>
    /// Calculate taxed price for healing services with a 15% combined tax cap.
    /// Prevents death spiral from high king+city taxes stacking on healing.
    /// </summary>
    public static (long kingTax, long cityTax, long total) CalculateHealingTaxedPrice(long basePrice)
    {
        var king = CastleLocation.GetCurrentKing();
        if (king == null)
            return (0, 0, basePrice);

        // Cap combined healing tax at 15% to prevent death spiral
        const int MaxHealingTaxPercent = 15;
        int kingPct = king.KingTaxPercent;
        int cityPct = king.CityTaxPercent;
        if (kingPct + cityPct > MaxHealingTaxPercent)
        {
            // Proportionally reduce both rates to fit within the cap
            double ratio = (double)MaxHealingTaxPercent / (kingPct + cityPct);
            kingPct = (int)(kingPct * ratio);
            cityPct = MaxHealingTaxPercent - kingPct;
        }

        long kingTax = kingPct > 0 ? Math.Max(1, (basePrice * kingPct) / 100) : 0;
        long cityTax = cityPct > 0 ? Math.Max(1, (basePrice * cityPct) / 100) : 0;

        return (kingTax, cityTax, basePrice + kingTax + cityTax);
    }

    /// <summary>
    /// Display a tax breakdown to the player showing item cost, king's tax, city tax, and total.
    /// Only shows breakdown when taxes are active (at least one > 0%).
    /// </summary>
    public void DisplayTaxBreakdown(TerminalEmulator terminal, string itemName, long basePrice)
    {
        var king = CastleLocation.GetCurrentKing();
        if (king == null) return;

        // Minimum 1 gold tax when rate is active, so even cheap items show tax
        long kingTax = king.KingTaxPercent > 0 ? Math.Max(1, (basePrice * king.KingTaxPercent) / 100) : 0;
        long cityTax = king.CityTaxPercent > 0 ? Math.Max(1, (basePrice * king.CityTaxPercent) / 100) : 0;

        // No breakdown needed if no taxes
        if (kingTax <= 0 && cityTax <= 0) return;

        long total = basePrice + kingTax + cityTax;

        terminal.WriteLine("");
        terminal.WriteLine($"  {itemName}: {basePrice:N0} {Loc.Get("ui.gold").ToLower()}", "white");
        if (kingTax > 0)
            terminal.WriteLine($"  {Loc.Get("city.kings_tax", king.KingTaxPercent)}: {kingTax:N0} {Loc.Get("ui.gold").ToLower()}", "yellow");
        if (cityTax > 0)
        {
            var controllingTeam = GetControllingTeam();
            string cityLabel = !string.IsNullOrEmpty(controllingTeam)
                ? Loc.Get("city.city_tax_to", king.CityTaxPercent, controllingTeam)
                : Loc.Get("city.city_tax", king.CityTaxPercent);
            terminal.WriteLine($"  {cityLabel}: {cityTax:N0} {Loc.Get("ui.gold").ToLower()}", "cyan");
        }
        if (!GameConfig.ScreenReaderMode)
            terminal.WriteLine($"  ─────────────────────────────", "gray");
        terminal.WriteLine($"  {Loc.Get("city.total")}: {total:N0} {Loc.Get("ui.gold").ToLower()}", "bright_white");
    }
}
