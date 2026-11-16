using UnityEngine;

namespace Ember
{
    /// <summary>Pushes the child rigidbodies apart when shown (rubble of the cracked wall).</summary>
    public class ExplodeOnEnable : MonoBehaviour
    {
        public float force = 6f;
        public float upward = 0.6f;
        public Vector3 bias = new Vector3(0f, 0f, 1f);
        public float lifetime = 12f;

        void OnEnable()
        {
            foreach (var rb in GetComponentsInChildren<Rigidbody>())
            {
                Vector3 dir = (rb.position - transform.position).normalized + bias.normalized * 0.8f + Vector3.up * upward;
                rb.AddForce(dir.normalized * force * Random.Range(0.7f, 1.3f), ForceMode.VelocityChange);
                rb.AddTorque(Random.insideUnitSphere * force, ForceMode.VelocityChange);
            }
            if (lifetime > 0f) Destroy(gameObject, lifetime);
        }
    }
}
