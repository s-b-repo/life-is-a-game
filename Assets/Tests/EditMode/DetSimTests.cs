// Phase 4 determinism tests.
//
// The invariant: identical inputs from a common start state produce
// byte-identical PlayerSimState[] and identical state hashes across
// two independent sim runs, on any runtime this test suite compiles
// under. If any of these fail, the lockstep contract in Phase 5 is
// broken by definition.

using LifeIsAGame.DetSim;
using NUnit.Framework;

namespace LifeIsAGame.Tests
{
    public sealed class DetSimTests
    {
        [Test]
        public void Fix64_Add_IsAssociative()
        {
            Fix64 a = Fix64.FromFloat(1.5f);
            Fix64 b = Fix64.FromFloat(2.25f);
            Fix64 c = Fix64.FromFloat(0.125f);
            Assert.AreEqual(((a + b) + c).raw, (a + (b + c)).raw,
                "Integer add MUST be associative; if this fails Fix64 is broken.");
        }

        [Test]
        public void Fix64_Mul_ByOne_IsIdentity()
        {
            Fix64 x = Fix64.FromFloat(3.14159f);
            Assert.AreEqual(x.raw, (x * Fix64.One).raw);
            Assert.AreEqual(x.raw, (Fix64.One * x).raw);
        }

        [Test]
        public void Fix64_Sqrt_OfFour_IsTwo_WithinOneUlp()
        {
            Fix64 four = Fix64.FromInt(4);
            Fix64 root = Fix64.Sqrt(four);
            long diff = System.Math.Abs(root.raw - Fix64.FromInt(2).raw);
            Assert.LessOrEqual(diff, 4L, "Sqrt(4) should be ~2 to within a few ULPs.");
        }

        [Test]
        public void Fix64_Sin_IsApproximatelyMathSin()
        {
            // 0, π/4, π/2, π — sanity anchors.
            AssertNear(Fix64.Sin(Fix64.Zero),      0.0,          1e-3);
            AssertNear(Fix64.Sin(Fix64.FromFloat((float)System.Math.PI / 4f)), 0.7071068, 1e-3);
            AssertNear(Fix64.Sin(Fix64.PiOver2),   1.0,          1e-3);
            AssertNear(Fix64.Sin(Fix64.Pi),        0.0,          1e-3);
        }

        [Test]
        public void DetRandom_SameSeed_ProducesSameSequence()
        {
            const ulong SEED = 0xA5A5F00D_12345678UL;

            DetRandom.Seed(SEED);
            ulong[] a = new ulong[16];
            for (int i = 0; i < a.Length; i++) a[i] = DetRandom.NextUInt64();

            DetRandom.Seed(SEED);
            ulong[] b = new ulong[16];
            for (int i = 0; i < b.Length; i++) b[i] = DetRandom.NextUInt64();

            CollectionAssert.AreEqual(a, b,
                "xorshift128+ from the same seed must produce byte-identical output.");
        }

        [Test]
        public void DetRandom_DifferentSeeds_ProduceDifferentSequences()
        {
            DetRandom.Seed(1UL);
            ulong a0 = DetRandom.NextUInt64();
            DetRandom.Seed(2UL);
            ulong b0 = DetRandom.NextUInt64();
            Assert.AreNotEqual(a0, b0);
        }

        [Test]
        public void Sim_TwoRuns_SameInputs_ProduceSameHash()
        {
            byte[] hashA = RunSim(inputPattern: 0xA55A, ticks: 300, seed: 12345UL);
            byte[] hashB = RunSim(inputPattern: 0xA55A, ticks: 300, seed: 12345UL);
            CollectionAssert.AreEqual(hashA, hashB,
                "Determinism failure: identical inputs + seed produced diverging state.");
        }

        [Test]
        public void Sim_TwoRuns_ForgedInput_ProduceDifferentHash()
        {
            byte[] honest    = RunSim(inputPattern: 0xA55A, ticks: 300, seed: 12345UL);
            byte[] forged    = RunSim(inputPattern: 0xA55B, ticks: 300, seed: 12345UL); // one bit flip on inputs
            CollectionAssert.AreNotEqual(honest, forged,
                "A single-bit input change must change the state hash — otherwise the hash can't detect forged inputs.");
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Run a small 2-player sim for `ticks` steps with a fixed
        /// pseudo-input pattern. Returns the state hash after the final tick.
        /// </summary>
        private static byte[] RunSim(ushort inputPattern, int ticks, ulong seed)
        {
            DetRandom.Seed(seed);
            TickClock.Reset();

            var players = new PlayerSimState[]
            {
                new PlayerSimState { PlayerId = 0, Alive = true, Grounded = true, Health = Fix64.FromInt(100), Pos = new FixVector3(Fix64.FromInt(-1), Fix64.Zero, Fix64.Zero) },
                new PlayerSimState { PlayerId = 1, Alive = true, Grounded = true, Health = Fix64.FromInt(100), Pos = new FixVector3(Fix64.FromInt( 1), Fix64.Zero, Fix64.Zero) },
            };
            var inputs = new InputCommand[2];

            for (int t = 1; t <= ticks; t++)
            {
                // Deterministic-per-tick input pattern (both peers do the same thing).
                InputBits bits = (InputBits)((inputPattern >> (t & 15)) & 0xF);
                inputs[0] = new InputCommand
                {
                    Tick = (uint)t,
                    PlayerId = 0,
                    Buttons = bits,
                    MouseDeltaYaw = Fix64.FromFloat(0.01f),
                };
                inputs[1] = new InputCommand
                {
                    Tick = (uint)t,
                    PlayerId = 1,
                    Buttons = bits,
                    MouseDeltaYaw = Fix64.FromFloat(-0.01f),
                };
                DeterministicSim.Advance(players, inputs);
            }

            return DeterministicSim.HashState(players);
        }

        private static void AssertNear(Fix64 actual, double expected, double tol)
        {
            double diff = System.Math.Abs(actual.ToDouble() - expected);
            Assert.LessOrEqual(diff, tol,
                $"Expected ~{expected}, got {actual.ToDouble()} (diff {diff})");
        }
    }
}
