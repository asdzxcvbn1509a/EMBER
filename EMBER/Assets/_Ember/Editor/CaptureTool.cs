using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ember.EditorTools
{
    /// <summary>
    /// Renders documentation images from the generated scenes (edit mode):
    ///  * a top-down level map per stage with numbered beat markers,
    ///  * a hero shot per beat (camera framing stored on each LevelBeat), with post-processing.
    /// </summary>
    public static class CaptureTool
    {
        public static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Docs"));

        public static void Run()
        {
            Directory.CreateDirectory(Path.Combine(OutDir, "LevelMaps"));
            Directory.CreateDirectory(Path.Combine(OutDir, "Screenshots"));
            foreach (var name in Pipeline.SceneOrder)
            {
                string path = EmberPaths.Scene(name);
                if (!File.Exists(path)) continue;
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                Prepare();
                if (name.StartsWith("Stage")) Map(name);
                HeroShots(name);
                if (name == "Title" || name == "Ending") UiShot(name);
            }
        }

        static void Prepare()
        {
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>())
                if (ps.main.loop) ps.Simulate(4f, true, true, true);
            foreach (var p in Object.FindObjectsByType<ReflectionProbe>())
                p.RenderProbe();
            var gm = Object.FindAnyObjectByType<GameManager>();
            if (gm != null) GameManager.SetAmbient(gm.ambient);
        }

        static Texture2D Render(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = prevTarget;
            rt.Release();
            Object.DestroyImmediate(rt);
            return tex;
        }

        static void Save(Texture2D tex, string file)
        {
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("[Ember] captured " + file);
        }

        static Camera TempCamera(bool post)
        {
            var go = new GameObject("CaptureCamera");
            var cam = go.AddComponent<Camera>();
            cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 600f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = post;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            return cam;
        }

        /// <summary>
        /// Menu / credits screenshot: the overlay canvas is switched to camera space for one render (the scene is not
        /// saved), the black fader is hidden and the ending credits are scrolled to the team names.
        /// </summary>
        static void UiShot(string scene)
        {
            var main = Camera.main;
            var canvas = Object.FindObjectsByType<Canvas>().FirstOrDefault(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay);
            if (main == null || canvas == null) return;
            var cam = TempCamera(true);
            cam.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            cam.fieldOfView = main.fieldOfView;
            Object.DestroyImmediate(Render(cam, 320, 180));
            var fader = canvas.GetComponentInChildren<ScreenFader>(true);
            var fadeGroup = fader != null ? fader.GetComponent<CanvasGroup>() : null;
            float fadeAlpha = fadeGroup != null ? fadeGroup.alpha : 0f;
            if (fadeGroup != null) fadeGroup.alpha = 0f;
            var credits = canvas.GetComponent<EndingCredits>();
            Vector2 scroll = Vector2.zero;
            if (credits != null && credits.scroller != null)
            {
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(credits.scroller);
                scroll = credits.scroller.anchoredPosition;
                var window = (RectTransform)credits.scroller.parent;
                credits.scroller.anchoredPosition = new Vector2(scroll.x, window.rect.height + 180f);
                if (credits.buttons != null) credits.buttons.alpha = 1f;
            }
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
            Save(Render(cam, 1920, 1080), Path.Combine(OutDir, "Screenshots", scene + "_UI.png"));
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
            if (credits != null && credits.scroller != null) credits.scroller.anchoredPosition = scroll;
            if (fadeGroup != null) fadeGroup.alpha = fadeAlpha;
            Object.DestroyImmediate(cam.gameObject);
        }

        static void Map(string stage)
        {
            var level = GameObject.Find("LEVEL");
            if (level == null) return;
            var rends = level.transform.Find("Geometry").GetComponentsInChildren<Renderer>()
                .Concat(level.transform.Find("Puzzles").GetComponentsInChildren<Renderer>()).ToList();
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            // map look: no fog, flat bright ambient, no post-processing
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            GameManager.SetAmbient(new Color(0.55f, 0.55f, 0.6f));
            var temp = new List<GameObject>();
            var markerMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            markerMat.SetColor("_BaseColor", new Color(0.08f, 0.07f, 0.1f));
            foreach (var beat in Object.FindObjectsByType<LevelBeat>())
            {
                Vector3 p = beat.transform.position + Vector3.up * 14f;
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(disc.GetComponent<Collider>());
                disc.transform.position = p;
                disc.transform.localScale = new Vector3(5.2f, 0.05f, 5.2f);
                disc.GetComponent<Renderer>().sharedMaterial = markerMat;
                var label = new GameObject("MapLabel").AddComponent<TextMeshPro>();
                label.font = UIFactory.Font();
                label.text = beat.code;
                label.fontSize = 18f;
                label.color = new Color(1f, 0.7f, 0.32f);
                label.alignment = TextAlignmentOptions.Center;
                label.rectTransform.sizeDelta = new Vector2(10f, 5f);
                label.transform.position = p + Vector3.up * 0.2f;
                label.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                temp.Add(disc);
                temp.Add(label.gameObject);
            }
            var cam = TempCamera(false);
            cam.orthographic = true;
            float aspect = Mathf.Clamp(b.size.x / Mathf.Max(1f, b.size.z), 0.35f, 2.5f);
            int h = 2400, w = Mathf.RoundToInt(h * aspect);
            cam.orthographicSize = b.size.z * 0.5f + 4f;
            cam.transform.position = new Vector3(b.center.x, b.max.y + 60f, b.center.z);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.backgroundColor = new Color(0.06f, 0.06f, 0.08f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            Save(Render(cam, w, h), Path.Combine(OutDir, "LevelMaps", stage + "_Map.png"));
            Object.DestroyImmediate(cam.gameObject);
            foreach (var t in temp) Object.DestroyImmediate(t);
            RenderSettings.fog = fog;
        }

        static void HeroShots(string scene)
        {
            var beats = Object.FindObjectsByType<LevelBeat>().OrderBy(x => x.code).ToList();
            var cam = TempCamera(true);
            // warm-up: the first render after opening a scene can miss reflection probes and fresh shader
            // variants (metal and emissive surfaces came out dark), so render once and throw it away
            Object.DestroyImmediate(Render(cam, 320, 180));
            if (beats.Count == 0)
            {
                var main = Camera.main;
                if (main != null)
                {
                    cam.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
                    cam.fieldOfView = main.fieldOfView;
                    Save(Render(cam, 1920, 1080), Path.Combine(OutDir, "Screenshots", scene + ".png"));
                }
            }
            foreach (var beat in beats)
            {
                Vector3 pos = beat.transform.position + beat.shotOffset;
                cam.transform.position = pos;
                cam.transform.rotation = Quaternion.LookRotation(beat.transform.position + beat.shotLookOffset - pos);
                cam.fieldOfView = beat.shotFov;
                Save(Render(cam, 1920, 1080), Path.Combine(OutDir, "Screenshots", $"{scene}_{beat.code}.png"));
            }
            // gameplay view from the follow camera
            var rig = GameObject.Find("CameraRig");
            if (rig != null && scene.StartsWith("Stage"))
            {
                var main = rig.GetComponentInChildren<Camera>();
                cam.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
                cam.fieldOfView = main.fieldOfView;
                Save(Render(cam, 1920, 1080), Path.Combine(OutDir, "Screenshots", $"{scene}_Start.png"));
            }
            Object.DestroyImmediate(cam.gameObject);
        }
    }
}
