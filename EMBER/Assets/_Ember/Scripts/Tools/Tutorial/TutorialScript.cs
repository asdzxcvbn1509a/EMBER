using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ember
{
    /// <summary>
    /// The tutorial video's script: what the ember does in each room and when each caption shows.
    /// Caption texts live in ArtSource/Tutorial/captions.json (ids used here); coordinates are Unity world
    /// positions taken from the stage builders (Editor/BuildStage1-3.cs). Run one part on its own with
    /// -tutorialFrom ID [-tutorialTo ID]: earlier puzzles are then solved off-camera first.
    /// </summary>
    public partial class TutorialDirector
    {
        static readonly string[] Stage1Plates = { "1A plate", "1B plate", "1C plate 1", "1C plate 2", "1C plate 3" };
        static readonly string[] WestLantern = { "3C echo tile 1", "3C echo tile 2", "3C turn the bridge", "3C west lantern" };
        static readonly string[] AllLanterns = { "3C echo tile 1", "3C echo tile 2", "3C turn the bridge", "3C west lantern", "3C release the sun-stone", "3C east lantern", "3C winds lantern" };

        List<Segment> Script() => new List<Segment>
        {
            new Segment { id = "T0",  scene = "Title",  body = Title, budget = 80f },
            new Segment { id = "1A",  scene = "Stage1", start = new Vector3(0f, 1.02f, 0f), body = S1_Roll, budget = 60f },
            new Segment { id = "1A2", scene = "Stage1", start = new Vector3(0f, 0.6f, 20.4f), body = S1_Plate, budget = 40f },
            new Segment { id = "1B",  scene = "Stage1", start = new Vector3(0f, 0.6f, 36.8f), requires = new[] { "1A plate" }, body = S1_Jump, budget = 80f },
            new Segment { id = "1C",  scene = "Stage1", start = new Vector3(0f, 0.6f, 60.6f), requires = new[] { "1A plate", "1B plate" }, body = S1_Hall, budget = 170f },
            new Segment { id = "1D",  scene = "Stage1", start = new Vector3(0f, 0.6f, 97.8f), requires = Stage1Plates, body = S1_Portal, budget = 40f },
            new Segment { id = "2A",  scene = "Stage2", start = new Vector3(-4f, 0.6f, 0f), body = S2_Raft, budget = 90f },
            new Segment { id = "2B",  scene = "Stage2", start = new Vector3(8f, 0.6f, 17f), body = S2_Iron, budget = 70f },
            new Segment { id = "2C",  scene = "Stage2", start = new Vector3(5f, 0.6f, 54.6f), requires = new[] { "2B iron ball plate" }, body = S2_Boulders, budget = 60f },
            new Segment { id = "2D",  scene = "Stage2", start = new Vector3(5f, 5.1f, 92.6f), body = S2_Weight, budget = 80f },
            new Segment { id = "3A",  scene = "Stage3", start = new Vector3(0f, 0.6f, -1f), body = S3_Rings, budget = 60f },
            new Segment { id = "3B",  scene = "Stage3", start = new Vector3(7f, 4.6f, 44.4f), body = S3_Bridge, budget = 80f },
            new Segment { id = "3C",  scene = "Stage3", start = new Vector3(8f, 4.6f, 86.6f), body = S3_West, budget = 90f },
            new Segment { id = "3C2", scene = "Stage3", start = new Vector3(8f, 4.6f, 89.8f), requires = WestLantern, body = S3_Others, budget = 45f },
            new Segment { id = "3D",  scene = "Stage3", start = new Vector3(8f, 4.6f, 98.4f), requires = AllLanterns, body = S3_Beacon, budget = 50f },
            new Segment { id = "END", scene = "Ending", body = Ending, budget = 30f },
        };

        // ================================================================ title screen and menus
        IEnumerator Title()
        {
            Keys(false);
            overlay.ShowCursor(false);
            yield return Wait(0.5f);
            yield return SayHold("t_intro");
            yield return SayHold("t_story");
            overlay.ShowCursorAt(new Vector2(1180f, 520f));
            yield return Wait(0.3f);
            Say("t_menu");
            yield return CursorTo("UI/Menu/NewGame", 0.8f);
            yield return Hold();
            Say("t_select");
            yield return CursorTo("UI/Menu/Select", 0.45f);
            yield return Click(0.5f);
            yield return CursorTo("UI/StageSelect/S1", 0.55f);
            yield return Wait(0.45f);
            yield return CursorTo("UI/StageSelect/S2", 0.3f);
            yield return Wait(0.45f);
            yield return CursorTo("UI/StageSelect/S3", 0.3f);
            yield return Wait(0.45f);
            yield return Hold();
            yield return CursorTo("UI/StageSelect/Back", 0.45f);
            yield return Click(0.4f);
            Say("t_how");
            yield return CursorTo("UI/Menu/How", 0.5f);
            yield return Click(0.4f);
            yield return Hold();
            yield return Wait(0.4f);
            yield return CursorTo("UI/HowToPlay/Back", 0.5f);
            yield return Click(0.3f);
            Unsay();
            yield return CursorTo("UI/Menu/NewGame", 0.6f);
            yield return Wait(0.3f);
            yield return Click(0.1f);
            yield return WaitScene("Stage1", 6f);
            overlay.ShowCursor(false);
        }

        // ================================================================ stage I - the sunken crypt
        IEnumerator S1_Roll()
        {
            yield return Wait(0.8f);
            Keys(true);
            // the start altar is a stepped dais 2.6 m in radius: these moves stay just outside it
            yield return Go(1.8f, V(0f, 3.4f));                      // first roll forward off the altar while the stage title shows
            yield return SceneTime(6.2f);
            Say("s1_roll");
            yield return Go(3.0f, V(-3.2f, 3.6f));                   // A
            yield return Go(3.0f, V(3.2f, 3.6f));                    // D
            yield return Go(2.4f, V(0.3f, 4.2f));
            yield return Go(2.0f, V(0.3f, 3.2f));                    // S: towards the camera
            yield return Go(2.4f, V(0f, 4.8f));                      // W: into the screen
            yield return Hold();
            Say("s1_brake");
            pilot.Manual();
            input.Set(DemoInput.Key.W, true);
            yield return Wait(0.75f);
            input.Set(DemoInput.Key.W, false);                       // let go: it keeps rolling
            yield return Wait(0.9f);
            input.Set(DemoInput.Key.S, true);                        // the opposite key brakes
            yield return Until(() => pilot.Velocity.z < 0.15f, 3f, "brake demo");
            input.Set(DemoInput.Key.S, false);
            yield return Hold();
            Unsay();
            yield return Go(4.5f, V(0f, 20.4f));                     // down the candle corridor
        }

        IEnumerator S1_Plate()
        {
            yield return Go(3.0f, V(0f, 23.0f));                     // through CP_1A: CHECKPOINT
            yield return SayHold("s1_cp");
            Say("s1_plate");
            yield return Go(1.4f, V(0f, 25.0f));                     // onto Plate_1A: lantern, then the bridge rises
            yield return Expect("1A plate", 4f);
            yield return Hold();
            yield return Wait(0.3f);
            Unsay();
            yield return Go(3.0f, V(0f, 27.6f), V(0f, 37.0f));       // across the new bridge into 1B
        }

        IEnumerator S1_Jump()
        {
            Say("s1_jump");
            yield return Go(2.4f, V(2.6f, 37.6f), V(2.6f, 38.2f));   // below the broken stair (ShardStep / ShardPillar)
            yield return Wait(0.3f);
            yield return JumpOnto("ShardStep_1B", () => new Vector3(2.6f, 1f, 42f), new Vector2(0.8f, 0.8f), new Vector2(0.45f, 0.35f), 1f, null,
                new PilotRoute(3.0f, false, V(2.6f, 47f)), 5f);
            yield return JumpOnto("ShardPillar_1B", () => new Vector3(2.6f, 2f, 44.8f), new Vector2(0.8f, 0.8f), new Vector2(0.45f, 0.55f), 2f, null,
                new PilotRoute(3.0f, false, V(2.6f, 49f)), 2.5f, null, 0.12f);
            yield return Settle();                                    // stop on top of the pillar
            yield return Until(() => GameManager.HasShard("S1_A"), 2f, "shard S1_A");
            yield return Hold();
            yield return SayHold("s1_shard");
            Say("s1_ledge");
            yield return Go(2.2f, V(1.2f, 44.8f), V(0.6f, 44.0f));   // drop off the pillar's west side
            yield return Wait(0.3f);
            yield return JumpOnto("Ledge_1B", () => new Vector3(-6.25f, 1f, 44f), new Vector2(1.75f, 3f), new Vector2(0.9f, 1.4f), 1f, null,
                new PilotRoute(3.0f, false, V(-9f, 44f)), 5f, null, 0.1f);
            yield return Go(1.2f, V(-6.25f, 44f));                   // onto Plate_1B: the gate lifts
            yield return Expect("1B plate", 4f);
            yield return Hold();
            Unsay();
            yield return Go(3.0f, V(-4.0f, 45.6f), V(-1.0f, 47.5f), V(0f, 50.5f), V(0f, 55.0f), V(0f, 59.6f));   // off the ledge, through the gate (CHECKPOINT)
        }

        IEnumerator S1_Hall()
        {
            yield return Go(3.0f, V(0f, 62.0f), V(0f, 65.6f));       // CP_1C (CHECKPOINT) and the hall camera
            yield return Wait(0.6f);

            // ---- pause menu
            Say("s1_pause");
            input.Tap(DemoInput.Key.Esc);
            GameManager.Instance.SetPaused(true);
            yield return Wait(0.5f);
            overlay.ShowCursorAt(new Vector2(1240f, 300f));
            yield return CursorTo("UI/PausePanel/Resume", 0.55f);
            yield return Hold();
            Say("s1_pause2");
            yield return CursorTo("UI/PausePanel/Restart", 0.3f);
            yield return Wait(0.55f);
            yield return CursorTo("UI/PausePanel/Menu", 0.3f);
            yield return Wait(0.55f);
            yield return CursorTo("UI/PausePanel/Quit", 0.3f);
            yield return Wait(0.55f);
            yield return Hold();
            yield return CursorTo("UI/PausePanel/Resume", 0.5f);
            yield return Click(0.2f);
            overlay.ShowCursor(false);
            Unsay();
            yield return Wait(0.5f);

            // ---- photo mode (the script flies the camera instead of the mouse)
            Say("s1_photo");
            input.Tap(DemoInput.Key.F);
            var photo = FindAnyObjectByType<PhotoMode>();
            photo.Toggle();
            var fly = photo.GetComponent<Fly>();
            if (fly != null) fly.enabled = false;
            yield return DemoFly(photo.transform);
            yield return Hold();
            input.Tap(DemoInput.Key.F);
            photo.Toggle();
            yield return Wait(1.4f);                                 // CameraFollow glides back
            Unsay();

            // ---- plate 1 across the narrow beam
            Say("s1_order");
            yield return Go(2.4f, V(0f, 70.6f), V(0f, 72.5f));
            yield return Careful(2.0f, V(-1.6f, 72.5f), V(-6.6f, 72.5f), V(-8.5f, 72.5f));   // Beam_1 (1.5 m wide) to Plate_1C_1
            yield return Expect("1C plate 1", 4f);
            yield return Hold();

            // ---- falling is fine
            Say("s1_fall");
            yield return FallFrom(2.2f, V(-8.5f, 71.2f), V(-8.5f, 66f));   // off island 1: back at CP_1C
            yield return Hold();

            // ---- plate 2 by the moving slab
            Say("s1_slab");
            yield return Go(3.2f, V(0f, 76f), V(0.5f, 79.5f));
            var mover = Find<KinematicMover>("Slab_2");
            var slab = mover.GetComponent<Rigidbody>();
            yield return Until(() => mover.Progress < 0.04f && (mover.Paused ? mover.PauseLeft > 0.7f : mover.Direction < 0), 16f, "slab at the causeway");
            yield return Ride(slab, Vector3.zero, () => mover.Paused && mover.Progress > 0.99f, 16f, "slab crossing");
            yield return Go(1.8f, V(9.6f, 79.5f), V(10.5f, 79.5f));  // onto island 2 and Plate_1C_2
            yield return Expect("1C plate 2", 4f);
            yield return SayHold("s1_beam3");

            // ---- R: back to the checkpoint from the island, then the third plate over the new beam
            Say("s1_r");
            yield return Wait(0.3f);
            yield return PressR();
            yield return Hold();
            Unsay();
            yield return Dip(() => TeleportTo(new Vector3(-9.2f, 0.6f, 84.8f)), "1C to island 3");
            yield return Go(1.4f, V(-8.5f, 86.5f));                  // Plate_1C_3
            yield return Expect("1C plate 3", 4f);
            Say("s1_door");
            yield return Careful(2.0f, V(-6.6f, 86.5f), V(-1.4f, 86.5f), V(0f, 87.6f));   // back over Beam_3
            yield return Go(3.4f, V(0f, 92.5f), V(0f, 97.6f));       // through the unsealed door
            Unsay();
        }

        IEnumerator DemoFly(Transform rig)
        {
            // Fly.cs: W A S D move 10 m/s in the camera's own frame, the mouse turns it
            input.Set(DemoInput.Key.W, true);
            for (float t = 0f; t < 0.45f; t += GameClock.UnscaledDeltaTime)
            {
                rig.Translate(Vector3.forward * 10f * GameClock.UnscaledDeltaTime, Space.Self);
                yield return null;
            }
            input.Set(DemoInput.Key.W, false);
            yield return Wait(0.2f);
            for (float t = 0f; t < 1.6f; t += GameClock.UnscaledDeltaTime)
            {
                float k = Mathf.Sin(t / 1.6f * Mathf.PI), dt = GameClock.UnscaledDeltaTime;
                var e = rig.eulerAngles;
                rig.eulerAngles = new Vector3(e.x - 6f * k * dt, e.y + 26f * k * dt, 0f);
                yield return null;
            }
            input.Set(DemoInput.Key.D, true);
            for (float t = 0f; t < 0.35f; t += GameClock.UnscaledDeltaTime)
            {
                rig.Translate(Vector3.right * 10f * GameClock.UnscaledDeltaTime, Space.Self);
                yield return null;
            }
            input.Set(DemoInput.Key.D, false);
        }

        IEnumerator S1_Portal()
        {
            yield return Go(3.0f, V(0f, 100.5f), V(0f, 106.4f));     // CP_1D (CHECKPOINT), the Moonwell camera
            Say("s1_portal");
            yield return Hold();
            Unsay();
            yield return Pass(2.2f, V(0f, 111f), V(0f, 119.5f));     // the outer ring fades the screen, the centre loads stage II
            yield return WaitScene("Stage2", 8f);
        }

        // ================================================================ stage II - the ember forge
        IEnumerator S2_Raft()
        {
            yield return Wait(0.8f);
            Keys(true);
            yield return Go(2.0f, V(-6.0f, 2.6f));                   // towards the plate while the stage title shows
            yield return SceneTime(6.2f);
            yield return SayHold("s2_intro");
            Say("s2_raft");
            yield return Go(1.8f, V(-8f, 6f));                       // Plate_2A: a raft now, then one every 5 s
            yield return Expect("2A raft plate", 3f);
            yield return Go(2.2f, V(-5.5f, 7.6f), V(-4f, 9.15f));    // wait at the edge of the dock
            yield return Hold();

            Rigidbody raft = null;
            for (int attempt = 0; attempt < 3 && raft == null; attempt++)
            {
                Transform next = null;
                yield return Until(() => (next = NextRaft(-4f)) != null && next.position.x > -7.0f, 14f, "raft approaching");
                var body = next.GetComponent<Rigidbody>();
                string error = null;
                yield return Guarded(JumpOnto("raft", () => body.position, new Vector2(1.5f, 1.5f), new Vector2(0.75f, 0.8f), 0.22f,
                    () => new Vector3(1.5f, 0f, 0f), new PilotRoute(2.4f, false, V(-4f, 13f)), 2.5f, () => pilot.Position.z > 9.85f), e => error = Describe(e));
                if (error == null) raft = body;
                else
                {
                    tl.Warn("raft attempt " + (attempt + 1) + ": " + error);
                    pilot.Stop();
                    yield return Wait(0.6f);
                    if (pilot.Position.z > 9.2f || pilot.Position.y < 0f) yield return Go(1.6f, V(-4f, 9.0f));
                }
            }
            if (raft == null) throw new TutorialFail("could not board a raft");
            Say("s2_raft2");
            yield return Ride(raft, Vector3.zero, () => raft.position.x > 6.6f, 16f, "raft to the landing");
            float rx = raft.position.x;
            yield return JumpOnto("landing", () => new Vector3(8f, 0f, 17.85f), new Vector2(4f, 4.15f), new Vector2(2.6f, 3.55f), 0f, null,
                new PilotRoute(2.2f, false, V(rx + 2.5f, 19f)), 2.5f, () => pilot.Position.z > 13.0f, 0.1f);
            yield return Hold();
            Unsay();
            yield return Go(2.4f, V(8f, 16.4f));                     // CP_2A (CHECKPOINT)
        }

        static Transform NextRaft(float boardX)
        {
            Transform best = null;
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name == "LavaRaft(Clone)" && t.position.x < boardX + 0.6f && (best == null || t.position.x > best.position.x)) best = t;
            return best;
        }

        IEnumerator S2_Iron()
        {
            yield return Go(4.2f, V(8f, 21f), V(8f, 31.4f), V(8f, 37f), V(8f, 42.8f));          // corridor, CP_2B, under the balcony
            Say("s2_iron");
            yield return Go(3.6f, V(3.4f, 43f), V(2f, 47.2f), V(-2.6f, 49.4f), V(-4f, 48.4f));  // under the chute, round the anvil, foot of the ramp
            yield return Go(3.8f, V(-4f, 44f), V(-4f, 35f), V(-3.6f, 32.8f));                    // up Ramp_Balcony onto the balcony
            yield return Go(1.4f, V(0f, 32f));                                                    // Plate_2B releases the iron ball
            yield return Until(() => AnyNamed("IronBall(Clone)"), 3f, "iron ball released");
            yield return Hold();
            yield return Dip(() => TeleportTo(new Vector3(7.2f, 0.6f, 45.2f)), "2B watch the cracked wall");
            yield return Expect("2B iron ball plate", 9f);
            yield return Wait(1.2f);
            Unsay();
            yield return Go(2.6f, V(6.4f, 50f), V(5f, 53.5f), V(5f, 56.0f));                    // through the broken wall into 2C
        }

        IEnumerator S2_Boulders()
        {
            yield return Go(2.6f, V(5f, 57.2f), V(9.2f, 57.6f));                                 // CP_2C_Bottom (CHECKPOINT); line up with the bridge
            Say("s2_boulder");
            yield return Careful(1.2f, V(9.2f, 59.4f), V(8.7f, 60.15f), V(8.35f, 60.65f), V(7.6f, 61.3f), V(6.4f, 62.6f), V(5f, 64f));   // bridge, round the deflector
            yield return Hold();
            Say("s2_boulder2");
            yield return Go(3.0f, V(5f, 75f));
            yield return Hold();
            Unsay();
            yield return Go(3.0f, V(5f, 88f), V(5f, 91.6f));                                     // the top: CP_2C_Top, the boulders stop
        }

        IEnumerator S2_Weight()
        {
            yield return Go(3.0f, V(3f, 95f), V(3f, 100.8f), V(-1.2f, 102.5f));                 // CP_2D (CHECKPOINT), foot of the walkway ramp
            Say("s2_weight");
            yield return Go(2.6f, V(-4f, 102.5f), V(-8.7f, 102.5f), V(-10f, 103.8f), V(-10f, 106f));   // up to Plate_2D: the counterweight drops
            yield return Until(() => AnyNamed("Counterweight(Clone)"), 3f, "counterweight released");
            yield return Go(1.6f, V(-10f, 109.4f));                                              // step off the plate (once is enough)
            yield return Expect("2D counterweight plate", 14f);                                  // on the scale: gate open, lamps lit
            yield return Hold();
            yield return SayHold("s2_weight2");
            Unsay();
            yield return Dip(() => TeleportTo(new Vector3(0f, 5.1f, 124.8f)), "2D to the crucible gate");
            Say("s2_exit");
            yield return Go(2.4f, V(0f, 128.5f));
            yield return Hold();
            Unsay();
            yield return Pass(2.2f, V(0f, 133f), V(0f, 140f));                                   // the crucible portal loads stage III
            yield return WaitScene("Stage3", 8f);
        }

        // ================================================================ stage III - the sky sanctum
        IEnumerator S3_Rings()
        {
            yield return Wait(0.8f);
            Keys(true);
            yield return Go(1.4f, V(0f, 1.2f));                      // in front of the gold ring while the stage title shows
            yield return SceneTime(6.2f);
            yield return SayHold("s3_rings");
            int w0 = pilot.Warps;
            yield return Pass(2.8f, V(0f, 3f), V(0f, 9f));            // through Ring_Gold -> Pad_B
            yield return Until(() => pilot.Warps > w0, 3f, "gold ring warp");
            Say("s3_rings2");
            yield return Go(2.8f, V(-10.5f, 24.4f));                 // still moving after the warp: brake (CHECKPOINT)
            yield return Hold();
            Unsay();
            yield return Go(2.0f, V(-7.2f, 25.3f));                  // in front of the cyan ring
            int w1 = pilot.Warps;
            yield return Pass(2.4f, V(-7.2f, 26.2f), V(-7.2f, 31f));  // through Ring_Cyan -> Pad_C
            yield return Until(() => pilot.Warps > w1, 3f, "cyan ring warp");
            yield return Go(2.4f, V(7f, 40.2f));                     // brake on island C (CHECKPOINT)
        }

        IEnumerator S3_Bridge()
        {
            yield return Go(2.6f, V(7f, 45f), V(7f, 50.2f));         // Path_3B (CHECKPOINT), stop before the turning bridge
            Say("s3_bridge");
            var rot = Find<KinematicRotator>("RotatingBridge");
            yield return Until(() => rot.Resting && rot.RestLeft > 4.4f, 18f, "bridge aligned");
            yield return Go(4.2f, V(7f, 52f), V(7f, 62.6f), V(7f, 65.6f));   // across (shard S3_B), CP_3B2
            yield return Hold();
            Unsay();
            var mover = Find<KinematicMover>("Slab_3B");
            var slab = mover.GetComponent<Rigidbody>();
            yield return Go(2.0f, V(7f, 66.4f));
            yield return Until(() => mover.Progress < 0.04f && (mover.Paused ? mover.PauseLeft > 0.7f : mover.Direction < 0), 16f, "slab 3B at the platform");
            yield return Ride(slab, Vector3.zero, () => mover.Paused && mover.Progress > 0.99f, 16f, "slab 3B crossing");
            yield return Go(3.0f, V(7f, 77f), V(8f, 81.4f), V(8f, 86.6f));   // to the hub (CHECKPOINT, hub camera)
        }

        IEnumerator S3_West()
        {
            yield return SayHold("s3_hub");
            Say("s3_tiles");
            yield return Go(3.4f, V(3f, 91.4f), V(-1.2f, 92f));      // west across the hub to the first echo tile
            yield return Go(2.0f, V(-3.0f, 92f));                    // tile 1: tile 2 appears
            yield return Expect("3C echo tile 1", 3f);
            yield return Go(2.0f, V(-5.0f, 92f));                    // tile 2: tile 3 appears
            yield return Expect("3C echo tile 2", 3f);
            yield return Go(2.2f, V(-7.4f, 92f), V(-9.6f, 93.2f));   // tile 3 and the west island
            yield return Go(1.4f, V(-9.6f, 94.6f));                  // Plate_West_Bridge lays the drawbridge down
            yield return Expect("3C turn the bridge", 3f);
            yield return Wait(0.6f);
            yield return Hold();
            yield return Careful(2.0f, V(-11.2f, 95.2f), V(-12f, 96.6f), V(-12f, 100.8f), V(-13.2f, 102.6f));   // over the drawbridge to Plate_West
            yield return Expect("3C west lantern", 4f);
            Say("s3_west");
            yield return Wait(1.0f);
            yield return Hold();
            Unsay();
            yield return Go(1.8f, V(-10.4f, 102.0f));                // in front of the return ring
            int w0 = pilot.Warps;
            yield return Pass(2.2f, V(-10.4f, 102.8f), V(-10.4f, 107f));   // Ring_West_Return -> the hub
            yield return Until(() => pilot.Warps > w0, 3f, "west return ring");
            yield return Go(2.2f, V(8f, 89.8f));                     // brake on the hub
        }

        IEnumerator S3_Others()
        {
            yield return Dip(OthersInBlack, "3C east and winds lanterns", 4f, "s3_others");
        }

        IEnumerator OthersInBlack()
        {
            yield return Solve("3C release the sun-stone");
            yield return Until(() => Check("3C release the sun-stone", false), 12f, "crystal seal broken");
            yield return Solve("3C east lantern");
            yield return Solve("3C winds lantern");
            yield return TeleportTo(new Vector3(8f, 4.6f, 98.4f), 0.6f);
        }

        IEnumerator S3_Beacon()
        {
            Say("s3_beacon");
            yield return Wait(0.6f);
            yield return Go(2.6f, V(8f, 101.6f), V(8f, 114.4f), V(8f, 115.8f));   // up the three flights to the terrace (CHECKPOINT)
            yield return Hold();
            Unsay();
            Keys(false);
            yield return Pass(1.6f, V(8f, 117.6f));                  // into the altar's fire: the beacon lights, dawn breaks
            pilot.Stop();
            yield return WaitScene("Ending", 18f);
        }

        // ================================================================ ending
        IEnumerator Ending()
        {
            Keys(false);
            yield return Wait(1.6f);
            yield return SayHold("end_stats");
            yield return SayHold("end_bye");
            Unsay();
            var gm = GameManager.Instance;
            if (gm != null && gm.fader != null) { gm.fader.SetColor(Color.black); gm.fader.FadeOut(1.6f); }
            yield return Wait(2.0f);
        }
    }
}
