using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// 1.3.4: password self-reset by email, the game side. The game writes request rows (mail_requests); the web
    /// server claims them, makes the tokens (account_tokens) and sends the mail, so the game never holds a mail key.
    /// An address is kept only in the players row, once it is confirmed (G2), and in request rows until they are
    /// sent; never in player_data, deleted_characters, pending_purges, an export or a log line. The security log
    /// keeps the account, the event, the IP and the time, never an address (S3).
    /// </summary>
    public partial class SqlSaveBackend
    {
        /// <summary>The account mail tables, also on a database made by an older release. Kinds and statuses are
        /// listed in <see cref="MailRequestKind"/> and by the web sender; no CHECK so both sides can add a kind.</summary>
        internal const string AccountMailDdl = @"
            CREATE TABLE IF NOT EXISTS mail_requests (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                kind         TEXT NOT NULL,
                username     TEXT,
                address      TEXT,
                ip           TEXT NOT NULL DEFAULT '',
                lang         TEXT NOT NULL DEFAULT 'en',
                created_at   TEXT NOT NULL DEFAULT (datetime('now')),
                status       TEXT NOT NULL DEFAULT 'pending',
                processed_at TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_mail_requests_status ON mail_requests(status, id);
            CREATE INDEX IF NOT EXISTS idx_mail_requests_ip ON mail_requests(ip, created_at);
            CREATE TABLE IF NOT EXISTS account_tokens (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                username    TEXT NOT NULL,
                kind        TEXT NOT NULL,
                token_hash  TEXT NOT NULL UNIQUE,
                new_address TEXT,
                created_at  TEXT NOT NULL DEFAULT (datetime('now')),
                expires_at  TEXT NOT NULL,
                used_at     TEXT,
                ip          TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_account_tokens_user ON account_tokens(username, kind);
            CREATE TABLE IF NOT EXISTS account_security_log (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                username   TEXT NOT NULL,
                event      TEXT NOT NULL,
                ip         TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            CREATE INDEX IF NOT EXISTS idx_account_security_log_user ON account_security_log(username, id);";

        internal const string PlayersEmailIndex = "idx_players_email";

        /// <summary>The longest account name a reset request keeps (names are 2 to 20 characters).</summary>
        internal const int ResetNameMaxLength = 64;

        /// <summary>1.3.4: the players columns, the unique address index and the three tables. Idempotent.</summary>
        private static void MigrateAccountMail(SqliteConnection connection)
        {
            foreach (var column in new[] { "email TEXT", "email_verified_at TEXT", "email_prompted_at TEXT" })
            {
                try
                {
                    using var migCmd = connection.CreateCommand();
                    migCmd.CommandText = $"ALTER TABLE players ADD COLUMN {column};";
                    migCmd.ExecuteNonQuery();
                }
                catch { /* Column already exists - expected */ }
            }
            try
            {
                using var migCmd = connection.CreateCommand();
                migCmd.CommandText = $"CREATE UNIQUE INDEX IF NOT EXISTS {PlayersEmailIndex} ON players(email) WHERE email IS NOT NULL;";
                migCmd.ExecuteNonQuery();
            }
            catch (Exception ex) { DebugLogger.Instance.LogWarning("SQL", $"{PlayersEmailIndex} not ensured: {ex.Message}"); }
            try
            {
                using var migCmd = connection.CreateCommand();
                migCmd.CommandText = AccountMailDdl;
                migCmd.ExecuteNonQuery();
            }
            catch (Exception ex) { DebugLogger.Instance.LogWarning("SQL", $"account mail tables not ensured: {ex.Message}"); }
        }

        /// <summary>The mail switch row (<see cref="MailSwitch"/>), or null when it is missing or cannot be read.</summary>
        internal (string? Value, string? UpdatedAt)? ReadMailSwitchRow()
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT value, updated_at FROM world_state WHERE key = @key;";
                cmd.Parameters.AddWithValue("@key", MailSwitch.Key);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) return null;
                return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SQL", $"mail switch not read: {ex.Message}");
                return null;
            }
        }

        /// <summary>The name a reset request keeps: trimmed, lower case, no control characters, at most 64 characters.</summary>
        internal static string ResetRequestName(string? typed)
        {
            var chars = new System.Text.StringBuilder();
            foreach (char c in (typed ?? "").Trim().ToLowerInvariant())
                if (!char.IsControl(c)) chars.Append(c);
            string s = chars.ToString();
            return s.Length > ResetNameMaxLength ? s.Substring(0, ResetNameMaxLength) : s;
        }

        /// <summary>S4: the rows written for this IP in the last hour have reached <see cref="GameConfig.MailRequestsPerIpPerHour"/>.</summary>
        private static bool MailRequestCapReached(SqliteConnection connection, string ip)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM mail_requests WHERE ip = @ip AND created_at > datetime('now', '-1 hour');";
            cmd.Parameters.AddWithValue("@ip", ip);
            return Convert.ToInt64(cmd.ExecuteScalar()) >= GameConfig.MailRequestsPerIpPerHour;
        }

        private static void InsertMailRequest(SqliteConnection connection, string kind, string? username, string? address, string ip, string lang)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO mail_requests (kind, username, address, ip, lang) VALUES (@k, @u, @a, @ip, @lang);";
            cmd.Parameters.AddWithValue("@k", kind);
            cmd.Parameters.AddWithValue("@u", (object?)username ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@a", (object?)address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ip", ip);
            cmd.Parameters.AddWithValue("@lang", lang);
            cmd.ExecuteNonQuery();
        }

        /// <summary>S3: one security log row; it never holds an address or a secret.</summary>
        private static void LogSecurityEvent(SqliteConnection connection, string username, string evt, string ip)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO account_security_log (username, event, ip) VALUES (@u, @e, @ip);";
            cmd.Parameters.AddWithValue("@u", username.ToLowerInvariant());
            cmd.Parameters.AddWithValue("@e", evt);
            cmd.Parameters.AddWithValue("@ip", ip);
            cmd.ExecuteNonQuery();
        }

        private static string RequestLang(string? lang) => string.IsNullOrEmpty(lang) ? GameConfig.Language : lang;

        /// <summary>
        /// 1.3: a forgotten password. One reset row (the name as typed, no address, the IP and the language),
        /// written the same way whether the name is an account or not: no account is looked up here, and the
        /// web sender sends only to an account with a confirmed address. Past the S4 cap no row is written.
        /// The player sees the same reply in every case. True when a row was written (tests).
        /// </summary>
        public bool RequestPasswordReset(string? typedName, string? ip, string? lang)
        {
            string name = ResetRequestName(typedName);
            if (name.Length == 0) return false;
            try
            {
                using var connection = OpenConnection();
                if (MailRequestCapReached(connection, ip ?? "")) return false;
                InsertMailRequest(connection, MailRequestKind.Reset, name, null, ip ?? "", RequestLang(lang));
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SQL", $"reset request not written: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 1.4, 1.5 and 1.6: a new address to confirm. The address stays in the request row; players.email is set
        /// by the web server when the link is opened. Past the S4 cap no row is written and the player sees the
        /// same reply. True when a row was written.
        /// </summary>
        public bool RequestEmailVerify(string username, string? address, string? ip, string? lang)
        {
            string? normalized = AccountEmail.Normalize(address);
            if (normalized == null || string.IsNullOrWhiteSpace(username)) return false;
            try
            {
                using var connection = OpenConnection();
                if (MailRequestCapReached(connection, ip ?? "")) return false;
                InsertMailRequest(connection, MailRequestKind.Verify, username.ToLowerInvariant(), normalized, ip ?? "", RequestLang(lang));
                LogSecurityEvent(connection, username, "email_verify_requested", ip ?? "");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SQL", $"verify request not written: {ex.Message}");
                return false;
            }
        }

        /// <summary>1.4 and 1.5: the account was asked for an address (set or skipped); it is not asked again.</summary>
        public void MarkEmailPrompted(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE players SET email_prompted_at = datetime('now') WHERE LOWER(username) = LOWER(@u) AND email_prompted_at IS NULL;";
                cmd.Parameters.AddWithValue("@u", username);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { DebugLogger.Instance.LogWarning("SQL", $"email prompt mark not written: {ex.Message}"); }
        }

        /// <summary>The account's confirmed address (null when none) and whether it was asked for one.
        /// Found is false when there is no such account or the row cannot be read.</summary>
        public (bool Found, string? Email, bool Prompted) GetAccountEmail(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT email, email_prompted_at FROM players WHERE LOWER(username) = LOWER(@u);";
                cmd.Parameters.AddWithValue("@u", username);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) return (false, null, false);
                return (true, reader.IsDBNull(0) ? null : reader.GetString(0), !reader.IsDBNull(1));
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SQL", $"account email not read: {ex.Message}");
                return (false, null, false);
            }
        }

        /// <summary>1.5: the one-time prompt is due: mail is on, the account exists, has no address and was never asked.</summary>
        public bool EmailPromptDue(string username)
        {
            if (!MailSwitch.IsOn(this)) return false;
            var (found, email, prompted) = GetAccountEmail(username);
            return found && email == null && !prompted;
        }

        /// <summary>1.6: the outcome of an add, change or remove in the in-game email menu.</summary>
        public enum EmailChangeResult { Requested, Removed, BadPassword, BadAddress, NoAddress, Failed }

        /// <summary>
        /// 1.6: add or change the address. Needs the current password; the new address goes into a verify request
        /// (the web server notifies the old address once the new one is confirmed). Past the S4 cap no row is
        /// written and the result is still Requested, so the reply is the same.
        /// </summary>
        public async Task<EmailChangeResult> RequestEmailChange(string username, string? currentPassword, string? newAddress, string? ip, string? lang)
        {
            if (!await CurrentPasswordMatches(username, currentPassword, ip, "email_change_refused")) return EmailChangeResult.BadPassword;
            if (AccountEmail.Normalize(newAddress) == null) return EmailChangeResult.BadAddress;
            RequestEmailVerify(username, newAddress, ip, lang);
            return EmailChangeResult.Requested;
        }

        /// <summary>
        /// 1.6 and S5: remove the address. Needs the current password; clears players.email and email_verified_at and
        /// writes a remove_notice request to the removed address (not held back by the S4 cap: it goes only to the
        /// address that was on the account).
        /// </summary>
        public async Task<EmailChangeResult> RemoveEmail(string username, string? currentPassword, string? ip, string? lang)
        {
            if (!await CurrentPasswordMatches(username, currentPassword, ip, "email_remove_refused")) return EmailChangeResult.BadPassword;
            try
            {
                using var connection = OpenConnection();
                using var tx = connection.BeginTransaction();
                string? old;
                using (var read = connection.CreateCommand())
                {
                    read.Transaction = tx;
                    read.CommandText = "SELECT email FROM players WHERE LOWER(username) = LOWER(@u);";
                    read.Parameters.AddWithValue("@u", username);
                    old = read.ExecuteScalar() as string;
                }
                if (string.IsNullOrEmpty(old)) { tx.Rollback(); return EmailChangeResult.NoAddress; }
                using (var clear = connection.CreateCommand())
                {
                    clear.Transaction = tx;
                    clear.CommandText = "UPDATE players SET email = NULL, email_verified_at = NULL WHERE LOWER(username) = LOWER(@u);";
                    clear.Parameters.AddWithValue("@u", username);
                    clear.ExecuteNonQuery();
                }
                using (var notice = connection.CreateCommand())
                {
                    notice.Transaction = tx;
                    notice.CommandText = "INSERT INTO mail_requests (kind, username, address, ip, lang) VALUES (@k, @u, @a, @ip, @lang);";
                    notice.Parameters.AddWithValue("@k", MailRequestKind.RemoveNotice);
                    notice.Parameters.AddWithValue("@u", username.ToLowerInvariant());
                    notice.Parameters.AddWithValue("@a", old);
                    notice.Parameters.AddWithValue("@ip", ip ?? "");
                    notice.Parameters.AddWithValue("@lang", RequestLang(lang));
                    notice.ExecuteNonQuery();
                }
                using (var log = connection.CreateCommand())
                {
                    log.Transaction = tx;
                    log.CommandText = "INSERT INTO account_security_log (username, event, ip) VALUES (@u, 'email_removed', @ip);";
                    log.Parameters.AddWithValue("@u", username.ToLowerInvariant());
                    log.Parameters.AddWithValue("@ip", ip ?? "");
                    log.ExecuteNonQuery();
                }
                tx.Commit();
                DebugLogger.Instance.LogInfo("AUTH", $"Email address removed for '{username}'");
                return EmailChangeResult.Removed;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SQL", $"email remove failed: {ex.Message}");
                return EmailChangeResult.Failed;
            }
        }

        /// <summary>The current password, checked as a login checks it; a wrong one writes a security log row.</summary>
        private async Task<bool> CurrentPasswordMatches(string username, string? password, string? ip, string refusedEvent)
        {
            if (!string.IsNullOrEmpty(password))
            {
                var (ok, _, _, _, _, _) = await AuthenticatePlayer(username, password, null);
                if (ok) return true;
            }
            try
            {
                using var connection = OpenConnection();
                LogSecurityEvent(connection, username, refusedEvent, ip ?? "");
            }
            catch (Exception ex) { DebugLogger.Instance.LogWarning("SQL", $"security log not written: {ex.Message}"); }
            return false;
        }
    }
}
