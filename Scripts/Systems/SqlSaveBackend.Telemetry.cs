using System;
using Microsoft.Data.Sqlite;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// 1.2.7: the per player telemetry answers of a self hosted server, in the table telemetry_consent of
    /// the game database. The table is made only when an answer is stored, which happens only with the
    /// operator switch on (TelemetryStore), so a database whose operator never turned it on is untouched.
    /// </summary>
    public partial class SqlSaveBackend
    {
        internal const string TelemetryConsentTable = "telemetry_consent";

        private static bool TelemetryTableExists(SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @t;";
            cmd.Parameters.AddWithValue("@t", TelemetryConsentTable);
            return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }

        /// <summary>One player's stored answer by consent key, or null when there is none (no table, no
        /// row, or a value other than 0 or 1).</summary>
        internal (bool Asked, bool Yes)? ReadTelemetryAnswer(string key)
        {
            using var connection = OpenConnection();
            if (!TelemetryTableExists(connection)) return null;
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT asked, yes FROM {TelemetryConsentTable} WHERE player_key = @k;";
            cmd.Parameters.AddWithValue("@k", key);
            using var r = cmd.ExecuteReader();
            if (!r.Read() || r.IsDBNull(0) || r.IsDBNull(1)) return null;
            long asked = r.GetInt64(0), yes = r.GetInt64(1);
            if ((asked != 0 && asked != 1) || (yes != 0 && yes != 1)) return null;
            return (asked == 1, yes == 1);
        }

        /// <summary>Store one player's answer, making the table first if it is not there.</summary>
        internal void WriteTelemetryAnswer(string key, bool asked, bool yes)
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $@"
                CREATE TABLE IF NOT EXISTS {TelemetryConsentTable} (
                    player_key TEXT PRIMARY KEY,
                    asked INTEGER NOT NULL,
                    yes INTEGER NOT NULL
                );
                INSERT INTO {TelemetryConsentTable} (player_key, asked, yes) VALUES (@k, @a, @y)
                ON CONFLICT(player_key) DO UPDATE SET asked = excluded.asked, yes = excluded.yes;";
            cmd.Parameters.AddWithValue("@k", key);
            cmd.Parameters.AddWithValue("@a", asked ? 1 : 0);
            cmd.Parameters.AddWithValue("@y", yes ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Remove one player's answer; a database without the table is left as it is.</summary>
        internal void DeleteTelemetryAnswer(string key)
        {
            using var connection = OpenConnection();
            if (!TelemetryTableExists(connection)) return;
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM {TelemetryConsentTable} WHERE player_key = @k;";
            cmd.Parameters.AddWithValue("@k", key);
            cmd.ExecuteNonQuery();
        }
    }
}
