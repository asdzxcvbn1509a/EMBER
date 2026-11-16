using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ember.EditorTools
{
    /// <summary>
    /// Stage 3 - THE SKY SANCTUM. Combines every mechanic: teleport rings (HitAndSetTransform onSelf),
    /// a stone bridge turned into place (HitAndSetTransform on a target), a path that appears tile by tile
    /// (HitAndShowHide chain), a sun-stone that breaks a crystal seal (HitAndClone + HitAndDestroy),
    /// stairs raised by three lanterns (HitAndPlayAnimation) and the dawn finale.
    /// </summary>
    public static class BuildStage3
    {
        public const string Name = "Stage3";
        const string P = "Player";
        static readonly Color Gold = new Color(1f, 0.8f, 0.45f);
        static readonly Color Cyan = new Color(0.35f, 0.85f, 1f);
        static readonly Color Dawn = new Color(1f, 0.74f, 0.6f);

        static Material M(string n) => EmberPaths.Mat(n);

        /// <summary>Floating island: Blender model for looks, a smooth invisible disc for rolling.</summary>
        static GameObject Island(LevelKit kit, string name, string model, Vector3 top, float radius)
        {
            var go = kit.Model(model, top, 0f, kit.geo, 1f, false, name);
            var col = new GameObject("WalkCollider");
            col.transform.SetParent(go.transform, false);
            col.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            var mc = col.AddComponent<MeshCollider>();
            mc.sharedMesh = MeshGen.Cylinder(radius * 0.97f, 1f, 24, 0.5f, 0f);
            mc.convex = true;
            return go;
        }

        static GameObject Ring(LevelKit kit, string name, Vector3 pos, Vector3 destination, bool cyan)
        {
            var ring = kit.Prefab("TeleportRing", pos, 0f, kit.puzzle, name);
            var st = ring.GetComponentInChildren<HitAndSetTransform>();
            st.position = destination;
            st.rotation = Vector3.zero;
            st.scale = Vector3.one;
            if (cyan)
            {
                LevelKit.Swap(ring, "M_RuneAmber", "M_RuneCyan");
                foreach (var l in ring.GetComponentsInChildren<Light>()) l.color = Cyan;
            }
            // landing pad marker at the destination
            var pad = kit.Plane(name + "_Pad", destination - new Vector3(0f, 0.66f, 0f), new Vector2(2.6f, 2.6f),
                M(cyan ? "M_RuneDecal_Cyan" : "M_RuneDecal_Amber"), kit.fx);
            pad.name = name + "_Pad";
            return ring;
        }

        static GameObject Slab(LevelKit kit, string name, Vector3 center, Vector3 travel)
        {
            var slab = kit.Block(name, center, new Vector3(3f, 0.6f, 3f), M("M_LimestoneTrim"), kit.puzzle, 0.06f, true, false);
            slab.AddComponent<Rigidbody>().isKinematic = true;
            var m = slab.AddComponent<KinematicMover>();
            m.travel = travel;
            m.speed = 1.3f;
            m.pause = 1.6f;
            return slab;
        }

        public static void Run()
        {
            var scene = StageCommon.NewScene();
            var theme = new Theme
            {
                sky = "M_Sky_Dawn", fogColor = new Color(0.42f, 0.34f, 0.5f), fogDensity = 0.0045f,
                ambient = new Color(0.2f, 0.19f, 0.28f), sunColor = Dawn, sunIntensity = 1.1f,
                sunEuler = new Vector3(2f, 200f, 0f), profile = "PP_SkyNight", ambience = "amb_sky", reverb = AudioReverbPreset.Mountains
            };
            var refs = StageCommon.Setup(3, "III  -  THE SKY SANCTUM", "LIGHT THE THREE LANTERNS OF DAWN", theme,
                new Vector3(0f, 0f, -1f), new Vector3(0f, 7.5f, -11f), 50f);
            var kit = new LevelKit(Name, M("M_Floor_Sky"), M("M_Limestone"), M("M_LimestoneTrim"));
            kit.player = refs.player.transform;
            kit.solution.nextScene = "Ending";
            var trim = M("M_LimestoneTrim");

            // sky dressing
            var sea = kit.Plane("CloudSea", new Vector3(0f, -14f, 60f), new Vector2(420f, 420f), M("M_CloudSea"));
            sea.AddComponent<ScrollingUVs>().uvAnimationRate = new Vector2(0.004f, 0.002f);
            var rnd = new System.Random(7);
            for (int i = 0; i < 18; i++)
            {
                var p = new Vector3((float)rnd.NextDouble() * 120f - 60f, -9f + (float)rnd.NextDouble() * 9f, (float)rnd.NextDouble() * 170f - 20f);
                if (Mathf.Abs(p.x - 6f) < 20f && p.y > -6f) p.x += p.x > 6f ? 24f : -24f;
                var c = kit.Model("CloudPuff", p, (float)rnd.NextDouble() * 360f, kit.deco, 1.5f + (float)rnd.NextDouble() * 2.5f);
                var b = c.AddComponent<Bob>();
                b.amplitude = new Vector3(0.6f, 0.4f, 0f);
                b.period = 9f + (float)rnd.NextDouble() * 6f;
            }
            kit.KillZone("Kill_Sky", new Vector3(0f, -12f, 60f), new Vector3(180f, 2f, 240f));

            // =========================================================== 3A  CLOUD STEPS
            Island(kit, "Island_Start", "Island_L", new Vector3(0f, 0f, 0f), 7f);
            kit.Checkpoint("CP_Start", new Vector3(-4.6f, 0f, -3.6f), 30f, new Vector3(0f, 1.2f, -1f), new Vector3(4f, 2.5f, 4f), new Vector3(0f, 0f, -1f), true);
            kit.Lantern("Lantern_Start", new Vector3(-3.6f, 0f, -1.6f), true, "LanternDawn");
            kit.Model("Tree", new Vector3(4.2f, 0f, -3.2f), 40f, kit.deco, 1.2f);
            kit.Model("Banner", new Vector3(3.8f, 0f, 2.5f), 180f, kit.deco);
            // pad and next ring are offset so the ember does not roll straight from one ring into the next
            var padB = new GameObject("Pad_B").transform; padB.SetParent(kit.puzzle, false); padB.position = new Vector3(-10.5f, 2.7f, 22f);
            var padC = new GameObject("Pad_C").transform; padC.SetParent(kit.puzzle, false); padC.position = new Vector3(7f, 4.7f, 37.4f);
            var ringA = Ring(kit, "Ring_Gold", new Vector3(0f, 0f, 5.2f), padB.position, false);
            Island(kit, "Island_B", "Island_M", new Vector3(-9f, 2f, 25f), 5f);
            var ringB = Ring(kit, "Ring_Cyan", new Vector3(-7.2f, 2f, 27.8f), padC.position, true);
            Island(kit, "Island_C", "Island_M", new Vector3(7f, 4f, 41f), 5f);
            Island(kit, "Island_Shard", "Island_S", new Vector3(10.5f, 0f, 5.2f), 3.5f);
            kit.Shard("S3_A", new Vector3(10.5f, 0.9f, 5.2f));
            kit.FloorText("ROLL THROUGH THE RINGS", new Vector3(0f, 0f, 1.8f), 2.4f, new Color(2.2f, 1.6f, 0.8f));
            kit.Checkpoint("CP_3A_B", new Vector3(-12f, 2f, 22f), 0f, new Vector3(-9f, 3.2f, 22.6f), new Vector3(6f, 2.5f, 1.4f), new Vector3(-9f, 2f, 23.2f));
            kit.Checkpoint("CP_3A_C", new Vector3(10f, 4f, 38.4f), 0f, new Vector3(7f, 5.2f, 38.8f), new Vector3(6f, 2.5f, 1.4f), new Vector3(7f, 4f, 39.4f));
            kit.CameraZone("Cam_Start", new Vector3(0f, 1.5f, -1f), new Vector3(6f, 3f, 6f), new Vector3(0f, 9f, -12f), 50f);
            kit.Beat("3A", "Cloud Steps", new Vector3(0f, 0f, 6f), new Vector3(-12f, 9f, -12f), new Vector3(0f, 1f, 14f), "Rings of the same colour are the same door (HitAndSetTransform onSelf)");

            // =========================================================== 3B  ORRERY TERRACE
            kit.Floor("Path_3B", 5f, 9f, 45.5f, 51f, 4f, trim, 0.6f);
            var bridge = kit.Prefab("RotatingBridge", new Vector3(7f, 4f, 57.2f), 0f, kit.puzzle, "RotatingBridge");
            var rot = bridge.GetComponent<KinematicRotator>();
            rot.degreesPerSecond = 24f;
            rot.stepAngle = 180f;
            rot.pauseTime = 5f;
            rot.initialPause = 4f;
            kit.Floor("Platform_3B", 5f, 9f, 63.4f, 67f, 4f, trim, 0.6f);
            Slab(kit, "Slab_3B", new Vector3(7f, 3.7f, 68.6f), new Vector3(0f, 0f, 4.8f));
            kit.Floor("Platform_3B2", 5f, 9f, 75f, 80f, 4f, trim, 0.6f);
            kit.Floor("Bridge_ToHub", 6f, 10f, 80f, 82.6f, 4f, trim, 0.6f);
            kit.Shard("S3_B", new Vector3(7f, 5.1f, 57.2f));
            var spin = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animation/Spin.anim");
            foreach (var pos in new[] { new Vector3(4.2f, 4f, 50.2f), new Vector3(9.8f, 4f, 64.4f) })
            {
                var orn = kit.Model("OrreryPlanet", pos + Vector3.up * 2.2f, 0f, kit.deco, 0.45f);
                var anim = orn.AddComponent<Animation>();
                if (spin != null) { AnimationUtility.SetAnimationClips(anim, new[] { spin }); anim.clip = spin; }
                anim.playAutomatically = true;
                anim.wrapMode = WrapMode.Loop;
                kit.Block("Ornament_Pole", pos + Vector3.up * 1f, new Vector3(0.18f, 2f, 0.18f), M("M_Gold"));
            }
            foreach (var (pos, sp) in new[] { (new Vector3(-6f, 7f, 58f), 9f), (new Vector3(20f, 9f, 62f), -7f), (new Vector3(7f, -2f, 57.2f), 12f) })
            {
                var planet = kit.Model("OrreryPlanet", pos, 0f, kit.deco, 2.2f);
                planet.AddComponent<Bob>().spinDegreesPerSecond = new Vector3(0f, sp, 0f);
            }
            kit.Checkpoint("CP_3B", new Vector3(9.6f, 4f, 47f), 0f, new Vector3(7f, 5.2f, 47.4f), new Vector3(4f, 2.5f, 1.4f), new Vector3(7f, 4f, 48.4f));
            kit.Checkpoint("CP_3B2", new Vector3(9.6f, 4f, 65f), 0f, new Vector3(7f, 5.2f, 64.6f), new Vector3(4f, 2.5f, 1.4f), new Vector3(7f, 4f, 65.4f));
            kit.Beat("3B", "Orrery Terrace", new Vector3(7f, 4f, 57f), new Vector3(10f, 9f, -10f), new Vector3(0f, 0f, 2f), "Wait for the turning bridge to align, then ride the slab");

            // =========================================================== 3C  THREE LANTERNS (hub)
            var hubCenter = new Vector3(8f, 4f, 92f);
            kit.Disc("Hub", hubCenter, 10f, 1.2f);
            kit.Plane("Hub_Rune", hubCenter + Vector3.up * 0.03f, new Vector2(12f, 12f), M("M_RuneDecal_Amber"));
            var hubPad = new GameObject("Hub_Pad").transform; hubPad.SetParent(kit.puzzle, false); hubPad.position = new Vector3(8f, 4.7f, 87.6f);
            kit.Checkpoint("CP_3C", new Vector3(13.5f, 4f, 84.4f), 0f, new Vector3(8f, 5.2f, 84f), new Vector3(8f, 2.5f, 1.6f), new Vector3(8f, 4f, 85.6f));
            kit.CameraZone("Cam_Hub", new Vector3(8f, 6f, 84f), new Vector3(8f, 4f, 1.6f), new Vector3(0f, 12f, -15f), 52f);
            var beaconTop = new Vector3(8f, 18.4f, 124f);

            // ---- stairs (three segments raised by the three lanterns) ----
            var stairs = new Animation[3];
            var stairModel = EmberPaths.Model("Stairs");
            float pitch = -Mathf.Atan2(1f, 4f) * Mathf.Rad2Deg;
            for (int k = 0; k < 3; k++)
            {
                Vector3 start = new Vector3(8f, 4f + k, 102f + 4f * k);
                Vector3 end = start + new Vector3(0f, 1f, 4f);
                Vector3 n = Quaternion.Euler(pitch, 0f, 0f) * Vector3.up;
                Vector3 c = (start + end) * 0.5f - n * 0.3f;
                stairs[k] = kit.RisingBlock("Stair" + (k + 1), c, new Vector3(4f, 0.6f, Vector3.Distance(start, end)), 6f, trim, stairModel,
                    pitch, start - c, new Vector3(1f, 1f, 1.6f));
            }
            kit.Floor("Beacon_Terrace", 1f, 15f, 114f, 128f, 7f, M("M_Floor_Sky"), 1f);
            kit.Wall("Terrace_W", 0.5f, 1f, 114f, 128f, 7f, 7.9f, trim);
            kit.Wall("Terrace_E", 15f, 15.5f, 114f, 128f, 7f, 7.9f, trim);
            var beacon = kit.Model("Beacon", new Vector3(8f, 7f, 124f), 0f, kit.geo, 1f, true);
            kit.Model("Altar", new Vector3(8f, 7f, 118.6f), 0f, kit.geo, 0.8f, true);

            // ---- WEST: Lantern of Echoes (tile path + stone bridge turned by HitAndSetTransform) ----
            Island(kit, "Island_West", "Island_M", new Vector3(-12f, 4f, 92f), 5f);
            Island(kit, "Islet_West", "Island_S", new Vector3(-12f, 4f, 103.6f), 3.5f);
            var tiles = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                float x1 = -2f - 2f * i, x0 = x1 - 2.1f;
                tiles[i] = kit.Floor("EchoTile_" + (i + 1), x0, x1, 90.8f, 93.2f, 4f, M("M_LimestoneTrim"), 0.5f, kit.puzzle);
                UnityEditor.GameObjectUtility.SetStaticEditorFlags(tiles[i], 0);
                kit.PointLight(new Vector3((x0 + x1) * 0.5f, 4.6f, 92f), Gold, 1f, 3f, false, false, tiles[i].transform);
                if (i > 0)
                {
                    tiles[i].AddComponent<PopIn>().fromScale = new Vector3(0.05f, 1f, 1f);
                    tiles[i].SetActive(false);
                }
            }
            for (int i = 0; i < 2; i++)
            {
                var trig = kit.Trigger("EchoTile_Trigger_" + (i + 1), tiles[i].transform.position + Vector3.up * 0.9f, new Vector3(2.1f, 1.2f, 2.4f), tiles[i].transform);
                LevelKit.ShowHide(trig, P, tiles[i + 1], true);
                trig.AddComponent<TriggerSound>().clip = EmberPaths.Clip("sfx_plate");
            }
            // a raised drawbridge stands at the island edge; the plate lays it across the gap (HitAndSetTransform on a target)
            var stoneBridge = kit.Block("Drawbridge", new Vector3(-12f, 6.45f, 97.3f), new Vector3(2.4f, 0.5f, 5.4f), trim, kit.puzzle, 0.06f, true, false, 0f, 90f);
            var bridgeDust = kit.Fx("Drawbridge_Dust", new Vector3(-12f, 4.2f, 98.6f), FxKind.Burst, 1.2f);
            bridgeDust.gameObject.SetActive(false);
            var plateTurn = kit.Plate("Plate_West_Bridge", new Vector3(-9.6f, 4f, 94.6f));
            LevelKit.SetTransform(plateTurn, P, stoneBridge.transform, new Vector3(-12f, 3.75f, 98.6f), Vector3.zero, Vector3.one, false, false);
            LevelKit.ShowHide(plateTurn, P, bridgeDust.gameObject, true);
            plateTurn.AddComponent<TriggerSound>().clip = EmberPaths.Clip("sfx_bridge");
            var lanternW = kit.Lantern("Lantern_West", new Vector3(-12f, 4f, 105.8f), false, "LanternDawn");
            var plateW = kit.Plate("Plate_West", new Vector3(-13.2f, 4f, 102.6f));
            var beamW = kit.Beam("Beam_West", new Vector3(-12f, 7.4f, 105.8f), beaconTop, 0.22f, "M_Beam_Gold");
            beamW.AddComponent<PopIn>().fromScale = new Vector3(0.05f, 1f, 0.05f);
            beamW.SetActive(false);
            LevelKit.ShowHide(plateW, P, LevelKit.Lit(lanternW), true);
            LevelKit.ShowHide(plateW, P, beamW, true);
            LevelKit.PlayAnim(plateW, P, stairs[0], stairs[0].clip.name);
            Ring(kit, "Ring_West_Return", new Vector3(-10.4f, 4f, 103.8f), hubPad.position, false);

            // ---- EAST: Lantern of the Forge (sun-stone breaks the crystal seal) ----
            kit.Floor("Bridge_East", 17.5f, 23.5f, 90.6f, 93.4f, 4f, trim, 0.6f);
            Island(kit, "Island_East", "Island_M", new Vector3(28f, 4f, 92f), 5f);
            var seal = kit.Model("CrystalSeal", new Vector3(29f, 4f, 94.2f), 0f, kit.puzzle, 1f, false, "CrystalSeal");
            var sealBox = seal.AddComponent<BoxCollider>();
            sealBox.center = new Vector3(0f, 1.5f, 0f);
            sealBox.size = new Vector3(3.2f, 3f, 3.2f);
            var sealBurst = kit.Fx("Seal_Burst", new Vector3(29f, 5.4f, 94.2f), FxKind.Burst, 1.6f);
            sealBurst.GetComponent<ParticleSystemRenderer>().sharedMaterial = M("M_Particle_Crystal");
            sealBurst.gameObject.SetActive(false);
            LevelKit.ShowHide(seal, "SunStone(Clone)", sealBurst.gameObject, true, false, false);
            LevelKit.DestroyOnHit(seal, "SunStone(Clone)", seal, false);
            LevelKit.DestroyOnHit(seal, "SunStone(Clone)", null, false);
            var plateSun = kit.Plate("Plate_East_Sun", new Vector3(25.4f, 4f, 89.6f));
            var sunSpawn = new GameObject("SunStoneSpawn").transform; sunSpawn.SetParent(kit.puzzle, false); sunSpawn.position = new Vector3(29f, 7.3f, 89.1f);
            LevelKit.Clone(plateSun, P, EmberPaths.Prefab("SunStone"), sunSpawn, false);
            plateSun.AddComponent<TriggerSound>().clip = EmberPaths.Clip("sfx_clone");
            kit.Block("SunChute_Pillar", new Vector3(29f, 5.3f, 88.2f), new Vector3(2.2f, 2.6f, 1.4f), trim);
            Vector3 sc0 = new Vector3(29f, 6.6f, 88.9f), sc1 = new Vector3(29f, 4.7f, 92.3f);
            kit.Ramp("SunChute_Floor", sc0, sc1, 1.8f, 0.3f, M("M_Gold"));
            kit.Ramp("SunChute_RailL", sc0 + new Vector3(-0.98f, 0.35f, 0f), sc1 + new Vector3(-0.98f, 0.35f, 0f), 0.15f, 0.7f, trim);
            kit.Ramp("SunChute_RailR", sc0 + new Vector3(0.98f, 0.35f, 0f), sc1 + new Vector3(0.98f, 0.35f, 0f), 0.15f, 0.7f, trim);
            kit.Model("Hopper", new Vector3(29f, 6.6f, 88.2f), 0f, kit.deco, 0.45f);
            var plateE = kit.Plate("Plate_East", new Vector3(29f, 4f, 94.2f));
            var lanternE = kit.Lantern("Lantern_East", new Vector3(29f, 4f, 96.4f), false, "LanternDawn");
            var beamE = kit.Beam("Beam_East", new Vector3(29f, 7.4f, 96.4f), beaconTop, 0.22f, "M_Beam_Gold");
            beamE.AddComponent<PopIn>().fromScale = new Vector3(0.05f, 1f, 0.05f);
            beamE.SetActive(false);
            LevelKit.ShowHide(plateE, P, LevelKit.Lit(lanternE), true);
            LevelKit.ShowHide(plateE, P, beamE, true);
            LevelKit.PlayAnim(plateE, P, stairs[1], stairs[1].clip.name);
            Ring(kit, "Ring_East_Return", new Vector3(25.8f, 4f, 94.6f), hubPad.position, false);

            // ---- SOUTH-WEST: Lantern of Winds (cyan ring up to a high island, then a moving slab) ----
            var padH = new GameObject("Pad_High").transform; padH.SetParent(kit.puzzle, false); padH.position = new Vector3(-14f, 10.7f, 67f);
            var ringN = Ring(kit, "Ring_Winds", new Vector3(2f, 4f, 87.4f), padH.position, true);
            Island(kit, "Island_High", "Island_M", new Vector3(-14f, 10f, 70f), 5f);
            Slab(kit, "Slab_Winds", new Vector3(-14f, 9.7f, 76.6f), new Vector3(0f, 0f, 4f));
            Island(kit, "Islet_Winds", "Island_M", new Vector3(-14f, 10f, 87f), 5f);
            var plateN = kit.Plate("Plate_Winds", new Vector3(-14f, 10f, 84.6f));
            var lanternN = kit.Lantern("Lantern_Winds", new Vector3(-14f, 10f, 88.8f), false, "LanternDawn");
            var beamN = kit.Beam("Beam_Winds", new Vector3(-14f, 13.4f, 88.8f), beaconTop, 0.22f, "M_Beam_Gold");
            beamN.AddComponent<PopIn>().fromScale = new Vector3(0.05f, 1f, 0.05f);
            beamN.SetActive(false);
            LevelKit.ShowHide(plateN, P, LevelKit.Lit(lanternN), true);
            LevelKit.ShowHide(plateN, P, beamN, true);
            LevelKit.PlayAnim(plateN, P, stairs[2], stairs[2].clip.name);
            Ring(kit, "Ring_Winds_Return", new Vector3(-10.8f, 10f, 87.6f), hubPad.position, true);
            kit.Shard("S3_C", new Vector3(-16.8f, 10.9f, 89.6f));
            kit.Checkpoint("CP_3C_High", new Vector3(-17f, 10f, 68f), 0f, new Vector3(-14f, 11.2f, 68.6f), new Vector3(6f, 2.5f, 1.4f), new Vector3(-14f, 10f, 69.4f));
            kit.FloorText("THREE LANTERNS - THREE STEPS", new Vector3(8f, 4f, 99.5f), 2f, new Color(2.2f, 1.6f, 0.8f));
            kit.Beat("3C", "Three Lanterns", hubCenter, new Vector3(0f, 22f, -22f), new Vector3(0f, 0f, 8f), "Hub with three spokes; each lantern raises one step to the beacon");

            // =========================================================== 3D  BEACON OF DAWN (finale)
            var dawnVolume = new GameObject("PostProcess_Dawn").AddComponent<Volume>();
            dawnVolume.isGlobal = true;
            dawnVolume.priority = 1;
            dawnVolume.weight = 0f;
            dawnVolume.sharedProfile = VolumeFactory.Get("PP_SkyDawn");
            var sunAnim = refs.sun.gameObject.AddComponent<Animation>();
            var sunClip = AnimFactory.Load("SunRise");
            AnimationUtility.SetAnimationClips(sunAnim, new[] { sunClip });
            sunAnim.clip = sunClip;
            sunAnim.playAutomatically = false;

            var finale = new GameObject("Finale");
            finale.transform.SetParent(kit.puzzle, false);
            // the beacon's fire bowl stays dark coal until the ignition shows the Finale group
            PrefabFactory.CoalBed(beacon, finale, 0f, 2.15f).transform.position = new Vector3(8f, 7f + 11.74f, 124f);
            var fire = FxFactory.Create(finale.transform, "BeaconFire", Vector3.zero, FxKind.BigFire, 3.2f);
            fire.transform.position = beaconTop + Vector3.up * 0.4f;
            FxFactory.Create(finale.transform, "BeaconSparks", Vector3.zero, FxKind.Sparks, 3f).transform.position = beaconTop + Vector3.up * 1f;
            var beaconLight = PrefabFactory.PointLight(finale.transform, "BeaconLight", Vector3.zero, new Color(1f, 0.7f, 0.4f), 8f, 70f, LightShadows.Soft);
            beaconLight.transform.position = beaconTop + Vector3.up * 2f;
            beaconLight.gameObject.AddComponent<FadeInLight>().duration = 2.5f;
            var vb = finale.AddComponent<VolumeBlend>();
            vb.fadeIn = dawnVolume;
            vb.fadeOut = refs.volume;
            vb.duration = 6f;
            var sb = finale.AddComponent<SkyBlend>();
            sb.sun = refs.sun;
            sb.duration = 6f;
            sb.fogTo = new Color(0.95f, 0.75f, 0.68f);
            sb.fogDensityTo = 0.0035f;
            sb.ambientTo = new Color(0.46f, 0.4f, 0.42f);
            sb.sunIntensityTo = 2.2f;
            sb.sunColorTo = new Color(1f, 0.86f, 0.68f);
            var cb = finale.AddComponent<CameraBlendOnEnable>();
            cb.offset = new Vector3(0f, 15f, -26f);
            cb.fieldOfView = 55f;
            cb.blendTime = 5f;
            var dl = finale.AddComponent<DelayedLoadScene>();
            dl.sceneName = "Ending";
            dl.delay = 8.5f;
            dl.fade = 2.5f;
            dl.fadeColor = new Color(1f, 0.95f, 0.88f);
            var music = finale.AddComponent<AudioSource>();
            music.clip = EmberPaths.Clip("sfx_finale");
            music.playOnAwake = true;
            music.spatialBlend = 0f;
            finale.SetActive(false);

            var ignite = kit.Trigger("Ignition", new Vector3(8f, 8.2f, 118.6f), new Vector3(3.2f, 2.2f, 3.2f), kit.puzzle);
            LevelKit.ShowHide(ignite, P, finale, true);
            LevelKit.PlayAnim(ignite, P, sunAnim, "SunRise");
            ignite.AddComponent<DisarmAfterTrigger>();
            kit.Lantern("Terrace_Lantern_L", new Vector3(3f, 7f, 116f), true, "LanternDawn");
            kit.Lantern("Terrace_Lantern_R", new Vector3(13f, 7f, 116f), true, "LanternDawn");
            kit.CameraZone("Cam_Terrace", new Vector3(8f, 8.5f, 114.8f), new Vector3(14f, 3f, 1.4f), new Vector3(0f, 9f, -13f), 50f);
            kit.Checkpoint("CP_3D", new Vector3(14f, 7f, 115f), 0f, new Vector3(8f, 8.2f, 114.8f), new Vector3(14f, 2.5f, 1.4f), new Vector3(8f, 7f, 115.6f));
            kit.FloorText("REKINDLE THE DAWN", new Vector3(8f, 7f, 116.4f), 1.8f, new Color(2.4f, 1.6f, 0.7f));
            kit.Beat("3D", "Beacon of Dawn", new Vector3(8f, 7f, 118f), new Vector3(-14f, 10f, -14f), new Vector3(0f, 6f, 6f), "Ignition: beacon fire, sunrise, post-processing and sky blend from night to dawn");
            StageCommon.Probe(new Vector3(8f, 8f, 100f), new Vector3(60f, 30f, 60f), 1f);

            // =========================================================== solution for AutoPilot
            var s1 = kit.Step("3A gold ring", ringA.transform.Find("Trigger"), 1.5f);
            LevelKit.Expect(s1, SolutionCheck.Kind.PlayerNear, padB.gameObject);
            var s2 = kit.Step("3A cyan ring", ringB.transform.Find("Trigger"), 1.5f);
            LevelKit.Expect(s2, SolutionCheck.Kind.PlayerNear, padC.gameObject);
            var s3 = kit.Step("3C echo tile 1", tiles[0].transform, 1.2f);
            LevelKit.Expect(s3, SolutionCheck.Kind.Active, tiles[1]);
            var s4 = kit.Step("3C echo tile 2", tiles[1].transform, 1.2f);
            LevelKit.Expect(s4, SolutionCheck.Kind.Active, tiles[2]);
            var s5 = kit.Step("3C turn the bridge", plateTurn.transform, 1.2f);
            LevelKit.Expect(s5, SolutionCheck.Kind.Rotated, stoneBridge);
            var s6 = kit.Step("3C west lantern", plateW.transform, 2.5f);
            LevelKit.Expect(s6, SolutionCheck.Kind.Active, LevelKit.Lit(lanternW));
            LevelKit.Expect(s6, SolutionCheck.Kind.AnimationPlayed, stairs[0].gameObject, stairs[0].clip.name);
            var s7 = kit.Step("3C release the sun-stone", plateSun.transform, 5f);
            LevelKit.Expect(s7, SolutionCheck.Kind.Destroyed, seal);
            var s8 = kit.Step("3C east lantern", plateE.transform, 2.5f);
            LevelKit.Expect(s8, SolutionCheck.Kind.Active, LevelKit.Lit(lanternE));
            LevelKit.Expect(s8, SolutionCheck.Kind.AnimationPlayed, stairs[1].gameObject, stairs[1].clip.name);
            var s9 = kit.Step("3C winds ring", ringN.transform.Find("Trigger"), 1.5f);
            LevelKit.Expect(s9, SolutionCheck.Kind.PlayerNear, padH.gameObject);
            var s10 = kit.Step("3C winds lantern", plateN.transform, 2.5f);
            LevelKit.Expect(s10, SolutionCheck.Kind.Active, LevelKit.Lit(lanternN));
            LevelKit.Expect(s10, SolutionCheck.Kind.AnimationPlayed, stairs[2].gameObject, stairs[2].clip.name);
            kit.Step("3D ignite the beacon", ignite.transform, 6f, true);

            StageCommon.Save(scene, Name);
        }
    }
}
