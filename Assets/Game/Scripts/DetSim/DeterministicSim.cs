// Deterministic simulation core.
//
// Owns the array of PlayerSimState — one per connected peer — and
// advances all of them one tick at a time given per-player inputs.
// Everything here is Fix64 / integer, so two peers with the same
// InputCommand[] since tick 0 produce byte-identical PlayerSimState[]
// after every tick. That is the invariant Phase 5's StateHashChain
// verifies each K ticks.
//
// This Phase 4 implementation covers movement + gravity + a ground
// plane at y=0 as a proof-of-concept — enough for the anti-cheat
// tests to demonstrate hash consensus and forged-input rejection.
// Full FPS physics (kinematic capsule vs. static mesh sweep) is a
// bounded follow-up scoped under the KinematicMover stub below;
// swapping the placeholder for the real sweep does not change the
// LockstepEngine or hashing surfaces above it.

using System;

namespace LifeIsAGame.DetSim
{
    [Flags]
    public enum InputBits : ushort
    {
        None    = 0,
        Forward = 1 << 0,
        Back    = 1 << 1,
        Left    = 1 << 2,
        Right   = 1 << 3,
        Jump    = 1 << 4,
        Crouch  = 1 << 5,
    }

    /// <summary>What one peer signed and broadcast for one tick.</summary>
    [Serializable]
    public struct InputCommand
    {
        public uint      Tick;
        public byte      PlayerId;
        public InputBits Buttons;
        public Fix64     MouseDeltaYaw;   // radians this tick
    }

    public static class DeterministicSim
    {
        // Tunables — expressed as Fix64 so no float leaks into the sim.
        public static readonly Fix64 GRAVITY        = Fix64.FromFloat(-19.62f);
        public static readonly Fix64 MOVE_ACCEL     = Fix64.FromFloat(60f);
        public static readonly Fix64 MAX_SPEED      = Fix64.FromFloat(8f);
        public static readonly Fix64 JUMP_VELOCITY  = Fix64.FromFloat(7f);
        public static readonly Fix64 GROUND_Y       = Fix64.Zero;
        public static readonly Fix64 TICK_DT        = Fix64.FromInt(1) / Fix64.FromInt(TickClock.TICKS_PER_SECOND);

        // Per-tick allocation-free scratch.
        private static readonly byte[] _hashScratch = new byte[4096];

        /// <summary>
        /// Advance every player one tick. inputs[i] MUST correspond to
        /// players[i]. Callers align by PlayerId ordering.
        /// </summary>
        public static void Advance(PlayerSimState[] players, InputCommand[] inputs)
        {
            if (players == null || inputs == null || players.Length != inputs.Length) return;

            for (int i = 0; i < players.Length; i++)
            {
                players[i] = AdvanceOne(players[i], inputs[i]);
            }

            TickClock.Advance();
        }

        /// <summary>Pure function — no shared state — so it's trivially deterministic and testable.</summary>
        public static PlayerSimState AdvanceOne(PlayerSimState s, InputCommand cmd)
        {
            s.LastInputTick = cmd.Tick;
            if (!s.Alive) return s;

            // Yaw update. Deltas were already Fix64.
            s.YawRad += cmd.MouseDeltaYaw;

            // Local WASD -> forward/right components.
            Fix64 fwd  = ((cmd.Buttons & InputBits.Forward) != 0 ? Fix64.One  : Fix64.Zero)
                       - ((cmd.Buttons & InputBits.Back)    != 0 ? Fix64.One  : Fix64.Zero);
            Fix64 strafe = ((cmd.Buttons & InputBits.Right) != 0 ? Fix64.One : Fix64.Zero)
                       - ((cmd.Buttons & InputBits.Left)    != 0 ? Fix64.One : Fix64.Zero);

            // Rotate (fwd, strafe) into world XZ using yaw.
            Fix64 cos = Fix64.Cos(s.YawRad);
            Fix64 sin = Fix64.Sin(s.YawRad);
            FixVector3 worldDir = new FixVector3(
                strafe * cos + fwd * sin,
                Fix64.Zero,
                fwd * cos - strafe * sin);

            // Accelerate horizontally, cap speed.
            s.Vel.x += worldDir.x * MOVE_ACCEL * TICK_DT;
            s.Vel.z += worldDir.z * MOVE_ACCEL * TICK_DT;

            Fix64 planarSq = s.Vel.x * s.Vel.x + s.Vel.z * s.Vel.z;
            Fix64 maxSq    = MAX_SPEED * MAX_SPEED;
            if (planarSq > maxSq)
            {
                Fix64 planar = Fix64.Sqrt(planarSq);
                Fix64 k = MAX_SPEED / planar;
                s.Vel.x *= k;
                s.Vel.z *= k;
            }

            // Jump — only from ground, sets vertical velocity.
            if ((cmd.Buttons & InputBits.Jump) != 0 && s.Grounded)
            {
                s.Vel.y = JUMP_VELOCITY;
                s.Grounded = false;
                s.Jumping = true;
            }
            else if (s.Grounded)
            {
                s.Jumping = false;
            }

            // Gravity + integrate.
            s.Vel.y += GRAVITY * TICK_DT;
            s.Pos += s.Vel * TICK_DT;

            // Ground plane at y=0.
            if (s.Pos.y <= GROUND_Y)
            {
                s.Pos.y = GROUND_Y;
                if (s.Vel.y < Fix64.Zero) s.Vel.y = Fix64.Zero;
                s.Grounded = true;
            }
            else
            {
                s.Grounded = false;
            }

            s.Crouching = (cmd.Buttons & InputBits.Crouch) != 0;
            return s;
        }

        /// <summary>Byte-stable hash of the current world state. See LockstepEngine (Phase 5) for how it's used.</summary>
        public static byte[] HashState(PlayerSimState[] players)
        {
            int need = 4 /*tick*/ + players.Length * PlayerSimState.SERIALIZED_BYTES;
            if (_hashScratch.Length < need)
                throw new InvalidOperationException("Increase _hashScratch — too many players for the fixed buffer.");

            int offset = 0;
            uint tick = TickClock.CurrentTick;
            _hashScratch[offset++] = (byte)(tick       & 0xFF);
            _hashScratch[offset++] = (byte)((tick >>  8) & 0xFF);
            _hashScratch[offset++] = (byte)((tick >> 16) & 0xFF);
            _hashScratch[offset++] = (byte)((tick >> 24) & 0xFF);

            // Sort by PlayerId so hash is independent of arrival order.
            Array.Sort(players, (a, b) => a.PlayerId.CompareTo(b.PlayerId));
            for (int i = 0; i < players.Length; i++)
            {
                players[i].SerializeTo(_hashScratch, offset);
                offset += PlayerSimState.SERIALIZED_BYTES;
            }

            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                return sha.ComputeHash(_hashScratch, 0, offset);
            }
        }
    }
}
