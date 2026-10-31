using System.Threading;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// The world_snapshots event counts (births, NPC deaths, marriages), kept in memory for the process and added to
    /// by the code paths that create a child, kill a roster NPC and marry two characters. Player events count too,
    /// with no names. The MUD's world sim reads and resets them when it writes the hour's row
    /// (WorldSimService.WriteHourlySnapshotIfDue); a row that is not written gives its counts back. Thread safe.
    /// Known limits. Only the MUD server process counts events, because it owns the world. On the official server,
    /// online play reaches it three ways: the web client direct, the SSH relay into the MUD, and telnet through
    /// haproxy to port 4001. BBS doors and standalone play keep their own databases there. A process other than the
    /// MUD that shares the database adds no counts. Deaths are recorded deaths and can undercount on rare revival
    /// paths. Counts not yet written are held in memory only, so a real process
    /// restart loses them.
    /// </summary>
    public static class WorldSnapshotCounters
    {
        private static long _births;
        private static long _deaths;
        private static long _marriages;

        /// <summary>A child was registered (FamilySystem.RegisterChild).</summary>
        public static void AddBirth() => Interlocked.Increment(ref _births);

        /// <summary>A death, counted as is. The death sites call CountNpcDeath, which counts each death once.</summary>
        public static void AddDeath() => Interlocked.Increment(ref _deaths);

        /// <summary>Two characters married (one per ceremony, not one per spouse).</summary>
        public static void AddMarriage() => Interlocked.Increment(ref _marriages);

        /// <summary>
        /// A roster NPC died, permanent or not: counted only when it is not already dead and its death mark
        /// (NPC.SnapshotDeathCounted) changes from 0 to 1, so one death seen by two paths or two threads counts once.
        /// The mark is set back to 0 only by NpcRevived, at each place that brings the NPC back to life. Called
        /// before the site sets IsDead. Returns whether it counted.
        /// </summary>
        public static bool CountNpcDeath(NPC npc)
        {
            if (npc == null || npc.IsDead) return false;
            if (Interlocked.CompareExchange(ref npc.SnapshotDeathCounted, 1, 0) != 0) return false;
            AddDeath();
            return true;
        }

        /// <summary>
        /// The NPC was brought back to life (respawn, Home or Team Corner resurrection), so its next death counts.
        /// The reset lives at revival, not in a scan: a scan can run between a death's count and its IsDead flag, or
        /// miss a revival and a new death that both happen between two scans.
        /// </summary>
        public static void NpcRevived(NPC npc)
        {
            if (npc != null) Interlocked.Exchange(ref npc.SnapshotDeathCounted, 0);
        }

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
