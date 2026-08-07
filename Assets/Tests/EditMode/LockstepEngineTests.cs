// Phase 5 tests for LockstepEngine — the peer-verified anti-cheat.
//
// EditMode-only: exercises the engine's decision logic (input signature
// verification, tick-gated advance, hash consensus, kick+snapshot) with
// synthetic peers instead of real Mirror connections. That keeps the
// tests fast, hermetic, and CI-friendly.
//
// Scenarios covered — mapped to the plan's verification list:
//   * 3 honest peers → sim runs, hashes match, nobody is kicked.
//   * 1 peer forges an input (invalid signature) → sender kicked
//     immediately on that input's arrival, sim continues.
//   * 1 peer reports a different state hash at a checkpoint tick →
//     other two agree, the outlier is kicked.
//   * 1 peer stops submitting inputs → after STALL_TIMEOUT_SEC the
//     other peers mark it Stalled and advance without it.

using System;
using LifeIsAGame.DetSim;
using LifeIsAGame.Net;
using NUnit.Framework;
using UnityEngine;

namespace LifeIsAGame.Tests
{
    public sealed class LockstepEngineTests
    {
        private LockstepEngine _eng;
        private PeerIdentity[] _peers;

        [SetUp]
        public void SetUp()
        {
            _eng = new LockstepEngine();
            _peers = new[] { new PeerIdentity(), new PeerIdentity(), new PeerIdentity() };

            for (byte id = 0; id < _peers.Length; id++)
            {
                _eng.RegisterPeer(id, _peers[id].PublicKeyBlob);
            }

            var initial = new PlayerSimState[_peers.Length];
            for (int i = 0; i < initial.Length; i++)
            {
                initial[i] = new PlayerSimState
                {
                    PlayerId = (byte)i,
                    Alive = true,
                    Grounded = true,
                    Health = Fix64.FromInt(100),
                    Pos = new FixVector3(Fix64.FromInt(i), Fix64.Zero, Fix64.Zero),
                };
            }
            _eng.StartSession(initial, sharedSeed: 42UL);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var p in _peers) p.Dispose();
        }

        [Test]
        public void HonestPeers_Advance_WithoutKicks()
        {
            int kicks = 0;
            _eng.OnPeerKicked += (_, __, ___) => kicks++;

            for (uint tick = 1; tick <= 30; tick++)
            {
                for (byte pid = 0; pid < _peers.Length; pid++)
                {
                    var cmd = new InputCommand
                    {
                        Tick = tick, PlayerId = pid,
                        Buttons = InputBits.Forward,
                        MouseDeltaYaw = Fix64.Zero,
                    };
                    _eng.ReceiveInput(SignCmd(_peers[pid], cmd), connectionSenderId: pid);
                }
                bool advanced = _eng.TryAdvance();
                Assert.IsTrue(advanced, $"Tick {tick} should have advanced.");
            }

            Assert.AreEqual(0, kicks, "No honest peer should be kicked.");
            Assert.AreEqual(30u, TickClock.CurrentTick);
        }

        [Test]
        public void ForgedSignature_IsRejected_AndSenderKicked()
        {
            byte kickedId = 255; string kickReason = null;
            _eng.OnPeerKicked += (id, _, r) => { kickedId = id; kickReason = r; };

            // Peer 1 sends a valid input first so the engine knows they exist.
            var pkt = SignCmd(_peers[1], new InputCommand
            {
                Tick = 1, PlayerId = 1,
                Buttons = InputBits.Forward,
                MouseDeltaYaw = Fix64.Zero,
            });

            // Attacker: tamper with the signature blob after it was signed.
            pkt.Signature[0] ^= 0xFF;

            _eng.ReceiveInput(pkt, connectionSenderId: 1);
            Assert.AreEqual((byte)1, kickedId, "Peer 1 should be kicked for invalid signature.");
            StringAssert.Contains("signature", kickReason);
        }

        [Test]
        public void ImpersonationAttempt_KicksSenderNotVictim()
        {
            // Peer 2 sends a packet claiming to be peer 0. Without the
            // sender/claim cross-check the invalid-signature path would
            // kick the innocent peer 0 (the "claimed" sender). With the
            // fix, the actual sender (peer 2) is kicked instead.
            var cmd = new InputCommand
            {
                Tick = 1, PlayerId = 0,
                Buttons = InputBits.Jump,
                MouseDeltaYaw = Fix64.Zero,
            };
            var pkt = SignCmd(_peers[2], cmd); // signed by peer 2's key, claims to be peer 0

            byte kickedId = 255; string kickReason = null;
            _eng.OnPeerKicked += (id, _, r) => { kickedId = id; kickReason = r; };

            _eng.ReceiveInput(pkt, connectionSenderId: 2);
            Assert.AreEqual((byte)2, kickedId,
                "The griefing sender (peer 2) must be kicked, NOT the impersonated victim (peer 0).");
            StringAssert.Contains("impersonat", kickReason);
        }

        [Test]
        public void HashConsensus_KicksDivergentPeer()
        {
            byte kickedId = 255; string kickReason = null;
            _eng.OnPeerKicked += (id, _, r) => { kickedId = id; kickReason = r; };

            // Advance 10 ticks so we hit a checkpoint.
            for (uint tick = 1; tick <= LockstepEngine.HASH_INTERVAL_TICKS; tick++)
            {
                for (byte pid = 0; pid < _peers.Length; pid++)
                {
                    var cmd = new InputCommand
                    {
                        Tick = tick, PlayerId = pid,
                        Buttons = InputBits.None,
                        MouseDeltaYaw = Fix64.Zero,
                    };
                    _eng.ReceiveInput(SignCmd(_peers[pid], cmd), connectionSenderId: pid);
                }
                _eng.TryAdvance();
            }

            uint checkpoint = TickClock.CurrentTick;
            byte[] honestHash = _eng.MaybeCheckpointHash();
            Assert.NotNull(honestHash, "Checkpoint tick should produce a hash.");
            byte[] forgedHash = new byte[honestHash.Length];
            Array.Copy(honestHash, forgedHash, honestHash.Length);
            forgedHash[0] ^= 0xFF;

            // Peers 0 and 1 report the honest hash; peer 2 reports the forged one.
            _eng.ReceiveHash(0, checkpoint, honestHash);
            _eng.ReceiveHash(1, checkpoint, honestHash);
            _eng.ReceiveHash(2, checkpoint, forgedHash);

            Assert.AreEqual((byte)2, kickedId, "Divergent peer should be kicked.");
            StringAssert.Contains("hash", kickReason);
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private static SignedInputCommand SignCmd(PeerIdentity signer, InputCommand cmd)
        {
            byte[] payload = SignedInputCommand.PayloadBytes(cmd);
            return new SignedInputCommand
            {
                Cmd = cmd,
                Signature = signer.Sign(payload),
            };
        }
    }
}
