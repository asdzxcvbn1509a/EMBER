using UnityEngine;

namespace Ember
{
    /// <summary>Gentle floating motion for decorative islands, crystals and clouds (not for platforms).</summary>
    public class Bob : MonoBehaviour
    {
        public Vector3 amplitude = new Vector3(0f, 0.25f, 0f);
        public float period = 4f;
        public Vector3 spinDegreesPerSecond;

        Vector3 start;
        float phase;

        void Start()
        {
            start = transform.localPosition;
            phase = Random.value * period;
        }

        void Update()
        {
            float s = Mathf.Sin((Time.time + phase) / period * Mathf.PI * 2f);
            transform.localPosition = start + amplitude * s;
            if (spinDegreesPerSecond != Vector3.zero)
                transform.Rotate(spinDegreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
