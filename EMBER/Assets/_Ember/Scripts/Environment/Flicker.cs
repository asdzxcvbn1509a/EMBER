using UnityEngine;

namespace Ember
{
    /// <summary>Candle/torch flicker driven by Perlin noise.</summary>
    [RequireComponent(typeof(Light))]
    public class Flicker : MonoBehaviour
    {
        public float amount = 0.15f;
        public float speed = 6f;
        [HideInInspector] public float baseIntensity = -1f;

        Light lt;
        float seed;

        void Awake()
        {
            lt = GetComponent<Light>();
            if (baseIntensity < 0f) baseIntensity = lt.intensity;
            seed = Random.value * 100f;
        }

        void Update()
        {
            float n = Mathf.PerlinNoise(seed, Time.time * speed) * 2f - 1f;
            lt.intensity = baseIntensity * (1f + n * amount);
        }
    }
}
