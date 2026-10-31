using System;
using System.Globalization;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// O2: the values of one world_snapshots row (the table is made in InitializeDatabase). Players online is not
    /// here: the insert counts it from online_players, so a failed read fails the write.
    /// </summary>
    public sealed record WorldSnapshotRow(
        long NpcsAlive, long NpcsDead, long NpcsMarried, long Children, long Treasury,
        long Births, long Deaths, long Marriages);

    /// <summary>O2: what a world_snapshots write did.</summary>
    public enum WorldSnapshotWrite
    {
        /// <summary>The hour's row was added.</summary>
        Inserted,
        /// <summary>The hour already had its row; nothing was added.</summary>
        AlreadyExists,
        /// <summary>Another process holds the world sim lock; nothing was written.</summary>
        NotOwner,
        /// <summary>The write failed; nothing was written.</summary>
        Failed,
    }

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
        /// transaction. With an ownerId the write happens only while the world sim lock names that owner, checked
        /// inside the same (immediate) transaction, so a process that lost the lock writes nothing.
        /// </summary>
        public WorldSnapshotWrite WriteWorldSnapshot(DateTime utcNow, WorldSnapshotRow row, string? ownerId = null)
        {
            try
            {
                using var connection = OpenConnection();
                using var tx = connection.BeginTransaction(deferred: false);
                if (ownerId != null)
                {
                    using var owner = connection.CreateCommand();
                    owner.Transaction = tx;
                    owner.CommandText = "SELECT json_extract(value, '$.owner') FROM world_state WHERE key = @key;";
                    owner.Parameters.AddWithValue("@key", WORLDSIM_LOCK_KEY);
                    if (owner.ExecuteScalar() as string != ownerId) return WorldSnapshotWrite.NotOwner;
                }
                int added;
                using (var insert = connection.CreateCommand())
                {
                    insert.Transaction = tx;
                    insert.CommandText = @"INSERT OR IGNORE INTO world_snapshots
                        (taken_at, npcs_alive, npcs_dead, npcs_married, children, treasury, players_online, births, deaths, marriages)
                        VALUES (@t, @alive, @dead, @married, @children, @treasury,
                            (SELECT COUNT(*) FROM online_players WHERE last_heartbeat > datetime('now', '-2 minutes')),
                            @births, @deaths, @marriages);";
                    insert.Parameters.AddWithValue("@t", WorldSnapshotHourKey(utcNow));
                    insert.Parameters.AddWithValue("@alive", row.NpcsAlive);
                    insert.Parameters.AddWithValue("@dead", row.NpcsDead);
                    insert.Parameters.AddWithValue("@married", row.NpcsMarried);
                    insert.Parameters.AddWithValue("@children", row.Children);
                    insert.Parameters.AddWithValue("@treasury", row.Treasury);
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
                return added > 0 ? WorldSnapshotWrite.Inserted : WorldSnapshotWrite.AlreadyExists;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SQL", $"world_snapshots row not written: {ex.Message}");
                return WorldSnapshotWrite.Failed;
            }
        }
    }
}
