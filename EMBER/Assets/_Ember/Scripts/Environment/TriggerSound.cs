using UnityEngine;

namespace Ember
{
    /// <summary>Plays a sound when a named object enters this trigger (teleport rings, spawners).</summary>
    public class TriggerSound : MonoBehaviour
    {
        public string objectName = "Player";
        public AudioClip clip;
        public float volume = 0.9f;
        public float cooldown = 0.6f;

        float last = -10f;

        void OnTriggerEnter(Collider other)
        {
            var rb = other.attachedRigidbody;
            string n = rb != null ? rb.name : other.name;
            if (n != objectName || Time.time - last < cooldown || clip == null) return;
            last = Time.time;
            AudioSource.PlayClipAtPoint(clip, transform.position, volume);
        }
    }
}
