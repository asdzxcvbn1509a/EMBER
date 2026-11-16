using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Ember.EditorTools
{
    /// <summary>Builds the uGUI/TextMeshPro interfaces in code: stage HUD, pause, title menu, ending credits.</summary>
    public static class UIFactory
    {
        public static readonly Color Amber = new Color(1f, 0.72f, 0.38f);
        public static readonly Color Pale = new Color(0.9f, 0.88f, 0.84f);
        public static readonly Color Dim = new Color(0.62f, 0.62f, 0.66f);
        public static readonly Color Cyan = new Color(0.55f, 0.88f, 1f);

        static TMP_FontAsset font;

        public static TMP_FontAsset Font()
        {
            if (font != null) return font;
            string path = $"{EmberPaths.Fonts}/Futura_SDF.asset";
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font != null) return font;
            var ttf = AssetDatabase.LoadAssetAtPath<Font>(EmberPaths.ClassS3 + "/Fonts/Futura Bold.TTF");
            if (ttf != null)
            {
                font = TMP_FontAsset.CreateFontAsset(ttf, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                if (font != null)
                {
                    font.name = "Futura_SDF";
                    EmberPaths.Ensure(EmberPaths.Fonts);
                    AssetDatabase.CreateAsset(font, path);
                    font.atlasTextures[0].name = "Futura_SDF Atlas";
                    AssetDatabase.AddObjectToAsset(font.atlasTextures[0], font);
                    font.material.name = "Futura_SDF Material";
                    AssetDatabase.AddObjectToAsset(font.material, font);
                    font.TryAddCharacters(" ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.,:;!?'\"-/()[]+&%#@*<>=_|·—–→←↑↓");
                    EditorUtility.SetDirty(font);
                    AssetDatabase.SaveAssets();
                    return font;
                }
            }
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            return font;
        }

        /// <summary>Glowing TMP material for words carved into the floor (bloom picks up the HDR face colour).</summary>
        public static Material EngraveMaterial(Color hdr)
        {
            string path = $"{EmberPaths.Fonts}/Futura_Engrave_{ColorUtility.ToHtmlStringRGB(hdr)}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Font().material);
            m.SetColor("_FaceColor", hdr);
            if (m.HasProperty("_GlowColor"))
            {
                m.EnableKeyword("GLOW_ON");
                m.SetColor("_GlowColor", new Color(hdr.r, hdr.g, hdr.b, 0.5f));
                m.SetFloat("_GlowPower", 0.35f);
                m.SetFloat("_GlowOuter", 0.3f);
            }
            EmberPaths.Ensure(EmberPaths.Fonts);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ------------------------------------------------------------------ primitives
        static GameObject Canvas(string name, int order)
        {
            var go = new GameObject(name);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920f, 1080f);
            s.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        public static void EventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static RectTransform Stretch(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color,
            TextAlignmentOptions align, Vector2 anchor, Vector2 pos, Vector2 box, float spacing = 0f)
        {
            var rt = Rect(parent, name, anchor, new Vector2(anchor.x, anchor.y), pos, box);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = Font();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.characterSpacing = spacing;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }

        static Image Img(Transform parent, string name, Color color, Vector2 anchor, Vector2 pos, Vector2 size, Sprite sprite = null)
        {
            var rt = Rect(parent, name, anchor, new Vector2(0.5f, 0.5f), pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }

        static Button Btn(Transform parent, string name, string label, UIButtonAction.Action action, Vector2 anchor, Vector2 pos,
            Vector2 size, GameObject panel = null, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var rt = Rect(parent, name, anchor, new Vector2(anchor.x, 0.5f), pos, size);
            var bg = rt.gameObject.AddComponent<Image>();
            // 0.21 = the old 0.35 plate under the old 0.6 rest tint, so buttons look the same when not hovered
            bg.color = new Color(0.04f, 0.04f, 0.08f, 0.21f);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            // a colour tint on a near-black plate was barely visible; MenuButtonFX draws the hover state instead
            b.transition = Selectable.Transition.None;
            var t = Text(rt, "Label", label, 26f, Pale, align, new Vector2(0.5f, 0.5f), Vector2.zero, size - new Vector2(40f, 0f), 8f);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            var act = rt.gameObject.AddComponent<UIButtonAction>();
            act.action = action;
            act.panel = panel;
            HoverFX(rt, t, size, align);
            return b;
        }

        /// <summary>
        /// Parts for MenuButtonFX, all invisible until hovered: a light band and a thin rule that sweep out from
        /// the text side, and an ember with a halo and sparks beside the text (one on each side for centred labels).
        /// </summary>
        static void HoverFX(RectTransform button, TextMeshProUGUI label, Vector2 size, TextAlignmentOptions align)
        {
            var side = align == TextAlignmentOptions.Right ? MenuButtonFX.Side.Right
                : align == TextAlignmentOptions.Center ? MenuButtonFX.Side.Center
                : MenuButtonFX.Side.Left;
            bool centred = side == MenuButtonFX.Side.Center;
            var fx = button.gameObject.AddComponent<MenuButtonFX>();
            fx.side = side;
            fx.label = label;
            var clear = new Color(1f, 1f, 1f, 0f);

            // band and rule start at the text edge; MenuButtonFX scales them in x (mirrored for right-aligned labels).
            // The band is soft light (feathered, a little taller than the plate); the rule is a crisp line along its bottom.
            float edgeX = side == MenuButtonFX.Side.Left ? 0f : side == MenuButtonFX.Side.Right ? 1f : 0.5f;
            var pivot = new Vector2(centred ? 0.5f : 0f, 0.5f);
            var rest = new Vector3(side == MenuButtonFX.Side.Right ? -0.35f : 0.35f, 1f, 1f);
            // a left/right band starts 24 px outside the plate so its soft ramp-in peaks at the ember, not at a hard edge
            float lead = centred ? 0f : 24f;
            fx.band = Img(button, "HoverBand", clear, new Vector2(edgeX, 0.5f), new Vector2(side == MenuButtonFX.Side.Right ? lead : -lead, 0f),
                size + new Vector2(lead, 12f), Sprite(centred ? "T_HoverC" : "T_HoverH"));
            fx.band.rectTransform.pivot = pivot;
            fx.band.rectTransform.localScale = rest;
            fx.line = Img(button, "HoverLine", clear, new Vector2(edgeX, 0f), new Vector2(0f, 1f), new Vector2(size.x, 2f), Sprite(centred ? "T_FadeC" : "T_FadeH"));
            fx.line.rectTransform.pivot = pivot;
            fx.line.rectTransform.localScale = rest;

            var glow = Sprite("T_UIGlow");
            fx.markers = new MenuButtonFX.Marker[centred ? 2 : 1];
            for (int i = 0; i < fx.markers.Length; i++)
            {
                var root = Rect(button, centred ? (i == 0 ? "EmberL" : "EmberR") : "Ember", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20f, 20f));
                var m = new MenuButtonFX.Marker { root = root, sparks = new Image[3] };
                m.halo = Img(root, "Halo", clear, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(76f, 76f), glow);
                for (int s = 0; s < m.sparks.Length; s++)
                    m.sparks[s] = Img(root, "Spark" + s, clear, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f), glow);
                m.ember = Img(root, "Core", clear, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, 18f), Sprite("T_Diamond"));
                m.ember.rectTransform.localScale = Vector3.zero;
                fx.markers[i] = m;
            }
            label.transform.SetAsLastSibling();

            fx.sfx = UiAudio(button);
            fx.hoverClip = fx.clickClip = EmberPaths.Clip("sfx_ui");
        }

        /// <summary>One 2D source per canvas for menu sounds (MenuButtonFX makes it ignore AudioListener.pause at runtime).</summary>
        static AudioSource UiAudio(Transform child)
        {
            var canvas = child.GetComponentInParent<Canvas>();
            var host = canvas != null ? canvas.gameObject : child.gameObject;
            var src = host.GetComponent<AudioSource>();
            if (src != null) return src;
            src = host.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.bypassReverbZones = true;
            return src;
        }

        static ScreenFader Fader(Transform canvas)
        {
            var rt = Stretch(canvas, "Fader");
            var img = rt.gameObject.AddComponent<Image>();
            img.color = Color.black;
            img.raycastTarget = false;
            rt.gameObject.AddComponent<CanvasGroup>();
            var f = rt.gameObject.AddComponent<ScreenFader>();
            f.image = img;
            return f;
        }

        // ------------------------------------------------------------------ stage HUD
        public struct StageUIRefs { public Hud hud; public ScreenFader fader; }

        public static StageUIRefs StageUI()
        {
            EventSystem();
            var canvas = Canvas("UI", 10).transform;
            var hud = canvas.gameObject.AddComponent<Hud>();

            // stage title card
            var card = Stretch(canvas, "StageCard");
            hud.stageCard = card.gameObject.AddComponent<CanvasGroup>();
            hud.stageTitle = Text(card, "Title", "", 66f, Amber, TextAlignmentOptions.Center, new Vector2(0.5f, 0.36f), Vector2.zero, new Vector2(1600f, 100f), 22f);
            Img(card, "Rule", new Color(1f, 0.72f, 0.38f, 0.6f), new Vector2(0.5f, 0.36f), new Vector2(0f, -58f), new Vector2(420f, 2f));
            hud.stageSubtitle = Text(card, "Subtitle", "", 24f, Pale, TextAlignmentOptions.Center, new Vector2(0.5f, 0.36f), new Vector2(0f, -94f), new Vector2(1400f, 60f), 14f);

            // shard counter (top right)
            var shard = Rect(canvas, "Shards", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-48f, -40f), new Vector2(220f, 60f));
            hud.shardGroup = shard.gameObject.AddComponent<CanvasGroup>();
            Img(shard, "Icon", Cyan, new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(26f, 40f), Sprite("T_Diamond"));
            hud.shardText = Text(shard, "Count", "0 / 3", 30f, Pale, TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(150f, 0f), new Vector2(200f, 50f), 6f);

            // toast (top centre)
            var toast = Rect(canvas, "Toast", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(900f, 60f));
            hud.toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
            hud.toastText = Text(toast, "Text", "", 24f, Amber, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 60f), 16f);

            // controls hint (bottom left, stage 1 only)
            var hint = Rect(canvas, "ControlsHint", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(48f, 40f), new Vector2(1300f, 50f));
            hud.controlsHint = hint.gameObject.AddComponent<CanvasGroup>();
            Text(hint, "Text", "W A S D  ROLL     SPACE  JUMP     R  CHECKPOINT     ESC  PAUSE     F  PHOTO MODE", 20f, Dim,
                TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(650f, 0f), new Vector2(1300f, 50f), 6f);

            // photo mode hint
            var photo = Text(canvas, "PhotoHint", "PHOTO MODE   -   W A S D FLY   -   MOUSE LOOK   -   F RETURN", 22f, Pale,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(1400f, 50f), 10f);
            hud.photoHint = photo.gameObject;

            // pause panel
            var pause = Stretch(canvas, "PausePanel");
            var dim = pause.gameObject.AddComponent<Image>();
            dim.color = new Color(0.01f, 0.01f, 0.03f, 0.72f);
            Text(pause, "Paused", "PAUSED", 60f, Amber, TextAlignmentOptions.Center, new Vector2(0.5f, 0.66f), Vector2.zero, new Vector2(800f, 90f), 30f);
            float y = 40f;
            Btn(pause, "Resume", "RESUME", UIButtonAction.Action.Resume, new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(560f, 64f), null, TextAlignmentOptions.Center);
            Btn(pause, "Restart", "RESTART AT CHECKPOINT", UIButtonAction.Action.RestartCheckpoint, new Vector2(0.5f, 0.5f), new Vector2(0f, y - 80f), new Vector2(560f, 64f), null, TextAlignmentOptions.Center);
            Btn(pause, "Menu", "MAIN MENU", UIButtonAction.Action.MainMenu, new Vector2(0.5f, 0.5f), new Vector2(0f, y - 160f), new Vector2(560f, 64f), null, TextAlignmentOptions.Center);
            Btn(pause, "Quit", "QUIT GAME", UIButtonAction.Action.Quit, new Vector2(0.5f, 0.5f), new Vector2(0f, y - 240f), new Vector2(560f, 64f), null, TextAlignmentOptions.Center);
            hud.pausePanel = pause.gameObject;

            var fader = Fader(canvas);
            return new StageUIRefs { hud = hud, fader = fader };
        }

        static Sprite Sprite(string tex)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>($"{EmberPaths.Textures}/{tex}.png");
        }

        /// <summary>Loads a PNG as a sprite, importing it / fixing its importer first if needed.</summary>
        static Sprite EnsureSprite(string path)
        {
            if (!System.IO.File.Exists(path)) return null;
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) return null;
            }
            if (ti.textureType != TextureImporterType.Sprite || ti.spriteImportMode != SpriteImportMode.Single)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>A full-width credits paragraph whose height is driven by the scroller's layout group.</summary>
        static TextMeshProUGUI CreditBlock(Transform parent, string name, string text)
        {
            return Text(parent, name, text, 28f, Pale, TextAlignmentOptions.Top, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(900f, 100f), 3f);
        }

        // ------------------------------------------------------------------ title
        public static ScreenFader TitleUI()
        {
            EventSystem();
            var canvas = Canvas("UI", 10).transform;
            Text(canvas, "Tagline", "THE LAST EMBER", 26f, Pale, TextAlignmentOptions.Center, new Vector2(0.5f, 0.86f), Vector2.zero, new Vector2(900f, 50f), 28f);

            var menu = Rect(canvas, "Menu", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(110f, 120f), new Vector2(520f, 380f));
            var select = Stretch(canvas, "StageSelect");
            var how = Stretch(canvas, "HowToPlay");

            Btn(menu, "NewGame", "NEW GAME", UIButtonAction.Action.NewGame, new Vector2(0f, 1f), new Vector2(0f, -40f), new Vector2(480f, 62f));
            Btn(menu, "Select", "STAGE SELECT", UIButtonAction.Action.ShowPanel, new Vector2(0f, 1f), new Vector2(0f, -120f), new Vector2(480f, 62f), select.gameObject);
            Btn(menu, "How", "HOW TO PLAY", UIButtonAction.Action.ShowPanel, new Vector2(0f, 1f), new Vector2(0f, -200f), new Vector2(480f, 62f), how.gameObject);
            Btn(menu, "Quit", "QUIT", UIButtonAction.Action.Quit, new Vector2(0f, 1f), new Vector2(0f, -280f), new Vector2(480f, 62f));

            // stage select panel
            var sdim = select.gameObject.AddComponent<Image>();
            sdim.color = new Color(0.01f, 0.01f, 0.03f, 0.8f);
            Text(select, "Head", "SELECT STAGE", 46f, Amber, TextAlignmentOptions.Center, new Vector2(0.5f, 0.74f), Vector2.zero, new Vector2(900f, 80f), 24f);
            Btn(select, "S1", "I  -  THE SUNKEN CRYPT", UIButtonAction.Action.Stage1, new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(720f, 66f), null, TextAlignmentOptions.Center);
            Btn(select, "S2", "II  -  THE EMBER FORGE", UIButtonAction.Action.Stage2, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(720f, 66f), null, TextAlignmentOptions.Center);
            Btn(select, "S3", "III  -  THE SKY SANCTUM", UIButtonAction.Action.Stage3, new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(720f, 66f), null, TextAlignmentOptions.Center);
            Btn(select, "Back", "BACK", UIButtonAction.Action.HidePanel, new Vector2(0.5f, 0.5f), new Vector2(0f, -190f), new Vector2(300f, 60f), select.gameObject, TextAlignmentOptions.Center);
            select.gameObject.SetActive(false);

            // how to play panel
            var hdim = how.gameObject.AddComponent<Image>();
            hdim.color = new Color(0.01f, 0.01f, 0.03f, 0.85f);
            Text(how, "Head", "HOW TO PLAY", 46f, Amber, TextAlignmentOptions.Center, new Vector2(0.5f, 0.8f), Vector2.zero, new Vector2(900f, 80f), 24f);
            Text(how, "Body",
                "You are the last ember of a fallen temple.\nRoll onto glowing rune plates to light lanterns, raise bridges and open gates.\n" +
                "Some things you cannot press yourself - release weights, rafts and stones and let them do it.\nLight the three lanterns of dawn and rekindle the beacon.\n\n" +
                "W A S D / ARROWS   roll        SPACE   jump\nR   back to checkpoint        ESC   pause        F   photo mode",
                26f, Pale, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(1300f, 420f), 2f);
            Btn(how, "Back", "BACK", UIButtonAction.Action.HidePanel, new Vector2(0.5f, 0.5f), new Vector2(0f, -300f), new Vector2(300f, 60f), how.gameObject, TextAlignmentOptions.Center);
            how.gameObject.SetActive(false);

            Text(canvas, "Footer", $"{ProjectInfo.Course.ToUpperInvariant()}  -  {ProjectInfo.Year}   -   MECHANICS: CLASS CONDITION-ACTION PACKAGE   -   MODELS MADE IN BLENDER   -   UNITY 6",
                15f, Dim, TextAlignmentOptions.Right, new Vector2(1f, 0f), new Vector2(-40f, 34f), new Vector2(1700f, 40f), 4f);
            return Fader(canvas);
        }

        // ------------------------------------------------------------------ ending
        public static ScreenFader EndingUI()
        {
            EventSystem();
            var canvas = Canvas("UI", 10).transform;
            var credits = canvas.gameObject.AddComponent<EndingCredits>();
            // credits run in a shaded column on the left so the sunrise and the lighthouse stay clear on the right
            const float colX = 500f, colW = 900f;
            Img(canvas, "CreditsShade", new Color(0.03f, 0.02f, 0.05f, 0.78f), new Vector2(0f, 0.5f), new Vector2(colX, 0f), new Vector2(1000f, 1080f));
            Img(canvas, "CreditsShadeEdge", new Color(0.03f, 0.02f, 0.05f, 0.78f), new Vector2(0f, 0.5f), new Vector2(1160f, 0f), new Vector2(320f, 1080f), Sprite("T_FadeH"));
            credits.statsText = Text(canvas, "Stats", "", 24f, Amber, TextAlignmentOptions.Center, new Vector2(0f, 1f), new Vector2(colX - colW * 0.5f, -60f), new Vector2(colW, 50f), 14f);

            var mask = Rect(canvas, "CreditsMask", new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(colX, -20f), new Vector2(colW, 760f));
            mask.gameObject.AddComponent<RectMask2D>();
            // the scroller stacks its blocks and sizes itself; EndingCredits scrolls it up from just below the mask
            var scroller = Rect(mask, "Scroller", new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(colW, 1900f));
            var stack = scroller.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.childAlignment = TextAnchor.UpperCenter;
            stack.childControlWidth = true;
            stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;
            scroller.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreditBlock(scroller, "Head", "<size=110><color=#FFB861>EMBER</color></size>\n<size=30>THE LAST EMBER</size>\n\n\n<color=#FFB861>DEVELOPED BY</color>");
            var team = EnsureSprite(ProjectInfo.TeamCreditsImage);
            if (team != null)
            {
                // rendered at 2x for crisp Thai glyphs, shown at 1x
                var size = new Vector2(team.rect.width, team.rect.height) * 0.5f;
                size *= Mathf.Min(1f, colW / size.x);
                var img = Img(scroller, "Team", Pale, new Vector2(0.5f, 1f), Vector2.zero, size, team);
                img.preserveAspect = true;
                var le = img.gameObject.AddComponent<LayoutElement>();
                le.minHeight = le.preferredHeight = size.y;
            }
            else
            {
                Debug.LogWarning("[Ember] team credits image missing - run Tools/render_team_credits.ps1");
            }
            CreditBlock(scroller, "Body",
                "<size=22><color=#C9C4D6>" + ProjectInfo.Course.ToUpperInvariant() + "  -  " + ProjectInfo.Year + "</color></size>\n\n\n" +
                "<color=#FFB861>MECHANICS</color>\nClass Unity package - Condition-Action scripts\nHitAndShowHide   HitAndPlayAnimation   HitAndClone\nHitAndDestroy   HitAndSetTransform   HitAndLoadScene\nBallController   CameraFollow   LookAtTarget   ScrollingUVs   Fly\n\n" +
                "<color=#FFB861>ART</color>\nProps modelled in Blender (procedural Python)\nStone, lava and wood textures from the class packages\nLanterns and candles from Session 2\n\n" +
                "<color=#FFB861>SOUND</color>\nAll sound effects and music synthesised in code\n\n" +
                "<color=#FFB861>MADE WITH</color>\nUnity 6   Universal Render Pipeline   Blender 5\n\n\n\n" +
                "<size=40><color=#FFB861>THANK YOU FOR PLAYING</color></size>");
            scroller.anchoredPosition = Vector2.zero;
            credits.scroller = scroller;

            var buttons = Rect(canvas, "Buttons", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60f, 60f), new Vector2(420f, 160f));
            credits.buttons = buttons.gameObject.AddComponent<CanvasGroup>();
            // the sunrise behind them is bright, so these buttons get a darker plate than the menu's
            // (0.51 = the old 0.85 plate under the old 0.6 rest tint)
            foreach (var b in new[] {
                Btn(buttons, "Menu", "MAIN MENU", UIButtonAction.Action.MainMenu, new Vector2(1f, 1f), new Vector2(0f, -30f), new Vector2(380f, 60f), null, TextAlignmentOptions.Right),
                Btn(buttons, "Quit", "QUIT", UIButtonAction.Action.Quit, new Vector2(1f, 1f), new Vector2(0f, -105f), new Vector2(380f, 60f), null, TextAlignmentOptions.Right) })
                ((Image)b.targetGraphic).color = new Color(0.04f, 0.03f, 0.07f, 0.51f);
            return Fader(canvas);
        }
    }
}
