using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ember.EditorTools
{
    /// <summary>
    /// Procedural architecture meshes. Every floor, wall and step in EMBER is a chamfered box:
    /// the bevel catches light along the edges (the low-poly "thick bevel" style), and UVs are
    /// box-projected in metres so the class stone textures keep the same scale on every block.
    /// Meshes are cached as assets so all scenes share them.
    /// </summary>
    public static class MeshGen
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        public static Mesh ChamferBox(Vector3 size, float bevel = 0.06f, float uvScale = 0.5f)
        {
            bevel = Mathf.Clamp(bevel, 0f, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.3f);
            string key = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Box_{0:0.###}x{1:0.###}x{2:0.###}_b{3:0.###}_u{4:0.###}", size.x, size.y, size.z, bevel, uvScale);
            return Cached(key, () => BuildChamferBox(size, bevel, uvScale));
        }

        public static Mesh Cylinder(float radius, float height, int segments = 24, float uvScale = 0.5f, float bevel = 0.05f)
        {
            string key = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Cyl_r{0:0.###}_h{1:0.###}_s{2}_u{3:0.###}_b{4:0.###}", radius, height, segments, uvScale, bevel);
            return Cached(key, () => BuildCylinder(radius, height, segments, uvScale, bevel));
        }

        /// <summary>
        /// Open, smooth tube for light beams: radial normals (so the shader's view-angle edge fade is soft)
        /// and normalised UVs (u wraps once around, v runs 0 at local -Y to 1 at local +Y).
        /// </summary>
        public static Mesh Tube(float radius, float height, int segments = 24)
        {
            string key = string.Format(System.Globalization.CultureInfo.InvariantCulture, "Tube_r{0:0.###}_h{1:0.###}_s{2}", radius, height, segments);
            return Cached(key, () =>
            {
                var verts = new List<Vector3>();
                var normals = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                for (int i = 0; i <= segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    verts.Add(dir * radius + Vector3.down * height * 0.5f);
                    verts.Add(dir * radius + Vector3.up * height * 0.5f);
                    normals.Add(dir);
                    normals.Add(dir);
                    uvs.Add(new Vector2((float)i / segments, 0f));
                    uvs.Add(new Vector2((float)i / segments, 1f));
                }
                for (int i = 0; i < segments; i++)
                {
                    int b = i * 2;
                    tris.AddRange(new[] { b, b + 1, b + 2, b + 2, b + 1, b + 3 });
                }
                var m = new Mesh();
                m.SetVertices(verts);
                m.SetNormals(normals);
                m.SetUVs(0, uvs);
                m.SetTriangles(tris, 0);
                m.RecalculateBounds();
                return m;
            });
        }

        static Mesh Cached(string key, System.Func<Mesh> build)
        {
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            string path = $"{EmberPaths.Meshes}/{key}.asset";
            m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m == null)
            {
                m = build();
                m.name = key;
                EmberPaths.Ensure(EmberPaths.Meshes);
                AssetDatabase.CreateAsset(m, path);
            }
            cache[key] = m;
            return m;
        }

        class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();
            readonly float uvScale;
            public Builder(float uvScale) { this.uvScale = uvScale; }

            public void Poly(Vector3 normal, params Vector3[] p)
            {
                normal.Normalize();
                float ax = Mathf.Abs(normal.x), ay = Mathf.Abs(normal.y), az = Mathf.Abs(normal.z);
                int axis = ax >= ay && ax >= az ? 0 : (ay >= az ? 1 : 2);
                int start = v.Count;
                foreach (var q in p)
                {
                    v.Add(q);
                    n.Add(normal);
                    Vector2 w = axis == 0 ? new Vector2(q.z * Mathf.Sign(normal.x), q.y) : axis == 1 ? new Vector2(q.x, q.z * Mathf.Sign(normal.y)) : new Vector2(-q.x * Mathf.Sign(normal.z), q.y);
                    uv.Add(w * uvScale);
                }
                for (int i = 1; i + 1 < p.Length; i++)
                {
                    int a = start, b = start + i, c = start + i + 1;
                    Vector3 cr = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                    if (Vector3.Dot(cr, normal) >= 0f) { t.Add(a); t.Add(b); t.Add(c); }
                    else { t.Add(a); t.Add(c); t.Add(b); }
                }
            }

            public Mesh ToMesh()
            {
                var m = new Mesh();
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetUVs(0, uv);
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                m.RecalculateTangents();
                return m;
            }
        }

        static Mesh BuildChamferBox(Vector3 size, float b, float uvScale)
        {
            var B = new Builder(uvScale);
            Vector3 h = size * 0.5f;
            Vector3 e = h - Vector3.one * b;
            for (int s = -1; s <= 1; s += 2)
            {
                B.Poly(new Vector3(s, 0, 0), new Vector3(s * h.x, -e.y, -e.z), new Vector3(s * h.x, e.y, -e.z), new Vector3(s * h.x, e.y, e.z), new Vector3(s * h.x, -e.y, e.z));
                B.Poly(new Vector3(0, s, 0), new Vector3(-e.x, s * h.y, -e.z), new Vector3(e.x, s * h.y, -e.z), new Vector3(e.x, s * h.y, e.z), new Vector3(-e.x, s * h.y, e.z));
                B.Poly(new Vector3(0, 0, s), new Vector3(-e.x, -e.y, s * h.z), new Vector3(e.x, -e.y, s * h.z), new Vector3(e.x, e.y, s * h.z), new Vector3(-e.x, e.y, s * h.z));
            }
            if (b > 1e-4f)
            {
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                        B.Poly(new Vector3(sx, sy, 0), new Vector3(sx * h.x, sy * e.y, -e.z), new Vector3(sx * e.x, sy * h.y, -e.z), new Vector3(sx * e.x, sy * h.y, e.z), new Vector3(sx * h.x, sy * e.y, e.z));
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        B.Poly(new Vector3(0, sy, sz), new Vector3(-e.x, sy * h.y, sz * e.z), new Vector3(-e.x, sy * e.y, sz * h.z), new Vector3(e.x, sy * e.y, sz * h.z), new Vector3(e.x, sy * h.y, sz * e.z));
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        B.Poly(new Vector3(sx, 0, sz), new Vector3(sx * h.x, -e.y, sz * e.z), new Vector3(sx * e.x, -e.y, sz * h.z), new Vector3(sx * e.x, e.y, sz * h.z), new Vector3(sx * h.x, e.y, sz * e.z));
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                        for (int sz = -1; sz <= 1; sz += 2)
                            B.Poly(new Vector3(sx, sy, sz), new Vector3(sx * h.x, sy * e.y, sz * e.z), new Vector3(sx * e.x, sy * h.y, sz * e.z), new Vector3(sx * e.x, sy * e.y, sz * h.z));
            }
            return B.ToMesh();
        }

        static Mesh BuildCylinder(float r, float height, int seg, float uvScale, float bevel)
        {
            var B = new Builder(uvScale);
            float y0 = -height * 0.5f, y1 = height * 0.5f;
            float rb = Mathf.Max(0.01f, r - bevel);
            float yb0 = y0 + bevel, yb1 = y1 - bevel;
            var ring = new Vector3[seg];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                ring[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }
            var top = new Vector3[seg];
            var bot = new Vector3[seg];
            for (int i = 0; i < seg; i++) { top[i] = ring[i] * rb + Vector3.up * y1; bot[i] = ring[i] * rb + Vector3.up * y0; }
            B.Poly(Vector3.up, top);
            System.Array.Reverse(bot);
            B.Poly(Vector3.down, bot);
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                Vector3 mid = (ring[i] + ring[j]).normalized;
                B.Poly(mid, ring[i] * r + Vector3.up * yb0, ring[j] * r + Vector3.up * yb0, ring[j] * r + Vector3.up * yb1, ring[i] * r + Vector3.up * yb1);
                if (bevel > 1e-4f)
                {
                    B.Poly((mid + Vector3.up).normalized, ring[i] * r + Vector3.up * yb1, ring[j] * r + Vector3.up * yb1, ring[j] * rb + Vector3.up * y1, ring[i] * rb + Vector3.up * y1);
                    B.Poly((mid + Vector3.down).normalized, ring[i] * rb + Vector3.up * y0, ring[j] * rb + Vector3.up * y0, ring[j] * r + Vector3.up * yb0, ring[i] * r + Vector3.up * yb0);
                }
            }
            return B.ToMesh();
        }
    }
}
