using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Ember
{
    /// <summary>Full-screen colour overlay used for every fade in the game.</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class ScreenFader : MonoBehaviour
    {
        public Image image;
        CanvasGroup group;
        Coroutine running;

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            if (image == null) image = GetComponent<Image>();
            group.alpha = 1f;
            group.blocksRaycasts = false;
            group.interactable = false;
        }

        public void SetColor(Color c)
        {
            if (image != null) image.color = new Color(c.r, c.g, c.b, 1f);
        }

        public void FadeIn(float duration) => Run(0f, duration);
        public void FadeOut(float duration) => Run(1f, duration);

        void Run(float target, float duration)
        {
            if (group == null) Awake();
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(Fade(target, Mathf.Max(0.01f, duration)));
        }

        IEnumerator Fade(float target, float duration)
        {
            float from = group.alpha;
            for (float t = 0f; t < duration; t += GameClock.UnscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(from, target, Mathf.SmoothStep(0f, 1f, t / duration));
                yield return null;
            }
            group.alpha = target;
            running = null;
        }
    }
}
