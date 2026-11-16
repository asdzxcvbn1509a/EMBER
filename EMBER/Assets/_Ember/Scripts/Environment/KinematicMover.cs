using UnityEngine;

namespace Ember
{
    /// <summary>
    /// Moving platform driven with Rigidbody.MovePosition so the rolling ember is carried by
    /// friction. Legacy animations teleport colliders each frame, which does not carry a ball.
    /// PingPong: travels between its start and start + travel. Drift: constant velocity (lava rafts).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class KinematicMover : MonoBehaviour
    {
        public enum Mode { PingPong, Drift }

        public Mode mode = Mode.PingPong;
        public Vector3 travel = new Vector3(0f, 0f, 4f);
        public float speed = 1.5f;
        public float pause = 1.2f;
        public Vector3 driftVelocity = new Vector3(0f, 0f, 2f);

        Rigidbody body;
        Vector3 a, b;
        float t;
        int dir = 1;
        float wait;
        bool started;

        // read-only state for tools (the tutorial pilot waits for a docked slab before boarding)
        /// <summary>PingPong position 0..1 along the travel (0 = start, 1 = start + travel).</summary>
        public float Progress => t;
        /// <summary>+1 while heading for start + travel, -1 while heading back.</summary>
        public int Direction => dir;
        /// <summary>True while resting at either end.</summary>
        public bool Paused => wait > 0f;
        public float PauseLeft => Mathf.Max(0f, wait);

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
        }

        void Begin()
        {
            a = body.position;
            b = a + travel;
            started = true;
        }

        void FixedUpdate()
        {
            if (mode == Mode.Drift)
            {
                body.MovePosition(body.position + driftVelocity * Time.fixedDeltaTime);
                return;
            }
            if (!started) Begin();
            if (wait > 0f) { wait -= Time.fixedDeltaTime; return; }
            float len = travel.magnitude;
            if (len < 1e-4f) return;
            t += dir * speed * Time.fixedDeltaTime / len;
            if (t >= 1f) { t = 1f; dir = -1; wait = pause; }
            else if (t <= 0f) { t = 0f; dir = 1; wait = pause; }
            body.MovePosition(Vector3.Lerp(a, b, Mathf.SmoothStep(0f, 1f, t)));
        }

        void OnDrawGizmosSelected()
        {
            if (mode != Mode.PingPong) return;
            Gizmos.color = Color.cyan;
            Vector3 p = Application.isPlaying && started ? a : transform.position;
            Gizmos.DrawLine(p, p + travel);
        }
    }
}
