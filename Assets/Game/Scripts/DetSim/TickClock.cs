// Global tick clock — 30 Hz.
//
// Every sim advancement happens inside TickClock.Advance(). The clock
// increments a monotonic uint counter that indexes into every peer's
// InputCommand[] buffer (Phase 5), so peers agree on which inputs
// belong to which tick.
//
// Fixed Timestep in ProjectSettings/TimeManager.asset is set to 1/30 s
// (0.0333) so Unity's FixedUpdate naturally aligns with a tick — the
// LockstepEngine hooks FixedUpdate to Advance().

namespace LifeIsAGame.DetSim
{
    public static class TickClock
    {
        public const int TICKS_PER_SECOND = 30;
        public const long MICROSECONDS_PER_TICK = 1_000_000L / TICKS_PER_SECOND;

        public static uint CurrentTick { get; private set; }

        /// <summary>Reset the clock — called at StartGame after all peers agree on a seed.</summary>
        public static void Reset()
        {
            CurrentTick = 0u;
        }

        /// <summary>Advance one tick. Callers that need to advance N ticks call this N times so state hooks fire per tick.</summary>
        public static void Advance()
        {
            CurrentTick++;
        }

        /// <summary>Seconds since tick 0, as a Fix64 for deterministic use inside the sim.</summary>
        public static Fix64 ElapsedFixSeconds()
            => Fix64.FromInt((int)CurrentTick) / Fix64.FromInt(TICKS_PER_SECOND);
    }
}
