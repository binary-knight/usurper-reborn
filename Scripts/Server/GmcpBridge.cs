using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UsurperRemake.Systems;

namespace UsurperRemake.Server;

/// <summary>
/// v0.57.21: GMCP (Generic MUD Communication Protocol) out-of-band event emitter.
///
/// GMCP is a telnet subnegotiation extension supported by Mudlet, MUSHclient, TinTin++,
/// and other modern MUD clients. The server emits structured JSON payloads inside
/// IAC SB GMCP framing; clients parse them into dedicated UI panes (status bars,
/// gauges, map widgets) instead of dumping raw text into the scroll buffer.
///
/// Wire format (per the GMCP spec):
///   IAC (0xFF) SB (0xFA) GMCP (0xC9) "Package.Message" SP "{json}" IAC (0xFF) SE (0xF0)
///
/// Two important framing rules:
///  - 0xFF bytes inside the payload MUST be doubled (0xFF 0xFF) so the SE terminator
///    is unambiguous. JsonSerializer.Serialize never emits 0xFF in valid UTF-8 JSON
///    output (no Unicode codepoint encodes to a single 0xFF byte), but the package
///    name comes from caller-controlled strings so we escape defensively anyway.
///  - The package name and JSON payload are separated by a single space (0x20).
///
/// This bridge is a no-op when:
///  - SessionContext.Current is null (single-player, BBS door, --local mode).
///  - SessionContext.Current.GmcpEnabled is false (client did not respond DO GMCP).
///  - The session's OutputStream is null or closed.
///
/// Mirrors the ElectronBridge.Emit pattern — same call sites, different transport.
/// </summary>
public static class GmcpBridge
{
    private static readonly byte[] IacSbGmcp = new byte[] { 0xFF, 0xFA, 0xC9 }; // IAC SB GMCP
    private static readonly byte[] IacSe = new byte[] { 0xFF, 0xF0 };           // IAC SE
    private static readonly byte Space = 0x20;

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // GMCP payloads are tiny (one Char.Vitals = ~80 bytes); compact serialization
        // matches conventions of every other GMCP-speaking server (Achaea, Iron Realms).
        WriteIndented = false,
        // v0.60.3: cap nesting at 8 levels so a hostile or buggy caller can't pass a
        // self-referential object graph that blows the stack inside the serializer.
        MaxDepth = 8,
        // Skip null-valued properties to keep frames compact -- the GMCP convention
        // is omit-when-empty, and clients (Mudlet/MUSHclient/TT++) treat missing
        // fields the same as null.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // v0.60.3: hardening constants. Frames larger than MaxPayloadBytes are dropped
    // with a warning rather than blasted at the client; per-session consecutive
    // errors are tracked on SessionContext and after MaxConsecutiveErrors successive
    // emit failures, GMCP is disabled for that session to stop spamming the log.
    private const int MaxPayloadBytes = 16384;
    private const int MaxConsecutiveErrors = 5;
    private static readonly Regex PackageNameValidator = new(
        @"^[A-Za-z][A-Za-z0-9._-]*$", RegexOptions.Compiled);

    /// <summary>
    /// True if there's an active GMCP-enabled session on the current async flow.
    /// Cheap check call sites can use to skip JSON serialization when GMCP is off.
    /// </summary>
    public static bool IsActive
    {
        get
        {
            var ctx = SessionContext.Current;
            return ctx != null && ctx.GmcpEnabled && ctx.OutputStream != null;
        }
    }

    /// <summary>
    /// v0.60.3: emit Char.Vitals if any tracked stat changed since the last emit on
    /// this session. Cheap no-op when GMCP isn't negotiated. Combat, dungeon room
    /// loops, and any other flow that doesn't re-enter BaseLocation.LocationLoop can
    /// call this directly so MUD clients see live HP/MP/SP gauges throughout the fight
    /// instead of frozen pre-combat values until the menu redraws.
    /// </summary>
    public static void EmitVitalsIfChanged(global::Character? player)
    {
        if (player == null || !IsActive) return;
        var ctx = SessionContext.Current;
        if (ctx == null) return;

        long hp = player.HP, maxHp = player.MaxHP;
        long mana = player.Mana, maxMana = player.MaxMana;
        long sta = player.Stamina;
        if (hp == ctx.LastGmcpHp && maxHp == ctx.LastGmcpMaxHp
            && mana == ctx.LastGmcpMana && maxMana == ctx.LastGmcpMaxMana
            && sta == ctx.LastGmcpStamina)
            return;

        ctx.LastGmcpHp = hp; ctx.LastGmcpMaxHp = maxHp;
        ctx.LastGmcpMana = mana; ctx.LastGmcpMaxMana = maxMana;
        ctx.LastGmcpStamina = sta;

        Emit("Char.Vitals", new { hp, maxHp, mp = mana, maxMp = maxMana, sp = sta });
    }

    /// <summary>
    /// Emit Char.Status if gold, bank balance, XP, or level changed since the last
    /// emit on this session. Called on every location-loop iteration and at combat
    /// end so MUD clients always see up-to-date wealth and progression without
    /// waiting for the next location transition. Cheap no-op when GMCP isn't
    /// negotiated.
    /// </summary>
    public static void EmitStatusIfChanged(global::Character? player)
    {
        if (player == null || !IsActive) return;
        var ctx = SessionContext.Current;
        if (ctx == null) return;

        long gold = player.Gold;
        long bank = player.BankGold;
        long xp   = player.Experience;
        int  level = player.Level;

        if (gold == ctx.LastGmcpGold && bank == ctx.LastGmcpBankGold
            && xp == ctx.LastGmcpXp && level == ctx.LastGmcpLevel)
            return;

        ctx.LastGmcpGold    = gold;
        ctx.LastGmcpBankGold = bank;
        ctx.LastGmcpXp      = xp;
        ctx.LastGmcpLevel   = level;

        Emit("Char.Status", new
        {
            name     = player.Name2 ?? player.Name1,
            @class   = player.Class.ToString(),
            level,
            race     = player.Race.ToString(),
            gold,
            bank,
            xp,
            location = player.CurrentLocation
        });
    }

    /// <summary>
    /// v0.60.3: emit Char.Items.List with the full backpack contents and equipped
    /// items. Called at character login and on combat end (post-loot pickup).
    /// MUD client scripts can wire this to inventory panes / equipment displays
    /// without text-pattern parsing the [I]nventory output. Cheap no-op when GMCP
    /// isn't negotiated.
    /// </summary>
    public static void EmitItemsList(global::Character? player)
    {
        if (player == null || !IsActive) return;

        // Backpack
        var inv = new System.Collections.Generic.List<object>();
        if (player.Inventory != null)
        {
            for (int i = 0; i < player.Inventory.Count; i++)
            {
                var it = player.Inventory[i];
                if (it == null) continue;
                inv.Add(new
                {
                    idx = i,
                    name = it.Name ?? "?",
                    type = it.Type.ToString(),
                    minLevel = it.MinLevel
                });
            }
        }

        // Equipped slots — emit slot name + item name only (not the full item blob).
        // Clients that need item details can reference the inventory by name.
        var equipped = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (player.EquippedItems != null)
        {
            foreach (var (slot, id) in player.EquippedItems)
            {
                if (id <= 0) continue;
                var eq = player.GetEquipment(slot);
                if (eq != null && !string.IsNullOrEmpty(eq.Name))
                    equipped[slot.ToString().ToLowerInvariant()] = eq.Name;
            }
        }

        Emit("Char.Items.List", new
        {
            inventory = inv,
            equipped,
            count = inv.Count,
            max = GameConfig.MaxInventoryItems
        });
    }

    /// <summary>
    /// v0.60.3: emit Char.Skills.List with the player's learned class abilities
    /// and current cooldown state. Called at character login and after each
    /// combat round (cooldowns tick down per round). Spells emitted as a separate
    /// 'spells' array so MUD client scripts can render the two skill types
    /// independently if they want. Cheap no-op when GMCP isn't negotiated.
    /// </summary>
    public static void EmitSkillsList(global::Character? player)
    {
        if (player == null || !IsActive) return;

        var abilities = new System.Collections.Generic.List<object>();
        foreach (var ability in ClassAbilitySystem.GetAvailableAbilities(player))
        {
            if (ability == null) continue;
            abilities.Add(new
            {
                id = ability.Id,
                name = ability.Name,
                type = ability.Type.ToString(),
                cooldown = ability.Cooldown,
                staminaCost = ability.StaminaCost,
                manaCost = ability.ManaCost,
                requiresLevel = ability.LevelRequired
            });
        }

        var spells = new System.Collections.Generic.List<object>();
        foreach (var spell in SpellSystem.GetAvailableSpells(player))
        {
            if (spell == null) continue;
            spells.Add(new
            {
                name = spell.Name,
                manaCost = spell.ManaCost,
                requiresLevel = spell.LevelRequired
            });
        }

        Emit("Char.Skills.List", new
        {
            abilities,
            spells,
            mana = player.Mana,
            maxMana = player.MaxMana,
            stamina = player.Stamina
        });
    }

    /// <summary>
    /// Emit a GMCP frame to the current session's output stream.
    /// Package format follows the GMCP convention: "Module.Submodule" (e.g. "Char.Vitals",
    /// "Room.Info", "Comm.Channel.Text"). Payload is JSON-serialized with camelCase property
    /// names. Silently no-ops when GMCP is not active for the current session.
    /// </summary>
    public static void Emit(string package, object? payload)
    {
        var ctx = SessionContext.Current;
        if (ctx == null || !ctx.GmcpEnabled) return;
        TryWriteFrame(ctx, package, payload, ctx.Username);
    }

    /// <summary>
    /// Emit a GMCP frame to a specific session's output stream regardless of which
    /// session is "current." Used by chat fan-out paths where the sender's
    /// SessionContext is current but the GMCP frame needs to reach the recipient.
    /// Silently no-ops when the target session has GmcpEnabled=false.
    /// </summary>
    public static void EmitTo(PlayerSession? session, string package, object? payload)
    {
        if (session?.Context == null || !session.Context.GmcpEnabled) return;
        TryWriteFrame(session.Context, package, payload, session.Username);
    }

    /// <summary>
    /// v0.60.3: centralized frame-write path used by Emit and EmitTo. Validates the
    /// package name, builds the frame, enforces the payload size cap, writes under
    /// the per-stream lock, and updates the per-session consecutive-error counter.
    /// After MaxConsecutiveErrors successive failures the session's GmcpEnabled flag
    /// is flipped off so we stop hammering a broken connection. Successful writes
    /// reset the counter to zero.
    /// </summary>
    private static void TryWriteFrame(SessionContext ctx, string package, object? payload, string label)
    {
        var stream = ctx.OutputStream;
        if (stream == null || !stream.CanWrite) return;

        // Validate package name format. Bad input here is a programmer error -- log
        // once and refuse to emit so we don't put garbage on the wire.
        if (string.IsNullOrEmpty(package) || !PackageNameValidator.IsMatch(package))
        {
            DebugLogger.Instance.LogWarning("GMCP",
                $"Refusing to emit invalid package name '{package}' for session '{label}'");
            RecordEmitError(ctx);
            return;
        }

        byte[] frame;
        try
        {
            frame = BuildFrame(package, payload);
        }
        catch (Exception ex)
        {
            // JSON serialization can throw on circular graphs, MaxDepth violations,
            // or types JsonSerializer doesn't handle. Don't propagate -- the GMCP
            // emit is fire-and-forget, callers don't expect exceptions.
            DebugLogger.Instance.LogWarning("GMCP",
                $"BuildFrame '{package}' for '{label}' failed: {ex.GetType().Name}: {ex.Message}");
            RecordEmitError(ctx);
            return;
        }

        if (frame.Length > MaxPayloadBytes)
        {
            DebugLogger.Instance.LogWarning("GMCP",
                $"Dropping oversized frame: package='{package}', size={frame.Length}b > cap={MaxPayloadBytes}b, session='{label}'");
            RecordEmitError(ctx);
            return;
        }

        try
        {
            // Synchronous Write — GMCP frames are small and writing async would
            // interleave with other output unpredictably. Per-stream lock prevents
            // concurrent writes from different emit sites within the same session.
            lock (stream)
            {
                stream.Write(frame, 0, frame.Length);
                stream.Flush();
            }
            // Successful emit -- reset the consecutive-error counter.
            if (ctx.GmcpConsecutiveErrors != 0) ctx.GmcpConsecutiveErrors = 0;
        }
        catch (IOException)
        {
            // Connection dropped mid-emit — main read/write loop will clean up.
            // Don't count toward error threshold (this is normal disconnect).
        }
        catch (ObjectDisposedException)
        {
            // Stream closed while we held the reference. Same handling as IOException.
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogWarning("GMCP",
                $"Emit '{package}' for '{label}' failed: {ex.GetType().Name}: {ex.Message}");
            RecordEmitError(ctx);
        }
    }

    /// <summary>
    /// Increment the per-session consecutive-error counter. After MaxConsecutiveErrors
    /// failures in a row, GmcpEnabled is flipped off so we stop spamming the log on a
    /// session that's clearly not handling our frames. The session can re-enable GMCP
    /// only by negotiating it again (which would require reconnect in current design).
    /// </summary>
    private static void RecordEmitError(SessionContext ctx)
    {
        ctx.GmcpConsecutiveErrors++;
        if (ctx.GmcpConsecutiveErrors >= MaxConsecutiveErrors && ctx.GmcpEnabled)
        {
            ctx.GmcpEnabled = false;
            DebugLogger.Instance.LogWarning("GMCP",
                $"Disabling GMCP for session '{ctx.Username}' after {ctx.GmcpConsecutiveErrors} consecutive emit failures.");
        }
    }

    /// <summary>
    /// Build a complete GMCP frame as raw bytes. Exposed internal for unit testing —
    /// callers should use Emit() in production code.
    /// </summary>
    internal static byte[] BuildFrame(string package, object? payload)
    {
        // UTF-8 encode package name with 0xFF doubling.
        byte[] pkgBytes = EscapeIacBytes(Encoding.UTF8.GetBytes(package ?? ""));

        // Serialize payload as JSON. Null payload is permitted — some GMCP messages
        // are pure event signals with no body (e.g. Comm.Channel.Start).
        byte[] jsonBytes = payload == null
            ? Array.Empty<byte>()
            : EscapeIacBytes(JsonSerializer.SerializeToUtf8Bytes(payload, PayloadOptions));

        int total = IacSbGmcp.Length + pkgBytes.Length + (jsonBytes.Length > 0 ? 1 + jsonBytes.Length : 0) + IacSe.Length;
        var frame = new byte[total];
        int offset = 0;

        Buffer.BlockCopy(IacSbGmcp, 0, frame, offset, IacSbGmcp.Length);
        offset += IacSbGmcp.Length;
        Buffer.BlockCopy(pkgBytes, 0, frame, offset, pkgBytes.Length);
        offset += pkgBytes.Length;

        if (jsonBytes.Length > 0)
        {
            frame[offset++] = Space;
            Buffer.BlockCopy(jsonBytes, 0, frame, offset, jsonBytes.Length);
            offset += jsonBytes.Length;
        }

        Buffer.BlockCopy(IacSe, 0, frame, offset, IacSe.Length);
        return frame;
    }

    /// <summary>
    /// Per-RFC, 0xFF bytes inside an SB block must be doubled (sent as 0xFF 0xFF) so
    /// the IAC SE terminator (0xFF 0xF0) is unambiguous. Returns the input unchanged
    /// when no escaping is needed (the common case for all-ASCII package names and
    /// JSON payloads).
    /// </summary>
    internal static byte[] EscapeIacBytes(byte[] input)
    {
        if (input == null || input.Length == 0) return Array.Empty<byte>();

        // Fast path: scan for any 0xFF. If none, return original (zero allocations).
        bool needsEscape = false;
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == 0xFF) { needsEscape = true; break; }
        }
        if (!needsEscape) return input;

        // Slow path: rebuild with doubled 0xFF.
        using var ms = new MemoryStream(input.Length + 8);
        for (int i = 0; i < input.Length; i++)
        {
            ms.WriteByte(input[i]);
            if (input[i] == 0xFF) ms.WriteByte(0xFF);
        }
        return ms.ToArray();
    }
}
