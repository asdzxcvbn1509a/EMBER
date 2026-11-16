using UnityEngine;

namespace Ember
{
    /// <summary>
    /// After the first time the named object enters, the trigger colliders on this object are switched off.
    /// HitAndClone starts a new spawning coroutine on every enter, so a repeat spawner (the lava rafts)
    /// is disarmed after its first press to keep a single, readable stream.
    /// </summary>
    public class DisarmAfterTrigger : MonoBehaviour
    {
        public string objectName = "Player";

        void OnTriggerEnter(Collider other)
        {
            var rb = other.attachedRigidbody;
            string n = rb != null ? rb.name : other.name;
            if (n != objectName) return;
            foreach (var c in GetComponents<Collider>())
                if (c.isTrigger) c.enabled = false;
        }
    }
}
