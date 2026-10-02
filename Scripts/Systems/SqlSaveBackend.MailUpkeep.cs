using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// 1.2.5: keeping the online mailbox to mail worth reading. A one-time purge of the nightly world boss
    /// notices, a cap on old mail from the "System" sender, mail rendered in the recipient's language, and
    /// one Auction House mail per seller per world-sim day.
    /// </summary>
    public partial class SqlSaveBackend
    {
        /// <summary>The world_state key that records the world boss mail purge ran (its value is the count it deleted).</summary>
        internal const string WorldBossMailPurgeMarker = "mail_purge_world_boss_125";

        /// <summary>The stored sender whose old mail <see cref="PruneOldSystemMail"/> deletes; matched exactly.</summary>
        internal const string SystemMailSender = "System";

        private static void EnsureMailUpkeepTables(SqliteConnection connection)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                // one row per seller (the mail address) per world-sim day: the running count and gold of the
                // day's NPC purchases, and the mail row that shows them
                cmd.CommandText = "CREATE TABLE IF NOT EXISTS auction_sale_mail (to_player TEXT NOT NULL, day TEXT NOT NULL, " +
                                  "sales INTEGER NOT NULL DEFAULT 0, gold INTEGER NOT NULL DEFAULT 0, message_id INTEGER NOT NULL, " +
                                  "created_at TEXT DEFAULT (datetime('now')), PRIMARY KEY (to_player, day));";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { DebugLogger.Instance.LogWarning("SQL", $"auction_sale_mail not ensured: {ex.Message}"); }
        }

        /// <summary>
        /// 1.2.5: deletes every mail row of type world_boss, once per database. The run and its count are
        /// recorded in world_state under <see cref="WorldBossMailPurgeMarker"/> in the same transaction, so a
        /// later start finds the marker, deletes nothing and says so. Returns the rows deleted, or -1 when it
        /// had run already (or failed, which leaves the marker unwritten so the next start tries again).
        /// </summary>
        internal int PurgeWorldBossMailOnce()
        {
            try
            {
                using var connection = OpenConnection();
                using var tx = connection.BeginTransaction(deferred: false);
                using (var check = connection.CreateCommand())
                {
                    check.Transaction = tx;
                    check.CommandText = "SELECT value FROM world_state WHERE key = @k;";
                    check.Parameters.AddWithValue("@k", WorldBossMailPurgeMarker);
                    if (check.ExecuteScalar() is string earlier)
                    {
                        DebugLogger.Instance.LogInfo("SQL", $"World boss mail purge ran already (deleted {earlier}); nothing deleted");
                        return -1;
                    }
                }
                int deleted;
                using (var del = connection.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM messages WHERE message_type = 'world_boss';";
                    deleted = del.ExecuteNonQuery();
                }
                using (var mark = connection.CreateCommand())
                {
                    mark.Transaction = tx;
                    mark.CommandText = "INSERT INTO world_state (key, value, updated_by) VALUES (@k, @v, 'mail_purge');";
                    mark.Parameters.AddWithValue("@k", WorldBossMailPurgeMarker);
                    mark.Parameters.AddWithValue("@v", deleted.ToString());
                    mark.ExecuteNonQuery();
                }
                tx.Commit();
                DebugLogger.Instance.LogInfo("SQL", $"World boss mail purge deleted {deleted} mail rows");
                return deleted;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"World boss mail purge failed; nothing was deleted, the next start tries again: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// 1.2.5: deletes mail whose stored sender is exactly "System" and which is older than
        /// <paramref name="keepDays"/> days, and the Auction House day rows as old. Mail from players (any
        /// other sender) is never touched. Run at startup and at the world-sim daily reset. Returns the
        /// mail rows deleted.
        /// </summary>
        public int PruneOldSystemMail(int keepDays)
        {
            try
            {
                using var connection = OpenConnection();
                int deleted;
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM messages WHERE from_player = @sender AND created_at < datetime('now', @cutoff);";
                    cmd.Parameters.AddWithValue("@sender", SystemMailSender);
                    cmd.Parameters.AddWithValue("@cutoff", $"-{keepDays} days");
                    deleted = cmd.ExecuteNonQuery();
                }
                using (var days = connection.CreateCommand())
                {
                    days.CommandText = "DELETE FROM auction_sale_mail WHERE created_at < datetime('now', @cutoff);";
                    days.Parameters.AddWithValue("@cutoff", $"-{keepDays} days");
                    days.ExecuteNonQuery();
                }
                if (deleted > 0)
                    DebugLogger.Instance.LogInfo("SQL", $"Deleted {deleted} System mail rows older than {keepDays} days");
                return deleted;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to prune old System mail: {ex.Message}");
                return 0;
            }
        }

        private static string MailLanguage(string? language) =>
            string.IsNullOrWhiteSpace(language) ? "en" : language;

        /// <summary>1.2.5: the account language of a save key, for mail; "en" when there is none.</summary>
        public string MailLanguageForKey(string key) => MailLanguage(GetAccountPreferences(key).language);

        /// <summary>
        /// 1.2.5: the account language of the player a mail to <paramref name="name"/> belongs to, by the
        /// same addressing <see cref="SendMessage"/> uses; "en" when the name is nobody's (an NPC).
        /// </summary>
        public string MailLanguageForName(string name)
        {
            var owner = ResolveMailOwner(MailAddressForName(name));
            return owner == null ? "en" : MailLanguageForKey(owner);
        }

        /// <summary>1.2.5: mail to a player by name, rendered in that player's language.</summary>
        public Task SendMessageLocalized(string from, string to, string messageType, Func<string, string> textIn) =>
            InsertMessage(from, MailAddressForName(to), messageType, textIn(MailLanguageForName(to)));

        /// <summary>1.2.5: mail to a player by save key, rendered in that player's language.</summary>
        public Task SendMessageToKeyLocalized(string from, string key, string messageType, Func<string, string> textIn) =>
            InsertMessage(from, MailAddressForKey(key), messageType, textIn(MailLanguageForKey(key)));

        /// <summary>The world-sim day a sale at <paramref name="utcNow"/> belongs to: the last 7 PM Eastern reset.</summary>
        internal static string AuctionMailDay(DateTime utcNow) =>
            DailySystemManager.ResetBoundaryAt(utcNow).ToString("yyyy-MM-ddTHH:mm'Z'");

        /// <summary>
        /// 1.2.5: tells a seller an NPC bought their listing. The first sale of a world-sim day (7 PM Eastern
        /// to 7 PM Eastern) is one mail; each later sale that day rewrites that same mail with the day's
        /// count and gold and the latest sale, and marks it unread again, instead of adding a row. Done in
        /// one immediate transaction, so two purchases at once cannot both start the day's mail. Returns the
        /// mail row's id, or 0 when it failed.
        /// </summary>
        public async Task<long> MailAuctionSale(string seller, string itemName, string buyer, long price, DateTime? utcNow = null)
        {
            try
            {
                string to = MailAddressForName(seller);
                string lang = MailLanguageForName(seller);
                string day = AuctionMailDay(utcNow ?? DateTime.UtcNow);
                using var connection = OpenConnection();
                using var tx = connection.BeginTransaction(deferred: false);

                long messageId = 0, sales = 0, gold = 0;
                using (var q = connection.CreateCommand())
                {
                    q.Transaction = tx;
                    q.CommandText = "SELECT d.message_id, d.sales, d.gold FROM auction_sale_mail d JOIN messages m ON m.id = d.message_id " +
                                    "WHERE d.to_player = @to AND d.day = @day;";
                    q.Parameters.AddWithValue("@to", to);
                    q.Parameters.AddWithValue("@day", day);
                    using var r = q.ExecuteReader();
                    if (r.Read()) { messageId = r.GetInt64(0); sales = r.GetInt64(1); gold = r.GetInt64(2); }
                }

                sales++;
                gold += price;
                if (messageId != 0)
                {
                    using var up = connection.CreateCommand();
                    up.Transaction = tx;
                    up.CommandText = "UPDATE messages SET message = @msg, is_read = 0 WHERE id = @id;";
                    up.Parameters.AddWithValue("@msg", Loc.GetIn(lang, "mail.auction_sold_today", sales, $"{gold:N0}", itemName, buyer, $"{price:N0}"));
                    up.Parameters.AddWithValue("@id", messageId);
                    up.ExecuteNonQuery();
                }
                else
                {
                    using var ins = connection.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = "INSERT INTO messages (from_player, to_player, message_type, message) VALUES ('Auction House', @to, 'auction', @msg); SELECT last_insert_rowid();";
                    ins.Parameters.AddWithValue("@to", to);
                    ins.Parameters.AddWithValue("@msg", Loc.GetIn(lang, "mail.auction_sold", itemName, buyer, $"{price:N0}"));
                    messageId = Convert.ToInt64(await ins.ExecuteScalarAsync());
                }

                using (var d = connection.CreateCommand())
                {
                    d.Transaction = tx;
                    d.CommandText = "INSERT INTO auction_sale_mail (to_player, day, sales, gold, message_id) VALUES (@to, @day, @s, @g, @id) " +
                                    "ON CONFLICT(to_player, day) DO UPDATE SET sales = excluded.sales, gold = excluded.gold, message_id = excluded.message_id;";
                    d.Parameters.AddWithValue("@to", to);
                    d.Parameters.AddWithValue("@day", day);
                    d.Parameters.AddWithValue("@s", sales);
                    d.Parameters.AddWithValue("@g", gold);
                    d.Parameters.AddWithValue("@id", messageId);
                    d.ExecuteNonQuery();
                }
                tx.Commit();
                return messageId;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to mail an auction sale: {ex.Message}");
                return 0;
            }
        }
    }
}
