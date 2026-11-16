using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Ember.EditorTools
{
    /// <summary>Per-stage look (the colour script) and the shared scene skeleton.</summary>
    public class Theme
    {
        public string sky = "M_Sky_Crypt";
        public Color fogColor = new Color(0.03f, 0.035f, 0.07f);
        public float fogDensity = 0.02f;
        public Color ambient = new Color(0.08f, 0.09f, 0.14f);
        public Color sunColor = new Color(0.45f, 0.55f, 0.9f);
        public float sunIntensity = 0.3f;
        public Vector3 sunEuler = new Vector3(55f, -30f, 0f);
        public string profile = "PP_Crypt";
        public string ambience = "amb_crypt";
        public float ambienceVolume = 0.55f;
        public string music;
        public float musicVolume = 0.45f;
        public AudioReverbPreset reverb = AudioReverbPreset.Cave;
    }

    public static class StageCommon
    {
        public struct Refs
        {
            public GameManager gm;
            public GameObject player;
            public CameraFollow follow;
            public Camera cam;
            public Light sun;
            public Volume volume;
        }

        public static Scene NewScene()
        {
            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        public static void Save(Scene scene, string name)
        {
            EmberPaths.Ensure(EmberPaths.Scenes);
            Lightmapping.lightingSettings = LightingFor(name);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, EmberPaths.Scene(name));
        }

        static LightingSettings LightingFor(string name)
        {
            string path = $"{EmberPaths.Settings}/Lighting_Realtime.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (ls == null)
            {
                ls = new LightingSettings { name = "Lighting_Realtime", bakedGI = false, realtimeGI = false };
                EmberPaths.Ensure(EmberPaths.Settings);
                AssetDatabase.CreateAsset(ls, path);
            }
            return ls;
        }

        public static Refs Setup(int stageNumber, string title, string subtitle, Theme theme, Vector3 spawn, Vector3 camOffset, float fov, bool followCamera = true)
        {
            var refs = new Refs();

            // ---- render settings (fog, sky, ambient) -----------------------------------
            RenderSettings.skybox = EmberPaths.Mat(theme.sky);
            RenderSettings.fog = theme.fogDensity > 0f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = theme.fogColor;
            RenderSettings.fogDensity = theme.fogDensity;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = theme.ambient;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.7f;

            // ---- key light ---------------------------------------------------------------
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = theme.sunColor;
            sun.intensity = theme.sunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sunGo.transform.rotation = Quaternion.Euler(theme.sunEuler);
            RenderSettings.sun = sun;
            refs.sun = sun;

            // ---- post-processing ---------------------------------------------------------
            var volGo = new GameObject("PostProcess");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0;
            vol.sharedProfile = VolumeFactory.Get(theme.profile);
            refs.volume = vol;

            // ---- player ------------------------------------------------------------------
            var player = (GameObject)PrefabUtility.InstantiatePrefab(EmberPaths.Prefab("Player"));
            player.name = "Player";
            player.transform.position = spawn + Vector3.up * 0.6f;
            refs.player = player;

            // ---- camera rig: class CameraFollow + our CameraRig + PhotoMode (class Fly.cs) ----
            var rig = new GameObject("CameraRig");
            rig.transform.position = player.transform.position + camOffset;
            rig.transform.rotation = Quaternion.LookRotation(player.transform.position - rig.transform.position);
            if (followCamera)
            {
                var follow = rig.AddComponent<CameraFollow>();
                follow.target = player.transform;
                follow.offset = camOffset;
                follow.useSmoothFollow = true;
                follow.smoothFollow = 4f;
                refs.follow = follow;
                rig.AddComponent<CameraRig>();
                var fly = rig.AddComponent<Fly>();
                fly.enabled = false;
                rig.AddComponent<PhotoMode>();
            }
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(rig.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 450f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            camGo.AddComponent<AudioListener>();
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.dithering = true;
            var cr = rig.GetComponent<CameraRig>();
            if (cr != null) cr.cam = cam;
            refs.cam = cam;

            // ---- UI + game manager -------------------------------------------------------
            ScreenFader fader;
            Hud hud = null;
            if (stageNumber == 0) fader = UIFactory.TitleUI();
            else if (stageNumber == 4) fader = UIFactory.EndingUI();
            else
            {
                var ui = UIFactory.StageUI();
                fader = ui.fader;
                hud = ui.hud;
            }
            var gmGo = new GameObject("GameManager");
            var gm = gmGo.AddComponent<GameManager>();
            gm.stageNumber = stageNumber;
            gm.stageTitle = title;
            gm.stageSubtitle = subtitle;
            gm.ambient = theme.ambient;
            gm.player = player.transform;
            gm.fader = fader;
            gm.hud = hud;
            refs.gm = gm;

            // ---- audio -------------------------------------------------------------------
            Loop("Ambience", theme.ambience, theme.ambienceVolume);
            if (!string.IsNullOrEmpty(theme.music)) Loop("Music", theme.music, theme.musicVolume);
            var rz = new GameObject("ReverbZone").AddComponent<AudioReverbZone>();
            rz.reverbPreset = theme.reverb;
            rz.minDistance = 120f;
            rz.maxDistance = 400f;
            return refs;
        }

        static void Loop(string name, string clip, float volume)
        {
            var src = new GameObject(name).AddComponent<AudioSource>();
            src.clip = EmberPaths.Clip(clip);
            src.loop = true;
            src.playOnAwake = true;
            src.spatialBlend = 0f;
            src.volume = volume;
        }

        public static ReflectionProbe Probe(Vector3 center, Vector3 size, float intensity = 1f)
        {
            var go = new GameObject("ReflectionProbe");
            go.transform.position = center;
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Realtime;
            p.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            p.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            p.size = size;
            p.intensity = intensity;
            p.resolution = 128;
            p.hdr = true;
            return p;
        }
    }

    /// <summary>Post-processing profiles (URP Volume) - the colour script in numbers.</summary>
    public static class VolumeFactory
    {
        public static VolumeProfile Get(string name)
        {
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>($"{EmberPaths.Settings}/{name}.asset");
            return p != null ? p : Build(name);
        }

        public static void Run()
        {
            foreach (var n in new[] { "PP_Title", "PP_Crypt", "PP_Forge", "PP_SkyNight", "PP_SkyDawn", "PP_Ending" }) Build(n);
            AssetDatabase.SaveAssets();
        }

        static VolumeProfile Build(string name)
        {
            EmberPaths.Ensure(EmberPaths.Settings);
            string path = $"{EmberPaths.Settings}/{name}.asset";
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (p == null)
            {
                p = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(p, path);
            }
            foreach (var c in p.components.ToList())
            {
                p.components.Remove(c);
                Object.DestroyImmediate(c, true);
            }

            switch (name)
            {
                case "PP_Title":
                    Grade(p, 0.4f, 18f, -6f, new Color(0.34f, 0.4f, 0.62f), new Color(0.82f, 0.64f, 0.44f), -20f, 0.4f, -8f);
                    Glow(p, 1.2f, 0.95f, new Color(1f, 0.85f, 0.7f));
                    Dof(p, 9.5f, 7f);
                    Grain(p, 0.14f);
                    break;
                case "PP_Crypt":
                    Grade(p, 0.6f, 16f, -6f, new Color(0.36f, 0.42f, 0.62f), new Color(0.82f, 0.66f, 0.44f), -18f, 0.32f, -8f);
                    Glow(p, 1.15f, 0.95f, new Color(1f, 0.86f, 0.72f));
                    Dof(p, 13.3f, 8f);
                    Grain(p, 0.12f);
                    break;
                case "PP_Forge":
                    Grade(p, 0.6f, 16f, 4f, new Color(0.5f, 0.42f, 0.36f), new Color(0.8f, 0.62f, 0.42f), -10f, 0.3f, 4f);
                    Glow(p, 1.6f, 0.92f, new Color(1f, 0.76f, 0.6f));
                    Dof(p, 13.3f, 8f);
                    Grain(p, 0.15f);
                    break;
                case "PP_SkyNight":
                    Grade(p, 0.4f, 12f, -4f, new Color(0.3f, 0.32f, 0.62f), new Color(1f, 0.76f, 0.62f), -8f, 0.3f, -6f);
                    Glow(p, 1.25f, 0.95f, new Color(1f, 0.9f, 0.82f));
                    Dof(p, 13.3f, 8f);
                    Grain(p, 0.1f);
                    break;
                case "PP_SkyDawn":
                case "PP_Ending":
                    Grade(p, 0.55f, 10f, 12f, new Color(0.55f, 0.45f, 0.72f), new Color(1f, 0.85f, 0.6f), 10f, 0.22f, 14f);
                    Glow(p, 1.5f, 0.95f, new Color(1f, 0.92f, 0.82f));
                    Dof(p, name == "PP_Ending" ? 20f : 13.3f, 9f);
                    Grain(p, 0.08f);
                    break;
            }

            foreach (var c in p.components)
            {
                if (!AssetDatabase.Contains(c)) AssetDatabase.AddObjectToAsset(c, p);
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            }
            EditorUtility.SetDirty(p);
            return p;
        }

        static void Grade(VolumeProfile p, float exposure, float contrast, float saturation, Color shadows, Color highlights,
            float balance, float vignette, float temperature)
        {
            var tm = p.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            var ca = p.Add<ColorAdjustments>(true);
            ca.postExposure.Override(exposure);
            ca.contrast.Override(contrast);
            ca.saturation.Override(saturation);
            var st = p.Add<SplitToning>(true);
            st.shadows.Override(shadows);
            st.highlights.Override(highlights);
            st.balance.Override(balance);
            var vg = p.Add<Vignette>(true);
            vg.intensity.Override(vignette);
            vg.smoothness.Override(0.45f);
            vg.color.Override(new Color(0.02f, 0.015f, 0.04f));
            var wb = p.Add<WhiteBalance>(true);
            wb.temperature.Override(temperature);
        }

        static void Glow(VolumeProfile p, float intensity, float threshold, Color tint)
        {
            var b = p.Add<Bloom>(true);
            b.intensity.Override(intensity);
            b.threshold.Override(threshold);
            b.scatter.Override(0.72f);
            b.tint.Override(tint);
            b.highQualityFiltering.Override(true);
        }

        static void Dof(VolumeProfile p, float focus, float aperture)
        {
            var d = p.Add<DepthOfField>(true);
            d.mode.Override(DepthOfFieldMode.Bokeh);
            d.focusDistance.Override(focus);
            d.aperture.Override(aperture);
            d.focalLength.Override(42f);
        }

        static void Grain(VolumeProfile p, float amount)
        {
            var g = p.Add<FilmGrain>(true);
            g.type.Override(FilmGrainLookup.Thin1);
            g.intensity.Override(amount);
            g.response.Override(0.8f);
        }
    }
}
