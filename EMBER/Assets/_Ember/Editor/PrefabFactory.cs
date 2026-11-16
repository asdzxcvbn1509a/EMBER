using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ember.EditorTools
{
    /// <summary>
    /// Gameplay prefabs. Visuals are the Blender models (Assets/_Ember/Art/Models); colliders are
    /// simple primitives sized to the models. Where the wiring never changes (a plate presses down
    /// when the Player rolls on it) the class Condition/Action scripts are added here; per-level
    /// wiring (what a plate opens) is added by the stage builders through LevelKit.
    ///
    /// Note on orientation: Blender "front" (-Y) imports as Unity +Z, i.e. facing away from the
    /// approaching player; builders use yaw 180 for things that must face the camera.
    /// </summary>
    public static class PrefabFactory
    {
        public static readonly Color Amber = new Color(1f, 0.62f, 0.26f);
        public static readonly Color Cyan = new Color(0.35f, 0.85f, 1f);
        public static readonly Color Gold = new Color(1f, 0.8f, 0.45f);

        public static void Run()
        {
            EmberPaths.Ensure(EmberPaths.Prefabs);
            EmberPaths.Ensure(EmberPaths.Physics);
            var pmBall = PhysMat("PM_Ember", 0.9f, 0.9f, 0.05f);
            var pmProp = PhysMat("PM_Prop", 0.55f, 0.6f, 0.1f);
            Player(pmBall);
            Plate();
            LanternLike("Lantern", "Lantern", new Vector3(0f, 1.92f, 0f), Amber, 3.2f, 9f, 0.24f, 2.6f, FxKind.Flame, 1f);
            LanternLike("LanternDawn", "LanternDawn", new Vector3(0f, 3.0f, 0f), Gold, 4.5f, 14f, 0.6f, 3.6f, FxKind.Flame, 1.6f);
            LanternLike("Brazier", "Brazier", new Vector3(0f, 1.18f, 0f), Amber, 4f, 11f, 0.62f, 1.2f, FxKind.BigFire, 1f);
            Gate();
            Seal();
            ExitPortal();
            MakeCheckpoint();
            TeleportRing();
            Shard();
            WardenStatue();
            LavaRaft();
            Ball("IronBall", "IronBall", 0.7f, 6f, pmProp, "M_Iron", null);
            Ball("Counterweight", "Counterweight", 0.7f, 5f, pmProp, null, null);
            Ball("SunStone", "SunStone", 0.6f, 3f, pmProp, null, Gold);
            Boulder(pmProp);
            CrackedWall();
            WallRubble(pmProp);
            ForgeGate();
            Hammer();
            RotatingBridge();
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ helpers
        static PhysicsMaterial PhysMat(string name, float dyn, float stat, float bounce)
        {
            string path = $"{EmberPaths.Physics}/{name}.asset";
            var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (pm == null)
            {
                pm = new PhysicsMaterial(name);
                AssetDatabase.CreateAsset(pm, path);
            }
            pm.dynamicFriction = dyn;
            pm.staticFriction = stat;
            pm.bounciness = bounce;
            pm.frictionCombine = PhysicsMaterialCombine.Average;
            pm.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(pm);
            return pm;
        }

        public static GameObject AddModel(GameObject parent, string model, Vector3 localPos, float yaw = 0f, string childName = null)
        {
            var asset = EmberPaths.Model(model);
            GameObject go;
            if (asset != null) go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            else
            {
                Debug.LogWarning("[Ember] model missing: " + model);
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(go.GetComponent<Collider>());
            }
            go.name = childName ?? model;
            if (parent != null) go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }

        public static Mesh MeshOf(GameObject go)
        {
            var mf = go.GetComponentInChildren<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        public static Light PointLight(Transform parent, string name, Vector3 pos, Color color, float intensity, float range, LightShadows shadows = LightShadows.None)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = shadows;
            l.shadowStrength = 0.85f;
            return l;
        }

        static GameObject Child(GameObject parent, string name, Vector3 pos = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            return go;
        }

        static BoxCollider Box(GameObject go, Vector3 center, Vector3 size, bool trigger = false)
        {
            var b = go.AddComponent<BoxCollider>();
            b.center = center;
            b.size = size;
            b.isTrigger = trigger;
            return b;
        }

        public static void SetClips(Animation anim, params string[] clipNames)
        {
            var clips = new AnimationClip[clipNames.Length];
            for (int i = 0; i < clipNames.Length; i++) clips[i] = AnimFactory.Load(clipNames[i]);
            AnimationUtility.SetAnimationClips(anim, clips);
            anim.clip = clips.Length > 0 ? clips[0] : null;
        }

        static AnimEventSfx Sfx(GameObject go, params string[] clips)
        {
            var s = go.AddComponent<AnimEventSfx>();
            s.clips = new AudioClip[clips.Length];
            for (int i = 0; i < clips.Length; i++) s.clips[i] = EmberPaths.Clip(clips[i]);
            return s;
        }

        static GameObject Save(GameObject root, string name)
        {
            string path = $"{EmberPaths.Prefabs}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------------ prefabs
        static void Player(PhysicsMaterial pm)
        {
            var root = new GameObject("Player");
            root.tag = "Player";
            var sc = root.AddComponent<SphereCollider>();
            sc.radius = 0.5f;
            sc.sharedMaterial = pm;
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.linearDamping = 0.15f;
            rb.angularDamping = 0.9f;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            var ball = root.AddComponent<BallController>();
            ball.power = 10f;
            ball.maxSpeed = 15f;
            root.AddComponent<CanJumpBuffered>();
            var fx = root.AddComponent<PlayerFX>();

            var vis = AddModel(root, "EmberCore", Vector3.zero, 0f, "Visual");
            foreach (var r in vis.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            var light = PointLight(root.transform, "EmberLight", Vector3.zero, new Color(1f, 0.56f, 0.24f), 2.4f, 8f, LightShadows.Soft);
            light.shadowNearPlane = 0.55f;
            fx.emberLight = light;
            fx.lightBase = 2.4f;
            FxFactory.Create(root.transform, "Aura", Vector3.zero, FxKind.Glow, 1.5f);
            fx.trail = FxFactory.Create(root.transform, "Trail", Vector3.zero, FxKind.Trail);

            var roll = root.AddComponent<AudioSource>();
            roll.clip = EmberPaths.Clip("loop_roll");
            roll.loop = true;
            roll.playOnAwake = true;
            roll.volume = 0f;
            roll.spatialBlend = 0.5f;
            var sfx = root.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0.3f;
            fx.rollSource = roll;
            fx.sfxSource = sfx;
            fx.jumpClip = EmberPaths.Clip("sfx_jump");
            fx.landClip = EmberPaths.Clip("sfx_land");
            fx.impactClip = EmberPaths.Clip("sfx_impact");
            Save(root, "Player");
        }

        static void Plate()
        {
            var root = new GameObject("Plate");
            var model = AddModel(root, "PressurePlate", Vector3.zero, 0f, "Model");
            var anim = model.AddComponent<Animation>();
            anim.playAutomatically = false;
            SetClips(anim, "PlateDown", "PlateUp");
            Sfx(model, "sfx_plate");
            Box(root, new Vector3(0f, 0.06f, 0f), new Vector3(2.62f, 0.12f, 2.62f));
            Box(root, new Vector3(0f, 0.6f, 0f), new Vector3(2.1f, 1.0f, 2.1f), true);
            var press = root.AddComponent<HitAndPlayAnimation>();
            press.checkType = HitAndPlayAnimation.CheckType.Trigger;
            press.objectName = "Player";
            press.target = anim;
            press.enterAnimName = "PlateDown";
            press.exitAnimName = "";
            PointLight(root.transform, "Glow", new Vector3(0f, 0.45f, 0f), Amber, 0.9f, 3.2f);
            Save(root, "Plate");
        }

        static void LanternLike(string prefabName, string model, Vector3 flamePos, Color color, float intensity, float range,
            float colRadius, float colHeight, FxKind fire, float fireScale)
        {
            var root = new GameObject(prefabName);
            var modelGo = AddModel(root, model, Vector3.zero, 0f, "Model");
            var cap = root.AddComponent<CapsuleCollider>();
            cap.radius = colRadius;
            cap.height = Mathf.Max(colHeight, colRadius * 2f);
            cap.center = new Vector3(0f, colHeight * 0.5f, 0f);
            var lit = Child(root, "Lit");
            if (model == "Brazier") CoalBed(modelGo, lit, 1.135f, 0.54f);
            var l = PointLight(lit.transform, "Light", flamePos, color, intensity, range);
            l.gameObject.AddComponent<Flicker>();
            l.gameObject.AddComponent<FadeInLight>();
            FxFactory.Create(lit.transform, "Fire", flamePos - new Vector3(0f, 0.1f, 0f), fire, fireScale);
            FxFactory.Create(lit.transform, "Glow", flamePos, FxKind.Glow, 1.4f * fireScale);
            FxFactory.Create(lit.transform, "Sparks", flamePos, FxKind.Sparks, fireScale);
            var src = lit.AddComponent<AudioSource>();
            src.clip = EmberPaths.Clip("sfx_ignite");
            src.playOnAwake = true;
            src.spatialBlend = 1f;
            src.minDistance = 4f;
            src.maxDistance = 45f;
            src.rolloffMode = AudioRolloffMode.Linear;
            Save(root, prefabName);
        }

        static void Gate()
        {
            var root = new GameObject("Gate");
            var mesh = AddModel(root, "Gate", Vector3.zero, 0f, "GateMesh");
            Box(mesh, new Vector3(0f, 2.15f, 0f), new Vector3(4.5f, 4.3f, 0.32f));
            var rb = mesh.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var anim = mesh.AddComponent<Animation>();
            anim.playAutomatically = false;
            anim.animatePhysics = true;
            SetClips(anim, "GateOpen", "GateClose");
            Sfx(mesh, "sfx_gate");
            Save(root, "Gate");
        }

        static void Seal()
        {
            var root = new GameObject("Seal");
            AddModel(root, "SealSlab", Vector3.zero, 0f, "Model");
            Box(root, new Vector3(0f, 1.5f, 0f), new Vector3(2.4f, 3f, 0.42f));
            PointLight(root.transform, "Glow", new Vector3(0f, 1.5f, 0.6f), new Color(1f, 0.2f, 0.1f), 1.2f, 4f);
            Save(root, "Seal");
        }

        static void ExitPortal()
        {
            var root = new GameObject("ExitPortal");
            var model = AddModel(root, "ExitPortal", Vector3.zero, 0f, "Model");
            var mc = model.AddComponent<MeshCollider>();
            mc.sharedMesh = MeshOf(model);
            var outer = Child(root, "OuterTrigger", new Vector3(0f, 1.5f, -2.6f));
            Box(outer, Vector3.zero, new Vector3(5.4f, 3f, 6.4f), true);
            var inner = Child(root, "InnerTrigger", new Vector3(0f, 1.1f, 0.1f));
            Box(inner, Vector3.zero, new Vector3(2.8f, 2.2f, 1.2f), true);
            var fade = Child(root, "FadeOut");
            fade.AddComponent<FadeScreenOnEnable>();
            var src = fade.AddComponent<AudioSource>();
            src.clip = EmberPaths.Clip("sfx_portal");
            src.playOnAwake = true;
            src.spatialBlend = 0f;
            fade.SetActive(false);
            var show = outer.AddComponent<HitAndShowHide>();
            show.checkType = HitAndShowHide.CheckType.Trigger;
            show.objectName = "Player";
            show.target = fade;
            show.action = HitAndShowHide.ShowHide.Show;
            show.cancelOnExit = true;
            var load = inner.AddComponent<HitAndLoadScene>();
            load.checkType = HitAndLoadScene.CheckType.Trigger;
            load.objectName = "Player";
            load.loadSceneName = "Stage2";
            PointLight(root.transform, "Light", new Vector3(0f, 1.9f, 0f), Amber, 3f, 9f);
            FxFactory.Create(root.transform, "Motes", new Vector3(0f, 0.25f, 0f), FxKind.Motes);
            Save(root, "ExitPortal");
        }

        static void MakeCheckpoint()
        {
            var root = new GameObject("Checkpoint");
            var modelGo = AddModel(root, "Checkpoint", Vector3.zero, 0f, "Model");
            Box(root, new Vector3(0f, 0.43f, 0f), new Vector3(0.58f, 0.86f, 0.58f));
            var trig = Child(root, "Trigger", new Vector3(0f, 1.2f, 0f));
            Box(trig, Vector3.zero, new Vector3(4f, 2.5f, 1.6f), true);
            var cp = trig.AddComponent<Checkpoint>();
            var lit = Child(root, "LitFx");
            CoalBed(modelGo, lit, 1.0f, 0.26f);
            FxFactory.Create(lit.transform, "Fire", new Vector3(0f, 0.98f, 0f), FxKind.Flame, 0.9f);
            var l = PointLight(lit.transform, "Light", new Vector3(0f, 1.25f, 0f), Amber, 1.6f, 5f);
            l.gameObject.AddComponent<Flicker>();
            l.gameObject.AddComponent<FadeInLight>();
            lit.SetActive(false);
            var sp = Child(root, "SpawnPoint", new Vector3(0f, 0f, 0f));
            cp.litFx = lit;
            cp.spawnPoint = sp.transform;
            cp.sound = EmberPaths.Clip("sfx_checkpoint");
            Save(root, "Checkpoint");
        }

        /// <summary>
        /// "Dark before solving": the model's glowing coal surface (M_Glow) becomes dark coal, and a glowing
        /// disc of the same size goes into the Lit child, so it only shines once the object is lit.
        /// </summary>
        public static GameObject CoalBed(GameObject model, GameObject lit, float y, float radius)
        {
            LevelKit.Swap(model, "M_Glow", "M_Coal");
            var go = new GameObject("CoalGlow");
            go.transform.SetParent(lit.transform, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = MeshGen.Cylinder(radius, 0.02f, 12, 0.5f, 0f);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = EmberPaths.Mat("M_Glow");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static void TeleportRing()
        {
            var root = new GameObject("TeleportRing");
            var model = AddModel(root, "TeleportRing", Vector3.zero, 0f, "Model");
            var mc = model.AddComponent<MeshCollider>();
            mc.sharedMesh = MeshOf(model);
            var trig = Child(root, "Trigger", new Vector3(0f, 1.2f, 0f));
            Box(trig, Vector3.zero, new Vector3(1.7f, 1.7f, 0.9f), true);
            var st = trig.AddComponent<HitAndSetTransform>();
            st.checkType = HitAndSetTransform.CheckType.Trigger;
            st.objectName = "Player";
            st.onSelf = true;
            st.space = HitAndSetTransform.Space.World;
            st.scale = Vector3.one;
            var ts = trig.AddComponent<TriggerSound>();
            ts.clip = EmberPaths.Clip("sfx_teleport");
            PointLight(root.transform, "Light", new Vector3(0f, 1.2f, 0f), Gold, 2f, 6f);
            FxFactory.Create(root.transform, "Motes", new Vector3(0f, 0.2f, 0f), FxKind.Motes, 0.6f);
            Save(root, "TeleportRing");
        }

        static void Shard()
        {
            var root = new GameObject("Shard");
            var sc = root.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 0.9f;
            var col = root.AddComponent<Collectible>();
            var bob = root.AddComponent<Bob>();
            bob.amplitude = new Vector3(0f, 0.18f, 0f);
            bob.period = 3f;
            var spin = Child(root, "Spin");
            var anim = spin.AddComponent<Animation>();
            var spinClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animation/Spin.anim");
            if (spinClip != null)
            {
                AnimationUtility.SetAnimationClips(anim, new[] { spinClip });
                anim.clip = spinClip;
            }
            anim.playAutomatically = true;
            anim.wrapMode = WrapMode.Loop;
            AddModel(spin, "Shard", Vector3.zero, 0f, "Crystal");
            PointLight(root.transform, "Light", Vector3.zero, Cyan, 1.4f, 4f);
            FxFactory.Create(root.transform, "Sparkle", Vector3.zero, FxKind.CrystalSparkle);
            var burst = FxFactory.Create(root.transform, "PickupFx", Vector3.zero, FxKind.Burst, 0.8f);
            burst.GetComponent<ParticleSystemRenderer>().sharedMaterial = EmberPaths.Mat("M_Particle_Crystal");
            burst.gameObject.SetActive(false);
            col.pickupFx = burst.gameObject;
            col.sound = EmberPaths.Clip("sfx_shard");
            Save(root, "Shard");
        }

        static void WardenStatue()
        {
            var root = new GameObject("WardenStatue");
            var model = AddModel(root, "WardenStatue", Vector3.zero, 0f, "Model");
            Box(root, new Vector3(0f, 1.4f, 0f), new Vector3(1.3f, 2.8f, 1.3f));
            var head = model.transform.Find("Head");
            if (head != null)
            {
                head.gameObject.AddComponent<LookAtTarget>();
                var amber = head.Find("EyesAmber");
                if (amber != null) amber.gameObject.SetActive(false);
                PointLight(head, "EyeLight", new Vector3(0f, 0.28f, 0.45f), new Color(1f, 0.15f, 0.08f), 0.8f, 3f);
            }
            Save(root, "WardenStatue");
        }

        static void LavaRaft()
        {
            var root = new GameObject("LavaRaft");
            AddModel(root, "LavaRaft", Vector3.zero, 0f, "Model");
            Box(root, new Vector3(0f, -0.225f, 0f), new Vector3(3.0f, 0.45f, 3.0f));
            Box(root, new Vector3(0f, 0.1f, 1.42f), new Vector3(3.0f, 0.24f, 0.2f));
            Box(root, new Vector3(0f, 0.1f, -1.42f), new Vector3(3.0f, 0.24f, 0.2f));
            Box(root, new Vector3(1.42f, 0.1f, 0f), new Vector3(0.2f, 0.24f, 3.0f));
            Box(root, new Vector3(-1.42f, 0.1f, 0f), new Vector3(0.2f, 0.24f, 3.0f));
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var mover = root.AddComponent<KinematicMover>();
            mover.mode = KinematicMover.Mode.Drift;
            mover.driftVelocity = new Vector3(1.5f, 0f, 0f);   // Stage 2 lava river flows +X
            PointLight(root.transform, "Glow", new Vector3(0f, -0.9f, 0f), new Color(1f, 0.4f, 0.1f), 2.2f, 5f);
            Save(root, "LavaRaft");
        }

        static void Ball(string prefab, string model, float radius, float mass, PhysicsMaterial pm, string matOverride, Color? glow)
        {
            var root = new GameObject(prefab);
            var vis = AddModel(root, model, Vector3.zero, 0f, "Model");
            if (matOverride != null)
                foreach (var r in vis.GetComponentsInChildren<Renderer>()) r.sharedMaterial = EmberPaths.Mat(matOverride);
            var sc = root.AddComponent<SphereCollider>();
            sc.radius = radius;
            sc.sharedMaterial = pm;
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.angularDamping = 0.2f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            if (glow.HasValue) PointLight(root.transform, "Glow", Vector3.zero, glow.Value, 2f, 6f);
            Save(root, prefab);
        }

        static void Boulder(PhysicsMaterial pm)
        {
            var root = new GameObject("Boulder");
            var vis = AddModel(root, "EmberCore", Vector3.zero, 0f, "Model");
            vis.transform.localScale = Vector3.one * 1.7f;
            foreach (var r in vis.GetComponentsInChildren<Renderer>())
            {
                var core = AssetDatabase.LoadAssetAtPath<Material>(EmberPaths.ClassS3 + "/Materials/LavaBallCoreMat.mat");
                r.sharedMaterial = core != null ? core : EmberPaths.Mat("M_Basalt");
            }
            var sc = root.AddComponent<SphereCollider>();
            sc.radius = 0.85f;
            sc.sharedMaterial = pm;
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 3f;
            rb.angularDamping = 0.1f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            FxFactory.Create(root.transform, "Fire", Vector3.zero, FxKind.Flame, 2.2f);
            PointLight(root.transform, "Glow", Vector3.zero, new Color(1f, 0.45f, 0.12f), 2.2f, 6f);
            Save(root, "Boulder");
        }

        static void CrackedWall()
        {
            var root = new GameObject("CrackedWall");
            AddModel(root, "CrackedWall", Vector3.zero, 0f, "Model");
            Box(root, new Vector3(0f, 2f, 0f), new Vector3(6f, 4f, 1f));
            PointLight(root.transform, "CrackGlow", new Vector3(0f, 1.8f, 1.2f), new Color(1f, 0.4f, 0.1f), 1.6f, 5f);
            Save(root, "CrackedWall");
        }

        static void WallRubble(PhysicsMaterial pm)
        {
            var root = new GameObject("WallRubble");
            var model = AddModel(root, "WallChunks", Vector3.zero, 0f, "Chunks");
            foreach (Transform chunk in model.transform)
            {
                var mc = chunk.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = MeshOf(chunk.gameObject);
                mc.convex = true;
                mc.sharedMaterial = pm;
                var rb = chunk.gameObject.AddComponent<Rigidbody>();
                rb.mass = 0.35f;
            }
            var ex = root.AddComponent<ExplodeOnEnable>();
            ex.force = 5f;
            ex.bias = new Vector3(0f, 0f, 1f);
            ex.lifetime = 0f;
            var shake = root.AddComponent<ShakeOnEnable>();
            shake.amplitude = 0.45f;
            shake.duration = 0.8f;
            shake.sound = EmberPaths.Clip("sfx_smash");
            FxFactory.Create(root.transform, "Dust", new Vector3(0f, 1.5f, 0f), FxKind.Burst, 1.6f);
            var smoke = FxFactory.Create(root.transform, "Smoke", new Vector3(0f, 0.5f, 0f), FxKind.Smoke, 1.2f);
            var main = smoke.main;
            main.loop = false;
            main.duration = 3f;
            Save(root, "WallRubble");
        }

        static void ForgeGate()
        {
            var root = new GameObject("ForgeGate");
            var model = AddModel(root, "ForgeGate", Vector3.zero, 0f, "Model");
            foreach (var door in new[] { "DoorL", "DoorR" })
            {
                var d = model.transform.Find(door);
                if (d == null) continue;
                Box(d.gameObject, new Vector3(0f, 2.3f, 0f), new Vector3(2.3f, 4.6f, 0.42f));
                var rb = d.gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }
            // frame pillars and lintel (Blender x is mirrored to -x in Unity; the frame is symmetric)
            Box(root, new Vector3(-2.8f, 2.6f, 0f), new Vector3(0.9f, 5.2f, 1.2f));
            Box(root, new Vector3(2.8f, 2.6f, 0f), new Vector3(0.9f, 5.2f, 1.2f));
            Box(root, new Vector3(0f, 5.4f, 0f), new Vector3(6.5f, 0.9f, 1.2f));
            var anim = model.AddComponent<Animation>();
            anim.playAutomatically = false;
            anim.animatePhysics = true;
            SetClips(anim, "ForgeGateOpen", "ForgeGateClose");
            Sfx(model, "sfx_gate");
            Save(root, "ForgeGate");
        }

        static void Hammer()
        {
            var root = new GameObject("Hammer");
            var model = AddModel(root, "Hammer", Vector3.zero, 0f, "Model");
            var anim = model.AddComponent<Animation>();
            SetClips(anim, "HammerLoop");
            anim.playAutomatically = true;
            anim.wrapMode = WrapMode.Loop;
            Sfx(model, "sfx_hammer").volume = 0.6f;
            Box(root, new Vector3(-1.5f, 2.1f, 0f), new Vector3(0.5f, 4.2f, 0.5f));
            Box(root, new Vector3(1.5f, 2.1f, 0f), new Vector3(0.5f, 4.2f, 0.5f));
            Save(root, "Hammer");
        }

        static void RotatingBridge()
        {
            var root = new GameObject("RotatingBridge");
            AddModel(root, "RotatingBridge", Vector3.zero, 0f, "Model");
            Box(root, new Vector3(0f, -0.25f, 0f), new Vector3(2.6f, 0.5f, 12f));
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var rot = root.AddComponent<KinematicRotator>();
            rot.degreesPerSecond = 30f;
            rot.stepped = true;
            rot.stepAngle = 90f;
            rot.pauseTime = 2.6f;
            Save(root, "RotatingBridge");
        }
    }
}
