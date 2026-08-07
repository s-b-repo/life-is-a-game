# Lockstep Peer-Verified Anti-Cheat — Protocol Spec

This document defines the anti-cheat contract Phase 5 implements. It is the
authoritative reference for what the engine defends against, what it does not
defend against, and how the pieces fit together.

## Threat model

**In scope**

- **Solo state tampering.** One peer runs a modified client that lies about
  their position, ammunition, health, or hit registration.
- **Solo input forgery.** One peer sends an input packet claiming to be
  another peer (e.g., "player 2 pressed jump").
- **Impossible moves.** One peer performs a state transition the deterministic
  sim would never permit (teleport, superhuman speed, negative HP).
- **Stalling to grief.** One peer stops sending inputs to freeze the
  session.

**Out of scope**

- **Colluding majority.** If ≥ ⌈N / 2⌉ peers agree on a lie, the majority-vote
  kick mechanism cannot help — those peers are the majority. Competitive play
  should therefore run in true P2P mode, not host-server mode, so at minimum a
  third party (or dedicated referee peer) is present.
- **Host trust.** In Mirror's host+client mode the host has extra powers: they
  authorize connections and can refuse RPCs. That's a Mirror architecture
  reality, not a lockstep flaw. For competitive play use dedicated servers or
  a true peer mesh.
- **Client-side visual cheats** (wallhack, ESP). These don't change simulation
  state and thus don't produce a state-hash divergence. Detecting them needs a
  separate visual/scan-based approach that is out of scope for the sim
  contract here.

## Actors

- **Peer** — one participant in the session (host or client). Every peer runs
  the same `DeterministicSim` and holds a private key generated at session
  start.
- **PlayerId** — a byte assigned by `GameNetworkManager` at connect time.
  Stable for the session.
- **PublicKey blob** — DER-encoded SubjectPublicKeyInfo for an ECDsa P-256
  key. Every peer publishes its public key in the lobby; peers exchange
  full pubkey lists at StartGame.

## Wire messages

Three message types drive the anti-cheat:

- `SignedInputCommand`
  - `InputCommand Cmd = { uint Tick; byte PlayerId; InputBits Buttons; Fix64 MouseDeltaYaw; }`
  - `byte[] Signature` — ECDsa/SHA-256 over the byte-stable payload
    (`SignedInputCommand.PayloadBytes`).
  - Broadcast every tick by every peer.

- `HashCheckpoint`
  - `uint CheckpointTick`
  - `byte[] SHA256Hash` — SHA-256 of the deterministic sim's world state
    (all players sorted by PlayerId, plus tick).
  - Broadcast once every `HASH_INTERVAL_TICKS = 10` ticks.

- `KickVote` (implicit — every peer independently runs the same kick logic
  and independently drops the target; no explicit vote message is on the
  wire in this version).

## Invariants

1. **Determinism.** Two peers with identical `InputCommand[Tick]` for every
   tick and the same `sharedSeed` produce byte-identical `PlayerSimState[]`
   after every tick. This is enforced by `Fix64` (BigInteger 128-bit multiply
   / divide + Newton sqrt), `FixVector3`, `DetRandom` (xorshift128+ from
   SplitMix64-seeded state), and `TickClock` (integer tick counter, fixed
   1/30 s). Verified by `DetSimTests`.

2. **Signature validity.** Every `SignedInputCommand` reaching the sim has
   a valid ECDsa signature over its `PayloadBytes()` under the claimed
   sender's public key. Verified in `LockstepEngine.ReceiveInput`.

   The engine also cross-checks the **transport-layer sender** (the
   connection id Mirror knows the packet arrived on) against the
   `PlayerId` inside the payload. If they disagree it is an impersonation
   attempt and the *sender* is kicked, never the impersonated victim.
   Without this check any peer could grief any other by broadcasting an
   invalidly-signed packet claiming to be that peer, letting the
   invalid-signature branch kick the innocent target.

3. **Tick alignment.** The sim only advances a tick when either
   (a) every non-kicked peer has submitted a valid `SignedInputCommand` for
   that tick, or (b) `STALL_TIMEOUT_SEC = 0.5 s` has elapsed and the
   stragglers have been marked stalled and kicked.

4. **Hash consensus.** Every `HASH_INTERVAL_TICKS = 10` ticks, each peer
   computes and broadcasts a hash. Peers whose hash differs from the
   majority hash at that checkpoint are kicked.

5. **Rollback bound.** On any kick, the sim rolls back to the last consensus
   checkpoint using the `SNAPSHOT_BUFFER = 300` ring buffer (~10 s at 30 Hz).
   No further rollback is possible past that horizon.

## Kick decisions

A peer is kicked on any of:

- **Invalid signature** on an incoming `SignedInputCommand`.
- **Stalled**: `Time.realtimeSinceStartup - LastInputRealTime > STALL_TIMEOUT_SEC`.
- **Hash divergent**: reported hash at a checkpoint disagrees with the
  majority.

Kicks are decided **independently and identically** on every peer because
every peer runs the same deterministic logic over the same signed evidence.
So there is no "vote message" — everyone converges on the same decision.

## Session lifecycle

1. **Lobby** — peers connect, exchange public keys, agree on a `sharedSeed`
   (host-generated ulong broadcast at `StartGameplay`).
2. **StartGame** — every peer resets `TickClock`, seeds `DetRandom`,
   materialises the initial `PlayerSimState[]` (positions from spawn
   points, health = 100, alive = true).
3. **Ticks** — the loop of receive-inputs → advance sim → maybe-checkpoint.
4. **EndGame** — reason: last man standing / host quits / everyone kicked.
   `PeerIdentity` is disposed on session end; the next session generates
   a fresh keypair.

## Post-mortem — DesyncReport

On any kick, `DesyncReport.Dump` writes the last ~10 s of PlayerSimState
snapshots to `Application.persistentDataPath/desync/`. Non-blocking; if
IO fails the kick still proceeds. Use these files to diagnose whether a
kick was a real cheat, a legitimate desync bug in the sim, or a code bug
in the anti-cheat itself.

## Known limitations

- **No horizontal scaling of trust.** Every peer's `PeerRecord` is held
  locally and consulted independently; there is no third-party attestation
  of who is present. If a peer drops off before their public key propagates,
  everyone else disagrees on the peer set. Lobby handshake must complete
  before StartGame.
- **Checkpoint blast radius = HASH_INTERVAL_TICKS ticks.** A cheating peer
  who diverges JUST after a checkpoint has 9 ticks of lie before the next
  hash catches them. Lower `HASH_INTERVAL_TICKS` to trade catch-latency
  for bandwidth.
- **No detection of consistent-across-peers cheating.** If a peer modifies
  their local rendering (e.g., paints walls transparent) but doesn't
  change simulated state, the sim hash still matches — no kick.
- **P-256 not Ed25519.** The plan spec calls for Ed25519 with an ECDsa
  fallback. Unity 2022.3's runtime doesn't reliably expose Ed25519 across
  every scripting backend (Mono / IL2CPP), so we use ECDsa/P-256
  outright. Security level is comparable (~128-bit); the assets/API
  surface is identical modulo the curve name.

## Files

| File | Role |
| --- | --- |
| `Net/PeerIdentity.cs` | Per-session ECDsa/P-256 keypair; Sign / Verify. |
| `Net/SignedInputCommand.cs` | Wire struct; byte-stable serialiser. |
| `Net/LockstepEngine.cs` | Peer registry, input buffer, tick gate, hash consensus, kick. |
| `Net/DesyncReport.cs` | Local snapshot dump on kick. |
| `DetSim/DeterministicSim.cs` | Pure sim advance + state hash. |
| `DetSim/Fix64.cs` etc. | Deterministic math foundation. |
| `Tests/EditMode/DetSimTests.cs` | Determinism invariants (Phase 4). |
| `Tests/EditMode/LockstepEngineTests.cs` | Kick decisions (Phase 5). |

## Extending

- **New sim state (weapons, ammo, projectiles):** add fields to
  `PlayerSimState`, extend `SerializeTo` and `SERIALIZED_BYTES`, teach
  `DeterministicSim.AdvanceOne` how to update them. The hash automatically
  covers the new fields; no engine changes needed.
- **New input bits:** extend `InputBits` enum (up to 16 bits). Because
  `PayloadBytes` reads two bytes of buttons, no wire-format change needed
  as long as you stay ≤ 16 bits.
- **New peer:** register mid-session via `LockstepEngine.RegisterPeer`
  — but you must also feed the fresh peer an `InputCommand` for every
  tick from their join-point onward or you'll stall the sim.
