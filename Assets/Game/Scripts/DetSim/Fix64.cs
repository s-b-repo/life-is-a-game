// Q31.32 signed fixed-point number.
//
// Deterministic arithmetic across CPUs, OSes, and .NET runtimes. Every
// gameplay calculation that must produce the same result on host and
// client for lockstep verification uses this type instead of `float`.
//
// Storage: 64 bits, split as 32.32 (sign bundled into the integer part).
// Range: ~±2.15e9. Precision: 1 / 2^32 ≈ 2.33e-10.
//
// Compared to the reference FixedMath.Net library this is a compact
// subset — the arithmetic operators, comparison, and the trig / sqrt
// approximations we actually need for the sim (Sin, Cos, Atan2, Sqrt,
// Abs, Sign, Min, Max, Clamp, Lerp). Anything more exotic (Exp, Log)
// can be added as needed. Correctness bound: every operator gives the
// same result for the same inputs on every runtime this code compiles
// under — that's the invariant lockstep depends on.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace LifeIsAGame.DetSim
{
    [Serializable]
    public struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>
    {
        // Backing storage — 64-bit signed with 32 fractional bits.
        public long raw;

        public const int FRACTIONAL_BITS = 32;
        public const long ONE_RAW = 1L << FRACTIONAL_BITS;
        public const long HALF_RAW = 1L << (FRACTIONAL_BITS - 1);

        public static readonly Fix64 Zero      = new Fix64 { raw = 0L };
        public static readonly Fix64 One       = new Fix64 { raw = ONE_RAW };
        public static readonly Fix64 NegOne    = new Fix64 { raw = -ONE_RAW };
        public static readonly Fix64 Half      = new Fix64 { raw = HALF_RAW };
        public static readonly Fix64 MaxValue  = new Fix64 { raw = long.MaxValue };
        public static readonly Fix64 MinValue  = new Fix64 { raw = long.MinValue };
        public static readonly Fix64 Pi        = FromRaw(13493037705L);       // π * 2^32
        public static readonly Fix64 TwoPi     = FromRaw(26986075410L);       // 2π * 2^32
        public static readonly Fix64 PiOver2   = FromRaw(6746518852L);        // π/2 * 2^32
        public static readonly Fix64 Rad2Deg   = FromRaw(246083499208L);      // (180/π) * 2^32
        public static readonly Fix64 Deg2Rad   = FromRaw(74961320L);          // (π/180) * 2^32
        public static readonly Fix64 Epsilon   = FromRaw(1L);                 // smallest positive

        // -----------------------------------------------------------------
        // Constructors / conversions
        // -----------------------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 FromRaw(long r) => new Fix64 { raw = r };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 FromInt(int i) => new Fix64 { raw = (long)i << FRACTIONAL_BITS };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 FromInt(long i) => new Fix64 { raw = i << FRACTIONAL_BITS };

        /// <summary>
        /// Approximate conversion from a float. Deterministic if `f`'s bits are
        /// identical on both sides — that's on the caller. Prefer FromInt +
        /// arithmetic when possible so no float leaks into the sim.
        /// </summary>
        public static Fix64 FromFloat(float f) => new Fix64 { raw = (long)(f * (float)ONE_RAW) };

        public static Fix64 FromDouble(double d) => new Fix64 { raw = (long)(d * (double)ONE_RAW) };

        public int   ToInt()    => (int)(raw >> FRACTIONAL_BITS);
        public float ToFloat()  => (float)raw / (float)ONE_RAW;
        public double ToDouble() => (double)raw / (double)ONE_RAW;

        public override string ToString() => ToDouble().ToString("0.##########");

        // -----------------------------------------------------------------
        // Arithmetic operators
        // -----------------------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 operator +(Fix64 a, Fix64 b) => new Fix64 { raw = a.raw + b.raw };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 operator -(Fix64 a, Fix64 b) => new Fix64 { raw = a.raw - b.raw };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 operator -(Fix64 a) => new Fix64 { raw = -a.raw };

        public static Fix64 operator *(Fix64 a, Fix64 b)
        {
            // BigInteger keeps the full 128-bit product exact regardless of
            // input magnitude, and its arithmetic is defined identically on
            // every .NET runtime — the whole point of Fix64.
            BigInteger product = (BigInteger)a.raw * b.raw;
            return new Fix64 { raw = (long)(product >> FRACTIONAL_BITS) };
        }

        public static Fix64 operator /(Fix64 a, Fix64 b)
        {
            if (b.raw == 0L) return a.raw >= 0 ? MaxValue : MinValue;
            // Shift numerator up by FRACTIONAL_BITS BEFORE dividing so the
            // scale ends up right; use BigInteger to survive the shift
            // without overflowing.
            BigInteger scaled = ((BigInteger)a.raw) << FRACTIONAL_BITS;
            return new Fix64 { raw = (long)(scaled / b.raw) };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 operator %(Fix64 a, Fix64 b) => new Fix64 { raw = a.raw % b.raw };

        // -----------------------------------------------------------------
        // Comparisons
        // -----------------------------------------------------------------

        public static bool operator ==(Fix64 a, Fix64 b) => a.raw == b.raw;
        public static bool operator !=(Fix64 a, Fix64 b) => a.raw != b.raw;
        public static bool operator < (Fix64 a, Fix64 b) => a.raw <  b.raw;
        public static bool operator > (Fix64 a, Fix64 b) => a.raw >  b.raw;
        public static bool operator <=(Fix64 a, Fix64 b) => a.raw <= b.raw;
        public static bool operator >=(Fix64 a, Fix64 b) => a.raw >= b.raw;

        public bool Equals(Fix64 other) => raw == other.raw;
        public override bool Equals(object obj) => obj is Fix64 f && Equals(f);
        public override int GetHashCode() => raw.GetHashCode();
        public int CompareTo(Fix64 other) => raw.CompareTo(other.raw);

        // -----------------------------------------------------------------
        // Static helpers
        // -----------------------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Abs(Fix64 x) => x.raw < 0 ? new Fix64 { raw = -x.raw } : x;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sign(Fix64 x) => x.raw > 0 ? 1 : (x.raw < 0 ? -1 : 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Min(Fix64 a, Fix64 b) => a.raw < b.raw ? a : b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Max(Fix64 a, Fix64 b) => a.raw > b.raw ? a : b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Clamp(Fix64 x, Fix64 lo, Fix64 hi)
            => x.raw < lo.raw ? lo : (x.raw > hi.raw ? hi : x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Lerp(Fix64 a, Fix64 b, Fix64 t)
        {
            t = Clamp(t, Zero, One);
            return a + (b - a) * t;
        }

        /// <summary>Newton–Raphson integer sqrt. Deterministic; correct within ~1 ULP.</summary>
        public static Fix64 Sqrt(Fix64 x)
        {
            if (x.raw < 0) throw new ArgumentException("Sqrt of negative Fix64.");
            if (x.raw == 0) return Zero;

            // Bit-scan to bracket the initial guess: sqrt of a k-bit number
            // has ~k/2 bits.
            ulong u = (ulong)x.raw;
            int msb = 63;
            while (msb > 0 && (u & (1UL << msb)) == 0) msb--;
            int guess_shift = (msb + FRACTIONAL_BITS) >> 1;
            long guess = 1L << guess_shift;

            // Newton iterate: g' = (g + (x << 32) / g) / 2. BigInteger keeps
            // (x << 32) exact across the whole range so we never overflow.
            BigInteger xShifted = ((BigInteger)x.raw) << FRACTIONAL_BITS;
            for (int i = 0; i < 16; i++)
            {
                if (guess == 0L) break;
                long next = (long)((guess + xShifted / guess) >> 1);
                if (next == guess) break; // fixed point
                guess = next;
            }
            return new Fix64 { raw = guess };
        }

        /// <summary>Sine via CORDIC-like 5-term Taylor series after argument reduction.</summary>
        public static Fix64 Sin(Fix64 x)
        {
            // Reduce to [-π, π].
            Fix64 xr = x % TwoPi;
            if (xr > Pi)      xr -= TwoPi;
            else if (xr < -Pi) xr += TwoPi;

            // Symmetry: sin(-x) = -sin(x).
            bool neg = xr.raw < 0;
            if (neg) xr = -xr;

            // Symmetry: sin(π - x) = sin(x), keep argument in [0, π/2].
            if (xr > PiOver2) xr = Pi - xr;

            // 5-term Taylor around 0: sin(x) = x - x^3/6 + x^5/120 - x^7/5040 + x^9/362880.
            Fix64 x2 = xr * xr;
            Fix64 x3 = x2 * xr;
            Fix64 x5 = x3 * x2;
            Fix64 x7 = x5 * x2;
            Fix64 x9 = x7 * x2;
            Fix64 result =
                  xr
                - x3 / FromInt(6)
                + x5 / FromInt(120)
                - x7 / FromInt(5040)
                + x9 / FromInt(362880);
            return neg ? -result : result;
        }

        /// <summary>Cosine — sin with a phase shift.</summary>
        public static Fix64 Cos(Fix64 x) => Sin(x + PiOver2);

        /// <summary>4-quadrant atan2. Approximation good to ~1e-4 rad.</summary>
        public static Fix64 Atan2(Fix64 y, Fix64 x)
        {
            if (x.raw == 0 && y.raw == 0) return Zero;

            // Rational approximation from "Efficient Approximations for the
            // Arctangent Function" (Rajan, Chen, Rich 2006).
            Fix64 absY = Abs(y) + FromRaw(1); // avoid div-by-zero
            Fix64 angle;

            if (x.raw >= 0)
            {
                Fix64 r = (x - absY) / (x + absY);
                Fix64 r3 = r * r * r;
                angle = FromFloat(0.1963f) * r3 - FromFloat(0.9817f) * r + FromFloat(0.7853982f);
            }
            else
            {
                Fix64 r = (x + absY) / (absY - x);
                Fix64 r3 = r * r * r;
                angle = FromFloat(0.1963f) * r3 - FromFloat(0.9817f) * r + FromFloat(2.3561945f);
            }

            return y.raw < 0 ? -angle : angle;
        }
    }
}
