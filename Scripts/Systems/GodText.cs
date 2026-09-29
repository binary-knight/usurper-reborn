using System;

namespace UsurperRemake.Systems;

/// <summary>
/// 1.2.0 Temple gods piece 6: the text shown for a god. A canon god's epithet and description come
/// from Loc (god.epithet.* and god.desc.*, five languages); god names are proper nouns and are shown
/// as they are. Any other god falls back to what GodSystem stores for it.
/// </summary>
public static class GodText
{
    /// <summary>The god's epithet ("The Radiant"): Loc for a canon god, else its stored domain or rank title.</summary>
    public static string Epithet(God god)
    {
        if (god == null) return "";
        string? canon = GodRegistry.CanonName(god.Name);
        if (canon != null) return Loc.Get("god.epithet." + canon.ToLowerInvariant());
        return god.Properties.TryGetValue("Domain", out var d) && d?.ToString() is { Length: > 0 } s ? s : god.GetTitle();
    }

    /// <summary>
    /// A god rank title ("Lesser Spirit" at 1 to "God" at 9) from Loc (god.title.1 to god.title.9,
    /// five languages); a level outside 1..9 is clamped. Shared text built for other players (news,
    /// the chat name, messages to another session) keeps GameConfig.GodTitles, as the news does.
    /// </summary>
    public static string Title(int level) =>
        Loc.Get("god.title." + Math.Clamp(level, 1, GameConfig.GodTitles.Length));

    /// <summary>The god's description: Loc for a canon god, else its stored description ("" with none).</summary>
    public static string Description(God god)
    {
        if (god == null) return "";
        string? canon = GodRegistry.CanonName(god.Name);
        if (canon != null) return Loc.Get("god.desc." + canon.ToLowerInvariant());
        return god.Properties.TryGetValue("Description", out var d) ? d?.ToString() ?? "" : "";
    }
}
