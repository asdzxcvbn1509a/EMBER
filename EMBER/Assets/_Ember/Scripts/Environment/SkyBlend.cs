using System.Collections;
using UnityEngine;

namespace Ember
{
    /// <summary>
    /// Finale sky change: blends the Ember/SkyGradient material from night (A) to dawn (B) and moves
    /// fog, ambient light and the sun colour with it. GameManager gives each scene its own copy of the
    /// skybox material, so the asset on disk is never modified in play mode.
    /// </summary>
    public class SkyBlend : MonoBehaviour
    {
        public float duration = 6f;
        public Color fogTo = new Color(0.95f, 0.72f, 0.6f);
        public float fogDensityTo = 0.004f;
        public Color ambientTo = new Color(0.45f, 0.38f, 0.40f);
        public Light sun;
        public float sunIntensityTo = 2.2f;
        public Color sunColorTo = new Color(1f, 0.85f, 0.65f);
        [Tooltip("Sky sun-disc direction at the end of the blend (the disc rises with the light).")]
        public Vector3 sunDirectionTo = new Vector3(0.25f, 0.4f, 1f);

        void OnEnable() => StartCoroutine(Run());

        IEnumerator Run()
        {
            var sky = RenderSettings.skybox;
            bool hasBlend = sky != null && sky.HasProperty("_Blend");
            float b0 = hasBlend ? sky.GetFloat("_Blend") : 0f;
            Vector3 sd0 = hasBlend && sky.HasProperty("_SunDirection") ? (Vector3)sky.GetVector("_SunDirection") : sunDirectionTo;
            Color f0 = RenderSettings.fogColor;
            float d0 = RenderSettings.fogDensity;
            Color a0 = RenderSettings.ambientLight;
            float s0 = sun != null ? sun.intensity : 0f;
            Color c0 = sun != null ? sun.color : Color.white;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                if (hasBlend)
                {
                    sky.SetFloat("_Blend", Mathf.Lerp(b0, 1f, k));
                    if (sky.HasProperty("_SunDirection")) sky.SetVector("_SunDirection", Vector3.Lerp(sd0, sunDirectionTo, k));
                }
                RenderSettings.fogColor = Color.Lerp(f0, fogTo, k);
                RenderSettings.fogDensity = Mathf.Lerp(d0, fogDensityTo, k);
                GameManager.SetAmbient(Color.Lerp(a0, ambientTo, k));
                if (sun != null)
                {
                    sun.intensity = Mathf.Lerp(s0, sunIntensityTo, k);
                    sun.color = Color.Lerp(c0, sunColorTo, k);
                }
                yield return null;
            }
        }
    }
}
