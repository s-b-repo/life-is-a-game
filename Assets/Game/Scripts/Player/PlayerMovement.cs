// Rigidbody FPS movement controller.
//
// Descended from the "stupid rigidbody movement by Dani" script that
// shipped with the original project. This Phase 1 rewrite fixes the
// bugs surfaced during code review while preserving the same look and
// feel (WASD + mouse, sprint, jump, crouch/slide, ground detection).
//
// Bugs fixed in this pass:
//   * Cursor.visible was `true` in an FPS scene — flipped to false.
//   * Time.deltaTime was used inside FixedUpdate; forces are now scaled
//     by Time.fixedDeltaTime.
//   * Jump used Vector2.up in a 3D scene — implicit-converted to (x,y,0)
//     losing intent; now Vector3.up.
//   * Mouse look was multiplied by Time.fixedDeltaTime which collapsed
//     sensitivity and tied it to the physics rate — removed.
//   * Crouch / uncrouch teleported ±0.5y with no clearance check —
//     added a Physics.CheckSphere guard so it won't clip through
//     ceilings or the floor.
//   * OnCollisionStay was Invoke'ing StopGrounded at Time.deltaTime * 3
//     (~50 ms), so `grounded` flickered every step. Replaced with a
//     proper ground-cooldown timer decremented in FixedUpdate.
//   * The CounterMovement operator-precedence expression had ambiguous
//     `&&`/`||` mixing — wrapped in explicit parentheses.
//   * OnDisable now cancels pending Invokes so a disabled → re-enabled
//     GameObject does not fire ResetJump or StopGrounded at the wrong
//     moment.
//   * playerCam / orientation are null-guarded in Awake; if unassigned
//     the component logs and self-disables instead of throwing every
//     frame.
//   * Public tunables became [SerializeField] private; internal state
//     that is externally observable (grounded) is now a read-only property.

using UnityEngine;

namespace LifeIsAGame.Player
{
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class PlayerMovement : MonoBehaviour
    {
        // ----- Wiring (assign in Inspector) --------------------------------
        [Header("References")]
        [SerializeField] private Transform playerCam;
        [SerializeField] private Transform orientation;

        // ----- Look --------------------------------------------------------
        [Header("Look")]
        [SerializeField] private float sensitivity = 50f;
        [SerializeField] private float sensMultiplier = 1f;

        // ----- Movement ----------------------------------------------------
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 4500f;
        [SerializeField] private float maxSpeed = 20f;
        [SerializeField] private LayerMask whatIsGround = ~0;
        [SerializeField] private float counterMovement = 0.175f;
        [SerializeField] private float threshold = 0.01f;
        [SerializeField] private float maxSlopeAngle = 35f;

        // ----- Crouch & Slide ---------------------------------------------
        [Header("Crouch / Slide")]
        [SerializeField] private Vector3 crouchScale = new Vector3(1f, 0.5f, 1f);
        [SerializeField] private float slideForce = 400f;
        [SerializeField] private float slideCounterMovement = 0.2f;
        [SerializeField] private float crouchOffsetY = 0.5f;
        // Radius used by CheckSphere when we consider standing up.
        [SerializeField] private float crouchClearanceRadius = 0.45f;

        // ----- Jump --------------------------------------------------------
        [Header("Jump")]
        [SerializeField] private float jumpForce = 550f;
        [SerializeField] private float jumpCooldown = 0.25f;
        // How long after leaving the ground we still count as grounded.
        [SerializeField] private float coyoteTime = 0.1f;

        // ----- Public read-only state -------------------------------------
        public bool Grounded { get; private set; }

        // ----- Private state ----------------------------------------------
        private Rigidbody rb;
        private Vector3 playerScale;
        private Vector3 normalVector = Vector3.up;
        private float xRotation;
        private float desiredX;
        private bool readyToJump = true;
        private float groundedCooldown;   // ticks down in FixedUpdate; ground state clears at 0

        // Input snapshot
        private float x, y;
        private bool jumping, crouching;

        // ----- Unity lifecycle --------------------------------------------

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            playerScale = transform.localScale;

            if (playerCam == null || orientation == null)
            {
                Debug.LogError(
                    $"{nameof(PlayerMovement)} on '{name}' needs both playerCam and " +
                    "orientation assigned in the Inspector. Disabling component.", this);
                enabled = false;
            }
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;   // BUG FIX: was `true`.
        }

        private void OnDisable()
        {
            // Kill any pending Invokes; on re-enable, callers can re-arm cleanly.
            CancelInvoke(nameof(ResetJump));
        }

        private void FixedUpdate()
        {
            Movement();

            // Ground-cooldown timer replaces the old Invoke-based scheme
            // that fired ~50 ms out and had `grounded` flickering. We only
            // decrement while Grounded is true; OnCollisionStay re-arms the
            // timer to coyoteTime on every floor-normal contact, so the
            // timer only reaches zero when the player has actually left
            // the ground for `coyoteTime` seconds.
            if (Grounded)
            {
                groundedCooldown -= Time.fixedDeltaTime;
                if (groundedCooldown <= 0f)
                {
                    Grounded = false;
                }
            }
        }

        private void Update()
        {
            ReadInput();
            Look();
        }

        // ----- Input -------------------------------------------------------

        private void ReadInput()
        {
            x = Input.GetAxisRaw("Horizontal");
            y = Input.GetAxisRaw("Vertical");
            jumping = Input.GetButton("Jump");
            crouching = Input.GetKey(KeyCode.LeftControl);

            if (Input.GetKeyDown(KeyCode.LeftControl)) StartCrouch();
            if (Input.GetKeyUp(KeyCode.LeftControl)) StopCrouch();
        }

        // ----- Look --------------------------------------------------------

        private void Look()
        {
            // BUG FIX: The original code multiplied by Time.fixedDeltaTime,
            // which turned an already frame-independent mouse delta into a
            // physics-rate-dependent value. Removed.
            float mouseX = Input.GetAxis("Mouse X") * sensitivity * sensMultiplier * 0.02f;
            float mouseY = Input.GetAxis("Mouse Y") * sensitivity * sensMultiplier * 0.02f;

            Vector3 rot = playerCam.localRotation.eulerAngles;
            desiredX = rot.y + mouseX;

            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -90f, 90f);

            playerCam.localRotation = Quaternion.Euler(xRotation, desiredX, 0f);
            orientation.localRotation = Quaternion.Euler(0f, desiredX, 0f);
        }

        // ----- Crouch / uncrouch ------------------------------------------

        private void StartCrouch()
        {
            transform.localScale = crouchScale;
            // Only nudge downward if there is room beneath us; on a very
            // thin platform, going down 0.5m could put us inside the floor.
            if (!Physics.CheckSphere(
                    transform.position + Vector3.down * crouchOffsetY,
                    crouchClearanceRadius, whatIsGround, QueryTriggerInteraction.Ignore))
            {
                transform.position = new Vector3(
                    transform.position.x,
                    transform.position.y - crouchOffsetY,
                    transform.position.z);
            }

            if (rb.velocity.magnitude > 0.5f && Grounded)
            {
                rb.AddForce(orientation.forward * slideForce);
            }
        }

        private void StopCrouch()
        {
            // BUG FIX: guard the +0.5y teleport with a clearance check so
            // we don't clip through a low ceiling.
            if (!Physics.CheckSphere(
                    transform.position + Vector3.up * crouchOffsetY,
                    crouchClearanceRadius, whatIsGround, QueryTriggerInteraction.Ignore))
            {
                transform.position = new Vector3(
                    transform.position.x,
                    transform.position.y + crouchOffsetY,
                    transform.position.z);
            }
            transform.localScale = playerScale;
        }

        // ----- Movement ----------------------------------------------------

        private void Movement()
        {
            // Extra gravity. BUG FIX: was Time.deltaTime inside FixedUpdate.
            rb.AddForce(Vector3.down * (Time.fixedDeltaTime * 10f));

            Vector2 mag = FindVelRelativeToLook();
            float xMag = mag.x, yMag = mag.y;

            CounterMovement(x, y, mag);

            if (readyToJump && jumping) Jump();

            float capSpeed = maxSpeed;

            // Sliding-down-ramp force: keep player grounded while building speed.
            if (crouching && Grounded && readyToJump)
            {
                rb.AddForce(Vector3.down * (Time.fixedDeltaTime * 3000f));
                return;
            }

            // Cancel input direction once at the cap so we don't accumulate past it.
            if (x > 0f && xMag > capSpeed) x = 0f;
            if (x < 0f && xMag < -capSpeed) x = 0f;
            if (y > 0f && yMag > capSpeed) y = 0f;
            if (y < 0f && yMag < -capSpeed) y = 0f;

            float multiplier = 1f, multiplierV = 1f;
            if (!Grounded) { multiplier = 0.5f; multiplierV = 0.5f; }
            if (Grounded && crouching) multiplierV = 0f;

            rb.AddForce(orientation.forward * (y * moveSpeed * Time.fixedDeltaTime * multiplier * multiplierV));
            rb.AddForce(orientation.right   * (x * moveSpeed * Time.fixedDeltaTime * multiplier));
        }

        private void Jump()
        {
            if (!Grounded || !readyToJump) return;

            readyToJump = false;

            // BUG FIX: was Vector2.up (implicit-converted to (0,1,0) but
            // reveals intent slip); now explicit Vector3.up.
            rb.AddForce(Vector3.up * (jumpForce * 1.5f));
            rb.AddForce(normalVector * (jumpForce * 0.5f));

            // If jumping while falling, reset y velocity; if rising, halve it.
            Vector3 vel = rb.velocity;
            if (vel.y < 0.5f)      rb.velocity = new Vector3(vel.x, 0f, vel.z);
            else if (vel.y > 0f)   rb.velocity = new Vector3(vel.x, vel.y * 0.5f, vel.z);

            Invoke(nameof(ResetJump), jumpCooldown);
        }

        private void ResetJump() => readyToJump = true;

        // ----- Counter-movement -------------------------------------------

        private void CounterMovement(float x, float y, Vector2 mag)
        {
            if (!Grounded || jumping) return;

            if (crouching)
            {
                rb.AddForce(moveSpeed * Time.fixedDeltaTime * -rb.velocity.normalized * slideCounterMovement);
                return;
            }

            // BUG FIX: original mixed `&&`/`||` without parens. C# precedence
            // groups `&&` before `||` so the intended
            //   (moving fast AND no input)  OR  (moving one way AND input the other)
            // just happened to work. Made explicit here.
            if ((Mathf.Abs(mag.x) > threshold && Mathf.Abs(x) < 0.05f) ||
                (mag.x < -threshold && x > 0f) ||
                (mag.x >  threshold && x < 0f))
            {
                rb.AddForce(moveSpeed * orientation.right * Time.fixedDeltaTime * -mag.x * counterMovement);
            }
            if ((Mathf.Abs(mag.y) > threshold && Mathf.Abs(y) < 0.05f) ||
                (mag.y < -threshold && y > 0f) ||
                (mag.y >  threshold && y < 0f))
            {
                rb.AddForce(moveSpeed * orientation.forward * Time.fixedDeltaTime * -mag.y * counterMovement);
            }

            // Hard cap: if planar speed exceeds max, clamp preserving y (fall speed).
            float planar = Mathf.Sqrt(rb.velocity.x * rb.velocity.x + rb.velocity.z * rb.velocity.z);
            if (planar > maxSpeed)
            {
                float fallY = rb.velocity.y;
                Vector3 n = rb.velocity.normalized * maxSpeed;
                rb.velocity = new Vector3(n.x, fallY, n.z);
            }
        }

        /// <summary>
        /// Velocity in the frame of the player's facing direction —
        /// x = right/left component, y = forward/back component.
        /// </summary>
        private Vector2 FindVelRelativeToLook()
        {
            float lookAngle = orientation.eulerAngles.y;
            float moveAngle = Mathf.Atan2(rb.velocity.x, rb.velocity.z) * Mathf.Rad2Deg;

            float u = Mathf.DeltaAngle(lookAngle, moveAngle);
            float v = 90f - u;

            float speed = rb.velocity.magnitude;
            return new Vector2(
                speed * Mathf.Cos(v * Mathf.Deg2Rad),
                speed * Mathf.Cos(u * Mathf.Deg2Rad));
        }

        // ----- Ground detection -------------------------------------------

        private bool IsFloor(Vector3 v) => Vector3.Angle(Vector3.up, v) < maxSlopeAngle;

        private void OnCollisionStay(Collision other)
        {
            int layer = other.gameObject.layer;
            if ((whatIsGround.value & (1 << layer)) == 0) return;

            for (int i = 0; i < other.contactCount; i++)
            {
                Vector3 n = other.contacts[i].normal;
                if (IsFloor(n))
                {
                    Grounded = true;
                    normalVector = n;
                    groundedCooldown = coyoteTime;
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (crouchClearanceRadius <= 0f) crouchClearanceRadius = 0.01f;
            if (jumpCooldown < 0f)           jumpCooldown = 0f;
            if (coyoteTime < 0f)             coyoteTime = 0f;
        }
#endif
    }
}
