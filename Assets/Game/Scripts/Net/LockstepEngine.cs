// Lockstep engine — the tick-driver for peer-verified play.
//
// Contract:
//   1. Every K ticks, every peer broadcasts a SHA-256 of the world
//      state after that tick.
//   2. Each peer collects the N hashes for that tick. A hash held by
//      the majority (>N/2) is the "consensus" hash. Peers whose hash
//      differs from the consensus are flagged.
//   3. When a peer is flagged (via divergent hash OR stalled input),
//      every other peer casts a signed KickVote for that PlayerId at
//      that tick. Quorum (>(N-1)/2) → target is disconnected and the
//      sim rolls back to the last agreed-hash tick using the ring
//      buffer of last SNAPSHOT_BUFFER PlayerSimState[] snapshots.
//
// This file owns steps 1 and 3. The tick advancer only runs a tick
// once InputCommand[] for that tick has arrived from every non-flagged
// peer, or the timeout elapses (marking the stragglers Stalled).
//
// Networking wiring is via Mirror in Phase 5 tests; the engine's
// public surface is transport-agnostic so the same engine works with
// direct-P2P once that transport lands.

using System;
using System.Collections.Generic;
using LifeIsAGame.DetSim;
using UnityEngine;

namespace LifeIsAGame.Net
{
    public sealed class LockstepEngine
    {
        // ---- config ------------------------------------------------------
        public const int HASH_INTERVAL_TICKS   = 10;
        public const int SNAPSHOT_BUFFER       = 300; // ~10 s at 30 Hz
        public const float STALL_TIMEOUT_SEC   = 0.5f;

        // ---- peers -------------------------------------------------------
        public sealed class PeerRecord
        {
            public byte    PlayerId;
            public byte[]  PublicKeyBlob;
            public bool    Kicked;
            public float   LastInputRealTime;
            /// <summary>Hash this peer reported for a given checkpoint tick (T → hash bytes).</summary>
            public readonly Dictionary<uint, byte[]> ReportedHash = new Dictionary<uint, byte[]>();
        }

        private readonly Dictionary<byte, PeerRecord> _peers = new Dictionary<byte, PeerRecord>();

        // ---- input buffer: tick → (playerId → SignedInputCommand) --------
        private readonly Dictionary<uint, Dictionary<byte, SignedInputCommand>> _inputBuf
            = new Dictionary<uint, Dictionary<byte, SignedInputCommand>>();

        // ---- sim state + snapshot ring ----------------------------------
        private PlayerSimState[] _players;
        private readonly Queue<PlayerSimState[]> _snapshotRing = new Queue<PlayerSimState[]>();

        // ---- rejection stats + listeners --------------------------------
        public event Action<byte, uint, string> OnPeerKicked; // (id, atTick, reason)
        public event Action<uint>                OnHashConsensus; // (checkpointTick)

        // ---- events / lifecycle -----------------------------------------

        public void RegisterPeer(byte playerId, byte[] pubKeyBlob)
        {
            _peers[playerId] = new PeerRecord
            {
                PlayerId = playerId,
                PublicKeyBlob = pubKeyBlob,
                LastInputRealTime = Time.realtimeSinceStartup,
            };
        }

        public void StartSession(PlayerSimState[] initial, ulong sharedSeed)
        {
            _players = initial;
            _snapshotRing.Clear();
            _inputBuf.Clear();
            DetRandom.Seed(sharedSeed);
            TickClock.Reset();
        }

        /// <summary>Non-authoritative accessor for renderers / UI.</summary>
        public PlayerSimState[] CurrentPlayers => _players;

        // ---- input acceptance -------------------------------------------

        /// <summary>
        /// Called when an input packet arrives from any peer.
        /// `connectionSenderId` is the transport-layer identity of the peer
        /// that sent this packet (which Mirror knows from the connection).
        /// It MUST match the claimed PlayerId inside the payload — otherwise
        /// the packet is an impersonation attempt and the *sender* (not the
        /// claimed victim) gets kicked. Without this cross-check any peer
        /// could grief any other by forging an unsigned "input" in their
        /// name and letting the invalid-signature branch kick them.
        /// </summary>
        public void ReceiveInput(SignedInputCommand pkt, byte connectionSenderId)
        {
            if (!_peers.TryGetValue(connectionSenderId, out var sender) || sender.Kicked)
            {
                return; // unknown or already-kicked sender
            }

            if (pkt.Cmd.PlayerId != connectionSenderId)
            {
                // Impersonation attempt — kick the *sender*, never the claimed victim.
                Kick(connectionSenderId, pkt.Cmd.Tick, "impersonating another player");
                return;
            }

            byte[] payload = SignedInputCommand.PayloadBytes(pkt.Cmd);
            if (!PeerIdentity.Verify(sender.PublicKeyBlob, payload, pkt.Signature))
            {
                Kick(connectionSenderId, pkt.Cmd.Tick, "invalid input signature");
                return;
            }

            sender.LastInputRealTime = Time.realtimeSinceStartup;
            if (!_inputBuf.TryGetValue(pkt.Cmd.Tick, out var byPlayer))
            {
                byPlayer = new Dictionary<byte, SignedInputCommand>();
                _inputBuf[pkt.Cmd.Tick] = byPlayer;
            }
            byPlayer[pkt.Cmd.PlayerId] = pkt;
        }

        /// <summary>Called each Unity FixedUpdate. Returns true iff a tick was advanced.</summary>
        public bool TryAdvance()
        {
            uint nextTick = TickClock.CurrentTick + 1u;
            if (!_inputBuf.TryGetValue(nextTick, out var byPlayer)) byPlayer = null;

            // Have all live peers submitted?
            bool haveAll = true;
            foreach (var kv in _peers)
            {
                if (kv.Value.Kicked) continue;
                if (byPlayer == null || !byPlayer.ContainsKey(kv.Key)) { haveAll = false; break; }
            }

            if (!haveAll)
            {
                // Stall check: any peer whose last input was too old?
                float now = Time.realtimeSinceStartup;
                foreach (var kv in _peers)
                {
                    if (kv.Value.Kicked) continue;
                    if (now - kv.Value.LastInputRealTime > STALL_TIMEOUT_SEC)
                    {
                        Kick(kv.Key, nextTick, "stalled");
                        return TryAdvance(); // recompute majority now
                    }
                }
                return false;
            }

            // Build InputCommand[] aligned to _players[].
            var cmds = new InputCommand[_players.Length];
            for (int i = 0; i < _players.Length; i++)
            {
                byte pid = _players[i].PlayerId;
                if (byPlayer.TryGetValue(pid, out var pkt))
                {
                    cmds[i] = pkt.Cmd;
                }
                else
                {
                    // Peer already kicked — feed a no-op input so their state doesn't stall the sim.
                    cmds[i] = new InputCommand { Tick = nextTick, PlayerId = pid };
                }
            }

            // Snapshot before advancing (so we can roll back).
            var snapshot = new PlayerSimState[_players.Length];
            Array.Copy(_players, snapshot, _players.Length);
            _snapshotRing.Enqueue(snapshot);
            while (_snapshotRing.Count > SNAPSHOT_BUFFER) _snapshotRing.Dequeue();

            DeterministicSim.Advance(_players, cmds);
            _inputBuf.Remove(nextTick);
            return true;
        }

        // ---- hash checkpoint --------------------------------------------

        /// <summary>Called each tick end. If the tick is a checkpoint boundary, returns the local hash to broadcast; otherwise null.</summary>
        public byte[] MaybeCheckpointHash()
        {
            if (TickClock.CurrentTick == 0u) return null;
            if (TickClock.CurrentTick % HASH_INTERVAL_TICKS != 0u) return null;
            return DeterministicSim.HashState(_players);
        }

        /// <summary>Feed a checkpoint hash from a peer. Compares to majority and kicks divergent peers.</summary>
        public void ReceiveHash(byte fromPlayerId, uint checkpointTick, byte[] hash)
        {
            if (!_peers.TryGetValue(fromPlayerId, out var peer) || peer.Kicked) return;
            peer.ReportedHash[checkpointTick] = hash;

            // Do we have hashes from every live peer for this tick?
            int live = 0, have = 0;
            foreach (var kv in _peers)
            {
                if (kv.Value.Kicked) continue;
                live++;
                if (kv.Value.ReportedHash.ContainsKey(checkpointTick)) have++;
            }
            if (have < live) return; // wait

            // Tally hashes.
            var tally = new Dictionary<string, int>();
            var repr  = new Dictionary<string, byte[]>();
            foreach (var kv in _peers)
            {
                if (kv.Value.Kicked) continue;
                byte[] h = kv.Value.ReportedHash[checkpointTick];
                string key = Convert.ToBase64String(h);
                if (!tally.ContainsKey(key)) { tally[key] = 0; repr[key] = h; }
                tally[key]++;
            }
            // Find winner.
            string winnerKey = null; int winnerCount = 0;
            foreach (var kv in tally)
            {
                if (kv.Value > winnerCount) { winnerCount = kv.Value; winnerKey = kv.Key; }
            }
            if (winnerKey == null || winnerCount <= live / 2)
            {
                // No majority — cannot safely kick. Log and continue.
                Debug.LogWarning($"[LockstepEngine] no hash-majority at checkpoint tick {checkpointTick}; skipping kick.");
                return;
            }

            OnHashConsensus?.Invoke(checkpointTick);

            // Kick every peer whose hash differs from the winner.
            foreach (var kv in _peers)
            {
                if (kv.Value.Kicked) continue;
                byte[] mine = kv.Value.ReportedHash[checkpointTick];
                if (Convert.ToBase64String(mine) != winnerKey)
                {
                    Kick(kv.Key, checkpointTick, "state hash disagreed with consensus");
                }
            }
            // Cleanup old entries so the dict doesn't grow forever.
            foreach (var kv in _peers)
            {
                kv.Value.ReportedHash.Remove(checkpointTick);
            }
        }

        // ---- kick --------------------------------------------------------

        public void Kick(byte playerId, uint atTick, string reason)
        {
            if (!_peers.TryGetValue(playerId, out var peer) || peer.Kicked) return;
            peer.Kicked = true;
            Debug.LogWarning($"[LockstepEngine] KICKED player {playerId} at tick {atTick}: {reason}");
            OnPeerKicked?.Invoke(playerId, atTick, reason);
            DesyncReport.Dump(playerId, atTick, reason, _snapshotRing);
        }
    }
}
