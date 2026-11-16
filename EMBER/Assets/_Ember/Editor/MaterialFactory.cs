using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ember.EditorTools
{
    /// <summary>
    /// Builds every URP material of EMBER. Surfaces reuse the stone, lava and wood textures from the
    /// class packages (Session 2 / Session 3); glows, runes, beams and sky are procedural.
    /// Also converts the class packages' Built-in "Standard" materials to URP so the class sample
    /// scenes and lantern prefabs render correctly, and remaps Blender material slots (M_*) by name.
    /// </summary>
    public static class MaterialFactory
    {
        static Shader Lit => Shader.Find("Universal Render Pipeline/Lit");
        static Shader Particle => Shader.Find("Universal Render Pipeline/Particles/Unlit");
        static Shader Scroll => Shader.Find("Ember/ScrollingTexture");
        static Shader Sky => Shader.Find("Ember/SkyGradient");

        const string S2 = EmberPaths.ClassS2 + "/Textures/";
        const string S3 = EmberPaths.ClassS3 + "/Textures/";

        public static void Run()
        {
            EmberPaths.Ensure(EmberPaths.Materials);

            // ---- stone families (class Session 2 textures) -----------------------------
            LitMat("M_Stone", C(0.62f, 0.66f, 0.78f), Tex(S2 + "StoneBrickWall1.png"), Nrm(S2 + "StoneBrickWall1NM.png"), 0.16f);
            LitMat("M_StoneTrim", C(0.74f, 0.74f, 0.78f), Tex(S2 + "DungeonFlatWall2.png"), Nrm(S2 + "DungeonFlatWall2NM.png"), 0.2f);
            LitMat("M_StoneDark", C(0.36f, 0.38f, 0.46f), Tex(S2 + "RockWall_3x.png"), Nrm(S2 + "RockWall_3xNM.png"), 0.12f);
            LitMat("M_Statue", C(0.52f, 0.55f, 0.62f), Tex(S2 + "StuccoTexture1.png"), Nrm(S2 + "StuccoTexture1NM.png"), 0.15f);
            LitMat("M_Floor_Crypt", C(0.52f, 0.56f, 0.68f), Tex(S2 + "StoneBrickFloor.png"), Nrm(S2 + "StoneBrickFloorNM.png"), 0.32f);
            LitMat("M_Wall_Crypt", C(0.46f, 0.5f, 0.62f), Tex(S2 + "DungeonBrickWall.png"), Nrm(S2 + "DungeonBrickWallNM2.png"), 0.14f);
            LitMat("M_Basalt", C(0.36f, 0.3f, 0.28f), Tex(S2 + "Rock1Texture2.png"), Nrm(S2 + "Rock1Texture2NM.png"), 0.2f);
            LitMat("M_BasaltTrim", C(0.44f, 0.37f, 0.34f), Tex(S2 + "RockOldWall.png"), Nrm(S2 + "RockOldWallNM.png"), 0.18f);
            LitMat("M_Floor_Forge", C(0.46f, 0.38f, 0.35f), Tex(S2 + "RockSurface.png"), Nrm(S2 + "RockSurfaceNM.png"), 0.25f);
            LitMat("M_Wall_Forge", C(0.42f, 0.34f, 0.32f), Tex(S2 + "CaveRockWall.png"), Nrm(S2 + "CaveRockWallNM.png"), 0.14f);
            LitMat("M_Limestone", C(1f, 0.95f, 0.87f), Tex(S2 + "LimeStoneWall.png"), Nrm(S2 + "LimeStoneWallNM.png"), 0.2f);
            LitMat("M_LimestoneTrim", C(1f, 0.93f, 0.82f), Tex(S2 + "OldStucco.png"), Nrm(S2 + "OldStuccoNM.png"), 0.25f);
            LitMat("M_Floor_Sky", C(0.97f, 0.92f, 0.84f), Tex(S2 + "StoneBrickWall4.png"), Nrm(S2 + "StoneBrickWall4NM.png"), 0.3f);
            LitMat("M_Wood", C(0.82f, 0.7f, 0.58f), Tex(S3 + "WoodTexture.png"), null, 0.22f);

            // ---- metals, cloth, nature -----------------------------------------------
            LitMat("M_Iron", C(0.14f, 0.14f, 0.15f), null, null, 0.48f, 0.85f);
            LitMat("M_Gold", C(1f, 0.76f, 0.38f), null, null, 0.72f, 1f);
            LitMat("M_Brass", C(0.82f, 0.58f, 0.3f), null, null, 0.55f, 1f);
            LitMat("M_Cloth", C(0.52f, 0.07f, 0.09f), null, null, 0.08f, 0f, doubleSided: true);
            LitMat("M_ClothGold", C(0.92f, 0.7f, 0.32f), null, null, 0.35f, 0.4f, doubleSided: true);
            LitMat("M_Bark", C(0.62f, 0.46f, 0.36f), Tex(S3 + "WoodTexture.png"), null, 0.1f);
            LitMat("M_Leaf", C(0.98f, 0.68f, 0.78f), null, null, 0.15f, 0f, emission: C(0.18f, 0.06f, 0.09f));
            LitMat("M_Cloud", C(1f, 0.94f, 0.96f), null, null, 0.0f, 0f, emission: C(0.25f, 0.2f, 0.22f));
            LitMat("M_Water", C(0.02f, 0.03f, 0.06f), null, Nrm(S2 + "FloorNM.png"), 0.96f, 0f, tiling: 3f, bump: 0.15f);

            // ---- emissive (HDR) --------------------------------------------------------
            var cracks = EmberPaths.GenTex("T_Cracks");
            LitMat("M_RuneAmber", C(0.08f, 0.05f, 0.03f), null, null, 0.4f, 0f, emission: C(1f, 0.55f, 0.16f) * 3.5f);
            LitMat("M_RuneRed", C(0.08f, 0.02f, 0.02f), null, null, 0.4f, 0f, emission: C(1f, 0.12f, 0.06f) * 3.2f);
            LitMat("M_RuneCyan", C(0.02f, 0.06f, 0.08f), null, null, 0.4f, 0f, emission: C(0.25f, 0.85f, 1f) * 3.2f);
            LitMat("M_Glow", C(0.15f, 0.04f, 0.01f), null, null, 0.3f, 0f, emission: C(1f, 0.33f, 0.07f) * 5f);
            // unlit coal beds (braziers, checkpoints, beacon): the bright M_Glow copy lives in their "Lit" child
            LitMat("M_Coal", C(0.05f, 0.04f, 0.035f), null, null, 0.25f, 0f, emission: C(1f, 0.28f, 0.06f) * 0.12f);
            // title logo: metallic gold reads black in a dark room, so the letters carry their own warm glow
            LitMat("M_Logo", C(1f, 0.74f, 0.4f), null, null, 0.6f, 0.3f, emission: C(1f, 0.55f, 0.2f) * 1.8f);
            LitMat("M_EyeRed", C(0.1f, 0.01f, 0.01f), null, null, 0.6f, 0f, emission: C(1f, 0.08f, 0.04f) * 9f);
            LitMat("M_EyeAmber", C(0.1f, 0.06f, 0.01f), null, null, 0.6f, 0f, emission: C(1f, 0.62f, 0.18f) * 9f);
            LitMat("M_Ember", C(0.1f, 0.035f, 0.02f), null, null, 0.45f, 0f, emission: C(1f, 0.38f, 0.08f) * 7f, emissionMap: cracks, tiling: 2f);
            LitMat("M_Crystal", new Color(0.5f, 0.85f, 1f, 0.5f), null, null, 0.95f, 0f, emission: C(0.3f, 0.8f, 1f) * 1.6f, transparent: true);

            // ---- scrolling (class ScrollingUVs drives _MainTex) ------------------------
            var noise = EmberPaths.GenTex("T_Noise");
            var beam = EmberPaths.GenTex("T_Beam");
            var dot = EmberPaths.GenTex("T_SoftDot");
            var rune = EmberPaths.GenTex("T_Rune");
            var lava = Tex(S3 + "Lava.png");
            ScrollMat("M_Lava", lava, C(1f, 0.45f, 0.14f), 2.4f, 4f, noise, 2f, new Vector2(0.015f, 0.011f), 0.65f, BlendMode.One, BlendMode.Zero, true, 2000);
            ScrollMat("M_LavaFall", lava, C(1f, 0.5f, 0.16f), 2.8f, 2f, noise, 1f, new Vector2(0f, -0.25f), 0.55f, BlendMode.One, BlendMode.Zero, true, 2000);
            ScrollMat("M_Mist", noise, new Color(0.55f, 0.65f, 0.95f, 0.32f), 1f, 2f, noise, 1.3f, new Vector2(-0.01f, 0.006f), 0.6f, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, false, 3000, doubleSided: true);
            ScrollMat("M_CloudSea", noise, new Color(1f, 0.9f, 0.93f, 0.92f), 1.15f, 3f, noise, 1.7f, new Vector2(0.008f, 0.004f), 0.7f, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, false, 2999);
            // additive glows: the generated textures are white with the pattern in alpha, so blend SrcAlpha + One
            ScrollMat("M_Beam_Amber", beam, C(1f, 0.62f, 0.25f), 2.2f, 1f, noise, 1f, new Vector2(0f, 0.2f), 0.4f, BlendMode.SrcAlpha, BlendMode.One, false, 3050, doubleSided: true, edgeFade: 1f);
            ScrollMat("M_Beam_Moon", beam, C(0.55f, 0.7f, 1f), 1.5f, 1f, noise, 1f, new Vector2(0f, 0.05f), 0.4f, BlendMode.SrcAlpha, BlendMode.One, false, 3050, doubleSided: true, edgeFade: 1f);
            ScrollMat("M_Beam_Gold", beam, C(1f, 0.82f, 0.45f), 2.6f, 1f, noise, 1f, new Vector2(0f, 0.3f), 0.4f, BlendMode.SrcAlpha, BlendMode.One, false, 3050, doubleSided: true, edgeFade: 1f);
            ScrollMat("M_RuneDecal_Amber", rune, C(1f, 0.58f, 0.2f), 2.4f, 1f, noise, 1f, new Vector2(0.02f, 0.01f), 0.3f, BlendMode.SrcAlpha, BlendMode.One, false, 3010, pulse: 0.15f);
            ScrollMat("M_RuneDecal_Cyan", rune, C(0.35f, 0.85f, 1f), 2.4f, 1f, noise, 1f, new Vector2(0.02f, 0.01f), 0.3f, BlendMode.SrcAlpha, BlendMode.One, false, 3010, pulse: 0.15f);
            ScrollMat("M_Glow_Soft", dot, C(1f, 0.6f, 0.25f), 1.4f, 1f, null, 1f, Vector2.zero, 0f, BlendMode.SrcAlpha, BlendMode.One, false, 3060, doubleSided: true);

            // ---- particles --------------------------------------------------------------
            ParticleMat("M_Particle_Spark", EmberPaths.GenTex("T_Spark"), new Color(1f, 0.75f, 0.35f, 1f), true);
            ParticleMat("M_Particle_Fire", dot, new Color(1f, 0.5f, 0.15f, 1f), true);
            ParticleMat("M_Particle_Glow", dot, new Color(1f, 0.55f, 0.22f, 0.35f), true);
            ParticleMat("M_Particle_Smoke", EmberPaths.GenTex("T_Smoke"), new Color(0.18f, 0.16f, 0.17f, 0.55f), false);
            ParticleMat("M_Particle_Dust", dot, new Color(0.65f, 0.75f, 1f, 0.5f), true);
            ParticleMat("M_Particle_Crystal", EmberPaths.GenTex("T_Spark"), new Color(0.5f, 0.9f, 1f, 1f), true);

            // ---- skies ------------------------------------------------------------------
            SkyMat("M_Sky_Crypt", C(0.015f, 0.02f, 0.045f), C(0.045f, 0.05f, 0.1f), C(0.01f, 0.01f, 0.02f), C(0.015f, 0.02f, 0.045f), C(0.045f, 0.05f, 0.1f), C(0.01f, 0.01f, 0.02f), 0f, new Vector3(0.2f, 0.3f, 1f), 0f, 0.25f);
            SkyMat("M_Sky_Forge", C(0.04f, 0.012f, 0.01f), C(0.2f, 0.055f, 0.02f), C(0.03f, 0.01f, 0.008f), C(0.04f, 0.012f, 0.01f), C(0.2f, 0.055f, 0.02f), C(0.03f, 0.01f, 0.008f), 0f, new Vector3(0f, 0.2f, 1f), 0f, 0f);
            SkyMat("M_Sky_Dawn", C(0.07f, 0.08f, 0.21f), C(0.38f, 0.26f, 0.44f), C(0.14f, 0.11f, 0.22f), C(0.3f, 0.46f, 0.78f), C(1f, 0.72f, 0.52f), C(0.78f, 0.57f, 0.62f), 0f, new Vector3(0.25f, 0.05f, 1f), 1f, 0.8f);
            SkyMat("M_Sky_Title", C(0.02f, 0.025f, 0.06f), C(0.08f, 0.08f, 0.16f), C(0.01f, 0.01f, 0.02f), C(0.02f, 0.025f, 0.06f), C(0.08f, 0.08f, 0.16f), C(0.01f, 0.01f, 0.02f), 0f, new Vector3(0f, 0.3f, 1f), 0f, 0.7f);
            SkyMat("M_Sky_Ending", C(0.3f, 0.46f, 0.78f), C(1f, 0.74f, 0.55f), C(0.78f, 0.57f, 0.62f), C(0.3f, 0.46f, 0.78f), C(1f, 0.74f, 0.55f), C(0.78f, 0.57f, 0.62f), 1f, new Vector3(0.25f, 0.12f, 1f), 1f, 0f);

            ConvertClassMaterials();
            AssetDatabase.SaveAssets();
            RemapModels();
        }

        static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

        static Texture2D Tex(string path)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t == null) Debug.LogWarning("[Ember] texture missing: " + path);
            return t;
        }

        static Texture2D Nrm(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null && imp.textureType != TextureImporterType.NormalMap)
            {
                imp.textureType = TextureImporterType.NormalMap;
                imp.SaveAndReimport();
            }
            return Tex(path);
        }

        static Material GetOrCreate(string name, Shader shader)
        {
            string path = $"{EmberPaths.Materials}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader) m.shader = shader;
            return m;
        }

        public static Material LitMat(string name, Color color, Texture2D albedo, Texture2D normal, float smooth, float metal = 0f,
            Color? emission = null, Texture2D emissionMap = null, float tiling = 1f, float bump = 1f, bool transparent = false, bool doubleSided = false)
        {
            var m = GetOrCreate(name, Lit);
            m.SetColor("_BaseColor", color);
            m.SetColor("_Color", color);
            m.SetTexture("_BaseMap", albedo);
            m.SetTexture("_MainTex", albedo);
            m.SetTextureScale("_BaseMap", Vector2.one * tiling);
            m.SetTextureScale("_MainTex", Vector2.one * tiling);
            m.SetFloat("_WorkflowMode", 1f);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", bump);
            if (emission.HasValue)
            {
                m.SetColor("_EmissionColor", emission.Value);
                m.SetTexture("_EmissionMap", emissionMap);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.SetColor("_EmissionColor", Color.black);
                m.SetTexture("_EmissionMap", null);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            m.SetFloat("_Surface", transparent ? 1f : 0f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_Cull", doubleSided ? 0f : 2f);
            m.SetFloat("_ReceiveShadows", 1f);
            m.SetFloat("_EnvironmentReflections", 1f);
            m.SetFloat("_SpecularHighlights", 1f);
            BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
            if (emission.HasValue) m.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material ScrollMat(string name, Texture2D tex, Color color, float brightness, float tiling, Texture2D detail, float detailTiling,
            Vector2 detailSpeed, float detailStrength, BlendMode src, BlendMode dst, bool zwrite, int queue,
            bool doubleSided = false, float edgeFade = 0f, float pulse = 0f)
        {
            var m = GetOrCreate(name, Scroll);
            m.SetTexture("_MainTex", tex);
            m.SetTextureScale("_MainTex", Vector2.one * tiling);
            m.SetColor("_Color", color);
            m.SetFloat("_Emission", brightness);
            m.SetTexture("_DetailTex", detail != null ? detail : Texture2D.whiteTexture);
            m.SetTextureScale("_DetailTex", Vector2.one * detailTiling);
            m.SetVector("_DetailSpeed", new Vector4(detailSpeed.x, detailSpeed.y, 0f, 0f));
            m.SetFloat("_DetailStrength", detail != null ? detailStrength : 0f);
            m.SetFloat("_SrcBlend", (float)src);
            m.SetFloat("_DstBlend", (float)dst);
            m.SetFloat("_ZWrite", zwrite ? 1f : 0f);
            m.SetFloat("_Cull", doubleSided ? 0f : 2f);
            m.SetFloat("_EdgeFade", edgeFade);
            m.SetFloat("_EdgePower", 1.6f);
            m.SetFloat("_Pulse", pulse);
            m.renderQueue = queue;
            m.SetOverrideTag("RenderType", queue >= 2500 ? "Transparent" : "Opaque");
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material ParticleMat(string name, Texture2D tex, Color color, bool additive)
        {
            var m = GetOrCreate(name, Particle);
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_MainTex", tex);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_ColorMode", 0f);
            BaseShaderGUI.SetMaterialKeywords(m, null, ParticleGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material SkyMat(string name, Color topA, Color horA, Color botA, Color topB, Color horB, Color botB, float blend,
            Vector3 sunDir, float sunVisible, float stars)
        {
            var m = GetOrCreate(name, Sky);
            m.SetColor("_TopA", topA); m.SetColor("_HorizonA", horA); m.SetColor("_BottomA", botA);
            m.SetColor("_TopB", topB); m.SetColor("_HorizonB", horB); m.SetColor("_BottomB", botB);
            m.SetFloat("_Blend", blend);
            m.SetVector("_SunDirection", new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
            m.SetFloat("_SunVisible", sunVisible);
            m.SetFloat("_Stars", stars);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ class packages -> URP
        public static void ConvertClassMaterials()
        {
            var guids = AssetDatabase.FindAssets("t:Material", new[] { EmberPaths.ClassS2, EmberPaths.ClassS3 });
            int n = 0;
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || m.shader == null) continue;
                string sn = m.shader.name;
                if (sn.StartsWith("Universal Render Pipeline") || sn.StartsWith("Ember/") || sn == "Sprites/Default") continue;
                string lower = m.name.ToLowerInvariant();
                if (lower.Contains("flame"))
                {
                    m.shader = Shader.Find("Sprites/Default");
                    m.color = Color.white;
                }
                else if (path.EndsWith("LavaFloorMat.mat"))
                {
                    var tex = m.GetTexture("_MainTex");
                    var scale = m.GetTextureScale("_MainTex");
                    m.shader = Scroll;
                    m.SetTexture("_MainTex", tex);
                    m.SetTextureScale("_MainTex", scale);
                    m.SetColor("_Color", new Color(1f, 0.5f, 0.2f));
                    m.SetFloat("_Emission", 2f);
                    m.SetTexture("_DetailTex", Texture2D.whiteTexture);
                    m.SetFloat("_DetailStrength", 0f);
                    m.SetFloat("_SrcBlend", (float)BlendMode.One);
                    m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                    m.SetFloat("_ZWrite", 1f);
                    m.renderQueue = 2000;
                }
                else ToLit(m, sn);
                EditorUtility.SetDirty(m);
                n++;
            }
            Debug.Log($"[Ember] converted {n} class materials to URP");
        }

        static Texture Get(Material m, string prop) => m.HasProperty(prop) ? m.GetTexture(prop) : null;

        static void ToLit(Material m, string oldShader)
        {
            Texture main = Get(m, "_MainTex");
            Vector2 scale = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one;
            Color col = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            Texture bump = Get(m, "_BumpMap");
            float bumpScale = m.HasProperty("_BumpScale") ? m.GetFloat("_BumpScale") : 1f;
            Texture emiMap = Get(m, "_EmissionMap");
            Color emi = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
            bool emiOn = m.IsKeywordEnabled("_EMISSION") || emiMap != null;
            float gloss = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0.3f;
            float metal = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f;
            Texture spec = Get(m, "_SpecGlossMap");
            Color specCol = m.HasProperty("_SpecColor") ? m.GetColor("_SpecColor") : new Color(0.2f, 0.2f, 0.2f);
            float mode = m.HasProperty("_Mode") ? m.GetFloat("_Mode") : 0f;
            bool glass = oldShader.Contains("Glass") || m.name.ToLowerInvariant().Contains("glass");
            bool specular = oldShader.Contains("Specular");

            m.shader = Lit;
            m.SetTexture("_BaseMap", main);
            m.SetTextureScale("_BaseMap", scale);
            m.SetColor("_BaseColor", glass ? new Color(0.65f, 0.75f, 0.85f, 0.28f) : col);
            m.SetTexture("_BumpMap", bump);
            m.SetFloat("_BumpScale", bumpScale);
            m.SetFloat("_Smoothness", glass ? 0.92f : gloss);
            if (specular)
            {
                m.SetFloat("_WorkflowMode", 0f);
                m.SetTexture("_SpecGlossMap", spec);
                m.SetColor("_SpecColor", specCol);
            }
            else
            {
                m.SetFloat("_WorkflowMode", 1f);
                m.SetFloat("_Metallic", metal);
            }
            if (emiOn && emi.maxColorComponent > 0.01f)
            {
                m.SetColor("_EmissionColor", emi);
                m.SetTexture("_EmissionMap", emiMap);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            bool transparent = glass || mode >= 2f;
            m.SetFloat("_Surface", transparent ? 1f : 0f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_AlphaClip", mode == 1f ? 1f : 0f);
            BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
        }

        // ------------------------------------------------------------------ Blender slot remap
        public static void RemapModels()
        {
            var mats = AssetDatabase.FindAssets("t:Material", new[] { EmberPaths.Materials })
                .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(m => m != null).ToList();
            int touched = 0;
            foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { EmberPaths.Models }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) continue;
                var map = imp.GetExternalObjectMap();
                bool changed = false;
                foreach (var m in mats)
                {
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name);
                    if (!map.TryGetValue(id, out var existing) || existing != m)
                    {
                        imp.AddRemap(id, m);
                        changed = true;
                    }
                }
                if (changed) { imp.SaveAndReimport(); touched++; }
            }
            Debug.Log($"[Ember] remapped materials on {touched} models");
        }
    }
}
