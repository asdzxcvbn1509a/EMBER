using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ember.EditorTools
{
    /// <summary>
    /// Procedural textures made for EMBER (no downloaded art): rune circles, glow dots, smoke,
    /// tileable noise, glowing cracks, light-beam streaks and the game icon.
    /// </summary>
    public static class TextureFactory
    {
        public static void Run()
        {
            EmberPaths.Ensure(EmberPaths.Textures);
            Save("T_Rune", Rune(512), clamp: true);
            Save("T_SoftDot", Dot(128, 2.2f), clamp: true);
            Save("T_Spark", Dot(64, 5f), clamp: true);
            Save("T_Smoke", Smoke(256), clamp: true);
            Save("T_Noise", Noise(512, 8, 5, 11), clamp: false);
            Save("T_Cracks", Cracks(512, 10, 21), clamp: false);
            Save("T_Beam", Beam(64, 256), clamp: false);
            Save("T_Icon", Icon(256), clamp: true);
            Save("T_Diamond", Diamond(64), clamp: true);
            Save("T_FadeH", FadeH(256, 4), clamp: true);
            Save("T_FadeC", FadeC(256, 4), clamp: true);
            Save("T_HoverH", HoverBand(256, 64, centred: false), clamp: true);
            Save("T_HoverC", HoverBand(256, 64, centred: true), clamp: true);
            Save("T_UIGlow", Dot(64, 2.2f), clamp: true);
            Save("T_White", Solid(8, Color.white), clamp: true);
            AssetDatabase.Refresh();
        }

        /// <summary>Textures that the UI uses as sprites; everything else is a material texture.</summary>
        static readonly string[] SpriteTextures = { "T_Icon", "T_Diamond", "T_FadeH", "T_FadeC", "T_HoverH", "T_HoverC", "T_UIGlow" };

        static void Save(string name, Texture2D tex, bool clamp)
        {
            string path = $"{EmberPaths.Textures}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = System.Array.IndexOf(SpriteTextures, name) >= 0 ? TextureImporterType.Sprite : TextureImporterType.Default;
            imp.alphaIsTransparency = true;
            imp.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.sRGBTexture = true;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
        }

        static Texture2D New(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false, false);

        static Texture2D Solid(int s, Color c)
        {
            var t = New(s, s);
            var px = new Color[s * s];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static Texture2D Dot(int s, float sharp)
        {
            var t = New(s, s);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Exp(-d * d * sharp * 2f) * Mathf.Clamp01(1f - d);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        /// <summary>Horizontal fade for UI panels: opaque on the left, transparent on the right.</summary>
        static Texture2D FadeH(int w, int h)
        {
            var t = New(w, h);
            for (int x = 0; x < w; x++)
            {
                float a = 1f - Edge(0f, 1f, (float)x / (w - 1));
                for (int y = 0; y < h; y++) t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            t.Apply();
            return t;
        }

        /// <summary>Horizontal fade for centred UI highlights: opaque in the middle, transparent at both ends.</summary>
        static Texture2D FadeC(int w, int h)
        {
            var t = New(w, h);
            for (int x = 0; x < w; x++)
            {
                float a = 1f - Edge(0f, 1f, Mathf.Abs((float)x / (w - 1) * 2f - 1f));
                for (int y = 0; y < h; y++) t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            t.Apply();
            return t;
        }

        /// <summary>
        /// Soft light behind a hovered menu button: brightest at the text side (near the left end, or the middle when
        /// centred), falling off quickly, and feathered on every edge so it reads as light rather than a box.
        /// </summary>
        static Texture2D HoverBand(int w, int h, bool centred)
        {
            var t = New(w, h);
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                float feather = Edge(0f, 0.35f, v) * Edge(0f, 0.35f, 1f - v);
                for (int x = 0; x < w; x++)
                {
                    float u = (float)x / (w - 1);
                    float a = centred
                        ? Mathf.Pow(1f - Edge(0f, 1f, Mathf.Abs(u * 2f - 1f)), 1.3f)
                        : Edge(0f, 0.08f, u) * Mathf.Pow(1f - Edge(0.04f, 1f, u), 1.6f); // short ramp-in, peak by the ember
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a * feather));
                }
            }
            t.Apply();
            return t;
        }

        static Texture2D Diamond(int s)
        {
            var t = New(s, s);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Abs((x + 0.5f) / s * 2f - 1f), dy = Mathf.Abs((y + 0.5f) / s * 2f - 1f);
                    float d = dx + dy * 0.7f;
                    float a = Mathf.Clamp01((0.95f - d) * 24f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        static float Ring(float d, float r, float w) => Mathf.Clamp01(1f - Mathf.Abs(d - r) / w);

        static Texture2D Rune(int s)
        {
            var t = New(s, s);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    float a = 0f;
                    a = Mathf.Max(a, Ring(d, 0.93f, 0.025f));
                    a = Mathf.Max(a, Ring(d, 0.86f, 0.012f));
                    a = Mathf.Max(a, Ring(d, 0.55f, 0.02f));
                    a = Mathf.Max(a, Ring(d, 0.2f, 0.03f));
                    // ticks between the outer rings
                    float tick = Mathf.Abs(Mathf.Sin(ang * 24f));
                    if (d > 0.87f && d < 0.92f && tick < 0.15f) a = Mathf.Max(a, 0.8f);
                    // eight glyph triangles between the middle rings
                    float seg = Mathf.Repeat(ang / (Mathf.PI * 2f) * 8f, 1f);
                    float tri = 1f - Mathf.Abs(seg - 0.5f) * 2f;
                    if (d > 0.6f && d < 0.8f && Mathf.Abs((d - 0.6f) / 0.2f - tri) < 0.08f) a = Mathf.Max(a, 0.9f);
                    // spokes inside
                    float spoke = Mathf.Abs(Mathf.Sin(ang * 4f));
                    if (d > 0.24f && d < 0.52f && spoke < 0.05f) a = Mathf.Max(a, 0.85f);
                    a *= Mathf.Clamp01((1f - d) * 30f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        // ------------------------------------------------------------- tileable noise
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695040;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xffffff) / (float)0xffffff;
            }
        }

        static float ValueNoise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int X0 = ((x0 % period) + period) % period, Y0 = ((y0 % period) + period) % period;
            int X1 = (X0 + 1) % period, Y1 = (Y0 + 1) % period;
            float a = Hash(X0, Y0, seed), b = Hash(X1, Y0, seed), c = Hash(X0, Y1, seed), d = Hash(X1, Y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int p = basePeriod;
            for (int o = 0; o < octaves; o++)
            {
                sum += ValueNoise(u, v, p, seed + o * 17) * amp;
                norm += amp;
                amp *= 0.5f;
                p *= 2;
            }
            return sum / norm;
        }

        static Texture2D Noise(int s, int basePeriod, int octaves, int seed)
        {
            var t = New(s, s);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float n = Fbm((float)x / s, (float)y / s, basePeriod, octaves, seed);
                    n = Mathf.Clamp01((n - 0.2f) * 1.6f);
                    t.SetPixel(x, y, new Color(n, n, n, n));
                }
            t.Apply();
            return t;
        }

        static Texture2D Smoke(int s)
        {
            var t = New(s, s);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float n = Fbm((float)x / s, (float)y / s, 4, 4, 77);
                    float a = Mathf.Clamp01(1f - d) * Mathf.Clamp01(n * 1.6f - 0.25f);
                    a = Mathf.SmoothStep(0f, 1f, a);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        static Texture2D Cracks(int s, int cells, int seed)
        {
            var t = New(s, s);
            var pts = new Vector2[cells * cells];
            for (int j = 0; j < cells; j++)
                for (int i = 0; i < cells; i++)
                    pts[j * cells + i] = new Vector2(i + Hash(i, j, seed), j + Hash(i, j, seed + 1));
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    Vector2 p = new Vector2((float)x / s * cells, (float)y / s * cells);
                    int ci = Mathf.FloorToInt(p.x), cj = Mathf.FloorToInt(p.y);
                    float f1 = 99f, f2 = 99f;
                    for (int oj = -1; oj <= 1; oj++)
                        for (int oi = -1; oi <= 1; oi++)
                        {
                            int ii = ci + oi, jj = cj + oj;
                            int wi = ((ii % cells) + cells) % cells, wj = ((jj % cells) + cells) % cells;
                            Vector2 q = pts[wj * cells + wi] + new Vector2(ii - wi, jj - wj);
                            float d = (q - p).magnitude;
                            if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) f2 = d;
                        }
                    float edge = f2 - f1;
                    float line = Mathf.Clamp01(1f - edge / 0.09f);
                    line = line * line;
                    float wobble = Fbm((float)x / s, (float)y / s, 16, 3, seed + 5);
                    float v = Mathf.Clamp01(line * (0.6f + wobble));
                    t.SetPixel(x, y, new Color(v, v, v, 1f));
                }
            t.Apply();
            return t;
        }

        /// <summary>
        /// Light-shaft alpha for a tube (u wraps around it, v runs from the source to the far end).
        /// Streaks are periodic in u so the tube has no seam; the soft silhouette comes from the shader's edge fade.
        /// </summary>
        static Texture2D Beam(int w, int h)
        {
            var t = New(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (float)x / w, v = (float)y / h;
                    float streak = 0.62f + 0.22f * Mathf.Sin(u * Mathf.PI * 6f + v * 2.5f) + 0.16f * Mathf.Sin(u * Mathf.PI * 14f - v * 6f);
                    float fade = Edge(0f, 0.18f, v) * (1f - 0.35f * Edge(0.7f, 1f, v));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(streak * fade)));
                }
            t.Apply();
            return t;
        }

        /// <summary>GLSL-style smoothstep (Mathf.SmoothStep is an eased lerp, not an edge function).</summary>
        static float Edge(float e0, float e1, float x)
        {
            float k = Mathf.Clamp01((x - e0) / (e1 - e0));
            return k * k * (3f - 2f * k);
        }

        static Texture2D Icon(int s)
        {
            var t = New(s, s);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color bg = Color.Lerp(new Color(0.11f, 0.13f, 0.25f), new Color(0.04f, 0.04f, 0.09f), d);
                    float core = Mathf.Clamp01((0.42f - d) * 18f);
                    float glow = Mathf.Exp(-d * d * 6f);
                    float n = Fbm((float)x / s, (float)y / s, 6, 3, 9);
                    Color ember = Color.Lerp(new Color(1f, 0.35f, 0.08f), new Color(1f, 0.85f, 0.45f), Mathf.Clamp01(1f - d * 2.2f + n * 0.3f));
                    Color c = Color.Lerp(bg, ember, core) + new Color(1f, 0.55f, 0.2f) * glow * 0.35f * (1f - core);
                    c.a = Mathf.Clamp01((1f - d) * 40f);
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }
    }
}
