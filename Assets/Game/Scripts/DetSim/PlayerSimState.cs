// Per-player sim state — the authoritative snapshot each peer keeps.
//
// This is the object hashed for lockstep verification. Two peers with
// the same inputs since tick 0 must produce byte-identical
// PlayerSimState arrays after any given tick or a desync is declared
// and the offender voted off.
//
// Deliberately blittable POD: no Unity references, no strings, no
// nullable fields. Copy-by-value cheap and safe.

using System;

namespace LifeIsAGame.DetSim
{
    [Serializable]
    public struct PlayerSimState : IEquatable<PlayerSimState>
    {
        public byte       PlayerId;      // stable id (same value on every peer)
        public uint       LastInputTick; // tick of the last input applied
        public FixVector3 Pos;
        public FixVector3 Vel;
        public Fix64      YawRad;        // simple rotation about Y — enough for the FPS body
        public Fix64      Health;
        public byte       Flags;         // bit 0 = alive, bit 1 = grounded, bit 2 = crouching, bit 3 = jumping

        public bool Alive     { get => (Flags & 0x01) != 0; set => Flags = (byte)(value ? Flags | 0x01 : Flags & ~0x01); }
        public bool Grounded  { get => (Flags & 0x02) != 0; set => Flags = (byte)(value ? Flags | 0x02 : Flags & ~0x02); }
        public bool Crouching { get => (Flags & 0x04) != 0; set => Flags = (byte)(value ? Flags | 0x04 : Flags & ~0x04); }
        public bool Jumping   { get => (Flags & 0x08) != 0; set => Flags = (byte)(value ? Flags | 0x08 : Flags & ~0x08); }

        // ---- serialisation for hashing / RPC ----
        public const int SERIALIZED_BYTES = 1 + 4 + 24 + 24 + 8 + 8 + 1;

        public void SerializeTo(byte[] buf, int offset)
        {
            buf[offset++] = PlayerId;
            WriteUInt(buf, ref offset, LastInputTick);
            Pos.CopyRawTo(buf, offset); offset += 24;
            Vel.CopyRawTo(buf, offset); offset += 24;
            WriteLong(buf, ref offset, YawRad.raw);
            WriteLong(buf, ref offset, Health.raw);
            buf[offset++] = Flags;
        }

        public bool Equals(PlayerSimState other)
            => PlayerId == other.PlayerId
            && LastInputTick == other.LastInputTick
            && Pos == other.Pos
            && Vel == other.Vel
            && YawRad == other.YawRad
            && Health == other.Health
            && Flags == other.Flags;

        public override bool Equals(object obj) => obj is PlayerSimState s && Equals(s);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = PlayerId;
                h = (h * 397) ^ (int)LastInputTick;
                h = (h * 397) ^ Pos.GetHashCode();
                h = (h * 397) ^ Vel.GetHashCode();
                h = (h * 397) ^ YawRad.GetHashCode();
                h = (h * 397) ^ Health.GetHashCode();
                h = (h * 397) ^ Flags;
                return h;
            }
        }

        // ---- helpers ----

        private static void WriteUInt(byte[] buf, ref int offset, uint v)
        {
            buf[offset++] = (byte)(v       & 0xFF);
            buf[offset++] = (byte)((v >>  8) & 0xFF);
            buf[offset++] = (byte)((v >> 16) & 0xFF);
            buf[offset++] = (byte)((v >> 24) & 0xFF);
        }
        private static void WriteLong(byte[] buf, ref int offset, long v)
        {
            for (int i = 0; i < 8; i++)
            {
                buf[offset++] = (byte)((v >> (i * 8)) & 0xFF);
            }
        }
    }
}
