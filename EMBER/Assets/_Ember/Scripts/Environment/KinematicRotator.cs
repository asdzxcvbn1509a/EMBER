using UnityEngine;

namespace Ember
{
    /// <summary>
    /// Rotating platform (orrery bridges) driven with MoveRotation. Optional stepping: rotates
    /// stepAngle degrees, then rests for pauseTime, which gives the player a readable rhythm.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class KinematicRotator : MonoBehaviour
    {
        public Vector3 axis = Vector3.up;
        public float degreesPerSecond = 30f;
        public bool stepped = true;
        public float stepAngle = 90f;
        public float pauseTime = 2.5f;
        [Tooltip("Rest this long before the first turn (the bridge starts aligned).")]
        public float initialPause = 0f;

        Rigidbody body;
        float accumulated;
        float wait;

        // read-only state for tools (the tutorial pilot crosses the orrery bridge while it rests aligned)
        public bool Resting => wait > 0f;
        public float RestLeft => Mathf.Max(0f, wait);

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            wait = initialPause;
        }

        void FixedUpdate()
        {
            if (wait > 0f) { wait -= Time.fixedDeltaTime; return; }
            float d = degreesPerSecond * Time.fixedDeltaTime;
            if (stepped)
            {
                if (accumulated + Mathf.Abs(d) >= stepAngle)
                {
                    d = Mathf.Sign(d) * (stepAngle - accumulated);
                    accumulated = 0f;
                    wait = pauseTime;
                }
                else accumulated += Mathf.Abs(d);
            }
            body.MoveRotation(body.rotation * Quaternion.AngleAxis(d, axis));
        }
    }
}
