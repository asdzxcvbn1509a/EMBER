using System.Collections;
using UnityEngine;

namespace Ember
{
    /// <summary>
    /// When a lantern's "Lit" group is shown by HitAndShowHide, its light swells up with a short
    /// flare instead of popping on, then hands over to Flicker.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class FadeInLight : MonoBehaviour
    {
        public float duration = 0.9f;
        public float flare = 1.8f;

        Light lt;
        float target = -1f;

        void Awake()
        {
            lt = GetComponent<Light>();
            if (target < 0f) target = lt.intensity;
        }

        void OnEnable()
        {
            if (lt == null) Awake();
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            var flicker = GetComponent<Flicker>();
            if (flicker != null) flicker.enabled = false;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                float swell = Mathf.SmoothStep(0f, 1f, k);
                float burst = 1f + (flare - 1f) * Mathf.Sin(k * Mathf.PI);
                lt.intensity = target * swell * burst;
                yield return null;
            }
            lt.intensity = target;
            if (flicker != null) { flicker.baseIntensity = target; flicker.enabled = true; }
        }
    }
}
