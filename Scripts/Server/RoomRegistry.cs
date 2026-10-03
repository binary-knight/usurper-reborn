using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Server;

/// <summary>
/// Tracks which players are at which game location. Used by the MUD server
/// for room-scoped chat, presence display ("Also here: X, Y"), and
/// entry/exit notifications.
///
/// Thread-safe via ConcurrentDictionary. Only active in MUD mode.
/// </summary>
public class RoomRegistry
{
    private static RoomRegistry? _instance;
    public static RoomRegistry? Instance => _instance;

    /// <summary>
    /// Map of location → set of player sessions at that location.
    /// </summary>
    private readonly ConcurrentDictionary<GameLocation, ConcurrentDictionary<string, PlayerSession>> _rooms = new();

    /// <summary>
    /// Reverse map: username → current location (for quick lookup).
    /// </summary>
    private readonly ConcurrentDictionary<string, GameLocation> _playerLocations = new();

    public RoomRegistry()
    {
        _instance = this;
    }

    /// <summary>
    /// A location each player has to themselves. 1.2.4: every player's home is the one
    /// GameLocation.Home, so without this everyone at home shared one room. No room line
    /// and no "Also here" crosses a private location.
    /// </summary>
    public static bool IsPrivateLocation(GameLocation location) => location == GameLocation.Home;

    /// <summary>
    /// "Also here" is shown at a location: not a private one, and not the Dungeons, where each
    /// player explores their own floors (room lines there still reach the room, as before).
    /// </summary>
    public static bool ShowsCoPresence(GameLocation location) =>
        location != GameLocation.Dungeons && !IsPrivateLocation(location);

    /// <summary>
    /// Called when a player enters a location. Broadcasts arrival to others in the room.
    /// </summary>
    public void PlayerEntered(GameLocation location, PlayerSession session)
    {
        var usernameKey = session.Username.ToLowerInvariant();

        // Remove from previous location first
        if (_playerLocations.TryGetValue(usernameKey, out var previousLocation))
        {
            PlayerLeft(previousLocation, session, destination: location);
        }

        // Add to new location
        var room = _rooms.GetOrAdd(location, _ => new ConcurrentDictionary<string, PlayerSession>());
        room[usernameKey] = session;
        _playerLocations[usernameKey] = location;

        // Room arrival broadcast removed — too spammy with multiple players online.
        // Players can see who's here via "Also here:" display.
    }

    /// <summary>
    /// Called when a player leaves a location. Broadcasts departure to others.
    /// </summary>
    public void PlayerLeft(GameLocation location, PlayerSession session, GameLocation? destination = null)
    {
        var usernameKey = session.Username.ToLowerInvariant();

        if (_rooms.TryGetValue(location, out var room))
        {
            room.TryRemove(usernameKey, out _);

            // Clean up empty rooms
            if (room.IsEmpty)
                _rooms.TryRemove(location, out _);
        }

        // Room departure broadcast removed — too spammy with multiple players online.
    }

    /// <summary>
    /// Remove a player from all tracking (disconnect, logout).
    /// </summary>
    public void PlayerDisconnected(PlayerSession session)
    {
        var usernameKey = session.Username.ToLowerInvariant();

        if (_playerLocations.TryRemove(usernameKey, out var location))
        {
            if (_rooms.TryGetValue(location, out var room))
            {
                room.TryRemove(usernameKey, out _);
                if (room.IsEmpty)
                    _rooms.TryRemove(location, out _);
            }

            // v1.2.5: each player in the room reads it in their own language.
            string name = session.ActiveCharacterName;
            BroadcastToRoomLocalized(location,
                lang => $"\u001b[90m{UsurperRemake.Systems.Loc.GetIn(lang, "mud.room_disconnected", name)}\u001b[0m",
                excludeUsername: session.Username);
        }
    }

    /// <summary>
    /// Get all player sessions currently at a location.
    /// </summary>
    public IReadOnlyList<PlayerSession> GetPlayersAt(GameLocation location)
    {
        if (_rooms.TryGetValue(location, out var room))
            return room.Values.ToList().AsReadOnly();

        return Array.Empty<PlayerSession>();
    }

    /// <summary>
    /// Get player names at a location, excluding a specific player by account username
    /// (the room key). v1.2.0: it used to compare a character name, which missed the viewer
    /// whenever their shown name differed from the session's (a family name, an alt).
    /// </summary>
    public IReadOnlyList<string> GetPlayerNamesAt(GameLocation location, string? excludeUsername = null)
    {
        if (IsPrivateLocation(location) || !_rooms.TryGetValue(location, out var room))
            return Array.Empty<string>();

        var excludeKey = excludeUsername?.ToLowerInvariant();
        // Determine viewer's wizard level for invisibility filtering
        var viewerWizLevel = SessionContext.IsActive
            ? (SessionContext.Current?.WizardLevel ?? WizardLevel.Mortal)
            : WizardLevel.Mortal;

        return room
            .Where(kv => excludeKey == null || kv.Key != excludeKey)
            .Select(kv => kv.Value)
            .Where(s => !s.IsWizInvisible || viewerWizLevel >= s.WizardLevel) // Hide invisible wizards from lower-level
            .Select(s =>
            {
                // Add wizard title to display name
                if (s.WizardLevel > WizardLevel.Mortal)
                    return $"{s.ActiveCharacterName} [{WizardConstants.GetTitle(s.WizardLevel)}]";
                return s.ActiveCharacterName;
            })
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Broadcast a message to all players at a specific location.
    /// </summary>
    public void BroadcastToRoom(GameLocation location, string message, string? excludeUsername = null, string? historyChannel = null)
    {
        if (IsPrivateLocation(location) || !_rooms.TryGetValue(location, out var room))
            return;

        var excludeKey = excludeUsername?.ToLowerInvariant();
        foreach (var kvp in room)
        {
            if (excludeKey != null && (kvp.Key == excludeKey || kvp.Value.ActiveCharacterName.ToLowerInvariant() == excludeKey))
                continue;

            kvp.Value.EnqueueMessage(message);
            // v1.2.4: chat lines go to the recipient's /history as delivered.
            if (historyChannel != null)
                MudChatSystem.RecordDelivered(kvp.Value, historyChannel, message);
        }
    }

    /// <summary>
    /// Broadcast a message to all players at a location, rendered per-recipient in each session's
    /// own language. `buildMessage` receives the recipient's language code and returns the rendered
    /// string (typically built with Loc.GetIn(lang, ...)). For announcements built in one player's
    /// session (e.g. a boss kill) that other players should read in their own language.
    /// </summary>
    // v1.2.5: historyChannel, as in BroadcastToRoom, keeps the line each recipient read in their /history.
    public void BroadcastToRoomLocalized(GameLocation location, Func<string, string> buildMessage, string? excludeUsername = null, string? historyChannel = null)
    {
        if (IsPrivateLocation(location) || !_rooms.TryGetValue(location, out var room))
            return;

        var excludeKey = excludeUsername?.ToLowerInvariant();
        foreach (var kvp in room)
        {
            if (excludeKey != null && (kvp.Key == excludeKey || kvp.Value.ActiveCharacterName.ToLowerInvariant() == excludeKey))
                continue;

            string lang = kvp.Value.Context?.Language ?? "en";
            string rendered = buildMessage(lang);
            kvp.Value.EnqueueMessage(rendered);
            if (historyChannel != null)
                MudChatSystem.RecordDelivered(kvp.Value, historyChannel, rendered);
        }
    }

    /// <summary>
    /// Broadcast a message to ALL connected players regardless of location.
    /// </summary>
    public void BroadcastGlobal(string message, string? excludeUsername = null, string? channelKey = null, string? historyChannel = null)
    {
        var server = MudServer.Instance;
        if (server != null)
            server.BroadcastToAll(message, excludeUsername, channelKey, historyChannel);
    }

    /// <summary>
    /// v1.2.5: BroadcastGlobal rendered per recipient in each session's own language, with the same
    /// channel mutes and /history recording.
    /// </summary>
    public void BroadcastGlobalLocalized(Func<string, string> buildMessage, string? excludeUsername = null, string? channelKey = null, string? historyChannel = null)
    {
        MudServer.Instance?.BroadcastLocalized(buildMessage, excludeUsername, channelKey, historyChannel);
    }

    /// <summary>
    /// Get the current location of a specific player.
    /// </summary>
    public GameLocation? GetPlayerLocation(string username)
    {
        if (_playerLocations.TryGetValue(username.ToLowerInvariant(), out var location))
            return location;
        return null;
    }

    /// <summary>
    /// Get count of online players at each location (for admin/status).
    /// </summary>
    public Dictionary<GameLocation, int> GetLocationCounts()
    {
        return _rooms
            .Where(kvp => !kvp.Value.IsEmpty)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count);
    }

    /// <summary>
    /// Convenience method: broadcast a game action to other players at the current
    /// player's location. Safe to call in non-MUD mode (no-ops silently).
    /// Uses ANSI gray color for unobtrusive action text.
    /// </summary>
    public static void BroadcastAction(string message)
    {
        if (!SessionContext.IsActive || Instance == null) return;

        var ctx = SessionContext.Current;
        if (ctx == null) return;

        var location = Instance.GetPlayerLocation(ctx.Username);
        if (!location.HasValue) return;

        Instance.BroadcastToRoom(
            location.Value,
            $"\u001b[90m  {message}\u001b[0m",
            excludeUsername: ctx.Username);
    }

    /// <summary>
    /// Per-recipient localized variant of BroadcastAction: each player at the broadcasting player's
    /// location sees the action rendered in their own language. buildMessage receives the
    /// recipient's language code (use Loc.GetIn(lang, ...)). Same gray action-text wrapping.
    /// </summary>
    public static void BroadcastActionLocalized(Func<string, string> buildMessage)
    {
        if (!SessionContext.IsActive || Instance == null) return;
        var ctx = SessionContext.Current;
        if (ctx == null) return;
        var location = Instance.GetPlayerLocation(ctx.Username);
        if (!location.HasValue) return;
        Instance.BroadcastToRoomLocalized(
            location.Value,
            lang => $"[90m  {buildMessage(lang)}[0m",
            excludeUsername: ctx.Username);
    }
}
