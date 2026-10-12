namespace UsurperRemake.Systems
{
    /// <summary>
    /// 1.2.8 difficulty pass: what MonsterGenerator.GenerateMonsterGroup may know about where the fight is.
    /// Only the two dungeon call sites (DungeonLocation room fights and MonsterEncounter) pass one; every
    /// other caller passes nothing and keeps the draws made before 1.2.8. RealFloor is the dungeon floor the
    /// player stands on, never the monster level: a straggler room asks for monsters several levels below
    /// its floor, and the floor band of a rule is read from here.
    /// </summary>
    public sealed class EncounterRules
    {
        /// <summary>The dungeon floor of the encounter (not the level the monsters are generated at).</summary>
        public int RealFloor { get; }

        private EncounterRules(int realFloor) { RealFloor = realFloor; }

        /// <summary>The rules of a dungeon encounter on the given floor.</summary>
        public static EncounterRules Dungeon(int realFloor) => new EncounterRules(realFloor);
    }
}
