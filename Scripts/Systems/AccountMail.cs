using System;
using System.Globalization;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// 1.3.4: the request kinds the game writes to mail_requests. The web sender writes its own kinds
    /// (the change notice and the notice that an address is already linked) and sends every row.
    /// </summary>
    public static class MailRequestKind
    {
        /// <summary>A forgotten password: username as typed, no address; written alike for a known and an unknown name.</summary>
        public const string Reset = "reset";
        /// <summary>A new address to confirm: username and the new address; players.email changes only when it is confirmed.</summary>
        public const string Verify = "verify";
        /// <summary>An address removed from an account: a notice to the removed address.</summary>
        public const string RemoveNotice = "remove_notice";
    }

    /// <summary>
    /// 1.3.4 (O3): whether this server sends mail. The web sender writes world_state key 'mail_enabled' on each of
    /// its loops: '1' while its mail key is set, '0' when not, with updated_at. Mail is ON only when the value is
    /// '1' and updated_at is under ten minutes old; a missing row, any other value, an unreadable time or an older
    /// beat (the sender stopped) is OFF. With mail off the game shows no reset choice, no email prompt and no
    /// email menu. Read at each prompt, never cached.
    /// </summary>
    public static class MailSwitch
    {
        public const string Key = "mail_enabled";
        public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);
        /// <summary>A beat stamped up to this far ahead of our clock still counts (two clocks on one host).</summary>
        public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

        /// <summary>Tests: the clock the beat's age is measured against.</summary>
        internal static Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

        /// <summary>The rule on one world_state row (value, updated_at as SQLite's datetime('now'), UTC).</summary>
        public static bool IsOn(string? value, string? updatedAt)
        {
            if (value != "1") return false;
            if (!TryParseUtc(updatedAt, out var at)) return false;
            var age = UtcNow() - at;
            return age < MaxAge && age > -ClockSkew;
        }

        /// <summary>The rule on the server's database; any read failure is OFF.</summary>
        public static bool IsOn(SqlSaveBackend? db)
        {
            if (db == null) return false;
            var row = db.ReadMailSwitchRow();
            return row != null && IsOn(row.Value.Value, row.Value.UpdatedAt);
        }

        internal static bool TryParseUtc(string? text, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            return DateTime.TryParseExact(text.Trim(), new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFFZ", "yyyy-MM-ddTHH:mm:ssZ" },
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out utc);
        }
    }

    /// <summary>
    /// 1.3.4 (S1): the one address rule, for every place an address is typed. Trimmed and lower case, at most 254
    /// characters, exactly one '@' with text on both sides, and no control character (CR and LF included) or other
    /// white space. The address is kept only in the players row (once confirmed) and in request rows.
    /// </summary>
    public static class AccountEmail
    {
        public const int MaxLength = 254;

        /// <summary>The address as stored, or null when the text is not an address.</summary>
        public static string? Normalize(string? input)
        {
            if (input == null) return null;
            string s = input.Trim().ToLowerInvariant();
            if (s.Length == 0 || s.Length > MaxLength) return null;
            foreach (char c in s)
                if (char.IsControl(c) || char.IsWhiteSpace(c)) return null;
            int at = s.IndexOf('@');
            if (at <= 0 || at != s.LastIndexOf('@') || at == s.Length - 1) return null;
            return s;
        }

        /// <summary>The address as shown: a***@d***.net (the first letter of each part and the last domain label).</summary>
        public static string Mask(string? address)
        {
            if (string.IsNullOrEmpty(address)) return "";
            int at = address.IndexOf('@');
            if (at <= 0 || at == address.Length - 1) return "***";
            string domain = address.Substring(at + 1);
            int dot = domain.LastIndexOf('.');
            string tld = dot > 0 ? domain.Substring(dot) : "";
            if (tld.Length > 10) tld = tld.Substring(0, 10); // a masked row stays short
            return $"{address[0]}***@{domain[0]}***{tld}";
        }
    }
}
