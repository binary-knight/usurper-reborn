using System.Threading.Tasks;

namespace UsurperRemake
{
    /// <summary>
    /// Presentation pauses (screen pacing). Outside the test run this is a plain Task.Delay.
    /// Only the test project sets Disabled (Tests/TestPacing.cs), which turns the pause into
    /// a yield so the continuation stays asynchronous but no wall time passes.
    /// </summary>
    public static class Pacing
    {
        internal static bool Disabled { get; set; }

        public static async Task Wait(int ms)
        {
            if (Disabled)
            {
                await Task.Yield();
                return;
            }
            await Task.Delay(ms);
        }
    }
}
