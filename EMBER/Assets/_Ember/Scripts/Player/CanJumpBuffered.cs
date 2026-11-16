using System;
using UnityEngine;

namespace Ember
{
    /// <summary>
    /// Improved version of the class script CanJump (Session 3).
    /// CanJump reads Input.GetButtonDown inside FixedUpdate, so a press made on a frame with no
    /// physics step is lost, and any contact (even a wall) counts as ground. This version:
    ///  * remembers the press in Update and consumes it in FixedUpdate (input buffer),
    ///  * only counts contacts whose normal points up as ground,
    ///  * allows a short "coyote time" after rolling off a ledge,
    ///  * sets the vertical speed directly, so the jump height does not depend on the timestep.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CanJumpBuffered : MonoBehaviour
    {
        [Tooltip("Upward speed in m/s. 6.3 m/s gives a jump about 2 m high.")]
        public float jumpVelocity = 6.3f;
        public float inputBuffer = 0.15f;
        public float coyoteTime = 0.1f;
        [Range(0f, 1f)] public float groundNormalMin = 0.55f;

        public event Action Jumped;
        public bool IsGrounded => Time.time - lastGrounded <= coyoteTime;

        Rigidbody body;
        float lastPressed = -10f;
        float lastGrounded = -10f;
        bool groundedThisStep;

        void Awake() => body = GetComponent<Rigidbody>();

        void Update()
        {
            if (!ExternalInput.Blocked && Input.GetButtonDown("Jump")) lastPressed = Time.time;
        }

        /// <summary>Lets tools (autopilot) request a jump without the keyboard.</summary>
        public void RequestJump() => lastPressed = Time.time;

        void FixedUpdate()
        {
            if (groundedThisStep) lastGrounded = Time.time;
            groundedThisStep = false;

            if (Time.time - lastPressed <= inputBuffer && Time.time - lastGrounded <= coyoteTime)
            {
                Vector3 v = body.linearVelocity;
                v.y = jumpVelocity;
                body.linearVelocity = v;
                lastPressed = -10f;
                lastGrounded = -10f;
                Jumped?.Invoke();
            }
        }

        void OnCollisionStay(Collision c)
        {
            for (int i = 0; i < c.contactCount; i++)
            {
                if (c.GetContact(i).normal.y >= groundNormalMin)
                {
                    groundedThisStep = true;
                    return;
                }
            }
        }
    }
}
