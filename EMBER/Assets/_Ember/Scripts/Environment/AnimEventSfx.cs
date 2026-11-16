using UnityEngine;

namespace Ember
{
    /// <summary>
    /// Receives AnimationEvents from the legacy clips played by HitAndPlayAnimation
    /// (plate click, gate rumble, bridge thud) and plays the matching clip in 3D.
    /// </summary>
    public class AnimEventSfx : MonoBehaviour
    {
        public AudioClip[] clips;
        public float volume = 0.9f;
        AudioSource source;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            if (source == null)
            {
                source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.minDistance = 5f;
                source.maxDistance = 45f;
                source.rolloffMode = AudioRolloffMode.Linear;
            }
        }

        public void PlaySfx(string clipName)
        {
            if (clips == null) return;
            foreach (var c in clips)
            {
                if (c != null && c.name == clipName)
                {
                    source.PlayOneShot(c, volume);
                    return;
                }
            }
        }
    }
}
