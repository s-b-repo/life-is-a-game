// Phase 1 regression tests for PlayerMovement.
//
// These are EditMode tests: they run in the Editor without entering Play
// mode, using UnityEngine primitives that work in EditMode (creating a
// GameObject, adding a Rigidbody, calling private methods via reflection).
// The scope is narrow — cover the bug-fixes the Phase 1 rewrite made, not
// the full physics model (that lives in PlayMode tests once we have them).

using System.Reflection;
using LifeIsAGame.Player;
using NUnit.Framework;
using UnityEngine;

namespace LifeIsAGame.Tests
{
    public sealed class PlayerMovementTests
    {
        private GameObject _root;
        private PlayerMovement _pm;
        private Rigidbody _rb;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("PMHost");
            _rb = _root.AddComponent<Rigidbody>();

            // playerCam + orientation must be assigned or PlayerMovement
            // self-disables in Awake(). Use two child transforms.
            var cam = new GameObject("cam").transform;   cam.SetParent(_root.transform);
            var ori = new GameObject("orient").transform; ori.SetParent(_root.transform);

            _pm = _root.AddComponent<PlayerMovement>();
            SetPrivate(_pm, "playerCam", cam);
            SetPrivate(_pm, "orientation", ori);

            // Awake ran during AddComponent — the null-guard already fired
            // before we set the fields. Re-enable manually for the test.
            _pm.enabled = true;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void GroundedIsFalse_OnFreshComponent()
        {
            Assert.IsFalse(_pm.Grounded, "A player just spawned in air should not be grounded.");
        }

        [Test]
        public void ResetJump_MarksReadyToJump()
        {
            // Force readyToJump = false, then invoke ResetJump manually.
            SetPrivate(_pm, "readyToJump", false);
            InvokePrivate(_pm, "ResetJump");
            Assert.IsTrue(GetPrivate<bool>(_pm, "readyToJump"),
                "ResetJump must set readyToJump back to true.");
        }

        [Test]
        public void GroundedCooldown_ClearsGrounded_AfterElapsed()
        {
            // Simulate: peer touches ground, coyoteTime starts counting.
            SetPrivate(_pm, "groundedCooldown", 0.05f);
            SetPropertyBacking(_pm, "<Grounded>k__BackingField", true);
            Assert.IsTrue(_pm.Grounded, "Precondition — Grounded should be true.");

            // Advance the cooldown past zero. Doing this by direct field
            // manipulation instead of waiting real time.
            SetPrivate(_pm, "groundedCooldown", -0.001f);

            // FixedUpdate does the decrement + clear; call it directly.
            InvokePrivate(_pm, "FixedUpdate");
            Assert.IsFalse(_pm.Grounded,
                "When the ground cooldown expires, Grounded should clear.");
        }

        [Test]
        public void CursorState_OnStart_IsLockedAndInvisible()
        {
            InvokePrivate(_pm, "Start");
            Assert.AreEqual(CursorLockMode.Locked, Cursor.lockState,
                "FPS scene must lock the cursor.");
            Assert.IsFalse(Cursor.visible,
                "FPS scene must hide the cursor (Phase 1 bug-fix — was true).");
        }

        // ----- reflection helpers -----

        private static void SetPrivate(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(f, $"Field '{field}' not found on {target.GetType().Name}.");
            f.SetValue(target, value);
        }

        private static T GetPrivate<T>(object target, string field)
        {
            var f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(f, $"Field '{field}' not found on {target.GetType().Name}.");
            return (T)f.GetValue(target);
        }

        private static void InvokePrivate(object target, string method)
        {
            var m = target.GetType().GetMethod(method,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(m, $"Method '{method}' not found on {target.GetType().Name}.");
            m.Invoke(target, null);
        }

        // Auto-property `Grounded { get; private set; }` compiles to a
        // <Grounded>k__BackingField. Test writes it directly to simulate
        // ground-contact without needing a real Physics collision setup.
        private static void SetPropertyBacking(object target, string backingName, object value)
        {
            var f = target.GetType().GetField(backingName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(f, $"Backing field '{backingName}' not found — property may not be an auto-property.");
            f.SetValue(target, value);
        }
    }
}
