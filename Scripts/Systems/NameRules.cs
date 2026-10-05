namespace UsurperRemake.Systems;

/// <summary>
/// v1.2.6: the one rule for the characters a player may not put in a name they choose.
/// Names reach web pages (dashboard, admin, leaderboards), so a new character, child,
/// divine, team or guild name may not hold &lt; &gt; &amp; or a double quote. Only names
/// typed from now on are checked; names already saved are kept as they are.
/// </summary>
public static class NameRules
{
    /// <summary>The characters a newly chosen name may not contain.</summary>
    public const string MarkupChars = "<>&\"";

    private static readonly char[] MarkupCharArray = MarkupChars.ToCharArray();

    /// <summary>True when <paramref name="name"/> holds any of <see cref="MarkupChars"/>.</summary>
    public static bool HasMarkupChars(string? name) =>
        !string.IsNullOrEmpty(name) && name.IndexOfAny(MarkupCharArray) >= 0;

    /// <summary>The refusal shown when a name holds one of <see cref="MarkupChars"/>.</summary>
    public static string MarkupCharsMessage => Loc.Get("creation.name_bad_chars");
}
