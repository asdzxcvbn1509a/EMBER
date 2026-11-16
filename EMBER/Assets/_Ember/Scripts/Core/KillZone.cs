using UnityEngine;

namespace Ember
{
    /// <summary>
    /// Pits and lava: the ember respawns at the last checkpoint, cloned props
    /// (names ending in "(Clone)") are removed so they do not fall forever.
    /// </summary>
    public class KillZone : MonoBehaviour
    {
        public bool destroyClones = true;

        void OnTriggerEnter(Collider other)
        {
            var rb = other.attachedRigidbody;
            var go = rb != null ? rb.gameObject : other.gameObject;
            if (go.name == "Player")
            {
                if (GameManager.Instance != null) GameManager.Instance.Respawn();
            }
            else if (destroyClones && go.name.EndsWith("(Clone)"))
            {
                Destroy(go);
            }
        }
    }
}
