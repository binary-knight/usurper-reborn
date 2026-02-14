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

        // Broadcast arrival to others already in the room (suppress for invisible wizards)
        if (!session.IsWizInvisible)
        {
            BroadcastToRoom(location, $"\u001b[36m{session.Username} arrives.\u001b[0m", excludeUsername: session.Username);
        }
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

        // Broadcast departure (suppress for invisible wizards)
        if (!session.IsWizInvisible)
        {
            var destName = destination.HasValue ? BaseLocation.GetLocationName(destination.Value) : "elsewhere";
            BroadcastToRoom(location, $"\u001b[90m{session.Username} leaves toward {destName}.\u001b[0m", excludeUsername: session.Username);
        }
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

            BroadcastToRoom(location, $"\u001b[90m{session.Username} has disconnected.\u001b[0m", excludeUsername: session.Username);
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
    /// Get player names at a location, excluding a specific player.
    /// </summary>
    public IReadOnlyList<string> GetPlayerNamesAt(GameLocation location, string? excludeUsername = null)
    {
        if (!_rooms.TryGetValue(location, out var room))
            return Array.Empty<string>();

        var excludeKey = excludeUsername?.ToLowerInvariant();
        // Determine viewer's wizard level for invisibility filtering
        var viewerWizLevel = SessionContext.IsActive
            ? (SessionContext.Current?.WizardLevel ?? WizardLevel.Mortal)
            : WizardLevel.Mortal;

        return room.Values
            .Where(s => excludeKey == null || s.Username.ToLowerInvariant() != excludeKey)
            .Where(s => !s.IsWizInvisible || viewerWizLevel >= s.WizardLevel) // Hide invisible wizards from lower-level
            .Select(s =>
            {
                // Add wizard title to display name
                if (s.WizardLevel > WizardLevel.Mortal)
                    return $"{s.Username} [{WizardConstants.GetTitle(s.WizardLevel)}]";
                return s.Username;
            })
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Broadcast a message to all players at a specific location.
    /// </summary>
    public void BroadcastToRoom(GameLocation location, string message, string? excludeUsername = null)
    {
        if (!_rooms.TryGetValue(location, out var room))
            return;

        var excludeKey = excludeUsername?.ToLowerInvariant();
        foreach (var kvp in room)
        {
            if (excludeKey != null && kvp.Key == excludeKey)
                continue;

            kvp.Value.EnqueueMessage(message);
        }
    }

    /// <summary>
    /// Broadcast a message to ALL connected players regardless of location.
    /// </summary>
    public void BroadcastGlobal(string message, string? excludeUsername = null)
    {
        var server = MudServer.Instance;
        if (server != null)
            server.BroadcastToAll(message, excludeUsername);
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
}
