using UnityEngine;

namespace Ember.EditorTools
{
    /// <summary>
    /// Stage 2 - THE EMBER FORGE. "Fire moves things": the player releases objects that act for them.
    /// HitAndClone (rafts, iron ball, boulders, counterweight), HitAndDestroy (cracked wall, clean-up),
    /// HitAndPlayAnimation with enter/exit clips (gate held open only while the weight sits on the scale).
    /// </summary>
    public static class BuildStage2
    {
        public const string Name = "Stage2";
        const string P = "Player";
        static readonly Color Fire = new Color(1f, 0.45f, 0.14f);
        static readonly Color Amber = new Color(1f, 0.62f, 0.26f);

        static Material M(string n) => EmberPaths.Mat(n);
        static float H(float z) => 4.5f * Mathf.Clamp01((z - 60.5f) / 28f);   // gallery ramp height

        public static void Run()
        {
            var scene = StageCommon.NewScene();
            var theme = new Theme
            {
                sky = "M_Sky_Forge", fogColor = new Color(0.09f, 0.03f, 0.02f), fogDensity = 0.016f,
                ambient = new Color(0.36f, 0.27f, 0.23f), sunColor = new Color(1f, 0.62f, 0.42f), sunIntensity = 1.0f,
                sunEuler = new Vector3(62f, 20f, 0f), profile = "PP_Forge", ambience = "amb_forge", reverb = AudioReverbPreset.StoneCorridor
            };
            var refs = StageCommon.Setup(2, "II  -  THE EMBER FORGE", "FIRE MOVES WHAT THE EMBER CANNOT", theme,
                new Vector3(-4f, 0f, 0f), new Vector3(0f, 7.5f, -11f), 50f);
            var kit = new LevelKit(Name, M("M_Floor_Forge"), M("M_Wall_Forge"), M("M_BasaltTrim"));
            kit.player = refs.player.transform;
            kit.solution.nextScene = "Stage3";
            var trim = M("M_BasaltTrim");
            var iron = M("M_Iron");

            // =========================================================== 2A  LAVA FERRY
            kit.Floor("Start", -12f, 4f, -6f, 10.3f);
            kit.Wall("Start_L", -13f, -12f, -7f, 10.3f, 0f, 5f);
            kit.Wall("Start_R", 4f, 5f, -7f, 10.3f, 0f, 5f);
            kit.Wall("Start_Back", -12f, 4f, -7f, -6f, 0f, 1.4f);
            kit.Checkpoint("CP_Start", new Vector3(-10.8f, 0f, -4.8f), 45f, new Vector3(-4f, 1.2f, 0f), new Vector3(4f, 2.5f, 4f), new Vector3(-4f, 0f, 0f), true);
            // river of lava flowing +X, rafts spawn in the cave on the left
            var lava = kit.Plane("Lava_River", new Vector3(0f, -0.9f, 12f), new Vector2(44f, 8f), M("M_Lava"), kit.geo);
            lava.AddComponent<ScrollingUVs>().uvAnimationRate = new Vector2(0.06f, 0.0f);
            kit.KillZone("Kill_River", new Vector3(0f, -2f, 12f), new Vector3(44f, 2f, 8f));
            kit.Wall("FarBank_W", -20f, 4f, 14.5f, 15.5f, -1f, 6f);
            kit.Wall("FarBank_E", 12f, 20f, 14.5f, 15.5f, -1f, 6f);
            kit.Wall("NearBank_E", 5f, 20f, 9f, 10.3f, -1f, 3f);
            kit.Wall("NearBank_W", -20f, -13f, 9f, 10.3f, -1f, 3f);
            kit.Wall("Cave", -21f, -19f, 8f, 16f, -1f, 6f);
            kit.Fx("Cave_Steam", new Vector3(-18f, 0f, 12f), FxKind.Steam, 0.6f);
            kit.PointLight(new Vector3(-17f, 1.5f, 12f), Fire, 4f, 12f, true);
            // lava light: emissive lava does not light its surroundings without GI, so warm lights sit on the lava
            for (float x = -12f; x <= 12f; x += 8f)
                kit.PointLight(new Vector3(x, 0.4f, 12f), Fire, 2.6f, 9f, true);
            kit.PointLight(new Vector3(4.2f, 0.2f, 59.5f), Fire, 2.2f, 7f, true);          // 2C trench
            kit.PointLight(new Vector3(3f, 4f, 41f), Fire, 2f, 13f);                       // 2B hall fill
            foreach (float gz in new[] { 63f, 70f, 77f, 84f })                                 // 2C gallery fill (walls shade the sun)
                kit.PointLight(new Vector3(5f, H(gz) + 3.2f, gz), Amber, 3f, 12f);
            kit.PointLight(new Vector3(0f, 4.5f + 4f, 106f), Fire, 2f, 14f);               // 2D forge entry fill
            var fall = kit.Plane("LavaFall", new Vector3(19f, -4f, 12f), new Vector2(8f, 7f), M("M_LavaFall"), kit.geo, 0f);
            fall.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            fall.AddComponent<ScrollingUVs>().uvAnimationRate = new Vector2(0f, -0.35f);
            var raftSink = kit.Trigger("Raft_Sink", new Vector3(17f, 0f, 12f), new Vector3(2f, 4f, 6f));
            LevelKit.DestroyOnHit(raftSink, "LavaRaft(Clone)", null);
            var rafts = kit.SubGroup("Rafts");
            var raftSpawn = new GameObject("RaftSpawn").transform;
            raftSpawn.SetParent(kit.puzzle, false);
            raftSpawn.position = new Vector3(-16.5f, 0f, 12f);
            var plateRaft = kit.Plate("Plate_2A", new Vector3(-8f, 0f, 6f));
            LevelKit.Clone(plateRaft, P, EmberPaths.Prefab("LavaRaft"), raftSpawn, true, 5f, false, rafts);
            plateRaft.AddComponent<DisarmAfterTrigger>();
            plateRaft.AddComponent<TriggerSound>().clip = EmberPaths.Clip("sfx_clone");
            kit.Lantern("Lantern_2A", new Vector3(-10.4f, 0f, 7.6f), true, "Brazier");
            kit.Shard("S2_A", new Vector3(0f, 1.4f, 12f));
            kit.Floor("Landing", 4f, 12f, 13.7f, 22f);
            kit.Wall("Landing_E", 12f, 13f, 14f, 30f, 0f, 6f);
            kit.Wall("Landing_W", 3f, 4f, 15.5f, 30f, 0f, 6f);
            kit.Checkpoint("CP_2A", new Vector3(11.2f, 0f, 15.2f), 0f, new Vector3(8f, 1.2f, 15.4f), new Vector3(8f, 2.5f, 1.4f), new Vector3(8f, 0f, 16.6f));
            kit.CameraZone("Cam_River", new Vector3(-4f, 2f, 3f), new Vector3(16f, 4f, 2f), new Vector3(0f, 10f, -12f), 52f);
            kit.FloorText("WAIT FOR THE RAFT", new Vector3(-3.5f, 0f, 8.4f), 3.2f, new Color(2.2f, 1.1f, 0.4f));
            kit.Beat("2A", "Lava Ferry", new Vector3(0f, 0f, 9f), new Vector3(-8f, 8f, -10f), new Vector3(2f, 0f, 3f), "Plate starts a stream of basalt rafts (HitAndClone repeat)");

            // =========================================================== 2B  BREAKER
            kit.Floor("Corridor_2B", 4f, 12f, 22f, 30f);
            kit.Floor("Hall_2B", -6f, 12f, 30f, 52f);
            kit.Wall("Hall_2B_W", -7f, -6f, 30f, 52.5f, 0f, 8f);
            kit.Wall("Hall_2B_E", 12f, 13f, 30f, 52.5f, 0f, 8f);
            kit.Floor("Balcony", -6f, 12f, 30f, 34f, 3.5f, trim, 0.6f);
            foreach (var x in new[] { -4f, 2f })
                kit.Block("Balcony_Pillar", new Vector3(x, 1.45f, 32f), new Vector3(0.8f, 2.9f, 0.8f), trim);
            // starts at x = -2 so the top of Ramp_Balcony (x -6..-2) stays open onto the balcony
            kit.Wall("Balcony_Rail_W", -2f, 4f, 33.75f, 34f, 3.5f, 4.2f, iron);
            kit.Wall("Balcony_Rail_E", 6f, 12f, 33.75f, 34f, 3.5f, 4.2f, iron);
            kit.Ramp("Ramp_Balcony", new Vector3(-4f, 0f, 48f), new Vector3(-4f, 3.5f, 33.9f), 4f, 0.6f, trim);
            var plateIron = kit.Plate("Plate_2B", new Vector3(0f, 3.5f, 32f));
            kit.Model("Hopper", new Vector3(5f, 3.5f, 33.1f), 0f, kit.deco, 0.6f);
            var ironSpawn = new GameObject("IronSpawn").transform;
            ironSpawn.SetParent(kit.puzzle, false);
            ironSpawn.position = new Vector3(5f, 4.4f, 34.5f);
            LevelKit.Clone(plateIron, P, EmberPaths.Prefab("IronBall"), ironSpawn, false);
            plateIron.AddComponent<TriggerSound>().clip = EmberPaths.Clip("sfx_clone");
            // chute: raised U-channel running straight at the cracked wall
            kit.Block("Chute_Guard", new Vector3(5f, 3.8f, 33.85f), new Vector3(2.2f, 0.6f, 0.2f), iron);   // keeps the ember out of the chute
            Vector3 c0 = new Vector3(5f, 3.3f, 34f), c1 = new Vector3(5f, 0.75f, 51.3f);
            kit.Ramp("Chute_Floor", c0, c1, 1.8f, 0.3f, iron);
            kit.Ramp("Chute_RailL", c0 + new Vector3(-0.98f, 0.35f, 0f), c1 + new Vector3(-0.98f, 0.35f, 0f), 0.15f, 0.7f, M("M_Brass"));
            kit.Ramp("Chute_RailR", c0 + new Vector3(0.98f, 0.35f, 0f), c1 + new Vector3(0.98f, 0.35f, 0f), 0.15f, 0.7f, M("M_Brass"));
            foreach (var z in new[] { 40f, 46f })
            {
                float y = Mathf.Lerp(c0.y, c1.y, (z - c0.z) / (c1.z - c0.z));
                kit.Block("Chute_Post", new Vector3(5f, (y - 0.3f) * 0.5f, z), new Vector3(0.3f, y - 0.3f, 0.3f), iron);
            }
            var wall = kit.Prefab("CrackedWall", new Vector3(5f, 0f, 52f), 180f, kit.puzzle, "CrackedWall");
            var rubble = kit.Prefab("WallRubble", new Vector3(5f, 0f, 52f), 180f, kit.puzzle, "WallRubble");
            rubble.SetActive(false);
            LevelKit.ShowHide(wall, "IronBall(Clone)", rubble, true, false, false);
            LevelKit.DestroyOnHit(wall, "IronBall(Clone)", wall, false);
            LevelKit.DestroyOnHit(wall, "IronBall(Clone)", null, false);
            kit.Wall("Wall_2B_W", -6f, 2f, 51.5f, 52.5f, 0f, 8f);
            kit.Wall("Wall_2B_E", 8f, 12f, 51.5f, 52.5f, 0f, 8f);
            kit.Prefab("Hammer", new Vector3(-2.5f, 0f, 44f), 90f, kit.deco, "Hammer_2B");
            kit.Model("Anvil", new Vector3(-0.5f, 0f, 44f), 90f, kit.deco, 1.4f, true);
            kit.Model("Gear", new Vector3(11.6f, 4f, 40f), 90f, kit.deco, 2f).AddComponent<Bob>().spinDegreesPerSecond = new Vector3(0f, 0f, 20f);
            kit.Lantern("Brazier_2B_1", new Vector3(10.6f, 0f, 36f), true, "Brazier");
            kit.Lantern("Brazier_2B_2", new Vector3(-4.8f, 3.5f, 31.2f), true, "Brazier");
            kit.Checkpoint("CP_2B", new Vector3(11.2f, 0f, 30.6f), 0f, new Vector3(8f, 1.2f, 30.6f), new Vector3(8f, 2.5f, 1.2f), new Vector3(8f, 0f, 31.6f));
            kit.CameraZone("Cam_Hall2B", new Vector3(8f, 2f, 29f), new Vector3(8f, 4f, 1.2f), new Vector3(0f, 11f, -13f), 52f);
            kit.FloorText("RELEASE THE IRON", new Vector3(1.4f, 3.5f, 32.9f), 2.2f, new Color(2.2f, 1.1f, 0.4f));
            kit.Beat("2B", "Breaker", new Vector3(5f, 0f, 41f), new Vector3(-6f, 12f, -10f), new Vector3(2f, 0f, 6f), "Iron ball released from the balcony smashes the cracked wall (Clone + Destroy + ShowHide)");

            // =========================================================== 2C  BOULDER GALLERY
            kit.Floor("Corridor_2C", 2.5f, 7.5f, 52f, 56f);
            kit.Floor("Gallery_Bottom", 0f, 10f, 56f, 58.5f);
            kit.Floor("Trench_Bridge", 8.4f, 10f, 58.5f, 60.5f, 0f, trim, 0.6f);
            var trenchLava = kit.Plane("Trench_Lava", new Vector3(4.2f, -1.6f, 59.5f), new Vector2(8.4f, 2f), M("M_Lava"), kit.geo);
            trenchLava.AddComponent<ScrollingUVs>().uvAnimationRate = new Vector2(-0.05f, 0.02f);
            kit.KillZone("Kill_Trench", new Vector3(4.2f, -2.4f, 59.5f), new Vector3(8.6f, 1.2f, 2.2f));
            var trench = kit.Trigger("Trench_Destroy", new Vector3(4.2f, -1f, 59.5f), new Vector3(8.4f, 1.6f, 2f));
            LevelKit.DestroyOnHit(trench, "Boulder(Clone)", null);
            kit.Block("Deflector", new Vector3(8.2f, 0.5f, 62.2f), new Vector3(0.5f, 1f, 3f), trim, null, 0.06f, true, true, -35f);
            kit.Ramp("Gallery_Ramp", new Vector3(5f, 0f, 60.5f), new Vector3(5f, 4.5f, 88.5f), 10f, 1f);
            // side walls with alcoves
            foreach (var (z0, z1) in new[] { (56f, 65f), (68f, 75f), (78f, 94f) }) kit.Wall("Gallery_W", -1f, 0f, z0, z1, -2f, 11f);
            foreach (var (z0, z1) in new[] { (56f, 70f), (73f, 80f), (83f, 94f) }) kit.Wall("Gallery_E", 10f, 11f, z0, z1, -2f, 11f);
            foreach (var (x0, x1, z0, z1, back) in new[] { (-3f, 0f, 65f, 68f, -4f), (-3f, 0f, 75f, 78f, -4f), (10f, 13f, 70f, 73f, 13f), (10f, 13f, 80f, 83f, 13f) })
            {
                float h = H((z0 + z1) * 0.5f);
                kit.Floor("Alcove", x0, x1, z0, z1, h, trim, 0.8f);
                kit.Wall("Alcove_Back", Mathf.Min(back, back + 1f), Mathf.Max(back, back + 1f), z0 - 1f, z1 + 1f, h - 1f, h + 5f);
                kit.Wall("Alcove_S", x0, x1, z0 - 1f, z0, h - 1f, h + 5f);
                kit.Wall("Alcove_N", x0, x1, z1, z1 + 1f, h - 1f, h + 5f);
                kit.PointLight(new Vector3((x0 + x1) * 0.5f, h + 2.2f, (z0 + z1) * 0.5f), Amber, 1.6f, 4.5f, true);
            }
            kit.Shard("S2_B", new Vector3(11.8f, H(81.5f) + 0.9f, 81.5f));
            // two boulder lanes (left / right); the centre lane between them is the discoverable safe path
            var spawner = kit.Trigger("Boulder_Spawner", new Vector3(5f, 4f, 74.5f), new Vector3(16f, 10f, 28f), kit.puzzle);
            var boulders = kit.SubGroup("Boulders");
            foreach (var x in new[] { 2.6f, 7.4f })
            {
                var spawn = new GameObject("BoulderSpawn").transform;
                spawn.SetParent(kit.puzzle, false);
                spawn.position = new Vector3(x, 7.2f, 87.4f);
                LevelKit.Clone(spawner, P, EmberPaths.Prefab("Boulder"), spawn, true, 2.6f, true, boulders);
            }
            kit.Model("Furnace", new Vector3(10.4f, 4.5f, 91.8f), -90f, kit.deco, 0.42f);
            kit.PointLight(new Vector3(5f, 7.5f, 88f), Fire, 4f, 12f, true);
            kit.Floor("Gallery_Top", 0f, 10f, 88.5f, 94f, 4.5f);
            kit.Checkpoint("CP_2C_Bottom", new Vector3(0.6f, 0f, 56.6f), 0f, new Vector3(5f, 1.2f, 57.2f), new Vector3(10f, 2.5f, 1.4f), new Vector3(6f, 0f, 57.6f));
            kit.Checkpoint("CP_2C_Top", new Vector3(9.3f, 4.5f, 91f), 0f, new Vector3(5f, 5.7f, 90.4f), new Vector3(10f, 2.5f, 1.4f), new Vector3(3f, 4.5f, 91.4f));
            kit.CameraZone("Cam_Gallery", new Vector3(5f, 2f, 57.2f), new Vector3(10f, 4f, 1.4f), new Vector3(0f, 9f, -9.5f), 55f);
            kit.Beat("2C", "Boulder Gallery", new Vector3(5f, H(74f), 74f), new Vector3(2f, 13f, -12f), new Vector3(0f, 0f, 6f), "Boulders roll down every 2.4 s while the ember is in the gallery (Clone repeat + cancelWhenExit)");

            // =========================================================== 2D  HEART OF THE FORGE
            const float F = 4.5f;
            kit.Floor("Corridor_2D", 0f, 6f, 94f, 100f, F);
            kit.Wall("Corridor_2D_W", -1f, 0f, 94f, 100f, F, F + 4f);
            kit.Wall("Corridor_2D_E", 6f, 7f, 94f, 100f, F, F + 4f);
            // the scale basin is exactly plate-sized, so a counterweight that drops in always rests on the plate
            kit.Floor("Forge_A", -12f, 14f, 100f, 119.1f, F);
            kit.Floor("Forge_B", -12f, -8.4f, 119.1f, 121.9f, F);
            kit.Floor("Forge_C", -5.6f, 14f, 119.1f, 121.9f, F);
            kit.Floor("Forge_D", -12f, 14f, 121.9f, 130f, F);
            kit.Floor("Basin", -8.4f, -5.6f, 119.1f, 121.9f, F - 1f, trim);
            kit.Wall("Forge_W", -13f, -12f, 100f, 131f, F, F + 10f);
            kit.Wall("Forge_E", 14f, 15f, 100f, 131f, F, F + 10f);
            kit.Wall("Forge_N_W", -12f, -3.25f, 130f, 131f, F, F + 10f);
            kit.Wall("Forge_N_E", 3.25f, 14f, 130f, 131f, F, F + 10f);
            kit.Wall("Forge_N_Top", -3.25f, 3.25f, 130f, 131f, F + 5.85f, F + 10f);
            kit.Model("Funnel", new Vector3(-7f, F - 1f, 120.5f), 0f, kit.deco, 0.75f);
            var scale = kit.Plate("Plate_Scale", new Vector3(-7f, F - 1f, 120.5f), "Counterweight(Clone)", "PlateUp");
            // walkway + plate + hopper + chute
            kit.Floor("Walkway", -12f, -8f, 101f, 114f, 6f, trim, 0.6f);
            kit.Ramp("Walkway_Ramp", new Vector3(-2f, F, 102.5f), new Vector3(-8.05f, 6f, 102.5f), 3f, 0.6f, trim);
            kit.Wall("Walkway_Rail", -8.15f, -8f, 104.5f, 114f, 6f, 6.6f, iron);
            var plateWeight = kit.Plate("Plate_2D", new Vector3(-10f, 6f, 106f));
            kit.Model("Hopper", new Vector3(-7f, 6f, 104.3f), 0f, kit.deco, 0.6f);
            var weightSpawn = new GameObject("WeightSpawn").transform;
            weightSpawn.SetParent(kit.puzzle, false);
            weightSpawn.position = new Vector3(-7f, 6.9f, 105.6f);
            LevelKit.Clone(plateWeight, P, EmberPaths.Prefab("Counterweight"), weightSpawn, false);
            plateWeight.AddComponent<TriggerSound>().clip = EmberPaths.Clip("sfx_clone");
            Vector3 w0 = new Vector3(-7f, 5.9f, 105.2f), w1 = new Vector3(-7f, 4.75f, 117.6f);
            kit.Ramp("WeightChute_Floor", w0, w1, 1.8f, 0.3f, iron);
            kit.Ramp("WeightChute_RailL", w0 + new Vector3(-0.98f, 0.35f, 0f), w1 + new Vector3(-0.98f, 0.35f, 0f), 0.15f, 0.7f, M("M_Brass"));
            kit.Ramp("WeightChute_RailR", w0 + new Vector3(0.98f, 0.35f, 0f), w1 + new Vector3(0.98f, 0.35f, 0f), 0.15f, 0.7f, M("M_Brass"));
            kit.Block("WeightChute_Post", new Vector3(-7f, F + 0.35f, 112f), new Vector3(0.3f, 0.7f, 0.3f), iron);
            // the crucible gate (held open by the counterweight) and the forge lamps
            var gate = kit.Prefab("ForgeGate", new Vector3(0f, F, 130.5f), 180f, kit.puzzle, "ForgeGate");
            var gateAnim = LevelKit.Find(gate, "Model").GetComponent<Animation>();
            var lamps = new GameObject("ForgeLamps");
            lamps.transform.SetParent(kit.puzzle, false);
            foreach (var p in new[] { new Vector3(-11f, F, 104f), new Vector3(-11f, F, 126f), new Vector3(13f, F, 104f), new Vector3(13f, F, 126f), new Vector3(-3.6f, F + 6.5f, 129.5f), new Vector3(3.6f, F + 6.5f, 129.5f) })
            {
                FxFactory.Create(lamps.transform, "LampFire", Vector3.zero, FxKind.BigFire, 1.2f).transform.position = p + Vector3.up * 0.3f;
                var l = PrefabFactory.PointLight(lamps.transform, "LampLight", Vector3.zero, Amber, 3.5f, 12f);
                l.transform.position = p + Vector3.up * 1.2f;
                l.gameObject.AddComponent<Flicker>();
                l.gameObject.AddComponent<FadeInLight>();
            }
            var lampSound = lamps.AddComponent<AudioSource>();
            lampSound.clip = EmberPaths.Clip("sfx_ignite");
            lampSound.playOnAwake = true;
            lampSound.spatialBlend = 0f;
            lamps.SetActive(false);
            LevelKit.PlayAnim(scale, "Counterweight(Clone)", gateAnim, "ForgeGateOpen", "ForgeGateClose");
            LevelKit.ShowHide(scale, "Counterweight(Clone)", lamps, true, true);
            foreach (var (x, yaw) in new[] { (-9.5f, 90f), (11.5f, -90f) })
            {
                kit.Prefab("Hammer", new Vector3(x, F, 125f), yaw, kit.deco, "Hammer_2D");
                kit.Model("Anvil", new Vector3(x + (yaw > 0 ? 2f : -2f), F, 125f), yaw, kit.deco, 1.5f, true);
            }
            kit.Model("Gear", new Vector3(-11.6f, F + 6f, 116f), 90f, kit.deco, 2.6f).AddComponent<Bob>().spinDegreesPerSecond = new Vector3(0f, 0f, -14f);
            kit.Model("Gear", new Vector3(13.6f, F + 5f, 112f), 90f, kit.deco, 2f).AddComponent<Bob>().spinDegreesPerSecond = new Vector3(0f, 0f, 18f);
            kit.Shard("S2_C", new Vector3(-10f, 6.9f, 113f));
            // crucible room and exit
            kit.Floor("Crucible", -6f, 6f, 130f, 144f, F);
            kit.Wall("Crucible_W", -7f, -6f, 131f, 144f, F, F + 8f);
            kit.Wall("Crucible_E", 6f, 7f, 131f, 144f, F, F + 8f);
            kit.Wall("Crucible_Back", -6f, 6f, 144f, 145f, F - 1f, F + 1.4f, trim);
            kit.Model("Furnace", new Vector3(0f, F, 149f), 180f, kit.deco, 1f);
            var portal = kit.Prefab("ExitPortal", new Vector3(0f, F, 137.5f), 0f, kit.puzzle, "ExitPortal");
            portal.GetComponentInChildren<HitAndLoadScene>().loadSceneName = "Stage3";
            kit.PointLight(new Vector3(0f, F + 3f, 142f), Fire, 5f, 16f, true);
            kit.Fx("Forge_Embers", new Vector3(0f, F + 1f, 115f), FxKind.Embers, 1.6f);
            kit.Fx("Forge_Smoke", new Vector3(0f, F + 8f, 149f), FxKind.Smoke, 2f);
            kit.Checkpoint("CP_2D", new Vector3(5.4f, F, 100.6f), 0f, new Vector3(3f, F + 1.2f, 99.2f), new Vector3(6f, 2.5f, 1.4f), new Vector3(3f, F, 101f));
            kit.CameraZone("Cam_Forge", new Vector3(3f, F + 2f, 99.2f), new Vector3(6f, 4f, 1.4f), new Vector3(0f, 11.5f, -14.5f), 52f);
            kit.FloorText("A WEIGHT FOR THE SCALE", new Vector3(-10f, 6f, 108.6f), 1.6f, new Color(2.2f, 1.1f, 0.4f));
            kit.Beat("2D", "Heart of the Forge", new Vector3(0f, F, 116f), new Vector3(12f, 12f, -14f), new Vector3(-4f, 1f, 6f), "Counterweight on the scale holds the crucible gate open (enter/exit animations)");
            kit.KillZone("Kill_Void", new Vector3(0f, -12f, 70f), new Vector3(120f, 2f, 200f));
            StageCommon.Probe(new Vector3(4f, F + 4f, 115f), new Vector3(28f, 12f, 32f), 0.9f);

            // =========================================================== solution for AutoPilot
            var s1 = kit.Step("2A raft plate", plateRaft.transform, 2.5f);
            LevelKit.Expect(s1, SolutionCheck.Kind.AnyCloneNamed, null, "LavaRaft(Clone)");
            var s2 = kit.Step("2B iron ball plate", plateIron.transform, 9f);
            LevelKit.Expect(s2, SolutionCheck.Kind.Destroyed, wall);
            LevelKit.Expect(s2, SolutionCheck.Kind.Active, rubble);
            var probe = new GameObject("Probe_Gallery").transform;
            probe.SetParent(kit.puzzle, false);
            probe.position = new Vector3(8.5f, H(66f) + 0.3f, 66f);
            var s3 = kit.Step("2C boulder gallery", probe, 4f);
            LevelKit.Expect(s3, SolutionCheck.Kind.AnyCloneNamed, null, "Boulder(Clone)");
            var s4 = kit.Step("2D counterweight plate", plateWeight.transform, 12f);
            LevelKit.Expect(s4, SolutionCheck.Kind.AnyCloneNamed, null, "Counterweight(Clone)");
            LevelKit.Expect(s4, SolutionCheck.Kind.AnimationPlayed, gateAnim.gameObject, "ForgeGateOpen");
            LevelKit.Expect(s4, SolutionCheck.Kind.Active, lamps);
            kit.Step("2D exit portal", portal.transform.Find("InnerTrigger"), 1.5f, true);

            StageCommon.Save(scene, Name);
        }
    }
}
