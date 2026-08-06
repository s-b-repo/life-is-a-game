// Random-spawn-offset component.
//
// Replaces the two pre-Phase-1 duplicates `random spawn.cs` (class
// `randomspawn`, ±4 XY offset) and `SPAWNRANDOM.cs` (class `SPAWNRANDOM`,
// 0..1 XY offset). Both hard-coded their axes and offset ranges; this
// version exposes min/max offsets on all three axes.
//
// Notes on determinism:
//   In Phase 1 this uses UnityEngine.Random through the DetRandom shim
//   under Assets/Game/Scripts/DetSim/. Phase 4 replaces the shim with a
//   seeded xorshift RNG shared across peers so lockstep spawns match.
//   Nothing in this file needs to change at that point — the shim is
//   the seam.

using UnityEngine;

namespace LifeIsAGame.Spawn
{
    [DisallowMultipleComponent]
    public sealed class RandomSpawn : MonoBehaviour
    {
        [Tooltip("Minimum per-axis offset added to this transform's " +
                 "position at Start.")]
        [SerializeField] private Vector3 minOffset = new Vector3(-4f, 0f, -4f);

        [Tooltip("Maximum per-axis offset added to this transform's " +
                 "position at Start.")]
        [SerializeField] private Vector3 maxOffset = new Vector3( 4f, 0f,  4f);

        [Tooltip("Apply the offset once on Start(). If false you can call " +
                 "Apply() manually — useful when the spawn seed isn't " +
                 "available yet at Start().")]
        [SerializeField] private bool applyOnStart = true;

        private void Start()
        {
            if (applyOnStart) Apply();
        }

        /// <summary>Apply a random per-axis offset within [min, max].</summary>
        public void Apply()
        {
            Vector3 delta = new Vector3(
                DetSim.DetRandom.Range(minOffset.x, maxOffset.x),
                DetSim.DetRandom.Range(minOffset.y, maxOffset.y),
                DetSim.DetRandom.Range(minOffset.z, maxOffset.z));
            transform.position += delta;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Keep max ≥ min per component so the Range() calls never
            // silently swap the order downstream.
            if (maxOffset.x < minOffset.x) maxOffset.x = minOffset.x;
            if (maxOffset.y < minOffset.y) maxOffset.y = minOffset.y;
            if (maxOffset.z < minOffset.z) maxOffset.z = minOffset.z;
        }
#endif
    }
}
