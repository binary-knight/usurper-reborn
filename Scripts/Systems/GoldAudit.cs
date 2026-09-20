using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems;

/// <summary>
/// v1.1.7: the signatures the old audit missed. It compared held wealth against lifetime earnings,
/// so an account that sold a duped item for billions looked honest: the sale was the earnings.
///
/// These are alerts for the maintainer's local log, never punishment and never an automatic edit.
/// Each rule fires once per player per session per subject, so a thousand autosaves do not repeat
/// it, while a new item or a new rule still reports. Nothing is transmitted off the machine.
/// </summary>
public static class GoldAudit
{
    // player -> the set of "rule:subject" already reported this run
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _reported = new();

    /// <summary>Per-level alert thresholds. Starting values, not statements of what is possible.</summary>
    public const long GoldFromSellingPerLevel = 2_000_000;
    public const long HighestSingleHitPerLevel = 5_000;

    internal static void Reset(string playerName) => _reported.TryRemove(Key(playerName), out _);

    private static string Key(string playerName) => (playerName ?? "").ToLowerInvariant();

    private static bool FirstTime(string playerName, string subject) =>
        _reported.GetOrAdd(Key(playerName), _ => new ConcurrentDictionary<string, byte>()).TryAdd(subject, 0);

    /// <summary>True when this alert was written: the same subject is reported once per session.</summary>
    private static bool Report(string playerName, string subject, string message)
    {
        if (!FirstTime(playerName, subject)) return false;
        DebugLogger.Instance.LogInfo("GOLD_AUDIT", $"SUSPICIOUS ({subject}): {playerName} {message}");
        return true;
    }

    /// <summary>
    /// Runs at save time on the player's raw numbers, before anything is corrected, so the evidence
    /// survives in the log. Returns how many alerts were actually written, so a repeat save returns
    /// zero even while the same conditions hold.
    /// </summary>
    public static int Inspect(Character player)
    {
        if (player == null) return 0;
        string name = player.DisplayName ?? player.Name2 ?? player.Name1 ?? "";
        int level = Math.Max(1, player.Level);
        var stats = player.Statistics;
        int fired = 0;

        long wealth = SafeAdd(player.Gold, player.BankGold);
        long earned = stats?.TotalGoldEarned ?? 0;
        if (wealth > 0 && earned > 0 && wealth > SafeMul(earned, 5))
        {
            if (Report(name, "wealth-vs-earned", $"wealth={wealth:N0} (gold={player.Gold:N0}, bank={player.BankGold:N0}) but totalEarned={earned:N0}")) fired++;
        }

        long sold = stats?.TotalGoldFromSelling ?? 0;
        long items = Math.Max(0, stats?.TotalItemsSold ?? 0);
        if (sold > 0 && items > 0)
        {
            long perItem = sold / items;
            // the most a single item can fetch: the value ceiling at the best fence in the game
            long ceiling = (long)(GameConfig.MaxItemValue * 0.8);
            if (perItem > ceiling)
            {
                if (Report(name, "gold-per-item", $"sold {items:N0} item(s) for {sold:N0}g, {perItem:N0}g each, above the {ceiling:N0}g a single item can fetch")) fired++;
            }
        }
        if (sold > SafeMul(GoldFromSellingPerLevel, level))
        {
            if (Report(name, "lifetime-sales", $"Lv{level} has sold {sold:N0}g in total")) fired++;
        }

        long hit = stats?.HighestSingleHit ?? 0;
        if (hit > SafeMul(HighestSingleHitPerLevel, level))
        {
            if (Report(name, "highest-hit", $"Lv{level} landed a single hit of {hit:N0}")) fired++;
        }

        foreach (var (where, item) in CarriedItems(player))
        {
            if (item == null) continue;
            // reporting only: Item.Clone is a memberwise copy that shares MagicProperties and the
            // effects list, so clamping a "copy" would quietly change the real item
            if (!item.ExceedsBounds(out var over)) continue;
            // after the load-time heal, an item over a ceiling means a hole that is still open
            if (Report(name, $"item:{where}:{item.Name}", $"{where} item '{item.Name}' is over the bounds: {over}")) fired++;
        }
        foreach (var worn in WornItems(player))
        {
            if (Report(name, $"item:worn:{worn.Name}", $"worn item '{worn.Name}' is over the bounds: {worn.Over}")) fired++;
        }

        return fired;
    }

    private readonly record struct WornOver(string Name, string Over);

    private static IEnumerable<(string Where, global::Item? Item)> CarriedItems(Character player)
    {
        foreach (var item in player.Inventory ?? new List<global::Item>())
            yield return ("carried", item);
    }

    private static IEnumerable<WornOver> WornItems(Character player)
    {
        foreach (var id in (player.EquippedItems ?? new Dictionary<EquipmentSlot, int>()).Values)
        {
            var equip = EquipmentDatabase.GetById(id);
            if (equip != null && equip.ExceedsBounds(out var over))
                yield return new WornOver(equip.Name, over);
        }
    }

    private static long SafeAdd(long a, long b) { try { return checked(a + b); } catch (OverflowException) { return long.MaxValue; } }
    private static long SafeMul(long a, long b) { try { return checked(a * b); } catch (OverflowException) { return long.MaxValue; } }
}
