using System;
using System.Globalization;
using System.Threading.Tasks;
using UsurperRemake.BBS;

namespace UsurperRemake.Systems;

/// <summary>
/// v1.2 (design item D): the bank's robbery reserve. It used to be a process-wide static
/// in BankLocation that restarted at 500,000 on every server start and was one number for
/// every session. It is now one persisted value per world: WorldStateData.BankVaultReserve
/// in single-player, the "bank_vault" world_state row online, where every change goes
/// through the backend's optimistic-concurrency update so two robbers cannot both take the
/// same gold. Player bank balances are never touched by anyone else's robbery.
/// </summary>
public static class BankVaultSystem
{
    public const string WorldStateKey = "bank_vault";

    /// <summary>The two backend calls the vault needs, so tests can supply a fake store.</summary>
    public interface IVaultStore
    {
        Task<string?> Load(string key);
        Task<bool> TryAtomicUpdate(string key, Func<string, string> transform);
    }

    private sealed class BackendStore : IVaultStore
    {
        private readonly IOnlineSaveBackend _backend;
        public BackendStore(IOnlineSaveBackend backend) { _backend = backend; }
        public Task<string?> Load(string key) => _backend.LoadWorldState(key);
        public Task<bool> TryAtomicUpdate(string key, Func<string, string> transform) => _backend.TryAtomicUpdate(key, transform);
    }

    /// <summary>1.2.4: the robbery attempts counter's world_state row, "yyyy-MM-dd|count".</summary>
    public const string RobberyCounterKey = "bank_robberies";

    private static long _reserve = GameConfig.BankVaultInitial;

    // 1.2.4: robbery attempts today, one counter for the world (it was a per-process static in
    // BankLocation, reset on every restart). Each attempt adds two guards until the day changes.
    private static int _robberies;
    private static string _robberyDate = "";

    /// <summary>Tests set this to move the calendar; production uses the local clock.</summary>
    internal static Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    private static string Today => Clock().Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Tests set this to a fake store; production resolves the online backend.</summary>
    internal static IVaultStore? StoreOverride { get; set; }

    private static IVaultStore? Store
    {
        get
        {
            if (StoreOverride != null) return StoreOverride;
            if (!DoorMode.IsOnlineMode) return null;
            return SaveSystem.Instance?.Backend is IOnlineSaveBackend online ? new BackendStore(online) : null;
        }
    }

    /// <summary>The last known reserve. Online it is refreshed on bank entry and after every change.</summary>
    public static long Current => _reserve;

    /// <summary>Single-player restore, and tests.</summary>
    public static void Load(long value) => _reserve = Math.Max(0, value);

    /// <summary>Online: read the shared rows. A missing row is the initial reserve and no robberies.</summary>
    public static async Task Refresh()
    {
        var store = Store;
        if (store == null) return;
        try
        {
            var json = await store.Load(WorldStateKey);
            _reserve = Parse(json);
            (_robberyDate, _robberies) = ParseRobberies(await store.Load(RobberyCounterKey));
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogWarning("BANK", $"Vault refresh failed: {ex.Message}");
        }
    }

    /// <summary>Robbery attempts made today. A count saved on an earlier day reads as zero.</summary>
    public static int RobberiesToday => _robberyDate == Today ? _robberies : 0;

    /// <summary>The day the stored count belongs to (single-player save).</summary>
    public static string RobberyDate => _robberyDate;

    /// <summary>The stored count as saved, whatever its day (single-player save).</summary>
    public static int RobberyCount => _robberies;

    /// <summary>Single-player restore, and tests.</summary>
    public static void LoadRobberies(int count, string? date)
    {
        _robberies = Math.Max(0, count);
        _robberyDate = date ?? "";
    }

    /// <summary>
    /// Count one robbery attempt. Online the shared row is updated atomically, so two robbers
    /// on different sessions both count; a change that loses the race three times is kept in
    /// the cached copy, since the counter only adds guards.
    /// </summary>
    public static async Task RecordRobberyAttempt()
    {
        string today = Today;
        (string, int) Next((string Date, int Count) cur) => (today, (cur.Date == today ? cur.Count : 0) + 1);

        var store = Store;
        if (store != null)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                (string, int) written = default;
                bool ok;
                try
                {
                    ok = await store.TryAtomicUpdate(RobberyCounterKey, json =>
                    {
                        written = Next(ParseRobberies(json));
                        return FormatRobberies(written);
                    });
                }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogWarning("BANK", $"Robbery counter update failed: {ex.Message}");
                    ok = false;
                }
                if (ok)
                {
                    (_robberyDate, _robberies) = written;
                    return;
                }
            }
            DebugLogger.Instance.LogWarning("BANK", "Robbery counter update lost the race three times; applied to the cached count only");
        }
        (_robberyDate, _robberies) = Next((_robberyDate, _robberies));
    }

    private static (string Date, int Count) ParseRobberies(string? row)
    {
        var parts = (row ?? "").Trim().Split('|');
        if (parts.Length == 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return (parts[0], Math.Max(0, n));
        return ("", 0);
    }

    private static string FormatRobberies((string Date, int Count) v) =>
        v.Date + "|" + v.Count.ToString(CultureInfo.InvariantCulture);

    public static Task Deposit(long amount) => amount <= 0 ? Task.CompletedTask : Mutate(v => SafeAdd(v, amount));

    public static Task Withdraw(long amount) => amount <= 0 ? Task.CompletedTask : Mutate(v => Math.Max(0, v - amount));

    /// <summary>
    /// Take the robbery cut: a quarter of what is in the vault beyond the robber's own
    /// deposits, capped. Returns the amount actually removed, which is what the robber is
    /// credited; a second robber arriving first gets the reduced remainder, never a stale copy.
    /// </summary>
    public static async Task<(long Stolen, bool Landed)> Rob(long robberOwnBankGold)
    {
        long stolen = 0;
        bool landed = await Mutate(v =>
        {
            stolen = RobberyTake(v, robberOwnBankGold);
            return Math.Max(0, v - stolen);
        }, fallbackToCache: false);
        // A robber who has not been paid is not short: if the shared row lost the race three
        // times, nothing was removed from it, so nothing is credited.
        return landed ? (stolen, true) : (0, false);
    }

    public static long RobberyTake(long reserve, long robberOwnBankGold)
    {
        long othersGold = Math.Max(0, reserve - Math.Max(0, robberOwnBankGold));
        return Math.Min(othersGold / 4, GameConfig.BankRobberyMaxTake);
    }

    /// <summary>Once per world day: a flat refill plus a percentage, up to the cap.</summary>
    public static Task DailyRefill() => Mutate(v =>
    {
        long grown = SafeAdd(v, GameConfig.BankVaultDailyRefill + v / 100 * GameConfig.BankVaultRefillRatePercent);
        return Math.Min(grown, GameConfig.BankVaultCap);
    });

    /// <summary>
    /// Apply <paramref name="f"/> to the reserve. Returns true when the shared row (or the
    /// single-player value) took the change. With <paramref name="fallbackToCache"/>, a change
    /// that lost the race three times is applied to the cached copy instead and true is
    /// returned: right for deposits, withdrawals and the refill, where the player's own gold
    /// has already moved and the reserve is an aggregate that is re-read on the next entry.
    /// </summary>
    private static async Task<bool> Mutate(Func<long, long> f, bool fallbackToCache = true)
    {
        var store = Store;
        if (store == null)
        {
            _reserve = f(_reserve);
            return true;
        }
        for (int attempt = 0; attempt < 3; attempt++)
        {
            long written = 0;
            bool ok;
            try
            {
                ok = await store.TryAtomicUpdate(WorldStateKey, json =>
                {
                    written = f(Parse(json));
                    return written.ToString(CultureInfo.InvariantCulture);
                });
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("BANK", $"Vault update failed: {ex.Message}");
                ok = false;
            }
            if (ok)
            {
                _reserve = written;
                return true;
            }
        }
        if (!fallbackToCache)
        {
            DebugLogger.Instance.LogWarning("BANK", "Vault update lost the race three times; the change was not applied");
            return false;
        }
        DebugLogger.Instance.LogWarning("BANK", "Vault update lost the race three times; applied to the cached reserve only");
        _reserve = f(_reserve);
        return true;
    }

    private static long Parse(string? json) =>
        long.TryParse((json ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Max(0, v) : GameConfig.BankVaultInitial;

    private static long SafeAdd(long current, long amount)
    {
        if (amount <= 0) return current;
        if (current > long.MaxValue - amount) return long.MaxValue;
        return current + amount;
    }
}
