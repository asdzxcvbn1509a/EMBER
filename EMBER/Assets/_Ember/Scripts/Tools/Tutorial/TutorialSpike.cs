using System.Collections;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace Ember
{
    /// <summary>
    /// Capture test for the tutorial recorder (EMBER.exe -tutorialSpike DIR), run on the title screen.
    /// 150 frames with known content, checked afterwards by ArtSource/Tutorial/tut_spike_check.py:
    ///  * a red square in the top-left corner (frames are upright),
    ///  * frame 60 is white and a click starts at frame 60 = 2.000 s (picture and sound are in sync),
    ///  * frame 10 is also encoded on the main thread (worker JPEGs are identical),
    ///  * a 0.9 s fade starts at frame 30 while frames 30..59 are slowed down by 80 ms each (GameClock: 27 frames).
    /// </summary>
    public class TutorialSpike : MonoBehaviour
    {
        public const int Frames = 150, FadeFrame = 30, FlashFrame = 60, MainEncodeFrame = 10;

        IEnumerator Start()
        {
            var rec = GetComponent<TutorialRecorder>();
            rec.MainThreadEncodeFrame = MainEncodeFrame;
            var tl = rec.Timeline;

            var canvasGo = new GameObject("SpikeCanvas", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var marker = NewImage(canvasGo.transform, "Marker", Color.red);
            marker.rectTransform.anchorMin = marker.rectTransform.anchorMax = new Vector2(0f, 1f);
            marker.rectTransform.pivot = new Vector2(0f, 1f);
            marker.rectTransform.anchoredPosition = new Vector2(8f, -8f);
            marker.rectTransform.sizeDelta = new Vector2(48f, 48f);
            var flash = NewImage(canvasGo.transform, "Flash", Color.white);
            flash.rectTransform.anchorMin = Vector2.zero;
            flash.rectTransform.anchorMax = Vector2.one;
            flash.rectTransform.offsetMin = flash.rectTransform.offsetMax = Vector2.zero;
            flash.enabled = false;
            marker.transform.SetAsLastSibling();

            // a sharp click: 12 ms of a loud square wave, easy to find in the recording
            int rate = AudioSettings.outputSampleRate;
            var data = new float[rate / 10];
            for (int i = 0; i < rate * 12 / 1000; i++) data[i] = ((i / 12) % 2 == 0 ? 0.9f : -0.9f);
            var click = AudioClip.Create("SpikeClick", data.Length, 1, rate, false);
            click.SetData(data, 0);
            var src = gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.ignoreListenerPause = true;

            bool clicked = false, faded = false, quieted = false;
            while (rec.Frame < Frames && !rec.Finished)
            {
                int f = rec.Frame;
                if (f >= FadeFrame - 1 && !quieted)
                {
                    // quiet the title music so the click onset stands out (frames 0..28 keep it for the level check)
                    foreach (var s in FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) if (s != src) s.volume *= 0.05f;
                    quieted = true;
                }
                if (f == FadeFrame && !faded)
                {
                    var gm = GameManager.Instance;
                    if (gm != null && gm.fader != null) { gm.fader.SetColor(Color.black); gm.fader.FadeOut(0.9f); }
                    faded = true;
                    tl.Event("spike", "fade out 0.9 s");
                }
                if (f >= FadeFrame && f < FadeFrame + 30) Thread.Sleep(80);
                flash.enabled = f == FlashFrame;
                if (f == FlashFrame && !clicked)
                {
                    src.PlayOneShot(click, 1f);
                    clicked = true;
                    tl.Event("spike", "flash + click");
                }
                if (f < 4 || f == FlashFrame || f == FadeFrame)
                    tl.Log($"time f{f}: unscaledDelta {Time.unscaledDeltaTime:0.0000} delta {Time.deltaTime:0.0000} capture {Time.captureDeltaTime:0.0000} GameClock {GameClock.UnscaledDeltaTime:0.0000}");
                yield return null;
            }
            rec.Finish(0, "spike done");
        }

        static Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }
    }
}
