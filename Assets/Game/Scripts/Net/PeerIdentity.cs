// Per-session peer identity + input-signing key.
//
// Each peer generates a fresh ECDsa keypair on the P-256 curve at
// session start (P-256 is available in every .NET / Mono / IL2CPP
// runtime Unity supports — Ed25519 is not universally reachable from
// Unity 2022.3, so the plan spec's "Ed25519 with ECDsa fallback" lands
// as ECDsa outright here for portability).
//
// The public key is broadcast in the lobby so every peer can verify
// every other peer's InputCommand signatures. The private key never
// leaves the local machine and is discarded at StopHost/StopClient —
// it exists only for the duration of one lobby session.
//
// Security model summary:
//   * Prevents impersonation:      you can't ship an input from another
//                                  peer because you can't sign as them.
//   * Prevents mid-session tamper: every input carries a signature
//                                  over (tick, playerId, buttons,
//                                  mouseDeltaYaw); flipping any bit
//                                  invalidates the signature.
//   * Does NOT prevent collusion:  N/2+ colluding peers can agree on a
//                                  false state hash. Discussed in
//                                  docs/lockstep-anti-cheat.md.

using System;
using System.Security.Cryptography;

namespace LifeIsAGame.Net
{
    public sealed class PeerIdentity : IDisposable
    {
        private readonly ECDsa _key;

        /// <summary>DER-encoded SubjectPublicKeyInfo (SPKI) for this peer.</summary>
        public byte[] PublicKeyBlob { get; }

        public PeerIdentity()
        {
            _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            PublicKeyBlob = _key.ExportSubjectPublicKeyInfo();
        }

        /// <summary>Sign the given payload with this peer's private key. Returns a fresh signature blob.</summary>
        public byte[] Sign(byte[] payload)
        {
            return _key.SignData(payload, HashAlgorithmName.SHA256);
        }

        /// <summary>Verify a signature made by the peer whose public key is `publicKeyBlob`.</summary>
        public static bool Verify(byte[] publicKeyBlob, byte[] payload, byte[] signature)
        {
            using ECDsa peer = ECDsa.Create();
            try
            {
                peer.ImportSubjectPublicKeyInfo(publicKeyBlob, out _);
            }
            catch (CryptographicException)
            {
                return false;
            }
            return peer.VerifyData(payload, signature, HashAlgorithmName.SHA256);
        }

        public void Dispose() => _key?.Dispose();
    }
}
