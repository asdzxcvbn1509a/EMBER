using UnityEngine;

namespace Ember
{
    /// <summary>
    /// Marks a level-design beat (1A, 1B ...). The capture tool uses these to number the
    /// top-down level maps and to frame hero screenshots. It has no effect on gameplay.
    /// </summary>
    public class LevelBeat : MonoBehaviour
    {
        public string code = "1A";
        public string title = "Awakening";
        [TextArea] public string note = "";
        [Tooltip("Hero shot camera position relative to this marker (world axes).")]
        public Vector3 shotOffset = new Vector3(7f, 7f, -10f);
        public Vector3 shotLookOffset = new Vector3(0f, 0.5f, 6f);
        public float shotFov = 50f;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.62f, 0.22f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 1.2f);
            Gizmos.color = new Color(1f, 0.62f, 0.22f, 0.35f);
            Gizmos.DrawLine(transform.position + shotOffset, transform.position + shotLookOffset);
        }
    }
}
