using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ember
{
    /// <summary>
    /// On-screen extras for the tutorial video, rebuilt in every scene as children of the game's "UI" canvas,
    /// just before its Fader (above the HUD and the pause menu, below fades):
    ///  * a key panel (bottom right) that lights up the keys the pilot holds (DemoInput), in the game's palette and font;
    ///  * a mouse cursor that moves to menu buttons and drives them with real pointer events (hover glow, click, sound).
    /// It also switches off the EventSystem's input module, so the real mouse cannot touch the menus during a take.
    /// </summary>
    public class TutorialOverlay : MonoBehaviour
    {
        public DemoInput input;

        static readonly Color Amber = new Color(1f, 0.72f, 0.38f, 1f);
        static readonly Color Pale = new Color(0.9f, 0.88f, 0.84f, 1f);
        static readonly Color IdleFill = new Color(0.106f, 0.129f, 0.251f, 0.62f);
        static readonly Color IdleFrame = new Color(0.62f, 0.62f, 0.66f, 0.6f);
        // a pressed key: deep amber with a light label, readable at every point of the fade (keys are often only tapped)
        static readonly Color LitFill = new Color(0.86f, 0.42f, 0.08f, 0.96f);
        static readonly Color LitFrame = new Color(1f, 0.8f, 0.5f, 1f);
        static readonly Color LitText = new Color(1f, 0.97f, 0.9f, 1f);

        /// <summary>Key panel rectangle in canvas pixels (origin bottom-left): x0, y0, x1, y1.</summary>
        public static readonly float[] KeysRect = { 1920f - 48f - 202f, 44f, 1920f - 48f, 44f + 222f };

        class KeyView
        {
            public DemoInput.Key key;
            public RectTransform rt;
            public Image frame, fill, glow;
            public TMP_Text label;
            public float lit;
        }

        readonly List<KeyView> keys = new List<KeyView>();
        CanvasGroup keysGroup, cursorGroup;
        RectTransform canvasRt, cursorRt, ringRt;
        Image ring;
        bool keysWanted, cursorWanted;
        Vector2 cursorPos = new Vector2(1250f, 560f);
        Coroutine moving;
        GameObject hovered;
        float ringT = 1f;
        static Sprite round, outline, soft, arrow, circle;

        void OnEnable() => SceneManager.sceneLoaded += OnScene;
        void OnDisable() => SceneManager.sceneLoaded -= OnScene;
        void Start() => Build();
        void OnScene(Scene s, LoadSceneMode m) => Build();

        // ---------------------------------------------------------------- public
        public void ShowKeys(bool on) => keysWanted = on;
        public void ShowCursor(bool on) => cursorWanted = on;
        public void ShowCursorAt(Vector2 canvasPos) { cursorPos = canvasPos; cursorWanted = true; Place(); }

        /// <summary>x, y in canvas pixels and z = 1 while visible (for the timeline).</summary>
        public Vector3 State() => new Vector3(cursorPos.x, cursorPos.y, cursorGroup != null && cursorGroup.alpha > 0.5f ? 1f : 0f);

        public GameObject Hovered => hovered;

        /// <summary>Canvas position of a point inside a UI element (fx, fy: 0..1 across its rectangle).</summary>
        public bool CanvasPointOf(string path, float fx, float fy, out Vector2 pos)
        {
            pos = cursorPos;
            var go = GameObject.Find(path);
            if (go == null || canvasRt == null) return false;
            var rt = (RectTransform)go.transform;
            Vector3 world = rt.TransformPoint(new Vector3(Mathf.Lerp(rt.rect.xMin, rt.rect.xMax, fx), Mathf.Lerp(rt.rect.yMin, rt.rect.yMax, fy), 0f));
            Vector2 local = canvasRt.InverseTransformPoint(world);
            pos = local + canvasRt.rect.size * 0.5f;   // canvas centre pivot -> bottom-left origin
            return true;
        }

        public IEnumerator MoveTo(Vector2 target, float seconds)
        {
            cursorWanted = true;
            Vector2 from = cursorPos;
            for (float t = 0f; t < seconds; t += GameClock.UnscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / seconds);
                cursorPos = Vector2.Lerp(from, target, k) + new Vector2(0f, Mathf.Sin(k * Mathf.PI) * 18f);   // a slight arc
                yield return null;
            }
            cursorPos = target;
        }

        /// <summary>Press and release the left button over the hovered element (Button.onClick and MenuButtonFX react).</summary>
        public bool Click()
        {
            ringT = 0f;
            if (hovered == null) return false;
            var es = EventSystem.current != null ? EventSystem.current : FindAnyObjectByType<EventSystem>();
            var ped = new PointerEventData(es)
            {
                button = PointerEventData.InputButton.Left,
                position = ScreenPoint(),
                pointerPress = hovered,
                rawPointerPress = hovered,
                eligibleForClick = true,
                clickCount = 1,
            };
            ExecuteEvents.Execute(hovered, ped, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(hovered, ped, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(hovered, ped, ExecuteEvents.pointerClickHandler);
            return true;
        }

        // ---------------------------------------------------------------- build
        void Build()
        {
            keys.Clear();
            hovered = null;
            foreach (var m in FindObjectsByType<StandaloneInputModule>(FindObjectsSortMode.None)) m.enabled = false;
            var ui = GameObject.Find("UI");
            if (ui == null) return;
            canvasRt = (RectTransform)ui.transform;
            var fader = ui.GetComponentInChildren<ScreenFader>(true);
            int index = fader != null ? fader.transform.GetSiblingIndex() : ui.transform.childCount;
            var tmp = FindAnyObjectByType<TMP_Text>();
            var font = tmp != null ? tmp.font : null;
            MakeSprites();

            // ---- key panel
            var panel = NewRect(ui.transform, "TutorialKeys");
            panel.anchorMin = panel.anchorMax = new Vector2(1f, 0f);
            panel.pivot = new Vector2(1f, 0f);
            panel.anchoredPosition = new Vector2(-48f, 44f);
            panel.sizeDelta = new Vector2(202f, 222f);
            panel.SetSiblingIndex(index++);
            keysGroup = panel.gameObject.AddComponent<CanvasGroup>();
            keysGroup.alpha = 0f;
            keysGroup.blocksRaycasts = false;
            AddKey(panel, font, DemoInput.Key.Space, "SPACE", 0f, 0f, 202f, 40f, 17f);
            AddKey(panel, font, DemoInput.Key.A, "A", 0f, 48f, 62f, 62f, 28f);
            AddKey(panel, font, DemoInput.Key.S, "S", 70f, 48f, 62f, 62f, 28f);
            AddKey(panel, font, DemoInput.Key.D, "D", 140f, 48f, 62f, 62f, 28f);
            AddKey(panel, font, DemoInput.Key.W, "W", 70f, 118f, 62f, 62f, 28f);
            AddKey(panel, font, DemoInput.Key.Esc, "ESC", 0f, 188f, 62f, 34f, 15f);
            AddKey(panel, font, DemoInput.Key.R, "R", 70f, 188f, 62f, 34f, 18f);
            AddKey(panel, font, DemoInput.Key.F, "F", 140f, 188f, 62f, 34f, 18f);

            // ---- cursor (tip at the pivot) and its click ring
            var cur = NewRect(ui.transform, "TutorialCursor");
            cur.anchorMin = cur.anchorMax = Vector2.zero;
            cur.pivot = new Vector2(0.5f, 0.5f);
            cur.sizeDelta = Vector2.zero;
            cur.SetSiblingIndex(index++);
            cursorGroup = cur.gameObject.AddComponent<CanvasGroup>();
            cursorGroup.alpha = 0f;
            cursorGroup.blocksRaycasts = false;
            cursorRt = cur;
            ringRt = NewRect(cur, "Ring");
            ringRt.sizeDelta = new Vector2(44f, 44f);
            ring = NewImage(ringRt, circle, new Color(Amber.r, Amber.g, Amber.b, 0f));
            var arrowRt = NewRect(cur, "Arrow");
            arrowRt.pivot = new Vector2(0.06f, 0.96f);
            arrowRt.sizeDelta = new Vector2(30f, 44f);
            NewImage(arrowRt, arrow, Color.white);
            Place();
        }

        void AddKey(RectTransform panel, TMP_FontAsset font, DemoInput.Key key, string text, float x, float y, float w, float h, float size)
        {
            var rt = NewRect(panel, "Key_" + text);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x + w * 0.5f, y + h * 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            var v = new KeyView { key = key, rt = rt };
            var g = NewRect(rt, "Glow");
            g.sizeDelta = new Vector2(w + 34f, h + 34f);
            v.glow = NewImage(g, soft, new Color(Amber.r, Amber.g, Amber.b, 0f));
            var fi = NewRect(rt, "Fill");
            fi.anchorMin = Vector2.zero; fi.anchorMax = Vector2.one; fi.offsetMin = new Vector2(1f, 1f); fi.offsetMax = new Vector2(-1f, -1f);
            v.fill = NewImage(fi, round, IdleFill, true);
            var fr = NewRect(rt, "Frame");
            fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = fr.offsetMax = Vector2.zero;
            v.frame = NewImage(fr, outline, IdleFrame, true);
            var lb = NewRect(rt, "Label");
            lb.anchorMin = Vector2.zero; lb.anchorMax = Vector2.one; lb.offsetMin = lb.offsetMax = Vector2.zero;
            var t = lb.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.characterSpacing = text.Length > 1 ? 6f : 0f;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Pale;
            t.raycastTarget = false;
            v.label = t;
            keys.Add(v);
        }

        // ---------------------------------------------------------------- per frame
        void LateUpdate()
        {
            float dt = GameClock.UnscaledDeltaTime;
            if (keysGroup != null)
            {
                keysGroup.alpha = Mathf.MoveTowards(keysGroup.alpha, keysWanted ? 1f : 0f, dt / 0.35f);
                foreach (var k in keys)
                {
                    bool on = input != null && input.IsDown(k.key);
                    k.lit = Mathf.MoveTowards(k.lit, on ? 1f : 0f, dt / 0.06f);
                    k.fill.color = Color.Lerp(IdleFill, LitFill, k.lit);
                    k.frame.color = Color.Lerp(IdleFrame, LitFrame, k.lit);
                    k.label.color = Color.Lerp(Pale, LitText, k.lit);
                    k.glow.color = new Color(Amber.r, Amber.g, Amber.b, 0.55f * k.lit);
                    k.rt.localScale = Vector3.one * (1f - 0.05f * k.lit);
                }
            }
            if (cursorGroup != null)
            {
                cursorGroup.alpha = Mathf.MoveTowards(cursorGroup.alpha, cursorWanted ? 1f : 0f, dt / 0.25f);
                Place();
                ringT = Mathf.Min(1f, ringT + dt / 0.35f);
                ringRt.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.5f, ringT);
                ring.color = new Color(Amber.r, Amber.g, Amber.b, 0.9f * (1f - ringT));
                UpdateHover();
            }
        }

        void Place()
        {
            if (cursorRt != null) cursorRt.anchoredPosition = cursorPos;
        }

        Vector2 ScreenPoint() => cursorRt != null ? (Vector2)cursorRt.position : Vector2.zero;

        /// <summary>Pointer enter / exit on the button under the cursor, among the buttons of the topmost open panel.</summary>
        void UpdateHover()
        {
            GameObject over = null;
            if (cursorWanted && cursorGroup.alpha > 0.2f)
            {
                Vector2 sp = ScreenPoint();
                Transform scope = null;
                foreach (var name in new[] { "UI/PausePanel", "UI/StageSelect", "UI/HowToPlay" })
                {
                    var p = GameObject.Find(name);
                    if (p != null && p.activeInHierarchy) scope = p.transform;
                }
                foreach (var b in FindObjectsByType<Button>(FindObjectsSortMode.None))
                {
                    if (!b.isActiveAndEnabled || !b.IsInteractable()) continue;
                    if (scope != null && !b.transform.IsChildOf(scope)) continue;
                    if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)b.transform, sp, null)) over = b.gameObject;
                }
            }
            if (over == hovered) return;
            var es = EventSystem.current != null ? EventSystem.current : FindAnyObjectByType<EventSystem>();
            var ped = new PointerEventData(es) { position = ScreenPoint() };
            if (hovered != null) ExecuteEvents.Execute(hovered, ped, ExecuteEvents.pointerExitHandler);
            hovered = over;
            if (hovered != null) ExecuteEvents.Execute(hovered, ped, ExecuteEvents.pointerEnterHandler);
        }

        // ---------------------------------------------------------------- procedural sprites
        static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            return (RectTransform)go.transform;
        }

        static Image NewImage(RectTransform rt, Sprite sprite, Color color, bool sliced = false)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            if (sliced) { img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1.6f; }   // 18 px borders drawn at about 11 px
            return img;
        }

        static void MakeSprites()
        {
            if (round != null) return;
            round = Make(64, 64, (x, y) => Mathf.Clamp01(13.5f - RoundedDistance(x, y, 64, 64, 14f)), new Vector4(18, 18, 18, 18));
            outline = Make(64, 64, (x, y) =>
            {
                float d = RoundedDistance(x, y, 64, 64, 14f);
                return Mathf.Clamp01(13.5f - d) * Mathf.Clamp01(d - 9.8f);   // a ring about 3 px wide along the edge
            }, new Vector4(18, 18, 18, 18));
            soft = Make(64, 64, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f;
                return Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d);
            }, Vector4.zero);
            circle = Make(64, 64, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f));
                return Mathf.Clamp01(2.2f - Mathf.Abs(d - 28f));
            }, Vector4.zero);
            // arrow pointer: white fill with a dark outline, tip at the top-left
            var poly = new[] { new Vector2(2, 2), new Vector2(2, 34), new Vector2(10, 27), new Vector2(15, 39), new Vector2(21, 37), new Vector2(16, 25), new Vector2(26, 25) };
            const int w = 30, h = 44;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);   // image coordinates, y down
                    float edge = EdgeDistance(p, poly);
                    bool inside = Inside(p, poly);
                    float fill = inside ? Mathf.Clamp01(edge - 1.4f) : 0f;
                    float outline = inside ? Mathf.Clamp01(edge + 0.5f) : Mathf.Clamp01(1.6f - edge);
                    byte a = (byte)(255 * Mathf.Clamp01(Mathf.Max(fill, outline)));
                    byte c = (byte)(255 * fill);
                    px[(h - 1 - y) * w + x] = new Color32(c, c, c, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            arrow = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.06f, 0.96f), 100f);
        }

        static Sprite Make(int w, int h, System.Func<float, float, float> alpha, Vector4 border)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(alpha(x, y))));
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        static float RoundedDistance(float x, float y, int w, int h, float r)
        {
            float cx = Mathf.Clamp(x + 0.5f, r, w - r), cy = Mathf.Clamp(y + 0.5f, r, h - r);
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
            return d + (r - 14f);
        }

        static bool Inside(Vector2 p, Vector2[] poly)
        {
            bool c = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    c = !c;
            return c;
        }

        static float EdgeDistance(Vector2 p, Vector2[] poly)
        {
            float best = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], b = poly[i], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }
    }
}
