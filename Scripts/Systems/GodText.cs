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

    /// <summary>The god's description: Loc for a canon god, else its stored description ("" with none).</summary>
    public static string Description(God god)
    {
        if (god == null) return "";
        string? canon = GodRegistry.CanonName(god.Name);
        if (canon != null) return Loc.Get("god.desc." + canon.ToLowerInvariant());
        return god.Properties.TryGetValue("Description", out var d) ? d?.ToString() ?? "" : "";
    }
}
