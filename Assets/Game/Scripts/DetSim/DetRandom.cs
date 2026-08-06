// Deterministic-random shim.
//
// Phase 1  (this file today):  forwards to UnityEngine.Random. Behaviour
//                              is what the game had before.
//
// Phase 4  (planned):          swap the implementation to a seeded
//                              xorshift128+ RNG. The lobby broadcasts a
//                              shared ulong seed at StartGame; every peer
//                              re-seeds its DetRandom to that value so
//                              subsequent Range() calls produce identical
//                              sequences across the lockstep session.
//
// Callers should only reach randomness through this shim so that Phase 4
// can flip the impl in one place without editing gameplay scripts.

using UnityEngine;

namespace LifeIsAGame.DetSim
{
    public static class DetRandom
    {
        /// <summary>Uniform float in [min, max).</summary>
        public static float Range(float min, float max) => Random.Range(min, max);

        /// <summary>Uniform int in [min, max) — half-open, Unity convention.</summary>
        public static int Range(int min, int max) => Random.Range(min, max);

        /// <summary>Set the RNG seed. Phase 1: forwards to UnityEngine.Random.InitState.</summary>
        public static void Seed(int seed) => Random.InitState(seed);
    }
}
