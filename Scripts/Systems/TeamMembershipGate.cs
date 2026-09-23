using System.Threading;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// v1.1.11: joining a team (checking it exists, taking the membership, saving it) and the world
    /// save's removal of an empty team (the last check that nobody is in it, and the delete) must not
    /// interleave: a join between the two lost the team, its upgrades and vault (review). Both hold this.
    /// </summary>
    public static class TeamMembershipGate
    {
        public static readonly SemaphoreSlim Gate = new(1, 1);
    }
}
