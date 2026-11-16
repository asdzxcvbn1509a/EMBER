using System.Collections;
using TMPro;
using UnityEngine;

namespace Ember
{
    /// <summary>Fills in the play statistics and scrolls the credits on the ending scene.</summary>
    public class EndingCredits : MonoBehaviour
    {
        public TMP_Text statsText;
        public RectTransform scroller;
        public float scrollSpeed = 38f;
        public float startDelay = 3f;
        public CanvasGroup buttons;

        float endY;

        IEnumerator Start()
        {
            float seconds = GameManager.SessionStartTime >= 0f ? GameClock.Realtime - GameManager.SessionStartTime : 0f;
            int m = Mathf.FloorToInt(seconds / 60f), s = Mathf.FloorToInt(seconds % 60f);
            if (statsText != null)
                statsText.text = $"TIME  {m:00}:{s:00}      EMBER SHARDS  {GameManager.ShardsCollected} / {GameManager.ShardsTotal}";
            if (buttons != null) { buttons.alpha = 0f; buttons.interactable = false; buttons.blocksRaycasts = false; }
            yield return null; // let the layout group size the credits first
            if (scroller != null)
            {
                Canvas.ForceUpdateCanvases();
                var window = scroller.parent as RectTransform;
                float windowHeight = window != null ? window.rect.height : 760f;
                // stop when the last line ("thank you for playing") sits in the middle of the window
                endY = scroller.rect.height + windowHeight * 0.5f - 40f;
            }
            yield return new WaitForSeconds(startDelay);
            if (buttons != null)
            {
                for (float t = 0f; t < 1.2f; t += Time.deltaTime) { buttons.alpha = t / 1.2f; yield return null; }
                buttons.alpha = 1f; buttons.interactable = true; buttons.blocksRaycasts = true;
            }
        }

        void Update()
        {
            if (scroller == null || Time.timeSinceLevelLoad < startDelay) return;
            var p = scroller.anchoredPosition;
            if (p.y < endY) scroller.anchoredPosition = new Vector2(p.x, p.y + scrollSpeed * Time.deltaTime);
        }
    }
}
