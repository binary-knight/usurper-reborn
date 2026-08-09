
namespace UsurperRemake.Systems
{
    /// <summary>
    /// The "FIRST IMPRESSION" line shown at the top of a Team Corner examine.
    ///
    /// Deterministic, instant, and works in every mode.
    /// </summary>
    public static class NPCImpressionText
    {
        public static string Build(NPC npc)
        {
            if (npc == null) return "";

            string name = npc.Name2 ?? npc.Name1 ?? "This stranger";
            string charClass = npc.Class.ToString();
            string archetype = npc.Archetype ?? "citizen";
            var p = npc.Brain?.Personality;

            if (p == null)
                return $"{name} is a {charClass} -- {archetype} by trade, unremarkable on a first look.";

            string tone = p.Aggression > 0.7f ? "hard-eyed"
                : p.Sociability > 0.7f ? "open and easy"
                : p.Patience < 0.3f ? "restless"
                : "watchful";

            string drive = p.Greed > 0.7f ? "the coin in your purse"
                : p.Ambition > 0.7f ? "what you can do for them"
                : p.Loyalty > 0.7f ? "where you stand with their kin"
                : "the room and who else is in it";

            return $"{name} the {charClass} is {tone}. Their attention is on {drive}.";
        }
    }
}
