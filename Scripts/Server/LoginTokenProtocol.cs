using System;
using System.Security.Cryptography;

namespace UsurperRemake.Server;

/// <summary>
/// 1.2.8: the remember-me token for Online Play, wire side. The server, the SSH relay and (later) the
/// client share these lines; they are protocol, not player text, and stay English.
///
///   Probe:     AUTH:CAPS                     server answers CAPS:token=1 and closes
///   Request:   ";token=1" in the connection type of a password or REGISTER line
///   Issue:     OK, then one line TOKEN:&lt;selector&gt;.&lt;verifier&gt;:&lt;expiresUnix&gt; (or TOKEN:-)
///   Login:     AUTH:TOKEN1 &lt;selector&gt;.&lt;verifier&gt; &lt;connectionType&gt; [&lt;clientVersion&gt;]
///
/// The token login line has no ':' after "AUTH:", so every server and relay from 1.0.0 to 1.2.7
/// refuses it at the part count, before any password work and without logging or echoing it.
/// The selector (16 random bytes, lower-case hex) is the lookup key and is not secret. The verifier
/// (32 random bytes, base64url without padding) is the secret; the server keeps only its SHA-256.
/// </summary>
internal static class LoginTokenProtocol
{
    public const string CapsLine = "AUTH:CAPS";
    public const string CapsReply = "CAPS:token=1";
    public const string TokenLoginPrefix = "AUTH:TOKEN1 ";
    public const string TokenReplyPrefix = "TOKEN:";
    public const string TokenReplyNone = "TOKEN:-";
    /// <summary>The existing success and refusal shapes, for the new branches (the old ones keep their own literals).</summary>
    public const string OkLine = "OK";
    public const string ErrPrefix = "ERR:";
    public static string ErrLine(string message) => ErrPrefix + message;

    /// <summary>
    /// The refusal of a token login whose token is no good (malformed, unknown, wrong, expired or revoked). The
    /// client clears its saved token only on this mark; a throttle or ban reply has none, so the token is kept.
    /// Machine-readable and English: client and server may run different languages, so the client never
    /// matches the localized text after it.
    /// </summary>
    public const string TokenRefusedPrefix = "ERR:[token=refused] ";
    public static string TokenRefusedLine(string message) => TokenRefusedPrefix + message;

    /// <summary>The connection type parameter that asks for a token on a password or REGISTER line.</summary>
    public const string RequestParam = "token";

    public const int SelectorBytes = 16;
    public const int VerifierBytes = 32;
    public const int SelectorLength = 32;   // hex
    public const int VerifierLength = 43;   // base64url, no padding
    public const int MaxTypeLength = 32;
    public const int MaxVersionLength = 32;

    /// <summary>A token login line: the prefix, and no ':' anywhere after "AUTH:" (a password line always has one).</summary>
    public static bool IsTokenLoginLine(string? line) =>
        line != null && line.StartsWith(TokenLoginPrefix, StringComparison.Ordinal) && line.IndexOf(':', 5) < 0;

    /// <summary>The token login line for a token, a connection type and an optional client version.</summary>
    public static string FormatTokenLogin(string token, string connectionType, string? clientVersion) =>
        string.IsNullOrEmpty(clientVersion)
            ? $"{TokenLoginPrefix}{token} {connectionType}"
            : $"{TokenLoginPrefix}{token} {connectionType} {clientVersion}";

    /// <summary>
    /// Parse and validate a token login line: the selector, the verifier, the connection type (may carry
    /// ";echo=1") and the optional client version, each in its own alphabet and length. False for anything else.
    /// </summary>
    public static bool TryParseTokenLogin(string? line, out string selector, out string verifier,
        out string connectionType, out string? clientVersion)
    {
        selector = verifier = connectionType = "";
        clientVersion = null;
        if (!IsTokenLoginLine(line)) return false;
        var fields = line!.Substring(TokenLoginPrefix.Length).Split(' ');
        if (fields.Length < 2 || fields.Length > 3) return false;
        if (!TrySplitToken(fields[0], out selector, out verifier)) return false;
        if (!IsTypeText(fields[1])) return false;
        connectionType = fields[1];
        if (fields.Length == 3)
        {
            if (!IsVersionText(fields[2])) return false;
            clientVersion = fields[2].Length == 0 ? null : fields[2];
        }
        return true;
    }

    /// <summary>Split "&lt;selector&gt;.&lt;verifier&gt;" and check both alphabets and lengths.</summary>
    public static bool TrySplitToken(string? token, out string selector, out string verifier)
    {
        selector = verifier = "";
        if (token == null) return false;
        int dot = token.IndexOf('.');
        if (dot < 0) return false;
        string s = token.Substring(0, dot), v = token.Substring(dot + 1);
        if (s.Length != SelectorLength || v.Length != VerifierLength) return false;
        foreach (char c in s) if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        foreach (char c in v) if (!IsBase64UrlChar(c)) return false;
        if (DecodeVerifier(v) == null) return false;
        selector = s;
        verifier = v;
        return true;
    }

    private static bool IsBase64UrlChar(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_';

    private static bool IsTypeText(string s)
    {
        if (s.Length < 1 || s.Length > MaxTypeLength) return false;
        foreach (char c in s)
            if (!(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or ';' or '=' or '_' or '.' or '-')) return false;
        return true;
    }

    private static bool IsVersionText(string s)
    {
        if (s.Length > MaxVersionLength) return false;
        foreach (char c in s)
            if (!(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-')) return false;
        return true;
    }

    /// <summary>A new token: (selector, verifier, the verifier's 32 bytes). Cryptographic randomness.</summary>
    public static (string Selector, string Verifier, byte[] VerifierBytes) NewToken()
    {
        string selector = Convert.ToHexString(RandomNumberGenerator.GetBytes(SelectorBytes)).ToLowerInvariant();
        byte[] raw = RandomNumberGenerator.GetBytes(VerifierBytes);
        return (selector, EncodeVerifier(raw), raw);
    }

    public static string EncodeVerifier(byte[] raw) =>
        Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The verifier's bytes, or null when it is not 32 bytes of base64url.</summary>
    public static byte[]? DecodeVerifier(string verifier)
    {
        if (verifier == null || verifier.Length != VerifierLength) return null;
        string b64 = verifier.Replace('-', '+').Replace('_', '/') + "=";
        var buffer = new byte[VerifierBytes + 2];
        if (!Convert.TryFromBase64String(b64, buffer, out int n) || n != VerifierBytes) return null;
        var raw = new byte[VerifierBytes];
        Array.Copy(buffer, raw, VerifierBytes);
        return raw;
    }

    /// <summary>base64 SHA-256 of the verifier's bytes: what the server stores.</summary>
    public static string HashVerifier(byte[] raw) => Convert.ToBase64String(SHA256.HashData(raw));

    /// <summary>base64 SHA-256 of the account's stored password hash: the stamp every token row carries.</summary>
    public static string PasswordStamp(string passwordHash) =>
        Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(passwordHash ?? "")));

    /// <summary>The label kept with a row: the connection type name, letters, digits, '_' and '-' only, at most 16.</summary>
    public static string Label(string? connectionType)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in connectionType ?? "")
        {
            if (sb.Length >= 16) break;
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-') sb.Append(c);
        }
        return sb.Length == 0 ? "unknown" : sb.ToString();
    }
}
