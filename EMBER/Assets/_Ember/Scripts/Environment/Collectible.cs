using UnityEngine;

namespace Ember
{
    /// <summary>Optional ember shard. Spins with the class animation Spin.anim; this script handles pickup.</summary>
    public class Collectible : MonoBehaviour
    {
        public string id = "S1_A";
        public GameObject pickupFx;
        public AudioClip sound;

        void Start()
        {
            if (GameManager.HasShard(id)) Destroy(gameObject);
        }

        void OnTriggerEnter(Collider other)
        {
            var rb = other.attachedRigidbody;
            if (rb == null || rb.name != "Player") return;
            if (GameManager.Instance != null) GameManager.Instance.CollectShard(id);
            if (pickupFx != null)
            {
                pickupFx.transform.SetParent(null, true);
                pickupFx.SetActive(true);
                Destroy(pickupFx, 3f);
            }
            if (sound != null) AudioSource.PlayClipAtPoint(sound, transform.position, 0.8f);
            Destroy(gameObject);
        }
    }
}
