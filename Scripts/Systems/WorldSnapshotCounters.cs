using System.Threading;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// The world_snapshots event counts (births, NPC deaths, marriages), kept in memory for the process and added to
    /// by the code paths that create a child, kill a roster NPC and marry two characters. Player events count too,
    /// with no names. The MUD's world sim reads and resets them when it writes the hour's row
    /// (WorldSimService.WriteHourlySnapshotIfDue); a row that is not written gives its counts back. Thread safe.
    /// </summary>
    public static class WorldSnapshotCounters
    {
        private static long _births;
        private static long _deaths;
        private static long _marriages;

        /// <summary>A child was registered (FamilySystem.RegisterChild).</summary>
        public static void AddBirth() => Interlocked.Increment(ref _births);

        /// <summary>A roster NPC died, permanent or not (the flag flip to IsDead at each site).</summary>
        public static void AddDeath() => Interlocked.Increment(ref _deaths);

        /// <summary>Two characters married (one per ceremony, not one per spouse).</summary>
        public static void AddMarriage() => Interlocked.Increment(ref _marriages);

        public static long Births => Interlocked.Read(ref _births);
        public static long Deaths => Interlocked.Read(ref _deaths);
        public static long Marriages => Interlocked.Read(ref _marriages);

        /// <summary>The counts since the last take, each set back to zero.</summary>
        public static (long Births, long Deaths, long Marriages) TakeAndReset() =>
            (Interlocked.Exchange(ref _births, 0), Interlocked.Exchange(ref _deaths, 0), Interlocked.Exchange(ref _marriages, 0));

        /// <summary>Counts taken for a row that was not written, added back so they reach the next row.</summary>
        public static void GiveBack(long births, long deaths, long marriages)
        {
            if (births != 0) Interlocked.Add(ref _births, births);
            if (deaths != 0) Interlocked.Add(ref _deaths, deaths);
            if (marriages != 0) Interlocked.Add(ref _marriages, marriages);
        }
    }
}
