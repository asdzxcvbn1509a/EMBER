using UnityEngine;

namespace Ember.EditorTools
{
    /// <summary>
    /// Stage 1 - THE SUNKEN CRYPT. Teaches: rolling, plates (HitAndShowHide / HitAndPlayAnimation),
    /// jumping, gates, sequence of seals (homage to the class TimerSample), and the exit (HitAndLoadScene).
    /// Layout runs along +Z (the class CameraFollow looks from behind).
    /// </summary>
    public static class BuildStage1
    {
        public const string Name = "Stage1";
        const string P = "Player";
        static readonly Color Amber = new Color(1f, 0.6f, 0.25f);
        static readonly Color Moon = new Color(0.55f, 0.68f, 1f);
        static readonly Color EngraveHdr = new Color(2.2f, 1.2f, 0.45f);

        static Material M(string n) => EmberPaths.Mat(n);

        public static void Run()
        {
            var scene = StageCommon.NewScene();
            var theme = new Theme
            {
                sky = "M_Sky_Crypt", fogColor = new Color(0.025f, 0.03f, 0.065f), fogDensity = 0.022f,
                ambient = new Color(0.15f, 0.17f, 0.27f), sunColor = Moon, sunIntensity = 0.5f,
                sunEuler = new Vector3(58f, -25f, 0f), profile = "PP_Crypt", ambience = "amb_crypt", reverb = AudioReverbPreset.Cave
            };
            var refs = StageCommon.Setup(1, "I  -  THE SUNKEN CRYPT", "WHERE THE LAST EMBER AWAKENS", theme,
                new Vector3(0f, 0.42f, 0f), new Vector3(0f, 7.5f, -11f), 50f);
            var kit = new LevelKit(Name, M("M_Floor_Crypt"), M("M_Wall_Crypt"), M("M_StoneTrim"));
            kit.player = refs.player.transform;
            kit.solution.nextScene = "Stage2";
            var trim = M("M_StoneTrim");

            // =========================================================== 1A  AWAKENING
            kit.Floor("Start", -6f, 6f, -6f, 6f);
            kit.Wall("Start_Back", -6f, 6f, -7f, -6f, 0f, 1.4f);
            kit.Wall("Start_L", -7f, -6f, -7f, 6f, 0f, 4f);
            kit.Wall("Start_R", 6f, 7f, -7f, 6f, 0f, 4f);
            kit.Model("Altar", Vector3.zero, 0f, kit.geo, 1f, true);
            kit.Checkpoint("CP_Start", new Vector3(-4.6f, 0f, -4.4f), 45f, new Vector3(0f, 1.2f, 0f), new Vector3(4f, 2.5f, 4f), new Vector3(0f, 0.42f, 0f), true);
            kit.FloorText("ROLL\n<size=60%>W A S D   OR   ARROWS</size>", new Vector3(0f, 0f, 3.6f), 5f, EngraveHdr);
            foreach (var x in new[] { -4.6f, 4.6f })
            {
                kit.Model("Pillar", new Vector3(x, 0f, 4.6f), 0f, kit.deco, 1f, true);
                kit.ClassProp("Assets/Session2/Laterns and candles/Prefabs/candlestick.prefab", new Vector3(x, 0f, -3.2f), 0f, 1f, Amber, 5f, 1.6f);
            }
            kit.Lantern("Lantern_Start", new Vector3(3.6f, 0f, -2.2f), true);

            // corridor lined with candles
            kit.Floor("Corridor_1A", -2.5f, 2.5f, 6f, 22f);
            kit.Wall("Corridor_1A_L", -3.5f, -2.5f, 6f, 22f, 0f, 3f);
            kit.Wall("Corridor_1A_R", 2.5f, 3.5f, 6f, 22f, 0f, 3f);
            for (int i = 0; i < 4; i++)
            {
                float z = 8f + i * 4f;
                foreach (var x in new[] { -2.05f, 2.05f })
                {
                    string candle = i % 3 == 0 ? "Candle1.prefab" : i % 3 == 1 ? "Candle2.prefab" : "Candle3 1.prefab";
                    kit.ClassProp("Assets/Session2/Laterns and candles/Prefabs/" + candle, new Vector3(x, 0f, z), 0f, 1f, Amber, 4.5f, 1.1f);
                }
            }
            kit.Model("Arch", new Vector3(0f, 0f, 6.5f), 0f, kit.deco, 0.85f, true);
            kit.Model("Arch", new Vector3(0f, 0f, 21.5f), 0f, kit.deco, 0.85f, true);

            // landing, first plate, first lantern, chasm and rising bridge
            kit.Floor("Landing_1A", -4.5f, 4.5f, 22f, 28f);
            kit.Wall("Landing_1A_L", -5.5f, -4.5f, 22f, 28f, -8f, 4f);
            kit.Wall("Landing_1A_R", 4.5f, 5.5f, 22f, 28f, -8f, 4f);
            var plate1 = kit.Plate("Plate_1A", new Vector3(0f, 0f, 25f));
            var lantern1 = kit.Lantern("Lantern_1A", new Vector3(3.3f, 0f, 26.6f), false);
            var bridge1 = kit.RisingBridge("Bridge_1A", new Vector3(0f, 0f, 28f), Vector3.forward, 4, 2f, 3f);
            LevelKit.ShowHide(plate1, P, LevelKit.Lit(lantern1), true);
            LevelKit.PlayAnim(plate1, P, bridge1, bridge1.clip.name);
            kit.Checkpoint("CP_1A", new Vector3(-3.9f, 0f, 23f), 0f, new Vector3(0f, 1.2f, 22.8f), new Vector3(9f, 2.5f, 1.4f), new Vector3(0f, 0f, 23.6f));
            kit.Wall("Chasm_L", -5.5f, -4.5f, 28f, 36f, -10f, 4f);
            kit.Wall("Chasm_R", 4.5f, 5.5f, 28f, 36f, -10f, 4f);
            kit.Plane("Chasm_Mist", new Vector3(0f, -2.6f, 32f), new Vector2(9f, 8f), M("M_Mist")).AddComponent<ScrollingUVs>().uvAnimationRate = new Vector2(0.012f, 0.006f);
            kit.PointLight(new Vector3(0f, -4f, 32f), Moon, 1.4f, 9f);
            kit.KillZone("Kill_Chasm1A", new Vector3(0f, -9f, 32f), new Vector3(30f, 4f, 10f));
            kit.Beat("1A", "Awakening", new Vector3(0f, 0f, 21f), new Vector3(7f, 6f, -9f), new Vector3(0f, 0f, 6f), "Plate -> lantern lights, bridge rises");

            // =========================================================== 1B  THE LEDGE
            kit.Floor("Room_1B", -8f, 3.5f, 36f, 62f);
            kit.Wall("Room_1B_L", -9f, -8f, 36f, 62f, 0f, 5f);
            kit.Wall("Room_1B_R", 3.5f, 4.5f, 36f, 62f, 0f, 5f);
            kit.Block("Ledge_1B", new Vector3(-6.25f, 0.5f, 44f), new Vector3(3.5f, 1f, 6f), trim);
            kit.FloorText("JUMP\n<size=60%>SPACE</size>", new Vector3(-1.6f, 0f, 41.2f), 4.4f, EngraveHdr);
            var plate2 = kit.Plate("Plate_1B", new Vector3(-6.25f, 1f, 44f));
            var (gate1, gate1Anim) = kit.Gate("Gate_1B", new Vector3(0f, 0f, 54f));
            kit.Wall("Gate_1B_WallL", -8f, -2.25f, 53.6f, 54.4f, 0f, 5f);
            kit.Wall("Gate_1B_WallR", 2.25f, 3.5f, 53.6f, 54.4f, 0f, 5f);
            kit.Wall("Gate_1B_Lintel", -2.25f, 2.25f, 53.6f, 54.4f, 4.4f, 5f);
            var lantern2 = kit.Lantern("Lantern_1B", new Vector3(2.9f, 0f, 52.3f), false);
            LevelKit.PlayAnim(plate2, P, gate1Anim, "GateOpen");
            LevelKit.ShowHide(plate2, P, LevelKit.Lit(lantern2), true);
            kit.Beam("Moonbeam_1B", new Vector3(-6.25f, 16f, 44f), new Vector3(-6.25f, 0.9f, 44f), 1.0f, "M_Beam_Moon", null, new Vector2(0f, 0.05f));
            kit.SpotLight(new Vector3(-6.25f, 13f, 44f), new Vector3(-6.25f, 0f, 44f), Moon, 9f, 18f, 34f, true);
            kit.Fx("Dust_1B", new Vector3(-6.25f, 3f, 44f), FxKind.Dust);
            // optional shard: two jumps up a broken stair
            kit.Block("ShardStep_1B", new Vector3(2.6f, 0.5f, 42f), new Vector3(1.6f, 1f, 1.6f), trim);
            kit.Block("ShardPillar_1B", new Vector3(2.6f, 1f, 44.8f), new Vector3(1.6f, 2f, 1.6f), trim);
            kit.Shard("S1_A", new Vector3(2.6f, 2.9f, 44.8f));
            kit.Checkpoint("CP_1B", new Vector3(2.9f, 0f, 57f), 0f, new Vector3(-2.25f, 1.2f, 57f), new Vector3(11.5f, 2.5f, 1.6f), new Vector3(0f, 0f, 57.8f));
            foreach (var z in new[] { 38.5f, 50f, 60f })
            {
                kit.Model("Pillar", new Vector3(-7.4f, 0f, z), 0f, kit.deco, 1.1f, true);
                kit.Model("Banner", new Vector3(-7.85f, 0.6f, z + 2.5f), 90f, kit.deco);
            }
            kit.Model("Sarcophagus", new Vector3(-6.2f, 0f, 58.6f), 90f, kit.deco, 1f, true);
            kit.Beat("1B", "The Ledge", new Vector3(-3f, 0f, 46f), new Vector3(8f, 7f, -9f), new Vector3(-2f, 0.5f, 2f), "Jump onto the moonlit ledge; plate raises the gate");

            // =========================================================== 1C  HALL OF WARDENS
            kit.Floor("Hall_Entry", -8f, 8f, 62f, 67f);
            kit.Floor("Causeway", -1.25f, 1.25f, 67f, 92f, 0f, trim);
            kit.Floor("Island_1", -11f, -6f, 70f, 75f);
            kit.Floor("Beam_1", -6f, -1.25f, 71.75f, 73.25f, 0f, trim, 0.6f);
            kit.Floor("Island_2", 8f, 13f, 77f, 82f);
            kit.Floor("Island_3", -11f, -6f, 84f, 89f);
            var beam3 = kit.Floor("Beam_3", -6f, -1.25f, 85.75f, 87.25f, 0f, trim, 0.6f);
            beam3.transform.SetParent(kit.puzzle, true);
            GameObjectUtilitySetDynamic(beam3);
            beam3.AddComponent<PopIn>().fromScale = new Vector3(0.02f, 1f, 1f);
            beam3.SetActive(false);
            // moving slab to island 2 (KinematicMover so the ember is carried)
            var slab = kit.Block("Slab_2", new Vector3(2.75f, -0.3f, 79.5f), new Vector3(3f, 0.6f, 3f), trim, kit.puzzle, 0.06f, true, false);
            slab.AddComponent<Rigidbody>().isKinematic = true;
            var mover = slab.AddComponent<KinematicMover>();
            mover.travel = new Vector3(3.75f, 0f, 0f);
            mover.speed = 1.2f;
            mover.pause = 1.6f;
            kit.Floor("Exit_Platform_1C", -8f, 8f, 92f, 100f);
            kit.Wall("Hall_L", -15f, -14f, 62f, 100f, -12f, 6f);
            kit.Wall("Hall_R", 14f, 15f, 62f, 100f, -12f, 6f);
            kit.Wall("Door_WallL", -8f, -1.6f, 93f, 96.5f, 0f, 5f);
            kit.Wall("Door_WallR", 1.6f, 8f, 93f, 96.5f, 0f, 5f);
            kit.Wall("Door_Lintel", -1.6f, 1.6f, 93f, 96.5f, 3.4f, 5f, trim);
            kit.Floor("Shard_Ledge_1C", 2.45f, 4.5f, 87f, 89f, 0f, trim, 0.6f);
            kit.Shard("S1_B", new Vector3(3.5f, 0.9f, 88f));

            var plateA = kit.Plate("Plate_1C_1", new Vector3(-8.5f, 0f, 72.5f));
            var plateB = kit.Plate("Plate_1C_2", new Vector3(10.5f, 0f, 79.5f));
            var plateC = kit.Plate("Plate_1C_3", new Vector3(-8.5f, 0f, 86.5f));
            var plates = new[] { plateA, plateB, plateC };
            var statuePos = new[] { new Vector3(-10.3f, 0f, 74.2f), new Vector3(12.3f, 0f, 81.3f), new Vector3(-10.3f, 0f, 88.2f) };
            var statueYaw = new[] { 90f, -90f, 90f };
            var brazierPos = new[] { new Vector3(-10.3f, 0f, 70.8f), new Vector3(12.3f, 0f, 77.8f), new Vector3(-10.3f, 0f, 84.8f) };
            var doorLanternPos = new[] { new Vector3(-4.6f, 0f, 92.6f), new Vector3(4.6f, 0f, 92.6f), new Vector3(0f, 5f, 94.75f) };
            var seals = new GameObject[3];
            var braziers = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                seals[i] = kit.Prefab("Seal", new Vector3(0f, 0f, 93.6f + i * 1.2f), 180f, kit.puzzle, "Seal_" + (i + 1));
                braziers[i] = kit.Lantern("Brazier_1C_" + (i + 1), brazierPos[i], false, "Brazier");
                var statue = kit.Prefab("WardenStatue", statuePos[i], statueYaw[i], kit.puzzle, "Warden_" + (i + 1));
                var head = statue.transform.Find("Model/Head");
                if (head != null) head.GetComponent<LookAtTarget>().target = refs.player.transform;
                var doorLantern = kit.Lantern("DoorLantern_" + (i + 1), doorLanternPos[i], false);
                var burst = kit.Fx("SealBreak_" + (i + 1), new Vector3(0f, 1.6f, 93.6f + i * 1.2f), FxKind.Burst, 1.4f);
                burst.gameObject.SetActive(false);

                var plate = plates[i];
                LevelKit.ShowHide(plate, P, seals[i], false);
                LevelKit.ShowHide(plate, P, LevelKit.Lit(braziers[i]), true);
                LevelKit.ShowHide(plate, P, LevelKit.Lit(doorLantern), true);
                LevelKit.ShowHide(plate, P, burst.gameObject, true);
                if (head != null)
                {
                    LevelKit.ShowHide(plate, P, head.Find("EyesRed").gameObject, false);
                    LevelKit.ShowHide(plate, P, head.Find("EyesAmber").gameObject, true);
                }
            }
            LevelKit.ShowHide(plateB, P, beam3, true);
            kit.Plane("Hall_Mist", new Vector3(0f, -3f, 79.5f), new Vector2(28f, 26f), M("M_Mist")).AddComponent<ScrollingUVs>().uvAnimationRate = new Vector2(-0.01f, 0.006f);
            kit.PointLight(new Vector3(0f, -5f, 79.5f), Moon, 2f, 18f);
            kit.KillZone("Kill_Hall", new Vector3(0f, -10f, 79.5f), new Vector3(40f, 4f, 30f));
            kit.Checkpoint("CP_1C", new Vector3(-6.6f, 0f, 63f), 0f, new Vector3(0f, 1.2f, 64.6f), new Vector3(16f, 2.5f, 1.6f), new Vector3(0f, 0f, 65f));
            kit.CameraZone("Cam_Hall", new Vector3(0f, 2f, 64.6f), new Vector3(16f, 4f, 1.6f), new Vector3(0f, 10.5f, -13.5f), 52f);
            kit.CameraZone("Cam_HallExit", new Vector3(0f, 2f, 98.5f), new Vector3(16f, 4f, 1.6f), new Vector3(0f, 7.5f, -11f), 50f);
            for (int i = 0; i < 4; i++)
            {
                kit.Model("Pillar", new Vector3(-13.3f, 0f, 64f + i * 10f), 0f, kit.deco, 1.3f, false);
                kit.Model("Pillar", new Vector3(13.3f, 0f, 64f + i * 10f), 0f, kit.deco, 1.3f, false);
            }
            kit.Fx("Dust_Hall", new Vector3(0f, 2f, 80f), FxKind.Dust, 3f);
            kit.Beat("1C", "Hall of Wardens", new Vector3(0f, 0f, 78f), new Vector3(0f, 16f, -14f), new Vector3(0f, 0f, 4f), "Three plates remove three seals; the third bridge appears after the second plate");

            // =========================================================== 1D  MOONWELL
            kit.Floor("Corridor_1D", -2.5f, 2.5f, 96.5f, 104f);
            kit.Wall("Corridor_1D_L", -3.5f, -2.5f, 96.5f, 104f, 0f, 4f);
            kit.Wall("Corridor_1D_R", 2.5f, 3.5f, 96.5f, 104f, 0f, 4f);
            kit.Checkpoint("CP_1D", new Vector3(2.1f, 0f, 99f), 0f, new Vector3(0f, 1.2f, 99.5f), new Vector3(5f, 2.5f, 1.6f), new Vector3(0f, 0f, 100.2f));
            kit.Floor("Moonwell_Path", -2f, 2f, 104f, 112f, 0f, trim);
            kit.Disc("Moonwell_Dais", new Vector3(0f, 0.2f, 116f), 4.5f, 1.2f, trim);
            kit.Floor("Pool_Floor", -12f, 12f, 103f, 127f, -3f);
            kit.Plane("Pool_Water", new Vector3(0f, -0.25f, 115f), new Vector2(24f, 24f), M("M_Water"));
            kit.Plane("Pool_Mist", new Vector3(0f, 0.08f, 115f), new Vector2(24f, 24f), M("M_Mist")).AddComponent<ScrollingUVs>().uvAnimationRate = new Vector2(0.01f, 0.006f);
            kit.KillZone("Kill_Pool", new Vector3(0f, -1.7f, 115f), new Vector3(24f, 1.2f, 24f));
            kit.Wall("Moonwell_L", -13f, -12f, 103f, 128f, -3f, 6f);
            kit.Wall("Moonwell_R", 12f, 13f, 103f, 128f, -3f, 6f);
            kit.Wall("Moonwell_Back", -13f, 13f, 127f, 128f, -3f, 8f);
            kit.Beam("Moonbeam_1D", new Vector3(0f, 24f, 116f), new Vector3(0f, 0.3f, 116f), 1.9f, "M_Beam_Moon", null, new Vector2(0f, 0.04f));
            kit.SpotLight(new Vector3(0f, 18f, 116f), new Vector3(0f, 0f, 116f), Moon, 12f, 26f, 30f, true);
            kit.Fx("Dust_1D", new Vector3(0f, 4f, 116f), FxKind.Dust, 1.4f);
            var portal = kit.Prefab("ExitPortal", new Vector3(0f, 0.2f, 117.5f), 0f, kit.puzzle, "ExitPortal");
            portal.GetComponentInChildren<HitAndLoadScene>().loadSceneName = "Stage2";
            var stones = new[] { new Vector3(4.2f, 0f, 107f), new Vector3(6.5f, 0f, 108.7f), new Vector3(8.7f, 0f, 110.6f), new Vector3(10.4f, 0f, 112.6f) };
            foreach (var s in stones) kit.Block("SteppingStone", s - new Vector3(0f, 0.6f, 0f), new Vector3(1.5f, 1.2f, 1.5f), trim);
            kit.Shard("S1_C", new Vector3(10.4f, 0.9f, 112.6f));
            foreach (var a in new[] { 200f, 235f, 305f, 340f })
            {
                float r = a * Mathf.Deg2Rad;
                kit.Model("PillarBroken", new Vector3(10.5f * Mathf.Cos(r), -0.2f, 116f + 10.5f * Mathf.Sin(r) * 0.95f), a, kit.deco, 1.1f);
            }
            kit.Lantern("Lantern_1D_L", new Vector3(-2.4f, 0f, 110f), true);
            kit.Lantern("Lantern_1D_R", new Vector3(2.4f, 0f, 110f), true);
            kit.CameraZone("Cam_Moonwell", new Vector3(0f, 2f, 105f), new Vector3(5f, 4f, 1.6f), new Vector3(0f, 9.5f, -13f), 50f);
            kit.Beat("1D", "Moonwell", new Vector3(0f, 0.2f, 114f), new Vector3(-9f, 8f, -12f), new Vector3(0f, 1f, 3f), "Calm reward vista; roll into the rune circle");
            StageCommon.Probe(new Vector3(0f, 3f, 115f), new Vector3(26f, 10f, 26f));
            StageCommon.Probe(new Vector3(0f, 3f, 80f), new Vector3(30f, 12f, 40f), 0.8f);

            // =========================================================== solution for AutoPilot
            var s1 = kit.Step("1A plate", plate1.transform, 3.2f);
            LevelKit.Expect(s1, SolutionCheck.Kind.Active, LevelKit.Lit(lantern1));
            LevelKit.Expect(s1, SolutionCheck.Kind.AnimationPlayed, bridge1.gameObject, bridge1.clip.name);
            var s2 = kit.Step("1B plate", plate2.transform, 2.5f);
            LevelKit.Expect(s2, SolutionCheck.Kind.AnimationPlayed, gate1Anim.gameObject, "GateOpen");
            LevelKit.Expect(s2, SolutionCheck.Kind.Active, LevelKit.Lit(lantern2));
            for (int i = 0; i < 3; i++)
            {
                var s = kit.Step("1C plate " + (i + 1), plates[i].transform, 1.5f);
                LevelKit.Expect(s, SolutionCheck.Kind.Inactive, seals[i]);
                LevelKit.Expect(s, SolutionCheck.Kind.Active, LevelKit.Lit(braziers[i]));
                if (i == 1) LevelKit.Expect(s, SolutionCheck.Kind.Active, beam3);
            }
            kit.Step("1D exit portal", portal.transform.Find("InnerTrigger"), 1.5f, true);

            StageCommon.Save(scene, Name);
        }

        static void GameObjectUtilitySetDynamic(GameObject go)
        {
            UnityEditor.GameObjectUtility.SetStaticEditorFlags(go, 0);
        }
    }
}
