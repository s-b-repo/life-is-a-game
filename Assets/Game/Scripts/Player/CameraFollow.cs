// Camera-follows-player.
//
// Was `MoveCamera.cs` in the pre-Phase-1 tree. Fixes from that version:
//   * Null-guard `player` in Awake and self-disable if unassigned instead
//     of throwing NullReferenceException every frame.
//   * Copy in LateUpdate, not Update. The player rigidbody moves in
//     FixedUpdate; following in Update reads the pre-physics position and
//     causes the camera to jitter by one frame behind the character. In
//     LateUpdate we always follow after physics + rendering-source
//     transforms have settled.
//   * Serialized position offset so the camera can sit at the player's
//     head instead of dead-center.
//   * `player.position` (Transform already exposed) instead of the
//     round-trip `player.transform.position`.

using UnityEngine;

namespace LifeIsAGame.Player
{
    [DisallowMultipleComponent]
    public sealed class CameraFollow : MonoBehaviour
    {
        [Tooltip("Transform the camera should follow. Usually the head bone or a socket on the player prefab.")]
        [SerializeField] private Transform player;

        [Tooltip("Offset in world space added to the target's position each frame. " +
                 "Use (0, 1.6, 0) for a first-person head height on a 2m capsule.")]
        [SerializeField] private Vector3 offset = Vector3.zero;

        private void Awake()
        {
            if (player == null)
            {
                Debug.LogError(
                    $"{nameof(CameraFollow)} on '{name}' has no `player` assigned. " +
                    "Disabling component.", this);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            transform.position = player.position + offset;
        }
    }
}
