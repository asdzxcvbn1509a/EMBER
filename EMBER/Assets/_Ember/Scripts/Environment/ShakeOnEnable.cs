using UnityEngine;

namespace Ember
{
    /// <summary>Shakes the camera when shown (e.g. the rubble group revealed by HitAndShowHide).</summary>
    public class ShakeOnEnable : MonoBehaviour
    {
        public float amplitude = 0.35f;
        public float duration = 0.6f;
        public AudioClip sound;

        void OnEnable()
        {
            if (CameraRig.Instance != null) CameraRig.Instance.Shake(amplitude, duration);
            if (sound != null) AudioSource.PlayClipAtPoint(sound, transform.position, 1f);
        }
    }
}
