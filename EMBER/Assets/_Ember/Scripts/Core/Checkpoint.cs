using UnityEngine;

namespace Ember
{
    /// <summary>Trigger that becomes the respawn point when the ember rolls through it.</summary>
    public class Checkpoint : MonoBehaviour
    {
        public Transform spawnPoint;
        public GameObject litFx;
        public bool isStart;
        public AudioClip sound;

        void Start()
        {
            if (litFx != null) litFx.SetActive(isStart);
            if (isStart && GameManager.Instance != null) GameManager.Instance.SetCheckpoint(Point, false);
        }

        Transform Point => spawnPoint != null ? spawnPoint : transform;

        void OnTriggerEnter(Collider other)
        {
            var rb = other.attachedRigidbody;
            if (rb == null || rb.name != "Player") return;
            var gm = GameManager.Instance;
            if (gm == null || gm.Checkpoint == Point) return;
            gm.SetCheckpoint(Point, !isStart);
            if (litFx != null && !litFx.activeSelf)
            {
                litFx.SetActive(true);
                if (sound != null) AudioSource.PlayClipAtPoint(sound, transform.position, 0.7f);
            }
        }
    }
}
