using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Ember.EditorTools
{
    /// <summary>
    /// Writes every generated scene as JSON plus a mesh blob, so the Blender trailer scripts (ArtSource/Trailer)
    /// can rebuild the levels exactly: transforms, meshes, materials, lights, particles, text, legacy clips and the
    /// serialized fields of every script (the class Condition/Action wiring is replayed in Blender).
    /// Edit mode and read only: scenes and assets are never saved or re-imported.
    /// Menu: Ember/6. Export Scenes for Trailer      Batch: Pipeline.BatchTrailerExport
    /// Output: ArtSource/Trailer/export/&lt;Scene&gt;/scene.json + meshes.bin, export_report.txt
    /// </summary>
    public static class TrailerExport
    {
        public const int Version = 1;
        public const int CurveRate = 120;
        public static string OutRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "ArtSource", "Trailer", "export"));

        public static void Run()
        {
            Directory.CreateDirectory(OutRoot);
            var report = new List<string> { $"EMBER trailer export v{Version} - Unity {Application.unityVersion} - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) };
            foreach (var name in Pipeline.SceneOrder)
            {
                string path = EmberPaths.Scene(name);
                if (!File.Exists(path)) { report.Add($"[{name}] scene file missing: {path}"); continue; }
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var ex = new SceneExporter(name, Path.Combine(OutRoot, name));
                ex.Export(scene);
                report.AddRange(ex.report);
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            File.WriteAllLines(Path.Combine(OutRoot, "export_report.txt"), report);
            foreach (var l in report) Debug.Log("[Ember] trailer export: " + l);
        }

        // ================================================================== one scene
        class Graph
        {
            public readonly Dictionary<GameObject, int> ids = new Dictionary<GameObject, int>();
            public readonly List<GameObject> order = new List<GameObject>();
            public readonly List<object> nodes = new List<object>();
        }

        class SceneExporter
        {
            readonly string sceneName, dir;
            public readonly List<string> report = new List<string>();
            BinaryWriter bin;
            readonly Dictionary<Mesh, int> meshIds = new Dictionary<Mesh, int>();
            readonly Dictionary<Material, int> matIds = new Dictionary<Material, int>();
            readonly Dictionary<Texture, int> texIds = new Dictionary<Texture, int>();
            readonly Dictionary<AnimationClip, int> clipIds = new Dictionary<AnimationClip, int>();
            readonly List<object> meshes = new List<object>(), materials = new List<object>(), textures = new List<object>(), clips = new List<object>();
            readonly Dictionary<string, object> templates = new Dictionary<string, object>();
            readonly Queue<GameObject> templateQueue = new Queue<GameObject>();
            readonly HashSet<string> templateQueued = new HashSet<string>();
            readonly SortedDictionary<string, string> audio = new SortedDictionary<string, string>();
            readonly SortedDictionary<string, int> shaderCount = new SortedDictionary<string, int>();
            readonly SortedDictionary<string, int> behCount = new SortedDictionary<string, int>();
            readonly SortedSet<string> otherComps = new SortedSet<string>();
            readonly SortedSet<string> missingTex = new SortedSet<string>();
            int lights, particles, tmps, sprites, vertexTotal;
            Graph current;

            public SceneExporter(string sceneName, string dir)
            {
                this.sceneName = sceneName;
                this.dir = dir;
            }

            public void Export(UnityEngine.SceneManagement.Scene scene)
            {
                Directory.CreateDirectory(dir);
                using (bin = new BinaryWriter(File.Open(Path.Combine(dir, "meshes.bin"), FileMode.Create)))
                {
                    var g = new Graph();
                    foreach (var root in scene.GetRootGameObjects())
                        if (!IsUi(root)) Collect(root, g);
                    current = g;
                    foreach (var go in g.order) g.nodes.Add(Node(go, g));
                    var gm = Object.FindAnyObjectByType<GameManager>();
                    var doc = new Dictionary<string, object>
                    {
                        ["version"] = Version,
                        ["scene"] = sceneName,
                        ["unity"] = Application.unityVersion,
                        ["colorSpace"] = QualitySettings.activeColorSpace.ToString(),
                        ["render"] = Render(gm, g),
                        ["nodes"] = g.nodes,
                    };
                    // clone templates (HitAndClone.sourcePrefab assets that are not in the scene)
                    while (templateQueue.Count > 0)
                    {
                        var prefab = templateQueue.Dequeue();
                        var tg = new Graph();
                        Collect(prefab, tg);
                        current = tg;
                        foreach (var go in tg.order) tg.nodes.Add(Node(go, tg));
                        templates[AssetDatabase.GetAssetPath(prefab)] = new Dictionary<string, object> { ["name"] = prefab.name, ["nodes"] = tg.nodes };
                    }
                    current = g;
                    doc["templates"] = templates;
                    doc["meshes"] = meshes;
                    doc["materials"] = materials;
                    doc["textures"] = textures;
                    doc["clips"] = clips;
                    doc["audio"] = audio;
                    var sb = new StringBuilder(1 << 22);
                    J.Write(sb, doc);
                    File.WriteAllText(Path.Combine(dir, "scene.json"), sb.ToString(), new UTF8Encoding(false));
                }
                report.Add($"[{sceneName}] nodes {current.nodes.Count}, meshes {meshes.Count} ({vertexTotal} verts), materials {materials.Count}, " +
                           $"textures {textures.Count}, clips {clips.Count}, lights {lights}, particle systems {particles}, texts {tmps}, sprites {sprites}, templates {templates.Count}");
                report.Add($"[{sceneName}] shaders: " + string.Join(", ", shaderCount.Select(k => $"{k.Key} x{k.Value}")));
                report.Add($"[{sceneName}] behaviours: " + string.Join(", ", behCount.Select(k => $"{k.Key} x{k.Value}")));
                if (otherComps.Count > 0) report.Add($"[{sceneName}] other components (not exported): " + string.Join(", ", otherComps));
                foreach (var m in missingTex) report.Add($"[{sceneName}] MISSING texture file: {m}");
            }

            static bool IsUi(GameObject go) =>
                go.GetComponent<Canvas>() != null || go.GetComponent<UnityEngine.EventSystems.EventSystem>() != null;

            static void Collect(GameObject go, Graph g)
            {
                g.ids[go] = g.order.Count;
                g.order.Add(go);
                foreach (Transform c in go.transform)
                    if (!IsUi(c.gameObject)) Collect(c.gameObject, g);
            }

            static string PathOf(GameObject go)
            {
                var parts = new List<string>();
                for (var t = go.transform; t != null; t = t.parent) parts.Add(t.name);
                parts.Reverse();
                return string.Join("/", parts);
            }

            // ---------------------------------------------------------- node
            Dictionary<string, object> Node(GameObject go, Graph g)
            {
                var t = go.transform;
                var n = new Dictionary<string, object>
                {
                    ["id"] = g.ids[go],
                    ["parent"] = t.parent != null && g.ids.TryGetValue(t.parent.gameObject, out int pid) ? pid : -1,
                    ["name"] = go.name,
                    ["path"] = PathOf(go),
                    ["active"] = go.activeSelf,
                    ["layer"] = go.layer,
                    ["tag"] = go.tag,
                    ["static"] = (int)GameObjectUtility.GetStaticEditorFlags(go),
                    ["lp"] = t.localPosition,
                    ["lr"] = t.localRotation,
                    ["ls"] = t.localScale,
                    ["wm"] = t.localToWorldMatrix,
                };
                if (PrefabUtility.IsAnyPrefabInstanceRoot(go))
                    n["prefab"] = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);

                var beh = new List<object>();
                var cols = new List<object>();
                bool isText = go.GetComponent<TMP_Text>() != null;
                foreach (var c in go.GetComponents<Component>())
                {
                    if (c == null) { otherComps.Add("(missing script)"); continue; }
                    switch (c)
                    {
                        case Transform _: break;
                        case MeshFilter _: break;
                        case MeshRenderer mr:
                            if (isText) break;
                            var mf = go.GetComponent<MeshFilter>();
                            n["mr"] = new Dictionary<string, object>
                            {
                                ["mesh"] = mf != null ? MeshId(mf.sharedMesh) : -1,
                                ["mats"] = mr.sharedMaterials.Select(MatId).ToList(),
                                ["cast"] = mr.shadowCastingMode.ToString(),
                                ["receive"] = mr.receiveShadows,
                                ["enabled"] = mr.enabled,
                            };
                            break;
                        case SkinnedMeshRenderer _:
                            otherComps.Add("SkinnedMeshRenderer");
                            break;
                        case SpriteRenderer sr:
                            sprites++;
                            n["sprite"] = new Dictionary<string, object>
                            {
                                ["enabled"] = sr.enabled,
                                ["color"] = sr.color,
                                ["flipX"] = sr.flipX,
                                ["flipY"] = sr.flipY,
                                ["mat"] = MatId(sr.sharedMaterial),
                                ["sprite"] = SpriteData(sr.sprite),
                                ["drawMode"] = sr.drawMode.ToString(),
                                ["size"] = sr.size,
                                ["order"] = sr.sortingOrder,
                            };
                            break;
                        case Light l:
                            lights++;
                            n["light"] = new Dictionary<string, object>
                            {
                                ["type"] = l.type.ToString(),
                                ["enabled"] = l.enabled,
                                ["color"] = l.color,
                                ["colorLin"] = l.color.linear,
                                ["intensity"] = l.intensity,
                                ["range"] = l.range,
                                ["spotAngle"] = l.spotAngle,
                                ["innerSpotAngle"] = l.innerSpotAngle,
                                ["shadows"] = l.shadows.ToString(),
                                ["shadowStrength"] = l.shadowStrength,
                                ["shadowNear"] = l.shadowNearPlane,
                                ["bounce"] = l.bounceIntensity,
                            };
                            break;
                        case Camera cam:
                            n["camera"] = new Dictionary<string, object>
                            {
                                ["enabled"] = cam.enabled,
                                ["fov"] = cam.fieldOfView,
                                ["near"] = cam.nearClipPlane,
                                ["far"] = cam.farClipPlane,
                                ["ortho"] = cam.orthographic,
                                ["orthoSize"] = cam.orthographicSize,
                                ["main"] = go.CompareTag("MainCamera"),
                            };
                            break;
                        case Collider col:
                            cols.Add(ColliderData(col));
                            break;
                        case Rigidbody rb:
                            n["rb"] = new Dictionary<string, object>
                            {
                                ["kinematic"] = rb.isKinematic,
                                ["mass"] = rb.mass,
                                ["gravity"] = rb.useGravity,
                                ["drag"] = rb.linearDamping,
                                ["angularDrag"] = rb.angularDamping,
                            };
                            break;
                        case ParticleSystem ps:
                            particles++;
                            n["ps"] = Particles(ps);
                            break;
                        case ParticleSystemRenderer _: break;
                        case TMP_Text tmp:
                            tmps++;
                            n["tmp"] = TextData(tmp);
                            break;
                        case Animation anim:
                            var aclips = AnimationUtility.GetAnimationClips(go);
                            n["anim"] = new Dictionary<string, object>
                            {
                                ["clips"] = aclips.Where(x => x != null).Select(ClipId).ToList(),
                                ["clip"] = anim.clip != null ? ClipId(anim.clip) : -1,
                                ["auto"] = anim.playAutomatically,
                                ["wrap"] = anim.wrapMode.ToString(),
                                ["physics"] = anim.animatePhysics,
                                ["enabled"] = anim.enabled,
                            };
                            break;
                        case Animator animator:
                            var ctrl = animator.runtimeAnimatorController;
                            n["animator"] = new Dictionary<string, object>
                            {
                                ["enabled"] = animator.enabled,
                                ["speed"] = animator.speed,
                                ["controller"] = ctrl != null ? ctrl.name : null,
                                ["clips"] = ctrl != null ? ctrl.animationClips.Where(x => x != null).Distinct().Select(ClipId).ToList() : new List<int>(),
                            };
                            break;
                        case AudioSource src:
                            if (!n.ContainsKey("audio")) n["audio"] = new List<object>();
                            ((List<object>)n["audio"]).Add(new Dictionary<string, object>
                            {
                                ["clip"] = AudioName(src.clip),
                                ["playOnAwake"] = src.playOnAwake,
                                ["loop"] = src.loop,
                                ["volume"] = src.volume,
                                ["spatial"] = src.spatialBlend,
                                ["enabled"] = src.enabled,
                            });
                            break;
                        case ReflectionProbe rp:
                            n["probe"] = new Dictionary<string, object>
                            {
                                ["size"] = rp.size,
                                ["center"] = rp.center,
                                ["intensity"] = rp.intensity,
                                ["mode"] = rp.mode.ToString(),
                            };
                            break;
                        case Volume vol:
                            n["volume"] = VolumeData(vol);
                            break;
                        case MonoBehaviour mb:
                            string type = mb.GetType().FullName;
                            behCount[type] = behCount.TryGetValue(type, out int k) ? k + 1 : 1;
                            beh.Add(new Dictionary<string, object>
                            {
                                ["type"] = mb.GetType().Name,
                                ["full"] = type,
                                ["enabled"] = mb.enabled,
                                ["f"] = Fields(mb),
                            });
                            break;
                        default:
                            otherComps.Add(c.GetType().Name);
                            break;
                    }
                }
                if (cols.Count > 0) n["colliders"] = cols;
                if (beh.Count > 0) n["beh"] = beh;
                return n;
            }

            Dictionary<string, object> ColliderData(Collider c)
            {
                var d = new Dictionary<string, object>
                {
                    ["type"] = c.GetType().Name,
                    ["trigger"] = c.isTrigger,
                    ["enabled"] = c.enabled,
                };
                switch (c)
                {
                    case BoxCollider b: d["center"] = b.center; d["size"] = b.size; break;
                    case SphereCollider s: d["center"] = s.center; d["radius"] = s.radius; break;
                    case CapsuleCollider cc: d["center"] = cc.center; d["radius"] = cc.radius; d["height"] = cc.height; d["direction"] = cc.direction; break;
                    case MeshCollider m: d["mesh"] = MeshId(m.sharedMesh); d["convex"] = m.convex; break;
                }
                var pm = c.sharedMaterial;
                if (pm != null)
                    d["phys"] = new Dictionary<string, object>
                    {
                        ["name"] = pm.name, ["dynamic"] = pm.dynamicFriction, ["static"] = pm.staticFriction, ["bounce"] = pm.bounciness,
                    };
                return d;
            }

            // ---------------------------------------------------------- render settings
            Dictionary<string, object> Render(GameManager gm, Graph g)
            {
                var sun = RenderSettings.sun;
                return new Dictionary<string, object>
                {
                    ["fog"] = RenderSettings.fog,
                    ["fogMode"] = RenderSettings.fogMode.ToString(),
                    ["fogColor"] = RenderSettings.fogColor,
                    ["fogColorLin"] = RenderSettings.fogColor.linear,
                    ["fogDensity"] = RenderSettings.fogDensity,
                    ["fogStart"] = RenderSettings.fogStartDistance,
                    ["fogEnd"] = RenderSettings.fogEndDistance,
                    ["ambientMode"] = RenderSettings.ambientMode.ToString(),
                    ["ambient"] = RenderSettings.ambientLight,
                    ["ambientLin"] = RenderSettings.ambientLight.linear,
                    // GameManager.SetAmbient puts the raw colour into the SH probe: that is the value the game renders with
                    ["ambientGame"] = gm != null ? (object)gm.ambient : null,
                    ["skybox"] = MatId(RenderSettings.skybox),
                    ["sun"] = sun != null && g.ids.TryGetValue(sun.gameObject, out int sid) ? sid : -1,
                    ["reflectionIntensity"] = RenderSettings.reflectionIntensity,
                    ["reflectionMode"] = RenderSettings.defaultReflectionMode.ToString(),
                };
            }

            Dictionary<string, object> VolumeData(Volume vol)
            {
                var prof = vol.sharedProfile;
                var comps = new List<object>();
                if (prof != null)
                    foreach (var c in prof.components)
                    {
                        if (c == null) continue;
                        var ps = new Dictionary<string, object>();
                        foreach (var f in c.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (!typeof(VolumeParameter).IsAssignableFrom(f.FieldType)) continue;
                            var p = (VolumeParameter)f.GetValue(c);
                            if (p == null) continue;
                            var vp = p.GetType().GetProperty("value", BindingFlags.Public | BindingFlags.Instance);
                            object v = null;
                            try { v = vp != null ? vp.GetValue(p) : null; } catch (Exception) { }
                            ps[f.Name] = new Dictionary<string, object> { ["o"] = p.overrideState, ["v"] = Plain(v) };
                        }
                        comps.Add(new Dictionary<string, object> { ["type"] = c.GetType().Name, ["active"] = c.active, ["params"] = ps });
                    }
                return new Dictionary<string, object>
                {
                    ["global"] = vol.isGlobal,
                    ["priority"] = vol.priority,
                    ["weight"] = vol.weight,
                    ["enabled"] = vol.enabled,
                    ["profile"] = prof != null ? prof.name : null,
                    ["components"] = comps,
                };
            }

            static object Plain(object v)
            {
                switch (v)
                {
                    case null: return null;
                    case float _: case int _: case bool _: case string _: case double _: return v;
                    case Color c: return new Dictionary<string, object> { ["raw"] = c, ["lin"] = c.linear };
                    case Vector2 _: case Vector3 _: case Vector4 _: return v;
                    case Enum e: return e.ToString();
                    case Object o: return o != null ? o.name : null;   // Unity "fake null" (unassigned texture parameter)
                    default: return v.ToString();
                }
            }

            // ---------------------------------------------------------- serialized fields
            Dictionary<string, object> Fields(Object c)
            {
                var f = new Dictionary<string, object>();
                var so = new SerializedObject(c);
                var it = so.GetIterator();
                bool enter = true;
                while (it.NextVisible(enter))
                {
                    enter = it.propertyType == SerializedPropertyType.Generic;
                    if (it.propertyPath == "m_Script") continue;
                    object v;
                    switch (it.propertyType)
                    {
                        case SerializedPropertyType.Generic: continue;
                        case SerializedPropertyType.ArraySize: v = it.intValue; break;
                        case SerializedPropertyType.Integer: v = it.longValue; break;
                        case SerializedPropertyType.LayerMask: v = it.intValue; break;
                        case SerializedPropertyType.Boolean: v = it.boolValue; break;
                        case SerializedPropertyType.Float: v = it.doubleValue; break;
                        case SerializedPropertyType.String: v = it.stringValue; break;
                        case SerializedPropertyType.Character: v = it.intValue; break;
                        case SerializedPropertyType.Color:
                            v = new Dictionary<string, object> { ["raw"] = it.colorValue, ["lin"] = it.colorValue.linear }; break;
                        case SerializedPropertyType.Enum:
                            v = it.enumValueIndex >= 0 && it.enumValueIndex < it.enumNames.Length ? it.enumNames[it.enumValueIndex] : (object)it.intValue; break;
                        case SerializedPropertyType.Vector2: v = it.vector2Value; break;
                        case SerializedPropertyType.Vector3: v = it.vector3Value; break;
                        case SerializedPropertyType.Vector4: v = it.vector4Value; break;
                        case SerializedPropertyType.Quaternion: v = it.quaternionValue; break;
                        case SerializedPropertyType.Rect: var r = it.rectValue; v = new[] { r.x, r.y, r.width, r.height }; break;
                        case SerializedPropertyType.Bounds: var b = it.boundsValue; v = new object[] { b.center, b.size }; break;
                        case SerializedPropertyType.AnimationCurve: v = Keys(it.animationCurveValue); break;
                        case SerializedPropertyType.Gradient: v = Grad(it.gradientValue); break;
                        case SerializedPropertyType.ObjectReference: v = Ref(it.objectReferenceValue); break;
                        default: v = it.propertyType.ToString(); break;
                    }
                    f[it.propertyPath] = v;
                }
                return f;
            }

            object Ref(Object o)
            {
                if (o == null) return null;
                if (o is AudioClip ac) return new Dictionary<string, object> { ["audio"] = AudioName(ac) };
                if (o is AnimationClip clip) return new Dictionary<string, object> { ["clip"] = ClipId(clip) };
                if (o is Material m) return new Dictionary<string, object> { ["mat"] = MatId(m) };
                if (o is Mesh mesh) return new Dictionary<string, object> { ["mesh"] = MeshId(mesh) };
                if (o is Texture tex) return new Dictionary<string, object> { ["tex"] = TexId(tex) };
                GameObject go = o as GameObject ?? (o as Component)?.gameObject;
                if (go != null)
                {
                    if (current.ids.TryGetValue(go, out int id))
                        return new Dictionary<string, object> { ["node"] = id, ["comp"] = o is Component ? o.GetType().Name : "GameObject" };
                    if (EditorUtility.IsPersistent(go))
                    {
                        var root = go.transform.root.gameObject;
                        string path = AssetDatabase.GetAssetPath(root);
                        if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab") && templateQueued.Add(path)) templateQueue.Enqueue(root);
                        return new Dictionary<string, object> { ["asset"] = path, ["name"] = go.name, ["comp"] = o.GetType().Name, ["template"] = path.EndsWith(".prefab") };
                    }
                    return new Dictionary<string, object> { ["missing"] = PathOf(go) };
                }
                return new Dictionary<string, object> { ["asset"] = AssetDatabase.GetAssetPath(o), ["name"] = o.name, ["type"] = o.GetType().Name };
            }

            string AudioName(AudioClip c)
            {
                if (c == null) return null;
                string p = AssetDatabase.GetAssetPath(c);
                if (!string.IsNullOrEmpty(p)) audio[c.name] = Abs(p);
                return c.name;
            }

            static string Abs(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath)).Replace('\\', '/');

            // ---------------------------------------------------------- meshes
            int MeshId(Mesh m)
            {
                if (m == null) return -1;
                if (meshIds.TryGetValue(m, out int id)) return id;
                id = meshes.Count;
                meshIds[m] = id;
                var d = new Dictionary<string, object> { ["id"] = id, ["name"] = m.name, ["asset"] = AssetDatabase.GetAssetPath(m) };
                try
                {
                    var v = m.vertices;
                    var nrm = m.normals;
                    var uv = new List<Vector2>();
                    m.GetUVs(0, uv);
                    var col = m.colors;
                    d["vcount"] = v.Length;
                    vertexTotal += v.Length;
                    d["pos"] = WriteV3(v);
                    if (nrm != null && nrm.Length == v.Length) d["nrm"] = WriteV3(nrm);
                    if (uv.Count == v.Length) d["uv0"] = WriteV2(uv);
                    if (col != null && col.Length == v.Length) d["col"] = WriteCol(col);
                    var subs = new List<object>();
                    for (int i = 0; i < m.subMeshCount; i++)
                    {
                        if (m.GetTopology(i) != MeshTopology.Triangles) { report.Add($"[{sceneName}] mesh {m.name} submesh {i} is {m.GetTopology(i)}"); subs.Add(null); continue; }
                        subs.Add(WriteU32(m.GetTriangles(i)));
                    }
                    d["sub"] = subs;
                    d["bounds"] = new object[] { m.bounds.center, m.bounds.size };
                }
                catch (Exception e)
                {
                    report.Add($"[{sceneName}] UNREADABLE mesh {m.name} ({AssetDatabase.GetAssetPath(m)}): {e.Message}");
                    d["error"] = e.Message;
                }
                meshes.Add(d);
                return id;
            }

            object[] WriteV3(Vector3[] a)
            {
                long off = bin.BaseStream.Position;
                foreach (var x in a) { bin.Write(x.x); bin.Write(x.y); bin.Write(x.z); }
                return new object[] { off, a.Length };
            }

            object[] WriteV2(List<Vector2> a)
            {
                long off = bin.BaseStream.Position;
                foreach (var x in a) { bin.Write(x.x); bin.Write(x.y); }
                return new object[] { off, a.Count };
            }

            object[] WriteCol(Color[] a)
            {
                long off = bin.BaseStream.Position;
                foreach (var x in a) { bin.Write(x.r); bin.Write(x.g); bin.Write(x.b); bin.Write(x.a); }
                return new object[] { off, a.Length };
            }

            object[] WriteU32(int[] a)
            {
                long off = bin.BaseStream.Position;
                foreach (var x in a) bin.Write((uint)x);
                return new object[] { off, a.Length };
            }

            // ---------------------------------------------------------- materials and textures
            int MatId(Material m)
            {
                if (m == null) return -1;
                if (matIds.TryGetValue(m, out int id)) return id;
                id = materials.Count;
                matIds[m] = id;
                var d = new Dictionary<string, object>();
                materials.Add(d);
                var sh = m.shader;
                string shName = sh != null ? sh.name : "(none)";
                shaderCount[shName] = shaderCount.TryGetValue(shName, out int k) ? k + 1 : 1;
                var props = new Dictionary<string, object>();
                if (sh != null)
                    for (int i = 0; i < sh.GetPropertyCount(); i++)
                    {
                        string pn = sh.GetPropertyName(i);
                        switch (sh.GetPropertyType(i))
                        {
                            case ShaderPropertyType.Color:
                                var c = m.GetColor(pn);
                                // [HDR] colours reach the shader as stored (verified against the game's screenshots);
                                // ordinary colours are converted gamma -> linear
                                bool hdr = (sh.GetPropertyFlags(i) & ShaderPropertyFlags.HDR) != 0;
                                props[pn] = new Dictionary<string, object> { ["t"] = "Color", ["v"] = c, ["lin"] = c.linear, ["hdr"] = hdr };
                                break;
                            case ShaderPropertyType.Vector:
                                props[pn] = new Dictionary<string, object> { ["t"] = "Vector", ["v"] = m.GetVector(pn) };
                                break;
                            case ShaderPropertyType.Float:
                            case ShaderPropertyType.Range:
                                props[pn] = new Dictionary<string, object> { ["t"] = "Float", ["v"] = m.GetFloat(pn) };
                                break;
                            case ShaderPropertyType.Int:
                                props[pn] = new Dictionary<string, object> { ["t"] = "Float", ["v"] = (float)m.GetInteger(pn) };
                                break;
                            case ShaderPropertyType.Texture:
                                var tex = m.GetTexture(pn);
                                var sc = m.GetTextureScale(pn);
                                var of = m.GetTextureOffset(pn);
                                props[pn] = new Dictionary<string, object> { ["t"] = "Texture", ["tex"] = TexId(tex), ["st"] = new[] { sc.x, sc.y, of.x, of.y } };
                                break;
                        }
                    }
                d["id"] = id;
                d["name"] = m.name;
                d["path"] = AssetDatabase.GetAssetPath(m);
                d["shader"] = shName;
                d["queue"] = m.renderQueue;
                d["keywords"] = m.shaderKeywords;
                d["props"] = props;
                return id;
            }

            int TexId(Texture t)
            {
                if (t == null) return -1;
                if (texIds.TryGetValue(t, out int id)) return id;
                id = textures.Count;
                texIds[t] = id;
                string p = AssetDatabase.GetAssetPath(t);
                var d = new Dictionary<string, object> { ["id"] = id, ["name"] = t.name, ["w"] = t.width, ["h"] = t.height, ["asset"] = p };
                bool builtin = string.IsNullOrEmpty(p) || p.StartsWith("Resources/unity_builtin") || p.StartsWith("Library/");
                if (builtin) d["builtin"] = t.name;
                else
                {
                    string abs = Abs(p);
                    d["path"] = abs;
                    bool embedded = !(p.EndsWith(".png", true, null) || p.EndsWith(".jpg", true, null) || p.EndsWith(".jpeg", true, null) ||
                                      p.EndsWith(".tga", true, null) || p.EndsWith(".tif", true, null) || p.EndsWith(".tiff", true, null) ||
                                      p.EndsWith(".psd", true, null) || p.EndsWith(".exr", true, null) || p.EndsWith(".bmp", true, null));
                    if (embedded) d["embedded"] = true;
                    else if (!File.Exists(abs)) missingTex.Add(abs);
                    if (AssetImporter.GetAtPath(p) is TextureImporter ti)
                        d["importer"] = new Dictionary<string, object>
                        {
                            ["type"] = ti.textureType.ToString(),
                            ["sRGB"] = ti.sRGBTexture,
                            ["toNormal"] = ti.convertToNormalmap,
                            ["bump"] = ti.heightmapScale,
                            ["wrap"] = ti.wrapMode.ToString(),
                            ["filter"] = ti.filterMode.ToString(),
                            ["alphaIsTransparency"] = ti.alphaIsTransparency,
                        };
                }
                textures.Add(d);
                return id;
            }

            Dictionary<string, object> SpriteData(Sprite s)
            {
                if (s == null) return null;
                var r = s.textureRect;
                return new Dictionary<string, object>
                {
                    ["name"] = s.name,
                    ["tex"] = TexId(s.texture),
                    ["rect"] = new[] { r.x, r.y, r.width, r.height },
                    ["pivot"] = s.pivot,
                    ["ppu"] = s.pixelsPerUnit,
                    ["verts"] = s.vertices.Select(v => (object)v).ToList(),
                    ["uv"] = s.uv.Select(v => (object)v).ToList(),
                    ["tris"] = s.triangles.Select(x => (int)x).ToList(),
                };
            }

            // ---------------------------------------------------------- clips (sampled, never played)
            int ClipId(AnimationClip c)
            {
                if (c == null) return -1;
                if (clipIds.TryGetValue(c, out int id)) return id;
                id = clips.Count;
                clipIds[c] = id;
                var d = new Dictionary<string, object>();
                clips.Add(d);
                var curves = new List<object>();
                foreach (var b in AnimationUtility.GetCurveBindings(c))
                {
                    var ac = AnimationUtility.GetEditorCurve(c, b);
                    if (ac == null) continue;
                    int n = Mathf.Max(2, Mathf.CeilToInt(c.length * CurveRate) + 1);
                    var s = new float[n];
                    for (int i = 0; i < n; i++) s[i] = ac.Evaluate(Mathf.Min(i / (float)CurveRate, c.length));
                    curves.Add(new Dictionary<string, object>
                    {
                        ["path"] = b.path, ["type"] = b.type != null ? b.type.Name : null, ["prop"] = b.propertyName,
                        ["rate"] = CurveRate, ["s"] = s, ["keys"] = Keys(ac),
                    });
                }
                var pptr = new List<object>();
                foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(c))
                {
                    var keys = AnimationUtility.GetObjectReferenceCurve(c, b);
                    pptr.Add(new Dictionary<string, object>
                    {
                        ["path"] = b.path, ["type"] = b.type != null ? b.type.Name : null, ["prop"] = b.propertyName,
                        ["keys"] = keys.Select(k => (object)new Dictionary<string, object>
                        {
                            ["t"] = k.time,
                            ["sprite"] = k.value is Sprite sp ? SpriteData(sp) : null,
                            ["name"] = k.value != null ? k.value.name : null,
                        }).ToList(),
                    });
                }
                var events = AnimationUtility.GetAnimationEvents(c).Select(e => (object)new Dictionary<string, object>
                {
                    ["t"] = e.time, ["fn"] = e.functionName, ["s"] = e.stringParameter, ["i"] = e.intParameter, ["f"] = e.floatParameter,
                    ["o"] = e.objectReferenceParameter != null ? e.objectReferenceParameter.name : null,
                }).ToList();
                d["id"] = id;
                d["name"] = c.name;
                d["path"] = AssetDatabase.GetAssetPath(c);
                d["length"] = c.length;
                d["frameRate"] = c.frameRate;
                d["wrap"] = c.wrapMode.ToString();
                d["loop"] = c.isLooping;
                d["legacy"] = c.legacy;
                d["curves"] = curves;
                d["pptr"] = pptr;
                d["events"] = events;
                return id;
            }

            static List<object> Keys(AnimationCurve ac) =>
                ac == null ? null : ac.keys.Select(k => (object)new[] { k.time, k.value, k.inTangent, k.outTangent }).ToList();

            static Dictionary<string, object> Grad(Gradient g)
            {
                if (g == null) return null;
                return new Dictionary<string, object>
                {
                    ["mode"] = g.mode.ToString(),
                    ["colors"] = g.colorKeys.Select(k => (object)new[] { k.time, k.color.r, k.color.g, k.color.b }).ToList(),
                    ["alphas"] = g.alphaKeys.Select(k => (object)new[] { k.time, k.alpha }).ToList(),
                };
            }

            // ---------------------------------------------------------- particles
            static Dictionary<string, object> Curve(ParticleSystem.MinMaxCurve c) => new Dictionary<string, object>
            {
                ["mode"] = c.mode.ToString(),
                ["constant"] = c.constant,
                ["min"] = c.constantMin,
                ["max"] = c.constantMax,
                ["mult"] = c.curveMultiplier,
                ["curve"] = Keys(c.curve),
                ["curveMin"] = Keys(c.curveMin),
                ["curveMax"] = Keys(c.curveMax),
            };

            static Dictionary<string, object> MinMaxColor(ParticleSystem.MinMaxGradient g) => new Dictionary<string, object>
            {
                ["mode"] = g.mode.ToString(),
                ["color"] = g.color,
                ["colorMin"] = g.colorMin,
                ["colorMax"] = g.colorMax,
                ["gradient"] = Grad(g.gradient),
                ["gradientMin"] = Grad(g.gradientMin),
                ["gradientMax"] = Grad(g.gradientMax),
            };

            Dictionary<string, object> Particles(ParticleSystem ps)
            {
                var m = ps.main;
                var e = ps.emission;
                var bursts = new ParticleSystem.Burst[e.burstCount];
                e.GetBursts(bursts);
                var s = ps.shape;
                var col = ps.colorOverLifetime;
                var sol = ps.sizeOverLifetime;
                var vel = ps.velocityOverLifetime;
                var lim = ps.limitVelocityOverLifetime;
                var frc = ps.forceOverLifetime;
                var noise = ps.noise;
                var rot = ps.rotationOverLifetime;
                var tsa = ps.textureSheetAnimation;
                var r = ps.GetComponent<ParticleSystemRenderer>();
                return new Dictionary<string, object>
                {
                    ["main"] = new Dictionary<string, object>
                    {
                        ["duration"] = m.duration, ["loop"] = m.loop, ["prewarm"] = m.prewarm, ["playOnAwake"] = m.playOnAwake,
                        ["startDelay"] = Curve(m.startDelay), ["startLifetime"] = Curve(m.startLifetime), ["startSpeed"] = Curve(m.startSpeed),
                        ["startSize"] = Curve(m.startSize), ["startRotation"] = Curve(m.startRotation), ["startColor"] = MinMaxColor(m.startColor),
                        ["gravity"] = Curve(m.gravityModifier), ["space"] = m.simulationSpace.ToString(), ["speed"] = m.simulationSpeed,
                        ["scaling"] = m.scalingMode.ToString(), ["maxParticles"] = m.maxParticles,
                    },
                    ["emission"] = new Dictionary<string, object>
                    {
                        ["enabled"] = e.enabled, ["rateOverTime"] = Curve(e.rateOverTime), ["rateOverDistance"] = Curve(e.rateOverDistance),
                        ["bursts"] = bursts.Select(b => (object)new Dictionary<string, object>
                        {
                            ["time"] = b.time, ["count"] = Curve(b.count), ["cycles"] = b.cycleCount, ["interval"] = b.repeatInterval, ["probability"] = b.probability,
                        }).ToList(),
                    },
                    ["shape"] = new Dictionary<string, object>
                    {
                        ["enabled"] = s.enabled, ["type"] = s.shapeType.ToString(), ["angle"] = s.angle, ["radius"] = s.radius,
                        ["thickness"] = s.radiusThickness, ["arc"] = s.arc, ["length"] = s.length, ["scale"] = s.scale,
                        ["position"] = s.position, ["rotation"] = s.rotation, ["randomDirection"] = s.randomDirectionAmount,
                        ["boxThickness"] = s.boxThickness, ["donutRadius"] = s.donutRadius,
                    },
                    ["colorOverLifetime"] = new Dictionary<string, object> { ["enabled"] = col.enabled, ["color"] = MinMaxColor(col.color) },
                    ["sizeOverLifetime"] = new Dictionary<string, object> { ["enabled"] = sol.enabled, ["size"] = Curve(sol.size), ["separate"] = sol.separateAxes },
                    ["velocityOverLifetime"] = new Dictionary<string, object>
                    {
                        ["enabled"] = vel.enabled, ["x"] = Curve(vel.x), ["y"] = Curve(vel.y), ["z"] = Curve(vel.z), ["space"] = vel.space.ToString(),
                        ["orbitalY"] = Curve(vel.orbitalY), ["radial"] = Curve(vel.radial), ["speedModifier"] = Curve(vel.speedModifier),
                    },
                    ["limitVelocity"] = new Dictionary<string, object> { ["enabled"] = lim.enabled, ["limit"] = Curve(lim.limit), ["dampen"] = lim.dampen, ["drag"] = Curve(lim.drag) },
                    ["force"] = new Dictionary<string, object> { ["enabled"] = frc.enabled, ["x"] = Curve(frc.x), ["y"] = Curve(frc.y), ["z"] = Curve(frc.z), ["space"] = frc.space.ToString() },
                    ["noise"] = new Dictionary<string, object>
                    {
                        ["enabled"] = noise.enabled, ["strength"] = Curve(noise.strength), ["frequency"] = noise.frequency,
                        ["scroll"] = Curve(noise.scrollSpeed), ["octaves"] = noise.octaveCount, ["damping"] = noise.damping,
                    },
                    ["rotation"] = new Dictionary<string, object> { ["enabled"] = rot.enabled, ["z"] = Curve(rot.z) },
                    ["sheet"] = new Dictionary<string, object>
                    {
                        ["enabled"] = tsa.enabled, ["tilesX"] = tsa.numTilesX, ["tilesY"] = tsa.numTilesY, ["frameOverTime"] = Curve(tsa.frameOverTime), ["cycles"] = tsa.cycleCount,
                    },
                    ["renderer"] = r == null ? null : new Dictionary<string, object>
                    {
                        ["enabled"] = r.enabled, ["mode"] = r.renderMode.ToString(), ["mat"] = MatId(r.sharedMaterial),
                        ["minSize"] = r.minParticleSize, ["maxSize"] = r.maxParticleSize, ["lengthScale"] = r.lengthScale,
                        ["velocityScale"] = r.velocityScale, ["alignment"] = r.alignment.ToString(),
                    },
                };
            }

            // ---------------------------------------------------------- text
            Dictionary<string, object> TextData(TMP_Text t)
            {
                t.ForceMeshUpdate(true, true);
                var ti = t.textInfo;
                var lines = new List<object>();
                for (int i = 0; i < ti.lineCount; i++)
                {
                    var li = ti.lineInfo[i];
                    lines.Add(new Dictionary<string, object>
                    {
                        ["first"] = li.firstCharacterIndex, ["last"] = li.lastCharacterIndex, ["visibleLast"] = li.lastVisibleCharacterIndex,
                        ["baseline"] = li.baseline, ["ascender"] = li.ascender, ["descender"] = li.descender,
                        ["min"] = li.lineExtents.min, ["max"] = li.lineExtents.max, ["width"] = li.width,
                    });
                }
                var chars = new List<object>();
                for (int c = 0; c < ti.characterCount; c++)
                {
                    var ci = ti.characterInfo[c];
                    chars.Add(new Dictionary<string, object>
                    {
                        ["ch"] = (int)ci.character, ["vis"] = ci.isVisible, ["pt"] = ci.pointSize, ["line"] = ci.lineNumber,
                        ["bl"] = ci.bottomLeft, ["tr"] = ci.topRight, ["base"] = ci.baseLine, ["asc"] = ci.ascender, ["desc"] = ci.descender,
                        ["origin"] = ci.origin, ["adv"] = ci.xAdvance, ["scale"] = ci.scale,
                    });
                }
                var rt = t.rectTransform;
                return new Dictionary<string, object>
                {
                    ["text"] = t.text,
                    ["parsed"] = t.GetParsedText(),
                    ["fontSize"] = t.fontSize,
                    ["spacing"] = t.characterSpacing,
                    ["lineSpacing"] = t.lineSpacing,
                    ["alignment"] = t.alignment.ToString(),
                    ["size"] = rt.sizeDelta,
                    ["pivot"] = rt.pivot,
                    ["color"] = t.color,
                    ["font"] = t.font != null ? t.font.name : null,
                    ["face"] = t.font != null ? new Dictionary<string, object>
                    {
                        ["pointSize"] = t.font.faceInfo.pointSize, ["scale"] = t.font.faceInfo.scale, ["unitsPerEm"] = t.font.faceInfo.unitsPerEM,
                        ["ascent"] = t.font.faceInfo.ascentLine, ["descent"] = t.font.faceInfo.descentLine, ["cap"] = t.font.faceInfo.capLine,
                        ["lineHeight"] = t.font.faceInfo.lineHeight,
                    } : null,
                    ["mat"] = MatId(t.fontSharedMaterial),
                    ["bounds"] = new object[] { t.textBounds.center, t.textBounds.size },
                    ["lines"] = lines,
                    ["chars"] = chars,
                };
            }
        }

        // ================================================================== tiny JSON writer (invariant culture)
        static class J
        {
            public static void Write(StringBuilder sb, object o)
            {
                switch (o)
                {
                    case null: sb.Append("null"); return;
                    case string s: Str(sb, s); return;
                    case bool b: sb.Append(b ? "true" : "false"); return;
                    case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
                    case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                    case uint u: sb.Append(u.ToString(CultureInfo.InvariantCulture)); return;
                    case float f: Num(sb, f); return;
                    case double d: Dbl(sb, d); return;
                    case Vector2 v: Arr(sb, v.x, v.y); return;
                    case Vector3 v: Arr(sb, v.x, v.y, v.z); return;
                    case Vector4 v: Arr(sb, v.x, v.y, v.z, v.w); return;
                    case Quaternion q: Arr(sb, q.x, q.y, q.z, q.w); return;
                    case Color c: Arr(sb, c.r, c.g, c.b, c.a); return;
                    case Matrix4x4 m:
                        Arr(sb, m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13, m.m20, m.m21, m.m22, m.m23, m.m30, m.m31, m.m32, m.m33);
                        return;
                    case Enum e: Str(sb, e.ToString()); return;
                    case IDictionary dict:
                        sb.Append('{');
                        bool first = true;
                        foreach (DictionaryEntry kv in dict)
                        {
                            if (!first) sb.Append(',');
                            first = false;
                            Str(sb, kv.Key.ToString());
                            sb.Append(':');
                            Write(sb, kv.Value);
                        }
                        sb.Append('}');
                        return;
                    case IEnumerable list:
                        sb.Append('[');
                        bool f0 = true;
                        foreach (var x in list)
                        {
                            if (!f0) sb.Append(',');
                            f0 = false;
                            Write(sb, x);
                        }
                        sb.Append(']');
                        return;
                    default: Str(sb, o.ToString()); return;
                }
            }

            static void Num(StringBuilder sb, float f)
            {
                if (float.IsNaN(f) || float.IsInfinity(f)) sb.Append("null");
                else sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
            }

            static void Dbl(StringBuilder sb, double d)
            {
                if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null");
                else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            }

            static void Arr(StringBuilder sb, params float[] a)
            {
                sb.Append('[');
                for (int i = 0; i < a.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    Num(sb, a[i]);
                }
                sb.Append(']');
            }

            static void Str(StringBuilder sb, string s)
            {
                sb.Append('"');
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else sb.Append(c);
                            break;
                    }
                }
                sb.Append('"');
            }
        }
    }
}
