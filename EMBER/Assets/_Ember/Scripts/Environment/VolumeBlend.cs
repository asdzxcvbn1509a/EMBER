using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ember
{
    /// <summary>Cross-fades two post-processing volumes when enabled (night grade -> dawn grade).</summary>
    public class VolumeBlend : MonoBehaviour
    {
        public Volume fadeIn;
        public Volume fadeOut;
        public float duration = 5f;

        void OnEnable() => StartCoroutine(Run());

        IEnumerator Run()
        {
            float a0 = fadeIn != null ? fadeIn.weight : 0f;
            float b0 = fadeOut != null ? fadeOut.weight : 1f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                if (fadeIn != null) fadeIn.weight = Mathf.Lerp(a0, 1f, k);
                if (fadeOut != null) fadeOut.weight = Mathf.Lerp(b0, 0f, k);
                yield return null;
            }
            if (fadeIn != null) fadeIn.weight = 1f;
            if (fadeOut != null) fadeOut.weight = 0f;
        }
    }
}
