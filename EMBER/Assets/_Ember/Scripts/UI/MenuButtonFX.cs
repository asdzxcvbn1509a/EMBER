using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ember
{
    /// <summary>
    /// Mouse-over effect for menu buttons, "the ember catches": the label warms to amber and steps aside,
    /// a small ember lights up beside it with a flickering halo and rising sparks, and a band of warm light
    /// sweeps across the plate. A click makes the ember flare.
    /// It follows the mouse only, not keyboard selection: on the title screen Space and WASD also roll the
    /// ember, and the default Submit / Navigate axes use those same keys.
    /// Runs on unscaled time so it also works in the pause menu (timeScale 0).
    /// The parts are built by UIFactory.Btn and start invisible.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class MenuButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public enum Side { Left, Center, Right }

        [Serializable]
        public class Marker
        {
            public RectTransform root;
            public Image ember;
            public Image halo;
            public Image[] sparks;
        }

        [Tooltip("Which edge the label hugs; the ember sits on that side (both sides when centred)")]
        public Side side;
        public TMP_Text label;
        public Image band;
        public Image line;
        public Marker[] markers;

        [Header("Sound")]
        public AudioSource sfx;
        public AudioClip hoverClip;
        public AudioClip clickClip;
        public float hoverVolume = 0.2f;
        public float clickVolume = 0.5f;

        [Header("Look")]
        public Color hoverText = new Color(1f, 0.88f, 0.68f);
        public Color glow = new Color(1f, 0.5f, 0.15f);
        public Color emberColor = new Color(1f, 0.8f, 0.45f);
        [Range(0f, 1f)] public float bandAlpha = 0.4f;
        [Range(0f, 1f)] public float lineAlpha = 0.7f;
        [Tooltip("How far the label slides away from the ember (px); centred labels stay put and the embers sit this much further out instead")]
        public float nudge = 14f;
        [Tooltip("Distance from the edge of the label box (or of the text, when centred) to the ember (px)")]
        public float gap = 10f;
        [Tooltip("The ember slides in from this much further out (px)")]
        public float slideIn = 10f;
        public float sparkRise = 28f;
        public float inTime = 0.18f;
        public float outTime = 0.25f;
        public float flareTime = 0.3f;

        Button button;
        bool hovered, dirty = true;
        float t, flare, seed;
        Vector2 labelHome;
        Color labelRest;
        float[] markerX = new float[0];
        static float lastHoverSound = -1f;

        void Awake()
        {
            button = GetComponent<Button>();
            if (label != null)
            {
                labelHome = label.rectTransform.anchoredPosition;
                labelRest = label.color;
            }
            seed = UnityEngine.Random.value * 100f;
            // not saved with the scene, so set here: the pause menu mutes everything through AudioListener.pause
            if (sfx != null) sfx.ignoreListenerPause = true;
        }

        void Start()
        {
            Layout();
            Apply(GameClock.UnscaledTime);
        }

        void OnDisable()
        {
            // a panel hidden while one of its buttons was hovered must not come back lit
            hovered = false;
            t = 0f;
            flare = 0f;
            dirty = true;
            Apply(GameClock.UnscaledTime);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (button != null && !button.IsInteractable()) return;
            hovered = true;
            float now = GameClock.UnscaledTime;
            if (now - lastHoverSound > 0.06f)
            {
                lastHoverSound = now;
                Play(hoverClip, hoverVolume, UnityEngine.Random.Range(1.1f, 1.2f));
            }
        }

        public void OnPointerExit(PointerEventData eventData) => hovered = false;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || (button != null && !button.IsInteractable())) return;
            flare = 1f;
            Play(clickClip, clickVolume, 0.9f);
        }

        void Update()
        {
            float dt = GameClock.UnscaledDeltaTime;
            float target = hovered ? 1f : 0f;
            if (t != target) t = Mathf.MoveTowards(t, target, dt / Mathf.Max(0.01f, hovered ? inTime : outTime));
            if (flare > 0f) flare = Mathf.Max(0f, flare - dt / Mathf.Max(0.01f, flareTime));
            bool active = t > 0f || flare > 0f;
            if (!active && !dirty) return;
            Apply(GameClock.UnscaledTime);
            dirty = active; // one more pass after it settles, to leave everything exactly at rest
        }

        /// <summary>Places the ember(s) just outside the text: at the label box edge, or around the text itself when centred.</summary>
        void Layout()
        {
            if (markers == null) return;
            markerX = new float[markers.Length];
            if (label == null) return;
            float boxHalf = label.rectTransform.rect.width * 0.5f;
            // left/right labels hug their box edge, so the ember sits just outside the box
            // same final spacing on every layout: a left/right label slides `nudge` away, a centred one does not move
            float distance = side == Side.Center ? Mathf.Min(label.preferredWidth * 0.5f, boxHalf) + gap + nudge : boxHalf + gap;
            for (int i = 0; i < markers.Length; i++) markerX[i] = labelHome.x + Outward(i) * distance;
        }

        float Outward(int marker) => side == Side.Left ? -1f : side == Side.Right ? 1f : (marker == 0 ? -1f : 1f);

        void Apply(float now)
        {
            float e = 1f - (1f - t) * (1f - t) * (1f - t); // ease-out cubic
            float pop = EaseOutBack(t);

            if (label != null)
            {
                float away = side == Side.Left ? 1f : side == Side.Right ? -1f : 0f;
                label.rectTransform.anchoredPosition = labelHome + new Vector2(away * nudge * e, 0f);
                label.color = Color.Lerp(Color.Lerp(labelRest, hoverText, e), Color.white, flare * 0.7f);
            }

            // the band and rule grow out from the text side; a negative x scale mirrors them for right-aligned labels
            float sweep = Mathf.Lerp(0.35f, 1f, e);
            float mirror = side == Side.Right ? -1f : 1f;
            if (band != null)
            {
                band.rectTransform.localScale = new Vector3(mirror * sweep, 1f, 1f);
                band.color = Alpha(glow, bandAlpha * e + 0.18f * flare);
            }
            if (line != null)
            {
                line.rectTransform.localScale = new Vector3(mirror * sweep, 1f, 1f);
                line.color = Alpha(glow, lineAlpha * e);
            }

            if (markers == null) return;
            for (int i = 0; i < markers.Length; i++)
            {
                var m = markers[i];
                if (m == null || m.root == null) continue;
                float flick = Mathf.PerlinNoise(now * 7f, seed + i * 13.1f);
                float x = i < markerX.Length ? markerX[i] : 0f;
                m.root.anchoredPosition = new Vector2(x + Outward(i) * slideIn * (1f - e), 0f);
                if (m.ember != null)
                {
                    m.ember.rectTransform.localScale = Vector3.one * (pop * (0.94f + 0.12f * flick) + 0.5f * flare);
                    m.ember.color = Alpha(emberColor, e);
                }
                if (m.halo != null)
                {
                    m.halo.rectTransform.localScale = Vector3.one * (0.75f + 0.25f * flick + 0.9f * flare);
                    m.halo.color = Alpha(glow, e * (0.55f + 0.35f * flick) + 0.4f * flare);
                }
                if (m.sparks == null) continue;
                for (int s = 0; s < m.sparks.Length; s++)
                {
                    var sp = m.sparks[s];
                    if (sp == null) continue;
                    float life = 1.0f + 0.2f * s;
                    float ph = Mathf.Repeat(now / life + (float)s / m.sparks.Length + i * 0.37f, 1f);
                    float sway = Mathf.Sin((now + s * 1.7f + i) * 5f) * 3f;
                    sp.rectTransform.anchoredPosition = new Vector2(sway + (s - 1) * 3f, 2f + ph * sparkRise);
                    sp.rectTransform.localScale = Vector3.one * (1f - 0.6f * ph);
                    sp.color = Alpha(emberColor, e * Mathf.Sin(ph * Mathf.PI));
                }
            }
        }

        void Play(AudioClip clip, float volume, float pitch)
        {
            if (sfx == null || clip == null) return;
            sfx.pitch = pitch;
            sfx.PlayOneShot(clip, volume);
        }

        static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, Mathf.Clamp01(a));

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float k = x - 1f;
            return 1f + c3 * k * k * k + c1 * k * k;
        }
    }
}
