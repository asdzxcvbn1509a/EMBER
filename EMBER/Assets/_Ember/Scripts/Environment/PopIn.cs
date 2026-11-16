using System.Collections;
using UnityEngine;

namespace Ember
{
    /// <summary>Scales an object in with a small overshoot when it is enabled (bridges, beams, rubble).</summary>
    public class PopIn : MonoBehaviour
    {
        public float duration = 0.5f;
        public float delay = 0f;
        public Vector3 fromScale = new Vector3(0.02f, 0.02f, 0.02f);
        public bool overshoot = true;

        Vector3 target;
        bool captured;

        void Capture()
        {
            if (captured) return;
            target = transform.localScale;
            captured = true;
        }

        void Awake() => Capture();

        void OnEnable()
        {
            Capture();
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            Vector3 start = Vector3.Scale(target, fromScale);
            transform.localScale = start;
            if (delay > 0f) yield return new WaitForSeconds(delay);
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                float e = overshoot ? EaseOutBack(k) : 1f - (1f - k) * (1f - k);
                transform.localScale = Vector3.LerpUnclamped(start, target, e);
                yield return null;
            }
            transform.localScale = target;
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }
    }
}
