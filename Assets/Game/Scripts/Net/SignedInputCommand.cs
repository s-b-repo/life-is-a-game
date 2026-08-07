// Serializable, signed input command.
//
// Wraps DetSim.InputCommand with an ECDsa signature so the receiving
// peer can verify (a) which player it came from, and (b) that no bit
// was flipped between sender and receiver.
//
// Serialization is manual and byte-stable — no reflection, no
// per-field alignment surprises — so the exact bytes signed by the
// sender are the exact bytes verified by the receiver. This is the
// signed-payload invariant lockstep depends on.

using System;
using LifeIsAGame.DetSim;

namespace LifeIsAGame.Net
{
    public struct SignedInputCommand
    {
        public InputCommand Cmd;
        public byte[]       Signature; // ECDsa signature over PayloadBytes()

        /// <summary>Byte-stable serialisation of the InputCommand fields for signing.</summary>
        public static byte[] PayloadBytes(InputCommand c)
        {
            var buf = new byte[4 + 1 + 2 + 8];
            int o = 0;
            buf[o++] = (byte)(c.Tick        & 0xFF);
            buf[o++] = (byte)((c.Tick >>  8) & 0xFF);
            buf[o++] = (byte)((c.Tick >> 16) & 0xFF);
            buf[o++] = (byte)((c.Tick >> 24) & 0xFF);
            buf[o++] = c.PlayerId;
            ushort b = (ushort)c.Buttons;
            buf[o++] = (byte)(b       & 0xFF);
            buf[o++] = (byte)((b >> 8) & 0xFF);
            long y = c.MouseDeltaYaw.raw;
            for (int i = 0; i < 8; i++)
            {
                buf[o++] = (byte)((y >> (i * 8)) & 0xFF);
            }
            return buf;
        }
    }
}
