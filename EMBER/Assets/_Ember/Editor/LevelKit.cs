using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Ember.EditorTools
{
    /// <summary>
    /// Small DSL the stage builders use to lay out levels in code: chamfered architecture,
    /// prefab placement and one-line wiring of the class Condition/Action scripts.
    /// Everything it creates is an ordinary scene object, so the generated scenes can be
    /// opened and edited by hand afterwards.
    /// </summary>
    public class LevelKit
    {
        public readonly Transform root, geo, deco, puzzle, fx, lights, triggers, beats;
        public readonly string stage;
        public Material floorMat, wallMat, trimMat;
        public Transform player;
        public StageSolution solution;

        public LevelKit(string stage, Material floor, Material wall, Material trim)
        {
            this.stage = stage;
            floorMat = floor;
            wallMat = wall;
            trimMat = trim;
            root = new GameObject("LEVEL").transform;
            geo = Group("Geometry");
            deco = Group("Decor");
            puzzle = Group("Puzzles");
            fx = Group("FX");
            lights = Group("Lights");
            triggers = Group("Triggers");
            beats = Group("Beats");
            solution = new GameObject("StageSolution").AddComponent<StageSolution>();
            solution.transform.SetParent(root, false);
        }

        Transform Group(string n)
        {
            var t = new GameObject(n).transform;
            t.SetParent(root, false);
            return t;
        }

        public Transform SubGroup(string name, Transform parent = null)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent != null ? parent : puzzle, false);
            return t;
        }

        // ------------------------------------------------------------------ architecture
        public GameObject Block(string name, Vector3 center, Vector3 size, Material mat = null, Transform parent = null,
            float bevel = 0.06f, bool collider = true, bool isStatic = true, float yaw = 0f, float pitch = 0f, float roll = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : geo, false);
            go.transform.position = center;
            go.transform.rotation = Quaternion.Euler(pitch, yaw, roll);
            go.AddComponent<MeshFilter>().sharedMesh = MeshGen.ChamferBox(size, bevel);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat != null ? mat : floorMat;
            if (collider) go.AddComponent<BoxCollider>().size = size;
            if (isStatic)
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        /// <summary>Floor slab whose top surface is at y = top.</summary>
        public GameObject Floor(string name, float x0, float x1, float z0, float z1, float top = 0f, Material mat = null, float thickness = 1f, Transform parent = null)
        {
            return Block(name, new Vector3((x0 + x1) * 0.5f, top - thickness * 0.5f, (z0 + z1) * 0.5f),
                new Vector3(Mathf.Abs(x1 - x0), thickness, Mathf.Abs(z1 - z0)), mat != null ? mat : floorMat, parent);
        }

        public GameObject Wall(string name, float x0, float x1, float z0, float z1, float y0, float y1, Material mat = null, Transform parent = null)
        {
            return Block(name, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)), mat != null ? mat : wallMat, parent, 0.08f);
        }

        /// <summary>Sloped slab: from / to are points on its top surface (centre line).</summary>
        public GameObject Ramp(string name, Vector3 from, Vector3 to, float width, float thickness = 0.6f, Material mat = null, Transform parent = null)
        {
            Vector3 dir = to - from;
            var rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            Vector3 center = (from + to) * 0.5f - rot * Vector3.up * (thickness * 0.5f);
            var go = Block(name, center, new Vector3(width, thickness, dir.magnitude), mat != null ? mat : floorMat, parent);
            go.transform.rotation = rot;
            return go;
        }

        public GameObject Disc(string name, Vector3 topCenter, float radius, float thickness = 1f, Material mat = null, Transform parent = null, int segments = 32)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : geo, false);
            go.transform.position = topCenter - Vector3.up * thickness * 0.5f;
            var mesh = MeshGen.Cylinder(radius, thickness, segments);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat != null ? mat : floorMat;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = true;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
            return go;
        }

        /// <summary>Visual-only quad (water, mist, cloud sea).</summary>
        public GameObject Plane(string name, Vector3 center, Vector2 size, Material mat, Transform parent = null, float yaw = 0f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent != null ? parent : fx, false);
            go.transform.position = center;
            go.transform.rotation = Quaternion.Euler(90f, yaw, 0f);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // ------------------------------------------------------------------ placement
        public GameObject Place(GameObject asset, Vector3 pos, float yaw = 0f, Transform parent = null, string name = null, float scale = 1f)
        {
            if (asset == null) { Debug.LogWarning("[Ember] Place: asset missing for " + name); return new GameObject(name ?? "Missing"); }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            go.transform.SetParent(parent != null ? parent : deco, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            if (name != null) go.name = name;
            return go;
        }

        public GameObject Prefab(string prefab, Vector3 pos, float yaw = 0f, Transform parent = null, string name = null, float scale = 1f)
            => Place(EmberPaths.Prefab(prefab), pos, yaw, parent != null ? parent : puzzle, name, scale);

        public GameObject Model(string model, Vector3 pos, float yaw = 0f, Transform parent = null, float scale = 1f, bool meshCollider = false, string name = null)
        {
            var go = Place(EmberPaths.Model(model), pos, yaw, parent, name, scale);
            if (meshCollider)
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            return go;
        }

        /// <summary>Class package prefab (Session 2 lanterns and candles).</summary>
        public GameObject ClassProp(string path, Vector3 pos, float yaw = 0f, float scale = 1f, Color? light = null, float range = 4f, float intensity = 1.3f)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var go = Place(asset, pos, yaw, deco, null, scale);
            if (light.HasValue)
            {
                var l = PrefabFactory.PointLight(go.transform, "CandleLight", new Vector3(0f, 0.6f, 0f) / Mathf.Max(0.01f, scale), light.Value, intensity, range);
                l.gameObject.AddComponent<Flicker>().amount = 0.22f;
            }
            return go;
        }

        public static void Swap(GameObject go, string from, string to)
        {
            var a = EmberPaths.Mat(from);
            var b = EmberPaths.Mat(to);
            if (a == null || b == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] == a) { mats[i] = b; changed = true; }
                if (changed) r.sharedMaterials = mats;
            }
        }

        public static GameObject Find(GameObject go, string path)
        {
            var t = go.transform.Find(path);
            return t != null ? t.gameObject : null;
        }

        // ------------------------------------------------------------------ class Condition/Action wiring
        public static HitAndShowHide ShowHide(GameObject on, string who, GameObject target, bool show, bool cancelOnExit = false, bool trigger = true)
        {
            var c = on.AddComponent<HitAndShowHide>();
            c.checkType = trigger ? HitAndShowHide.CheckType.Trigger : HitAndShowHide.CheckType.Collision;
            c.objectName = who;
            c.target = target;
            c.action = show ? HitAndShowHide.ShowHide.Show : HitAndShowHide.ShowHide.Hide;
            c.cancelOnExit = cancelOnExit;
            return c;
        }

        public static HitAndPlayAnimation PlayAnim(GameObject on, string who, Animation target, string enter, string exit = "", bool trigger = true)
        {
            var c = on.AddComponent<HitAndPlayAnimation>();
            c.checkType = trigger ? HitAndPlayAnimation.CheckType.Trigger : HitAndPlayAnimation.CheckType.Collision;
            c.objectName = who;
            c.target = target;
            c.enterAnimName = enter;
            c.exitAnimName = exit;
            return c;
        }

        public static HitAndClone Clone(GameObject on, string who, GameObject prefab, Transform start, bool repeat = false,
            float interval = 2f, bool cancelWhenExit = false, Transform parent = null, bool trigger = true)
        {
            var c = on.AddComponent<HitAndClone>();
            c.checkType = trigger ? HitAndClone.CheckType.Trigger : HitAndClone.CheckType.Collision;
            c.objectName = who;
            c.sourcePrefab = prefab != null ? prefab.transform : null;
            c.startPosition = start;
            c.cloneParent = parent;
            c.useRepeating = repeat;
            c.interval = interval;
            c.cancelWhenExit = cancelWhenExit;
            return c;
        }

        /// <summary>target null = destroy whatever hit (the class script's destroySelf mode).</summary>
        public static HitAndDestroy DestroyOnHit(GameObject on, string who, GameObject target, bool trigger = true)
        {
            var c = on.AddComponent<HitAndDestroy>();
            c.checkType = trigger ? HitAndDestroy.CheckType.Trigger : HitAndDestroy.CheckType.Collision;
            c.objectName = who;
            c.destroySelf = target == null;
            c.target = target;
            return c;
        }

        public static HitAndSetTransform SetTransform(GameObject on, string who, Transform target, Vector3 position, Vector3 rotation,
            Vector3 scale, bool world, bool onSelf, bool trigger = true)
        {
            var c = on.AddComponent<HitAndSetTransform>();
            c.checkType = trigger ? HitAndSetTransform.CheckType.Trigger : HitAndSetTransform.CheckType.Collision;
            c.objectName = who;
            c.onSelf = onSelf;
            c.target = onSelf ? null : target;
            c.position = position;
            c.rotation = rotation;
            c.scale = scale;
            c.space = world ? HitAndSetTransform.Space.World : HitAndSetTransform.Space.Local;
            return c;
        }

        public static HitAndLoadScene LoadScene(GameObject on, string who, string scene, bool trigger = true)
        {
            var c = on.AddComponent<HitAndLoadScene>();
            c.checkType = trigger ? HitAndLoadScene.CheckType.Trigger : HitAndLoadScene.CheckType.Collision;
            c.objectName = who;
            c.loadSceneName = scene;
            return c;
        }

        // ------------------------------------------------------------------ composite pieces
        public GameObject Plate(string name, Vector3 pos, string who = "Player", string exitAnim = "")
        {
            var p = Prefab("Plate", pos, 0f, puzzle, name);
            var press = p.GetComponent<HitAndPlayAnimation>();
            if (press != null) { press.objectName = who; press.exitAnimName = exitAnim; }
            return p;
        }

        public GameObject Lantern(string name, Vector3 pos, bool lit, string prefab = "Lantern", float yaw = 0f)
        {
            var l = Prefab(prefab, pos, yaw, puzzle, name);
            var litGo = Find(l, "Lit");
            if (litGo != null)
            {
                litGo.SetActive(lit);
                if (lit)
                {
                    var src = litGo.GetComponent<AudioSource>();
                    if (src != null) src.playOnAwake = false;
                }
            }
            return l;
        }

        public static GameObject Lit(GameObject lantern) => Find(lantern, "Lit");

        public (GameObject root, Animation anim) Gate(string name, Vector3 pos, float yaw = 0f)
        {
            var g = Prefab("Gate", pos, yaw, puzzle, name);
            var mesh = Find(g, "GateMesh");
            return (g, mesh != null ? mesh.GetComponent<Animation>() : null);
        }

        /// <summary>Planks that rise one after another out of a chasm (per-instance legacy clip "{name}_Rise").</summary>
        public Animation RisingBridge(string name, Vector3 start, Vector3 dir, int planks, float plankLength, float width,
            float drop = 6f, Material mat = null, float thickness = 0.35f)
        {
            dir = dir.normalized;
            var rootGo = new GameObject(name);
            rootGo.transform.SetParent(puzzle, false);
            rootGo.transform.position = start;
            rootGo.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            var parts = new List<(string, Vector3, Vector3)>();
            for (int i = 0; i < planks; i++)
            {
                string pn = "Plank" + i;
                Vector3 rest = new Vector3(0f, -thickness * 0.5f, plankLength * (i + 0.5f));
                Vector3 sunk = rest - new Vector3(0f, drop, 0f);
                var plank = Block(pn, Vector3.zero, new Vector3(width, thickness, plankLength - 0.06f), mat != null ? mat : EmberPaths.Mat("M_Wood"), rootGo.transform, 0.05f, true, false);
                plank.transform.localPosition = sunk;
                plank.transform.localRotation = Quaternion.identity;
                var rb = plank.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                parts.Add((pn, sunk, rest));
            }
            var clip = AnimFactory.Rise($"{stage}_{name}_Rise", parts, 0.28f, 0.75f, "sfx_bridge");
            var anim = rootGo.AddComponent<Animation>();
            AnimationUtility.SetAnimationClips(anim, new[] { clip });
            anim.clip = clip;
            anim.playAutomatically = false;
            anim.animatePhysics = true;
            var sfx = rootGo.AddComponent<AnimEventSfx>();
            sfx.clips = new[] { EmberPaths.Clip("sfx_bridge") };
            return anim;
        }

        /// <summary>A block that rises from below into place (stairs to the beacon).</summary>
        public Animation RisingBlock(string name, Vector3 restCenter, Vector3 size, float drop, Material mat, GameObject visualModel = null,
            float pitch = 0f, Vector3 visualOffset = default, Vector3 visualScale = default, float visualYaw = 0f)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(puzzle, false);
            holder.transform.position = restCenter;
            var body = new GameObject("Body");
            body.transform.SetParent(holder.transform, false);
            body.transform.localPosition = new Vector3(0f, -drop, 0f);
            var col = new GameObject("Ramp");
            col.transform.SetParent(body.transform, false);
            col.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            col.AddComponent<BoxCollider>().size = size;
            if (visualModel != null)
            {
                var v = (GameObject)PrefabUtility.InstantiatePrefab(visualModel);
                v.transform.SetParent(body.transform, false);
                v.transform.localPosition = visualOffset;
                v.transform.localRotation = Quaternion.Euler(0f, visualYaw, 0f);
                v.transform.localScale = visualScale == default ? Vector3.one : visualScale;
            }
            else
            {
                col.AddComponent<MeshFilter>().sharedMesh = MeshGen.ChamferBox(size);
                col.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            var rb = body.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var clip = AnimFactory.Rise($"{stage}_{name}_Rise", new List<(string, Vector3, Vector3)> { ("Body", new Vector3(0f, -drop, 0f), Vector3.zero) }, 0f, 1.6f, "sfx_bridge");
            var anim = holder.AddComponent<Animation>();
            AnimationUtility.SetAnimationClips(anim, new[] { clip });
            anim.clip = clip;
            anim.playAutomatically = false;
            anim.animatePhysics = true;
            holder.AddComponent<AnimEventSfx>().clips = new[] { EmberPaths.Clip("sfx_bridge") };
            return anim;
        }

        public GameObject Checkpoint(string name, Vector3 pedestal, float yaw, Vector3 triggerCenter, Vector3 triggerSize, Vector3 spawn, bool isStart = false)
        {
            var cp = Prefab("Checkpoint", pedestal, yaw, triggers, name);
            var trig = Find(cp, "Trigger");
            trig.transform.position = triggerCenter;
            trig.transform.rotation = Quaternion.identity;
            trig.GetComponent<BoxCollider>().size = triggerSize;
            trig.GetComponent<BoxCollider>().center = Vector3.zero;
            var sp = Find(cp, "SpawnPoint");
            sp.transform.position = spawn;
            trig.GetComponent<Checkpoint>().isStart = isStart;
            return cp;
        }

        public GameObject KillZone(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(triggers, false);
            go.transform.position = center;
            var b = go.AddComponent<BoxCollider>();
            b.isTrigger = true;
            b.size = size;
            go.AddComponent<KillZone>();
            return go;
        }

        public GameObject Trigger(string name, Vector3 center, Vector3 size, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : triggers, false);
            go.transform.position = center;
            var b = go.AddComponent<BoxCollider>();
            b.isTrigger = true;
            b.size = size;
            return go;
        }

        public GameObject CameraZone(string name, Vector3 center, Vector3 size, Vector3 offset, float fov = 50f, float blend = 1.4f)
        {
            var go = Trigger(name, center, size);
            var z = go.AddComponent<CameraZone>();
            z.offset = offset;
            z.fieldOfView = fov;
            z.blendTime = blend;
            return go;
        }

        public LevelBeat Beat(string code, string title, Vector3 pos, Vector3 shotOffset, Vector3 shotLook, string note = "")
        {
            var go = new GameObject("Beat " + code);
            go.transform.SetParent(beats, false);
            go.transform.position = pos;
            var b = go.AddComponent<LevelBeat>();
            b.code = code;
            b.title = title;
            b.note = note;
            b.shotOffset = shotOffset;
            b.shotLookOffset = shotLook;
            return b;
        }

        public GameObject Shard(string id, Vector3 pos)
        {
            var s = Prefab("Shard", pos, 0f, puzzle, "Shard " + id);
            s.GetComponent<Collectible>().id = id;
            return s;
        }

        public Light PointLight(Vector3 pos, Color color, float intensity, float range, bool flicker = false, bool shadows = false, Transform parent = null)
        {
            var l = PrefabFactory.PointLight(parent != null ? parent : lights, "PointLight", Vector3.zero, color, intensity, range,
                shadows ? LightShadows.Soft : LightShadows.None);
            l.transform.position = pos;
            if (flicker) l.gameObject.AddComponent<Flicker>();
            return l;
        }

        public Light SpotLight(Vector3 pos, Vector3 lookAt, Color color, float intensity, float range, float angle, bool shadows = false)
        {
            var go = new GameObject("SpotLight");
            go.transform.SetParent(lights, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(lookAt - pos);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.spotAngle = angle;
            l.innerSpotAngle = angle * 0.6f;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            return l;
        }

        public ParticleSystem Fx(string name, Vector3 pos, FxKind kind, float scale = 1f, Transform parent = null)
        {
            var ps = FxFactory.Create(parent != null ? parent : fx, name, Vector3.zero, kind, scale);
            ps.transform.position = pos;
            return ps;
        }

        /// <summary>Additive light beam (cylinder) between two points; ScrollingUVs (class script) animates it.</summary>
        public GameObject Beam(string name, Vector3 from, Vector3 to, float radius, string material, Transform parent = null, Vector2? scroll = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : fx, false);
            Vector3 d = to - from;
            go.transform.position = (from + to) * 0.5f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            go.AddComponent<MeshFilter>().sharedMesh = MeshGen.Tube(radius, d.magnitude, 24);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = EmberPaths.Mat(material);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var su = go.AddComponent<ScrollingUVs>();
            su.uvAnimationRate = scroll ?? new Vector2(0f, 0.25f);
            return go;
        }

        /// <summary>Glowing words carved into the floor (TextMeshPro 3D).</summary>
        public TextMeshPro FloorText(string text, Vector3 pos, float size, Color hdr, float yaw = 0f, float spacing = 12f)
        {
            var go = new GameObject("Engraving " + text.Split('\n')[0]);
            go.transform.SetParent(deco, false);
            go.transform.position = pos + Vector3.up * 0.02f;
            go.transform.rotation = Quaternion.Euler(90f, yaw, 0f);
            var t = go.AddComponent<TextMeshPro>();
            t.font = UIFactory.Font();
            t.fontSharedMaterial = UIFactory.EngraveMaterial(hdr);
            t.text = text;
            t.fontSize = size;
            t.characterSpacing = spacing;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(30f, 6f);
            t.color = Color.white;
            return t;
        }

        public TextMeshPro WorldText(string text, Vector3 pos, float size, Color color, float yaw = 0f, float spacing = 20f)
        {
            var go = new GameObject("Text " + text);
            go.transform.SetParent(deco, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var t = go.AddComponent<TextMeshPro>();
            t.font = UIFactory.Font();
            t.text = text;
            t.fontSize = size;
            t.characterSpacing = spacing;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(40f, 6f);
            t.color = color;
            return t;
        }

        // ------------------------------------------------------------------ solution steps (AutoPilot)
        public SolutionStep Step(string label, Transform moveTo, float wait, bool sceneChange = false)
        {
            var s = new SolutionStep { label = label, moveTo = moveTo, wait = wait, expectSceneChange = sceneChange };
            solution.steps.Add(s);
            return s;
        }

        public static void Expect(SolutionStep s, SolutionCheck.Kind kind, GameObject target, string text = null)
        {
            s.checks.Add(new SolutionCheck { kind = kind, target = target, text = text });
        }
    }
}
