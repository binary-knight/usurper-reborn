using System;
using System.Globalization;

namespace UsurperRemake.Systems
{
    /// <summary>O2: the values of one world_snapshots row (the table is made in InitializeDatabase).</summary>
    public sealed record WorldSnapshotRow(
        long NpcsAlive, long NpcsDead, long NpcsMarried, long Children, long Treasury, long PlayersOnline,
        long Births, long Deaths, long Marriages);

    public partial class SqlSaveBackend
    {
        /// <summary>O2: rows older than this many days are pruned in the same write.</summary>
        public const int WorldSnapshotKeepDays = 90;

        /// <summary>The top of the UTC hour of the instant, as the table keys it: 'yyyy-MM-ddTHH:00:00Z'.</summary>
        public static string WorldSnapshotHourKey(DateTime utc)
        {
            var u = utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc;
            return new DateTime(u.Year, u.Month, u.Day, u.Hour, 0, 0, DateTimeKind.Utc)
                .ToString("yyyy'-'MM'-'dd'T'HH':00:00Z'", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// O2: the hour's row, keyed by the top of the UTC hour of utcNow. INSERT OR IGNORE, so a second write in the
        /// same hour (a restart) leaves the first row as it is; rows older than 90 days are pruned in the same
        /// transaction. Returns true only when this call added the row; false when the hour already had one or
        /// the write failed.
        /// </summary>
        public bool WriteWorldSnapshot(DateTime utcNow, WorldSnapshotRow row)
        {
            try
            {
                using var connection = OpenConnection();
                using var tx = connection.BeginTransaction();
                int added;
                using (var insert = connection.CreateCommand())
                {
                    insert.Transaction = tx;
                    insert.CommandText = @"INSERT OR IGNORE INTO world_snapshots
                        (taken_at, npcs_alive, npcs_dead, npcs_married, children, treasury, players_online, births, deaths, marriages)
                        VALUES (@t, @alive, @dead, @married, @children, @treasury, @online, @births, @deaths, @marriages);";
                    insert.Parameters.AddWithValue("@t", WorldSnapshotHourKey(utcNow));
                    insert.Parameters.AddWithValue("@alive", row.NpcsAlive);
                    insert.Parameters.AddWithValue("@dead", row.NpcsDead);
                    insert.Parameters.AddWithValue("@married", row.NpcsMarried);
                    insert.Parameters.AddWithValue("@children", row.Children);
                    insert.Parameters.AddWithValue("@treasury", row.Treasury);
                    insert.Parameters.AddWithValue("@online", row.PlayersOnline);
                    insert.Parameters.AddWithValue("@births", row.Births);
                    insert.Parameters.AddWithValue("@deaths", row.Deaths);
                    insert.Parameters.AddWithValue("@marriages", row.Marriages);
                    added = insert.ExecuteNonQuery();
                }
                using (var prune = connection.CreateCommand())
                {
                    prune.Transaction = tx;
                    prune.CommandText = "DELETE FROM world_snapshots WHERE taken_at < @cutoff;";
                    prune.Parameters.AddWithValue("@cutoff", WorldSnapshotHourKey(utcNow.AddDays(-WorldSnapshotKeepDays)));
                    prune.ExecuteNonQuery();
                }
                tx.Commit();
                return added > 0;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SQL", $"world_snapshots row not written: {ex.Message}");
                return false;
            }
        }
    }
}
