using System.Collections;
using TMPro;
using UnityEngine;

namespace Ember
{
    /// <summary>Minimal HUD: stage title card, shard counter, toasts, pause panel, photo-mode hint.</summary>
    public class Hud : MonoBehaviour
    {
        public CanvasGroup stageCard;
        public TMP_Text stageTitle;
        public TMP_Text stageSubtitle;
        public TMP_Text shardText;
        public CanvasGroup shardGroup;
        public CanvasGroup toastGroup;
        public TMP_Text toastText;
        public GameObject pausePanel;
        public GameObject photoHint;
        public CanvasGroup controlsHint;

        Coroutine toastRoutine;

        void Start()
        {
            if (stageCard != null) stageCard.alpha = 0f;
            if (toastGroup != null) toastGroup.alpha = 0f;
            if (pausePanel != null) pausePanel.SetActive(false);
            if (photoHint != null) photoHint.SetActive(false);
            var gm = GameManager.Instance;
            bool inStage = gm != null && gm.stageNumber >= 1 && gm.stageNumber <= 3;
            if (shardGroup != null) shardGroup.alpha = inStage ? 1f : 0f;
            if (controlsHint != null) StartCoroutine(ControlsHint(inStage && gm.stageNumber == 1));
            RefreshShards();
        }

        public void ShowStageCard(string title, string subtitle)
        {
            if (stageCard == null) return;
            if (stageTitle != null) stageTitle.text = title;
            if (stageSubtitle != null) stageSubtitle.text = subtitle;
            StartCoroutine(StageCardRoutine());
        }

        IEnumerator StageCardRoutine()
        {
            yield return new WaitForSeconds(0.8f);
            yield return Fade(stageCard, 0f, 1f, 1.2f);
            yield return new WaitForSeconds(2.6f);
            yield return Fade(stageCard, 1f, 0f, 1.4f);
        }

        IEnumerator ControlsHint(bool show)
        {
            controlsHint.alpha = 0f;
            if (!show) yield break;
            yield return new WaitForSeconds(5.5f);
            yield return Fade(controlsHint, 0f, 1f, 1f);
            yield return new WaitForSeconds(9f);
            yield return Fade(controlsHint, 1f, 0f, 1.5f);
        }

        public void Toast(string message)
        {
            if (toastGroup == null) return;
            if (toastText != null) toastText.text = message;
            if (toastRoutine != null) StopCoroutine(toastRoutine);
            toastRoutine = StartCoroutine(ToastRoutine());
        }

        IEnumerator ToastRoutine()
        {
            yield return Fade(toastGroup, toastGroup.alpha, 1f, 0.25f);
            yield return new WaitForSeconds(1.6f);
            yield return Fade(toastGroup, 1f, 0f, 0.7f);
            toastRoutine = null;
        }

        public void RefreshShards()
        {
            var gm = GameManager.Instance;
            if (shardText == null || gm == null) return;
            shardText.text = gm.ShardsInThisStage + " / " + gm.shardsInStage;
        }

        public void ShowPause(bool on)
        {
            if (pausePanel != null) pausePanel.SetActive(on);
            // the stage title card would show through the pause menu during the first seconds of a stage
            if (stageCard != null) stageCard.gameObject.SetActive(!on);
        }

        public void ShowPhotoHint(bool on)
        {
            if (photoHint != null) photoHint.SetActive(on);
        }

        static IEnumerator Fade(CanvasGroup g, float from, float to, float time)
        {
            for (float t = 0f; t < time; t += GameClock.UnscaledDeltaTime)
            {
                g.alpha = Mathf.Lerp(from, to, t / time);
                yield return null;
            }
            g.alpha = to;
        }
    }
}
