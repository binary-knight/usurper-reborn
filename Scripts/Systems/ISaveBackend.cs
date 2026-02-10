using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Abstraction layer for game save persistence.
    /// FileSaveBackend: JSON files for local/Steam single-player.
    /// SqlSaveBackend: SQLite for online multiplayer server (Phase 2).
    /// </summary>
    public interface ISaveBackend
    {
        /// <summary>
        /// Write complete game state for a player.
        /// </summary>
        Task<bool> WriteGameData(string playerName, SaveGameData data);

        /// <summary>
        /// Read complete game state for a player (most recent manual save).
        /// </summary>
        Task<SaveGameData?> ReadGameData(string playerName);

        /// <summary>
        /// Read game state from a specific save file/record.
        /// </summary>
        Task<SaveGameData?> ReadGameDataByFileName(string fileName);

        /// <summary>
        /// Check if a save exists for the given player name.
        /// </summary>
        bool GameDataExists(string playerName);

        /// <summary>
        /// Delete a save for the given player name.
        /// </summary>
        bool DeleteGameData(string playerName);

        /// <summary>
        /// Get all saves across all players.
        /// </summary>
        List<SaveInfo> GetAllSaves();

        /// <summary>
        /// Get all saves for a specific player (manual + autosaves).
        /// </summary>
        List<SaveInfo> GetPlayerSaves(string playerName);

        /// <summary>
        /// Get the most recent save for a player.
        /// </summary>
        SaveInfo? GetMostRecentSave(string playerName);

        /// <summary>
        /// Get all unique player names that have saves.
        /// </summary>
        List<string> GetAllPlayerNames();

        /// <summary>
        /// Write an autosave with rotation (keeps N most recent).
        /// </summary>
        Task<bool> WriteAutoSave(string playerName, SaveGameData data);

        /// <summary>
        /// Create a backup of the current save before overwriting.
        /// </summary>
        void CreateBackup(string playerName);

        /// <summary>
        /// Get the save directory path (for SysOp operations, log display, etc.)
        /// </summary>
        string GetSaveDirectory();
    }
}
