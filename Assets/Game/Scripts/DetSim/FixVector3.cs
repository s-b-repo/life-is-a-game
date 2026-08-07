// 3-component vector using Fix64.
//
// Mirrors UnityEngine.Vector3's surface (Add, Sub, Dot, Cross,
// Magnitude, Normalized, Distance, etc.) but every operation stays in
// Fix64, so it's byte-identical across CPUs.
//
// Serializable so lockstep peers can hash the raw bytes without
// having to project through Unity's non-deterministic Vector3.

using System;
using System.Runtime.CompilerServices;

namespace LifeIsAGame.DetSim
{
    [Serializable]
    public struct FixVector3 : IEquatable<FixVector3>
    {
        public Fix64 x, y, z;

        public static readonly FixVector3 Zero     = new FixVector3();
        public static readonly FixVector3 One      = new FixVector3(Fix64.One, Fix64.One, Fix64.One);
        public static readonly FixVector3 Up       = new FixVector3(Fix64.Zero, Fix64.One, Fix64.Zero);
        public static readonly FixVector3 Down     = new FixVector3(Fix64.Zero, -Fix64.One, Fix64.Zero);
        public static readonly FixVector3 Right    = new FixVector3(Fix64.One, Fix64.Zero, Fix64.Zero);
        public static readonly FixVector3 Left     = new FixVector3(-Fix64.One, Fix64.Zero, Fix64.Zero);
        public static readonly FixVector3 Forward  = new FixVector3(Fix64.Zero, Fix64.Zero, Fix64.One);
        public static readonly FixVector3 Back     = new FixVector3(Fix64.Zero, Fix64.Zero, -Fix64.One);

        public FixVector3(Fix64 x, Fix64 y, Fix64 z) { this.x = x; this.y = y; this.z = z; }

        public static FixVector3 FromFloats(float x, float y, float z)
            => new FixVector3(Fix64.FromFloat(x), Fix64.FromFloat(y), Fix64.FromFloat(z));

        /// <summary>Snapshot the raw bytes for lockstep state-hash inputs.</summary>
        public void CopyRawTo(byte[] buf, int offset)
        {
            WriteLong(buf, offset,      x.raw);
            WriteLong(buf, offset + 8,  y.raw);
            WriteLong(buf, offset + 16, z.raw);
        }

        public UnityEngine.Vector3 ToUnity() => new UnityEngine.Vector3(x.ToFloat(), y.ToFloat(), z.ToFloat());

        public override string ToString() => $"({x}, {y}, {z})";

        // -----------------------------------------------------------------
        // Operators
        // -----------------------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixVector3 operator +(FixVector3 a, FixVector3 b)
            => new FixVector3(a.x + b.x, a.y + b.y, a.z + b.z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixVector3 operator -(FixVector3 a, FixVector3 b)
            => new FixVector3(a.x - b.x, a.y - b.y, a.z - b.z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixVector3 operator -(FixVector3 v)
            => new FixVector3(-v.x, -v.y, -v.z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixVector3 operator *(FixVector3 v, Fix64 s)
            => new FixVector3(v.x * s, v.y * s, v.z * s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixVector3 operator *(Fix64 s, FixVector3 v) => v * s;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixVector3 operator /(FixVector3 v, Fix64 s)
            => new FixVector3(v.x / s, v.y / s, v.z / s);

        public static bool operator ==(FixVector3 a, FixVector3 b) => a.Equals(b);
        public static bool operator !=(FixVector3 a, FixVector3 b) => !a.Equals(b);

        public bool Equals(FixVector3 other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object o) => o is FixVector3 v && Equals(v);
        public override int GetHashCode()
        {
            unchecked
            {
                int h = x.GetHashCode();
                h = (h * 397) ^ y.GetHashCode();
                h = (h * 397) ^ z.GetHashCode();
                return h;
            }
        }

        // -----------------------------------------------------------------
        // Vector ops
        // -----------------------------------------------------------------

        public Fix64 SqrMagnitude => x * x + y * y + z * z;
        public Fix64 Magnitude => Fix64.Sqrt(SqrMagnitude);

        public FixVector3 Normalized
        {
            get
            {
                Fix64 m = Magnitude;
                if (m.raw == 0L) return Zero;
                return new FixVector3(x / m, y / m, z / m);
            }
        }

        public static Fix64 Dot(FixVector3 a, FixVector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        public static FixVector3 Cross(FixVector3 a, FixVector3 b) => new FixVector3(
            a.y * b.z - a.z * b.y,
            a.z * b.x - a.x * b.z,
            a.x * b.y - a.y * b.x);

        public static Fix64 Distance(FixVector3 a, FixVector3 b) => (a - b).Magnitude;

        public static FixVector3 Lerp(FixVector3 a, FixVector3 b, Fix64 t)
            => new FixVector3(Fix64.Lerp(a.x, b.x, t), Fix64.Lerp(a.y, b.y, t), Fix64.Lerp(a.z, b.z, t));

        // -----------------------------------------------------------------
        // Byte-writer
        // -----------------------------------------------------------------

        private static void WriteLong(byte[] buf, int offset, long value)
        {
            // Little-endian; matches how BinaryWriter serialises longs.
            buf[offset + 0] = (byte)(value       & 0xFF);
            buf[offset + 1] = (byte)((value >>  8) & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
            buf[offset + 4] = (byte)((value >> 32) & 0xFF);
            buf[offset + 5] = (byte)((value >> 40) & 0xFF);
            buf[offset + 6] = (byte)((value >> 48) & 0xFF);
            buf[offset + 7] = (byte)((value >> 56) & 0xFF);
        }
    }
}
