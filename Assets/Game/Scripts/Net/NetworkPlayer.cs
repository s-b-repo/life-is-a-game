// Networked wrapper around PlayerMovement.
//
// A single Player prefab in the scene serves both host and client; this
// component decides whether the local input should drive movement or
// whether the transform is being interpolated from a remote peer.
//
// Phase 2 (this file today):
//   * Client-authoritative: each client reads input, runs PlayerMovement
//     locally, and Mirror's NetworkTransform ships the resulting
//     position/rotation to everyone else. Simple and fast; NOT
//     cheat-resistant (a peer can lie about its position).
//
// Phase 4 replaces the transform-sync path with the DetSim tick loop
// (each peer runs the same deterministic sim and only ships InputCommand,
// not state). Phase 5 adds the peer-verification hash consensus. When
// that happens, this file will change to feed inputs into LockstepEngine
// instead of writing to PlayerMovement directly.

using Mirror;
using UnityEngine;
using LifeIsAGame.Player;

namespace LifeIsAGame.Net
{
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(PlayerMovement))]
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        [Tooltip("Optional display name shown in the lobby and chat. " +
                 "Falls back to `Player <slot>` if empty.")]
        [SyncVar(hook = nameof(OnNameChanged))]
        public string DisplayName = string.Empty;

        [Tooltip("Server-assigned stable id used by LockstepEngine (Phase 5) " +
                 "and RoomListPanel. Zero until the server has assigned one.")]
        [SyncVar]
        public byte PlayerIdSlot;

        private PlayerMovement _movement;

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
        }

        public override void OnStartLocalPlayer()
        {
            // Only the local player reads WASD and moves the cursor. Remote
            // copies of this prefab keep PlayerMovement disabled so their
            // transforms are driven entirely by NetworkTransform.
            _movement.enabled = true;

            // Camera setup: attach the main camera to the local player. In
            // Phase 3 the Lobby scene has no camera on this prefab; the
            // gameplay scene's main camera parents to this on spawn.
            Camera main = Camera.main;
            if (main != null && main.transform.parent == null)
            {
                main.transform.SetParent(transform, worldPositionStays: false);
                main.transform.localPosition = new Vector3(0f, 1.6f, 0f);
                main.transform.localRotation = Quaternion.identity;
            }
        }

        public override void OnStartClient()
        {
            // Remote players don't run local input.
            if (!isLocalPlayer)
            {
                _movement.enabled = false;
            }
        }

        // -----------------------------------------------------------------
        // Client -> server -> everyone: change my display name.
        // -----------------------------------------------------------------

        [Command]
        public void CmdSetDisplayName(string name)
        {
            // Server-side sanitize.
            if (string.IsNullOrWhiteSpace(name)) return;
            string trimmed = name.Trim();
            if (trimmed.Length > 24) trimmed = trimmed.Substring(0, 24);
            DisplayName = trimmed;
        }

        private void OnNameChanged(string oldName, string newName)
        {
            // Phase 3 Lobby UI listens for this via a static event to
            // refresh its player-slot list; hook plumbed in Phase 3.
            gameObject.name = $"Player [{PlayerIdSlot}] {newName}";
        }
    }
}
